using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Domain.Clinical;

/// <summary>What a plan administers.</summary>
public enum TreatmentItemType
{
    /// <summary>A vaccine.</summary>
    Vaccine = 0,

    /// <summary>A medication.</summary>
    Medication = 1,
}

/// <summary>Where / by whom each occurrence of a plan is carried out.</summary>
public enum TreatmentKind
{
    /// <summary>The patient must come to the clinic.</summary>
    ClinicVisit = 0,

    /// <summary>The owner gives it at home (an owner reminder applies).</summary>
    HomeByOwner = 1,

    /// <summary>The patient is boarded and the clinic gives it there.</summary>
    BoardingAdminister = 2,
}

/// <summary>Lifecycle of a <see cref="TreatmentPlan"/>.</summary>
public enum TreatmentPlanState
{
    /// <summary>At least one occurrence is still pending.</summary>
    Active = 0,

    /// <summary>Every occurrence was given or skipped.</summary>
    Completed = 1,

    /// <summary>The plan was abandoned; its pending occurrences were skipped.</summary>
    Cancelled = 2,
}

/// <summary>State of one <see cref="TreatmentOccurrence"/>.</summary>
public enum TreatmentOccurrenceState
{
    /// <summary>Still to be given.</summary>
    Pending = 0,

    /// <summary>Given.</summary>
    Done = 1,

    /// <summary>Will not be given (plan cancelled).</summary>
    Skipped = 2,
}

/// <summary>A new plan together with the occurrences its schedule produced; persisted as one unit.</summary>
public sealed record TreatmentPlanDraft(TreatmentPlan Plan, IReadOnlyList<TreatmentOccurrence> Occurrences);

/// <summary>
/// A medication or vaccine scheduled for one patient on one or more days — the source of the
/// «دارو و واکسن» calendar. Distinct from <see cref="VaccinationRecord"/> (an administered dose): a plan is
/// the <i>intent</i>, each <see cref="TreatmentOccurrence"/> is one day of it.
/// </summary>
/// <remarks>
/// The aggregate keeps no child collection (the bounded occurrence set is loaded on demand), so a plan with
/// hundreds of days never inflates a read of the plan itself. The schedule is expanded once, at creation.
/// </remarks>
public sealed class TreatmentPlan : AggregateRoot
{
    /// <summary>Longest accepted title.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Longest accepted dosage text.</summary>
    public const int MaxDosageLength = 200;

    /// <summary>Longest accepted note.</summary>
    public const int MaxNotesLength = 1000;

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private TreatmentPlan()
    {
    }

    /// <summary>The treated patient.</summary>
    public string PetId { get; private set; } = string.Empty;

    /// <summary>Vaccine or medication.</summary>
    public TreatmentItemType ItemType { get; private set; }

    /// <summary>Clinic visit, owner at home, or boarding.</summary>
    public TreatmentKind Kind { get; private set; }

    /// <summary>Name of the vaccine / medication.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Catalog code when this is a catalogued vaccine.</summary>
    public string? VaccineCode { get; private set; }

    /// <summary>Dose / strength text, e.g. «۵ میلی‌گرم، دو بار در روز».</summary>
    public string? Dosage { get; private set; }

    /// <summary>Free-form remarks.</summary>
    public string? Notes { get; private set; }

    /// <summary>The owner is to be reminded (meaningful for <see cref="TreatmentKind.HomeByOwner"/> only).</summary>
    public bool NotifyOwner { get; private set; }

    /// <summary>How the dates were produced (informational; the occurrences are the truth).</summary>
    public TreatmentScheduleType ScheduleType { get; private set; }

    /// <summary>The staff member who created the plan.</summary>
    public string CreatedByUserId { get; private set; } = string.Empty;

    /// <summary>When the plan was created.</summary>
    public DateTimeOffset CreatedUtc { get; private set; }

    /// <summary>Lifecycle marker.</summary>
    public TreatmentPlanState State { get; private set; } = TreatmentPlanState.Active;

    /// <summary>Creates a plan and expands <paramref name="schedule"/> into its occurrences.</summary>
    /// <param name="today">The clinic-local day, used to bound the schedule.</param>
    public static Result<TreatmentPlanDraft> Create(
        string tenantId,
        string petId,
        TreatmentItemType itemType,
        TreatmentKind kind,
        string title,
        TreatmentSchedule schedule,
        DateOnly today,
        string createdByUserId,
        IClock clock,
        string? vaccineCode = null,
        string? dosage = null,
        string? notes = null,
        bool notifyOwner = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(clock);

        var invalid = Validate(itemType, kind, title, dosage, notes);
        if (invalid is not null)
        {
            return Result<TreatmentPlanDraft>.Fail(ErrorCodes.ValidationFailed, invalid);
        }

        var days = TreatmentScheduleExpander.Expand(schedule, today);
        if (days.IsFailure)
        {
            return Result<TreatmentPlanDraft>.Fail(days.Error, days.Detail);
        }

        var now = clock.UtcNow;
        var plan = new TreatmentPlan
        {
            Id = IdGenerator.NewId(now),
            TenantId = tenantId,
            PetId = petId,
            ItemType = itemType,
            Kind = kind,
            Title = title.Trim(),
            VaccineCode = itemType == TreatmentItemType.Vaccine ? Clean(vaccineCode)?.ToUpperInvariant() : null,
            Dosage = Clean(dosage),
            Notes = Clean(notes),
            NotifyOwner = notifyOwner && kind == TreatmentKind.HomeByOwner,
            ScheduleType = schedule.Type,
            CreatedByUserId = createdByUserId,
            CreatedUtc = now,
            State = TreatmentPlanState.Active,
        };

        var occurrences = days.Value!
            .Select((day, index) => TreatmentOccurrence.Create(tenantId, plan.Id, day, index, now))
            .ToArray();
        return Result<TreatmentPlanDraft>.Ok(new TreatmentPlanDraft(plan, occurrences));
    }

