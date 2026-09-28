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

[RequiresPermission(PermissionCodes.SalesPost)]
[Auditable(AuditCategory.Financial, CommandTypes.SalesPaymentRegister)]
public sealed record RegisterPaymentCommand(string InvoiceId, PaymentMethod Method, Money Amount,
    string? Reference = null, string? CashSessionId = null) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesPaymentRegister;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}

public sealed class RegisterPaymentCommandValidator : IValidator<RegisterPaymentCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RegisterPaymentCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)),
            Rules.ValidEnum(c.Method, nameof(c.Method)),
            c.Amount.Minor <= 0 ? Rules.Range(c.Amount.Minor, 1L, long.MaxValue, nameof(c.Amount)) : null);
}

public sealed class RegisterPaymentCommandHandler(IInvoiceRepository invoices, IPaymentRepository payments, IClock clock)
    : ICommandHandler<RegisterPaymentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RegisterPaymentCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var invoice = found.Value!;
        if (c.Amount.Currency != invoice.Currency) return Result<Unit>.Fail(ErrorCodes.ValidationFailed);
        var created = Payment.Create(context.TenantId, invoice.Id, c.Method, c.Amount, clock.UtcNow,
            context.UserId, cashSessionId: c.CashSessionId, chequeId: c.Method == PaymentMethod.Cheque ? c.Reference : null,
            cardReference: c.Method == PaymentMethod.Card ? c.Reference : null, operationId: context.OperationId);
        if (created.IsFailure) return Result<Unit>.Fail(created.Error, created.Detail);
        var applied = invoice.ApplyPayment(created.Value!);
        if (applied.IsFailure) return Result<Unit>.Fail(applied.Error, applied.Detail);
        payments.Add(created.Value!);
        return Result<Unit>.Ok(Unit.Value);
    }
}
