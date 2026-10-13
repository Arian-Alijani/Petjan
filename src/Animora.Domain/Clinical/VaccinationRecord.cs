using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Domain.Clinical;

/// <summary>Where a vaccination stands relative to its next due date.</summary>
public enum VaccinationState
{
    /// <summary>Given, and the next dose is not close yet.</summary>
    Administered = 0,

    /// <summary>The next dose falls inside the reminder window.</summary>
    DueSoon = 1,

    /// <summary>The next dose is past its due date.</summary>
    Overdue = 2,

    /// <summary>The series was deliberately abandoned; no further dose is expected.</summary>
    Skipped = 3,
}

/// <summary>
/// One administered vaccine dose, and the due date it implies.
/// </summary>
/// <remarks>
/// <see cref="State"/> is <b>derived</b>, not stored-and-drifted: a stored "DueSoon" silently becomes
/// a lie the next morning, especially on a device that was offline for a week. The persisted
/// <see cref="State"/> only distinguishes a live series from a <see cref="VaccinationState.Skipped"/>
/// one; every screen reads <see cref="StateAt"/>.
/// </remarks>
public sealed class VaccinationRecord : AggregateRoot
{
    /// <summary>How far ahead of the due date a dose is reported as <see cref="VaccinationState.DueSoon"/>.</summary>
    public static readonly TimeSpan DueSoonWindow = TimeSpan.FromDays(30);

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private VaccinationRecord()
    {
    }

    /// <summary>The vaccinated patient.</summary>
    public string PetId { get; private set; } = string.Empty;

    /// <summary>Protocol code, e.g. <c>"DHPP"</c>; the key into <see cref="VaccineSchedule"/>.</summary>
    public string VaccineCode { get; private set; } = string.Empty;

    /// <summary>Commercial vaccine name as printed on the vial.</summary>
    public string VaccineName { get; private set; } = string.Empty;

    /// <summary>1-based position of this dose in the series.</summary>
    public int DoseNumber { get; private set; }

    /// <summary>Doses in the whole series; <see langword="null"/> when the protocol is open-ended.</summary>
    public int? TotalDoses { get; private set; }

    /// <summary>When the dose was given, UTC.</summary>
    public DateTimeOffset AdministeredUtc { get; private set; }

    /// <summary>The staff member who administered it.</summary>
    public string AdministeredByUserId { get; private set; } = string.Empty;

    /// <summary>Vial batch/lot number, kept for traceability and recalls.</summary>
    public string? BatchNumber { get; private set; }

    /// <summary>When the next dose is due, UTC — computed by the due-date rule.</summary>
    public DateTimeOffset? NextDueUtc { get; private set; }

    /// <summary>
    /// Stored lifecycle marker. <see cref="VaccinationState.Administered"/> for a live series and
    /// <see cref="VaccinationState.Skipped"/> once abandoned; the time-relative values come from
    /// <see cref="StateAt"/>.
    /// </summary>
    public VaccinationState State { get; private set; } = VaccinationState.Administered;

    /// <summary>The encounter the dose was given in, when there was one.</summary>
    public string? VisitId { get; private set; }

    /// <summary>Reaction notes and other remarks.</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Records an administered dose and derives <see cref="NextDueUtc"/> as
    /// <c>AdministeredUtc + VaccineSchedule.IntervalFor(code, doseNumber)</c>. When this dose is the
    /// last of the series the interval is the annual booster, so the patient never falls off the
    /// reminder list. Raises <see cref="VaccinationAdministeredEvent"/>.
    /// </summary>
    public static Result<VaccinationRecord> Administer(
        string tenantId,
        string petId,
        string vaccineCode,
        string vaccineName,
        int doseNumber,
        DateTimeOffset administeredUtc,
        string administeredByUserId,
        IClock clock,
        int? totalDoses = null,
        string? batchNumber = null,
        string? visitId = null,
        string? notes = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaccineCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaccineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(administeredByUserId);
        ArgumentNullException.ThrowIfNull(clock);

        if (doseNumber < 1 || (totalDoses is { } total && (total < 1 || doseNumber > total)))
        {
            return Result<VaccinationRecord>.Fail(
                ErrorCodes.VaccineDoseInvalid,
                $"DoseNumber {doseNumber} is not valid for a series of {totalDoses?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} doses.");
        }

        var code = vaccineCode.Trim().ToUpperInvariant();
        var nextDue = administeredUtc.AddDays(VaccineSchedule.IntervalFor(code, doseNumber));

        var now = clock.UtcNow;
        var record = new VaccinationRecord
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(now) : id,
            TenantId = tenantId,
            PetId = petId,
            VaccineCode = code,
            VaccineName = vaccineName.Trim(),
            DoseNumber = doseNumber,
            TotalDoses = totalDoses ?? VaccineSchedule.TotalDosesFor(code),
            AdministeredUtc = administeredUtc,
            AdministeredByUserId = administeredByUserId,
            BatchNumber = Clean(batchNumber),
            NextDueUtc = nextDue,
            State = VaccinationState.Administered,
            VisitId = Clean(visitId),
            Notes = Clean(notes),
        };

        record.Raise(new VaccinationAdministeredEvent(
            IdGenerator.NewId(now), tenantId, now, record.Id, petId, code, nextDue));
        return Result<VaccinationRecord>.Ok(record);
    }

    /// <summary>
    /// The state this record has at <paramref name="nowUtc"/>: <see cref="VaccinationState.Overdue"/>
    /// once the due date has passed, <see cref="VaccinationState.DueSoon"/> inside
    /// <see cref="DueSoonWindow"/>, otherwise <see cref="VaccinationState.Administered"/>. A skipped
    /// series stays skipped, and a record with no due date is simply administered.
    /// </summary>
    public VaccinationState StateAt(DateTimeOffset nowUtc)
    {
        if (State == VaccinationState.Skipped || NextDueUtc is not { } due)
        {
            return State == VaccinationState.Skipped ? VaccinationState.Skipped : VaccinationState.Administered;
        }

        if (due < nowUtc)
        {
            return VaccinationState.Overdue;
        }

        return due - nowUtc <= DueSoonWindow ? VaccinationState.DueSoon : VaccinationState.Administered;
    }

    /// <summary>Abandons the remaining series: the record stops producing reminders.</summary>
    public void Skip(string? reason = null)
    {
        State = VaccinationState.Skipped;
        NextDueUtc = null;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Notes = reason.Trim();
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
