# C06 — Inventory & Purchasing · Implementation Plan (self-contained)

> **For the executing model:** everything you need is in THIS file. You should NOT need to re-read
> the phase file or re-explore the repo except the exact files each task names. Facts below were
> verified against the repo on 2026-09-28. After **every** completed task run
> `scripts/git/push.sh "msg"` (mandatory, locked). Do not run full builds/tests (user directive) —
> build only the touched project when needed; leave `build.sh full`/`test.sh` for the final task.
> Branch: work on the current branch; `push.sh` handles stage→commit→rebase→push.

---

## 0. Ground truth discovered (do not re-derive)

### 0.1 CRITICAL deviations from the phase file (the phase text is stale here)
- **Migration number:** phase says `012_InventorySnapshot`. But `012` is already taken by C05
  (`20260928120000_012_BoardingHandoff.cs`). **Use `013_InventorySnapshot`** with a timestamp
  prefix greater than `20260928120000` (e.g. `20260928130000_013_InventorySnapshot`). Migrations are
  sequential & additive — never edit an existing one (CONVENTIONS §5).
- **CommandTypes already present** in `src/Animora.Contracts.Wire/Commands/CommandTypes.cs`:
  `InventoryStockAdjust = "inventory.stock.adjust"`, `InventoryReceiptPost = "inventory.receipt.post"`,
  `PurchasingOrderSubmit = "purchasing.order.submit"`. The phase's "add 7" list partly overlaps.
  **Net new constants to ADD (5):** `inventory.product.create`, `inventory.product.update`,
  `inventory.variant.add`, `inventory.product.delete`, plus purchasing `purchasing.order.create`,
  `purchasing.receipt.record`, `purchasing.order.cancel`. **Reuse** existing `InventoryStockAdjust`
  for AdjustStock. So: add `InventoryProductCreate`, `InventoryProductUpdate`, `InventoryVariantAdd`,
  `InventoryProductDelete`, `PurchasingOrderCreate`, `PurchasingReceiptRecord`, `PurchasingOrderCancel`
  (**7 new consts**), and add all 7 to the `All` list. Do NOT redefine `inventory.stock.adjust`.
- **Seed markers block re-seed:** `LocalSeeder` already seeds units & categories under
  `SeedMarker` keys `inventory.units` (7 units: PCS BOX ML MG G KG DOSE) and `inventory.categories`
  (5 cats: MED VAC FOOD ACC SRV) at `SchemaVersion = 1`. A marker check (`IsAppliedAsync`) skips a
  block whose marker exists. The phase wants **12 categories + 8 units**. Because an existing store
  already has the SchemaVersion=1 marker, simply appending more rows to the `Units`/`Categories`
  arrays will NOT re-run on an already-seeded DB. **Decision (implement this):** expand the arrays to
  12 categories + 8 units AND bump the two blocks to a fresh marker key suffix so new stores get the
  full set; for already-seeded dev stores this is acceptable because Track C runs on throwaway local
  DBs. Simplest correct approach: change `UnitsKey`/`CategoriesKey` marker values are constants — do
  NOT rename them (marker keys are permanent-ish, but they are LocalOnly seed bookkeeping, not wire).
  **Chosen concrete approach:** keep the keys, expand the arrays, and rely on fresh-store seeding
  (Track C DBs are recreated). Note this as a deviation in the PROGRESS entry. The 8th unit: add
  `L` (litre, decimals 3) or `TAB` (tablet, 0) — use `TAB` (tablet, decimals 0). The 7 extra
  categories (to reach 12) beyond MED/VAC/FOOD/ACC/SRV: `SUPP` (Supplements), `GROOM` (Grooming),
  `TOY` (Toys), `HYG` (Hygiene), `LAB` (Lab supplies), `SURG` (Surgical consumables), `EQUIP`
  (Equipment). (Names are English; UI localizes.)
- **Client.Services has NO Inventory/Purchasing folder yet** — create `Inventory/` and `Purchasing/`.
- **Persistence query DI** uses `services.TryAddScoped<IPort, Impl>()` in
  `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` (lines ~72–89).
  `IProductRepository`, `IStockMovementRepository`, `IPurchasingRepository` already registered
  (lines 87–89). You must add the two new query ports (`IInventoryQueries`, `IPurchasingQueries`)
  and the snapshot updater/rebuild there.

