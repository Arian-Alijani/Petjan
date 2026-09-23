# Phase A07 — Domain: Sales & Cashier

**Band:** M · **Track:** A · **Depends on:** A04, A06 · **Satisfies:** R-B01, R-B17
**Runnable after this phase:** desktop offline yes · server required no

> **Track A testing policy (see ROADMAP §3.10).** This phase writes **no tests** and runs **no test
> suite**. It ships domain behavior only. All tests for every Track A behavior are authored and executed
> once, in **Phase A12**. The single verification here is a lightweight build (`scripts/sandbox/build.sh
> full` = 0/0), run only if you need to confirm the new code compiles.

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §6 (items 7, 9), §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §8.3 (settlement states), §8.5 (posted invoice immutable; payment idempotent),
  §11.1 (money), §21 (A-RULE 002, 003, 006)
- `src/Animora.Domain/ValueObjects/Money.cs`, `src/Animora.Domain/Clinical/VisitServiceLine.cs`,
  `src/Animora.Domain/Boarding/StayItem.cs`, `src/Animora.Domain/Results/ErrorCodes.cs`
- `src/Animora.Contracts/Sales/SalesModels.cs`, `src/Animora.Contracts/Cashier/CashierModels.cs`

## Goal
Model the money-facing core: the invoice aggregate with lines, discounts, tax and insurance coverage, an
immutable-after-posting state machine, payments as idempotent compensating-entry-only facts, and the
cash session with its open/close invariants.

## Design decisions (pre-made — implement, do not re-derive)
- **Folders:** `src/Animora.Domain/Sales/`, `src/Animora.Domain/Cashier/`.
- `Sales/Invoice.cs` — `sealed class Invoice : AggregateRoot`:
  `InvoiceNumber` (per-tenant sequential, assigned by the Application layer — never by the entity),
  `InvoiceKind Kind`, `InvoiceState State`, `string? OwnerId`, `string? PetId`, `string? VisitId`,
  `string? BoardingStayId`, `string? GuestName`, `DateTimeOffset IssuedUtc`,
  `DateTimeOffset? PostedUtc`, `DateTimeOffset? SettledUtc`,
  `Money Subtotal`, `DiscountKind DiscountKind`, `decimal DiscountValue`, `Money DiscountTotal`,
  `Money TaxTotal`, `string? InsurerId`, `decimal InsurancePercent`, `Money InsuranceCoverage`,
  `Money GrandTotal`, `Money PaidTotal`, `Money DueTotal`,
  `SettlementStatus Settlement`, `string? OperationId`,
  `string CreatedByUserId`, `string? Notes`,
  `IReadOnlyList<InvoiceLine> Lines`, `IReadOnlyList<InvoiceAdjustment> Adjustments`.
  `enum InvoiceKind { Visit, Boarding, DirectSale, Purchase Return }` → exact members:
  `{ Visit, Boarding, DirectSale, SalesReturn }`.
  `enum InvoiceState { Draft, Posted, PartiallyPaid, Paid, Void, Refunded }`.
  **State machine (the only allowed transitions):**
  ```
  Draft         -> Posted | Void
  Posted        -> PartiallyPaid | Paid | Void
  PartiallyPaid -> Paid | Refunded
  Paid          -> Refunded
  Void          -> terminal
  Refunded      -> terminal
  ```
  **[INVARIANT] Immutability after posting:** once `State != Draft`, `AddLine`, `RemoveLine`,
  `EditLine`, `SetDiscount` and `SetInsurance` all return `INVOICE_IMMUTABLE`. A change after posting is
  expressed as an `InvoiceAdjustment` (credit note / reversal), never an edit (A-RULE 002).
  `Void` is allowed from `Draft` and `Posted` only while `PaidTotal == Money.Zero`; a paid invoice must
  be refunded instead (`INVOICE_VOID_AFTER_PAYMENT`).
  Methods returning `Result`: `AddLine`, `RemoveLine`, `EditLine`, `SetDiscount`, `SetInsurance`,
  `AttachVisit`, `AttachStay`, `AssignOwner`, `Post(IClock)`, `ApplyPayment(Payment)`,
  `ApplyRefund(InvoiceAdjustment)`, `AddCreditNote(InvoiceAdjustment)`, `Void(string reason, IClock)`,
  `MarkSettlement(SettlementStatus, string? errorCode)`.
  `Post` raises `InvoicePostedEvent`; `ApplyPayment` raises `PaymentRegisteredEvent`;
  `ApplyRefund` raises `InvoiceRefundedEvent`; `Void` raises `InvoiceVoidedEvent`.
