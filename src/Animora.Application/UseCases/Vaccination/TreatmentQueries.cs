using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Time;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Vaccination;

/// <summary>
/// The scheduled medication / vaccine days inside a window of at most <see cref="MaxRangeDays"/> days
/// (a month view plus its leading/trailing weeks), optionally narrowed by kind, item type and patient.
/// <para>
/// This is the single read path for «what is due»: it returns both the days of a treatment plan
/// (<see cref="TreatmentCalendarRowSource.PlanDay"/>) and the next-dose reminders derived from administered
/// vaccination records (<see cref="TreatmentCalendarRowSource.VaccineBooster"/>). The «دارو و واکسن» screen and
/// the appointment calendar both consume it, so a dose can never appear in one and be missing from the other.
/// </para>
/// </summary>
[RequiresPermission(PermissionCodes.VaccinationRead)]
public sealed record TreatmentCalendarQuery(DateOnly From, DateOnly To, IReadOnlyCollection<TreatmentKind>? Kinds = null,
    TreatmentItemType? ItemType = null, string? PetId = null) : IQuery<IReadOnlyList<TreatmentCalendarRow>>
{
    /// <summary>Widest accepted window, inclusive.</summary>
    public const int MaxRangeDays = 62;

    /// <summary>Hard row cap; a window that exceeds it is truncated, never unbounded.</summary>
    public const int MaxRows = 2000;
}

public sealed class TreatmentCalendarQueryHandler(
    ITreatmentPlanQueries queries, IVaccinationQueries vaccinations, IClinicTimeZoneProvider zones)
    : IQueryHandler<TreatmentCalendarQuery, IReadOnlyList<TreatmentCalendarRow>>
{
    public async Task<Result<IReadOnlyList<TreatmentCalendarRow>>> HandleAsync(TreatmentCalendarQuery q,
        QueryContext context, CancellationToken ct)
    {
        if (q.To < q.From || q.To.DayNumber - q.From.DayNumber + 1 > TreatmentCalendarQuery.MaxRangeDays)
            return Result<IReadOnlyList<TreatmentCalendarRow>>.Fail(ErrorCodes.AppointmentRangeTooWide);

        var kinds = q.Kinds is { Count: > 0 } ? q.Kinds : null;
        var petId = string.IsNullOrWhiteSpace(q.PetId) ? null : q.PetId;
        var rows = (await queries.GetCalendarAsync(q.From, q.To, kinds, q.ItemType, petId,
            TreatmentCalendarQuery.MaxRows, ct)).ToList();

        // A next-dose reminder is a vaccine and is always carried out at the clinic, so the same filters that
        // apply to planned days apply to it: «دارو» only, or «منزل/پانسیون» only, must not surface it.
        if (q.ItemType != TreatmentItemType.Medication && (kinds is null || kinds.Contains(TreatmentKind.ClinicVisit)))
        {
            rows.AddRange(await GetBoosterRowsAsync(q, petId, rows, ct));
        }

        // OrderBy is stable, so the plan rows keep the order the store returned and the reminders of a day follow them.
        return Result<IReadOnlyList<TreatmentCalendarRow>>.Ok(rows
            .OrderBy(r => r.DueDay)
            .Take(TreatmentCalendarQuery.MaxRows)
            .ToArray());
    }

    /// <summary>
    /// The next-dose reminders of administered vaccinations inside the window, as calendar rows. They are derived
    /// from <c>VaccinationRecord.NextDueUtc</c> — never stored — so the «دارو و واکسن» screen and the appointment
    /// calendar show one and the same set of due doses.
    /// </summary>
    private async Task<IReadOnlyList<TreatmentCalendarRow>> GetBoosterRowsAsync(TreatmentCalendarQuery q,
        string? petId, List<TreatmentCalendarRow> planned, CancellationToken ct)
    {
        var tz = await zones.GetClinicTimeZoneAsync(ct).ConfigureAwait(false);
        var (startTicks, _) = ClinicCalendar.DayRangeUtc(q.From, tz);
        var (_, endTicks) = ClinicCalendar.DayRangeUtc(q.To, tz);

        var boosters = await vaccinations.GetPendingBoostersAsync(
            new DateTimeOffset(startTicks, TimeSpan.Zero), new DateTimeOffset(endTicks, TimeSpan.Zero),
            TreatmentCalendarQuery.MaxRows, ct).ConfigureAwait(false);

        // A vaccine that is already planned for the same patient on the same day is one due dose, not two.
        var scheduled = planned
            .Where(r => r.ItemType == TreatmentItemType.Vaccine && r.State != TreatmentOccurrenceState.Done)
            .Select(r => (r.PetId, r.DueDay, Normalize(r.Title)))
            .ToHashSet();

        var rows = new List<TreatmentCalendarRow>(boosters.Count);
        foreach (var b in boosters)
        {
            var day = ClinicCalendar.LocalDay(b.NextDueUtc.UtcTicks, tz);
            if (day < q.From || day > q.To)
                continue;
            if (petId is not null && !string.Equals(petId, b.PetId, StringComparison.Ordinal))
                continue;
            if (scheduled.Contains((b.PetId, day, Normalize(b.VaccineName))))
                continue;

            rows.Add(new TreatmentCalendarRow(
                b.Id, PlanId: string.Empty, day, b.PetId, b.PetName, b.Species, b.OwnerName,
                b.VaccineName, Dosage: null, TreatmentItemType.Vaccine, TreatmentKind.ClinicVisit,
                TreatmentOccurrenceState.Pending, DoneUtc: null, NotifyOwner: false,
                TreatmentCalendarRowSource.VaccineBooster, b.VaccineName, b.DoseNumber));
        }

        return rows;
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}

/// <summary>The treatment plans of one patient, newest first (bounded).</summary>
[RequiresPermission(PermissionCodes.VaccinationRead)]
public sealed record TreatmentPlansForPetQuery(string PetId, int Take = 50) : IQuery<IReadOnlyList<TreatmentPlanRow>>;

public sealed class TreatmentPlansForPetQueryHandler(ITreatmentPlanQueries queries)
    : IQueryHandler<TreatmentPlansForPetQuery, IReadOnlyList<TreatmentPlanRow>>
{
    public async Task<Result<IReadOnlyList<TreatmentPlanRow>>> HandleAsync(TreatmentPlansForPetQuery q,
        QueryContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q.PetId))
            return Result<IReadOnlyList<TreatmentPlanRow>>.Fail(ErrorCodes.ValidationFailed);
        return Result<IReadOnlyList<TreatmentPlanRow>>.Ok(
            await queries.GetPlansForPetAsync(q.PetId, Math.Clamp(q.Take, 1, 200), ct));
    }
}
