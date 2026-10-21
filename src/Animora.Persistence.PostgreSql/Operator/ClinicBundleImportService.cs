using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Animora.Application.Text;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Purchasing;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Scheduling;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>Result of validating or ingesting a format-v2 clinic import bundle (<c>clinic-import.animora.json</c>).</summary>
public sealed record ClinicImportSummary(
    string TenantId,
    int FormatVersion,
    string SourceFingerprint,
    int Owners,
    int Pets,
    int Visits,
    int Vaccinations,
    int Antiparasites,
    int LabResults,
    int Appointments,
    int Invoices,
    int StockReceipts,
    int Products,
    int Services,
    int Categories,
    int Insurers,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Server-side validator + tenant-isolated ingestion service for the format-v2 legacy <c>Clinic.bak</c> import bundle (T8).
/// <para><b>Isolation &amp; stamping.</b> Every write runs on a dedicated <see cref="TenantBoundContext"/> enlisted in the operator
/// transaction, so <c>TenantStampingInterceptor</c> and the named <c>Tenant</c> query filters apply to every entity.</para>
/// <para><b>Idempotency.</b> Every entity id is derived deterministically via <see cref="StableId"/> from
/// <c>SHA-256(tenantId | table | sourceId)</c> (26-char Crockford ULID), so re-importing the same bundle updates/keeps existing
/// records without duplicating rows or breaking foreign keys.</para>
/// </summary>
public static class ClinicBundleImportService
{
    public const int ExpectedFormatVersion = 2;
    public const long MaxBundleBytes = 32L * 1024 * 1024;
    public const string ImportAuditAction = "tenant.import_bundle";
    public const string ImportFailedAuditAction = "tenant.import_bundle.failed";

    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string DefaultUnitCode = "PCS";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
    };

    private static readonly HashSet<string> AllowedTopKeys = new(StringComparer.Ordinal)
    {
        "formatVersion", "generatedUtc", "converterVersion", "sourceFingerprint",
        "sourceRowCounts", "dedupeRule", "currency", "notes", "warnings",
        "insurers", "paymentTypes", "categories", "species", "vaccineTypes",
        "staff", "services", "products", "owners", "visits", "vaccinations",
        "antiparasites", "labResults", "appointments", "invoices", "stockReceipts",
        "holidays", "archive", "raw",
    };

    /// <summary>
    /// Deterministic 26-char Crockford ULID derived from <c>SHA-256(tenantId | table | sourceId)</c> —
    /// bit-for-bit identical to <c>converter.convert.stable_id</c> in Python.
    /// </summary>
    public static string StableId(string tenantId, string table, string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}|{table}|{sourceId}"), digest);

        UInt128 n = 0;
        for (var i = 0; i < 16; i++)
        {
            n = (n << 8) | digest[i];
        }

        Span<char> chars = stackalloc char[26];
        for (var i = 25; i >= 0; i--)
        {
            chars[i] = Crockford[(int)(n & 31)];
            n >>= 5;
        }

        if (chars[0] > '7')
        {
            chars[0] = '0';
        }

        return new string(chars);
    }

    /// <summary>Parses and validates a bundle JSON payload without touching the database (used for dry-run preview &amp; preflight).</summary>
    public static Result<ClinicImportBundle> ParseAndValidate(ReadOnlySpan<byte> utf8Json, out ClinicImportSummary? preview, string tenantId = "")
    {
        preview = null;
        if (utf8Json.IsEmpty || utf8Json.Length > MaxBundleBytes)
        {
            return Result<ClinicImportBundle>.Fail(ErrorCodes.ValidationFailed, "Bundle is empty or exceeds the 32 MiB limit.");
        }

        ClinicImportBundle? bundle;
        try
        {
            using var doc = JsonDocument.Parse(utf8Json.ToArray());
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result<ClinicImportBundle>.Fail(ErrorCodes.ValidationFailed, "Bundle root must be a JSON object.");
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!AllowedTopKeys.Contains(prop.Name))
                {
                    return Result<ClinicImportBundle>.Fail(ErrorCodes.ValidationFailed, $"Unexpected top-level property '{prop.Name}'.");
                }
            }

            bundle = JsonSerializer.Deserialize<ClinicImportBundle>(utf8Json, JsonOptions);
        }
        catch (JsonException)
        {
            return Result<ClinicImportBundle>.Fail(ErrorCodes.ValidationFailed, "Bundle is not valid format-v2 JSON.");
        }

        if (bundle is null)
        {
            return Result<ClinicImportBundle>.Fail(ErrorCodes.ValidationFailed, "Bundle deserialized to null.");
        }

        var error = ValidateBundle(bundle);
        if (error is not null)
        {
            return Result<ClinicImportBundle>.Fail(ErrorCodes.ValidationFailed, error);
        }

        preview = Summarize(tenantId, bundle);
        return Result<ClinicImportBundle>.Ok(bundle);
    }

    /// <summary>Validates and ingests a format-v2 bundle into <paramref name="tenantId"/> on a dedicated tenant-bound scope.</summary>
    public static async Task<Result<ClinicImportSummary>> ImportAsync(
        AnimoraOperatorDbContext operatorDb,
        string tenantId,
        ReadOnlyMemory<byte> utf8Json,
        ActorRef actor,
        string correlationId,
        IClock clock,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operatorDb);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26)
        {
            return Result<ClinicImportSummary>.Fail(ErrorCodes.ValidationFailed, "Invalid tenant id.");
        }

        var parsed = ParseAndValidate(utf8Json.Span, out _, tenantId);
        if (parsed.IsFailure)
        {
            return Result<ClinicImportSummary>.Fail(parsed.Error, parsed.Detail);
        }

        var bundle = parsed.Value!;

        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false))
        {
            return Result<ClinicImportSummary>.Fail(ErrorCodes.ConcurrencyConflict, "Another clinic import is currently in progress.");
        }

        try
        {
            await using IDbContextTransaction tx = await operatorDb.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (!await operatorDb.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && !t.Deleted, ct).ConfigureAwait(false))
            {
                return Result<ClinicImportSummary>.Fail(ErrorCodes.NotFound, "Target tenant does not exist.");
            }

            await using var tenant = TenantBoundContext.Create(operatorDb, tenantId, clock);
            await tenant.Database.UseTransactionAsync(tx.GetDbTransaction(), ct).ConfigureAwait(false);

            var applied = await ApplyBundleToTenantContextAsync(tenant, tenantId, bundle, clock, ct).ConfigureAwait(false);
            if (applied.IsFailure)
            {
                return Result<ClinicImportSummary>.Fail(applied.Error, applied.Detail);
            }

            var now = clock.UtcNow;
            var corr = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : (correlationId.Length > 64 ? correlationId[..64] : correlationId);
            var summary = Summarize(tenantId, bundle);

            tenant.SecurityAudit.Add(SecurityAuditEntry.Create(
                IdGenerator.NewId(now),
                tenantId,
                now,
                "operator",
                ImportAuditAction,
                "success",
                corr,
                detailJson: SecurityAuditDetail.Create(
                    ("owners", summary.Owners.ToString(CultureInfo.InvariantCulture)),
                    ("pets", summary.Pets.ToString(CultureInfo.InvariantCulture)))));

            operatorDb.Audit.Add(OperatorAudit.Create(
                now,
                actor,
                ImportAuditAction,
                tenantId,
                corr,
                "success",
                new
                {
                    formatVersion = summary.FormatVersion,
                    owners = summary.Owners,
                    pets = summary.Pets,
                    visits = summary.Visits,
                    vaccinations = summary.Vaccinations,
                    antiparasites = summary.Antiparasites,
                    invoices = summary.Invoices,
                    products = summary.Products,
                    services = summary.Services,
                }));

            await tenant.SaveChangesAsync(ct).ConfigureAwait(false);
            await operatorDb.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return Result<ClinicImportSummary>.Ok(summary);
        }
        catch (Npgsql.NpgsqlException)
        {
            return Result<ClinicImportSummary>.Fail(ErrorCodes.StoreUnavailable);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Materializes a validated <see cref="ClinicImportBundle"/> into an already tenant-bound <see cref="AnimoraServerDbContext"/>.
    /// Internal/public entry point so targeted verification tests can exercise the exact domain materialization logic.
    /// </summary>
    public static async Task<Result<ClinicImportSummary>> ApplyBundleToTenantContextAsync(
        AnimoraServerDbContext tenant,
        string tenantId,
        ClinicImportBundle bundle,
        IClock clock,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(clock);

        if (!string.Equals(tenant.CurrentTenantId, tenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("TenantBoundContext must be bound to the target tenantId.");
        }

        var validationError = ValidateBundle(bundle);
        if (validationError is not null)
        {
            return Result<ClinicImportSummary>.Fail(ErrorCodes.ValidationFailed, validationError);
        }

        var now = clock.UtcNow;

        // Resolve default actor user in the clinic (Manager owner or first user; fallback to deterministic import user id).
        var defaultUserId = await tenant.StaffMemberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && !m.Deleted && m.Role == StaffRole.Manager)
            .OrderBy(m => m.JoinedUtc)
            .Select(m => m.UserId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? await tenant.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && !u.Deleted)
                .OrderBy(u => u.Id)
                .Select(u => u.Id)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? StableId(tenantId, "system", "legacy-import-operator");

        // 1. Ensure default UnitOfMeasure ("PCS") exists.
        var existingUnits = await tenant.UnitOfMeasureSet.ToDictionaryAsync(u => u.Code, StringComparer.Ordinal, ct).ConfigureAwait(false);
        if (!existingUnits.ContainsKey(DefaultUnitCode))
        {
            var uom = UnitOfMeasure.Create(tenantId, DefaultUnitCode, "unit.pcs", 0);
            if (uom.IsSuccess)
            {
                tenant.UnitOfMeasureSet.Add(uom.Value!);
            }
        }

        // 2. Insurers (tblInsurances -> Insurer).
        var existingInsurers = await tenant.InsurerSet.ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var insurerIdsBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in bundle.Insurers)
        {
            var id = StableId(tenantId, "tblInsurances", item.SourceId);
            insurerIdsBySource[item.SourceId] = id;
            var code = "INS-" + IdSuffix(id, 8);
            if (existingInsurers.TryGetValue(id, out var existing))
            {
                existing.Update(Truncate(item.Name, 128), existing.CoveragePercent, existing.CeilingMinor);
            }
            else
            {
                var created = Insurer.Create(tenantId, code, Truncate(item.Name, 128), 0m, 0L, id);
                tenant.InsurerSet.Add(created);
                existingInsurers[id] = created;
            }
        }

        // 3. Product Categories (tblStuffGroup + default categories for uncategorized goods & services).
        var existingCategories = await tenant.ProductCategorySet.ToDictionaryAsync(c => c.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var categoryIdsBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        var sortOrder = existingCategories.Count;
        foreach (var cat in bundle.Categories)
        {
            var id = StableId(tenantId, "tblStuffGroup", cat.SourceId);
            categoryIdsBySource[cat.SourceId] = id;
            var code = "CAT-" + IdSuffix(id, 8);
            if (existingCategories.TryGetValue(id, out var existing))
            {
                existing.Rename(Truncate(cat.Name, 64), existing.SortOrder);
            }
            else
            {
                var created = ProductCategory.Create(tenantId, code, Truncate(cat.Name, 64), sortOrder: ++sortOrder, id: id);
                if (created.IsSuccess)
                {
                    tenant.ProductCategorySet.Add(created.Value!);
                    existingCategories[id] = created.Value!;
                }
            }
        }

        var defaultGoodsCategoryId = StableId(tenantId, "tblStuffGroup", "__default_goods__");
        if (!existingCategories.ContainsKey(defaultGoodsCategoryId))
        {
            var created = ProductCategory.Create(
                tenantId,
                "CAT-" + IdSuffix(defaultGoodsCategoryId, 8),
                "کالاهای واردشده",
                sortOrder: ++sortOrder,
                id: defaultGoodsCategoryId);
            if (created.IsSuccess)
            {
                tenant.ProductCategorySet.Add(created.Value!);
                existingCategories[defaultGoodsCategoryId] = created.Value!;
            }
        }

        var defaultServiceCategoryId = StableId(tenantId, "tblStuffGroup", "__default_services__");
        if (!existingCategories.ContainsKey(defaultServiceCategoryId))
        {
            var created = ProductCategory.Create(
                tenantId,
                "CAT-" + IdSuffix(defaultServiceCategoryId, 8),
                "خدمات کلینیک",
                sortOrder: ++sortOrder,
                id: defaultServiceCategoryId);
            if (created.IsSuccess)
            {
                tenant.ProductCategorySet.Add(created.Value!);
                existingCategories[defaultServiceCategoryId] = created.Value!;
            }
        }

        // 4. Vaccine Catalog (tblVaccinationType -> VaccineCatalogItem).
        var existingVaccineCatalog = await tenant.VaccineCatalogItemSet.ToDictionaryAsync(v => v.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var usedVaccineCodes = new HashSet<string>(
            existingVaccineCatalog.Values.Select(v => v.Code),
            StringComparer.OrdinalIgnoreCase);
        var vaccineCodeBySource = new Dictionary<string, (string Code, string Name)>(StringComparer.Ordinal);
        foreach (var vt in bundle.VaccineTypes)
        {
            var id = StableId(tenantId, "tblVaccinationType", vt.SourceId);
            var code = "VAC-" + IdSuffix(id, 8);
            var name = Truncate(vt.Name, VaccineCatalogItem.MaxNameLength);
            vaccineCodeBySource[vt.SourceId] = (code, name);
            if (!existingVaccineCatalog.ContainsKey(id) && !usedVaccineCodes.Contains(code) && !VaccineSchedule.All.ContainsKey(code))
            {
                var created = VaccineCatalogItem.Create(tenantId, code, name, now, id: id);
                if (created.IsSuccess)
                {
                    tenant.VaccineCatalogItemSet.Add(created.Value!);
                    existingVaccineCatalog[id] = created.Value!;
                    usedVaccineCodes.Add(code);
                }
            }
        }

        // 5. Products (tblStuff -> ProductKind.Medicine/PetShop) & Services (tblServices -> ProductKind.Service).
        var existingProducts = await tenant.ProductSet.ToDictionaryAsync(p => p.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var productInfoBySource = new Dictionary<string, (string ProductId, string VariantId, string Name, long SaleMinor, long BuyMinor)>(StringComparer.Ordinal);
        foreach (var prod in bundle.Products)
        {
            var productId = StableId(tenantId, "tblStuff", prod.SourceId);
            var variantId = StableId(tenantId, "tblStuff:variant", prod.SourceId);
            var sku = "PRD-" + IdSuffix(productId, 8);
            var categoryId = prod.CategorySourceId is not null && categoryIdsBySource.TryGetValue(prod.CategorySourceId, out var cid)
                ? cid
                : defaultGoodsCategoryId;
            var name = Truncate(prod.Name, 256);
            var cost = Money.Irt(Math.Max(0L, prod.BuyPriceMinor));
            var sale = Money.Irt(Math.Max(0L, prod.SalePriceMinor));
            var reorderVal = prod.ReorderPoint is > 0 ? Math.Round((decimal)prod.ReorderPoint.Value, 3) : 0m;
            var reorder = new Quantity(reorderVal, DefaultUnitCode);
            var notes = LegacyNote(prod.Legacy);

            if (existingProducts.TryGetValue(productId, out var existing))
            {
                existing.Describe(name, existing.Brand, notes);
                existing.SetPrices(cost, sale);
                var vId = existing.Variants.Count > 0 ? existing.Variants[0].VariantId : variantId;
                productInfoBySource[prod.SourceId] = (productId, vId, name, sale.Minor, cost.Minor);
            }
            else
            {
                var variant = new ProductVariant(
                    variantId,
                    sku + "-1",
                    ProductVariant.DefaultAttributeName,
                    sku,
                    cost,
                    sale,
                    reorder,
                    Clean(prod.Barcode),
                    true);
                var created = Product.Create(
                    tenantId,
                    sku,
                    name,
                    categoryId,
                    ProductKind.Medicine,
                    DefaultUnitCode,
                    cost,
                    sale,
                    [variant],
                    trackStock: true,
                    reorderLevel: reorder,
                    notes: notes,
                    id: productId);
                if (created.IsSuccess)
                {
                    tenant.ProductSet.Add(created.Value!);
                    existingProducts[productId] = created.Value!;
                    productInfoBySource[prod.SourceId] = (productId, variantId, name, sale.Minor, cost.Minor);
                }
            }
        }

        var serviceInfoBySource = new Dictionary<string, (string ProductId, string Name, long PriceMinor)>(StringComparer.Ordinal);
        foreach (var svc in bundle.Services)
        {
            var serviceId = StableId(tenantId, "tblServices", svc.SourceId);
            var variantId = StableId(tenantId, "tblServices:variant", svc.SourceId);
            var sku = "SRV-" + IdSuffix(serviceId, 8);
            var name = Truncate(svc.Name, 256);
            var price = Money.Irt(Math.Max(0L, svc.PriceMinor));
            var notes = LegacyNote(svc.Legacy);

            serviceInfoBySource[svc.SourceId] = (serviceId, name, price.Minor);
            if (existingProducts.TryGetValue(serviceId, out var existing))
            {
                existing.Describe(name, existing.Brand, notes);
                existing.SetPrices(Money.Irt(0), price);
            }
            else
            {
                var variant = new ProductVariant(
                    variantId,
                    sku + "-1",
                    ProductVariant.DefaultAttributeName,
                    sku,
                    Money.Irt(0),
                    price,
                    Quantity.Zero(DefaultUnitCode),
                    null,
                    true);
                var created = Product.Create(
                    tenantId,
                    sku,
                    name,
                    defaultServiceCategoryId,
                    ProductKind.Service,
                    DefaultUnitCode,
                    Money.Irt(0),
                    price,
                    [variant],
                    trackStock: false,
                    notes: notes,
                    id: serviceId);
                if (created.IsSuccess)
                {
                    tenant.ProductSet.Add(created.Value!);
                    existingProducts[serviceId] = created.Value!;
                }
            }
        }

        // 6. Staff lookup (tblStaff -> map to defaultUserId for FK references, preserve names for notes).
        var staffNameBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var st in bundle.Staff)
        {
            staffNameBySource[st.SourceId] = st.FullName;
        }

        // 7. Owners & Pets (tblPatient + tblEnglishInfo).
        var existingOwners = await tenant.OwnerSet.ToDictionaryAsync(o => o.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var existingPets = await tenant.PetSet.ToDictionaryAsync(p => p.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var existingHistoryIds = (await tenant.MedicalHistoryEntrySet.AsNoTracking()
            .Select(h => h.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

        var petOwnerBySource = new Dictionary<string, (string PetId, string OwnerId, string PetName, string OwnerName)>(StringComparer.Ordinal);

        foreach (var ownerDto in bundle.Owners)
        {
            var ownerId = StableId(tenantId, "tblPatient:owner", ownerDto.SourceId);
            var regInstant = ParseUtc(ownerDto.RegisteredUtc) ?? now;
            var ownerClock = new AtClock(regInstant);
            var gender = Enum.IsDefined((OwnerGender)ownerDto.Gender) ? (OwnerGender)ownerDto.Gender : OwnerGender.Unspecified;
            var primaryPhone = NormalizeDigitsOrNull(ownerDto.PhonePrimary);
            var secondaryPhone = NormalizeDigitsOrNull(ownerDto.PhoneSecondary);
            var landline = NormalizeDigitsOrNull(ownerDto.PhoneLandline);
            var ownerNotes = CombineNotes(ownerDto.Notes, LegacyNote(ownerDto.Legacy));
            var fullName = string.IsNullOrWhiteSpace(ownerDto.FullName) ? "مالک نامشخص" : ownerDto.FullName.Trim();

            if (!existingOwners.TryGetValue(ownerId, out var owner))
            {
                owner = Owner.Register(tenantId, fullName, primaryPhone, ownerClock, gender, ownerId);
                tenant.OwnerSet.Add(owner);
                existingOwners[ownerId] = owner;
            }

            owner.UpdateContactDetails(
                fullName,
                primaryPhone,
                secondaryPhone,
                nationalId: Clean(ownerDto.NationalId),
                email: Clean(ownerDto.Email),
                address: Clean(ownerDto.Address),
                city: Clean(ownerDto.City),
                gender: gender,
                notes: ownerNotes,
                phoneLandline: landline);
            owner.SearchKey = BuildOwnerSearchKey(owner);

            foreach (var petDto in ownerDto.Pets)
            {
                var petId = StableId(tenantId, "tblPatient", petDto.SourceId);
                var petRegInstant = ParseUtc(petDto.RegisteredUtc) ?? regInstant;
                var petClock = new AtClock(petRegInstant);
                var species = Enum.IsDefined((PetSpecies)petDto.Species) ? (PetSpecies)petDto.Species : PetSpecies.Other;
                var petGender = Enum.IsDefined((PetGender)petDto.Gender) ? (PetGender)petDto.Gender : PetGender.Unknown;
                var petStatus = Enum.IsDefined((PetStatus)petDto.Status) ? (PetStatus)petDto.Status : PetStatus.Active;
                var birthUtc = ParseUtc(petDto.BirthDateUtc);
                var petName = string.IsNullOrWhiteSpace(petDto.Name) ? "بدون نام" : petDto.Name.Trim();

                if (!existingPets.TryGetValue(petId, out var pet))
                {
                    pet = Pet.Register(tenantId, ownerId, petName, species, petClock, petGender, birthUtc, Clean(petDto.Breed), petId);
                    pet.ClearDomainEvents();
                    tenant.PetSet.Add(pet);
                    existingPets[petId] = pet;
                }

                pet.UpdateDetails(
                    petName,
                    Clean(petDto.Breed),
                    petGender,
                    birthUtc,
                    petDto.IsNeutered,
                    Clean(petDto.Color),
                    habitat: null,
                    behavior: null,
                    diet: null,
                    bloodType: Clean(petDto.BloodType),
                    insurance: Clean(petDto.Insurance),
                    neuteredUtc: null);

                pet.SetStatus(petStatus);
                if (!string.IsNullOrWhiteSpace(petDto.MicrochipCode) && ParseUtc(petDto.MicrochipImplantUtc) is { } implantUtc)
                {
                    pet.SetMicrochip(petDto.MicrochipCode, implantUtc);
                }

                pet.SearchKey = BuildPetSearchKey(pet);
                petOwnerBySource[petDto.SourceId] = (petId, ownerId, petName, fullName);

                var petNoteText = CombineNotes(petDto.Notes, LegacyNote(petDto.Legacy));
                if (!string.IsNullOrWhiteSpace(petNoteText))
                {
                    var historyId = StableId(tenantId, "tblPatient:note", petDto.SourceId);
                    if (existingHistoryIds.Add(historyId))
                    {
                        tenant.MedicalHistoryEntrySet.Add(new MedicalHistoryEntry(
                            tenantId,
                            petId,
                            MedicalHistoryKind.Note,
                            "یادداشت پرونده قدیم",
                            petRegInstant,
                            defaultUserId,
                            petClock,
                            detail: petNoteText,
                            sourceEntityId: petDto.SourceId,
                            sourceEntityType: "LegacyClinic:tblPatient",
                            id: historyId));
                    }
                }
            }
        }

        // 8. Visits & LabResults & Radiology (tblPatientRecord + tblBloodTestResult/tblUrine/tblEtcTest/tblRadiology).
        var existingVisits = await tenant.VisitSet.ToDictionaryAsync(v => v.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var labsByPetSource = bundle.LabResults
            .GroupBy(l => l.PetSourceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var attachedLabKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vDto in bundle.Visits)
        {
            if (!petOwnerBySource.TryGetValue(vDto.PetSourceId, out var po))
            {
                continue;
            }

            var visitId = StableId(tenantId, "tblPatientRecord", vDto.SourceId);
            var visitedInstant = ParseUtc(vDto.VisitedUtc) ?? now;
            var visitClock = new AtClock(visitedInstant);
            var complaint = JoinList(vDto.Complaints) ?? Clean(vDto.Reason);
            var diagnosis = Clean(vDto.Diagnosis) ?? Clean(vDto.Reason) ?? "ویزیت واردشده از سیستم قدیم";
            var treatmentPlan = CombineNotes(Clean(vDto.Prescription), JoinList(vDto.Treatments));
            var doctorName = vDto.DoctorSourceId is not null && staffNameBySource.TryGetValue(vDto.DoctorSourceId, out var dn) ? dn : null;
            var doctorNotes = CombineNotes(
                doctorName is not null ? $"پزشک معالج (قدیم): {doctorName}" : null,
                JoinList(vDto.Symptoms) is { } sym ? $"علائم: {sym}" : null,
                vDto.NextVisitUtc is { } nxt ? $"ویزیت بعدی: {nxt}" : null,
                LegacyNote(vDto.Legacy));

            if (!existingVisits.TryGetValue(visitId, out var visit))
            {
                visit = Visit.Open(
                    tenantId,
                    po.PetId,
                    po.OwnerId,
                    defaultUserId,
                    MapVisitReason(vDto.Reason),
                    visitClock,
                    chiefComplaint: complaint,
                    id: visitId);
                visit.Start();
                visit.SetAssessment(diagnosis, treatmentPlan, doctorNotes);

                // Attach any lab/radiology records for this pet if not yet attached.
                if (labsByPetSource.TryGetValue(vDto.PetSourceId, out var petLabs))
                {
                    foreach (var lab in petLabs)
                    {
                        var labKey = $"{lab.SourceTable}:{lab.SourceId}";
                        if (!attachedLabKeys.Add(labKey)) continue;
                        AttachLabOrRadiologyToVisit(tenantId, visit, lab, visitedInstant);
                    }
                }

                visit.Complete(visitClock);
                visit.ClearDomainEvents();
                tenant.VisitSet.Add(visit);
                existingVisits[visitId] = visit;
            }

            var vHistId = StableId(tenantId, "tblPatientRecord:history", vDto.SourceId);
            if (existingHistoryIds.Add(vHistId))
            {
                tenant.MedicalHistoryEntrySet.Add(new MedicalHistoryEntry(
                    tenantId,
                    po.PetId,
                    MedicalHistoryKind.Visit,
                    Truncate(diagnosis, 200),
                    visitedInstant,
                    defaultUserId,
                    visitClock,
                    detail: CombineNotes(treatmentPlan, doctorNotes),
                    sourceEntityId: visitId,
                    sourceEntityType: nameof(Visit),
                    id: vHistId));
            }
        }

        // For any lab results whose pet had no Visit row in tblPatientRecord, open a dedicated historical lab visit
        // and append a MedicalHistoryKind.Lab entry so every lab/radiology record is visible in the pet's clinical file.
        foreach (var lab in bundle.LabResults)
        {
            if (!petOwnerBySource.TryGetValue(lab.PetSourceId, out var po))
            {
                continue;
            }

            var labKey = $"{lab.SourceTable}:{lab.SourceId}";
            var performedInstant = ParseUtc(lab.PerformedUtc) ?? now;
            var labClock = new AtClock(performedInstant);

            if (attachedLabKeys.Add(labKey))
            {
                var synthVisitId = StableId(tenantId, "labVisit:" + lab.SourceTable, lab.SourceId);
                if (!existingVisits.ContainsKey(synthVisitId))
                {
                    var synthVisit = Visit.Open(
                        tenantId,
                        po.PetId,
                        po.OwnerId,
                        defaultUserId,
                        VisitReason.Checkup,
                        labClock,
                        chiefComplaint: Clean(lab.Title) ?? lab.Kind,
                        id: synthVisitId);
                    synthVisit.Start();
                    synthVisit.SetAssessment(Clean(lab.Title) ?? $"آزمایش ({lab.Kind})", null, Clean(lab.Notes));
                    AttachLabOrRadiologyToVisit(tenantId, synthVisit, lab, performedInstant);
                    synthVisit.Complete(labClock);
                    synthVisit.ClearDomainEvents();
                    tenant.VisitSet.Add(synthVisit);
                    existingVisits[synthVisitId] = synthVisit;
                }
            }

            var labHistId = StableId(tenantId, lab.SourceTable + ":history", lab.SourceId);
            if (existingHistoryIds.Add(labHistId))
            {
                var valuesText = FormatLabValues(lab.Values);
                tenant.MedicalHistoryEntrySet.Add(new MedicalHistoryEntry(
                    tenantId,
                    po.PetId,
                    MedicalHistoryKind.Lab,
                    Truncate(Clean(lab.Title) ?? $"آزمایش {lab.Kind}", 200),
                    performedInstant,
                    defaultUserId,
                    labClock,
                    detail: CombineNotes(valuesText, Clean(lab.Notes), LegacyNote(lab.Legacy)),
                    sourceEntityId: lab.SourceId,
                    sourceEntityType: "LegacyClinic:" + lab.SourceTable,
                    id: labHistId));
            }
        }

        // 9. Vaccinations (tblVaccination -> VaccinationRecord + MedicalHistoryEntry).
        var existingVaccinations = await tenant.VaccinationRecordSet.ToDictionaryAsync(v => v.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        foreach (var vac in bundle.Vaccinations)
        {
            if (!petOwnerBySource.TryGetValue(vac.PetSourceId, out var po))
            {
                continue;
            }

            var vacId = StableId(tenantId, "tblVaccination", vac.SourceId);
            var adminInstant = ParseUtc(vac.AdministeredUtc) ?? ParseUtc(vac.NextDueUtc) ?? now;
            var vacClock = new AtClock(adminInstant);
            var (code, catName) = vac.VaccineTypeSourceId is not null && vaccineCodeBySource.TryGetValue(vac.VaccineTypeSourceId, out var vt)
                ? vt
                : ("VAC-LEGACY", "واکسن");
            var name = Truncate(Clean(vac.Name) ?? catName, 120);
            var notes = CombineNotes(
                Clean(vac.Notes),
                vac.NextDueUtc is not null ? $"موعد بعدی در منبع: {vac.NextDueUtc}" : null,
                LegacyNote(vac.Legacy));

            if (!existingVaccinations.ContainsKey(vacId))
            {
                var created = VaccinationRecord.Administer(
                    tenantId,
                    po.PetId,
                    code,
                    name,
                    1,
                    adminInstant,
                    defaultUserId,
                    vacClock,
                    notes: notes,
                    id: vacId);
                if (created.IsSuccess)
                {
                    created.Value!.ClearDomainEvents();
                    tenant.VaccinationRecordSet.Add(created.Value!);
                    existingVaccinations[vacId] = created.Value!;
                }
            }

            var vacHistId = StableId(tenantId, "tblVaccination:history", vac.SourceId);
            if (existingHistoryIds.Add(vacHistId))
            {
                tenant.MedicalHistoryEntrySet.Add(new MedicalHistoryEntry(
                    tenantId,
                    po.PetId,
                    MedicalHistoryKind.Vaccination,
                    name,
                    adminInstant,
                    defaultUserId,
                    vacClock,
                    detail: notes,
                    sourceEntityId: vacId,
                    sourceEntityType: nameof(VaccinationRecord),
                    id: vacHistId));
            }
        }

        // 10. Antiparasites (tblAntiParasite -> TreatmentPlan + TreatmentOccurrence + MedicalHistoryEntry).
        var existingPlans = await tenant.TreatmentPlanSet.ToDictionaryAsync(p => p.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        foreach (var ap in bundle.Antiparasites)
        {
            if (!petOwnerBySource.TryGetValue(ap.PetSourceId, out var po))
            {
                continue;
            }

            var planId = StableId(tenantId, "tblAntiParasite", ap.SourceId);
            var occId = StableId(tenantId, "tblAntiParasite:occ", ap.SourceId);
            var adminInstant = ParseUtc(ap.AdministeredUtc) ?? ParseUtc(ap.NextDueUtc) ?? now;
            var apClock = new AtClock(adminInstant);
            var dueInstant = ParseUtc(ap.NextDueUtc) ?? adminInstant;
            var dueDay = DateOnly.FromDateTime(dueInstant.UtcDateTime);
            var title = BuildAntiparasiteTitle(ap);
            var notes = TruncateNullable(CombineNotes(
                Clean(ap.Notes),
                ap.InternalParasite is not null ? $"انگل داخلی: {ap.InternalParasite}" : null,
                ap.ExternalParasite is not null ? $"انگل خارجی: {ap.ExternalParasite}" : null,
                LegacyNote(ap.Legacy)), TreatmentPlan.MaxNotesLength);

            if (!existingPlans.ContainsKey(planId))
            {
                var draft = TreatmentPlan.Create(
                    tenantId,
                    po.PetId,
                    TreatmentItemType.Medication,
                    TreatmentKind.ClinicVisit,
                    title,
                    TreatmentSchedule.OnceOn(dueDay),
                    dueDay,
                    defaultUserId,
                    apClock,
                    notes: notes,
                    id: planId,
                    occurrenceId: _ => occId);
                if (draft.IsSuccess)
                {
                    var plan = draft.Value!.Plan;
                    var occ = draft.Value.Occurrences[0];
                    if (ap.AdministeredUtc is not null && ap.NextDueUtc is null)
                    {
                        occ.MarkDone(adminInstant, defaultUserId);
                    }

                    tenant.TreatmentPlanSet.Add(plan);
                    tenant.TreatmentOccurrenceSet.Add(occ);
                    existingPlans[planId] = plan;
                }
            }

            var apHistId = StableId(tenantId, "tblAntiParasite:history", ap.SourceId);
            if (existingHistoryIds.Add(apHistId))
            {
                tenant.MedicalHistoryEntrySet.Add(new MedicalHistoryEntry(
                    tenantId,
                    po.PetId,
                    MedicalHistoryKind.Prescription,
                    title,
                    adminInstant,
                    defaultUserId,
                    apClock,
                    detail: notes,
                    sourceEntityId: planId,
                    sourceEntityType: nameof(TreatmentPlan),
                    id: apHistId));
            }
        }

        // 11. Appointments (tblAppointments -> Appointment).
        var existingAppointments = await tenant.AppointmentSet.ToDictionaryAsync(a => a.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        foreach (var appt in bundle.Appointments)
        {
            (string PetId, string OwnerId, string PetName, string OwnerName) target;
            if (appt.PetSourceId is not null && petOwnerBySource.TryGetValue(appt.PetSourceId, out var foundPo))
            {
                target = foundPo;
            }
            else if (petOwnerBySource.Values.FirstOrDefault() is { PetId.Length: > 0 } fallbackPo)
            {
                target = fallbackPo;
            }
            else
            {
                continue;
            }

            var apptId = StableId(tenantId, "tblAppointments", appt.SourceId);
            if (existingAppointments.ContainsKey(apptId))
            {
                continue;
            }

            var startInstant = ParseUtc(appt.StartUtc) ?? now;
            var apptClock = new AtClock(startInstant);
            var note = CombineNotes(
                Clean(appt.PatientName) is { } pn ? $"نام بیمار/مراجع: {pn}" : null,
                Clean(appt.Mobile) is { } mob ? $"موبایل: {mob}" : null,
                Clean(appt.Notes),
                LegacyNote(appt.Legacy));

            var booked = Appointment.Book(
                tenantId,
                target.PetId,
                target.OwnerId,
                "CHECKUP",
                startInstant,
                30,
                AppointmentSource.Phone,
                defaultUserId,
                apptClock,
                doctorUserId: null,
                note: note,
                id: apptId);
            if (booked.IsSuccess)
            {
                if (appt.IsCheckedIn)
                {
                    booked.Value!.CheckIn(apptClock);
                }

                booked.Value!.ClearDomainEvents();
                tenant.AppointmentSet.Add(booked.Value!);
                existingAppointments[apptId] = booked.Value!;
            }
        }

        // 12. Stock Receipts (tblStockHead + tblStock -> Supplier + GoodsReceipt + StockMovement).
        var existingSuppliers = await tenant.SupplierSet.ToDictionaryAsync(s => s.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var existingReceipts = await tenant.GoodsReceiptSet.ToDictionaryAsync(r => r.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var existingMovements = (await tenant.StockMovementSet.AsNoTracking()
            .Select(m => m.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

        var defaultSupplierId = StableId(tenantId, "Supplier", "__default__");
        if (!existingSuppliers.ContainsKey(defaultSupplierId))
        {
            var sup = Supplier.Create(tenantId, "SUP-" + IdSuffix(defaultSupplierId, 6), "تأمین‌کننده پیش‌فرض (واردشده)", id: defaultSupplierId);
            if (sup.IsSuccess)
            {
                tenant.SupplierSet.Add(sup.Value!);
                existingSuppliers[defaultSupplierId] = sup.Value!;
            }
        }

        foreach (var rcpt in bundle.StockReceipts)
        {
            if (rcpt.Lines.Count == 0)
            {
                continue;
            }

            var receiptId = StableId(tenantId, "tblStockHead", rcpt.SourceId);
            if (existingReceipts.ContainsKey(receiptId))
            {
                continue;
            }

            var receivedInstant = ParseUtc(rcpt.ReceivedUtc) ?? now;
            var rcptClock = new AtClock(receivedInstant);
            var sellerName = Clean(rcpt.Seller);
            var supplierId = defaultSupplierId;
            if (!string.IsNullOrWhiteSpace(sellerName))
            {
                supplierId = StableId(tenantId, "Supplier", sellerName);
                if (!existingSuppliers.ContainsKey(supplierId))
                {
                    var sup = Supplier.Create(tenantId, "SUP-" + IdSuffix(supplierId, 6), Truncate(sellerName, 128), id: supplierId);
                    if (sup.IsSuccess)
                    {
                        tenant.SupplierSet.Add(sup.Value!);
                        existingSuppliers[supplierId] = sup.Value!;
                    }
                    else
                    {
                        supplierId = defaultSupplierId;
                    }
                }
            }

            var receiptNumber = $"IMP-GR-{rcpt.SourceId}";
            var createdReceipt = GoodsReceipt.Create(
                tenantId,
                receiptNumber,
                supplierId,
                receivedInstant,
                defaultUserId,
                invoiceReference: Clean(rcpt.Number),
                id: receiptId);
            if (createdReceipt.IsFailure)
            {
                continue;
            }

            var receipt = createdReceipt.Value!;
            var addedLines = 0;
            foreach (var lineDto in rcpt.Lines)
            {
                if (!productInfoBySource.TryGetValue(lineDto.ProductSourceId, out var pInfo))
                {
                    continue;
                }

                var qtyDecimal = Math.Round((decimal)lineDto.Quantity, 3);
                if (qtyDecimal <= 0m)
                {
                    continue;
                }

                var lineId = StableId(tenantId, "tblStock", lineDto.SourceId);
                var qty = new Quantity(qtyDecimal, DefaultUnitCode);
                var cost = Money.Irt(Math.Max(0L, lineDto.BuyPriceMinor));
                var expUtc = ParseUtc(lineDto.ExpireUtc);

                var grLine = new GoodsReceiptLine(lineId, pInfo.ProductId, pInfo.VariantId, qty, cost, null, expUtc);
                if (receipt.AddLine(grLine).IsSuccess)
                {
                    addedLines++;
                    var movId = StableId(tenantId, "tblStock:movement", lineDto.SourceId);
                    if (existingMovements.Add(movId))
                    {
                        var mov = StockMovement.Create(
                            tenantId,
                            pInfo.ProductId,
                            pInfo.VariantId,
                            MovementKind.Receipt,
                            qty,
                            cost,
                            receivedInstant,
                            defaultUserId,
                            expiryUtc: expUtc,
                            sourceEntityType: nameof(GoodsReceipt),
                            sourceEntityId: receiptId,
                            operationId: movId,
                            reason: "Legacy stock receipt import",
                            id: movId);
                        if (mov.IsSuccess)
                        {
                            tenant.StockMovementSet.Add(mov.Value!);
                        }
                    }
                }
            }

            if (addedLines > 0 && receipt.Post(rcptClock).IsSuccess)
            {
                receipt.ClearDomainEvents();
                tenant.GoodsReceiptSet.Add(receipt);
                existingReceipts[receiptId] = receipt;
            }
        }

        // 13. Payment Types lookup & Invoices (tblCharge + tblChargeDetails + tblChargePay).
        var paymentMethodBySource = new Dictionary<string, PaymentMethod>(StringComparer.Ordinal);
        foreach (var pt in bundle.PaymentTypes)
        {
            paymentMethodBySource[pt.SourceId] = MapPaymentMethod(pt.Method);
        }

        var existingInvoices = await tenant.InvoiceSet.ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var usedInvoiceNumbers = (await tenant.InvoiceSet.AsNoTracking()
            .Select(i => i.InvoiceNumber).ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

        foreach (var invDto in bundle.Invoices)
        {
            var invoiceId = StableId(tenantId, "tblCharge", invDto.SourceId);
            if (existingInvoices.ContainsKey(invoiceId))
            {
                continue;
            }

            var issuedInstant = ParseUtc(invDto.IssuedUtc) ?? now;
            var invClock = new AtClock(issuedInstant);

            string? ownerId = null;
            string? petId = null;
            if (invDto.PetSourceId is not null && petOwnerBySource.TryGetValue(invDto.PetSourceId, out var po))
            {
                ownerId = po.OwnerId;
                petId = po.PetId;
            }

            var guestName = ownerId is null ? (Clean(invDto.CustomerName) ?? "مشتری حضوری") : null;
            var invNumber = $"IMP-INV-{invDto.SourceId}";
            if (!usedInvoiceNumbers.Add(invNumber))
            {
                invNumber = $"IMP-INV-{invDto.SourceId}-{IdSuffix(invoiceId, 4)}";
                usedInvoiceNumbers.Add(invNumber);
            }

            var invNotes = CombineNotes(
                Clean(invDto.Number) is { } num ? $"شماره قبض/فاکتور قدیم: {num}" : null,
                Clean(invDto.Notes),
                LegacyNote(invDto.Legacy));

            var createdInv = Invoice.Create(
                tenantId,
                invNumber,
                InvoiceKind.DirectSale,
                issuedInstant,
                defaultUserId,
                ownerId: ownerId,
                guestName: guestName,
                petId: petId,
                currency: CurrencyScale.Irt,
                operationId: invoiceId,
                notes: invNotes,
                id: invoiceId);
            if (createdInv.IsFailure)
            {
                continue;
            }

            var invoice = createdInv.Value!;
            var hasLines = false;

            foreach (var lDto in invDto.Lines)
            {
                var lineId = StableId(tenantId, "tblChargeDetails", lDto.SourceId);
                var qtyVal = Math.Round((decimal)lDto.Quantity, 3);
                if (qtyVal <= 0m)
                {
                    qtyVal = 1m;
                }

                var unitMinor = Math.Max(0L, lDto.UnitPriceMinor);
                if (unitMinor == 0L && lDto.TotalMinor > 0L && qtyVal == 1m)
                {
                    unitMinor = lDto.TotalMinor;
                }

                var lineGross = unitMinor * qtyVal;
                var discPct = 0m;
                if (lDto.DiscountMinor > 0 && lineGross > 0)
                {
                    discPct = Math.Clamp(Math.Round(lDto.DiscountMinor * 100m / lineGross, 2), 0m, 100m);
                }

                InvoiceLine line;
                if (lDto.Kind == "product" && lDto.ProductSourceId is not null && productInfoBySource.TryGetValue(lDto.ProductSourceId, out var pInfo))
                {
                    line = new InvoiceLine(
                        lineId,
                        InvoiceLineKind.Product,
                        pInfo.ProductId,
                        pInfo.ProductId,
                        pInfo.VariantId,
                        Clean(lDto.Description) ?? pInfo.Name,
                        new Quantity(qtyVal, DefaultUnitCode),
                        Money.Irt(unitMinor),
                        discPct,
                        0m,
                        InvoiceLineSource.Catalog,
                        pInfo.ProductId);
                }
                else if (lDto.Kind == "service" && lDto.ServiceSourceId is not null && serviceInfoBySource.TryGetValue(lDto.ServiceSourceId, out var sInfo))
                {
                    line = new InvoiceLine(
                        lineId,
                        InvoiceLineKind.Service,
                        sInfo.ProductId,
                        sInfo.ProductId,
                        null,
                        Clean(lDto.Description) ?? sInfo.Name,
                        new Quantity(qtyVal, DefaultUnitCode),
                        Money.Irt(unitMinor),
                        discPct,
                        0m,
                        InvoiceLineSource.Catalog,
                        sInfo.ProductId);
                }
                else
                {
                    line = new InvoiceLine(
                        lineId,
                        InvoiceLineKind.Custom,
                        null,
                        null,
                        null,
                        Clean(lDto.Description) ?? "آیتم فاکتور قدیم",
                        new Quantity(qtyVal, DefaultUnitCode),
                        Money.Irt(unitMinor),
                        discPct,
                        0m,
                        InvoiceLineSource.Manual,
                        null);
                }

                if (invoice.AddLine(line).IsSuccess)
                {
                    hasLines = true;
                }
            }

            if (!hasLines && invDto.TotalMinor > 0)
            {
                var fallbackLineId = StableId(tenantId, "tblChargeDetails:hdr", invDto.SourceId);
                var fallbackLine = new InvoiceLine(
                    fallbackLineId,
                    InvoiceLineKind.Custom,
                    null,
                    null,
                    null,
                    "مبلغ فاکتور واردشده از سیستم قدیم",
                    new Quantity(1m, DefaultUnitCode),
                    Money.Irt(invDto.TotalMinor),
                    0m,
                    0m,
                    InvoiceLineSource.Manual,
                    null);
                if (invoice.AddLine(fallbackLine).IsSuccess)
                {
                    hasLines = true;
                }
            }

            if (invDto.DiscountMinor > 0)
            {
                invoice.SetDiscount(DiscountKind.Amount, invDto.DiscountMinor);
            }

            if (invDto.InsurancePayMinor > 0 && invDto.InsurerSourceId is not null &&
                insurerIdsBySource.TryGetValue(invDto.InsurerSourceId, out var insId) && invoice.GrandTotal.Minor > 0)
            {
                var pct = Math.Clamp(Math.Round(invDto.InsurancePayMinor * 100m / invoice.GrandTotal.Minor, 4), 0m, 100m);
                invoice.SetInsurance(insId, pct);
            }

            if (hasLines && invoice.GrandTotal.Minor > 0)
            {
                if (invoice.Post(invClock).IsSuccess)
                {
                    var payIndex = 0;
                    foreach (var pDto in invDto.Payments)
                    {
                        if (pDto.AmountMinor <= 0 || invoice.DueTotal.Minor <= 0)
                        {
                            continue;
                        }

                        var payMinor = Math.Min(pDto.AmountMinor, invoice.DueTotal.Minor);
                        if (payMinor <= 0)
                        {
                            continue;
                        }

                        var paidInstant = ParseUtc(pDto.PaidUtc) ?? issuedInstant;
                        var payOpId = StableId(tenantId, $"tblChargePay:{invDto.SourceId}", $"{pDto.SourceId}:{payIndex++}");
                        var rawMethod = pDto.PaymentTypeSourceId is not null && paymentMethodBySource.TryGetValue(pDto.PaymentTypeSourceId, out var pm)
                            ? pm
                            : (invDto.PaymentTypeSourceId is not null && paymentMethodBySource.TryGetValue(invDto.PaymentTypeSourceId, out var hpm) ? hpm : PaymentMethod.Transfer);
                        // Cash requires an open CashSession and Cheque requires a Cheque record; map historical payments
                        // to Card/Transfer so they settle cleanly without fabricating live drawer sessions.
                        var effectiveMethod = rawMethod is PaymentMethod.Cash or PaymentMethod.Cheque ? PaymentMethod.Transfer : rawMethod;

                        var payment = Payment.Create(
                            tenantId,
                            invoiceId,
                            effectiveMethod,
                            Money.Irt(payMinor),
                            paidInstant,
                            defaultUserId,
                            operationId: payOpId,
                            settlement: SettlementStatus.Settled,
                            note: $"Legacy payment ({rawMethod})");
                        if (payment.IsSuccess && invoice.ApplyPayment(payment.Value!).IsSuccess)
                        {
                            tenant.PaymentSet.Add(payment.Value!);
                        }
                    }

                    invoice.MarkSettlement(SettlementStatus.Settled);
                }
            }

            invoice.ClearDomainEvents();
            tenant.InvoiceSet.Add(invoice);
            existingInvoices[invoiceId] = invoice;
        }

        return Result<ClinicImportSummary>.Ok(Summarize(tenantId, bundle));
    }

    private static void AttachLabOrRadiologyToVisit(
        string tenantId,
        Visit visit,
        ClinicImportLabResult lab,
        DateTimeOffset takenInstant)
    {
        var entryId = StableId(tenantId, lab.SourceTable, lab.SourceId);
        if (string.Equals(lab.Kind, "radiology", StringComparison.OrdinalIgnoreCase))
        {
            var bodyPart = Truncate(Clean(lab.Title) ?? "تصویربرداری", 100);
            var result = TruncateNullable(CombineNotes(Clean(lab.Notes), LegacyNote(lab.Legacy)), 4000);
            visit.AddRadiology(new RadiologyRecord(entryId, takenInstant, bodyPart, result, []));
            return;
        }

        var items = new List<LabResultItem>();
        if (lab.Values is not null)
        {
            foreach (var (k, v) in lab.Values)
            {
                if (items.Count >= 100) break;
                var valStr = FormatJsonElement(v);
                if (string.IsNullOrWhiteSpace(valStr)) continue;
                items.Add(new LabResultItem(
                    Truncate(k, 40),
                    Truncate(k, 120),
                    Truncate(valStr, 200),
                    null));
            }
        }

        var desc = TruncateNullable(CombineNotes(Clean(lab.Title), Clean(lab.Notes), LegacyNote(lab.Legacy)), 4000);
        if (items.Count == 0 && string.IsNullOrWhiteSpace(desc))
        {
            desc = $"آزمایش ({lab.Kind})";
        }

        visit.AddLabResult(new LabResult(entryId, takenInstant, desc, items));
    }

    private static string? ValidateBundle(ClinicImportBundle b)
    {
        if (b.FormatVersion != ExpectedFormatVersion)
        {
            return $"Unsupported formatVersion {b.FormatVersion}; expected {ExpectedFormatVersion}.";
        }

        if (string.IsNullOrWhiteSpace(b.ConverterVersion) ||
            string.IsNullOrWhiteSpace(b.GeneratedUtc) ||
            string.IsNullOrWhiteSpace(b.SourceFingerprint) || b.SourceFingerprint.Length < 64 ||
            string.IsNullOrWhiteSpace(b.DedupeRule) ||
            !string.Equals(b.Currency, "IRT", StringComparison.Ordinal))
        {
            return "Bundle header metadata is incomplete or currency is not IRT.";
        }

        if (b.Owners is null || b.Owners.Count == 0)
        {
            return "Bundle must contain at least one owner.";
        }

        var petIds = new HashSet<string>(StringComparer.Ordinal);
        var ownerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in b.Owners)
        {
            if (string.IsNullOrWhiteSpace(owner.SourceId) || !ownerIds.Add(owner.SourceId) || string.IsNullOrWhiteSpace(owner.FullName))
            {
                return $"Invalid or duplicate owner '{owner.SourceId}'.";
            }

            if (owner.Gender is < 0 or > 2)
            {
                return $"Invalid gender {owner.Gender} on owner '{owner.SourceId}'.";
            }

            foreach (var pet in owner.Pets)
            {
                if (string.IsNullOrWhiteSpace(pet.SourceId) || !petIds.Add(pet.SourceId) || string.IsNullOrWhiteSpace(pet.Name))
                {
                    return $"Invalid or duplicate pet '{pet.SourceId}'.";
                }

                if (pet.Species is < 0 or > 6 || pet.Gender is < 0 or > 2 || pet.Status is < 0 or > 2)
                {
                    return $"Invalid enum value on pet '{pet.SourceId}'.";
                }
            }
        }

        if (petIds.Count == 0)
        {
            return "Bundle must contain at least one pet.";
        }

        var staffIds = CollectIds(b.Staff, s => s.SourceId, out var err) ?? throw new InvalidOperationException(err);
        var insurerIds = CollectIds(b.Insurers, i => i.SourceId, out err);
        if (insurerIds is null) return err;
        var paymentTypeIds = CollectIds(b.PaymentTypes, p => p.SourceId, out err);
        if (paymentTypeIds is null) return err;
        var categoryIds = CollectIds(b.Categories, c => c.SourceId, out err);
        if (categoryIds is null) return err;
        var vaccineTypeIds = CollectIds(b.VaccineTypes, v => v.SourceId, out err);
        if (vaccineTypeIds is null) return err;
        var serviceIds = CollectIds(b.Services, s => s.SourceId, out err);
        if (serviceIds is null) return err;
        var productIds = CollectIds(b.Products, p => p.SourceId, out err);
        if (productIds is null) return err;

        foreach (var s in b.Services)
        {
            if (s.PriceMinor < 0 || (s.StaffSourceId is not null && !staffIds.Contains(s.StaffSourceId)))
            {
                return $"Invalid service '{s.SourceId}'.";
            }
        }

        foreach (var p in b.Products)
        {
            if (p.SalePriceMinor < 0 || p.BuyPriceMinor < 0 || (p.CategorySourceId is not null && !categoryIds.Contains(p.CategorySourceId)))
            {
                return $"Invalid product '{p.SourceId}'.";
            }
        }

        foreach (var v in b.Visits)
        {
            if (string.IsNullOrWhiteSpace(v.SourceId) || !petIds.Contains(v.PetSourceId) ||
                (v.DoctorSourceId is not null && !staffIds.Contains(v.DoctorSourceId)))
            {
                return $"Unresolved reference in visit '{v.SourceId}'.";
            }
        }

        foreach (var vac in b.Vaccinations)
        {
            if (string.IsNullOrWhiteSpace(vac.SourceId) || !petIds.Contains(vac.PetSourceId) ||
                (vac.VaccineTypeSourceId is not null && !vaccineTypeIds.Contains(vac.VaccineTypeSourceId)))
            {
                return $"Unresolved reference in vaccination '{vac.SourceId}'.";
            }
        }

        foreach (var ap in b.Antiparasites)
        {
            if (string.IsNullOrWhiteSpace(ap.SourceId) || !petIds.Contains(ap.PetSourceId))
            {
                return $"Unresolved reference in antiparasite '{ap.SourceId}'.";
            }
        }

        foreach (var lab in b.LabResults)
        {
            if (string.IsNullOrWhiteSpace(lab.SourceId) || string.IsNullOrWhiteSpace(lab.SourceTable) || !petIds.Contains(lab.PetSourceId))
            {
                return $"Unresolved reference in labResult '{lab.SourceId}'.";
            }
        }

        foreach (var appt in b.Appointments)
        {
            if (string.IsNullOrWhiteSpace(appt.SourceId) ||
                (appt.PetSourceId is not null && !petIds.Contains(appt.PetSourceId)) ||
                (appt.DoctorSourceId is not null && !staffIds.Contains(appt.DoctorSourceId)))
            {
                return $"Unresolved reference in appointment '{appt.SourceId}'.";
            }
        }

        foreach (var inv in b.Invoices)
        {
            if (string.IsNullOrWhiteSpace(inv.SourceId) || inv.TotalMinor < 0 || inv.DiscountMinor < 0 ||
                inv.InsurancePayMinor < 0 || inv.TaxMinor < 0 || inv.PaidMinor < 0 ||
                (inv.PetSourceId is not null && !petIds.Contains(inv.PetSourceId)) ||
                (inv.InsurerSourceId is not null && !insurerIds.Contains(inv.InsurerSourceId)) ||
                (inv.PaymentTypeSourceId is not null && !paymentTypeIds.Contains(inv.PaymentTypeSourceId)))
            {
                return $"Unresolved reference or negative amount in invoice '{inv.SourceId}'.";
            }

            foreach (var line in inv.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.SourceId) || line.UnitPriceMinor < 0 || line.TotalMinor < 0 || line.DiscountMinor < 0 ||
                    (line.ProductSourceId is not null && !productIds.Contains(line.ProductSourceId)) ||
                    (line.ServiceSourceId is not null && !serviceIds.Contains(line.ServiceSourceId)))
                {
                    return $"Invalid line '{line.SourceId}' on invoice '{inv.SourceId}'.";
                }
            }
        }

        foreach (var rcpt in b.StockReceipts)
        {
            if (string.IsNullOrWhiteSpace(rcpt.SourceId) || rcpt.TotalMinor < 0)
            {
                return $"Invalid stockReceipt '{rcpt.SourceId}'.";
            }

            foreach (var line in rcpt.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.SourceId) || !productIds.Contains(line.ProductSourceId) || line.BuyPriceMinor < 0)
                {
                    return $"Invalid line '{line.SourceId}' on stockReceipt '{rcpt.SourceId}'.";
                }
            }
        }

        return null;
    }

    private static HashSet<string>? CollectIds<T>(IReadOnlyList<T> items, Func<T, string> selector, out string? error)
    {
        error = null;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = selector(item);
            if (string.IsNullOrWhiteSpace(id) || !set.Add(id))
            {
                error = $"Duplicate or empty sourceId '{id}'.";
                return null;
            }
        }

        return set;
    }

    private static ClinicImportSummary Summarize(string tenantId, ClinicImportBundle b) =>
        new(
            tenantId,
            b.FormatVersion,
            b.SourceFingerprint,
            b.Owners.Count,
            b.Owners.Sum(o => o.Pets.Count),
            b.Visits.Count,
            b.Vaccinations.Count,
            b.Antiparasites.Count,
            b.LabResults.Count,
            b.Appointments.Count,
            b.Invoices.Count,
            b.StockReceipts.Count,
            b.Products.Count,
            b.Services.Count,
            b.Categories.Count,
            b.Insurers.Count,
            b.Warnings);

    private static string BuildOwnerSearchKey(Owner o) =>
        PersianText.Normalize(string.Join(' ', o.FullName, o.PhonePrimary, o.PhoneSecondary ?? string.Empty, o.NationalId ?? string.Empty));

    private static string BuildPetSearchKey(Pet p) =>
        PersianText.Normalize(string.Join(' ', p.Name, p.Breed ?? string.Empty, p.MicrochipCode ?? string.Empty));

    private static string? NormalizeDigitsOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = Owner.NormalizePhone(raw);
        return digits.Length == 0 ? null : digits;
    }

    private static DateTimeOffset? ParseUtc(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : null;
    }

    private static VisitReason MapVisitReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return VisitReason.Checkup;
        if (reason.Contains("واکسن", StringComparison.Ordinal)) return VisitReason.Vaccination;
        if (reason.Contains("جراح", StringComparison.Ordinal)) return VisitReason.Surgery;
        if (reason.Contains("دندان", StringComparison.Ordinal)) return VisitReason.Dental;
        if (reason.Contains("مجدد", StringComparison.Ordinal) || reason.Contains("پیگیر", StringComparison.Ordinal)) return VisitReason.Followup;
        return VisitReason.Checkup;
    }

    private static PaymentMethod MapPaymentMethod(string? method) => method switch
    {
        "cash" => PaymentMethod.Cash,
        "card" => PaymentMethod.Card,
        "transfer" => PaymentMethod.Transfer,
        "cheque" => PaymentMethod.Cheque,
        _ => PaymentMethod.Transfer,
    };

    private static string BuildAntiparasiteTitle(ClinicImportAntiparasite ap)
    {
        var hasInternal = !string.IsNullOrWhiteSpace(ap.InternalParasite) &&
                          !string.Equals(ap.InternalParasite, "False", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(ap.InternalParasite, "0", StringComparison.Ordinal);
        var hasExternal = !string.IsNullOrWhiteSpace(ap.ExternalParasite) &&
                          !string.Equals(ap.ExternalParasite, "False", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(ap.ExternalParasite, "0", StringComparison.Ordinal);
        var label = (hasInternal, hasExternal) switch
        {
            (true, true) => "ضدانگل داخلی و خارجی",
            (true, false) => "ضدانگل داخلی",
            (false, true) => "ضدانگل خارجی",
            _ => Clean(ap.Notes) ?? "درمان ضدانگل",
        };
        return Truncate(label, TreatmentPlan.MaxTitleLength);
    }

    private static string? FormatLabValues(Dictionary<string, JsonElement>? values)
    {
        if (values is null || values.Count == 0) return null;
        return string.Join(" · ", values.Select(kv => $"{kv.Key}: {FormatJsonElement(kv.Value)}"));
    }

    private static string FormatJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => el.GetRawText(),
    };

    private static string? JoinList(IReadOnlyList<string>? items) =>
        items is { Count: > 0 } ? string.Join("، ", items.Where(s => !string.IsNullOrWhiteSpace(s))) : null;

    private static string? LegacyNote(Dictionary<string, JsonElement>? legacy) =>
        legacy is { Count: > 0 } ? "legacy: " + JsonSerializer.Serialize(legacy) : null;

    private static string? CombineNotes(params string?[] parts)
    {
        var nonEmpty = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray();
        return nonEmpty.Length == 0 ? null : string.Join("\n", nonEmpty);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value, maxLength);

    private static string IdSuffix(string ulid, int length) =>
        ulid.Length >= length ? ulid[^length..] : ulid;

    private sealed class AtClock(DateTimeOffset at) : IClock
    {
        public DateTimeOffset UtcNow => at;
    }
}

public sealed class ClinicImportBundle
{
    public int FormatVersion { get; set; }
    public string GeneratedUtc { get; set; } = string.Empty;
    public string ConverterVersion { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public Dictionary<string, int> SourceRowCounts { get; set; } = [];
    public string DedupeRule { get; set; } = string.Empty;
    public string Currency { get; set; } = "IRT";
    public List<string> Notes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<ClinicImportLookup> Insurers { get; set; } = [];
    public List<ClinicImportPaymentType> PaymentTypes { get; set; } = [];
    public List<ClinicImportLookup> Categories { get; set; } = [];
    public List<ClinicImportSpecies> Species { get; set; } = [];
    public List<ClinicImportLookup> VaccineTypes { get; set; } = [];
    public List<ClinicImportStaff> Staff { get; set; } = [];
    public List<ClinicImportService> Services { get; set; } = [];
    public List<ClinicImportProduct> Products { get; set; } = [];
    public List<ClinicImportOwner> Owners { get; set; } = [];
    public List<ClinicImportVisit> Visits { get; set; } = [];
    public List<ClinicImportVaccination> Vaccinations { get; set; } = [];
    public List<ClinicImportAntiparasite> Antiparasites { get; set; } = [];
    public List<ClinicImportLabResult> LabResults { get; set; } = [];
    public List<ClinicImportAppointment> Appointments { get; set; } = [];
    public List<ClinicImportInvoice> Invoices { get; set; } = [];
    public List<ClinicImportStockReceipt> StockReceipts { get; set; } = [];
    public List<ClinicImportHoliday> Holidays { get; set; } = [];
    public Dictionary<string, JsonElement>? Archive { get; set; }
    public Dictionary<string, JsonElement>? Raw { get; set; }
}

public sealed class ClinicImportLookup
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportPaymentType
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = "other";
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportSpecies
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Species { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportStaff
{
    public string SourceId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Specialty { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportService
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceMinor { get; set; }
    public string? StaffSourceId { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportProduct
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SalePriceMinor { get; set; }
    public long BuyPriceMinor { get; set; }
    public string? Barcode { get; set; }
    public double? ReorderPoint { get; set; }
    public string? CategorySourceId { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportOwner
{
    public string SourceId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhonePrimary { get; set; }
    public string? PhoneSecondary { get; set; }
    public string? PhoneLandline { get; set; }
    public string? NationalId { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public int Gender { get; set; }
    public string? RegisteredUtc { get; set; }
    public string? Notes { get; set; }
    public List<ClinicImportPet> Pets { get; set; } = [];
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportPet
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Species { get; set; }
    public string? SpeciesName { get; set; }
    public string? Breed { get; set; }
    public int Gender { get; set; }
    public string? BirthDateUtc { get; set; }
    public bool IsNeutered { get; set; }
    public string? Color { get; set; }
    public string? MicrochipCode { get; set; }
    public string? MicrochipImplantUtc { get; set; }
    public string? BloodType { get; set; }
    public string? Insurance { get; set; }
    public int Status { get; set; }
    public string? RegisteredUtc { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportVisit
{
    public string SourceId { get; set; } = string.Empty;
    public string PetSourceId { get; set; } = string.Empty;
    public string? DoctorSourceId { get; set; }
    public string? VisitedUtc { get; set; }
    public string? Diagnosis { get; set; }
    public string? Prescription { get; set; }
    public string? NextVisitUtc { get; set; }
    public string? Reason { get; set; }
    public List<string> Symptoms { get; set; } = [];
    public List<string> Treatments { get; set; } = [];
    public List<string> Complaints { get; set; } = [];
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportVaccination
{
    public string SourceId { get; set; } = string.Empty;
    public string PetSourceId { get; set; } = string.Empty;
    public string? VaccineTypeSourceId { get; set; }
    public string? Name { get; set; }
    public string? Notes { get; set; }
    public string? AdministeredUtc { get; set; }
    public string? NextDueUtc { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportAntiparasite
{
    public string SourceId { get; set; } = string.Empty;
    public string PetSourceId { get; set; } = string.Empty;
    public string? AdministeredUtc { get; set; }
    public string? Notes { get; set; }
    public string? InternalParasite { get; set; }
    public string? ExternalParasite { get; set; }
    public string? NextDueUtc { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportLabResult
{
    public string SourceTable { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string PetSourceId { get; set; } = string.Empty;
    public string Kind { get; set; } = "other";
    public string? Title { get; set; }
    public string? PerformedUtc { get; set; }
    public string? NextVisitUtc { get; set; }
    public Dictionary<string, JsonElement> Values { get; set; } = [];
    public string? Notes { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportAppointment
{
    public string SourceId { get; set; } = string.Empty;
    public string? PetSourceId { get; set; }
    public string? PatientName { get; set; }
    public string? DoctorSourceId { get; set; }
    public string? StartUtc { get; set; }
    public string? Notes { get; set; }
    public string? Mobile { get; set; }
    public bool IsCheckedIn { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportInvoice
{
    public string SourceId { get; set; } = string.Empty;
    public string? PetSourceId { get; set; }
    public string? CustomerName { get; set; }
    public string? Mobile { get; set; }
    public string? IssuedUtc { get; set; }
    public string? Number { get; set; }
    public long TotalMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long InsurancePayMinor { get; set; }
    public string? InsurerSourceId { get; set; }
    public string? InsuranceNo { get; set; }
    public long TaxMinor { get; set; }
    public long PaidMinor { get; set; }
    public string? PaymentTypeSourceId { get; set; }
    public bool IsDebtor { get; set; }
    public string? Notes { get; set; }
    public List<ClinicImportInvoiceLine> Lines { get; set; } = [];
    public List<ClinicImportInvoicePayment> Payments { get; set; } = [];
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportInvoiceLine
{
    public string SourceId { get; set; } = string.Empty;
    public string Kind { get; set; } = "other";
    public string? ServiceSourceId { get; set; }
    public string? ProductSourceId { get; set; }
    public string? StaffSourceId { get; set; }
    public long UnitPriceMinor { get; set; }
    public double Quantity { get; set; }
    public long TotalMinor { get; set; }
    public long DiscountMinor { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportInvoicePayment
{
    public string SourceId { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public string? PaymentTypeSourceId { get; set; }
    public string? PaidUtc { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportStockReceipt
{
    public string SourceId { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string? ReceivedUtc { get; set; }
    public string? Seller { get; set; }
    public string? Notes { get; set; }
    public long TotalMinor { get; set; }
    public List<ClinicImportStockReceiptLine> Lines { get; set; } = [];
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportStockReceiptLine
{
    public string SourceId { get; set; } = string.Empty;
    public string ProductSourceId { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public long BuyPriceMinor { get; set; }
    public string? ExpireUtc { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}

public sealed class ClinicImportHoliday
{
    public string SourceId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? DateUtc { get; set; }
    public Dictionary<string, JsonElement>? Legacy { get; set; }
}
