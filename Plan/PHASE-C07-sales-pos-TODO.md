# Phase C07 — Sales / POS — Execution Plan (self-contained)

> **Purpose of this file.** A model resuming in a fresh session must be able to implement
> Phase C07 end-to-end from this file **without re-reading the phase doc or re-deriving
> anything**. All names, real enum values, migration numbers, error codes and the exact
> transaction steps below were verified against the live repo on 2026-09-28. Where the
> phase doc and the actual code disagree, **the code wins** and the correction is called
> out inline as ⚠️.
>
> **Band M · Track C · Depends on C03, C05, C06 · Satisfies R-B01, R-B02, R-B16.**
> Goal: the POS screen creates and posts **real invoices** (from a visit handoff, a
> boarding handoff, or blank); lines from catalog; discounts, insurance, payments.
> Posting is a **journaled command through the B02 pipeline** — never CRUD — and is the
> single place stock consumption movements are written.

---

## 🔒 LOCKED WORKFLOW RULE (violation = project death)

After **every** task whose checkbox you tick to `[x]`, immediately run:

```bash
scripts/git/push.sh "type(scope): <what this task did>"
```

- It stages all → commits → fetch+rebase → pushes. One `✓`/`✗` line.
- Exit `3` = no credentials → call the **`setup_github_environment`** tool once, then re-run.
- Exit `2` = rebase conflict → resolve (prefer remote), re-run.
- Work on branch **`genspark_ai_developer`** (use `push.sh -b genspark_ai_developer "msg"` on the first push if not already on it).
- **Do not batch** multiple tasks into one push. One task → one push.

---

## ✅ CANONICAL FACTS (verified — do not re-investigate)

### The "C01 pattern" (every Track C phase follows it)
1. Use cases live in `src/Animora.Application/UseCases/<Context>/`. Business-fact commands are
   `IJournaledCommand` records with `[RequiresPermission(...)]` + `[Auditable(...)]`, each with a
   handler + validator. Pure reads are query records + handlers.
2. The Contracts interface is implemented by a `sealed` `Client.Services/<Context>/<Context>Service`
   taking `IDispatcher` + `ICurrentUserProvider` + `IOperationIdFactory` — **no EF / SQL / business
   rule**; it only builds a command context, dispatches, and maps.
3. DTO ↔ read-model mapping in `Client.Services/<Context>/<Context>Mapping.cs` — hand-written static
   extensions, no AutoMapper.
4. A list read is a **projection query handler** returning a purpose-built read model (never a loaded
   aggregate); paging via `QueryPaging.ToPagedAsync`.
5. **No Persian text** in Application/Client.Services — a failure is an `ErrorCode`; the VM maps it.
6. Reference seed in `LocalSeeder`; realistic sample rows in `Seed/<Context>SampleData.cs` behind
   `LocalStoreOptions.SeedSampleData`.
7. DI swap is one line per interface in `App.axaml.cs` + the mock deleted in the same phase.

### Reality vs. phase-doc naming (⚠️ CRITICAL — the code already exists)
- **Domain `Invoice` is ALREADY fully implemented** at `src/Animora.Domain/Sales/Invoice.cs` with the
  complete state machine, immutability guard (`EnsureDraft`), totals recompute (`RecalculateTotals` /
  `EnsureTotalsMatch`), payment idempotency (`_appliedPaymentOperationIds`), and events. **Do NOT
  rewrite it.** C07 is the Application/Persistence/Client-services layer that drives it.
- Real `InvoiceState` enum = `Draft, Posted, PartiallyPaid, Paid, Void, Refunded` (⚠️ it is `Void`,
  **not** `Voided`; there is no `Deleted` / `LocallyPosted` member on this enum — `LocallyPosted` is a
  `SettlementStatus`, see below).
- Real `InvoiceKind` enum = `Visit, Boarding, DirectSale, SalesReturn` (⚠️ there is **no**
  `CreditNote` kind — reversals use `InvoiceKind.SalesReturn` + an `InvoiceAdjustment`, or a negative
  adjustment; see "compensation" below).
- Reversal / refund / void are expressed through **`InvoiceAdjustment`** (domain type; methods
  `Invoice.AddCreditNote`, `Invoice.ApplyRefund`, `Invoice.Void(clock, reason)`), **not** by minting a
  second `Invoice` with negative lines. ⚠️ The phase text's "create a CreditNote invoice with negative
  lines + `ReversesInvoiceId`" describes intent; the implemented mechanism is `InvoiceAdjustment`.
  Follow the **domain's** existing methods and keep the original invoice byte-identical.
- `Invoice.Post(IClock)` already: checks non-empty (`INVOICE_EMPTY`), re-verifies totals
  (`INVOICE_TOTAL_MISMATCH`), rejects negative grand total (`INVOICE_NEGATIVE_TOTAL`), transitions to
  `Posted`, stamps `PostedUtc`, sets `Settlement = SettlementStatus.LocallyPosted`, raises
  `InvoicePostedEvent`. The command layer must NOT trust any client total — it calls `RecalculateTotals`
  / `Post` and lets the domain be the authority.
- `Invoice.ApplyPayment(Payment)` already handles duplicate-op idempotency
  (`PAYMENT_DUPLICATE_OPERATION`) and over-due (`PAYMENT_EXCEEDS_DUE`). ⚠️ The phase text names a new
  `PAYMENT_EXCEEDS_BALANCE` code; the domain already uses `PAYMENT_EXCEEDS_DUE`. **Prefer reusing the
  existing `PaymentExceedsDue`** and only add a new code if the Contracts/UI truly needs a distinct one
  (default: reuse; do not add `PAYMENT_EXCEEDS_BALANCE`).