- `Sales/InvoiceLine.cs` — `sealed record InvoiceLine(string LineId, InvoiceLineKind Kind,
  string? CatalogItemId, string? ProductId, string? VariantId, string Title,
  Quantity Quantity, Money UnitPrice, decimal DiscountPercent, decimal TaxPercent,
  InvoiceLineSource Source, string? SourceEntityId)` with computed
  `Money LineGross`, `Money LineDiscount`, `Money LineTax`, `Money LineNet`.
  `enum InvoiceLineKind { Service, Product, Boarding, Custom }`,
  `enum InvoiceLineSource { Visit, Stay, Catalog, Manual }`.
- `Sales/InvoiceTotals.cs` — static, the **single** totals algorithm (`InvoiceCalculator` behavior):
  ```
  Subtotal          = Σ LineGross
  LineDiscountTotal = Σ LineDiscount                       (per-line percentages)
  InvoiceDiscount   = DiscountKind.Percent ? (Subtotal - LineDiscountTotal).ApplyPercent(DiscountValue)
                                           : Money(DiscountValue minor units)
  DiscountTotal     = LineDiscountTotal + InvoiceDiscount
  TaxTotal          = Σ (LineNetBeforeTax.ApplyPercent(TaxPercent))
  Taxable base      = line net after BOTH line and pro-rated invoice discount
  InsuranceCoverage = (Subtotal - DiscountTotal + TaxTotal).ApplyPercent(InsurancePercent)
  GrandTotal        = Subtotal - DiscountTotal + TaxTotal - InsuranceCoverage
  DueTotal          = GrandTotal - PaidTotal - Σ Adjustments.Amount(signed)
  ```
  Rounding is `Money.ApplyPercent` only (away from zero, minor unit). The invoice discount is pro-rated
  over taxable lines with `Money.Allocate` so no minor unit is lost.
  Invariant checked in `Post`: recomputed totals must equal the stored ones (`INVOICE_TOTAL_MISMATCH`),
  and `GrandTotal >= Money.Zero` (`INVOICE_NEGATIVE_TOTAL`).
- `Sales/Payment.cs` — `sealed class Payment : SyncableEntity` (**append-only**):
  `InvoiceId`, `PaymentMethod Method`, `Money Amount` (positive for a receipt, negative for a refund —
  the compensating entry), `DateTimeOffset ReceivedUtc`, `string ReceivedByUserId`,
  `string? CashSessionId`, `string? ChequeId`, `string? CardReference`, `string? OperationId`,
  `SettlementStatus Settlement`, `string? Note`, `string? ReversesPaymentId`.
  `enum PaymentMethod { Cash, Card, Transfer, Cheque, Credit, Insurance, Wallet }`.
  **[INVARIANT] A payment is never edited or deleted.** A reversal is a new `Payment` with a negative
  `Amount` and `ReversesPaymentId` set (A-RULE 002/003). `OperationId` is the idempotency key linking
  it to its business command (A-RULE 006) — a second application of the same `OperationId` is rejected
  by `Invoice.ApplyPayment` with `PAYMENT_DUPLICATE_OPERATION`.
  Invariant: a `Cheque` payment requires `ChequeId`; a `Cash` payment requires `CashSessionId`.
- `Sales/InvoiceAdjustment.cs` — `sealed record InvoiceAdjustment(string AdjustmentId,
  AdjustmentKind Kind, Money Amount, string Reason, DateTimeOffset CreatedUtc,
  string CreatedByUserId, string? OperationId, string? ReversesAdjustmentId)`.
  `enum AdjustmentKind { CreditNote, Refund, WriteOff, RoundingCorrection, LineReturn }`.
  This is the **only** mechanism that changes a posted invoice's economics.
- `Sales/SettlementStatus.cs` — `enum SettlementStatus { Draft, LocallyPosted, PendingSettlement,
  Settled, Rejected, Conflict, Reversed, NeedsAttention }` — the exact `ARCHITECTURE.md` §8.3 list.
  **Defined here once** and reused by B02, D05, E06 (never redefined).