### 0.2 Domain facts (already built in A06 — reuse, don't rebuild)
- `Product` (`src/Animora.Domain/Inventory/Product.cs`): aggregate root, `AggregateRoot`.
  **Has NO Stock/OnHand/Quantity property** (the phase's test asserts this). Fields: `Sku`, `Name`,
  `Brand?`, `CategoryId`, `Kind` (`ProductKind` enum), `UnitCode`, `IsPrescriptionOnly`, `IsActive`,
  `TrackStock`, `ReorderLevel` (`Quantity`), `DefaultCostPrice`/`DefaultSalePrice` (`Money`),
  `ShelfLifeDays?`, `Notes?`, `Variants` (`IReadOnlyList<ProductVariant>`). Factory
  `Product.Create(...)`. Mutators return `Result`: `AddVariant`, `UpdateVariant`,
  `DeactivateVariant`, `SetPrices`, `SetReorderLevel`, `Describe(name,brand,notes)`, `Deactivate`,
  `Activate`. Soft-delete is via `SyncableEntity.Deleted`/`Deactivate()` — Product is
  `AggregateRoot`; confirm `AggregateRoot : SyncableEntity` carries a `Deleted` flag (check
  `src/Animora.Domain/Primitives/`). Domain `ProductVariant` is a nested type on Product with
  `VariantId`, `Sku`, `IsActive`, `ReorderLevel` (`Quantity`) — NOT the Contracts DTO of the same
  name (different shape; keep them separate, map in `InventoryMapping`).
- `StockMovement` (`.../Inventory/StockMovement.cs`): `SyncableEntity`, append-only (no mutator).
  Factory `StockMovement.Create(tenantId, productId, variantId, MovementKind kind, Quantity quantity,
  Money unitCost, DateTimeOffset occurredUtc, string recordedByUserId, lotNumber?, expiryUtc?,
  sourceEntityType?, sourceEntityId?, operationId?, reason?, reversesMovementId?)` → `Result<StockMovement>`.
  **Sign rule enforced by factory** (`MOVEMENT_SIGN_INVALID`). `MovementKind`:
  `Receipt=0` (inbound +), `Sale=1`, `Consumption=2`, `Loss=3`, `AdjustmentIn=4` (inbound +),
  `AdjustmentOut=5`, `TransferIn=6`, `TransferOut=7`, `ReturnIn=8`, `ReturnOut=9`.
  `IsInboundKind(kind)` → Receipt/AdjustmentIn/TransferIn/ReturnIn. `Quantity.Value` is signed
  decimal. There is NO single `MovementKind.Adjustment` — the phase's "MovementKind.Adjustment" maps
  to **AdjustmentIn (positive delta) or AdjustmentOut (negative delta)** depending on delta sign.
- `Quantity` value object (`src/Animora.Domain/ValueObjects/Quantity.cs`): `Value` (decimal),
  `UnitCode`, `Zero(unit)`, `Negate()`, `Abs()`, `Add`, `Subtract`, `IsNegative`. Persisted as
  `<N>Unit` (TEXT) + `<N>Scaled` (INTEGER ×1000) via `FixedScaleQuantityConverter` (A10a). The A10
  `StockOnHandQuery` sums `m.Quantity.Value` grouped by `(ProductId, VariantId)`.
- `Money` (`src/Animora.Domain/ValueObjects/Money.cs`): `Minor` (long), `Currency`, `Money.Irt(minor)`,
  `Money.Zero(cur)`, `ApplyPercent`, `Add`, `Subtract`. Currency default `CurrencyScale.Irt`.
- `PurchaseOrder` (`.../Purchasing/PurchaseOrder.cs`): `AggregateRoot`. State enum
  `PurchaseOrderState`: `Draft=0, Submitted=1, PartiallyReceived=2, Received=3, Cancelled=4, Closed=5`.
  Factory `Create(tenantId, orderNumber, supplierId, orderedUtc, createdByUserId, currency=Irt,
  expectedUtc?, notes?)`. `AddLine(PurchaseOrderLine)`, `RemoveLine(id)`, `Submit(IClock)` (raises
  `PurchaseOrderSubmittedEvent`), `ApplyReceipt(GoodsReceipt)` (advances per-line received qty +
  transitions to PartiallyReceived/Received; **does NOT write stock**), `Cancel(reason)`, `Close()`,
  `RecalculateTotals()`. Totals: `Subtotal/DiscountTotal/TaxTotal/GrandTotal` (Money). Lines frozen
  once `State != Draft` (`PO_LOCKED`). `IsTransitionAllowed(from,to)`.
- `PurchaseOrderLine` record: `LineId, ProductId, VariantId, OrderedQuantity(Quantity),
  ReceivedQuantity(Quantity), UnitCost(Money), DiscountPercent(dec), TaxPercent(dec), LotNumber?,
  ExpiryUtc?`. Derived: `Gross, DiscountAmount, NetAmount, TaxAmount, LineTotal, OutstandingQuantity`.
- `GoodsReceipt` (`.../Purchasing/GoodsReceipt.cs`): `AggregateRoot`. State
  `GoodsReceiptState`: `Draft=0, Posted=1, Reversed=2`. Factory `Create(tenantId, receiptNumber,
  supplierId, receivedUtc, receivedByUserId, purchaseOrderId?, invoiceReference?, reversesReceiptId?)`.
  `AddLine(GoodsReceiptLine)`, `Post(IClock)` (freezes + raises `GoodsReceiptPostedEvent`),
  `MarkReversed()`. **Never writes ledger itself** — C06's `ReceiveGoodsCommand` handler appends the
  `StockMovement` rows.
- `GoodsReceiptLine` record: `LineId, ProductId, VariantId, Quantity(Quantity, positive), UnitCost(Money),
  LotNumber?, ExpiryUtc?`. `LineCost`.
- `Supplier` exists at `src/Animora.Domain/Purchasing/Supplier.cs` (read it when writing sample data
  & purchasing queries — need its factory + fields for seeding 6 suppliers).
- Events (`src/Animora.Domain/Events/InventoryEvents.cs`): contains `GoodsReceivedEvent`? Verify.
  Phase says `ReceiveGoodsCommand` "raises `GoodsReceivedEvent`". `GoodsReceipt.Post` raises
  `GoodsReceiptPostedEvent`. Check InventoryEvents.cs for the exact event name to raise from the
  handler; if `GoodsReceivedEvent` exists use it, else raise `GoodsReceiptPostedEvent` via the
  aggregate (preferred: let `GoodsReceipt.Post(clock)` raise it, then the handler appends movements).

### 0.3 ErrorCodes facts (`src/Animora.Domain/Results/ErrorCodes.cs`)
- Pattern: `public static readonly ErrorCode X = new("SCREAMING_SNAKE", ErrorCode.<Category>);`
  Categories seen: `Validation, NotFound, Conflict, Forbidden, Integrity, Entitlement, Infrastructure`.
- Region `#region Inventory & Purchasing (Phase A06)` (lines ~297–356) already has:
  `ServiceNoStock, MovementSignInvalid, InsufficientStock, UnitMismatch, CategoryCycle,
  VariantRequired, PurchaseOrderInvalidTransition (PO_INVALID_TRANSITION), PurchaseOrderLocked
  (PO_LOCKED), PurchaseOrderTotalMismatch (PO_TOTAL_MISMATCH), ReceiptLocked, ProductSkuDuplicate`.
- **8 codes to ADD** (append inside a new `#region Inventory & Purchasing (Phase C06)` at end, or
  extend A06 region). String values (SCREAMING_SNAKE) + category:
  1. `ProductNotFound` = `PRODUCT_NOT_FOUND` (NotFound)
  2. `ProductHasStock` = `PRODUCT_HAS_STOCK` (Conflict)
  3. `VariantNotFound` = `VARIANT_NOT_FOUND` (NotFound)
  4. `AdjustmentReasonInvalid` = `ADJUSTMENT_REASON_INVALID` (Validation)
  5. `StockWouldGoNegative` = `STOCK_WOULD_GO_NEGATIVE` (Conflict)
  6. `PurchaseOrderNotFound` = `PURCHASE_ORDER_NOT_FOUND` (NotFound)
  7. `PurchaseOrderStateInvalid` = `PURCHASE_ORDER_STATE_INVALID` (Conflict)
  8. `ReceiptExceedsOrder` = `RECEIPT_EXCEEDS_ORDER` (Conflict)
  (Do NOT collide with existing `PurchaseOrderInvalidTransition`; `PurchaseOrderStateInvalid` is a
  distinct new code with a distinct string.)

### 0.4 Application use-case pattern (verified from `UseCases/Boarding/`)
- One file per context grouping is fine (`BoardingCommands.cs`, `BoardingQueries.cs`,
  `BoardingRegistration.cs`, `ReadModels/BoardingReadModels.cs`). Follow the same for Inventory &
  Purchasing.
- **Command shape** (exact):
  ```csharp
  [RequiresPermission(PermissionCodes.<Perm>)]           // string const; see 0.6
  [Auditable(AuditCategory.Inventory, CommandTypes.<Type>)]
  public sealed record <Verb><Agg>Command(...) : ICommand<TResult>, IJournaledCommand
  {
      public string CommandTypeName => CommandTypes.<Type>;
      public string AggregateTypeName => "Product";      // or "PurchaseOrder"/"GoodsReceipt"
      public string? AggregateIdOrNull => <id or null>;
  }
  public sealed class <...>Validator : IValidator<<...>Command> { ... Rules.Required(...) ... }
  public sealed class <...>Handler(<ports>) : ICommandHandler<<...>Command, TResult>
  { public async Task<Result<TResult>> HandleAsync(<...>Command c, CommandContext context, CancellationToken ct){...} }
  ```
  `context.TenantId`, `context.UserId` available. Result type `Unit` (from `Animora.Domain.Primitives`)
  for void; `string` for created-id.
- **`AuditCategory`** (`src/Animora.Application/Auditing/IAuditWriter.cs`) — VERIFIED: members are
  `Security, Financial, Clinical, Configuration, Integrity, Maintenance`. **There is NO `Inventory`
  member.** The phase text says `AuditCategory.Inventory`. **Action:** add `Inventory` to the enum
  (additive, safe) in T01, and use `AuditCategory.Inventory` on the C06 command attributes.
- **Query shape** (exact):
  ```csharp
  [RequiresPermission(PermissionCodes.<ReadPerm>)]
  public sealed record <Subject>Query(...) : IQuery<TResult>;
  public sealed class <Subject>QueryHandler(IInventoryQueries queries) : IQueryHandler<<Subject>Query, TResult>
  { public async Task<Result<TResult>> HandleAsync(...) => Result<TResult>.Ok(await queries....); }
  ```
- **Registration** via `ApplicationRegistrationBuilder` extension `AddInventoryUseCases` /
  `AddPurchasingUseCases`, chained with `.AddQuery<Q,R,H>()`, `.AddCommand<C,R,H>()`,
  `.AddValidator<C,V>()`. VERIFIED call site — the chain is invoked in **`src/Animora.Desktop/App.axaml.cs:192`**:
  `services.AddAnimoraApplication(builder => builder.AddIdentityUseCases().AddPatientsUseCases()
  .AddReceptionUseCases().AddSchedulingUseCases().AddBoardingUseCases().AddVisitsUseCases()
  .AddVaccinationUseCases());`. Append `.AddInventoryUseCases().AddPurchasingUseCases()` to that
  chain (this edit belongs to T11 since it's in App.axaml.cs; the registration extension classes are
  created in T07/T08).

### 0.5 Ports & queries pattern
- Application ports live in `src/Animora.Application/Ports/`. Existing:
  `IProductRepository`, `IStockMovementRepository`, `IPurchasingRepository` (each:
  `FindAsync/RequireAsync/Add/AddRange`). **No `IInventoryQueries`/`IPurchasingQueries` port yet** —
  create them in `Ports/` (interface, methods returning read models). Impl in
  `src/Animora.Persistence.Sqlite/Queries/InventoryQueries.cs` &
  `PurchasingQueries.cs` (class taking `AnimoraLocalDbContext db`, `AsNoTracking()`, explicit
  `Select` projections, `QueryPaging.ToPagedAsync` for paging — see `Queries/StockOnHandQuery.cs`
  and `Queries/QueryPaging.cs`). Register both with `TryAddScoped` in
  `LocalStoreServiceCollectionExtensions.cs`.
- `StockOnHandQuery` (A10) already sums movements — but C06 grid must join the **snapshot** table for
  O(1) reads (avoid N+1). Catalog query reads `StockOnHandSnapshot.OnHand`, not a live SUM.

### 0.6 Permissions — VERIFIED (`src/Animora.Domain/Identity/PermissionCodes.cs`)
- The phase's names (`inventory.product.edit`, `inventory.stock.adjust`, `purchasing.order.manage`)
  do **NOT** exist. The actual existing consts in the `#region Inventory & purchasing`:
  - `InventoryRead = "inventory.read"`  ← use for all inventory queries
  - `InventoryAdjust = "inventory.adjust"`  ← use for `AdjustStockCommand` AND product create/update/
    variant/delete (there is no separate product-edit code; `InventoryAdjust` is the closest edit
    permission and is already granted to the manager role at line ~204). **Decision:** use
    `InventoryAdjust` for create/update/variant/delete/adjust to avoid inventing new permission codes
    that also need `DefaultRolePermissions` wiring. (If a reviewer insists on separation, adding an
    `InventoryManage = "inventory.manage"` const + DefaultRolePermissions grant is the alternative —
    but not required; document the choice.)
  - `InventoryReceive = "inventory.receive"`  ← use for `ReceiveGoodsCommand`
  - `PurchasingRead = "purchasing.read"`  ← use for `PurchasingSnapshotQuery`
  - `PurchasingManage = "purchasing.manage"`  ← use for `CreatePurchaseOrderCommand` &
    `CancelPurchaseOrderCommand`
  No new permission codes are strictly needed. Do NOT rename existing ones.

### 0.7 Snapshot table & updater (the core new mechanism)
- New EF entity `StockOnHandSnapshot` in `src/Animora.Persistence.Sqlite/Entities/`, marked
  `[LocalOnly]` (see other entities in that folder for the attribute + the "not tenant-filtered vs
  filtered" comment convention; snapshot IS tenant-scoped so include `TenantId` and a tenant filter
  is fine). Columns: `TenantId` (string), `ProductId` (string), `VariantId` (string), `OnHand`
  (long — exact ×1000 scaled to match `QuantityScaled`, OR store the summed scaled integer; the A10
  sum is over `Quantity.Value` decimal but persisted `QuantityScaled` is ×1000 INTEGER — **store
  `OnHand` as the ×1000 scaled long** to be exact and index-comparable; document this), `LastMovementUtc`
  (DateTimeOffset via the UTC-ticks converter), `LastMovementId` (string). PK could be a synthetic id
  or composite `(TenantId, ProductId, VariantId)`. Unique index
  `IX_StockOnHandSnapshots_TenantId_ProductId_VariantId`.
- Configuration `Configurations/Inventory/StockOnHandSnapshotConfiguration.cs` (mirror an existing
  config in that folder for style: `IEntityTypeConfiguration<T>`, `ToTable`, key, `HasIndex(...).IsUnique()`,
  converters for DateTimeOffset).
- `Inventory/StockSnapshotUpdater.cs`: given the added `StockMovement`(s) in the change set, upsert
  the snapshot row inside the SAME transaction. **Hook point:** the phase says hook into
  `LocalUnitOfWork`'s save path. Concrete mechanism (verified): `AnimoraLocalDbContext.PrepareSave()`
  runs `AppendOnlyGuard.Check(this)` then stamps `SyncableEntity` rows. `LocalUnitOfWork.RunAsync`
  calls `db.SaveChangesAsync` then dispatches events. **Recommended concrete approach:** the
  `ReceiveGoodsCommand` and `AdjustStockCommand` handlers, after adding the movement(s) via
  `IStockMovementRepository.Add`, call `StockSnapshotUpdater.Apply(movement)` (a scoped service that
  reads/creates the snapshot row and adds the delta) BEFORE the UoW saves — this keeps it in the same
  transaction because handlers run inside `UoW.ExecuteAsync`. Alternatively walk the ChangeTracker
  for `Added` `StockMovement` inside `PrepareSave` and update snapshots there (like `AppendOnlyGuard`).
  **Choose the ChangeTracker-in-DbContext approach** so EVERY added movement (from any path, incl.
  future C07 sales) maintains the snapshot automatically — this satisfies "every added StockMovement
  maintains it". Implement `StockSnapshotUpdater.Maintain(DbContext db)` called from `PrepareSave()`
  AFTER `AppendOnlyGuard.Check`. It: for each `Added` `StockMovement`, find/create the snapshot row
  for its `(TenantId,ProductId,VariantId)` in the local `ChangeTracker` (or DB), add the scaled
  signed qty, set `LastMovementUtc`/`LastMovementId`. Guard against double-counting on retries: a
  movement is only ever `Added` once per transaction, so it's safe.
- `ILocalStoreMaintenance.RebuildStockSnapshotsAsync(CancellationToken ct)` → `Task<Result<int>>`
  (interface in `src/Animora.Persistence.Sqlite/Maintenance/`; impl in `LocalStoreMaintenance.cs`).
  Recompute: delete all snapshot rows, re-SUM movements grouped by key, insert fresh rows; return
  count. Call it from A11's corrupt-DB rebuild path (`QuarantineAndRebuild`/init) — find where the
  store rebuilds and add a call so a rebuilt DB has correct snapshots.

### 0.8 Client.Services pattern (verified from `Patients/PatientsService.cs`)
- `sealed class <X>Service(IServiceScopeFactory scopes) : IContract`. Per call:
  `await using var scope = scopes.CreateAsyncScope(); var (dispatcher, provider) = Resolve(scope);`
  build query/command context via `provider.BuildQueryContext()` / command context, dispatch, map
  result to Contracts DTO, return `[]`/`null` on failure. `MaxPageSize = 200`. No EF/SQL here.
  Copy the `Resolve` helper from `PatientsService`.
- Mapping file `Inventory/InventoryMapping.cs`: static extension methods read model ↔ Contracts DTO.
  Contracts DTOs (from `src/Animora.Contracts/Inventory/InventoryModels.cs`): `Product(Id, Code, Name,
  Brand, CategoryId, Glyph, Tone, Description, UnitInfo? Attribute, IReadOnlyList<ProductVariant> Variants)`,
  Contracts `ProductVariant(Id, Label, Sku, int Stock, int ReorderPoint, long Cost, long Price, string? Expiry)`
  — **`Stock` here is a computed int filled FROM the snapshot/on-hand**, not stored on the domain.
  `CategoryInfo(Id,Label,Tone,IconKey)`, `StockMovement(Id,ProductId,VariantId?,Kind("in"/"out"/"adjust"),
  Qty(int signed),Source,By,Date,Time,Note?)`, `ProductDetail(Product, IReadOnlyList<StockMovement>)`,
  request DTOs: `ProductInfoUpdate`, `NewVariantRequest(Label)`, `NewProductRequest(...)`,
  `StockAdjustRequest(int Stock)`. NOTE the Contracts `StockAdjustRequest` carries an **absolute
  target Stock**, but the C06 `AdjustStockCommand` takes a **DeltaQuantity + ReasonCode**. Bridge in
  the service: compute `delta = targetStock - currentOnHand`, pick a default reason (`Correction`)
  since the Contracts DTO has no reason field — document this bridge (a richer reason UI is out of
  Contracts scope per phase). Map Jalali date/time strings: reuse the existing Persian/Jalali
  formatter used by other mappings (grep other `*Mapping.cs` in Client.Services for the calendar
  helper; do not invent).
- `IInventoryQueryService` methods to implement (from `src/Animora.Contracts/Inventory/IInventoryQueryService.cs`):
  `GetProductsAsync, GetCategoriesAsync, GetDetailAsync(id), SaveProductInfoAsync(id, update),
  AddVariantAsync(id, req)→ProductVariant, GetHistoryAsync(id), CreateProductAsync(req)→Product,
  DeleteProductAsync(id), AdjustStockAsync(id, variantId, req)`.
- `IPurchasingQueryService.GetSnapshotAsync()→PurchasingSnapshot` (from Contracts/Purchasing). DTOs:
  `PurchasingSnapshot(PurchaseKpi ActiveOrders, PurchaseKpi MonthlyPurchases, PurchaseKpi SupplierDebt,
  IReadOnlyList<PurchaseOrderSummary> Orders)`, `PurchaseKpi(Value, Label, DeltaText, IsDeltaPositive,
  IconKey, Tone)` (all pre-formatted Persian strings), `PurchaseOrderSummary(Id, Number, Supplier,
  OrderDate, ItemCountText, AmountText, Settlement, PurchaseOrderStatus Status)`,
  `PurchaseOrderStatus{Delivered,InProgress,Finalized}`. Map domain `PurchaseOrderState`→ this enum:
  Received/Closed→Delivered or Finalized, PartiallyReceived/Submitted→InProgress, Draft→InProgress,
  Cancelled→(exclude or InProgress). Decide a clear mapping and document it.

### 0.9 DI swap (`src/Animora.Desktop/App.axaml.cs`)
- Line ~287: `services.AddSingleton<IInventoryQueryService, MockInventoryQueryService>();`
  → `services.AddSingleton<IInventoryQueryService, InventoryQueryService>();`
- Line ~292: `services.AddSingleton<IPurchasingQueryService, MockPurchasingQueryService>();`
  → `services.AddSingleton<IPurchasingQueryService, PurchasingQueryService>();`
- Add `using Animora.Client.Services.Inventory;` / `...Purchasing;` (or fully-qualify).
- Ensure `AddInventoryUseCases()`/`AddPurchasingUseCases()` are wired into the Application
  registration (0.4) so the dispatcher knows the handlers.

### 0.10 ViewModel readiness-gate line
- Pattern (from `BoardingPageViewModel`): ctor takes `IStoreReadinessGate readiness`, stored in
  `_readiness`, and the load method does `await _readiness.WaitAsync();` before its first backend
  call. Add the SAME single line to `InventoryPageViewModel` and `PurchasingPageViewModel`:
  inject `IStoreReadinessGate`, store it, and `await _readiness.WaitAsync();` at the top of the
  load/refresh method. Change ONLY what's needed (CONVENTIONS §10 item 6). `IStoreReadinessGate` is
  already registered (App.axaml.cs lines 220–221).

### 0.11 Mocks to delete
- `src/Animora.Desktop/Mocks/MockInventoryQueryService.cs`
- `src/Animora.Desktop/Mocks/MockPurchasingQueryService.cs`
- Delete in the SAME task as the DI swap; grep to confirm no other reference remains.

---

## 1. TASK LIST (do in order; push after each)

- [x] **T01 — Domain: AdjustmentReasons + 8 error codes.**
  Create `src/Animora.Domain/Inventory/AdjustmentReasons.cs`: a catalog of valid adjustment reason
  codes `Count, Damage, Expiry, Theft, Correction` (as string consts + a `IsValid(string)` / a
  `IReadOnlySet<string>` `All`, matching how other code catalogs are shaped in Domain — mirror e.g.
  `PermissionCodes`/`AccountCodes` style). Append the **8 error codes** from §0.3 to
  `src/Animora.Domain/Results/ErrorCodes.cs` (new region `#region Inventory & Purchasing (Phase C06)`).
  **Also add `Inventory` to the `AuditCategory` enum** in
  `src/Animora.Application/Auditing/IAuditWriter.cs` (additive, needed by the command attributes in
  T07/T08). Build `Animora.Domain` + `Animora.Application` to confirm.
  **Push:** `feat(domain): C06 adjustment reasons + 8 error codes + AuditCategory.Inventory`.

- [x] **T02 — Persistence: StockOnHandSnapshot entity + configuration + migration 013.**
  Create `Entities/StockOnHandSnapshot.cs` (`[LocalOnly]`, fields per §0.7),
  `Configurations/Inventory/StockOnHandSnapshotConfiguration.cs` (map + unique index
  `IX_StockOnHandSnapshots_TenantId_ProductId_VariantId`, DateTimeOffset converter). Register the
  entity/config in `AnimoraLocalDbContext` (find where other configs/entities are added —
  `ApplyConfigurationsFromAssembly` or explicit `modelBuilder`). Add `DbSet` if the codebase uses
  explicit sets. Generate migration **`013_InventorySnapshot`** (timestamp > 20260928120000) using
  the repo's `dotnet-ef` (tools at `.sandbox/tools`; see A10d entry / a prior migration for the exact
  command). Verify `ef migrations list` shows 001..013 and there are **no pending model changes**.
  Do NOT touch 001–012. **Push:** `feat(persist): StockOnHandSnapshot + migration 013_InventorySnapshot`.

