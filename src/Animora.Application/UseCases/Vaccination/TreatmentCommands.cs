using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Time;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Vaccination;

internal static class TreatmentRules
{
    public static ValueTask<IReadOnlyList<ValidationFailure>> Check(params ValidationFailure?[] checks) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(checks.OfType<ValidationFailure>().ToArray());

    public static ValidationFailure? Enum<T>(T value, string field) where T : struct, System.Enum =>
        System.Enum.IsDefined(value) ? null : new(field, Rules.RangeCode);

    public static DateOnly Today(IClock clock, IClinicTimeZoneProvider zones) =>
        ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zones.GetClinicTimeZone());
}

/// <summary>Writes the vaccination-ledger entries of plan lifecycle events (registered / edited / cancelled / moved).</summary>
internal static class TreatmentHistory
{
    /// <summary>Vaccines belong to the vaccination timeline, medications to the prescription timeline.</summary>
    public static MedicalHistoryKind KindOf(TreatmentPlan plan) =>
        plan.ItemType == TreatmentItemType.Vaccine ? MedicalHistoryKind.Vaccination : MedicalHistoryKind.Prescription;

    public static void Record(IMedicalHistoryRepository history, CommandContext context, IClock clock, TreatmentPlan plan,
        string title, string detail) =>
        history.Add(new MedicalHistoryEntry(context.TenantId, plan.PetId, KindOf(plan), title, clock.UtcNow, context.UserId,
            clock, detail, plan.Id, nameof(TreatmentPlan)));
}

/// <summary>Schedules a medication / vaccine for a patient. Returns the new plan id.</summary>
[RequiresPermission(PermissionCodes.VaccinationWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VaccinationTreatmentCreate)]
public sealed record CreateTreatmentPlanCommand(string PetId, TreatmentItemType ItemType, TreatmentKind Kind,
    string Title, TreatmentSchedule Schedule, string? VaccineCode = null, string? Dosage = null,
    string? Notes = null, bool NotifyOwner = false, int RescheduledDayCount = 0) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VaccinationTreatmentCreate;
    public string AggregateTypeName => nameof(TreatmentPlan);
    public string? AggregateIdOrNull => null;
}

public sealed class CreateTreatmentPlanCommandValidator : IValidator<CreateTreatmentPlanCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateTreatmentPlanCommand c, CancellationToken ct) =>
        TreatmentRules.Check(Rules.Required(c.PetId, nameof(c.PetId)),
            Rules.Required(c.Title, nameof(c.Title)), Rules.MaxLength(c.Title, TreatmentPlan.MaxTitleLength, nameof(c.Title)),
            Rules.MaxLength(c.Dosage, TreatmentPlan.MaxDosageLength, nameof(c.Dosage)),
            Rules.MaxLength(c.Notes, TreatmentPlan.MaxNotesLength, nameof(c.Notes)),
            Rules.Range(c.RescheduledDayCount, 0, TreatmentScheduleExpander.MaxOccurrences, nameof(c.RescheduledDayCount)),
            TreatmentRules.Enum(c.ItemType, nameof(c.ItemType)), TreatmentRules.Enum(c.Kind, nameof(c.Kind)),
            c.Schedule is null ? new ValidationFailure(nameof(c.Schedule), Rules.RequiredCode) : null);
}

public sealed class CreateTreatmentPlanCommandHandler(ITreatmentPlanRepository plans, IPetRepository pets,
    IMedicalHistoryRepository history, IClinicTimeZoneProvider zones, IClock clock) : ICommandHandler<CreateTreatmentPlanCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateTreatmentPlanCommand c, CommandContext context, CancellationToken ct)
    {
        var pet = await pets.FindAsync(c.PetId, ct);
        if (pet is null || pet.Deleted) return Result<string>.Fail(ErrorCodes.PetNotFound);

        var draft = TreatmentPlan.Create(context.TenantId, c.PetId, c.ItemType, c.Kind, c.Title, c.Schedule,
            TreatmentRules.Today(clock, zones), context.UserId, clock, c.VaccineCode, c.Dosage, c.Notes, c.NotifyOwner);
        if (draft.IsFailure) return Result<string>.Fail(draft.Error, draft.Detail);

        var plan = draft.Value!.Plan;
        plans.Add(draft.Value!);
        TreatmentHistory.Record(history, context, clock, plan, VaccinationHistoryTitles.DoseScheduled, plan.Title);
        if (c.RescheduledDayCount > 0)
        {
            TreatmentHistory.Record(history, context, clock, plan, VaccinationHistoryTitles.DoseRescheduled,
                $"{plan.Title} · {c.RescheduledDayCount} day(s) moved off the clinic weekend");
        }

        return Result<string>.Ok(plan.Id);
    }
}

