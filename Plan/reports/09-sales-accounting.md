# 09 — Sales + Accounting System Audit (audit target #11)

Scope: Domain `Sales.*` / `Cashier.*` / `Finance.*`; Application finance use cases + outbox; Persistence
finance projections (`DailyFinanceFact`, `ProductSalesFact`, `OwnerFinancialSnapshot`), the number
allocators (`InvoiceNumberAllocator`, `JournalNumberAllocator`, `ExpenseNumberAllocator`,
`LocalSequenceAllocator` over `PipelineSql.NextSequenceAsync`), `FinanceProjectionStore` (live outbox path)
and `FinanceProjectionRebuilder` (full recompute); Client.Services `Sales/Cashier/Cheques`; Contracts finance
DTOs; server `Commands/ServerFinancialConcurrencyGuard`, `Sync/FinanceEndpoints`.

**Verification outcome.** The accounting core is strong and well-defended: money is integer-minor
everywhere (`Money.Minor` = `long`), `InvoiceTotals.Compute` is the single totals algorithm shared by client
and server, `PostingRules` is the single authoritative event→journal table (balance/positivity/single-currency
structurally guaranteed by `JournalEntry.Create`), payments are append-only with `OperationId` idempotency
(A-RULE 006), the invoice state machine freezes post-posting economics (A-RULE 002), and document numbers are
allocated by a single-statement atomic upsert (`INSERT … ON CONFLICT DO UPDATE … RETURNING`) that SQLite's own
locking arbitrates — so there is no read-modify-write race and no duplicate numbers. The findings below are a
small set of real divergences and latent limits, not a broken ledger.

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| SAC-01 | High | bug / inconsistency | Void handling **diverges** between the live projection path and the rebuild: `FinanceProjectionStore` books a void's grand total as a `refund` bump (+ reverses revenue), but `FinanceProjectionRebuilder` simply excludes void invoices and books **no** refund — so a rebuild silently changes the daily `Refund`/`PaidInvoices` figures vs the warm path |
| SAC-02 | High | latent | Document-number format `{sequence:D6}` (`PREFIX-YY-NNNNNN`) overflows at the 1,000,000th invoice/journal/expense in a single Jalali year: the number silently widens to 7 digits, breaking the fixed-width contract (sorting, reports, any column sized for 6) |
| SAC-03 | Medium | bug | Fixed-amount invoice discount `new Money((long)discountValue, currency)` **truncates** a decimal and is **uncapped** — a fixed discount larger than the net produces a negative line base / negative grand total; only `Post` rejects it (`INVOICE_NEGATIVE_TOTAL`), a draft can carry and display nonsense totals |
| SAC-04 | Medium | perf / latent | `FinanceProjectionRebuilder.FoldPeerFinanceAsync` loads **every** local invoice id into an in-memory `HashSet` (`db.Set<Invoice>().Select(i => i.Id).ToList()`) on each rebuild — unbounded with invoice count; the rebuild also streams all invoices, all lines, all sale movements, all expenses and all negative payments with no date bound |
| SAC-05 | Medium | inconsistency | `RecurringExpenseSchedule.IsDue(dayOfMonth, currentDayOfMonth, …)` compares against the **raw** `DayOfMonth`, while `Evaluate` compares against `EffectiveDueDay` (clamped to month length). A day-31 template in a 30-day month is "Due/Overdue" under `Evaluate` on the 30th but never `IsDue` — two schedule authorities disagree |
| SAC-06 | Medium | latent / bug | A bounced cheque can return to `InPortfolio` → `Deposited` → `Cleared`, raising a **second** `ChequeClearedEvent`; `MarkBounced` already raised `ChequeBouncedEvent` (AR reinstated). Nothing in the aggregate blocks the re-clear from re-posting the clearing journal entry — relies entirely on the posting layer de-duping by event id |
| SAC-07 | Low | perf | `FinanceProjectionStore` resolves the invoice with `FirstOrDefaultAsync` (tracking) **per event** inside the outbox drain; a posted invoice with N product lines is re-materialized with its full `Lines` collection for the posted event and again for any later void — fine at current volume, an N+1 shape as the outbox batch grows |
| SAC-08 | Info | — | Core verified sound: integer-minor money, single `InvoiceTotals`/`PostingRules`, append-only payments with `OperationId` idempotency, atomic single-statement number allocation, server `MoneyResourceLock` ordering (numbering/directory → `ledger-period` → aggregate → stock) preventing cross-command deadlock |