- `Invoice.ApplyRefund` already enforces `ADJUSTMENT_EXCEEDS_PAID`. ⚠️ Phase text names
  `REFUND_EXCEEDS_PAID`; **reuse the existing `AdjustmentExceedsPaid`** unless a distinct Contracts code
  is required (default: reuse).

### Migration numbering (⚠️ 013 is TAKEN)
- Existing migrations go up to **`20260928130000_013_InventorySnapshot`** (C06).
- The phase says "`013_SalesInsurers`" — that number is used. **Create `014_SalesInsurers`** with
  timestamp prefix in the established format `2026MMDDHHMMSS_014_SalesInsurers` (e.g.
  `20260928140000_014_SalesInsurers`). Also generate its `.Designer.cs` and update
  `AnimoraLocalDbContextModelSnapshot.cs`. This is deviation **D1** (record it in PROGRESS like C03/C05/C06 did).

### Stock movement facts (C06)
- `MovementKind` enum values: `Receipt=0, Sale=1, Consumption=2, Loss=3, AdjustmentIn=4,
  AdjustmentOut=5, TransferIn=6, TransferOut=7, ReturnIn=8, ReturnOut=9`.
- ⚠️ A client **sale** consumes stock ⇒ `MovementKind.Sale`. A client **return** brings stock back
  ⇒ `MovementKind.ReturnIn` (the phase text's "`MovementKind.Return`" does not exist; use `ReturnIn`).
- Commands write stock by adding a `StockMovement` via **`IStockMovementRepository`** (see
  `src/Animora.Application/Ports/IStockMovementRepository.cs`; usage example in
  `InventoryCommands.cs` `AdjustStockCommand` handler: `StockMovement.Create(...)` → `movements.Add(...)`).
- The on-hand snapshot is maintained automatically: `StockSnapshotUpdater.Maintain(...)` runs from
  `AnimoraLocalDbContext.PrepareSave()` for **every** added `StockMovement` in the same transaction.
  ⇒ C07 posting only needs to add the movement; the snapshot updates itself. No manual decrement (R-B23).
- `UnitCostMinor` for a `Sale` movement is copied from the latest `Receipt` movement of the same
  variant so C09 can compute COGS (read it via inventory queries / latest-receipt lookup).
- Negative stock: gated by new `LocalStoreOptions.AllowNegativeStockOnSale` (default **true**). When
  `false` and a sale would drive on-hand below zero, the command returns `STOCK_WOULD_GO_NEGATIVE`
  (this error code already exists). Insufficient stock never blocks posting when the flag is true.

### Invoice numbering (CONVENTIONS §5)
- Reuse B02's counter table **`LocalSequenceCounters`** (entity `LocalSequenceCounter`,
  `src/Animora.Persistence.Sqlite/Entities/LocalSequenceCounter.cs`). Atomic increment SQL already
  exists in `PipelineSql.NextSequenceAsync(db, name, ct)`:
  `INSERT ... VALUES ($name,1) ON CONFLICT(Name) DO UPDATE SET Value=Value+1 RETURNING Value;`
  — it joins the ambient transaction via `db.Database.CurrentTransaction`.
- New `InvoiceNumberAllocator` (`src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs`):
  counter name = `invoice:<tenantId>:<jalaliYear>`, output format `{yy}-{seq:000000}` (e.g. `05-000001`).
  Unique per tenant/year, gap-tolerant, never reused (a rollback discards the seq value with the txn).
  Model it on `LocalSequenceAllocator` (`src/Animora.Persistence.Sqlite/Commands/LocalSequenceAllocator.cs`),
  which calls `PipelineSql.NextSequenceAsync`. Jalali year via the project's existing Jalali/date helper
  (search `SeedPeriodCodeProvider` / date utils; use the same one the codebase already uses for Jalali).

### Handoff facts (C03 visit + C05 boarding)
- Entities: `src/Animora.Persistence.Sqlite/Entities/VisitBillingHandoff.cs` and
  `BoardingBillingHandoff.cs`. Both `[LocalOnly]`, share `enum HandoffStatus { Ready, Invoiced, Cancelled }`.