- [x] **T03 — Persistence: StockSnapshotUpdater + hook into save path.**
  Create `Inventory/StockSnapshotUpdater.cs` with `Maintain(AnimoraLocalDbContext db)` that walks
  `ChangeTracker.Entries<StockMovement>()` in state `Added` and upserts the snapshot row
  (add scaled signed qty, set `LastMovementUtc`/`LastMovementId`). Call it from
  `AnimoraLocalDbContext.PrepareSave()` immediately after `AppendOnlyGuard.Check(this)` and before/at
  the stamping loop (so new snapshot rows also get their `TenantId` if they're `SyncableEntity` — but
  snapshot is `[LocalOnly]`, set TenantId explicitly in the updater). Keep it allocation-light,
  `for`-loop style (PERFORMANCE.md §2). Build only `Animora.Persistence.Sqlite`.
  **Push:** `feat(persist): in-transaction StockSnapshotUpdater hooked into save path`.

- [x] **T04 — Persistence: RebuildStockSnapshotsAsync on ILocalStoreMaintenance.**
  Add `Task<Result<int>> RebuildStockSnapshotsAsync(CancellationToken ct)` to `ILocalStoreMaintenance`
  and implement in `LocalStoreMaintenance.cs` (delete all snapshot rows for tenant, re-SUM movements
  by `(TenantId,ProductId,VariantId)`, insert fresh rows, return inserted count). Call it from A11's
  corrupt-DB rebuild path (find `QuarantineAndRebuild`/init in `Maintenance`/`Startup`; add the call
  after `MigrateAsync`/seed so a rebuilt DB is consistent). Build `Animora.Persistence.Sqlite`.
  **Push:** `feat(persist): RebuildStockSnapshotsAsync + call from rebuild path`.