---

## SAC-08 (baseline verification — the accounting core is sound)

- Severity: Info
- Location: `Domain/Sales/InvoiceTotals.cs`, `Domain/Sales/Invoice.cs`, `Domain/Sales/Payment.cs`,
  `Domain/Finance/PostingRules.cs`, `Persistence.Sqlite/Commands/PipelineSql.cs`,
  `Server/Commands/ServerFinancialConcurrencyGuard.cs`.
- Evidence:
  - **Single totals algorithm.** `InvoiceTotals.Compute` is the only code that computes invoice economics;
    `Invoice.RecalculateTotals` delegates to it and `Invoice.Post` re-verifies stored == recomputed
    (`INVOICE_TOTAL_MISMATCH`). Rounding happens only inside `Money.ApplyPercent` (away-from-zero on the minor
    unit) and the invoice discount is spread by largest-remainder `AllocateByWeight`, so shares sum back
    exactly — no minor unit lost or invented.
  - **Idempotent, append-only payments.** `Payment` has no mutators (`private init`); `Invoice.ApplyPayment`
    rejects a replayed `OperationId` (`PAYMENT_DUPLICATE_OPERATION`) and leaves `PaidTotal` untouched; a refund
    is a new negative `Payment` with `ReversesPaymentId`.
  - **Single posting table.** `PostingRules` translates each domain event to a `JournalEntry` whose
    `Create` structurally guarantees balance, positivity, single currency and account postability — client (A10)
    and server (D07/D09) dispatch into the same pure functions, so the two ledgers cannot diverge.
  - **Atomic numbering.** `PipelineSql.NextSequenceAsync` is one statement
    (`INSERT … ON CONFLICT(Name) DO UPDATE SET Value = Value + 1 RETURNING Value`) joined to the ambient
    transaction, so a failed post rolls the counter back (no gap) and two concurrent callers never share a value.
  - **Server lock ordering.** `ServerFinancialConcurrencyGuard` acquires money resources in a fixed order
    (global `purchasing`/`ledger-period` → `AggregateType:id` → `stock:prod:variant`) so concurrent financial
    commands cannot deadlock.
- Why it matters: establishes the ledger's structural correctness so the findings below are read as residual
  divergences/limits, not systemic money bugs.
- Confidence: High.

## SAC-01 Void is projected differently by the live path and the rebuild

- Severity: High
- Class: bug / inconsistency (projection vs rebuild divergence)
- Location: `Persistence.Sqlite/Finance/FinanceProjectionStore.cs` (`InvoiceVoidedEvent` case) vs
  `Persistence.Sqlite/Finance/FinanceProjectionRebuilder.cs` (`RebuildAsync`).
- Evidence:
  - Live path on void:
    ```csharp
    (await Daily(e.TenantId, day, ct)).Apply(revenue: -invoice.Subtotal.Minor,
        discount: -invoice.DiscountTotal.Minor, insurance: -invoice.InsuranceCoverage.Minor,
        refund: invoice.GrandTotal.Minor, invoices: -1);
    ```
    i.e. it reverses the revenue/discount/insurance **and** adds the grand total to `Refund`.
  - Rebuild path only ever folds non-void invoices:
    ```csharp
    .Where(i => i.State != InvoiceState.Draft && i.State != InvoiceState.Void && i.PostedUtc != null)
    ```
    A voided invoice therefore contributes nothing — revenue naturally nets to its never-posted baseline, but
    **no `Refund` is booked** and the day's `Invoices` count is the posted-minus-void residual, not
    "posted then −1". The two methods produce different `DailyFinanceFact.Refund` and `Invoices` for any day
    containing a void.
- Why it is a problem: the facts are documented as "a cache with a proof" — `RebuildAsync` must reproduce the
  live path exactly. It does not for voids: a maintenance rebuild (or the peer-finance-triggered rebuild) will
  silently rewrite the daily refund total and invoice count for every day that had a void, so the dashboard
  figure changes with no user action and the "proof" no longer matches the warm path.
- Scenario / impact: clinic voids a mistakenly-posted invoice; dashboard shows a refund of its grand total
  until the next rebuild, after which the refund disappears and the day's counts shift — finance numbers that
  move on their own undermine trust in «آمار»/accounting. Also affects reconciliation against the ledger.
