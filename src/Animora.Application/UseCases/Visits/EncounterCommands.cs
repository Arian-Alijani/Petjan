using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Visits;

internal static class EncounterCommandRules
{
    public static ValueTask<IReadOnlyList<ValidationFailure>> ValidateId(string id) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(
            Rules.Required(id, "VisitId") is { } error ? [error] : []);
}

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsVisitVitals)]
public sealed record SaveVisitVitalsCommand(string VisitId, decimal? WeightKg, decimal? TemperatureC,
    int? HeartRateBpm, int? RespiratoryRate) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsVisitVitals;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => VisitId;
}

public sealed class SaveVisitVitalsCommandValidator : IValidator<SaveVisitVitalsCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(SaveVisitVitalsCommand c, CancellationToken ct) =>
        EncounterCommandRules.ValidateId(c.VisitId);
}

public sealed class SaveVisitVitalsCommandHandler(IVisitRepository visits)
    : ICommandHandler<SaveVisitVitalsCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SaveVisitVitalsCommand c, CommandContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(c.VisitId, ct);
        if (visit is null) return Result<Unit>.Fail(ErrorCodes.VisitNotFound);
        var result = visit.SetVitals(new PetVitals(c.WeightKg, c.TemperatureC, c.HeartRateBpm,
            c.RespiratoryRate, visit.Vitals?.MucousMembrane, visit.Vitals?.Hydration));
        return result.IsFailure ? Result<Unit>.Fail(ErrorCodes.VisitNotEditable) : Result<Unit>.Ok(Unit.Value);
    }
}

/// <summary>History and examination occupy independently editable labeled sections of DoctorNotes.</summary>
public static class EncounterNoteSections
{
    private const string HistoryTag = "[H]";
    private const string ExamTag = "[E]";

    public static (string History, string Exam) Split(string? notes)
    {
        if (string.IsNullOrEmpty(notes)) return (string.Empty, string.Empty);
        if (!notes.StartsWith(HistoryTag, StringComparison.Ordinal)) return (notes, string.Empty);
        var exam = notes.IndexOf(ExamTag, HistoryTag.Length, StringComparison.Ordinal);
        return exam < 0
            ? (notes[HistoryTag.Length..], string.Empty)
            : (notes[HistoryTag.Length..exam], notes[(exam + ExamTag.Length)..]);
    }

    public static string Join(string history, string exam) => $"{HistoryTag}{history}{ExamTag}{exam}";
}

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsVisitNote)]
public sealed record SaveVisitNoteCommand(string VisitId, VisitNoteField Field, string Text)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsVisitNote;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => VisitId;
}

public sealed class SaveVisitNoteCommandValidator : IValidator<SaveVisitNoteCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(SaveVisitNoteCommand c, CancellationToken ct) =>
        EncounterCommandRules.ValidateId(c.VisitId);
}

public sealed class SaveVisitNoteCommandHandler(IVisitRepository visits)
    : ICommandHandler<SaveVisitNoteCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SaveVisitNoteCommand c, CommandContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(c.VisitId, ct);
        if (visit is null) return Result<Unit>.Fail(ErrorCodes.VisitNotFound);
        if (!Enum.IsDefined(c.Field)) return Result<Unit>.Fail(ErrorCodes.ValidationFailed);
        var (history, exam) = EncounterNoteSections.Split(visit.DoctorNotes);
        var text = c.Text ?? string.Empty;
        var assessment = c.Field switch
        {
            VisitNoteField.History => visit.SetAssessment(visit.Diagnosis, visit.TreatmentPlan,
                EncounterNoteSections.Join(text, exam)),
            VisitNoteField.Exam => visit.SetAssessment(visit.Diagnosis, visit.TreatmentPlan,
                EncounterNoteSections.Join(history, text)),
            VisitNoteField.Diagnosis => visit.SetAssessment(text, visit.TreatmentPlan, visit.DoctorNotes),
            VisitNoteField.Plan => visit.SetAssessment(visit.Diagnosis, text, visit.DoctorNotes),
            _ => throw new InvalidOperationException(),
        };
        return assessment.IsFailure ? Result<Unit>.Fail(ErrorCodes.VisitNotEditable) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsRxAdd)]
public sealed record AddPrescriptionLineCommand(string VisitId, string CatalogId, string Title,
    VisitLineSource Source, long UnitPrice, int Quantity) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsRxAdd;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => VisitId;
}

public sealed class AddPrescriptionLineCommandValidator : IValidator<AddPrescriptionLineCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddPrescriptionLineCommand c, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();
        if (Rules.Required(c.VisitId, nameof(c.VisitId)) is { } id) errors.Add(id);
        if (Rules.Required(c.Title, nameof(c.Title)) is { } title) errors.Add(title);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(errors);
    }
}

public sealed class AddPrescriptionLineCommandHandler(IVisitRepository visits, IClock clock)
    : ICommandHandler<AddPrescriptionLineCommand, string>
{
    public async Task<Result<string>> HandleAsync(AddPrescriptionLineCommand c, CommandContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(c.VisitId, ct);
        if (visit is null) return Result<string>.Fail(ErrorCodes.VisitNotFound);
        if (c.Quantity <= 0 || c.UnitPrice < 0 || !Enum.IsDefined(c.Source))
            return Result<string>.Fail(ErrorCodes.ValidationFailed);
        var id = IdGenerator.NewId(clock.UtcNow);
        var line = new VisitServiceLine(id, c.CatalogId, c.Title,
            new Quantity(c.Quantity, "PCS"), Money.Irt(c.UnitPrice), c.Source);
        var added = visit.AddServiceLine(line);
        return added.IsFailure ? Result<string>.Fail(ErrorCodes.VisitNotEditable) : Result<string>.Ok(id);
    }
}