- [x] **T05 — Persistence: InventoryQueries + PurchasingQueries + ports + DI.**
  Create Application ports `Ports/IInventoryQueries.cs` and `Ports/IPurchasingQueries.cs` (methods
  returning the read models from T07/T08 — define read models first if needed, or co-develop). Impl:
  `Queries/InventoryQueries.cs` (catalog paged read joining `StockOnHandSnapshot`; `LowStockOnly`
  compares `OnHand <= variant ReorderPoint` in SQL; product detail = product+variants + last 100
  movements; categories; movement history paged), `Queries/PurchasingQueries.cs` (KPI grouped
  aggregates — **≤3 SELECTs total**: one grouped PO-count/state block, one MTD-spend block, one
  PO-list; top suppliers derivable within those). Use `AsNoTracking`, explicit `Select`,
  `QueryPaging.ToPagedAsync`. Register both via `TryAddScoped` in
  `LocalStoreServiceCollectionExtensions.cs` next to the other query ports (~line 75).
  Build `Animora.Persistence.Sqlite`.
  **Push:** `feat(persist): InventoryQueries + PurchasingQueries (snapshot-joined, grouped KPIs)`.

- [ ] **T06 — Wire: CommandTypes += 7.**
  In `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` add the 7 new consts (§0.1):
  `InventoryProductCreate="inventory.product.create"`, `InventoryProductUpdate="inventory.product.update"`,
  `InventoryVariantAdd="inventory.variant.add"`, `InventoryProductDelete="inventory.product.delete"`,
  `PurchasingOrderCreate="purchasing.order.create"`, `PurchasingReceiptRecord="purchasing.receipt.record"`,
  `PurchasingOrderCancel="purchasing.order.cancel"`. Add all 7 to the `All` list. Do NOT touch the
  existing `inventory.stock.adjust`. Build `Animora.Contracts.Wire`.
  **Push:** `feat(wire): CommandTypes += 7 (inventory/purchasing C06)`.

