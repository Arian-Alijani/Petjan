using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Finance.Cheques;

[RequiresPermission(PermissionCodes.ChequesManage)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceChequeCreate)]
public sealed record CreateChequeCommand(string Number, ChequeDirection Direction, long AmountMinor,
    DateTimeOffset DueUtc, DateTimeOffset IssuedUtc, string? OwnerId = null, string? SupplierId = null,
    string? BankName = null, string? BankBranch = null, string? AccountNumber = null,
    string? SayadId = null, string? InvoiceId = null, string? PaymentId = null, string? Note = null)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceChequeCreate;
    public string AggregateTypeName => "Cheque";
    public string? AggregateIdOrNull => null;
}

public sealed class CreateChequeCommandHandler(IChequeRepository cheques) : ICommandHandler<CreateChequeCommand, string>
{
    public Task<Result<string>> HandleAsync(CreateChequeCommand c, CommandContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Number) || !Enum.IsDefined(c.Direction))
            return Task.FromResult(Result<string>.Fail(ErrorCodes.ValidationFailed));
        var created = Cheque.Register(context.TenantId, c.Number, c.Direction, Money.Irt(c.AmountMinor),
            c.DueUtc, c.IssuedUtc, c.OwnerId, c.SupplierId, c.BankName, c.BankBranch,
            c.AccountNumber, c.SayadId, c.InvoiceId, c.PaymentId, c.Note);
        if (created.IsFailure) return Task.FromResult(Result<string>.Fail(created.Error, created.Detail));
        cheques.Add(created.Value!);
        return Task.FromResult(Result<string>.Ok(created.Value!.Id));
    }
}

[RequiresPermission(PermissionCodes.ChequesManage)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceChequeUpdate)]
public sealed record UpdateChequeCommand(string ChequeId, string Number, DateTimeOffset DueUtc,
    string? BankName = null, string? BankBranch = null, string? AccountNumber = null,
    string? SayadId = null, string? Note = null) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceChequeUpdate;
    public string AggregateTypeName => "Cheque";
    public string? AggregateIdOrNull => ChequeId;
}

public sealed class UpdateChequeCommandHandler(IChequeRepository cheques) : ICommandHandler<UpdateChequeCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateChequeCommand c, CommandContext context, CancellationToken ct)
    {
        var cheque = await cheques.FindAsync(c.ChequeId, ct);
        if (cheque is null || cheque.Deleted) return Result<Unit>.Fail(ErrorCodes.ChequeNotFound);
        var result = cheque.UpdateMetadata(c.Number, c.DueUtc, c.BankName, c.BankBranch, c.AccountNumber, c.SayadId, c.Note);
        return result.IsFailure ? Result<Unit>.Fail(result.Error, result.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.ChequesManage)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceChequeDelete)]
public sealed record DeleteChequeCommand(string ChequeId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceChequeDelete;
    public string AggregateTypeName => "Cheque";
    public string? AggregateIdOrNull => ChequeId;
}

public sealed class DeleteChequeCommandHandler(IChequeRepository cheques) : ICommandHandler<DeleteChequeCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteChequeCommand c, CommandContext context, CancellationToken ct)
    {
        var cheque = await cheques.FindAsync(c.ChequeId, ct);
        if (cheque is null || cheque.Deleted) return Result<Unit>.Fail(ErrorCodes.ChequeNotFound);
        var result = cheque.DeleteDraft();
        return result.IsFailure ? Result<Unit>.Fail(result.Error, result.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.ChequesManage)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceChequeStatus)]
public sealed record ChangeChequeStatusCommand(string ChequeId, ChequeState Target, string? Reason = null)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceChequeStatus;
    public string AggregateTypeName => "Cheque";
    public string? AggregateIdOrNull => ChequeId;
}

