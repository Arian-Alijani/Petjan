using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Finance.Cheques;

internal static class ChequeRules
{
    public const int MaxNumberLength = 64;
    public const int MaxTextLength = 120;
    public const int MaxNoteLength = 1000;

    public static ValueTask<IReadOnlyList<ValidationFailure>> Check(params ValidationFailure?[] checks) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(checks.OfType<ValidationFailure>().ToArray());

    /// <summary>The counterparty invariant surfaced at validation time: inbound needs an owner, outbound a supplier.</summary>
    public static ValidationFailure? Counterparty(ChequeDirection direction, string? ownerId, string? supplierId) =>
        direction == ChequeDirection.Inbound && string.IsNullOrWhiteSpace(ownerId)
            ? new(nameof(RegisterChequeCommand.OwnerId), ErrorCodes.ChequeCounterpartyRequired.Code)
            : direction == ChequeDirection.Outbound && string.IsNullOrWhiteSpace(supplierId)
                ? new(nameof(RegisterChequeCommand.SupplierId), ErrorCodes.ChequeCounterpartyRequired.Code)
                : null;

    public static async Task<Result<Cheque>> LoadAsync(IChequeRepository cheques, string id, CancellationToken ct)
    {
        var cheque = await cheques.FindAsync(id, ct);
        return cheque is null || cheque.Deleted
            ? Result<Cheque>.Fail(ErrorCodes.ChequeNotFound)
            : Result<Cheque>.Ok(cheque);
    }
}

/// <summary>Registers a new cheque (state <c>Registered</c>). Returns the cheque id.</summary>
[RequiresPermission(PermissionCodes.ChequesManage)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceChequeCreate)]
public sealed record RegisterChequeCommand(ChequeDirection Direction, string ChequeNumber, long AmountMinor,
    DateTimeOffset DueUtc, DateTimeOffset IssuedUtc, string? OwnerId = null, string? SupplierId = null,
    string? BankName = null, string? BankBranch = null, string? AccountNumber = null, string? SayadId = null,
    string? Note = null) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceChequeCreate;
    public string AggregateTypeName => nameof(Cheque);
    public string? AggregateIdOrNull => null;
}

public sealed class RegisterChequeCommandValidator : IValidator<RegisterChequeCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RegisterChequeCommand c, CancellationToken ct) =>
        ChequeRules.Check(Rules.ValidEnum(c.Direction, nameof(c.Direction)),
            Rules.Required(c.ChequeNumber, nameof(c.ChequeNumber)),
            Rules.MaxLength(c.ChequeNumber, ChequeRules.MaxNumberLength, nameof(c.ChequeNumber)),
            Rules.PositiveMoney(Money.Irt(c.AmountMinor), nameof(c.AmountMinor)),
            ChequeRules.Counterparty(c.Direction, c.OwnerId, c.SupplierId),
            Rules.MaxLength(c.BankName, ChequeRules.MaxTextLength, nameof(c.BankName)),
            Rules.MaxLength(c.BankBranch, ChequeRules.MaxTextLength, nameof(c.BankBranch)),
            Rules.MaxLength(c.AccountNumber, ChequeRules.MaxTextLength, nameof(c.AccountNumber)),
            Rules.MaxLength(c.SayadId, ChequeRules.MaxTextLength, nameof(c.SayadId)),
            Rules.MaxLength(c.Note, ChequeRules.MaxNoteLength, nameof(c.Note)));
}

public sealed class RegisterChequeCommandHandler(IChequeRepository cheques, IClock clock)
    : ICommandHandler<RegisterChequeCommand, string>
{
    public Task<Result<string>> HandleAsync(RegisterChequeCommand c, CommandContext context, CancellationToken ct)
    {
        var created = Cheque.Register(context.TenantId, c.ChequeNumber, c.Direction, Money.Irt(c.AmountMinor), c.DueUtc,
            c.IssuedUtc, c.Direction == ChequeDirection.Inbound ? c.OwnerId : null,
            c.Direction == ChequeDirection.Outbound ? c.SupplierId : null,
            c.BankName, c.BankBranch, c.AccountNumber, c.SayadId, note: c.Note,
            id: IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow));
        if (created.IsFailure) return Task.FromResult(Result<string>.Fail(created.Error, created.Detail));
        cheques.Add(created.Value!);
        return Task.FromResult(Result<string>.Ok(created.Value!.Id));
    }
}

/// <summary>Edits the descriptive fields of a cheque that is still <c>Registered</c>.</summary>
[RequiresPermission(PermissionCodes.ChequesManage)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceChequeUpdate)]
public sealed record UpdateChequeMetadataCommand(string ChequeId, string ChequeNumber, DateTimeOffset DueUtc,
    string? BankName = null, string? BankBranch = null, string? AccountNumber = null, string? SayadId = null,
    string? Note = null) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceChequeUpdate;
    public string AggregateTypeName => nameof(Cheque);
    public string? AggregateIdOrNull => ChequeId;
}

public sealed class UpdateChequeMetadataCommandValidator : IValidator<UpdateChequeMetadataCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(UpdateChequeMetadataCommand c, CancellationToken ct) =>
        ChequeRules.Check(Rules.Required(c.ChequeId, nameof(c.ChequeId)),
            Rules.Required(c.ChequeNumber, nameof(c.ChequeNumber)),
            Rules.MaxLength(c.ChequeNumber, ChequeRules.MaxNumberLength, nameof(c.ChequeNumber)),
            Rules.MaxLength(c.BankName, ChequeRules.MaxTextLength, nameof(c.BankName)),
            Rules.MaxLength(c.BankBranch, ChequeRules.MaxTextLength, nameof(c.BankBranch)),
            Rules.MaxLength(c.AccountNumber, ChequeRules.MaxTextLength, nameof(c.AccountNumber)),
            Rules.MaxLength(c.SayadId, ChequeRules.MaxTextLength, nameof(c.SayadId)),
            Rules.MaxLength(c.Note, ChequeRules.MaxNoteLength, nameof(c.Note)));
}