- [x] **T07 — Application: Inventory read models + 4 queries + 5 commands + registration.**
  In `src/Animora.Application/UseCases/Inventory/`: `ReadModels/InventoryReadModels.cs`
  (`ProductReadModel`, `ProductDetailReadModel` incl. last-100 movements, `CategoryReadModel`,
  `MovementReadModel`). **`PagedResult<T>` already exists** at
  `src/Animora.Application/Abstractions/PagedResult.cs` — reuse it, do not redefine. Queries (`InventoryQueries.cs`
  in Application — distinct from the Persistence file; namespace differs): `ProductCatalogQuery(Search?,
  CategoryId?, LowStockOnly, Page, PageSize)`, `ProductDetailQuery(ProductId)`,
  `ProductCategoriesQuery()`, `StockMovementHistoryQuery(ProductId, Page, PageSize)`. Commands
  (`InventoryCommands.cs`): `CreateProductCommand`, `UpdateProductInfoCommand`,
  `AddProductVariantCommand`, `DeleteProductCommand`, `AdjustStockCommand`. Attributes/permissions
  (REAL const names, §0.6): create/update/variant/delete/adjust →
  `[RequiresPermission(PermissionCodes.InventoryAdjust)]`; queries →
  `[RequiresPermission(PermissionCodes.InventoryRead)]`; all
  `[Auditable(AuditCategory.Inventory, CommandTypes.X)]` and `IJournaledCommand`.
  **Business rules to implement exactly:**
  - `DeleteProductCommand` = soft delete (`product.Deactivate()` + set `Deleted=true`); **refuse**
    with `PRODUCT_HAS_STOCK` if the product has any movement in the last 90 days OR non-zero on-hand.
  - `AdjustStockCommand(ProductId, VariantId, long DeltaQuantity, string ReasonCode, string? Note)`:
    validate `ReasonCode` ∈ `AdjustmentReasons` else `ADJUSTMENT_REASON_INVALID`; compute new on-hand
    = currentOnHand + DeltaQuantity; if new < 0 return `STOCK_WOULD_GO_NEGATIVE` **unless**
    `ReasonCode == Count` (a physical count may correct down to zero but never below zero — clamp
    behaviour: Count may reach 0, still reject going strictly below 0). Write ONE `StockMovement`:
    kind `AdjustmentIn` if Delta>0 else `AdjustmentOut`, `Quantity` signed accordingly, `reason=ReasonCode`.
    `VARIANT_NOT_FOUND` if variant missing; `PRODUCT_NOT_FOUND` if product missing.
  Register in `InventoryRegistration.cs` (`AddInventoryUseCases`). (The composition-chain edit in
  `App.axaml.cs:192` is done in T11.) Build `Animora.Application`.
  **Push:** `feat(app): inventory use cases (4 queries + 5 commands)`.

