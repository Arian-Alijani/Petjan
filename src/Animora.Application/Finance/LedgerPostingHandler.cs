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
    IPeriodRepository periods, ILedgerRepository ledger,
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
        // PERF (DocumentCounters lock window): the gapless sequential journal number is NOT allocated here.
        // The counter row-lock that INSERT…ON CONFLICT DO UPDATE takes is held until COMMIT, and this handler
        // runs mid-dispatch — before the balance re-check, the event clear and the save. Allocating here made
        // that serialized "journal" counter row the clinic-wide gate every financial command (invoice, payment,
        // expense, cheque, stock) queued behind, exactly the tail the invoice-number fix left behind. The number
        // is not read by the posting rule, the JournalEntryPostedEvent or the entry's identity, so the entry is
        // built with a reserved placeholder and the real gapless value is stamped by IJournalNumberFinalizer in
        // one deterministic pass at the very end of the transaction (collapsing the lock hold to ~the save).
        var actor = correlation.Current?.UserId;
        var context = new PostingContext(JournalEntry.PendingNumber, period.PeriodCode,
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
    /// Idempotency &amp; concurrency: a row with the resolved code may already exist (a concurrent post, or a
    /// seeded row the resolver re-derives). Provisioning goes through
    /// <see cref="IPeriodRepository.EnsureAsync"/>, an atomic <c>(TenantId, PeriodCode)</c> upsert, so two
    /// concurrent first-posts into a brand-new month converge on one authoritative row instead of one hitting
    /// an un-retried unique-violation. This is what lets the former clinic-wide pessimistic ledger lock be
    /// dropped from the hot posting path.
    /// </para>
    /// </remarks>
    private async Task<Result<AccountingPeriod>> ProvisionContainingPeriodAsync(DomainEvent e, CancellationToken ct)
    {
        var boundary = periodBoundaries.Resolve(e.OccurredAtUtc);

        var created = AccountingPeriod.Create(e.TenantId, boundary.PeriodCode, boundary.StartUtc, boundary.EndUtc);
        if (created.IsFailure) return created;

        // Race-free lazy open: EnsureAsync atomically inserts-or-reuses the period, returning the authoritative
        // row (ours, or the concurrent winner's) exactly as the old find-first-then-add did, but without the
        // 23505 race window between the SELECT and the COMMIT.
        var ensured = await periods.EnsureAsync(created.Value!, ct).ConfigureAwait(false);
        return Result<AccountingPeriod>.Ok(ensured);
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