- `Cashier/CashSession.cs` — `sealed class CashSession : AggregateRoot`:
  `SessionNumber`, `string CashierUserId`, `string? TerminalCode`, `CashSessionState State`,
  `DateTimeOffset OpenedUtc`, `DateTimeOffset? ClosedUtc`, `Money OpeningFloat`,
  `Money DeclaredClosing`, `Money ExpectedClosing`, `Money Variance`,
  `string? CloseNote`, `string? ClosedByUserId`, `IReadOnlyList<CashMovement> Movements`.
  `enum CashSessionState { Open, Closing, Closed, Reconciled }`.
  **State machine:** `Open -> Closing -> Closed -> Reconciled`; no backward transition.
  Invariants:
  - Only one `Open` session per `(TenantId, CashierUserId, TerminalCode)` — enforced by a unique
    filtered index (A09) plus an Application-layer check; the entity exposes
    `static Result EnsureNoOpenSession(bool anyOpenExists)` returning `CASH_SESSION_ALREADY_OPEN`.
  - `ExpectedClosing = OpeningFloat + Σ CashMovement.Amount(signed)`; `Variance =
    DeclaredClosing - ExpectedClosing`. A non-zero variance is **allowed** but requires a non-empty
    `CloseNote` (`CASH_VARIANCE_NOTE_REQUIRED`).
  - `Close` is forbidden while any linked invoice is `PendingSettlement`
    (`CASH_SESSION_UNSETTLED_COMMANDS`) — the caller supplies that flag.
  - A closed session is immutable (`CASH_SESSION_CLOSED`); a correction is a new session movement in
    the next session referencing the prior one.
  Methods: `AddMovement`, `BeginClose(Money declared, string? note, IClock, bool hasUnsettled)`,
  `Close(IClock)`, `Reconcile(string reconciledByUserId, IClock)`.
  Raises `CashSessionOpenedEvent`, `CashSessionClosedEvent`.
- `Cashier/CashMovement.cs` — `sealed record CashMovement(string MovementId, CashMovementKind Kind,
  Money Amount, string? InvoiceId, string? PaymentId, string Reason,
  DateTimeOffset OccurredUtc, string RecordedByUserId, string? OperationId)`.
  `enum CashMovementKind { SalePayment, RefundOut, PettyCashOut, FloatIn, BankDropOut,
  CorrectionIn, CorrectionOut }`. Sign rule: `SalePayment, FloatIn, CorrectionIn` positive;
  `RefundOut, PettyCashOut, BankDropOut, CorrectionOut` negative — enforced in a `Create` factory.
- **Domain events** in `src/Animora.Domain/Events/SalesEvents.cs`: `InvoicePostedEvent(InvoiceId,
  OwnerId, GrandTotalMinor, Currency, VisitId, BoardingStayId)`,
  `PaymentRegisteredEvent(PaymentId, InvoiceId, AmountMinor, Currency, Method)`,
  `InvoiceRefundedEvent(InvoiceId, AdjustmentId, AmountMinor, Currency)`,
  `InvoiceVoidedEvent(InvoiceId, Reason)`, `CashSessionOpenedEvent(SessionId, CashierUserId)`,
  `CashSessionClosedEvent(SessionId, VarianceMinor, Currency)`.