- [ ] **T08 — Application: Purchasing read models + 1 query + 3 commands + registration.**
  In `src/Animora.Application/UseCases/Purchasing/`: `ReadModels/PurchasingReadModels.cs`
  (`PurchasingSnapshotReadModel`: open/received/cancelled PO counts, MTD spend, top suppliers, PO list).
  Query `PurchasingSnapshotQuery()` → `PurchasingSnapshotReadModel` (delegates to `IPurchasingQueries`,
  ≤3 SELECTs). Commands (`PurchasingCommands.cs`): `CreatePurchaseOrderCommand`,
  `ReceiveGoodsCommand`, `CancelPurchaseOrderCommand`. Permissions (§0.6): create/cancel →
  `[RequiresPermission(PermissionCodes.PurchasingManage)]`; `ReceiveGoodsCommand` →
  `[RequiresPermission(PermissionCodes.InventoryReceive)]`; `PurchasingSnapshotQuery` →
  `[RequiresPermission(PermissionCodes.PurchasingRead)]`. All
  `[Auditable(AuditCategory.Inventory, CommandTypes.X)]` + `IJournaledCommand`. **`ReceiveGoodsCommand` (implement exactly):** in one transaction —
  create a `GoodsReceipt`, `AddLine` per received line, `Post(clock)`; append ONE `StockMovement`
  of `MovementKind.Receipt` per received line **carrying `UnitCostMinor`** onto the movement
  (`unitCost: Money.Irt(line.UnitCostMinor)`); the snapshot auto-updates via T03; call
  `purchaseOrder.ApplyReceipt(receipt)` to transition PO to PartiallyReceived/Received; raise the
  goods-received event (via `GoodsReceipt.Post`). **Over-receipt** beyond ordered qty →
  `RECEIPT_EXCEEDS_ORDER` unless `AllowOverReceipt` set on the command. `PURCHASE_ORDER_NOT_FOUND` /
  `PURCHASE_ORDER_STATE_INVALID` as appropriate. Register in `PurchasingRegistration.cs`
  (`AddPurchasingUseCases`). (Composition-chain edit is in T11.) Build `Animora.Application`.
  **Push:** `feat(app): purchasing use cases (snapshot query + 3 commands, ReceiveGoods writes receipt movements)`.

