# Phase C07 — Sales / POS

**Band:** M · **Track:** C · **Depends on:** C03, C05, C06 · **Satisfies:** R-B01, R-B02, R-B16
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §6, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (the **C01 pattern**; C03 `VisitBillingHandoff`; C05
  `BoardingBillingHandoff` + the stale-index rule; C06 `StockOnHandSnapshot`)
- `ARCHITECTURE.md` §8.3, §11.1 · `PERFORMANCE.md` §5, §6
- `src/Animora.Contracts/Sales/ISalesQueryService.cs`, `src/Animora.Contracts/Sales/SalesModels.cs`,
  `src/Animora.Contracts/Sales/CashierHistoryModels.cs`
- `src/Animora.Domain/Sales/Invoice.cs`, `src/Animora.Domain/Sales/InvoiceLine.cs`,
  `src/Animora.Domain/Sales/InvoiceTotals.cs`, `src/Animora.Domain/Sales/Payment.cs`
- `src/Animora.Application/Commands/ICommandJournal.cs`,
  `src/Animora.Application/Commands/IJournaledCommand.cs` (B02)
- `src/Animora.Application/Ports/IInvoiceRepository.cs`,
  `src/Animora.Application/Ports/IPaymentRepository.cs`
- `src/Animora.Desktop/App.axaml.cs` (line registering `ISalesQueryService`)

## Goal
The POS screen creates and posts **real invoices**: from a visit handoff, a boarding handoff or
blank; lines from the product/service catalog; discounts, insurance and payments. Posting is a
**journaled command through the B02 pipeline** — never CRUD — and it is the single place stock
consumption movements are written.

## Design decisions (pre-made — implement, do not re-derive)
- **Draft vs. posted is the axis everything hangs on.** A `Draft` invoice is ordinary local state:
  lines/discount/insurance edits are plain commands with no financial effect and
  `FinancialEffect.None`. `PostInvoiceCommand` is the transition that creates financial truth; from
  `InvoiceState.Posted` onward the invoice is **immutable** (A-RULE 002) and every correction is a
  compensating command. `EditLineAsync`/`RemoveLineAsync`/`SetDiscountAsync`/`SetInsuranceAsync` on a
  posted invoice return `INVOICE_ALREADY_POSTED` (already in `ErrorCodes` from A07).
- **Contracts mapping of the posting moment:** the Contracts surface has no explicit `PostAsync`.
  **Decision:** `TakePaymentAsync` posts the invoice first when it is still `Draft` (one transaction:
  post + register payment), and `VoidAsync` on a posted invoice issues a reversal rather than a
  delete. `DeleteInvoiceAsync` is allowed **only** for a `Draft` (otherwise
  `INVOICE_ALREADY_POSTED`). This is documented in the service XML doc and in `PROGRESS.md`; no
  Contracts member is reshaped.
- **Commands** (`UseCases/Sales/`) — journaled ones are marked ⟶:
  `CreateInvoiceFromVisitCommand` ⟶, `CreateInvoiceFromStayCommand` ⟶, `CreateBlankInvoiceCommand` ⟶,
  `AddInvoiceLineCommand` ⟶, `AddCustomInvoiceLineCommand` ⟶, `EditInvoiceLineCommand` ⟶,
  `RemoveInvoiceLineCommand` ⟶, `SetInvoiceDiscountCommand` ⟶, `SetInvoiceInsuranceCommand` ⟶,
  `AssignInvoiceOwnerCommand` ⟶, `DeleteDraftInvoiceCommand` ⟶, `PostInvoiceCommand` ⟶,
  `RegisterPaymentCommand` ⟶, `RefundInvoiceCommand` ⟶, `ReturnInvoiceLineCommand` ⟶,
  `VoidInvoiceCommand` ⟶.
  `CommandTypes`: reuse `sales.invoice.post` and `sales.payment.register` (defined in B02); add
  `sales.invoice.create`, `sales.invoice.line.add`, `sales.invoice.line.edit`,
  `sales.invoice.line.remove`, `sales.invoice.discount.set`, `sales.invoice.insurance.set`,
  `sales.invoice.owner.set`, `sales.invoice.delete`, `sales.invoice.void`, `sales.invoice.refund`,
  `sales.invoice.line.return` (11).
  Permissions: `sales.invoice.edit` for draft edits, `sales.invoice.post` for posting,
  `sales.payment.register` for payments, `sales.invoice.void` for void/refund/return.
  All carry `[Auditable(AuditCategory.Financial, ...)]`.
