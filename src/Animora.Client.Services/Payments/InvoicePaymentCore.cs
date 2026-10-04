using Animora.Application.Abstractions;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using CashierMethod = Animora.Contracts.Cashier.CashierPaymentMethod;
using SalesMethod = Animora.Contracts.Sales.PaymentMethod;

namespace Animora.Client.Services.Payments;

/// <summary>Outcome of a shared payment / refund operation. <see cref="Applied"/> is the ledger amount (Rial).</summary>
internal readonly record struct PaymentOutcome(bool Ok, string? Error, long Applied);

/// <summary>
/// The single code path that registers payments and refunds for BOTH the Sales and Cashier screens (X41):
/// pay-on-post, the cap-to-due / cap-to-paid rules, the typed failure codes and the contract-method →
/// domain-method mapping live here exactly once, so a fix can no longer be applied to one screen and
/// drift on the other. Callers only supply dispatch/read delegates and map the outcome to their contract.
/// </summary>
internal static class InvoicePaymentCore
{
    /// <summary>
    /// One mapping table for both screens' method enums. «ترکیبی» (mixed) has no domain equivalent of its own
    /// and is recorded as a bank transfer on both screens.
    /// </summary>
    public static PaymentMethod ToDomain(SalesMethod method) => method switch
    {
        SalesMethod.Cash => PaymentMethod.Cash,
        SalesMethod.Card => PaymentMethod.Card,
        SalesMethod.Cheque => PaymentMethod.Cheque,
        SalesMethod.Credit => PaymentMethod.Credit,
        _ => PaymentMethod.Transfer,
    };

    /// <inheritdoc cref="ToDomain(SalesMethod)"/>
    public static PaymentMethod ToDomain(CashierMethod method) => method switch
    {
        CashierMethod.Cash => PaymentMethod.Cash,
        CashierMethod.Card => PaymentMethod.Card,
        CashierMethod.Cheque => PaymentMethod.Cheque,
        CashierMethod.Credit => PaymentMethod.Credit,
        _ => PaymentMethod.Transfer,
    };

    /// <summary>
    /// Registers a payment: posts a draft first (one logical operation), re-reads the POSTED due (X17), clamps
    /// the requested amount to <c>[1, due]</c> and dispatches <see cref="RegisterPaymentCommand"/>.
    /// </summary>
    public static async Task<PaymentOutcome> PayAsync(
        string invoiceId,
        PaymentMethod method,
        long requestedAmount,
        string? reference,
        Func<ICommand<Unit>, Task<Result<Unit>>> send,
        Func<Task<InvoiceDetailReadModel?>> read)
    {
        var current = await read();
        if (current is null) return new(false, "INVOICE_NOT_FOUND", 0);

        if (current.Summary.State is InvoiceState.Void or InvoiceState.Refunded)
        {
            return new(false, ErrorCodes.InvoiceImmutable.Code, 0);
        }

        if (current.Summary.State == InvoiceState.Draft)
        {
            var posted = await send(new PostInvoiceCommand(invoiceId));
            if (posted.IsFailure) return new(false, "INVOICE_NOT_POSTED", 0);

            // Posting can finalise the payable total (tax / rounding / insurance): cap against the POSTED due.
            current = await read() ?? current;
        }

        var amount = Math.Min(Math.Max(0, requestedAmount), Math.Max(0, current.Summary.DueMinor));
        if (amount <= 0) return new(false, ErrorCodes.PaymentExceedsDue.Code, 0);

        var registered = await send(new RegisterPaymentCommand(invoiceId, method, Money.Irt(amount), reference));
        return registered.IsFailure
            ? new(false, registered.Error.Code, 0)
            : new(true, null, amount);
    }

    /// <summary>Refunds a paid invoice, clamped to what was actually received (the clinic never returns more).</summary>
    public static async Task<PaymentOutcome> RefundAsync(
        string invoiceId,
        long requestedAmount,
        string reason,
        Func<ICommand<Unit>, Task<Result<Unit>>> send,
        Func<Task<InvoiceDetailReadModel?>> read)
    {
        var current = await read();
        if (current is null) return new(false, "INVOICE_NOT_FOUND", 0);

        if (current.Summary.State is not (InvoiceState.PartiallyPaid or InvoiceState.Paid))
        {
            return new(false, ErrorCodes.InvoiceImmutable.Code, 0);
        }

        var amount = Math.Min(Math.Max(0, requestedAmount), Math.Max(0, current.PaidMinor));
        if (amount <= 0) return new(false, ErrorCodes.AdjustmentExceedsPaid.Code, 0);

        var refunded = await send(new RefundInvoiceCommand(invoiceId, Money.Irt(amount), reason));
        return refunded.IsFailure
            ? new(false, refunded.Error.Code, 0)
            : new(true, null, amount);
    }
}
