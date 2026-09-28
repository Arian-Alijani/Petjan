using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Vaccination;

[RequiresPermission(PermissionCodes.VaccinationWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VaccinationRecordAdminister)]
public sealed record MarkVaccinationDoneCommand(string RecordId) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VaccinationRecordAdminister;
    public string AggregateTypeName => "VaccinationRecord";
    public string? AggregateIdOrNull => RecordId;
}

public sealed class MarkVaccinationDoneCommandValidator : IValidator<MarkVaccinationDoneCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(MarkVaccinationDoneCommand c, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(
            Rules.Required(c.RecordId, nameof(c.RecordId)) is { } failure ? [failure] : []);
}

public sealed class MarkVaccinationDoneCommandHandler(IVaccinationRepository records,
    IVaccinationQueries queries, IMedicalHistoryRepository history, IClock clock)
    : ICommandHandler<MarkVaccinationDoneCommand, string>
{
    public async Task<Result<string>> HandleAsync(MarkVaccinationDoneCommand c,
        CommandContext context, CancellationToken ct)
    {
        var record = await records.FindAsync(c.RecordId, ct);
        if (record is null) return Result<string>.Fail(ErrorCodes.VaccinationNotFound);
        if (record.State == VaccinationState.Skipped || await queries.IsMarkedDoneAsync(record.Id, ct))
            return Result<string>.Fail(ErrorCodes.VaccinationAlreadyDone);

        // MarkDone confirms that the next dose has actually been given. The existing row
        // remains the preceding administered dose; never create an unadministered placeholder.
        if (record.TotalDoses is { } total && record.DoseNumber < total)
        {
            var nextDose = record.DoseNumber + 1;
            if (await queries.HasNextDoseAsync(record.PetId, record.VaccineCode, nextDose, ct))
                return Result<string>.Fail(ErrorCodes.VaccinationAlreadyDone);
            var administered = VaccinationRecord.Administer(context.TenantId, record.PetId,
                record.VaccineCode, record.VaccineName, nextDose, clock.UtcNow, context.UserId,
                clock, total, visitId: record.VisitId);
            if (administered.IsFailure) return Result<string>.Fail(administered.Error);
            records.Add(administered.Value!);
        }
        history.Add(new MedicalHistoryEntry(context.TenantId, record.PetId, MedicalHistoryKind.Vaccination,
            "Dose administered", clock.UtcNow, context.UserId, clock, record.VaccineName,
            record.Id, "VaccinationRecord"));
        return Result<string>.Ok(record.Id);
    }
}

[RequiresPermission(PermissionCodes.VaccinationWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VaccinationRecordReminder)]
public sealed record RecordVaccinationReminderCommand(string RecordId, string Message)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VaccinationRecordReminder;
    public string AggregateTypeName => "VaccinationRecord";
    public string? AggregateIdOrNull => RecordId;
}

public sealed class RecordVaccinationReminderCommandValidator : IValidator<RecordVaccinationReminderCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RecordVaccinationReminderCommand c, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();
        if (Rules.Required(c.RecordId, nameof(c.RecordId)) is { } record) errors.Add(record);
        if (Rules.Required(c.Message, nameof(c.Message)) is { } message) errors.Add(message);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(errors);
    }
}

public sealed class RecordVaccinationReminderCommandHandler(IVaccinationRepository records,
    IMedicalHistoryRepository history, IClock clock)
    : ICommandHandler<RecordVaccinationReminderCommand, string>
{
    public async Task<Result<string>> HandleAsync(RecordVaccinationReminderCommand c,
        CommandContext context, CancellationToken ct)
    {
        var record = await records.FindAsync(c.RecordId, ct);
        if (record is null) return Result<string>.Fail(ErrorCodes.VaccinationNotFound);
        // Audit only; this operation does not send a message or claim successful delivery.
        history.Add(new MedicalHistoryEntry(context.TenantId, record.PetId, MedicalHistoryKind.Vaccination,
            "Reminder produced", clock.UtcNow, context.UserId, clock, c.Message,
            record.Id, "VaccinationRecord"));
        return Result<string>.Ok(record.Id);
    }
}