public sealed class UpdateChequeMetadataCommandHandler(IChequeRepository cheques)
    : ICommandHandler<UpdateChequeMetadataCommand, string>
{
    public async Task<Result<string>> HandleAsync(UpdateChequeMetadataCommand c, CommandContext context, CancellationToken ct)
    {
        var loaded = await ChequeRules.LoadAsync(cheques, c.ChequeId, ct);
        if (loaded.IsFailure) return Result<string>.Fail(loaded.Error, loaded.Detail);
        var cheque = loaded.Value!;
        var updated = cheque.UpdateMetadata(c.ChequeNumber, c.DueUtc, c.BankName, c.BankBranch, c.AccountNumber,
            c.SayadId, c.Note);
        return updated.IsFailure ? Result<string>.Fail(updated.Error, updated.Detail) : Result<string>.Ok(cheque.Id);
    }
}

/// <summary>Removes a cheque that was never filed (tombstone; history is kept).</summary>
[RequiresPermission(PermissionCodes.ChequesManage)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceChequeDelete)]
public sealed record DeleteChequeDraftCommand(string ChequeId) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceChequeDelete;
    public string AggregateTypeName => nameof(Cheque);
    public string? AggregateIdOrNull => ChequeId;
}

public sealed class DeleteChequeDraftCommandValidator : IValidator<DeleteChequeDraftCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(DeleteChequeDraftCommand c, CancellationToken ct) =>
        ChequeRules.Check(Rules.Required(c.ChequeId, nameof(c.ChequeId)));
}

public sealed class DeleteChequeDraftCommandHandler(IChequeRepository cheques)
    : ICommandHandler<DeleteChequeDraftCommand, string>
{
    public async Task<Result<string>> HandleAsync(DeleteChequeDraftCommand c, CommandContext context, CancellationToken ct)
    {
        var loaded = await ChequeRules.LoadAsync(cheques, c.ChequeId, ct);
        if (loaded.IsFailure) return Result<string>.Fail(loaded.Error, loaded.Detail);
        var deleted = loaded.Value!.DeleteDraft();
        return deleted.IsFailure ? Result<string>.Fail(deleted.Error, deleted.Detail) : Result<string>.Ok(c.ChequeId);
    }
}

/// <summary>The lifecycle moves a user can request; each maps to exactly one domain mutator (the state machine stays in the aggregate).</summary>
public enum ChequeTransition
{
    MoveToPortfolio = 0,
    Deposit = 1,
    MarkCleared = 2,
    MarkBounced = 3,
    Return = 4,
    Cancel = 5,
    SettleInCash = 6,
}

/// <summary>
/// Moves a cheque along its lifecycle (portfolio / deposit / cleared / bounced / returned / cancelled / settled in cash).
/// A reason is mandatory for bounce, return and cancel. Clearing and bouncing raise the ledger events inside the aggregate.
/// </summary>
[RequiresPermission(PermissionCodes.ChequesManage)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceChequeTransition)]
public sealed record TransitionChequeCommand(string ChequeId, ChequeTransition Transition, string? Reason = null)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceChequeTransition;
    public string AggregateTypeName => nameof(Cheque);
    public string? AggregateIdOrNull => ChequeId;

    public bool RequiresReason => Transition is ChequeTransition.MarkBounced or ChequeTransition.Return
        or ChequeTransition.Cancel;
}

public sealed class TransitionChequeCommandValidator : IValidator<TransitionChequeCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(TransitionChequeCommand c, CancellationToken ct) =>
        ChequeRules.Check(Rules.Required(c.ChequeId, nameof(c.ChequeId)),
            Rules.ValidEnum(c.Transition, nameof(c.Transition)),
            c.RequiresReason ? Rules.Required(c.Reason, nameof(c.Reason)) : null,
            Rules.MaxLength(c.Reason, ChequeRules.MaxNoteLength, nameof(c.Reason)));
}

public sealed class TransitionChequeCommandHandler(IChequeRepository cheques, IClock clock)
    : ICommandHandler<TransitionChequeCommand, string>
{
    public async Task<Result<string>> HandleAsync(TransitionChequeCommand c, CommandContext context, CancellationToken ct)
    {
        var loaded = await ChequeRules.LoadAsync(cheques, c.ChequeId, ct);
        if (loaded.IsFailure) return Result<string>.Fail(loaded.Error, loaded.Detail);
        var cheque = loaded.Value!;
        var reason = c.Reason?.Trim() ?? string.Empty;
        var moved = c.Transition switch
        {
            ChequeTransition.MoveToPortfolio => cheque.MoveToPortfolio(context.UserId, clock),
            ChequeTransition.Deposit => cheque.Deposit(context.UserId, clock),
            ChequeTransition.MarkCleared => cheque.MarkCleared(context.UserId, clock),
            ChequeTransition.MarkBounced => cheque.MarkBounced(context.UserId, reason, clock),
            ChequeTransition.Return => cheque.Return(context.UserId, reason, clock),
            ChequeTransition.Cancel => cheque.Cancel(context.UserId, reason, clock),
            ChequeTransition.SettleInCash => cheque.SettleInCash(context.UserId, clock),
            _ => Result.Fail(ErrorCodes.ValidationFailed),
        };
        return moved.IsFailure ? Result<string>.Fail(moved.Error, moved.Detail) : Result<string>.Ok(cheque.Id);
    }
}
