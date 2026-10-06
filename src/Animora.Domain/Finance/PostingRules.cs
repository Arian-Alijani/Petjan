using Animora.Domain.Cashier;
using Animora.Domain.Events;
using Animora.Domain.Inventory;
using Animora.Domain.Primitives;
using Animora.Domain.Purchasing;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Finance;

/// <summary>
/// The bookkeeping metadata a posting rule cannot derive from the event itself: the sequential
/// entry number and the period the Application layer resolved, who is posting, and the command
/// that triggered it.
/// </summary>
/// <param name="EntryNumber">Per-tenant sequential entry number, allocated by the Application layer.</param>
/// <param name="PeriodId">
/// The <see cref="AccountingPeriod.PeriodCode"/> to post into. The caller has already run
/// <see cref="AccountingPeriod.EnsurePostable(AccountingPeriod)"/> on it.
/// </param>
/// <param name="PostedByUserId">Who posts the entry.</param>
/// <param name="OperationId">The business command that triggered it (A-RULE 006 idempotency key).</param>
public readonly record struct PostingContext(
    string EntryNumber,
    string PeriodId,
    string PostedByUserId,
    string? OperationId);

/// <summary>
/// The <b>authoritative</b> event → journal translation table of the product. Nothing else decides
/// which account a business fact lands on: A10 (local) and D07/D09 (server) both dispatch their
/// domain events into these same pure functions, so client and server ledgers cannot diverge.
/// </summary>
/// <remarks>
/// <para>
/// Every rule is a pure function of its event payload plus a <see cref="PostingContext"/> and an
/// <see cref="IClock"/>; none reads a clock or a store of its own. Each builds its lines and
/// delegates to <see cref="JournalEntry.Create"/>, so <b>balance, positivity, single currency and
/// account postability are structurally guaranteed</b> — a rule cannot emit a malformed entry even
/// if its own arithmetic were wrong, because <c>Create</c> would refuse it.
/// </para>
/// <para>Every account referenced here is a postable account of <see cref="ChartOfAccounts"/>.</para>
/// <para><b>The table:</b></para>
/// <list type="table">
/// <item><term><see cref="ForInvoicePosted"/></term><description>D 1100 AR + 4900 discounts + 1110 insurance · C 4000/4010/4020 revenue + 2200 tax</description></item>
/// <item><term><see cref="ForPaymentRegistered"/></term><description>D 1000 cash / 1010 bank / 1020 cheques receivable · C 1100 AR</description></item>
/// <item><term><see cref="ForInvoiceRefunded"/></term><description>D 1100 AR · C 1000/1010 by the original method</description></item>
/// <item><term><see cref="ForInvoiceVoided"/></term><description>the mirrored reversal of the posting entry</description></item>
/// <item><term><see cref="ForGoodsReceiptPosted"/></term><description>D 1200 inventory · C 2000 AP</description></item>
/// <item><term><see cref="ForStockMovementRecorded"/></term><description>D 5000 COGS (sale/consumption) or 6200 loss (loss/adjustment-out) · C 1200 inventory</description></item>
/// <item><term><see cref="ForChequeCleared"/></term><description>inbound D 1010 · C 1020 — outbound D 2010 · C 1010</description></item>
/// <item><term><see cref="ForChequeBounced"/></term><description>inbound D 1100 AR · C 1020 — outbound D 2010 · C 2000</description></item>
/// <item><term><see cref="ForCashSessionClosed"/></term><description>shortage D 1900 · C 1000; an overage swaps the sides</description></item>
/// <item><term><see cref="ForExpensePosted"/></term><description>D 6000/6100 by category · C 1000/1010/2010/2000 by settlement</description></item>
/// </list>
/// </remarks>
public static class PostingRules
{
    // ---- Sales -------------------------------------------------------------

    /// <summary>
    /// Recognises a posted invoice: the payer's debt, the insurer's share and the discount given
    /// away on the debit side; gross revenue per line kind and the tax liability on the credit side.
    /// </summary>
    /// <remarks>
    /// Revenue is credited <b>gross</b> and the discount debited separately to
    /// <see cref="AccountCodes.SalesDiscounts"/>, so a report can show what was given away instead
    /// of only what was collected. The identity that makes it balance is
    /// <c>GrandTotal + DiscountTotal + InsuranceCoverage = Subtotal + TaxTotal</c>. A line kind maps
    /// to its revenue account (<see cref="InvoiceLineKind.Custom"/> counts as service revenue) and
    /// a zero-amount component contributes no line at all.
    /// </remarks>
    /// <param name="e">The posting event.</param>
    /// <param name="invoice">The posted invoice; read-only — the rule supplies the revenue split.</param>
    /// <param name="context">Entry number, period, actor and operation id.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForInvoicePosted(
        InvoicePostedEvent e,
        Invoice invoice,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var currency = invoice.Currency;
        var lines = new List<JournalLine>(8);