- Confidence: High (direct code comparison; the divergence is structural, not data-dependent).

## SAC-02 Document-number format overflows past 999,999 per year

- Severity: High
- Class: latent (overflow / format contract)
- Location: `Application/Numbering/DocumentNumber.Format`:
  ```csharp
  return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{jalaliYear[^2..]}-{sequence:D6}");
  ```
  consumed by `InvoiceNumberAllocator`, `JournalNumberAllocator`, `ExpenseNumberAllocator`.
- Why it is a problem: `D6` pads to a **minimum** of 6 digits; it does not cap. The 1,000,000th sequence value
  in a single Jalali year renders as a 7-digit `NNNNNNN`, breaking the fixed-width `PREFIX-YY-NNNNNN` contract.
  Any lexicographic sort of numbers (reports, exports, UI columns, a fixed-width DB/printed field) orders
  `...-999999` after `...-1000000` and anything sized for 11 chars truncates. The counter itself is a `long`
  so it does not wrap, but the *format* silently changes shape.
- Scenario / impact: a high-volume multi-site tenant crossing 1M journal/invoice rows in a year gets
  mis-sorted and potentially truncated document numbers — a correctness/legal-document problem that only
  appears years in. Horizon: large tenant, multi-year. Fix is cheap (widen or range-check), flagged as latent.
- Confidence: High.

## SAC-03 Fixed-amount invoice discount is truncated and uncapped

- Severity: Medium
- Class: bug
- Location: `Domain/Sales/InvoiceTotals.cs`:
  ```csharp
  var invoiceDiscount = discountKind == DiscountKind.Percent
      ? netAfterLineDiscount.ApplyPercent(discountValue)
      : new Money((long)discountValue, currency);
  ```
  and `Invoice.SetDiscount` (which only rejects `value < 0` and `percent > 100`).
- Why it is a problem: for `DiscountKind.Amount` the decimal `discountValue` is **cast** `(long)` — it
  truncates any fractional minor unit rather than rounding through the product's single rounding boundary
  (`Money.ApplyPercent`). More importantly there is **no upper bound**: a fixed discount larger than
  `netAfterLineDiscount` makes `DiscountTotal > Subtotal`, so `GrandTotal` and each line's taxable base go
  negative. A draft happily stores and displays these negative totals; the only guard is `Invoice.Post`
  rejecting `GrandTotal.IsNegative` / zero — the bad state is reachable and visible pre-post.
- Scenario / impact: a cashier typing a fixed discount of e.g. 10,000,000 on a 500,000 invoice sees a negative
  total in the UI (and, if tax lines exist, negative tax bases feeding `ApplyPercent`) until they try to post.
  Truncation also means a fixed discount entered with sub-unit precision is quietly off by up to one minor unit.
- Scenario / impact (continued): because `SetInsurance`/tax run on the post-discount base, an over-discount can
  also drive insurance coverage math on a negative base.
- Confidence: High.

## SAC-04 Rebuild loads all invoice ids + full unbounded scans

- Severity: Medium
- Class: perf / latent (unbounded growth)
- Location: `Persistence.Sqlite/Finance/FinanceProjectionRebuilder.RebuildAsync` /
  `FoldPeerFinanceAsync`:
  ```csharp
  var localInvoiceIds = (await db.Set<Invoice>().AsNoTracking()
          .Select(i => i.Id).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
  ```
  plus `invoices`, `lines` (`SelectMany`), `movements` (all `MovementKind.Sale`), `expenses`, `refunds` each
  materialized with `ToListAsync` and **no date/period bound**.
- Why it is a problem: a full rebuild (maintenance `RebuildFinanceProjections`, or triggered by the peer pull)
  pulls the entire financial history into memory — every invoice id, every posted line, every sale movement —
  on a single-threaded SQLite store. Cost grows linearly with the clinic's lifetime data; the `HashSet<string>`
  of all invoice ids is the clearest unbounded allocation. There is no incremental/windowed rebuild.
- Scenario / impact: on a clinic with years of data, a rebuild stalls the local store (and the UI if run on a
  foreground scope) with a large transient allocation; the peer-finance pull that *triggers* a rebuild makes a
  routine sync increasingly expensive over time. Horizon: scales with data age. Cheap mitigation: bound the
  rebuild to affected days/periods; structural fix: incremental fact maintenance only.