    /// <summary>
    /// Edits the descriptive fields of an active plan. The schedule is intentionally immutable: to change the
    /// dates, cancel the plan and create a new one.
    /// </summary>
    public Result Update(TreatmentKind kind, string title, string? dosage, string? notes, bool notifyOwner)
    {
        if (State != TreatmentPlanState.Active)
        {
            return Result.Fail(ErrorCodes.TreatmentPlanNotActive);
        }

        var invalid = Validate(ItemType, kind, title, dosage, notes);
        if (invalid is not null)
        {
            return Result.Fail(ErrorCodes.ValidationFailed, invalid);
        }

        Kind = kind;
        Title = title.Trim();
        Dosage = Clean(dosage);
        Notes = Clean(notes);
        NotifyOwner = notifyOwner && kind == TreatmentKind.HomeByOwner;
        return Result.Ok();
    }

    /// <summary>Abandons an active plan. The caller skips its pending occurrences in the same unit of work.</summary>
    public Result Cancel()
    {
        if (State != TreatmentPlanState.Active)
        {
            return Result.Fail(ErrorCodes.TreatmentPlanNotActive);
        }

        State = TreatmentPlanState.Cancelled;
        return Result.Ok();
    }

    /// <summary>Marks the plan completed once no occurrence is pending. Idempotent for an already-completed plan.</summary>
    public Result Complete()
    {
        if (State == TreatmentPlanState.Cancelled)
        {
            return Result.Fail(ErrorCodes.TreatmentPlanNotActive);
        }

        State = TreatmentPlanState.Completed;
        return Result.Ok();
    }

    private static string? Validate(TreatmentItemType itemType, TreatmentKind kind, string? title, string? dosage, string? notes)
    {
        if (!Enum.IsDefined(itemType) || !Enum.IsDefined(kind))
        {
            return "Unknown item type or kind.";
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return "A treatment plan needs a title.";
        }

        if (title.Trim().Length > MaxTitleLength || (dosage?.Trim().Length ?? 0) > MaxDosageLength
            || (notes?.Trim().Length ?? 0) > MaxNotesLength)
        {
            return "Title, dosage or notes exceed the allowed length.";
        }

        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// One day of a <see cref="TreatmentPlan"/>: due on <see cref="DueDay"/>, later given or skipped. Carries no
/// navigation property — it references its plan by id only.
/// </summary>
public sealed class TreatmentOccurrence : SyncableEntity
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private TreatmentOccurrence()
    {
    }

    /// <summary>The owning plan.</summary>
    public string PlanId { get; private set; } = string.Empty;

    /// <summary>The clinic-local calendar day it is due (stored as <c>yyyy-MM-dd</c>, never shifted by a time zone).</summary>
    public DateOnly DueDay { get; private set; }

    /// <summary>Position inside the plan (0-based, by day).</summary>
    public int SortOrder { get; private set; }

    /// <summary>Pending, done or skipped.</summary>
    public TreatmentOccurrenceState State { get; private set; } = TreatmentOccurrenceState.Pending;

    /// <summary>When it was given.</summary>
    public DateTimeOffset? DoneUtc { get; private set; }

    /// <summary>Who gave it.</summary>
    public string? DoneByUserId { get; private set; }

    /// <summary>Creates a pending occurrence.</summary>
    public static TreatmentOccurrence Create(string tenantId, string planId, DateOnly dueDay, int sortOrder, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);

        return new TreatmentOccurrence
        {
            Id = IdGenerator.NewId(at),
            TenantId = tenantId,
            PlanId = planId,
            DueDay = dueDay,
            SortOrder = sortOrder,
            State = TreatmentOccurrenceState.Pending,
        };
    }

    /// <summary>Records that the dose was given. Refused when already given or skipped.</summary>
    public Result MarkDone(DateTimeOffset nowUtc, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (State != TreatmentOccurrenceState.Pending)
        {
            return Result.Fail(ErrorCodes.TreatmentOccurrenceAlreadyDone);
        }

        State = TreatmentOccurrenceState.Done;
        DoneUtc = nowUtc;
        DoneByUserId = userId;
        return Result.Ok();
    }

    /// <summary>Withdraws a pending occurrence (its plan was cancelled). A given dose is never rewritten.</summary>
    public Result Skip()
    {
        if (State == TreatmentOccurrenceState.Done)
        {
            return Result.Fail(ErrorCodes.TreatmentOccurrenceAlreadyDone);
        }

        State = TreatmentOccurrenceState.Skipped;
        return Result.Ok();
    }
}
