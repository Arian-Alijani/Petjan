using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Sales;

[RequiresPermission(PermissionCodes.SalesVoid)]
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceRefund)]
public sealed record RefundInvoiceCommand(string InvoiceId, Money Amount, string Reason,
    PaymentMethod Method = PaymentMethod.Transfer, string? Reference = null, string? CashSessionId = null)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceRefund;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}

public sealed class RefundInvoiceCommandValidator : IValidator<RefundInvoiceCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RefundInvoiceCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)),
            Rules.Required(c.Reason, nameof(c.Reason)), Rules.PositiveMoney(c.Amount, nameof(c.Amount)),
            Rules.ValidEnum(c.Method, nameof(c.Method)));
}

public sealed class RefundInvoiceCommandHandler(IInvoiceRepository invoices, IPaymentRepository payments, IClock clock)
    : ICommandHandler<RefundInvoiceCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RefundInvoiceCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var invoice = found.Value!;
        if (c.Amount.Currency != invoice.Currency) return Result<Unit>.Fail(ErrorCodes.ValidationFailed);
        if (c.Amount > invoice.PaidTotal) return Result<Unit>.Fail(ErrorCodes.AdjustmentExceedsPaid);
        var now = clock.UtcNow;
        var payment = Payment.Create(context.TenantId, invoice.Id, c.Method, c.Amount.Negate(), now,
            context.UserId, cashSessionId: c.CashSessionId,
            chequeId: c.Method == PaymentMethod.Cheque ? c.Reference : null,
            cardReference: c.Method == PaymentMethod.Card ? c.Reference : null,
            operationId: context.OperationId);
        if (payment.IsFailure) return Result<Unit>.Fail(payment.Error, payment.Detail);
        var adjustment = new InvoiceAdjustment(Animora.Domain.Primitives.IdGenerator.NewId(now),
            AdjustmentKind.Refund, c.Amount.Negate(), c.Reason.Trim(), now, context.UserId, context.OperationId, null);
        var applied = invoice.ApplyRefund(adjustment, payment.Value!);
        if (applied.IsFailure) return Result<Unit>.Fail(applied.Error, applied.Detail);
        payments.Add(payment.Value!);
        return Result<Unit>.Ok(Unit.Value);
    }
}