- Confidence: High (code reads the full tables unconditionally).

## SAC-05 Two disagreeing recurring-expense "due" authorities

- Severity: Medium
- Class: inconsistency (latent)
- Location: `Domain/Finance/RecurringExpense.cs` — `RecurringExpenseSchedule`:
  ```csharp
  public static bool IsDue(int dayOfMonth, int currentDayOfMonth, bool alreadyPaidThisPeriod) =>
      currentDayOfMonth >= dayOfMonth && !alreadyPaidThisPeriod;          // raw dayOfMonth
  …
  var due = EffectiveDueDay(dayOfMonth, daysInMonth);                     // clamped
  if (currentDayOfMonth > due) return RecurringDueState.Overdue;
  ```
- Why it is a problem: `Evaluate` correctly clamps a day-31 template to the last day of a short month via
  `EffectiveDueDay`, but the standalone `IsDue` compares against the **raw** `DayOfMonth`. For a template due on
  the 31st in a 30-day month, `Evaluate` reports `Due`/`Overdue` on the 30th while `IsDue(31, 30, …)` returns
  `false` — the two schedule APIs give opposite answers. Whichever caller uses `IsDue` (reminders / badge) will
  never fire for day-29/30/31 templates in short months.
- Scenario / impact: «اجاره مطب» set to day 31 is never reminded/flagged as due in 30-day (and February-length)
  months if the reminder path calls `IsDue`; the dashboard state (`Evaluate`) and the reminder (`IsDue`) show
  conflicting status. Latent: only manifests in short months for high-day templates.
- Confidence: Medium (bug is in the pure rule pair; impact depends on which API the Application reminder path
  calls — both are public and the names invite misuse).

## SAC-06 Re-clear of a recovered bounced cheque can double-post clearing

- Severity: Medium
- Class: latent / bug
- Location: `Domain/Finance/Cheque.cs` — state machine + `MarkCleared`:
  ```csharp
  (ChequeState.Bounced, ChequeState.InPortfolio) => true,   // recover
  (ChequeState.InPortfolio, ChequeState.Deposited) => true, // re-deposit
  (ChequeState.Deposited, ChequeState.Cleared)   => true,   // re-clear → 2nd ChequeClearedEvent
  ```
- Why it is a problem: the lifecycle deliberately allows a bounced cheque to re-enter the portfolio and be
  deposited again. If it then clears, `MarkCleared` raises a **second** `ChequeClearedEvent`. The aggregate
  carries a single `JournalEntryId` and does not record that a clearing entry was already posted, nor does it
  block re-clearing. Correctness of the ledger therefore depends entirely on the posting/outbox layer
  de-duplicating by event id (and on the earlier `ChequeBouncedEvent` having reversed the first clear — but the
  first attempt never cleared, it bounced, so the AR reinstatement from bounce and a later genuine clear are
  both legitimate). The risk is a *ret* of the cleared event (replay) or a second genuine clear producing two
  debit-bank / credit-cheque entries.
- Scenario / impact: a cheque bounces, the customer re-presents it, it clears — the clinic can end up with the
  bank debited twice for one leaf if the clearing event is reprocessed, unless downstream posting is strictly
  idempotent on the event id. The domain gives no second line of defence.
- Confidence: Medium (needs confirmation that the posting pipeline de-dupes clearing events by id; the domain
  invariant gap is certain).

## SAC-07 Per-event tracking invoice fetch in the projection drain

- Severity: Low
- Class: perf (N+1 shape)
- Location: `Persistence.Sqlite/Finance/FinanceProjectionStore.ApplyAsync`:
  ```csharp
  var invoice = await db.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == posted.InvoiceId, ct) …
  foreach (var line in invoice.Lines.Where(…)) { … }
  ```
- Why it is a problem: each `InvoicePostedEvent`/`InvoiceVoidedEvent`/`PaymentRegisteredEvent` re-queries the
  invoice (tracked, with its `Lines`) individually as the outbox batch drains. For a batch of many finance
  events this is one round-trip per event plus the line collection load — an N+1 against the single-writer
  SQLite store. Current volumes make it immaterial; it degrades as batches grow.
- Scenario / impact: a large backlog drain (post-offline catch-up) does N invoice loads instead of a batched
  read; UI-visible only under heavy catch-up. Cheap: load the distinct invoice ids of the batch once.
- Confidence: High (code structure).
