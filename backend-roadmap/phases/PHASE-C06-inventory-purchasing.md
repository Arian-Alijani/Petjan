# Phase C06 — Inventory & Purchasing

**Band:** M · **Track:** C · **Depends on:** C01 · **Satisfies:** R-B01, R-B02, R-B23
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (the **C01 pattern**)
- `ARCHITECTURE.md` §11.2 · `PERFORMANCE.md` §5, §6
- `src/Animora.Contracts/Inventory/IInventoryQueryService.cs`,
  `src/Animora.Contracts/Inventory/InventoryModels.cs`
- `src/Animora.Contracts/Purchasing/IPurchasingQueryService.cs`,
  `src/Animora.Contracts/Purchasing/PurchasingModels.cs`
- `src/Animora.Domain/Inventory/Product.cs`, `src/Animora.Domain/Inventory/StockMovement.cs`,
  `src/Animora.Domain/Inventory/StockCalculator.cs`,
  `src/Animora.Domain/Purchasing/PurchaseOrder.cs`, `src/Animora.Domain/Purchasing/GoodsReceipt.cs`
- `src/Animora.Application/Ports/IProductRepository.cs`,
  `src/Animora.Application/Ports/IStockMovementRepository.cs`,
  `src/Animora.Application/Ports/IPurchasingRepository.cs`
- `src/Animora.Persistence.Sqlite/Queries/StockOnHandQuery.cs` (A10)
- `src/Animora.Desktop/App.axaml.cs` (lines registering `IInventoryQueryService`,
  `IPurchasingQueryService`)

## Goal
The inventory and purchasing screens run on real data with **on-hand derived from `StockMovement`
rows only** — never an absolute stored quantity. Goods receipt produces receipt movements; manual
adjustment produces an adjustment movement with a mandatory reason; categories and units are
persisted settings.

## Design decisions (pre-made — implement, do not re-derive)
- **On-hand is a projection, period.** `Contracts.ProductVariant.Stock` is filled from
  `StockOnHandQuery` (A10), which sums `StockMovement.SignedQuantity` per
  `(TenantId, ProductId, VariantId)`. There is **no** `Stock` column anywhere, in any table, ever.
  A test asserts that no EF model property named `Stock`/`OnHand`/`Quantity` exists on `Product` or
  `ProductVariant`.
- **On-hand performance:** a naive per-row sum would be an N+1 across the product grid, so this
  phase adds a **maintained snapshot table** `StockOnHandSnapshot` (`[LocalOnly]`:
  `TenantId`, `ProductId`, `VariantId`, `OnHand` (`long`), `LastMovementUtc`, `LastMovementId`)
  updated **inside the same transaction** by `StockSnapshotUpdater`
  (`Animora.Persistence.Sqlite/Inventory/StockSnapshotUpdater.cs`) whenever a `StockMovement` is
  added. The snapshot is a **cache with a proof**: `ILocalStoreMaintenance` (A11) gains
  `Task<Result<int>> RebuildStockSnapshotsAsync(CancellationToken ct)` which recomputes from
  movements, and `StockSnapshotIntegrityTests` asserts snapshot == recomputation after a randomized
  movement sequence. This preserves "derived, never stored absolute" (the movements remain the only
  source of truth) while keeping the grid a single indexed read.
  Unique index `IX_StockOnHandSnapshots_TenantId_ProductId_VariantId`. Migration
  `012_InventorySnapshot`.
- **Queries** (`UseCases/Inventory/`):
  - `ProductCatalogQuery(string? Search, string? CategoryId, bool LowStockOnly, int Page, int PageSize)`
    → `PagedResult<ProductReadModel>` joining the snapshot; `LowStockOnly` compares
    `OnHand <= ProductVariant.ReorderPoint` in SQL.
  - `ProductDetailQuery(string ProductId)` → product + variants + the **last 100** movements.
  - `ProductCategoriesQuery()`, `StockMovementHistoryQuery(string ProductId, int Page, int PageSize)`.