- Columns: `Id, TenantId, VisitId|StayId, OwnerId, PetId, DoctorId, CompletedUtc, LinesJson, Status, InvoiceId`.
- Methods: `MarkInvoiced(invoiceId)` (throws if not `Ready`), `Cancel()`.
- ⚠️ `ReadyForBillingQuery` unions the two tables where `Status = Ready` (real enum member `Ready`,
  **not** the phase text's "`Status = Ready`" abstraction — it maps to `HandoffStatus.Ready`), newest
  first, max 100.
- Posting from a handoff must call `MarkInvoiced(invoiceId)`; if the handoff is already `Invoiced`,
  return new code `HANDOFF_ALREADY_INVOICED`.
- Writers: `IVisitBillingHandoffWriter` / `IBoardingBillingHandoffWriter` ports already exist; reuse them.

### Contracts surface (`src/Animora.Contracts/Sales/`) — DO NOT reshape
- `ISalesQueryService` has **22 members** (this is the "22 members" the phase counts). Full list:
  `GetReadyEncountersAsync, GetInvoicesAsync, GetInvoiceAsync, CreateFromEncounterAsync,
  CreateBlankAsync, GetInsurersAsync, GetKpisAsync, GetCatalogAsync, AddLineAsync, AddCustomLineAsync,
  TakePaymentAsync, VoidAsync, EditLineAsync, RemoveLineAsync, DeleteInvoiceAsync, RefundAsync,
  ReturnLineAsync, SetDiscountAsync, SetInsuranceAsync, GetOwnersAsync, PickOwnerAsync, GetHistoryAsync`.
- **Contracts mapping of the posting moment** (documented design decision — implement, don't reshape):
  - `TakePaymentAsync` **posts the invoice first when it is still `Draft`** (one transaction: post +
    register payment), then registers the payment.
  - `VoidAsync` on a posted invoice issues a **reversal/adjustment** (not a delete).
  - `DeleteInvoiceAsync` is allowed **only** for a `Draft`; otherwise return/`null`-map the
    `INVOICE_ALREADY_POSTED` situation. ⚠️ The domain's immutability error is `INVOICE_IMMUTABLE`
    (`ErrorCodes.InvoiceImmutable`), and the phase text says "`INVOICE_ALREADY_POSTED` (already in
    ErrorCodes from A07)" — **verify**: it is actually `INVOICE_IMMUTABLE` in the code. Use
    `ErrorCodes.InvoiceImmutable` for posted-edit refusals. (If a literal `INVOICE_ALREADY_POSTED`
    string is required by a test, add it as an alias code — but default to `InvoiceImmutable`.)
  - Document all of the above in the `SalesQueryService` XML docs and in PROGRESS. No Contracts member
    is reshaped.
- DTO types in `Sales/SalesModels.cs`: `InvoiceKind, InvoiceState, InvoiceAuditKind, InvoiceAuditEntry,
  InvoiceLineKind, DiscountKind, PaymentMethod, ReadyEncounterRef, InvoiceSummary, InvoiceLine,
  InvoicePayment, InsurerRef, InvoiceDetail, CatalogItem, CatalogFilterRef, RecentItemRef,
  CatalogSnapshot, CustomLineRequest, PaymentRequest, PaymentResult, RefundRequest, RefundResult,
  LineReturnRequest, LineReturnResult, DiscountChangeRequest, OwnerRef, OwnerPickResult, SaleKpiSnapshot`.
- `Sales/CashierHistoryModels.cs`: `CashierHistoryFilter` → `CashierHistoryPage` (paged; service applies
  filter + sort + paging; totals/sums computed over the whole filtered set — PERFORMANCE §6).

### Settlement visibility (R-B16, A-RULE 024)
- `InvoiceDetailReadModel` carries `SettlementStatus`, `LastSettlementErrorCode`, `ResponsibleRole`,
  read from the `PendingCommand` row via B02's `UnsettledCommandListQuery`.
- `SalesMapping` maps these onto the existing `InvoiceDetail` / `InvoiceAuditEntry` Contracts fields the
  UI already renders — **no Contracts change**. A bare failure with no settlement info is a defect.

### DI + mock
- DI line to swap: `src/Animora.Desktop/App.axaml.cs:301`
  `services.AddSingleton<ISalesQueryService, MockSalesQueryService>();`
  → `services.AddSingleton<ISalesQueryService, SalesQueryService>();`
  (register any new repositories/queries/allocator/handoff writers alongside, mirroring how C06 wired
  `IInventoryQueries` etc.)
- Mock to delete: `src/Animora.Desktop/Mocks/MockSalesQueryService.cs` (exists, ~51 KB). Confirm no
  remaining reference after deletion.
- ViewModel: `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs` gains the standard **readiness-gate
  line** (mirror the C02/C06 VMs that already added an `IStoreReadinessGate` gate line).

### CommandTypes (`src/Animora.Contracts.Wire/Commands/CommandTypes.cs`)
- Already defined (reuse): `SalesInvoicePost = "sales.invoice.post"`,
  `SalesPaymentRegister = "sales.payment.register"`, plus `SalesInvoiceVoid = "sales.invoice.void"`,
  `SalesInvoiceRefund = "sales.invoice.refund"`, `SalesAdjustmentAdd = "sales.adjustment.add"`.
  ⚠️ So `void` and `refund` **already exist** too. The phase's "add 11" count assumed only 2 existed;
  in reality post/void/refund/register + adjustment (5) exist. **Add only the still-missing ones** and
  append them to the `All`/validation array (line ~82). Missing to add:
  `sales.invoice.create`, `sales.invoice.line.add`, `sales.invoice.line.edit`,
  `sales.invoice.line.remove`, `sales.invoice.discount.set`, `sales.invoice.insurance.set`,
  `sales.invoice.owner.set`, `sales.invoice.delete`, `sales.invoice.line.return`. (Custom-line add can
  reuse `sales.invoice.line.add` or add `sales.invoice.line.add.custom` — pick one and be consistent.)
  Verify against the current file before adding; never duplicate an existing const.

### Permissions (already exist from A-phases — reuse, don't invent)
- `sales.invoice.edit` (draft edits), `sales.invoice.post` (posting), `sales.payment.register`
  (payments), `sales.invoice.void` (void/refund/return). Confirm the exact `PermissionCodes.*` member
  names in `src/Animora.Application/Security/` before use.

### Auditing
- Every journaled command carries `[Auditable(AuditCategory.Financial, CommandTypes.<X>)]`.
  Confirm `AuditCategory.Financial` exists (A07 region references it; verify enum member name).

### Error codes to ADD (append to the Sales region of `src/Animora.Domain/Results/ErrorCodes.cs`)
Verify each against the file first; add only the ones that do not already exist. Candidates from the
phase (with the ⚠️ reuse notes applied):
- `INVOICE_NOT_FOUND` → `InvoiceNotFound` (NotFound). **ADD** (not present).
- `INVOICE_LINE_INDEX_STALE` → `InvoiceLineIndexStale` (Conflict). **ADD** (C05 stale-index rule).
- `INVOICE_NO_LINES` → **likely reuse existing `InvoiceEmpty` (`INVOICE_EMPTY`)**. Default: reuse; only
  add if a test demands the exact string `INVOICE_NO_LINES`.
- `INVOICE_OWNER_REQUIRED` → `InvoiceOwnerRequired` (Validation). **ADD**.
- `PAYMENT_EXCEEDS_BALANCE` → **reuse `PaymentExceedsDue`**; do not add (see note above).
- `HANDOFF_ALREADY_INVOICED` → `HandoffAlreadyInvoiced` (Conflict). **ADD**.
- `INSURER_NOT_FOUND` → `InsurerNotFound` (NotFound). **ADD**.
- `REFUND_EXCEEDS_PAID` → **reuse `AdjustmentExceedsPaid`**; do not add.
- (Optional) `INVOICE_ALREADY_POSTED` → **reuse `InvoiceImmutable`**; do not add.
- Net new codes actually added: `INVOICE_NOT_FOUND`, `INVOICE_LINE_INDEX_STALE`,
  `INVOICE_OWNER_REQUIRED`, `HANDOFF_ALREADY_INVOICED`, `INSURER_NOT_FOUND` (**5**), with the other 3
  satisfied by reusing existing codes. If a later task's test forces a literal string, add that one
  code then and record it as a deviation.

### Insurance model
- `Insurer` reference entity: `src/Animora.Domain/Sales/Insurer.cs` — fields `Code, Name,
  CoveragePercent, CeilingMinor` (+ standard `Id`, `TenantId` per AggregateRoot/reference pattern).
- Seeded by `LocalSeeder` — **6 insurers** (reference data, like C05's 6 boarding service defs / C06's
  categories). `InsurerConfiguration` maps it with a **unique `Code` index**.
- `SetInvoiceInsuranceCommand` recomputes the insurance adjustment via the domain
  (`Invoice.SetInsurance(insurerId, percent)` → `RecalculateTotals`) — never a free-typed amount. The
  percent comes from the resolved `Insurer.CoveragePercent`; `CeilingMinor` caps the coverage.
  ⚠️ `Invoice.SetInsurance` currently takes `(insurerId, percent)`; the ceiling cap must be applied by
  the command (resolve insurer → compute capped percent/amount) before calling, or extend the domain if
  the ceiling cannot be expressed as a percent. Prefer computing in the command and passing the effective
  percent; document if `InvoiceTotals.ApplyInsurance` needs a ceiling-aware overload.

### Sample data
- `Seed/SalesSampleData.cs` behind `LocalStoreOptions.SeedSampleData` — **30 invoices** across states
  (draft / posted / partially paid / paid / void) with payments and matching stock movements, plus the
  6 reference insurers seeded via `LocalSeeder`. Deterministic ULIDs. Mirror the structure of
  `InventorySampleData.cs` / `BoardingSampleData.cs`.

### Build / test / run gates (final verification only — no full runs mid-way)
- `scripts/sandbox/build.sh full` must be **0 warnings / 0 errors**.
- `scripts/sandbox/test.sh` must print **`TESTS OK`** (run **filtered** to C07 test classes while
  iterating; only run the whole suite once at the end).
- `scripts/sandbox/run.sh` must print **`SMOKE RUN OK`**.
- ⚠️ Rule from the task: **no long full-test / full-build loops mid-implementation.** Build only the
  affected projects (`build.sh fast` or per-project `dotnet build`), run only filtered tests, and reserve
  the full `build.sh full` / `test.sh` / `run.sh` for the final verification task.

---

## 📋 TASK LIST (do in order; tick `[x]` then push after each)

> Status legend: `[ ]` pending · `[x]` done. Each task lists **files**, **exact work**, and
> **acceptance**. After ticking a task, run the locked push command with the suggested message.

### T01 — Domain + reference groundwork
- **Status:** `[ ]`
- **Files:**
  - create `src/Animora.Domain/Sales/Insurer.cs` (fields `Code, Name, CoveragePercent, CeilingMinor`;
    factory `Create(tenantId, code, name, coveragePercent, ceilingMinor)`; follow the reference-entity
    style of an existing domain reference type such as `BoardingServiceDef`).
  - modify `src/Animora.Domain/Sales/Invoice.cs` **only if** `ReversesInvoiceId` is genuinely needed —
    ⚠️ first check whether reversals are already expressed via `InvoiceAdjustment`. If the compensation
    tests can be satisfied through `InvoiceAdjustment` + `Adjustments`, **do not add** `ReversesInvoiceId`
    (avoid touching the finished aggregate). If a link is required, add a nullable
    `ReversesInvoiceId` with a private setter and a guarded setter method, plus the migration column.
  - modify `src/Animora.Domain/Results/ErrorCodes.cs` — append the **5 net-new** codes into the
    `#region Sales & Cashier (Phase A07)` block: `InvoiceNotFound (NotFound)`,
    `InvoiceLineIndexStale (Conflict)`, `InvoiceOwnerRequired (Validation)`,
    `HandoffAlreadyInvoiced (Conflict)`, `InsurerNotFound (NotFound)`. Reuse existing for the other 3.
  - modify `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs` — add
    `public bool AllowNegativeStockOnSale { get; init; } = true;` with an XML doc.
- **Acceptance:** `Animora.Domain` + `Animora.Persistence.Sqlite` compile (per-project build). No
  behavioural change to existing `Invoice` members.
- **Push:** `feat(sales): add Insurer entity, C07 error codes, AllowNegativeStockOnSale option`

### T02 — Insurer persistence + migration 014 + seed
- **Status:** `[ ]`
- **Files:**
  - create `src/Animora.Persistence.Sqlite/Configurations/Sales/InsurerConfiguration.cs` — table
    mapping + **unique index on `Code`** (per tenant if the convention is composite; check existing
    reference configs for the tenant-scoped unique-index pattern).
  - register the entity/config in `AnimoraLocalDbContext` (mirror how other configs are picked up —
    usually `ApplyConfigurationsFromAssembly`; verify no manual `DbSet` is required, or add one if the
    pattern uses explicit sets).
  - create migration `src/Animora.Persistence.Sqlite/Migrations/20260928140000_014_SalesInsurers.cs`
    (+ `.Designer.cs`) and update `AnimoraLocalDbContextModelSnapshot.cs`. Prefer generating via
    `dotnet ef migrations add 014_SalesInsurers` (check the EF tooling command the repo uses; other
    migrations were generated, not hand-written). Contents: `Insurers` table (+ unique `Code` index)
    and, **only if T01 added it**, the `ReversesInvoiceId` column on `Invoices`.
  - modify `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` — seed **6 insurers** (deterministic
    ids; realistic Iranian insurers, e.g. تأمین اجتماعی / ایران / آسیا / دانا / البرز / پارسیان with
    plausible `CoveragePercent` + `CeilingMinor`). Persian only in seed data values, not in logic.
- **Acceptance:** migration applies on a `013` DB (the existing `Persistence.Tests` migration-apply test
  style); a second insurer with a duplicate `Code` is rejected by the unique index. Per-project build 0/0.
- **Push:** `feat(sales): Insurer config + migration 014_SalesInsurers + seed 6 insurers`

### T03 — InvoiceNumberAllocator (+ concurrency test)
- **Status:** `[ ]`
- **Files:**
  - create `src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs` — define its port
    interface (e.g. `IInvoiceNumberAllocator.AllocateAsync(tenantId, DateTimeOffset issued, ct)` returning
    the formatted string) in `src/Animora.Application/Ports/` if none exists; the impl calls
    `PipelineSql.NextSequenceAsync(db, $"invoice:{tenantId}:{jalaliYear}", ct)` and formats
    `{yy}-{seq:000000}`. Get `jalaliYear`/`yy` from the codebase's existing Jalali/date helper (search
    for the one `SeedPeriodCodeProvider` or the reception/boarding code uses — do not add a new date lib).
  - create `tests/Animora.Persistence.Tests/InvoiceNumberAllocatorTests.cs` — **200 concurrent
    allocations yield 200 distinct numbers; no reuse after a rollback** (start a txn, allocate, roll back,
    allocate again → the rolled-back value is gone, next value is strictly greater; gap-tolerant).
