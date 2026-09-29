using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Ports;
using Animora.Domain.Events;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.Finance;

/// <summary>The only application writer of journal entries. Runs within the source document's UoW.</summary>
public sealed class LedgerPostingHandler(
    IPeriodRepository periods, IJournalNumberAllocator numbers, ILedgerRepository ledger,
    IInvoiceRepository invoices, IPurchasingRepository purchasing, IStockMovementRepository movements,
    IPaymentRepository payments,
    ICorrelationContext correlation, IClock clock) :
    IDomainEventHandler<InvoicePostedEvent>, IDomainEventHandler<InvoiceVoidedEvent>,
    IDomainEventHandler<PaymentRegisteredEvent>, IDomainEventHandler<InvoiceRefundedEvent>,
    IDomainEventHandler<GoodsReceiptPostedEvent>, IDomainEventHandler<StockMovementRecordedEvent>,
    IDomainEventHandler<ChequeClearedEvent>, IDomainEventHandler<ChequeBouncedEvent>,
    IDomainEventHandler<CashSessionClosedEvent>, IDomainEventHandler<ExpensePostedEvent>
{
    private async Task<Result> PostAsync(DomainEvent e, Func<PostingContext, Result<JournalEntry>> rule,
        CancellationToken ct)
    {
        var period = await periods.FindContainingAsync(e.OccurredAtUtc, ct).ConfigureAwait(false);
        if (period is null)
            return Result.Fail(ErrorCodes.PeriodNotFound, $"No period contains {e.OccurredAtUtc:O}");
        if (AccountingPeriod.EnsurePostable(period).IsFailure)
        {
            period = await periods.FindNextOpenAsync(period.EndUtc, ct).ConfigureAwait(false);
            if (period is null)
                return Result.Fail(ErrorCodes.PeriodClosed, "No subsequent open period accepts this late event.");
        }
        var number = await numbers.AllocateAsync(e.TenantId, period.PeriodCode, ct).ConfigureAwait(false);
        var actor = correlation.Current?.UserId;
        var context = new PostingContext(number, period.PeriodCode,
            string.IsNullOrWhiteSpace(actor) ? "SYSTEM" : actor, correlation.Current?.OperationId);
        var posted = rule(context);
        if (posted.IsFailure) return posted.ToResult();
        ledger.Add(posted.Value!);
        return Result.Ok();
    }

    public async Task<Result> HandleAsync(InvoicePostedEvent e, CancellationToken ct)
    {
        var invoice = await invoices.FindAsync(e.InvoiceId, ct).ConfigureAwait(false);
        return invoice is null ? Result.Fail(ErrorCodes.NotFound, $"Invoice {e.InvoiceId}")
            : await PostAsync(e, context => PostingRules.ForInvoicePosted(e, invoice, context, clock), ct);
    }

    public async Task<Result> HandleAsync(InvoiceVoidedEvent e, CancellationToken ct)
    {
        var original = await ledger.FindInvoicePostingAsync(e.InvoiceId, ct).ConfigureAwait(false);
        return original is null ? Result.Fail(ErrorCodes.NotFound, $"Posting for invoice {e.InvoiceId}")
            : await PostAsync(e, context => PostingRules.ForInvoiceVoided(e, original, context, clock), ct);
    }

    public Task<Result> HandleAsync(PaymentRegisteredEvent e, CancellationToken ct) =>
        e.Method == PaymentMethod.Credit || e.AmountMinor == 0 ? Task.FromResult(Result.Ok()) :
        e.AmountMinor < 0 ? Task.FromResult(Result.Ok()) :
        PostAsync(e, context => PostingRules.ForPaymentRegistered(e, context, clock), ct);

    public async Task<Result> HandleAsync(InvoiceRefundedEvent e, CancellationToken ct)
    {
        var original = await payments.FindLatestForInvoiceAsync(e.InvoiceId, ct).ConfigureAwait(false);
        if (original is null) return Result.Fail(ErrorCodes.NotFound, $"Payment for refund {e.InvoiceId}");
        return await PostAsync(e,
            context => PostingRules.ForInvoiceRefunded(e, original.Method, context, clock), ct);
    }

    public async Task<Result> HandleAsync(GoodsReceiptPostedEvent e, CancellationToken ct)
    {
        var receipt = await purchasing.FindReceiptAsync(e.ReceiptId, ct).ConfigureAwait(false);
        if (receipt is null) return Result.Fail(ErrorCodes.NotFound, $"Receipt {e.ReceiptId}");
        if (receipt.Lines.Count == 0) return Result.Fail(ErrorCodes.ValidationFailed, "Receipt has no lines");
        return await PostAsync(e, context => PostingRules.ForGoodsReceiptPosted(e, receipt,
            receipt.Lines[0].LineCost.Currency, context, clock), ct);
    }

    public async Task<Result> HandleAsync(StockMovementRecordedEvent e, CancellationToken ct)
    {
        if (e.Kind is not (MovementKind.Sale or MovementKind.Consumption or MovementKind.Loss or MovementKind.AdjustmentOut))
            return Result.Ok();
        var movement = await movements.FindAsync(e.MovementId, ct).ConfigureAwait(false);
        if (movement is null) return Result.Fail(ErrorCodes.NotFound, $"Movement {e.MovementId}");
        var cost = new Money(checked((long)decimal.Round(
            Math.Abs(movement.Quantity.Value) * movement.UnitCost.Minor, 0, MidpointRounding.AwayFromZero)),
            movement.UnitCost.Currency);
        if (cost.Minor == 0) return Result.Ok();
        return await PostAsync(e, context => PostingRules.ForStockMovementRecorded(e, cost, context, clock), ct);
    }

    public Task<Result> HandleAsync(ChequeClearedEvent e, CancellationToken ct) =>
        PostAsync(e, context => PostingRules.ForChequeCleared(e, context, clock), ct);
    public Task<Result> HandleAsync(ChequeBouncedEvent e, CancellationToken ct) =>
        PostAsync(e, context => PostingRules.ForChequeBounced(e, context, clock), ct);
    public Task<Result> HandleAsync(CashSessionClosedEvent e, CancellationToken ct) =>
        e.VarianceMinor == 0 ? Task.FromResult(Result.Ok()) :
        PostAsync(e, context => PostingRules.ForCashSessionClosed(e, context, clock), ct);
    public Task<Result> HandleAsync(ExpensePostedEvent e, CancellationToken ct) =>
        PostAsync(e, context => PostingRules.ForExpensePosted(e, context, clock), ct);
}