- [ ] **T09 — Persistence: seed 12 categories + 8 units + InventorySampleData.**
  In `Seed/LocalSeeder.cs` expand `Units` to 8 (add `("TAB",0)`) and `Categories` to 12 (add the 7
  from §0.1). Create `Seed/InventorySampleData.cs` mirroring `BoardingSampleData` shape
  (`SampleKey` const, `AlreadySeeded()`, `Populate()`): 45 products across the seeded categories,
  90 variants, one opening `MovementKind.Receipt` per variant (positive qty so on-hand starts
  non-zero and C09 has a basis), 6 suppliers (read `Supplier` factory), 8 purchase orders in mixed
  states (Draft/Submitted/PartiallyReceived/Received/Cancelled). Add it behind
  `LocalStoreOptions.SeedSampleData` in `LocalSeeder.SeedAsync` (new marker-guarded block, same
  pattern as boarding). Register `InventorySampleData` in DI (constructor of `LocalSeeder` + its DI
  registration — find where `BoardingSampleData` is registered). Build `Animora.Persistence.Sqlite`.
  **Push:** `feat(persist): seed 12 categories + 8 units + InventorySampleData`.

- [ ] **T10 — Client.Services: InventoryMapping + InventoryQueryService + PurchasingQueryService.**
  Create `src/Animora.Client.Services/Inventory/InventoryMapping.cs`,
  `Inventory/InventoryQueryService.cs`, `Purchasing/PurchasingQueryService.cs` per §0.8 (copy the
  `Resolve`/scope pattern from `Patients/PatientsService.cs`). Implement all 9 `IInventoryQueryService`
  members + `IPurchasingQueryService.GetSnapshotAsync`. Bridge `StockAdjustRequest(int Stock)` →
  `AdjustStockCommand` delta+`Correction` reason (§0.8). Map Jalali strings via the existing calendar
  helper used in other mappings. Build `Animora.Client.Services`.
  **Push:** `feat(client): InventoryQueryService + PurchasingQueryService + mapping`.

