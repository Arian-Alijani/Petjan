using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Cashier;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Cashier;

[RequiresPermission(PermissionCodes.CashierSessionOpen)]
[Auditable(AuditCategory.Financial, CommandTypes.CashierSessionOpen)]
public sealed record OpenCashSessionCommand(Money OpeningFloat) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.CashierSessionOpen;
    public string AggregateTypeName => "CashSession";
    public string? AggregateIdOrNull => null;
}

public sealed class OpenCashSessionCommandHandler(ICashSessionContext current, ICashSessionRepository sessions,
    ILocalSequenceAllocator sequence, IClock clock) : ICommandHandler<OpenCashSessionCommand, string>
{
    public async Task<Result<string>> HandleAsync(OpenCashSessionCommand c, CommandContext context, CancellationToken ct)
    {
        var existing = await current.GetOpenSessionAsync(context.TenantId, context.UserId, ct);
        var guard = CashSession.EnsureNoOpenSession(existing is not null);
        if (guard.IsFailure) return Result<string>.Fail(guard.Error, guard.Detail);
        var next = await sequence.NextAsync(ct);
        if (next.IsFailure) return Result<string>.Fail(next.Error, next.Detail);
        var opened = CashSession.Open(context.TenantId, $"CS-{next.Value:00000000}",
            context.UserId, c.OpeningFloat, clock.UtcNow);
        if (opened.IsFailure) return Result<string>.Fail(opened.Error, opened.Detail);
        sessions.Add(opened.Value!);
        return Result<string>.Ok(opened.Value!.Id);
    }
}

[RequiresPermission(PermissionCodes.CashierSessionClose)]
[Auditable(AuditCategory.Financial, CommandTypes.CashierSessionClose)]
public sealed record CloseCashSessionCommand(Money CountedCash, string? Note) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.CashierSessionClose;
    public string AggregateTypeName => "CashSession";
    public string? AggregateIdOrNull => null;
}

public sealed class CloseCashSessionCommandHandler(ICashSessionContext current, ICashierQueries queries,
    IClock clock) : ICommandHandler<CloseCashSessionCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CloseCashSessionCommand c, CommandContext context, CancellationToken ct)
    {
        var session = await current.GetOpenSessionAsync(context.TenantId, context.UserId, ct);
        if (session is null) return Result<Unit>.Fail(ErrorCodes.CashSessionClosed);
        var unsettled = await queries.HasUnsettledPaymentsAsync(session.Id, ct);
        var begun = session.BeginClose(c.CountedCash, c.Note, clock, unsettled);
        if (begun.IsFailure) return Result<Unit>.Fail(begun.Error, begun.Detail);
        var closed = session.Close(clock);
        return closed.IsFailure ? Result<Unit>.Fail(closed.Error, closed.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.CashierSessionOpen)]
[Auditable(AuditCategory.Financial, CommandTypes.CashierMovementRecord)]
public sealed record RecordCashMovementCommand(CashMovementKind Kind, Money Amount, string Reason)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.CashierMovementRecord;
    public string AggregateTypeName => "CashSession";
    public string? AggregateIdOrNull => null;
}

public sealed class RecordCashMovementCommandHandler(ICashSessionContext current, IClock clock)
    : ICommandHandler<RecordCashMovementCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RecordCashMovementCommand c, CommandContext context, CancellationToken ct)
    {
        var session = await current.GetOpenSessionAsync(context.TenantId, context.UserId, ct);
        if (session is null) return Result<Unit>.Fail(ErrorCodes.CashSessionClosed);
        if (c.Kind == CashMovementKind.SalePayment || c.Kind == CashMovementKind.RefundOut)
            return Result<Unit>.Fail(ErrorCodes.ValidationFailed, "Invoice-linked cash movements must originate from sales commands.");
        if (string.IsNullOrWhiteSpace(c.Reason)) return Result<Unit>.Fail(ErrorCodes.ValidationFailed);
        var movement = CashMovement.Create(c.Kind, c.Amount, c.Reason, clock.UtcNow, context.UserId,
            operationId: context.OperationId);
        if (movement.IsFailure) return Result<Unit>.Fail(movement.Error, movement.Detail);
        var added = session.AddMovement(movement.Value!);
        return added.IsFailure ? Result<Unit>.Fail(added.Error, added.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}