        Add(lines, now, EntrySide.Debit, AccountCodes.AccountsReceivable, invoice.GrandTotal, "Amount due from payer");
        Add(lines, now, EntrySide.Debit, AccountCodes.SalesDiscounts, invoice.DiscountTotal, "Discount granted");
        Add(lines, now, EntrySide.Debit, AccountCodes.InsuranceReceivable, invoice.InsuranceCoverage, "Insurer share");

        var serviceMinor = 0L;
        var productMinor = 0L;
        var boardingMinor = 0L;
        var invoiceLines = invoice.Lines;
        for (var i = 0; i < invoiceLines.Count; i++)
        {
            var line = invoiceLines[i];
            switch (line.Kind)
            {
                case InvoiceLineKind.Product:
                    productMinor += line.LineGross.Minor;
                    break;
                case InvoiceLineKind.Boarding:
                    boardingMinor += line.LineGross.Minor;
                    break;
                default:
                    serviceMinor += line.LineGross.Minor;
                    break;
            }
        }

        Add(lines, now, EntrySide.Credit, AccountCodes.ServiceRevenue, new Money(serviceMinor, currency), "Service revenue");
        Add(lines, now, EntrySide.Credit, AccountCodes.ProductRevenue, new Money(productMinor, currency), "Product revenue");
        Add(lines, now, EntrySide.Credit, AccountCodes.BoardingRevenue, new Money(boardingMinor, currency), "Boarding revenue");
        Add(lines, now, EntrySide.Credit, AccountCodes.TaxPayable, invoice.TaxTotal, "Tax payable");