- [ ] **T11 — Desktop: DI swap + 2 ViewModel readiness lines + delete 2 mocks.**
  In `App.axaml.cs`: (a) swap the two DI lines (§0.9) + add usings; (b) append
  `.AddInventoryUseCases().AddPurchasingUseCases()` to the `AddAnimoraApplication(builder => ...)`
  chain at **line 192** (§0.4). Add the readiness-gate line to `InventoryPageViewModel` and
  `PurchasingPageViewModel` (§0.10).
  Delete `Mocks/MockInventoryQueryService.cs` and `Mocks/MockPurchasingQueryService.cs`. Grep to
  confirm zero remaining references to the mock types. Build `Animora.Desktop`.
  **Push:** `refactor(desktop): swap to real inventory/purchasing services, delete mocks, readiness gate`.

- [ ] **T12 — Tests: 3 test classes.**
  `tests/Animora.Application.Tests/InventoryUseCaseTests.cs`: unknown reason → `ADJUSTMENT_REASON_INVALID`;
  `Damage` below zero → `STOCK_WOULD_GO_NEGATIVE`; `Count` may reach 0 but not below; delete with
  on-hand → `PRODUCT_HAS_STOCK`; receiving 7 of ordered 5 without `AllowOverReceipt` →
  `RECEIPT_EXCEEDS_ORDER`; a receipt appends `MovementKind.Receipt` movements.
  `tests/Animora.Persistence.Tests/StockSnapshotIntegrityTests.cs`: after 500 randomized movements,
  snapshot == recomputation per key; a stored EF model has **no** `Stock`/`OnHand`/`Quantity`
  property on `Product`/`ProductVariant` (reflect over the EF model); `RebuildStockSnapshotsAsync`
  repairs a deliberately corrupted row.
  `tests/Animora.Persistence.Tests/PurchasingQueryTests.cs`: KPI numbers match seeded data; snapshot
  query issues ≤3 SELECTs (capture via a command interceptor / `ToQueryString` count as other
  persistence tests do — check existing tests for the technique). Use temp **file** DBs (never
  `:memory:`), inject `IClock`. Follow existing test fixtures (`LocalStoreTestFixture`, `FakeClock`).
  Run ONLY these test classes (`dotnet test --filter`), not the full suite.
  **Push:** `test: inventory use cases + snapshot integrity + purchasing query`.

- [ ] **T13 — Final verification + PROGRESS.md update.**
  Run `scripts/sandbox/build.sh full` (expect 0/0) and `scripts/sandbox/run.sh` (expect `SMOKE RUN OK`);
  run `scripts/sandbox/test.sh` if time permits (or the 3 filtered classes). Confirm acceptance
  criteria from the phase (§ Acceptance criteria). Update `backend-roadmap/PROGRESS.md`: mark C06
  done in the status table, advance "Next phase" to `C07`, append a "Repo state log" `### C06` entry
  listing everything built (snapshot table + updater + "maintained cache with a proof" decision that
  C07/C09/D07 rely on, `RebuildStockSnapshotsAsync`, `AdjustmentReasons`, migration
  `013_InventorySnapshot`, the 13 use cases, `InventorySampleData` + seeded categories/units, the 3
  client services/mappings, the 2 DI swaps, mocks deleted, `CommandTypes` += 7, `ErrorCodes` += 8,
  the 3 test classes) and **log the deviations**: migration is `013` not `012`; seed marker/schema
  note; `MovementKind.Adjustment` mapped to AdjustmentIn/AdjustmentOut; `StockAdjustRequest`
  delta-bridge with `Correction` reason.
  **Push:** `docs(progress): mark C06 done, advance to C07, record C06 deviations`.

- [ ] **T14 — Delete this plan file (final task).**
  Delete `Plan/PHASE-C06-inventory-purchasing.todo.md` ONLY. Do not touch any other file, do not
  touch the roadmap phase file. **Push:** `chore(plan): remove completed C06 plan file`.

---

## 2. Locked rules recap
- After EVERY task above: `scripts/git/push.sh "<message>"`. If it exits 3 → call
  `setup_github_environment` once, re-run. If exit 2 → resolve conflict (prefer remote), re-run.
  **No push = failure.**
- No `build.sh full`/`test.sh` mid-flight (only in T13). Build only the touched project to sanity-check.
- Purposeful reading only — the file/line references above are sufficient; don't re-scan the repo.
- Never read `Animora-UI/**`, `windows-frontend-roadmap/phases/**`, or the whole PROGRESS log.