/// <summary>Edits the descriptive fields of an active plan (the dates are fixed — cancel and recreate to change them).</summary>
[RequiresPermission(PermissionCodes.VaccinationWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VaccinationTreatmentUpdate)]
public sealed record UpdateTreatmentPlanCommand(string PlanId, TreatmentKind Kind, string Title, string? Dosage,
    string? Notes, bool NotifyOwner) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VaccinationTreatmentUpdate;
    public string AggregateTypeName => nameof(TreatmentPlan);
    public string? AggregateIdOrNull => PlanId;
}

public sealed class UpdateTreatmentPlanCommandValidator : IValidator<UpdateTreatmentPlanCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(UpdateTreatmentPlanCommand c, CancellationToken ct) =>
        TreatmentRules.Check(Rules.Required(c.PlanId, nameof(c.PlanId)),
            Rules.Required(c.Title, nameof(c.Title)), Rules.MaxLength(c.Title, TreatmentPlan.MaxTitleLength, nameof(c.Title)),
            Rules.MaxLength(c.Dosage, TreatmentPlan.MaxDosageLength, nameof(c.Dosage)),
            Rules.MaxLength(c.Notes, TreatmentPlan.MaxNotesLength, nameof(c.Notes)),
            TreatmentRules.Enum(c.Kind, nameof(c.Kind)));
}

public sealed class UpdateTreatmentPlanCommandHandler(ITreatmentPlanRepository plans, IMedicalHistoryRepository history,
    IClock clock)
    : ICommandHandler<UpdateTreatmentPlanCommand, string>
{
    public async Task<Result<string>> HandleAsync(UpdateTreatmentPlanCommand c, CommandContext context, CancellationToken ct)
    {
        var plan = await plans.FindPlanAsync(c.PlanId, ct);
        if (plan is null || plan.Deleted) return Result<string>.Fail(ErrorCodes.TreatmentPlanNotFound);
        var updated = plan.Update(c.Kind, c.Title, c.Dosage, c.Notes, c.NotifyOwner);
        if (updated.IsFailure) return Result<string>.Fail(updated.Error, updated.Detail);

        TreatmentHistory.Record(history, context, clock, plan, VaccinationHistoryTitles.TreatmentUpdated, plan.Title);
        return Result<string>.Ok(plan.Id);
    }
}

/// <summary>Cancels an active plan; its still-pending occurrences are skipped, given ones are kept.</summary>
[RequiresPermission(PermissionCodes.VaccinationWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VaccinationTreatmentCancel)]
public sealed record CancelTreatmentPlanCommand(string PlanId) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VaccinationTreatmentCancel;
    public string AggregateTypeName => nameof(TreatmentPlan);
    public string? AggregateIdOrNull => PlanId;
}

public sealed class CancelTreatmentPlanCommandValidator : IValidator<CancelTreatmentPlanCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CancelTreatmentPlanCommand c, CancellationToken ct) =>
        TreatmentRules.Check(Rules.Required(c.PlanId, nameof(c.PlanId)));
}

public sealed class CancelTreatmentPlanCommandHandler(ITreatmentPlanRepository plans, IMedicalHistoryRepository history,
    IClock clock)
    : ICommandHandler<CancelTreatmentPlanCommand, string>
{
    public async Task<Result<string>> HandleAsync(CancelTreatmentPlanCommand c, CommandContext context, CancellationToken ct)
    {
        var plan = await plans.FindPlanAsync(c.PlanId, ct);
        if (plan is null || plan.Deleted) return Result<string>.Fail(ErrorCodes.TreatmentPlanNotFound);
        var cancelled = plan.Cancel();
        if (cancelled.IsFailure) return Result<string>.Fail(cancelled.Error, cancelled.Detail);

        foreach (var occurrence in await plans.ListPendingOccurrencesAsync(plan.Id, ct))
        {
            occurrence.Skip();
        }

        TreatmentHistory.Record(history, context, clock, plan, VaccinationHistoryTitles.TreatmentCancelled, plan.Title);
        return Result<string>.Ok(plan.Id);
    }
}

/// <summary>Records that one scheduled day was carried out; completes the plan when it was the last pending one.</summary>
[RequiresPermission(PermissionCodes.VaccinationWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VaccinationTreatmentOccurrenceDone)]
public sealed record MarkTreatmentOccurrenceDoneCommand(string OccurrenceId) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VaccinationTreatmentOccurrenceDone;
    public string AggregateTypeName => nameof(TreatmentOccurrence);
    public string? AggregateIdOrNull => OccurrenceId;
}