[RequiresPermission(PermissionCodes.VisitsWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsRxRemove)]
public sealed record RemovePrescriptionLineCommand(string VisitId, string RxLineId)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsRxRemove;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => VisitId;
}

public sealed class RemovePrescriptionLineCommandValidator : IValidator<RemovePrescriptionLineCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RemovePrescriptionLineCommand c, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();
        if (Rules.Required(c.VisitId, nameof(c.VisitId)) is { } id) errors.Add(id);
        if (Rules.Required(c.RxLineId, nameof(c.RxLineId)) is { } line) errors.Add(line);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(errors);
    }
}

public sealed class RemovePrescriptionLineCommandHandler(IVisitRepository visits)
    : ICommandHandler<RemovePrescriptionLineCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemovePrescriptionLineCommand c, CommandContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(c.VisitId, ct);
        if (visit is null) return Result<Unit>.Fail(ErrorCodes.VisitNotFound);
        if (visit.State is not (VisitState.Draft or VisitState.InProgress))
            return Result<Unit>.Fail(ErrorCodes.VisitNotEditable);
        var removed = visit.RemoveServiceLine(c.RxLineId);
        return removed.IsFailure ? Result<Unit>.Fail(ErrorCodes.RxLineNotFound) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.VisitsComplete)]
[Auditable(AuditCategory.Clinical, CommandTypes.VisitsVisitComplete)]
public sealed record CompleteVisitCommand(string VisitId) : ICommand<EncounterSummaryReadModel>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VisitsVisitComplete;
    public string AggregateTypeName => "Visit";
    public string? AggregateIdOrNull => VisitId;
}

public sealed class CompleteVisitCommandValidator : IValidator<CompleteVisitCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CompleteVisitCommand c, CancellationToken ct) =>
        EncounterCommandRules.ValidateId(c.VisitId);
}

public sealed class CompleteVisitCommandHandler(IVisitRepository visits, IMedicalHistoryRepository history,
    IVisitBillingHandoffWriter handoffs, IReceptionQueueRepository queue, IPetRepository pets,
    ITriageQueries triage, IClock clock) : ICommandHandler<CompleteVisitCommand, EncounterSummaryReadModel>
{
    public async Task<Result<EncounterSummaryReadModel>> HandleAsync(CompleteVisitCommand c,
        CommandContext context, CancellationToken ct)
    {
        var visit = await visits.FindAsync(c.VisitId, ct);
        if (visit is null) return Result<EncounterSummaryReadModel>.Fail(ErrorCodes.VisitNotFound);
        if (visit.State != VisitState.InProgress) return Result<EncounterSummaryReadModel>.Fail(ErrorCodes.VisitNotEditable);
        // The aggregate requires a diagnosis even for a lines-only visit (D8).
        if (string.IsNullOrWhiteSpace(visit.Diagnosis))
            return Result<EncounterSummaryReadModel>.Fail(ErrorCodes.VisitIncomplete);

        var completed = visit.Complete(clock);
        if (completed.IsFailure) return Result<EncounterSummaryReadModel>.Fail(ErrorCodes.VisitIncomplete);
        var completedUtc = visit.CompletedUtc!.Value;
        history.Add(new MedicalHistoryEntry(context.TenantId, visit.PetId, MedicalHistoryKind.Visit,
            visit.Diagnosis!, completedUtc, visit.DoctorUserId, clock, visit.TreatmentPlan, visit.Id, "Visit"));
        var snapshot = JsonSerializer.Serialize(new { ServiceLines = visit.ServiceLines, Prescriptions = visit.Prescriptions });
        handoffs.Add(context.TenantId, visit.Id, visit.OwnerId, visit.PetId, visit.DoctorUserId, completedUtc, snapshot);
        if (visit.AppointmentId is { } appointmentId)
        {
            var entry = await queue.FindByAppointmentAsync(appointmentId, ct);
            if (entry?.State == QueueState.InVisit) entry.Close(clock);
        }
        var ready = visit.MarkReadyForInvoice();
        if (ready.IsFailure) return Result<EncounterSummaryReadModel>.Fail(ready.Error);
        var subtotal = checked(visit.ServiceLines.Sum(l => checked((long)(l.UnitPrice.Minor * l.Quantity.Value))));
        var tax = Money.Irt(subtotal).ApplyPercent(9m).Minor;
        var pet = await pets.FindAsync(visit.PetId, ct);
        var doctor = (await triage.GetDoctorRailAsync(completedUtc.AddDays(-1), completedUtc.AddDays(1), ct))
            .FirstOrDefault(d => d.Id == visit.DoctorUserId);
        return Result<EncounterSummaryReadModel>.Ok(new EncounterSummaryReadModel(visit.Id,
            pet?.Name ?? string.Empty, pet?.Species.ToString() ?? string.Empty,
            visit.DoctorUserId, doctor?.Name ?? string.Empty, subtotal, tax, checked(subtotal + tax), completedUtc));
    }
}