public sealed class ChangeChequeStatusCommandHandler(IChequeRepository cheques, IClock clock)
    : ICommandHandler<ChangeChequeStatusCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangeChequeStatusCommand c, CommandContext context, CancellationToken ct)
    {
        var cheque = await cheques.FindAsync(c.ChequeId, ct);
        if (cheque is null || cheque.Deleted) return Result<Unit>.Fail(ErrorCodes.ChequeNotFound);
        return Apply(cheque, c.Target, c.Reason, context.UserId, clock);
    }

    internal static Result<Unit> Apply(Cheque cheque, ChequeState target, string? reason, string userId, IClock clock)
    {
        if (!Cheque.IsTransitionAllowed(cheque.State, target))
            return Result<Unit>.Fail(ErrorCodes.ChequeInvalidTransition);
        if (target is ChequeState.Bounced or ChequeState.Returned or ChequeState.Cancelled && string.IsNullOrWhiteSpace(reason))
            return Result<Unit>.Fail(ErrorCodes.ValidationFailed, "A reason is required for this transition.");
        var changed = target switch
        {
            ChequeState.InPortfolio => cheque.MoveToPortfolio(userId, clock),
            ChequeState.Deposited => cheque.Deposit(userId, clock),
            ChequeState.Cleared => cheque.MarkCleared(userId, clock),
            ChequeState.Bounced => cheque.MarkBounced(userId, reason!, clock),
            ChequeState.Returned => cheque.Return(userId, reason!, clock),
            ChequeState.Cancelled => cheque.Cancel(userId, reason!, clock),
            ChequeState.Settled => cheque.SettleInCash(userId, clock),
            _ => Result.Fail(ErrorCodes.ChequeInvalidTransition),
        };
        return changed.IsFailure ? Result<Unit>.Fail(changed.Error, changed.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

/// <summary>Each item has its own operation id, so partial results remain individually reconcilable.</summary>
public sealed record ChequeBulkItemResult(string ChequeId, string OperationId, bool Succeeded, string? Error);

[RequiresPermission(PermissionCodes.ChequesManage)]
[Auditable(AuditCategory.Financial, CommandTypes.FinanceChequeStatus)]
public sealed record BulkChangeChequeStatusCommand(IReadOnlyList<string> Ids, ChequeState Target, string? Reason = null)
    : ICommand<IReadOnlyList<ChequeBulkItemResult>>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.FinanceChequeStatus;
    public string AggregateTypeName => "Cheque";
    public string? AggregateIdOrNull => null;
}

public sealed class BulkChangeChequeStatusCommandHandler(IChequeRepository cheques, IClock clock)
    : ICommandHandler<BulkChangeChequeStatusCommand, IReadOnlyList<ChequeBulkItemResult>>
{
    public async Task<Result<IReadOnlyList<ChequeBulkItemResult>>> HandleAsync(
        BulkChangeChequeStatusCommand c, CommandContext context, CancellationToken ct)
    {
        if (c.Ids is null || c.Ids.Count is < 1 or > 200 || c.Ids.Any(string.IsNullOrWhiteSpace))
            return Result<IReadOnlyList<ChequeBulkItemResult>>.Fail(ErrorCodes.ValidationFailed);
        var results = new List<ChequeBulkItemResult>(c.Ids.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in c.Ids)
        {
            // A repeated ID is reported, not applied twice in the same transaction.
            var operationId = Animora.Domain.Primitives.IdGenerator.NewId(clock.UtcNow);
            if (!seen.Add(id))
            {
                results.Add(new ChequeBulkItemResult(id, operationId, false, ErrorCodes.ValidationFailed.Code));
                continue;
            }
            var cheque = await cheques.FindAsync(id, ct);
            var result = cheque is null || cheque.Deleted
                ? Result<Unit>.Fail(ErrorCodes.ChequeNotFound)
                : ChangeChequeStatusCommandHandler.Apply(cheque, c.Target, c.Reason, context.UserId, clock);
            results.Add(new ChequeBulkItemResult(id, operationId, result.IsSuccess, result.IsFailure ? result.Error.Code : null));
        }
        return Result<IReadOnlyList<ChequeBulkItemResult>>.Ok(results);
    }
}