- **`PostInvoiceCommand` — the one transaction that must be exactly right:**
  1. validate state `Draft`, at least one line, a resolved owner, and totals recomputed by
     `InvoiceTotals` (A07) — a client-supplied total is never trusted;
  2. allocate `Invoice.Number` from `InvoiceNumberAllocator`
     (`Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs`, reusing B02's
     `LocalSequenceCounter` table with counter name `invoice:<tenantId>:<jalaliYear>`, format
     `{yy}-{seq:000000}`) — unique per tenant, gap-tolerant, never reused;
  3. transition to `InvoiceState.Posted`, stamp `PostedUtc` and `SettlementStatus.LocallyPosted`;
  4. write one `StockMovement` of `MovementKind.Sale` per stockable line (via C06's
     `StockSnapshotUpdater`), with `UnitCostMinor` copied from the latest receipt movement so C09
     can compute COGS; a line whose product is a service writes no movement;
  5. mark the originating `VisitBillingHandoff`/`BoardingBillingHandoff` row `HandoffStatus.Invoiced`
     with `InvoiceId`;
  6. raise `InvoicePostedEvent` (A07) — C09's ledger posting subscribes to it;
  7. the `CommandJournalBehavior` (B02) writes the `PendingCommand` in the same transaction, so the
     posting is settlement-ready the moment a server exists.
  Insufficient stock does **not** block posting (a clinic may sell the last unit while a count is
  stale); it records the movement and lets on-hand go negative **only** when
  `LocalStoreOptions.AllowNegativeStockOnSale` is `true` (default `true`, surfaced in settings);
  otherwise the command returns `STOCK_WOULD_GO_NEGATIVE`.
- **Payments:** `RegisterPaymentCommand(InvoiceId, PaymentMethod, Money Amount, string? Reference)`
  appends an **append-only** `Payment` row, recomputes the balance with `InvoiceBalanceQuery` (A10)
  and sets `InvoiceState.Paid`/`PartiallyPaid`. Overpayment returns `PAYMENT_EXCEEDS_BALANCE`.
  A payment is never edited or deleted — a mistake is corrected by `RefundInvoiceCommand`.
- **Refund / return / void are compensating, never mutating** (A-RULE 002):
  `VoidInvoiceCommand` creates a full-value reversal invoice (`InvoiceKind.CreditNote`, negative
  lines, `ReversesInvoiceId` set) and marks the original `InvoiceState.Voided`;
  `RefundInvoiceCommand` creates a `CreditNote` for the refunded amount plus a negative `Payment`;
  `ReturnInvoiceLineCommand` creates a `CreditNote` for the returned line **and** a
  `MovementKind.Return` stock movement. All three raise `InvoiceReversedEvent`.