- **Acceptance:** the concurrency test is green (run **filtered**:
  `dotnet test tests/Animora.Persistence.Tests --filter InvoiceNumberAllocator`).
- **Push:** `feat(sales): InvoiceNumberAllocator over LocalSequenceCounters + concurrency test`

### T04 — SalesQueries (persistence read side)
- **Status:** `[ ]`
- **Files:**
  - create `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs` (+ port `ISalesQueries` in
    `Application/Ports/` or `Application/UseCases/Sales/`, matching how `IInventoryQueries` is placed).
    Implement single-statement, indexed, paged projections for:
    - `ReadyForBilling` — `UNION ALL` over `VisitBillingHandoffs` + `BoardingBillingHandoffs` where
      `Status = HandoffStatus.Ready`, newest first (`CompletedUtc DESC`), **max 100**. Filter on the
      entity before projecting (C05 lesson: projecting an embedded entity breaks EF translation).
    - `InvoiceList(filter, page)` — paged via `QueryPaging.ToPagedAsync`, indexed, newest first.
    - `InvoiceDetail(invoiceId)` — includes lines, payments, adjustments **and the settlement join**
      (LEFT JOIN the `PendingCommand` for this invoice's post op via B02's
      `UnsettledCommandListQuery`/table to read `SettlementStatus`, `LastSettlementErrorCode`,
      `ResponsibleRole`).
    - `SalesCatalog` — products + variants + service defs + recent-item refs.
    - `Insurers` — the 6 reference rows.
    - `SaleKpi` — today's revenue / count / average in **one grouped statement**.
    - `SalesHistory(CashierHistoryFilter)` → page rows + total count + inflow/outflow sums over the
      whole filtered set (service applies filter/sort/paging; PERFORMANCE §6).
    - `OwnerLookup(string query)` — reuse the patients owner-directory projection style.