        return Build(
            e.TenantId,
            context,
            JournalSource.Sales,
            lines,
            $"Invoice {invoice.InvoiceNumber} posted",
            nameof(Invoice),
            e.InvoiceId,
            now);
    }

    /// <summary>
    /// Recognises money received against an invoice: the receiving asset account is debited and
    /// the receivable cleared by the same amount.
    /// </summary>
    /// <remarks>
    /// <see cref="PaymentMethod.Cash"/> lands on 1000, <see cref="PaymentMethod.Card"/> and
    /// <see cref="PaymentMethod.Transfer"/> on 1010, <see cref="PaymentMethod.Cheque"/> on 1020
    /// (the cheque only reaches the bank when it clears —
    /// see <see cref="ForChequeCleared"/>), <see cref="PaymentMethod.Insurance"/> on 1110 and
    /// <see cref="PaymentMethod.Wallet"/> on 2100. <see cref="PaymentMethod.Credit"/> moves no
    /// money — the receivable simply stands — so it produces no entry and is refused here.
    /// A refund (negative amount) is <see cref="ForInvoiceRefunded"/>'s job, not this rule's.
    /// </remarks>
    /// <param name="e">The payment event; its amount must be positive.</param>
    /// <param name="context">Entry number, period, actor and operation id.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForPaymentRegistered(
        PaymentRegisteredEvent e,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(clock);

        if (e.AmountMinor <= 0L)
        {
            return Result<JournalEntry>.Fail(
                ErrorCodes.JournalLineAmountInvalid,
                $"Payment '{e.PaymentId}' moved {e.AmountMinor}; a refund is posted by {nameof(ForInvoiceRefunded)}.");
        }

        var debitAccount = e.Method switch
        {
            PaymentMethod.Cash => AccountCodes.CashOnHand,
            PaymentMethod.Card or PaymentMethod.Transfer => AccountCodes.Bank,
            PaymentMethod.Cheque => AccountCodes.ChequesReceivable,
            PaymentMethod.Insurance => AccountCodes.InsuranceReceivable,
            PaymentMethod.Wallet => AccountCodes.CustomerPrepayments,
            _ => null,
        };

        if (debitAccount is null)
        {
            return Result<JournalEntry>.Fail(
                ErrorCodes.ValidationFailed,
                $"Payment method {e.Method} moves no money and produces no journal entry.");
        }

        var now = clock.UtcNow;
        var amount = new Money(e.AmountMinor, e.Currency);
        var lines = new List<JournalLine>(2);
        Add(lines, now, EntrySide.Debit, debitAccount, amount, $"{e.Method} received");
        Add(lines, now, EntrySide.Credit, AccountCodes.AccountsReceivable, amount, "Receivable settled");

        return Build(
            e.TenantId,
            context,
            JournalSource.Payment,
            lines,
            $"Payment {e.PaymentId} on invoice {e.InvoiceId}",
            nameof(Payment),
            e.PaymentId,
            now);
    }

    /// <summary>
    /// Recognises money given back: the receivable re-opens on the debit side and the asset the
    /// money left is credited, chosen by the method the original payment used.
    /// </summary>
    /// <param name="e">The refund event; the amount is taken as a magnitude.</param>
    /// <param name="originalMethod">How the original payment was taken; fixes the credited account.</param>
    /// <param name="context">Entry number, period, actor and operation id.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForInvoiceRefunded(
        InvoiceRefundedEvent e,
        PaymentMethod originalMethod,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(clock);

        var creditAccount = originalMethod == PaymentMethod.Cash
            ? AccountCodes.CashOnHand
            : AccountCodes.Bank;

        var now = clock.UtcNow;
        var amount = new Money(Math.Abs(e.AmountMinor), e.Currency);
        var lines = new List<JournalLine>(2);
        Add(lines, now, EntrySide.Debit, AccountCodes.AccountsReceivable, amount, "Receivable re-opened");
        Add(lines, now, EntrySide.Credit, creditAccount, amount, $"Refund paid by {originalMethod}");

        return Build(
            e.TenantId,
            context,
            JournalSource.Refund,
            lines,
            $"Refund {e.AdjustmentId} on invoice {e.InvoiceId}",
            nameof(Invoice),
            e.InvoiceId,
            now);
    }

    /// <summary>
    /// Undoes a posted invoice by mirroring its posting entry — the <b>only</b> correction
    /// mechanism the ledger has (A-RULE 002/003). The original entry is never touched.
    /// </summary>
    /// <param name="e">The void event; its reason becomes the reversal's audit memo.</param>
    /// <param name="postingEntry">The entry <see cref="ForInvoicePosted"/> produced.</param>
    /// <param name="context">Entry number, period and actor for the reversal.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForInvoiceVoided(
        InvoiceVoidedEvent e,
        JournalEntry postingEntry,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(postingEntry);

        return JournalEntry.CreateReversal(
            postingEntry,
            e.Reason,
            clock,
            context.EntryNumber,
            context.PeriodId,
            context.PostedByUserId,
            context.OperationId);
    }

    // ---- Purchasing & inventory -------------------------------------------

    /// <summary>
    /// Recognises received goods: inventory rises at cost and the supplier liability with it.
    /// </summary>
    /// <param name="e">The receipt event.</param>
    /// <param name="receipt">The posted receipt; the rule sums its lines' costs.</param>
    /// <param name="currency">ISO-4217 code of the receipt's costs.</param>
    /// <param name="context">Entry number, period, actor and operation id.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForGoodsReceiptPosted(
        GoodsReceiptPostedEvent e,
        GoodsReceipt receipt,
        string currency,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentNullException.ThrowIfNull(clock);

        var costMinor = 0L;
        var receiptLines = receipt.Lines;
        for (var i = 0; i < receiptLines.Count; i++)
        {
            costMinor += receiptLines[i].LineCost.Minor;
        }

        var now = clock.UtcNow;
        var total = new Money(costMinor, currency);
        var lines = new List<JournalLine>(2);
        Add(lines, now, EntrySide.Debit, AccountCodes.Inventory, total, "Stock received at cost");
        Add(lines, now, EntrySide.Credit, AccountCodes.AccountsPayable, total, "Supplier liability");

        return Build(
            e.TenantId,
            context,
            JournalSource.Receipt,
            lines,
            $"Goods receipt {receipt.ReceiptNumber} posted",
            nameof(GoodsReceipt),
            e.ReceiptId,
            now);
    }

    /// <summary>
    /// Recognises stock leaving the shelf: a sale or clinical consumption becomes cost of goods
    /// sold, a loss or an outward adjustment becomes inventory loss. Inventory is credited in both
    /// cases.
    /// </summary>
    /// <remarks>
    /// The rule never computes a cost: it receives the extended cost the movement was valued at,
    /// which <c>StockCalculator</c> derives as a moving weighted average (R-B23). Kinds other than
    /// the four tabled ones have no ledger effect and are refused here.
    /// </remarks>
    /// <param name="e">The movement event.</param>
    /// <param name="cost">The extended weighted-average cost of the movement; strictly positive.</param>
    /// <param name="context">Entry number, period, actor and operation id.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForStockMovementRecorded(
        StockMovementRecordedEvent e,
        Money cost,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(clock);

        var debitAccount = e.Kind switch
        {
            MovementKind.Sale or MovementKind.Consumption => AccountCodes.Cogs,
            MovementKind.Loss or MovementKind.AdjustmentOut => AccountCodes.InventoryLoss,
            _ => null,
        };

        if (debitAccount is null)
        {
            return Result<JournalEntry>.Fail(
                ErrorCodes.ValidationFailed,
                $"Movement kind {e.Kind} has no ledger effect of its own.");
        }

        var now = clock.UtcNow;
        var amount = cost.Abs();
        var lines = new List<JournalLine>(2);
        Add(lines, now, EntrySide.Debit, debitAccount, amount, $"{e.Kind} at weighted-average cost");
        Add(lines, now, EntrySide.Credit, AccountCodes.Inventory, amount, "Stock relieved");

        return Build(
            e.TenantId,
            context,
            JournalSource.Inventory,
            lines,
            $"Stock movement {e.MovementId} ({e.Kind})",
            nameof(StockMovement),
            e.MovementId,
            now);
    }

    // ---- Cheques -----------------------------------------------------------

    /// <summary>
    /// Recognises a cleared cheque: an inbound one turns the receivable into bank money, an
    /// outbound one settles the payable out of the bank.
    /// </summary>
    /// <param name="e">The clearing event.</param>
    /// <param name="context">Entry number, period, actor and operation id.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForChequeCleared(
        ChequeClearedEvent e,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var amount = new Money(Math.Abs(e.AmountMinor), e.Currency);
        var inbound = e.Direction == ChequeDirection.Inbound;
        var lines = new List<JournalLine>(2);

        if (inbound)
        {
            Add(lines, now, EntrySide.Debit, AccountCodes.Bank, amount, "Cheque collected into bank");
            Add(lines, now, EntrySide.Credit, AccountCodes.ChequesReceivable, amount, "Cheque receivable cleared");
        }
        else
        {
            Add(lines, now, EntrySide.Debit, AccountCodes.ChequesPayable, amount, "Cheque payable settled");
            Add(lines, now, EntrySide.Credit, AccountCodes.Bank, amount, "Bank debited by drawee");
        }

        return Build(
            e.TenantId,
            context,
            JournalSource.Cheque,
            lines,
            $"Cheque {e.ChequeId} cleared ({e.Direction})",
            nameof(Cheque),
            e.ChequeId,
            now);
    }

    /// <summary>
    /// Recognises a bounced cheque: an inbound one puts the debt back on the customer's
    /// receivable, an outbound one puts the obligation back on accounts payable.
    /// </summary>
    /// <param name="e">The bounce event; its reason is carried into the memo (A-RULE 003).</param>
    /// <param name="context">Entry number, period, actor and operation id.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForChequeBounced(
        ChequeBouncedEvent e,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var amount = new Money(Math.Abs(e.AmountMinor), e.Currency);
        var inbound = e.Direction == ChequeDirection.Inbound;
        var lines = new List<JournalLine>(2);

        if (inbound)
        {
            Add(lines, now, EntrySide.Debit, AccountCodes.AccountsReceivable, amount, "Debt returns to customer");
            Add(lines, now, EntrySide.Credit, AccountCodes.ChequesReceivable, amount, "Cheque receivable written back");
        }
        else
        {
            Add(lines, now, EntrySide.Debit, AccountCodes.ChequesPayable, amount, "Cheque payable written back");
            Add(lines, now, EntrySide.Credit, AccountCodes.AccountsPayable, amount, "Obligation returns to supplier");
        }

        return Build(
            e.TenantId,
            context,
            JournalSource.Cheque,
            lines,
            $"Cheque {e.ChequeId} bounced ({e.Direction}): {e.Reason}",
            nameof(Cheque),
            e.ChequeId,
            now);
    }

    // ---- Cashier & expenses ------------------------------------------------

    /// <summary>
    /// Recognises a drawer's over/short at close: a shortage is an expense charged to
    /// <see cref="AccountCodes.CashVariance"/> against cash; an overage swaps the two sides.
    /// </summary>
    /// <remarks>
    /// The event's variance is <c>declared - expected</c>, so a negative value is a shortage. A
    /// zero variance is not a bookkeeping fact and produces no entry — the caller checks it first.
    /// </remarks>
    /// <param name="e">The close event.</param>
    /// <param name="context">Entry number, period, actor and operation id.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForCashSessionClosed(
        CashSessionClosedEvent e,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(clock);

        if (e.VarianceMinor == 0L)
        {
            return Result<JournalEntry>.Fail(
                ErrorCodes.ValidationFailed,
                $"Cash session '{e.SessionId}' closed on the nose; a zero variance posts nothing.");
        }

        var now = clock.UtcNow;
        var amount = new Money(Math.Abs(e.VarianceMinor), e.Currency);
        var shortage = e.VarianceMinor < 0L;
        var lines = new List<JournalLine>(2);

        if (shortage)
        {
            Add(lines, now, EntrySide.Debit, AccountCodes.CashVariance, amount, "Drawer shortage");
            Add(lines, now, EntrySide.Credit, AccountCodes.CashOnHand, amount, "Cash written down");
        }
        else
        {
            Add(lines, now, EntrySide.Debit, AccountCodes.CashOnHand, amount, "Cash written up");
            Add(lines, now, EntrySide.Credit, AccountCodes.CashVariance, amount, "Drawer overage");
        }

        return Build(
            e.TenantId,
            context,
            JournalSource.Cashier,
            lines,
            $"Cash session {e.SessionId} variance {e.VarianceMinor}",
            nameof(CashSession),
            e.SessionId,
            now);
    }

    /// <summary>
    /// Recognises a posted expense: the category's expense account is debited and the settlement
    /// account credited — cash, bank, cheques payable, or accounts payable when it is still unpaid.
    /// </summary>
    /// <param name="e">The expense event.</param>
    /// <param name="context">Entry number, period, actor and operation id.</param>
    /// <param name="clock">The injected clock.</param>
    public static Result<JournalEntry> ForExpensePosted(
        ExpensePostedEvent e,
        PostingContext context,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(clock);

        var creditAccount = e.SettledMethod switch
        {
            PaymentMethod.Cash => AccountCodes.CashOnHand,
            PaymentMethod.Card or PaymentMethod.Transfer => AccountCodes.Bank,
            PaymentMethod.Cheque => AccountCodes.ChequesPayable,
            _ => AccountCodes.AccountsPayable,
        };

        var now = clock.UtcNow;
        var amount = new Money(Math.Abs(e.AmountMinor), e.Currency);
        var debitAccount = ExpenseCategories.AccountCodeFor(e.CategoryCode);
        var lines = new List<JournalLine>(2);
        // A reversing expense mirrors the original: the expense account is credited back and the
        // settlement account debited, so the pair nets to zero per account.
        Add(lines, now, e.IsReversal ? EntrySide.Credit : EntrySide.Debit, debitAccount, amount,
            e.IsReversal ? $"{e.CategoryCode} expense reversal" : $"{e.CategoryCode} expense");
        Add(lines, now, e.IsReversal ? EntrySide.Debit : EntrySide.Credit, creditAccount, amount,
            e.IsReversal ? "Expense settlement reversal" : "Expense settlement");

        return Build(
            e.TenantId,
            context,
            JournalSource.Expense,
            lines,
            e.IsReversal ? $"Expense {e.ExpenseId} ({e.CategoryCode}) reversal posted" : $"Expense {e.ExpenseId} ({e.CategoryCode}) posted",
            nameof(Expense),
            e.ExpenseId,
            now);
    }

    // ---- shared plumbing ---------------------------------------------------

    /// <summary>Appends a line unless the amount is zero — a zero posting is noise, not a fact.</summary>
    private static void Add(
        List<JournalLine> lines,
        DateTimeOffset now,
        EntrySide side,
        string accountCode,
        Money amount,
        string memo)
    {
        if (amount.Minor == 0L)
        {
            return;
        }

        lines.Add(new JournalLine(IdGenerator.NewId(now), accountCode, side, amount.Abs(), memo, null));
    }

    /// <summary>The single exit of every rule: <see cref="JournalEntry.Create"/> validates it.</summary>
    private static Result<JournalEntry> Build(
        string tenantId,
        PostingContext context,
        JournalSource source,
        IReadOnlyList<JournalLine> lines,
        string memo,
        string sourceEntityType,
        string sourceEntityId,
        DateTimeOffset now) =>
        JournalEntry.Create(
            tenantId,
            context.EntryNumber,
            context.PeriodId,
            source,
            lines,
            memo,
            context.PostedByUserId,
            now,
            now,
            sourceEntityType,
            sourceEntityId,
            context.OperationId);
}