- **New error codes:** `INVOICE_IMMUTABLE`, `INVOICE_INVALID_TRANSITION`, `INVOICE_TOTAL_MISMATCH`,
  `INVOICE_NEGATIVE_TOTAL`, `INVOICE_EMPTY`, `INVOICE_VOID_AFTER_PAYMENT`,
  `PAYMENT_EXCEEDS_DUE`, `PAYMENT_DUPLICATE_OPERATION`, `PAYMENT_METHOD_REFERENCE_REQUIRED`,
  `CASH_SESSION_ALREADY_OPEN`, `CASH_SESSION_CLOSED`, `CASH_VARIANCE_NOTE_REQUIRED`,
  `CASH_SESSION_UNSETTLED_COMMANDS`, `ADJUSTMENT_EXCEEDS_PAID`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Sales/Invoice.cs` | create | invoice aggregate + state machine + immutability |
| `src/Animora.Domain/Sales/InvoiceLine.cs` | create | line value object + computed amounts |
| `src/Animora.Domain/Sales/InvoiceTotals.cs` | create | the single totals algorithm |
| `src/Animora.Domain/Sales/Payment.cs` | create | append-only payment + reversal linkage |
| `src/Animora.Domain/Sales/InvoiceAdjustment.cs` | create | credit note / refund / write-off record |
| `src/Animora.Domain/Sales/SettlementStatus.cs` | create | the 8 settlement states (defined once) |
| `src/Animora.Domain/Cashier/CashSession.cs` | create | session aggregate + open/close invariants |
| `src/Animora.Domain/Cashier/CashMovement.cs` | create | signed cash movement + factory |
| `src/Animora.Domain/Events/SalesEvents.cs` | create | 6 sales/cashier events |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append the 14 codes |

**No test files in this phase.** Tests for this phase's behavior are authored in **A12**; the
"Test coverage owed to A12" section below records exactly what A12 must cover.

## Implementation steps
1. Create `Sales/` and add `SettlementStatus`, `InvoiceLineKind`/`InvoiceLineSource`, `InvoiceLine`.
2. Implement `InvoiceTotals` exactly as specified, using `Money.Allocate` for the pro-rated invoice
   discount and `Money.ApplyPercent` for every percentage.
3. Implement `Invoice`: private line/adjustment lists, a `Result Transition(InvoiceState)` helper, the
   immutability guard as a single private `Result EnsureDraft()` called by every mutator,
   `RecalculateTotals()` after each change, and the `Post` invariants.
4. Add `Payment` (factory-validated method references, no mutators) and `InvoiceAdjustment`.
   `Invoice.ApplyPayment` rejects a duplicate `OperationId` and an amount above `DueTotal`
   (`PAYMENT_EXCEEDS_DUE`), then advances `PartiallyPaid`/`Paid`.
5. Create `Cashier/` and add `CashMovement` (sign factory) and `CashSession` with the expected/variance
   computation and the four guards.
6. Add `Events/SalesEvents.cs` and append the 14 error codes.
7. **No tests here.** Record the coverage owed to A12 (below).
8. Lightweight build check only, if needed: `build.sh full` = 0/0. Do **not** run `test.sh`.
   `run.sh` = `SMOKE RUN OK`.

## Test coverage owed to A12 (author + run in A12, not here)
- `InvoiceStateMachineTests`: all `InvoiceState` pairs (only the tabled transitions succeed); `Void`
  after a partial payment → `INVOICE_VOID_AFTER_PAYMENT`.
- `InvoiceImmutabilityTests`: **every** mutator after `Post` returns `INVOICE_IMMUTABLE` (all 5).
- `InvoiceTotalsTests`: a 7% invoice discount on three lines allocated with no lost minor unit (sum of
  allocations == total discount); an insurance-coverage-at-30% case; a recomputation mismatch detected
  by `Post` as `INVOICE_TOTAL_MISMATCH`.
- `PaymentTests`: over-payment → `PAYMENT_EXCEEDS_DUE`; duplicate `OperationId` →
  `PAYMENT_DUPLICATE_OPERATION` with `PaidTotal` unchanged; reversal; method references;
  `Payment`/`InvoiceAdjustment` expose no setter (reflection).
- `CashSessionTests`: expected/variance math; variance with an empty note →
  `CASH_VARIANCE_NOTE_REQUIRED`; close with unsettled commands → `CASH_SESSION_UNSETTLED_COMMANDS`;
  closed-session immutability.

## Contracts / schema added
- Entities: `Invoice`, `Payment`, `CashSession`.
- Value objects: `InvoiceLine`, `InvoiceAdjustment`, `CashMovement`.
- Enums: `InvoiceKind`, `InvoiceState`, `InvoiceLineKind`, `InvoiceLineSource`, `PaymentMethod`,
  `AdjustmentKind`, `SettlementStatus`, `CashSessionState`, `CashMovementKind`.
- Statics: `InvoiceTotals`. Events: 6 new records.
- `ErrorCodes` += 14. No migration, no `Animora.Contracts` change. **No test files** (owed to A12).

## Rules that apply here
- **A-RULE 002** — a posted invoice is immutable; `InvoiceAdjustment` is the only economic change path.
- **A-RULE 003** — no client-wins/server-wins anywhere in this phase; every correction is a
  compensating entry with a reason.
- **A-RULE 006** — `Payment.OperationId` plus `ApplyPayment`'s duplicate check make a replayed command
  a no-op, not a second financial effect.
- **ARCHITECTURE.md §8.3** — `SettlementStatus` matches the spec's 8 states exactly, and `Invoice`
  carries it so the UI can always show it (never hidden).
- **CONVENTIONS §4 / STACK.md §3.3** — all amounts are `Money` (minor units); no `decimal` money field.
- **CONVENTIONS §6 item 9** — invoice/payment conflicts are domain-decided, never LWW.

## Out of scope
- **All tests for this phase's behavior → A12** (this phase writes and runs none).
- Ledger journal entries produced by posting → A08. Cheque lifecycle → A08.
- Invoice-number sequence assignment → B01. Stock movements on sale → C07/D07.
- Command envelope, `PendingCommand`, local settlement pipeline → B02.
- `ISalesQueryService`/`ICashierService` implementations → C07, C08.
- Server-side posting handler → D07.

## Acceptance criteria
- Build (lightweight, only if run): `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **No test suite is run in this phase** (`test.sh` is A12's; do not invoke it here).
- The "Test coverage owed to A12" section is present and names every invariant A12 must assert.
- `Animora.Domain.csproj` still has zero references.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark A07 done; advance "Next phase" to `A08`; append a "Repo state" entry listing: the 9 new files, the
entities/enums/events added, the `InvoiceTotals` algorithm and `SettlementStatus` definition,
`ErrorCodes` += 14, **no tests written (owed to A12)**, no DI change, no mock deleted, no migration,
deviations.
