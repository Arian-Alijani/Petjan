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
    IPaymentRepository payments, IAccountingPeriodBoundaryResolver periodBoundaries,
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
        {
            // Standard open-period provisioning: no closed book spans this instant, so the clinic
            // simply never opened the calendar month it falls in. Mature ledgers auto-open the
            // containing period on first post rather than refusing the entry; the operator only ever
            // *closes* a period deliberately (finance.period.close), never opens one by hand. The
            // boundary resolver yields the exact Jalali-month code and UTC bounds the first-run
            // seeder would have produced, so the lazily-created period is indistinguishable from a
            // seeded one and the invoice/expense number allocators stay consistent.
            var provisioned = await ProvisionContainingPeriodAsync(e, ct).ConfigureAwait(false);
            if (provisioned.IsFailure) return provisioned.ToResult();
            period = provisioned.Value!;
        }
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

    /// <summary>
    /// Lazily opens the accounting period that contains <paramref name="e"/>'s instant, when none
    /// exists yet. The boundary is computed by the platform calendar resolver (the same authority the
    /// first-run seeder uses), so the new period's code and UTC range match a seeded one exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This only runs when <see cref="IPeriodRepository.FindContainingAsync"/> found <i>no</i> period
    /// spanning the instant, so the new open month can never overlap an existing closed/locked book:
    /// had a closed period contained the instant it would have been found, and the caller's
    /// <see cref="AccountingPeriod.EnsurePostable"/> branch would have rolled the entry into the next
    /// open period (A-RULE 003) instead of reaching here.
    /// </para>
    /// <para>
    /// Idempotency guard: a row with the resolved code may already exist (a concurrent post that won
    /// the <c>(TenantId, PeriodCode)</c> unique index, or a seeded row the resolver re-derives); it is
    /// reused rather than inserting a duplicate that the index would reject.
    /// </para>
    /// </remarks>
    private async Task<Result<AccountingPeriod>> ProvisionContainingPeriodAsync(DomainEvent e, CancellationToken ct)
    {
        var boundary = periodBoundaries.Resolve(e.OccurredAtUtc);

        // Idempotency: a period with this code may already exist (concurrent post, or a seeded row
        // whose window the resolver re-derives); reuse it rather than inserting a duplicate.
        var existingByCode = await periods.FindByCodeAsync(boundary.PeriodCode, ct).ConfigureAwait(false);
        if (existingByCode is not null) return Result<AccountingPeriod>.Ok(existingByCode);

        var created = AccountingPeriod.Create(e.TenantId, boundary.PeriodCode, boundary.StartUtc, boundary.EndUtc);
        if (created.IsFailure) return created;

        periods.Add(created.Value!);
        return created;
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