- **Queries** (`UseCases/Sales/`): `ReadyForBillingQuery()` (a `UNION ALL` over the two handoff
  tables where `Status = Ready`, newest first, max 100), `InvoiceListQuery(filter, page)`,
  `InvoiceDetailQuery(invoiceId)`, `SalesCatalogQuery()` (products + variants + service defs +
  recent-item refs, cached in `TenantScopedCache` from C03, invalidated on `ProductChangedEvent`),
  `InsurersQuery()`, `SaleKpiQuery()` (today's revenue/count/average, one grouped statement),
  `SalesHistoryQuery(CashierHistoryFilter)` → `CashierHistoryPage`, `OwnerLookupQuery(string Query)`.
- **Insurance model:** `Insurer` is **reference data** (`Animora.Domain/Sales/Insurer.cs` with
  `Code`, `Name`, `CoveragePercent`, `CeilingMinor`) seeded by `LocalSeeder` (6 insurers).
  `SetInvoiceInsuranceCommand` recomputes the insurance adjustment via
  `InvoiceTotals.ApplyInsurance` — never a free-typed amount.
- **Index-based members** (`EditLineAsync`, `RemoveLineAsync` by `lineIndex`) follow **C05's
  stale-index rule**: the service resolves the `InvoiceLine.Id` from the last-read detail and the
  command takes `(InvoiceId, int Index, string ExpectedLineId)`; a mismatch returns
  `INVOICE_LINE_INDEX_STALE`.
- **Settlement visibility (R-B16):** `InvoiceDetailReadModel` carries `SettlementStatus`,
  `LastSettlementErrorCode` and `ResponsibleRole` read from the `PendingCommand` row via B02's
  `UnsettledCommandListQuery`. `SalesMapping` maps them onto the existing
  `InvoiceDetail`/`InvoiceAuditEntry` fields the UI already renders — no Contracts change.
- **Sample data:** `Seed/SalesSampleData.cs` — 6 insurers (reference), 30 invoices across states
  (draft/posted/partially paid/paid/voided) with payments and the matching stock movements, so the
  cashier, finance and history screens have real data the moment they are switched on.
- **ViewModel changes (exhaustive):** `SalePageViewModel` gains the readiness-gate line.
- **New error codes:** `INVOICE_NOT_FOUND`, `INVOICE_LINE_INDEX_STALE`, `INVOICE_NO_LINES`,
  `INVOICE_OWNER_REQUIRED`, `PAYMENT_EXCEEDS_BALANCE`, `HANDOFF_ALREADY_INVOICED`,
  `INSURER_NOT_FOUND`, `REFUND_EXCEEDS_PAID`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Sales/Insurer.cs` | create | insurance reference entity |
| `src/Animora.Application/UseCases/Sales/*.cs` | create | 8 queries + 16 commands + handlers/validators |
| `src/Animora.Application/UseCases/Sales/ReadModels/*.cs` | create | 6 read models incl. settlement fields |
| `src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs` | create | per-tenant per-year numbering |
| `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs` | create | ready-for-billing union, list, detail, KPIs, history |
| `src/Animora.Persistence.Sqlite/Configurations/Sales/InsurerConfiguration.cs` | create | mapping + unique code index |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `013_SalesInsurers` (insurers + `ReversesInvoiceId`) + snapshot |
| `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs` | modify | add `AllowNegativeStockOnSale` |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` | modify | seed 6 insurers |
| `src/Animora.Persistence.Sqlite/Seed/SalesSampleData.cs` | create | 30 invoices + payments + movements |
| `src/Animora.Client.Services/Sales/SalesQueryService.cs` | create | real `ISalesQueryService` (22 members) |
| `src/Animora.Client.Services/Sales/SalesMapping.cs` | create | read models ↔ Contracts DTOs incl. settlement |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify | add 11 types (reuse 2 from B02) |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap `ISalesQueryService` |
| `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Mocks/MockSalesQueryService.cs` | delete | replaced |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 8 codes |
| `tests/Animora.Domain.Tests/InvoiceImmutabilityTests.cs` | create | posted-invoice invariants, reversal math |
| `tests/Animora.Application.Tests/PostInvoiceCommandTests.cs` | create | the 7-step transaction, idempotent replay |
| `tests/Animora.Application.Tests/SalesCompensationTests.cs` | create | void/refund/return produce credit notes |
| `tests/Animora.Persistence.Tests/InvoiceNumberAllocatorTests.cs` | create | uniqueness under concurrency, no reuse |

## Implementation steps
1. Add `Insurer` + configuration + `ReversesInvoiceId`; generate `013_SalesInsurers`; seed 6 insurers;
   append the 8 error codes; add `AllowNegativeStockOnSale`.
2. Add `InvoiceNumberAllocator` over B02's counter table, with its concurrency test.
3. Write `SalesQueries` — the handoff `UNION ALL`, list, detail (including the settlement join),
   catalog, KPIs, history, owner lookup.
4. Write the draft-editing commands (create ×3, line add/edit/remove, discount, insurance, owner,
   delete) with the stale-index rule and validators.
5. Write `PostInvoiceCommand` exactly as the 7-step transaction; assert no client total is trusted.
6. Write `RegisterPaymentCommand`, then the three compensating commands.
7. Write `SalesMapping` (including settlement fields) and `SalesQueryService`'s 22 members.
8. Add `SalesSampleData` behind `SeedSampleData`.
9. Swap the DI line, add the ViewModel line, delete the mock.
10. Write the four test classes.
11. Verify manually: create an invoice from a completed visit and from a checked-out stay; add
    catalog and custom lines; apply a discount and an insurer; take a payment (invoice posts and
    gets a number); confirm stock dropped by the sold quantity; void, refund and return each produce
    a credit note and leave the original untouched; the settlement chip shows `LocallyPosted`.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Domain: `Insurer`, `Invoice.ReversesInvoiceId`.
- Application: 8 sales queries, 16 sales commands, 6 read models.
- Persistence: `InvoiceNumberAllocator`, `SalesQueries`, `InsurerConfiguration`, migration
  `013_SalesInsurers`, `LocalStoreOptions.AllowNegativeStockOnSale`, `SalesSampleData`, 6 seeded
  insurers.
- Client services: `SalesQueryService`, `SalesMapping`.
- Wire: 11 new `CommandTypes` entries (`sales.invoice.post`, `sales.payment.register` reused).
  `ErrorCodes` += 8. No `Animora.Contracts` change.
- DI: `ISalesQueryService` → `SalesQueryService`. **Mock deleted: `MockSalesQueryService.cs`.**

## Rules that apply here
- **A-RULE 002 / R-B22** — a posted invoice and every `Payment` are immutable; void/refund/return are
  compensating credit notes.
- **A-RULE 005 / 006 / R-B14** — posting is a journaled command with a stable `OperationId`; replay
  returns the stored result and never creates a second invoice or a second stock movement.
- **A-RULE 024 / R-B16** — the settlement status, error code and responsible role reach the UI
  through the read model; a bare failure is a defect.
- **R-B23** — sale consumption is a `StockMovement`, never a decrement of a stored quantity.
- **CONVENTIONS §5** — invoice numbering uses the counter table inside the transaction; the list and
  history queries are paged and indexed.
- **A-RULE 018** — `InvoiceDetailReadModel` is denormalized for reading; `Invoice` stays the
  normalized write model.

## Out of scope
- Cash session open/close and the cashier screen's own surface → C08.
- Ledger journal entries from `InvoicePostedEvent` → C09 (`PostingRules` already exists from A08).
- Cheque payments → C08.
- Server-side posting/settlement → D07; transport of the pending commands → E06.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `src/Animora.Desktop/Mocks/MockSalesQueryService.cs` does not exist; no DI line references it.
- `InvoiceImmutabilityTests` green: editing a line, discount or insurer on a posted invoice throws /
  returns `INVOICE_ALREADY_POSTED`; a credit note's totals are the exact negation of what it reverses.
- `PostInvoiceCommandTests` green: posting writes exactly one `PendingCommand`, one stock movement
  per stockable line, marks the handoff `Invoiced`, and allocates a unique number; replaying the same
  `OperationId` returns the stored result with **no** second movement and **no** second number;
  posting an invoice whose handoff is already `Invoiced` returns `HANDOFF_ALREADY_INVOICED`; a
  tampered client total is ignored in favor of `InvoiceTotals`.
- `SalesCompensationTests` green: void, refund and return each create exactly one `CreditNote`
  linked by `ReversesInvoiceId`, leave the original rows byte-identical, and (for return) write one
  `MovementKind.Return` movement; a refund above the paid amount returns `REFUND_EXCEEDS_PAID`.
- `InvoiceNumberAllocatorTests` green: 200 concurrent allocations yield 200 distinct numbers with no
  reuse after a rollback.
- Manual: the full POS flow works offline and survives restart; the settlement chip is visible.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C07 done; advance "Next phase" to `C08`; append a "Repo state" entry listing: the draft-vs-posted
rule and the `TakePaymentAsync`-posts decision, the 7-step `PostInvoiceCommand` transaction,
`InvoiceNumberAllocator` format, the compensating void/refund/return design (**C08/C09 depend on
it**), `Insurer` reference data, `AllowNegativeStockOnSale`, migration `013_SalesInsurers`, the 24
use cases, `SalesQueryService`/`SalesMapping` + the settlement fields, the DI swap, **mock deleted:
`MockSalesQueryService.cs`**, `CommandTypes` += 11, `ErrorCodes` += 8, the 4 test classes,
deviations.