- **Acceptance:** project builds 0/0; queries are projection handlers returning read models (no loaded
  aggregates); paging uses `QueryPaging.ToPagedAsync`.
- **Push:** `feat(sales): SalesQueries — ready-for-billing union, list, detail(+settlement), catalog, KPIs, history, owner lookup`

### T05 — Read models
- **Status:** `[ ]`
- **Files:**
  - create `src/Animora.Application/UseCases/Sales/ReadModels/*.cs` — **6 read models** including the
    settlement fields on the detail model: e.g. `InvoiceSummaryReadModel`, `InvoiceDetailReadModel`
    (with `SettlementStatus`, `LastSettlementErrorCode`, `ResponsibleRole`), `ReadyForBillingReadModel`,
    `SalesCatalogReadModel`, `SaleKpiReadModel`, `CashierHistoryReadModel`/page. Purpose-built for
    reading (A-RULE 018 — denormalized read model; `Invoice` stays the normalized write model).
- **Acceptance:** builds 0/0; models are records with no behaviour.
- **Push:** `feat(sales): 6 sales read models incl. settlement fields`

### T06 — Draft-editing commands (create ×3, line add/edit/remove, discount, insurance, owner, delete)
- **Status:** `[ ]`
- **Files:** create in `src/Animora.Application/UseCases/Sales/` (one file per command + handler +
  validator, C01 pattern). Commands (all `IJournaledCommand`, `[RequiresPermission(sales.invoice.edit)]`
  except where noted, `[Auditable(AuditCategory.Financial, CommandTypes.<X>)]`):
  - `CreateInvoiceFromVisitCommand` (`sales.invoice.create`) — reads the `VisitBillingHandoff` lines,
    builds a `Draft` `Invoice` (`InvoiceKind.Visit`), attaches visit/owner/pet; does **not** mark the
    handoff yet (marking happens at post).
  - `CreateInvoiceFromStayCommand` (`sales.invoice.create`) — same from `BoardingBillingHandoff`
    (`InvoiceKind.Boarding`).
  - `CreateBlankInvoiceCommand` (`sales.invoice.create`) — empty `Draft` (`InvoiceKind.DirectSale`),
    owner or guest.
  - `AddInvoiceLineCommand` + `AddCustomInvoiceLineCommand` (`sales.invoice.line.add`) — build an
    `InvoiceLine`, `Invoice.AddLine`.
  - `EditInvoiceLineCommand` (`sales.invoice.line.edit`) and `RemoveInvoiceLineCommand`
    (`sales.invoice.line.remove`) — **C05 stale-index rule**: take `(InvoiceId, int Index,
    string ExpectedLineId)`; resolve the line at `Index`, compare its `LineId` to `ExpectedLineId`; on
    mismatch return `INVOICE_LINE_INDEX_STALE`. On a posted invoice the domain already returns
    `INVOICE_IMMUTABLE`.
  - `SetInvoiceDiscountCommand` (`sales.invoice.discount.set`) → `Invoice.SetDiscount(kind, value)`.
  - `SetInvoiceInsuranceCommand` (`sales.invoice.insurance.set`) — resolve `Insurer` (else
    `INSURER_NOT_FOUND`), compute effective coverage percent honoring `CeilingMinor`, call
    `Invoice.SetInsurance(insurerId, effectivePercent)`.
  - `AssignInvoiceOwnerCommand` (`sales.invoice.owner.set`) → `Invoice.AssignOwner(ownerId, petId)`.
  - `DeleteDraftInvoiceCommand` (`sales.invoice.delete`) — allowed only for `Draft`; a non-draft returns
    `INVOICE_IMMUTABLE`. (Contracts `DeleteInvoiceAsync` soft-delete semantics are handled at the
    service/mapping layer per the design decision.)
