using Animora.Application.Abstractions;
using Animora.Domain.Boarding;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Options;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>
/// Idempotent first-run reference seeding of the local store (CONVENTIONS.md §10.3). Each logical block
/// checks its <see cref="SeedMarker"/> (unique-indexed lookup, never a business-table scan) and inserts its
/// rows plus the marker in one unit-of-work transaction. A rerun skips applied blocks; a partial run
/// resumes at the first missing block; a concurrent loser hits a unique index, rolls back, and succeeds
/// once it sees the winner's marker. Never seeds demo owners/pets/invoices, credentials or secrets.
/// </summary>
/// <remarks>
/// Static catalogs — <see cref="ExpenseCategories"/> (9), <c>AppointmentTypeCatalog</c> (7) and
/// <c>VaccineSchedule</c> (6) — are deliberately <b>not</b> persisted: the domain types are the single
/// authority, no aggregate references them by foreign key (rows store the opaque code), and a DB copy
/// would only create a second truth that can drift. No rows for them exist in the store.
/// </remarks>
public sealed class LocalSeeder(
    AnimoraLocalDbContext db, IUnitOfWork unitOfWork, ILocalTenantContext tenant, IClock clock,
    ISeedPeriodCodeProvider periodCodes, PatientsSampleData sampleData,
    ReceptionSampleData receptionSampleData, ClinicalSampleData clinicalSampleData,
    SchedulingSampleData schedulingSampleData, BoardingSampleData boardingSampleData,
    IOptions<LocalStoreOptions> storeOptions)
{
    public const string TenantKey = "identity.tenant";
    public const string PermissionsKey = "identity.permissions";
    public const string RolesKey = "identity.roles";
    public const string ChartKey = "finance.chart";
    public const string PeriodKey = "finance.period.initial";
    public const string UnitsKey = "inventory.units";
    public const string CategoriesKey = "inventory.categories";
    public const string RoomsKey = "boarding.rooms";
    public const string ServicesKey = "boarding.services";
    public const string SmsTemplatesKey = "vaccination.sms.templates";
    private const int SchemaVersion = 1;

    private static readonly (string Code, int Decimals)[] Units =
        [("PCS", 0), ("BOX", 0), ("ML", 2), ("MG", 2), ("G", 2), ("KG", 3), ("DOSE", 0)];

    private static readonly (string Code, string Name)[] Categories =
        [("MED", "Medicines"), ("VAC", "Vaccines"), ("FOOD", "Food"), ("ACC", "Accessories"), ("SRV", "Services")];

    public async Task<Result> SeedAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId != Tenant.LocalDefaultId)
            return Result.Fail(ErrorCodes.ValidationFailed, "Local seeding requires the local default tenant context.");

        var tenantId = tenant.TenantId;
        var now = clock.UtcNow;
        (string Key, Func<Result> Add)[] blocks =
        [
            (TenantKey, () => Add(Tenant.CreateLocalDefault("Animora Clinic"))),
            (PermissionsKey, () => AddRange(PermissionCodes.CreateCatalog())),
            (RolesKey, () => AddRange(Enum.GetValues<StaffRole>().Select(r => RoleDefinition.CreateSystem(tenantId, r, clock)))),
            (ChartKey, () => AddAll(ChartOfAccounts.Default.Select(e =>
                LedgerAccount.Create(tenantId, e.Code, e.Name, e.Type, now, isPostable: e.IsPostable)))),
            (PeriodKey, () =>
            {
                var period = periodCodes.For(now);
                return AddAll([AccountingPeriod.Create(tenantId, period.PeriodCode, period.StartUtc, period.EndUtc)]);
            }),
            (UnitsKey, () => AddAll(Units.Select(u =>
                UnitOfMeasure.Create(tenantId, u.Code, "unit." + u.Code.ToLowerInvariant(), u.Decimals)))),
            (CategoriesKey, () => AddAll(Categories.Select((c, i) =>
                ProductCategory.Create(tenantId, c.Code, c.Name, sortOrder: i)))),
            (RoomsKey, () => AddAll(Enumerable.Range(1, 8).Select(RoomFor))),
            (ServicesKey, () => AddRange(new[]
            {
                BoardingServiceDef.Create(tenantId, "WALK", "Extra walk", Animora.Domain.ValueObjects.Money.Irt(60_000), BoardingServiceBilling.PerDay),
                BoardingServiceDef.Create(tenantId, "DIET", "Special diet", Animora.Domain.ValueObjects.Money.Irt(90_000), BoardingServiceBilling.PerDay),
                BoardingServiceDef.Create(tenantId, "BATH", "Bath", Animora.Domain.ValueObjects.Money.Irt(150_000), BoardingServiceBilling.PerStay),
                BoardingServiceDef.Create(tenantId, "GROOM", "Grooming", Animora.Domain.ValueObjects.Money.Irt(180_000), BoardingServiceBilling.PerStay),
                BoardingServiceDef.Create(tenantId, "PLAY", "Play session", Animora.Domain.ValueObjects.Money.Irt(50_000), BoardingServiceBilling.PerUse),
                BoardingServiceDef.Create(tenantId, "CARE", "Extra care", Animora.Domain.ValueObjects.Money.Irt(75_000), BoardingServiceBilling.PerUse),
            })),
            (SmsTemplatesKey, () => AddRange(new[]
            {
                SmsTemplateRow.Create("due-reminder", tenantId, "Due reminder", "SMS",
                    "{owner}, {pet} is due for {vaccine} on {due}."),
                SmsTemplateRow.Create("overdue-reminder", tenantId, "Overdue reminder", "SMS",
                    "{owner}, {pet} is overdue for {vaccine} since {due}."),
                SmsTemplateRow.Create("confirmation", tenantId, "Vaccination confirmation", "SMS",
                    "{owner}, {pet}'s {vaccine} appointment is confirmed for {due}."),
                SmsTemplateRow.Create("booster", tenantId, "Booster reminder", "SMS",
                    "{owner}, {pet}'s {vaccine} booster is due on {due}."),
            })),
        ];

        foreach (var (key, add) in blocks)
        {
            var outcome = await RunBlockAsync(key, add, ct);
            if (outcome.IsFailure) return outcome;
        }

        // C01: optional, deterministic sample owners/pets so the Patients screens are testable on a fresh
        // store. Behind LocalStoreOptions.SeedSampleData, in its own idempotent marker-guarded block.
        if (storeOptions.Value.SeedSampleData)
        {
            var sampled = await RunBlockAsync(PatientsSampleData.SampleKey, () =>
            {
                if (!sampleData.AlreadySeeded()) sampleData.Populate();
                return Result.Ok();
            }, ct);
            if (sampled.IsFailure) return sampled;
            var reception = await RunBlockAsync(ReceptionSampleData.SampleKey, () =>
            {
                if (!receptionSampleData.AlreadySeeded()) receptionSampleData.Populate();
                return Result.Ok();
            }, ct);
            if (reception.IsFailure) return reception;
            var clinical = await RunBlockAsync(ClinicalSampleData.SampleKey, () =>
            {
                if (!clinicalSampleData.AlreadySeeded()) clinicalSampleData.Populate();
                return Result.Ok();
            }, ct);
            if (clinical.IsFailure) return clinical;
            var scheduling = await RunBlockAsync(SchedulingSampleData.SampleKey, () =>
            {
                if (!schedulingSampleData.AlreadySeeded()) schedulingSampleData.Populate();
                return Result.Ok();
            }, ct);
            if (scheduling.IsFailure) return scheduling;
            var boarding = await RunBlockAsync(BoardingSampleData.SampleKey, () =>
            {
                if (!boardingSampleData.AlreadySeeded()) boardingSampleData.Populate();
                return Result.Ok();
            }, ct);
            if (boarding.IsFailure) return boarding;
        }
        return Result.Ok();

        Result<Room> RoomFor(int n) => Room.Create(tenantId, $"R-{n:D2}", $"Room {n:D2}",
            n switch { 7 => RoomType.Isolation, 8 => RoomType.Vip, _ => RoomType.Standard }, capacity: 1);
    }

    private async Task<Result> RunBlockAsync(string key, Func<Result> addRows, CancellationToken ct)
    {
        if (await IsAppliedAsync(key, ct)) return Result.Ok();
        var outcome = await unitOfWork.ExecuteAsync(async token =>
        {
            if (await IsAppliedAsync(key, token)) return Result<bool>.Ok(false);
            var added = addRows();
            if (added.IsFailure) return Result<bool>.Fail(added.Error, added.Detail);
            db.Add(SeedMarker.Create(key, SchemaVersion, clock.UtcNow));
            return Result<bool>.Ok(true);
        }, ct);
        if (outcome.IsSuccess) return Result.Ok();
        // Concurrent seeder won the unique index: its committed marker makes this block done.
        return await IsAppliedAsync(key, ct) ? Result.Ok() : outcome.ToResult();
    }

    private Task<bool> IsAppliedAsync(string key, CancellationToken ct) =>
        db.Set<SeedMarker>().AsNoTracking().AnyAsync(m => m.SeedKey == key, ct);

    private Result Add(object entity)
    {
        db.Add(entity);
        return Result.Ok();
    }

    private Result AddRange(IEnumerable<object> entities)
    {
        db.AddRange(entities);
        return Result.Ok();
    }

    /// <summary>Adds every created row, or none: the first failed domain factory result is propagated.</summary>
    private Result AddAll<T>(IEnumerable<Result<T>> results) where T : class
    {
        var rows = new List<T>();
        foreach (var result in results)
        {
            if (result.IsFailure) return Result.Fail(result.Error, result.Detail);
            rows.Add(result.Value!);
        }
        db.AddRange(rows);
        return Result.Ok();
    }
}
