using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>Local, explicit legacy preview only. No data resource is shipped in the product binary.</summary>
public sealed class ClinicBakPreviewData(
    AnimoraLocalDbContext db, ILocalTenantContext tenant, IClock clock,
    IOptions<LocalStoreOptions> options)
{
    public const string SourceSha256 = "ae4affdd3a5d4c116ed8e55a7f10227970d4af029a7ab7d77b0262149b733414";
    public const string SeedKey = "clinic-bak.preview.v1";
    public const string ProfilePrefix = "clinic-bak-";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string ProfileName(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > 32 * 1024 * 1024)
            throw new ArgumentException("Clinic preview file must exist and be at most 32 MiB.");
        using var stream = File.OpenRead(path);
        return ProfilePrefix + Convert.ToHexStringLower(SHA256.HashData(stream))[..16];
    }

    public Result Populate()
    {
        var valid = options.Value.Validate();
        if (valid.IsFailure) return valid;
        var path = options.Value.ClinicBakPreviewPath;
        if (path is null) return Result.Fail(ErrorCodes.ValidationFailed, "Clinic preview was not selected.");
        // The marker is checked by LocalSeeder. A missing marker on a nonempty store
        // is NOT permission to merge/overwrite any business data or existing credentials.
        if (db.Set<Owner>().Any() || db.Set<Pet>().Any() || db.Set<User>().Any() || db.Set<Product>().Any())
            return Result.Fail(ErrorCodes.ValidationFailed, "Clinic preview requires an empty isolated store.");
        try
        {
            using var stream = File.OpenRead(path);
            var data = JsonSerializer.Deserialize<Preview>(stream, JsonOptions);
            if (data is null || data.FormatVersion != 1 || data.SourceSha256 != SourceSha256 || data.Owners.Count == 0)
                return Result.Fail(ErrorCodes.ValidationFailed, "Invalid Clinic preview format or source fingerprint.");
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in data.Owners)
            {
                if (string.IsNullOrWhiteSpace(row.FullName) || row.Pets.Count == 0)
                    return Result.Fail(ErrorCodes.ValidationFailed, "Invalid owner in Clinic preview.");
                // The domain normalizes this explicit unknown label to an empty phone;
                // never invent a routable telephone number for missing source contacts.
                var phone = string.IsNullOrWhiteSpace(row.Phone) ? "ثبت نشده" : row.Phone;
                var ownerClock = new AtClock(Date(row.RegisteredDate) ?? clock.UtcNow);
                var owner = Owner.Register(tenant.TenantId, row.FullName, phone, ownerClock);
                owner.UpdateContactDetails(row.FullName, phone, row.SecondaryPhone,
                    nationalId: row.NationalId, email: row.Email, address: row.Address, city: row.City,
                    notes: "واردشده از Clinic.bak؛ کد پرونده‌ها: " + string.Join(", ", row.Pets.Select(p => p.Code)) +
                        (string.IsNullOrWhiteSpace(row.Phone) ? "؛ شمارهٔ تماس در منبع ثبت نشده" : ""));
                db.Add(owner);
                foreach (var source in row.Pets)
                {
                    if (!codes.Add(source.Code) || string.IsNullOrWhiteSpace(source.Code) ||
                        string.IsNullOrWhiteSpace(source.Name) || !Enum.IsDefined((PetSpecies)source.Species))
                        return Result.Fail(ErrorCodes.ValidationFailed, "Invalid or duplicate patient in Clinic preview.");
                    var petClock = new AtClock(Date(source.RegisteredDate) ?? clock.UtcNow);
                    var pet = Pet.Register(tenant.TenantId, owner.Id, source.Name,
                        (PetSpecies)source.Species, petClock, birthDateUtc: Date(source.BirthDate), breed: source.Breed);
                    pet.UpdateProfile(source.Name, source.Breed, isNeutered: source.IsNeutered, color: source.Color);
                    if (source.IsInactive) pet.SetStatus(PetStatus.Inactive);
                    // Implant date is mandatory in the domain. Keep an undated source
                    // microchip in the original-record note instead of inventing a date.
                    if (!string.IsNullOrWhiteSpace(source.Microchip) && Date(source.ImplantDate) is { } implanted)
                        pet.SetMicrochip(source.Microchip, implanted);
                    db.Add(pet);
                    foreach (var entry in source.History)
                    {
                        var occurred = Date(entry.Date);
                        db.Add(new MedicalHistoryEntry(tenant.TenantId, pet.Id, MedicalHistoryKind.Note,
                            entry.Title, occurred ?? clock.UtcNow, "legacy-import", clock,
                            detail: (occurred is null ? "تاریخ منبع نامعتبر/نامعلوم؛ زمان نمایش، زمان ورود داده است.\n" : "") + entry.Detail,
                            sourceEntityId: entry.SourceId, sourceEntityType: "LegacyClinic:" + entry.SourceTable));
                    }
                }
            }
            return Result.Ok();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Don't log the exception body: it can contain patient data or paths.
            return Result.Fail(ErrorCodes.ValidationFailed, "Clinic preview data could not be read or validated.");
        }
    }

    internal static DateTimeOffset? Date(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Trim().Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) || !int.TryParse(parts[2], out var day) || year is < 1200 or > 1600)
            return null;
        try
        {
            var local = new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private sealed class AtClock(DateTimeOffset at) : IClock { public DateTimeOffset UtcNow => at; }
    private sealed class Preview
    {
        public int FormatVersion { get; set; }
        public string SourceSha256 { get; set; } = "";
        public List<OwnerRow> Owners { get; set; } = [];
    }
    private sealed class OwnerRow
    {
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string? SecondaryPhone { get; set; }
        public string? NationalId { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? RegisteredDate { get; set; }
        public List<PetRow> Pets { get; set; } = [];
    }
    private sealed class PetRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int Species { get; set; }
        public string? Breed { get; set; }
        public string? Color { get; set; }
        public string? BirthDate { get; set; }
        public string? RegisteredDate { get; set; }
        public bool IsNeutered { get; set; }
        public bool IsInactive { get; set; }
        public string? Microchip { get; set; }
        public string? ImplantDate { get; set; }
        public List<HistoryRow> History { get; set; } = [];
    }
    private sealed class HistoryRow
    {
        public string SourceTable { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string? Date { get; set; }
    }
}