- **Acceptance:** builds 0/0; each command has a validator (`Rules.*` style from `InventoryCommands`);
  no client total trusted (totals come from `Invoice.RecalculateTotals`).
- **Push:** `feat(sales): draft-editing commands (create x3, line add/edit/remove, discount, insurance, owner, delete)`

### T07 — PostInvoiceCommand (the 7-step transaction)
- **Status:** `[ ]`
- **File:** create `src/Animora.Application/UseCases/Sales/PostInvoiceCommand.cs` (+ handler +
  validator). `IJournaledCommand`, `[RequiresPermission(sales.invoice.post)]`,
  `[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoicePost)]`. Stable `OperationId` (A-RULE
  005/006) so replay returns the stored result.
- **Exact steps (all in the one B02 transaction):**
  1. Load invoice; validate state `Draft`, ≥1 line, a resolved owner (`INVOICE_OWNER_REQUIRED` if none),
     totals recomputed by the domain (`RecalculateTotals`) — **never trust a client total**.
  2. Allocate `Invoice.InvoiceNumber` via `IInvoiceNumberAllocator` (T03).
  3. `Invoice.Post(clock)` → transitions to `Posted`, stamps `PostedUtc`, sets
     `Settlement = SettlementStatus.LocallyPosted`, raises `InvoicePostedEvent`.
     (The domain already does 3; the command wires the number in **before** posting.)
  4. For each **stockable** line (product is not a service): add one `StockMovement` of
     `MovementKind.Sale` via `IStockMovementRepository`, `UnitCostMinor` copied from the latest
     `Receipt` movement of that variant. A service line writes no movement. If
     `AllowNegativeStockOnSale == false` and on-hand would go negative → `STOCK_WOULD_GO_NEGATIVE`;
     otherwise allow negative (snapshot maintained automatically by `StockSnapshotUpdater`).
  5. Mark the originating `VisitBillingHandoff`/`BoardingBillingHandoff` `Invoiced` with `InvoiceId`
     (via the handoff writer). If already `Invoiced` → `HANDOFF_ALREADY_INVOICED`.
  6. `InvoicePostedEvent` already raised in step 3 (C09 ledger subscribes).
  7. `CommandJournalBehavior` (B02) writes the `PendingCommand` in the same transaction — automatic
     because the command is `IJournaledCommand`; nothing extra to do.
- **Acceptance (covered by T13 tests, but assert now):** exactly one `PendingCommand`, one `Sale`
  movement per stockable line, handoff marked `Invoiced`, unique number allocated; replaying the same
  `OperationId` returns the stored result with **no** second movement/number; already-invoiced handoff →
  `HANDOFF_ALREADY_INVOICED`; a tampered client total is ignored.
- **Push:** `feat(sales): PostInvoiceCommand — 7-step journaled posting transaction`