- **Inventory commands** (`UseCases/Inventory/`, all `IJournaledCommand` +
  `[RequiresPermission("inventory.product.edit")]` (adjust uses `inventory.stock.adjust`) +
  `[Auditable(AuditCategory.Inventory, ...)]`):
  `CreateProductCommand`, `UpdateProductInfoCommand`, `AddProductVariantCommand`,
  `DeleteProductCommand`, `AdjustStockCommand`.
  `CommandTypes` += `inventory.product.create`, `inventory.product.update`,
  `inventory.variant.add`, `inventory.product.delete`, `inventory.stock.adjust` (5 —
  `inventory.stock.adjust` already exists from B02's catalog; reuse it, do not redefine).
- **`DeleteProductCommand` is a soft delete** (`SyncableEntity.Deleted = true`) and is **refused**
  when the product has any movement in the last 90 days or a non-zero on-hand
  (`PRODUCT_HAS_STOCK`) — deleting stock history is never allowed (append-only).
- **`AdjustStockCommand(ProductId, VariantId, long DeltaQuantity, string ReasonCode, string? Note)`**
  writes one `StockMovement` of `MovementKind.Adjustment` with a **mandatory** reason from
  `AdjustmentReasons` (`Animora.Domain/Inventory/AdjustmentReasons.cs`: `Count`, `Damage`,
  `Expiry`, `Theft`, `Correction`) — an unknown reason returns `ADJUSTMENT_REASON_INVALID`. An
  adjustment that would drive on-hand negative returns `STOCK_WOULD_GO_NEGATIVE` unless the reason
  is `Count` (a physical count is authoritative and may correct downward past zero to zero, never
  below).
- **Purchasing** (`UseCases/Purchasing/`):
  - `PurchasingSnapshotQuery()` → `PurchasingSnapshotReadModel`: open/received/cancelled PO counts,
    month-to-date spend, top suppliers, and the PO list — **one** grouped statement per KPI block,
    three statements total, executed in parallel-free sequence inside one connection.
  - Commands: `CreatePurchaseOrderCommand`, `ReceiveGoodsCommand`, `CancelPurchaseOrderCommand`.
    `CommandTypes` += `purchasing.order.create`, `purchasing.receipt.record`,
    `purchasing.order.cancel` (3). Permission `purchasing.order.manage`.
  - **`ReceiveGoodsCommand` is the only inventory-increasing business path besides adjustment:** in
    one transaction it creates a `GoodsReceipt`, appends one `StockMovement` of
    `MovementKind.Receipt` per received line (with `UnitCostMinor` carried onto the movement so
    C09's COGS has a basis), updates the snapshot, transitions the PO to
    `PurchaseOrderState.PartiallyReceived` or `Received`, and raises `GoodsReceivedEvent`.
    Over-receipt beyond the ordered quantity returns `RECEIPT_EXCEEDS_ORDER` unless
    `AllowOverReceipt` is set on the command (an explicit, audited operator choice).
- **Settings persistence:** categories and units are reference data seeded by `LocalSeeder`
  (12 categories, 8 units) and editable through `CreateProductCommand`'s category reference only —
  a category/unit editor is **not** in the Contracts surface, so no command is added for it.
- **Sample data:** `Seed/InventorySampleData.cs` — 45 products across the seeded categories, 90
  variants, an opening `MovementKind.Receipt` per variant (so on-hand starts non-zero and the ledger
  in C09 has a basis), 6 suppliers, 8 purchase orders in mixed states.
- **ViewModel changes (exhaustive):** `InventoryPageViewModel` and `PurchasingPageViewModel` each
  gain the readiness-gate line.
- **New error codes:** `PRODUCT_NOT_FOUND`, `PRODUCT_HAS_STOCK`, `VARIANT_NOT_FOUND`,
  `ADJUSTMENT_REASON_INVALID`, `STOCK_WOULD_GO_NEGATIVE`, `PURCHASE_ORDER_NOT_FOUND`,
  `PURCHASE_ORDER_STATE_INVALID`, `RECEIPT_EXCEEDS_ORDER`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Inventory/AdjustmentReasons.cs` | create | reason catalog |
| `src/Animora.Application/UseCases/Inventory/*.cs` | create | 4 queries + 5 commands + handlers/validators |
| `src/Animora.Application/UseCases/Purchasing/*.cs` | create | 1 query + 3 commands + handlers/validators |
| `src/Animora.Application/UseCases/*/ReadModels/*.cs` | create | product, movement, purchasing read models |
| `src/Animora.Persistence.Sqlite/Entities/StockOnHandSnapshot.cs` | create | `[LocalOnly]` maintained projection |
| `src/Animora.Persistence.Sqlite/Inventory/StockSnapshotUpdater.cs` | create | in-transaction snapshot maintenance |
| `src/Animora.Persistence.Sqlite/Configurations/Inventory/StockOnHandSnapshotConfiguration.cs` | create | mapping + unique index |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `012_InventorySnapshot` + snapshot |
| `src/Animora.Persistence.Sqlite/Queries/InventoryQueries.cs` | create | catalog/detail/history projections |
| `src/Animora.Persistence.Sqlite/Queries/PurchasingQueries.cs` | create | KPI + PO list projections |
| `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` | modify | add `RebuildStockSnapshotsAsync` |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` | modify | seed 12 categories + 8 units |
| `src/Animora.Persistence.Sqlite/Seed/InventorySampleData.cs` | create | products, variants, opening receipts, suppliers, POs |
| `src/Animora.Client.Services/Inventory/InventoryQueryService.cs` | create | real `IInventoryQueryService` |
| `src/Animora.Client.Services/Inventory/InventoryMapping.cs` | create | read models ↔ Contracts DTOs |
| `src/Animora.Client.Services/Purchasing/PurchasingQueryService.cs` | create | real `IPurchasingQueryService` |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify | add 7 new types (reuse `inventory.stock.adjust`) |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap 2 DI lines |
| `src/Animora.Desktop/Pages/Inventory/InventoryPageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Pages/Purchasing/PurchasingPageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Mocks/MockInventoryQueryService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/MockPurchasingQueryService.cs` | delete | replaced |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 8 codes |
| `tests/Animora.Application.Tests/InventoryUseCaseTests.cs` | create | adjustment rules, delete refusal, receipt movements |
| `tests/Animora.Persistence.Tests/StockSnapshotIntegrityTests.cs` | create | snapshot == recomputation; rebuild |
| `tests/Animora.Persistence.Tests/PurchasingQueryTests.cs` | create | KPI correctness, statement count |

## Implementation steps
1. Add `AdjustmentReasons` and the 8 error codes.
2. Add `StockOnHandSnapshot` + configuration + `StockSnapshotUpdater`; hook the updater into
   `LocalUnitOfWork`'s save path so **every** added `StockMovement` maintains it; generate
   `012_InventorySnapshot`.
3. Add `RebuildStockSnapshotsAsync` to `LocalStoreMaintenance` and call it from A11's corrupt-DB
   rebuild path.
4. Write `InventoryQueries` and `PurchasingQueries` (snapshot-joined catalog; grouped KPIs).
5. Write the 4 inventory queries, 5 inventory commands, 1 purchasing query, 3 purchasing commands,
   with validators, permissions, audit attributes and the `CommandTypes` entries.
6. Implement `ReceiveGoodsCommand` exactly as specified, carrying `UnitCostMinor` onto each movement.
7. Seed categories + units as reference data; add `InventorySampleData` behind `SeedSampleData`.
8. Write `InventoryMapping`, `InventoryQueryService`, `PurchasingQueryService`.
9. Swap the two DI lines, add the two ViewModel lines, delete both mocks.
10. Write the three test classes.
11. Verify manually: the product grid shows correct on-hand and low-stock badges; an adjustment
    changes on-hand and appears in the movement history; receiving a PO raises on-hand by exactly the
    received quantity; deleting a product with stock is refused.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Domain: `AdjustmentReasons`.
- Application: 5 inventory queries/commands groups (9 use cases), 4 purchasing use cases, read models.
- Persistence: `StockOnHandSnapshot`, `StockSnapshotUpdater`,
  `ILocalStoreMaintenance.RebuildStockSnapshotsAsync`, migration `012_InventorySnapshot`,
  `InventoryQueries`, `PurchasingQueries`, `InventorySampleData`, seeded categories + units.
- Client services: `InventoryQueryService`, `InventoryMapping`, `PurchasingQueryService`.
- Wire: 7 `CommandTypes` entries. `ErrorCodes` += 8. No `Animora.Contracts` change.
- DI: `IInventoryQueryService` → `InventoryQueryService`; `IPurchasingQueryService` →
  `PurchasingQueryService`. **Mocks deleted: `MockInventoryQueryService.cs`,
  `MockPurchasingQueryService.cs`.**

## Rules that apply here
- **R-B23 / ARCHITECTURE.md §11.2** — on-hand is derived from movements; the snapshot is a
  maintained cache proven equal to the recomputation, never an authoritative absolute value.
- **A-RULE 002** — `StockMovement` is append-only (`AppendOnlyGuard`); product deletion is soft and
  refused while stock exists.
- **CONVENTIONS §5 / PERFORMANCE.md §6** — the catalog grid is one indexed, paged statement; the
  movement history is paged.
- **A-RULE 005** — every stock-changing action is a journaled command so D07 can settle it.
- **A-RULE 034** — purchasing KPIs are grouped aggregate reads, not row scans in the UI.
- **CONVENTIONS §10** — both mocks deleted with the swap.

## Out of scope
- Sale-driven consumption movements → C07 (posting an invoice writes `MovementKind.Sale`).
- COGS and inventory valuation reporting → C09.
- Supplier payment / payable ledger entries → C09 via `PostingRules`.
- Server-side inventory commands → D07.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `MockInventoryQueryService.cs` and `MockPurchasingQueryService.cs` do not exist; no DI line
  references them.
- A test asserts the EF model has **no** stored on-hand property on `Product`/`ProductVariant`.
- `InventoryUseCaseTests` green: an adjustment with an unknown reason returns
  `ADJUSTMENT_REASON_INVALID`; a `Damage` adjustment below zero returns `STOCK_WOULD_GO_NEGATIVE`;
  a `Count` adjustment may reach zero but not go below; deleting a product with on-hand returns
  `PRODUCT_HAS_STOCK`; receiving 7 of an ordered 5 without `AllowOverReceipt` returns
  `RECEIPT_EXCEEDS_ORDER`.
- `StockSnapshotIntegrityTests` green: after 500 randomized movements the snapshot equals the
  recomputation for every key; `RebuildStockSnapshotsAsync` repairs a deliberately corrupted row.
- `PurchasingQueryTests` green: KPI numbers match the seeded data; the snapshot query issues at most
  three `SELECT`s.
- Manual: inventory and purchasing screens work offline and survive restart.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C06 done; advance "Next phase" to `C07`; append a "Repo state" entry listing:
`StockOnHandSnapshot` + `StockSnapshotUpdater` + the "maintained cache with a proof" decision
(**C07/C09/D07 rely on it**), `RebuildStockSnapshotsAsync`, `AdjustmentReasons`, migration
`012_InventorySnapshot`, the 13 use cases, `InventorySampleData` + seeded categories/units, the 3
client services/mappings, the 2 DI swaps, **mocks deleted: `MockInventoryQueryService.cs`,
`MockPurchasingQueryService.cs`**, `CommandTypes` += 7, `ErrorCodes` += 8, the 3 test classes,
deviations.