public sealed class MarkTreatmentOccurrenceDoneCommandValidator : IValidator<MarkTreatmentOccurrenceDoneCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(MarkTreatmentOccurrenceDoneCommand c, CancellationToken ct) =>
        TreatmentRules.Check(Rules.Required(c.OccurrenceId, nameof(c.OccurrenceId)));
}

public sealed class MarkTreatmentOccurrenceDoneCommandHandler(ITreatmentPlanRepository plans,
    IMedicalHistoryRepository history, IClock clock) : ICommandHandler<MarkTreatmentOccurrenceDoneCommand, string>
{
    public async Task<Result<string>> HandleAsync(MarkTreatmentOccurrenceDoneCommand c, CommandContext context, CancellationToken ct)
    {
        var occurrence = await plans.FindOccurrenceAsync(c.OccurrenceId, ct);
        if (occurrence is null || occurrence.Deleted) return Result<string>.Fail(ErrorCodes.TreatmentOccurrenceNotFound);
        var plan = await plans.FindPlanAsync(occurrence.PlanId, ct);
        if (plan is null || plan.Deleted) return Result<string>.Fail(ErrorCodes.TreatmentPlanNotFound);
        if (plan.State != TreatmentPlanState.Active) return Result<string>.Fail(ErrorCodes.TreatmentPlanNotActive);

        var done = occurrence.MarkDone(clock.UtcNow, context.UserId);
        if (done.IsFailure) return Result<string>.Fail(done.Error, done.Detail);

        var vaccine = plan.ItemType == TreatmentItemType.Vaccine;
        history.Add(new MedicalHistoryEntry(context.TenantId, plan.PetId, TreatmentHistory.KindOf(plan),
            vaccine ? VaccinationHistoryTitles.DoseAdministered : VaccinationHistoryTitles.MedicationGiven,
            clock.UtcNow, context.UserId, clock, plan.Title, occurrence.Id, nameof(TreatmentOccurrence)));

        if (await plans.CountPendingExceptAsync(plan.Id, occurrence.Id, ct) == 0) plan.Complete();
        return Result<string>.Ok(occurrence.Id);
    }
}

/// <summary>Adds a vaccine to the clinic's own catalog («انبار واکسن»). Returns the item id.</summary>
[RequiresPermission(PermissionCodes.VaccinationWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VaccinationCatalogAdd)]
public sealed record AddVaccineCatalogItemCommand(string Code, string Name, string? Protects = null,
    int TotalDoses = 1, int BoosterDays = VaccineSchedule.AnnualBoosterDays, int Stock = 0, int MinStock = 0)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VaccinationCatalogAdd;
    public string AggregateTypeName => nameof(VaccineCatalogItem);
    public string? AggregateIdOrNull => null;
}

public sealed class AddVaccineCatalogItemCommandValidator : IValidator<AddVaccineCatalogItemCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddVaccineCatalogItemCommand c, CancellationToken ct) =>
        TreatmentRules.Check(Rules.Required(c.Code, nameof(c.Code)),
            Rules.MaxLength(c.Code, VaccineCatalogItem.MaxCodeLength, nameof(c.Code)),
            Rules.Required(c.Name, nameof(c.Name)), Rules.MaxLength(c.Name, VaccineCatalogItem.MaxNameLength, nameof(c.Name)),
            Rules.MaxLength(c.Protects, VaccineCatalogItem.MaxProtectsLength, nameof(c.Protects)),
            Rules.Range(c.TotalDoses, 1, VaccineCatalogItem.MaxCount, nameof(c.TotalDoses)),
            Rules.Range(c.BoosterDays, 1, VaccineCatalogItem.MaxBoosterDays, nameof(c.BoosterDays)),
            Rules.Range(c.Stock, 0, VaccineCatalogItem.MaxCount, nameof(c.Stock)),
            Rules.Range(c.MinStock, 0, VaccineCatalogItem.MaxCount, nameof(c.MinStock)));
}

public sealed class AddVaccineCatalogItemCommandHandler(ITreatmentPlanRepository plans, IClock clock)
    : ICommandHandler<AddVaccineCatalogItemCommand, string>
{
    public async Task<Result<string>> HandleAsync(AddVaccineCatalogItemCommand c, CommandContext context, CancellationToken ct)
    {
        var created = VaccineCatalogItem.Create(context.TenantId, c.Code, c.Name, clock.UtcNow, c.Protects,
            c.TotalDoses, c.BoosterDays, c.Stock, c.MinStock);
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);
        if (await plans.CatalogCodeExistsAsync(created.Value!.Code, ct))
            return Result<string>.Fail(ErrorCodes.VaccineCatalogCodeTaken);

        plans.AddCatalogItem(created.Value!);
        return Result<string>.Ok(created.Value!.Id);
    }
}