### T08 — RegisterPaymentCommand + compensating commands (refund / return / void)
- **Status:** `[ ]`
- **Files:** create in `src/Animora.Application/UseCases/Sales/`:
  - `RegisterPaymentCommand(InvoiceId, PaymentMethod, Money Amount, string? Reference)`
    (`sales.payment.register`, `[RequiresPermission(sales.payment.register)]`) — build a `Payment`
    (append-only), `Invoice.ApplyPayment` (domain handles idempotency + over-due `PAYMENT_EXCEEDS_DUE`
    and advances to `PartiallyPaid`/`Paid`). A payment is never edited/deleted.
  - `RefundInvoiceCommand` (`sales.invoice.refund`, perm `sales.invoice.void`) — build an
    `InvoiceAdjustment` (refund) + a negative `Payment`; `Invoice.ApplyRefund` (enforces
    `ADJUSTMENT_EXCEEDS_PAID`). Raises the refunded event (domain does it).
  - `ReturnInvoiceLineCommand` (`sales.invoice.line.return`, perm `sales.invoice.void`) — credit-note
    adjustment for the returned line **and** one `MovementKind.ReturnIn` stock movement.
  - `VoidInvoiceCommand` (`sales.invoice.void`, perm `sales.invoice.void`) — `Invoice.Void(clock,
    reason)` for a draft/posted-unpaid invoice; a paid invoice must be refunded
    (`INVOICE_VOID_AFTER_PAYMENT`, domain-enforced).
  - ⚠️ Use the domain's `InvoiceAdjustment`/`Void`/`ApplyRefund`/`AddCreditNote` — keep the original
    invoice byte-identical (A-RULE 002). Do not mint negative-line invoices unless the domain lacks a
    path (it does not — it has `InvoiceAdjustment`).
- **Acceptance:** builds 0/0; refund above paid → `ADJUSTMENT_EXCEEDS_PAID`; return writes exactly one
  `ReturnIn` movement.
- **Push:** `feat(sales): RegisterPayment + refund/return/void compensating commands`

### T09 — Registration wiring (Application DI for Sales use cases)
- **Status:** `[ ]`
- **File:** create `src/Animora.Application/UseCases/Sales/SalesRegistration.cs` (mirror
  `InventoryRegistration` / `PatientsRegistration`): register all query handlers, command handlers,
  validators, and any Application-side ports. Wire the persistence-side services
  (`ISalesQueries → SalesQueries`, `IInvoiceNumberAllocator → InvoiceNumberAllocator`) where the other
  persistence services are registered (find C06's registration site in
  `Animora.Infrastructure.Local` / persistence DI).
- **Acceptance:** builds 0/0; all handlers resolvable.
- **Push:** `feat(sales): register Sales use cases, queries and allocator`

### T10 — SalesMapping + SalesQueryService (the 22 members)
- **Status:** `[ ]`
- **Files:**
  - create `src/Animora.Client.Services/Sales/SalesMapping.cs` — hand-written static extensions mapping
    read models ↔ Contracts DTOs, **including the settlement fields** onto `InvoiceDetail`/
    `InvoiceAuditEntry`. Real-enum mapping: domain `InvoiceState.Void` ↔ Contracts `InvoiceState`
    (check the Contracts enum members and map exactly). Reuse a `PersianFormat` helper if one exists
    (C06 added one).
  - create `src/Animora.Client.Services/Sales/SalesQueryService.cs` — `sealed`, ctor
    `(IDispatcher, ICurrentUserProvider, IOperationIdFactory)` (+ any read port it needs), **no EF/SQL/
    rule**. Implement all **22** `ISalesQueryService` members by building a command/query context,
    dispatching, and mapping. Encode the documented posting-moment behavior:
    - `TakePaymentAsync`: if the invoice is `Draft`, dispatch `PostInvoiceCommand` first (same logical
      op), then `RegisterPaymentCommand`; return `PaymentResult`.
    - `VoidAsync`: posted → dispatch `VoidInvoiceCommand`/reversal; return updated detail or `null` when
      state forbids.
    - `DeleteInvoiceAsync`: draft → `DeleteDraftInvoiceCommand`; posted → surface `INVOICE_IMMUTABLE`
      (map to `null` per interface contract) or the soft-delete semantics the interface documents.
    - `EditLineAsync`/`RemoveLineAsync`: resolve `ExpectedLineId` from the last-read detail (stale-index
      rule) before dispatching.
    - XML-doc the post-on-payment, void-as-reversal, draft-only-delete decisions here (design decision).
- **Acceptance:** builds 0/0; every interface member implemented; no business logic in the service.
- **Push:** `feat(sales): SalesMapping + SalesQueryService (22 members, settlement + posting-moment mapping)`

### T11 — CommandTypes + SalesSampleData
- **Status:** `[ ]`
- **Files:**
  - modify `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` — add only the **still-missing** const
    strings (see canonical facts: create, line.add, line.edit, line.remove, discount.set, insurance.set,
    owner.set, delete, line.return) and append each to the `All`/validation array (~line 82). Verify no
    duplicates with the existing post/void/refund/register/adjustment consts.
  - create `src/Animora.Persistence.Sqlite/Seed/SalesSampleData.cs` behind `LocalStoreOptions.SeedSampleData`
    — **30 invoices** across draft/posted/partially-paid/paid/void with payments and matching
    `Sale`/`ReturnIn` stock movements; deterministic ids. Wire it into `LocalSeeder` next to the other
    `*SampleData` calls. Ensure the sample data is internally consistent (posted invoices have numbers,
    movements, and settlement rows) so cashier/finance/history screens have real data immediately.
- **Acceptance:** builds 0/0; `SeedingTests`-style count assertion (30 invoices + 6 insurers) passes when
  added in T13.
