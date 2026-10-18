using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
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
                clock, total, visitId: record.VisitId,
                id: IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow));
            if (administered.IsFailure) return Result<string>.Fail(administered.Error);
            records.Add(administered.Value!);
        }
        history.Add(new MedicalHistoryEntry(context.TenantId, record.PetId, MedicalHistoryKind.Vaccination,
            VaccinationHistoryTitles.DoseAdministered, clock.UtcNow, context.UserId, clock, record.VaccineName,
            record.Id, "VaccinationRecord", id: IdGenerator.ForOperation(context.OperationId, 1, clock.UtcNow)));
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
            VaccinationHistoryTitles.ReminderProduced, clock.UtcNow, context.UserId, clock, c.Message,
            record.Id, "VaccinationRecord", id: IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow)));
        return Result<string>.Ok(record.Id);
    }
}

[RequiresPermission(PermissionCodes.VaccinationWrite)]
[Auditable(AuditCategory.Clinical, CommandTypes.VaccinationRecordRegister)]
public sealed record RegisterVaccinationCommand(string PetId, string VaccineCode, string VaccineName,
    int DoseNumber, DateTimeOffset AdministeredUtc, int? TotalDoses = null, string? BatchNumber = null,
    string? VisitId = null, string? Notes = null) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.VaccinationRecordRegister;
    public string AggregateTypeName => "VaccinationRecord";
    public string? AggregateIdOrNull => null;
}

public sealed class RegisterVaccinationCommandValidator : IValidator<RegisterVaccinationCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RegisterVaccinationCommand c, CancellationToken ct)
    {
        var errors = new List<ValidationFailure>();
        if (Rules.Required(c.PetId, nameof(c.PetId)) is { } pet) errors.Add(pet);
        if (Rules.Required(c.VaccineCode, nameof(c.VaccineCode)) is { } code) errors.Add(code);
        if (Rules.Required(c.VaccineName, nameof(c.VaccineName)) is { } name) errors.Add(name);
        else if (Rules.MaxLength(c.VaccineName, 120, nameof(c.VaccineName)) is { } tooLong) errors.Add(tooLong);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(errors);
    }
}

/// <summary>
/// Records one administered dose from the pet file (Task 9). Mirrors <see cref="MarkVaccinationDoneCommand"/>'s
/// write shape — a <see cref="VaccinationRecord"/> plus its «Dose administered» ledger entry — but binds the
/// dose to an explicit pet, dose number and administration instant, so a dose entered from the pet file is
/// indistinguishable (in the Vaccination module) from one entered there.
/// </summary>
public sealed class RegisterVaccinationCommandHandler(IPetRepository pets, IVaccinationRepository records,
    IMedicalHistoryRepository history, IClock clock)
    : ICommandHandler<RegisterVaccinationCommand, string>
{
    public async Task<Result<string>> HandleAsync(RegisterVaccinationCommand c,
        CommandContext context, CancellationToken ct)
    {
        var pet = await pets.FindAsync(c.PetId, ct);
        if (pet is null) return Result<string>.Fail(ErrorCodes.PetNotFound);

        var record = VaccinationRecord.Administer(context.TenantId, c.PetId, c.VaccineCode, c.VaccineName,
            c.DoseNumber, c.AdministeredUtc, context.UserId, clock, c.TotalDoses, c.BatchNumber, c.VisitId,
            c.Notes, id: IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow));
        if (record.IsFailure) return Result<string>.Fail(record.Error);
        records.Add(record.Value!);

        // The ledger entry is what the Vaccination module's history reads, and it marks this dose as given
        // in the due list (same contract as MarkVaccinationDoneCommand).
        history.Add(new MedicalHistoryEntry(context.TenantId, c.PetId, MedicalHistoryKind.Vaccination,
            VaccinationHistoryTitles.DoseAdministered, c.AdministeredUtc, context.UserId, clock,
            record.Value!.VaccineName, record.Value!.Id, "VaccinationRecord",
            id: IdGenerator.ForOperation(context.OperationId, 1, clock.UtcNow)));
        return Result<string>.Ok(record.Value!.Id);
    }
}