- **Push:** `feat(sales): CommandTypes additions + SalesSampleData (30 invoices)`

### T12 — Desktop swap (DI, ViewModel, delete mock)
- **Status:** `[ ]`
- **Files:**
  - modify `src/Animora.Desktop/App.axaml.cs` — swap line 301 to
    `services.AddSingleton<ISalesQueryService, SalesQueryService>();` and register any new
    persistence/query/allocator/handoff-writer services C07 introduced (mirror C06's DI additions).
  - modify `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs` — add the standard readiness-gate line
    (copy the exact pattern from a C02/C06 VM that injects/uses `IStoreReadinessGate`).
  - delete `src/Animora.Desktop/Mocks/MockSalesQueryService.cs`; then
    `grep -rn "MockSalesQueryService" src` must return **nothing**.
- **Acceptance:** Desktop project builds 0/0; no reference to the deleted mock.
- **Push:** `feat(sales): swap ISalesQueryService to real impl, add readiness gate, delete mock`

### T13 — The four test classes
- **Status:** `[ ]`
- **Files (run each FILTERED as you write it, not the whole suite):**
  - create `tests/Animora.Domain.Tests/InvoiceImmutabilityTests.cs` — editing a line / discount /
    insurer on a **posted** invoice returns `INVOICE_IMMUTABLE`; a credit-note/reversal's totals are the
    exact negation of what it reverses.
  - create `tests/Animora.Application.Tests/PostInvoiceCommandTests.cs` — the 7-step transaction:
    exactly one `PendingCommand`, one `Sale` movement per stockable line, handoff → `Invoiced`, unique
    number; **idempotent replay** (same `OperationId` → stored result, no 2nd movement/number);
    already-invoiced handoff → `HANDOFF_ALREADY_INVOICED`; tampered client total ignored in favor of
    `InvoiceTotals`.
  - create `tests/Animora.Application.Tests/SalesCompensationTests.cs` — void / refund / return each
    create exactly one credit-note adjustment linked to the original, leave the original rows
    byte-identical, and (return) write one `MovementKind.ReturnIn` movement; refund above paid →
    `ADJUSTMENT_EXCEEDS_PAID`.
  - create `tests/Animora.Persistence.Tests/InvoiceNumberAllocatorTests.cs` — already delivered in T03;
    if not, add here.
  - (optional) extend the seeding test with the 30-invoice / 6-insurer count assertion.
- **Acceptance:** each new test class green when run filtered
  (`dotnet test <proj> --filter <ClassName>`).
- **Push:** `test(sales): InvoiceImmutability, PostInvoiceCommand, SalesCompensation, allocator tests`

### T14 — Final verification (the only place full gates run)
- **Status:** `[ ]`
- **Do:**
  - `scripts/sandbox/build.sh full` → must be **0 warnings / 0 errors**.
  - `scripts/sandbox/test.sh` → must print **`TESTS OK`** (or run the C07-filtered set if the full suite
    is too slow, but the acceptance is the full gate).
  - `scripts/sandbox/run.sh` → must print **`SMOKE RUN OK`**.
  - Confirm `src/Animora.Desktop/Mocks/MockSalesQueryService.cs` does not exist and no DI line references it.
  - Manual sanity (mental/checklist): invoice from a completed visit and a checked-out stay; catalog +
    custom lines; discount + insurer; payment posts + numbers the invoice; stock drops by sold qty;
    void/refund/return each produce a credit note and leave the original untouched; settlement chip shows
    `LocallyPosted`.
  - Update `backend-roadmap/PROGRESS.md` with the C07 closeout row + deviations (D1 = migration numbered
    014 not 013; reused `PaymentExceedsDue`/`AdjustmentExceedsPaid`/`InvoiceImmutable` instead of the
    phase's proposed new codes; reversals via `InvoiceAdjustment` not negative-line invoices;
    `MovementKind.ReturnIn` not `Return`). ⚠️ Editing PROGRESS/roadmap is allowed **only** in this final
    task, and only the closeout row — do not touch other roadmap content.
- **Acceptance:** all three gates pass; PROGRESS updated.
- **Push:** `feat(sales): C07 Sales/POS complete — build 0/0, TESTS OK, SMOKE RUN OK; PROGRESS updated`

### T15 — Delete this plan file (final task)
- **Status:** `[ ]`
- **Do:** `rm Plan/PHASE-C07-sales-pos-TODO.md` (and `rmdir Plan` if empty). Do **not** touch any other
  file. This is the closing task per the instructions.
- **Push:** `chore(plan): remove completed C07 plan file`

---

## Out of scope (do not implement here)
- Cash session open/close + cashier screen surface → C08.
- Ledger journal entries from `InvoicePostedEvent` → C09.
- Cheque payments → C08.
- Server-side posting/settlement → D07; pending-command transport → E06.

## Rules that apply
- **A-RULE 002 / R-B22** — posted invoice + every `Payment` immutable; void/refund/return are
  compensating credit notes (`InvoiceAdjustment`).
- **A-RULE 005 / 006 / R-B14** — posting is a journaled command with a stable `OperationId`; replay
  returns the stored result, never a second invoice/movement.
- **A-RULE 024 / R-B16** — settlement status/error/role reach the UI via the read model.
- **R-B23** — sale consumption is a `StockMovement`, never a stored-quantity decrement.
- **CONVENTIONS §5** — numbering uses the counter table inside the transaction; list/history queries
  paged + indexed.
- **A-RULE 018** — `InvoiceDetailReadModel` denormalized for reading; `Invoice` stays the normalized
  write model.
