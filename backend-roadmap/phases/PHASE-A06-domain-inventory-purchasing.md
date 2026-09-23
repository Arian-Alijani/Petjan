# Phase A06 — Domain: Inventory & Purchasing

**Band:** M · **Track:** A · **Depends on:** A02 · **Satisfies:** R-B01, R-B23
**Runnable after this phase:** desktop offline yes · server required no

> **Track A testing policy (see ROADMAP §3.10).** This phase writes **no tests** and runs **no test
> suite**. It ships domain behavior only. All tests for every Track A behavior are authored and executed
> once, in **Phase A12**. The single verification here is a lightweight build (`scripts/sandbox/build.sh
> full` = 0/0), run only if you need to confirm the new code compiles.

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §6 (item 9), §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §11.2 (movement-based stock), §8.5 (inventory = movement-based, never absolute)
- `src/Animora.Domain/Primitives/*`, `src/Animora.Domain/ValueObjects/Money.cs`,
  `src/Animora.Domain/ValueObjects/Quantity.cs`, `src/Animora.Domain/Results/ErrorCodes.cs`
- `src/Animora.Contracts/Inventory/InventoryModels.cs`,
  `src/Animora.Contracts/Purchasing/PurchasingModels.cs`

## Goal
Model the product catalog and a strictly **movement-based** stock ledger where on-hand is always derived
and never stored as an absolute, plus suppliers, purchase orders and goods receipts that generate
movements.

## Design decisions (pre-made — implement, do not re-derive)
- **Folders:** `src/Animora.Domain/Inventory/`, `src/Animora.Domain/Purchasing/`.
- `Inventory/UnitOfMeasure.cs` — `sealed class UnitOfMeasure : AggregateRoot`: `Code` (e.g. `"EA"`,
  `"BOX"`, `"ML"`, `"MG"`, `"KG"`), `DisplayKey`, `int DecimalPlaces` (0–3),
  `string? BaseUnitCode`, `decimal? ConversionToBase`.
  Invariant: `BaseUnitCode` and `ConversionToBase` are both null or both set; `ConversionToBase > 0`.
- `Inventory/ProductCategory.cs` — `sealed class ProductCategory : AggregateRoot`:
  `Code`, `DisplayName`, `string? ParentCategoryId`, `int SortOrder`, `bool IsActive`.
  Invariant: a category may not be its own ancestor (checked with a supplied ancestor list —
  `CATEGORY_CYCLE`).
- `Inventory/Product.cs` — `sealed class Product : AggregateRoot`: `Sku` (unique per tenant),
  `Name`, `Brand?`, `string CategoryId`, `ProductKind Kind`, `string UnitCode`,
  `bool IsPrescriptionOnly`, `bool IsActive`, `bool TrackStock`,
  `Quantity ReorderLevel`, `Money DefaultCostPrice`, `Money DefaultSalePrice`,
  `int? ShelfLifeDays`, `string? Notes`, `IReadOnlyList<ProductVariant> Variants`.
  `enum ProductKind { Medicine, Vaccine, Food, PetShop, Consumable, Equipment, Service }`.
  **Invariant:** `Kind == Service` ⇒ `TrackStock == false` (a service has no stock — `SERVICE_NO_STOCK`).
  Methods: `AddVariant`, `UpdateVariant`, `DeactivateVariant`, `SetPrices`, `SetReorderLevel`,
  `Deactivate`. **No `StockOnHand` property exists on `Product`** — attempting to add one is a
  review-blocking defect.
- `Inventory/ProductVariant.cs` — `sealed record ProductVariant(string VariantId, string Sku,
  string AttributeName, string AttributeValue, Money CostPrice, Money SalePrice,
  Quantity ReorderLevel, string? Barcode, bool IsActive)`.
  A stock-tracked product **always** has at least one variant; a single-variant product uses
  `AttributeName = "DEFAULT"`. All movements reference a `VariantId`, never a bare `ProductId` —
  this keeps on-hand computation uniform.
- `Inventory/StockMovement.cs` — `sealed class StockMovement : SyncableEntity`
  (**append-only; no mutator, no correction-by-edit**):
  `ProductId`, `VariantId`, `MovementKind Kind`, `Quantity Quantity` (signed — see the sign rule),
  `Money UnitCost`, `string? LotNumber`, `DateTimeOffset? ExpiryUtc`,
  `DateTimeOffset OccurredUtc`, `string? SourceEntityType`, `string? SourceEntityId`,
  `string? OperationId` (the business command that produced it — B02/D07),
  `string RecordedByUserId`, `string? Reason`, `string? ReversesMovementId`.
  `enum MovementKind { Receipt, Sale, Consumption, Loss, AdjustmentIn, AdjustmentOut, TransferIn,
  TransferOut, ReturnIn, ReturnOut }`.
  **Sign rule (authoritative):** `Receipt, AdjustmentIn, TransferIn, ReturnIn` ⇒ `Quantity.Value > 0`;
  `Sale, Consumption, Loss, AdjustmentOut, TransferOut, ReturnOut` ⇒ `Quantity.Value < 0`.
  Enforced by the factory `StockMovement.Create(...)` returning `Result<StockMovement>`
  (`MOVEMENT_SIGN_INVALID`). A correction is a **new** movement with `ReversesMovementId` set.
- `Inventory/StockLevel.cs` — `sealed record StockLevel(string ProductId, string VariantId,
  Quantity OnHand, Quantity Reserved, Quantity Available, Money AverageCost,
  DateTimeOffset? EarliestExpiryUtc)`. **A projection, never a table with authority** — A10 may
  persist it as a `[LocalOnly]` cached read model that is always rebuildable from movements.
- `Inventory/StockCalculator.cs` — static, the **single** on-hand algorithm:
  - `Quantity OnHand(IReadOnlyList<StockMovement> movements)` = sum of signed quantities.
  - `Money WeightedAverageCost(IReadOnlyList<StockMovement> movements)` — moving weighted average over
    inbound movements only; outbound movements consume at the running average (the product's costing
    method for the whole system; FIFO/LIFO are not implemented and are not a future toggle without an ADR).
  - `Result EnsureSufficient(Quantity onHand, Quantity requested, bool allowNegative)` →
    `INSUFFICIENT_STOCK` when `!allowNegative && requested > onHand`.
  - `bool IsBelowReorder(Quantity onHand, Quantity reorderLevel)`.
  - `DateTimeOffset? EarliestExpiry(IReadOnlyList<StockMovement> inbound)`.
  All methods are pure, allocation-light (`for` loops, no LINQ) and unit-code-checked.
- `Purchasing/Supplier.cs` — `sealed class Supplier : AggregateRoot`: `Code`, `Name`,
  `ContactPerson?`, `Phone?`, `Email?`, `Address?`, `string? NationalId`,
  `int PaymentTermDays`, `bool IsActive`, `string? Notes`.
- `Purchasing/PurchaseOrder.cs` — `sealed class PurchaseOrder : AggregateRoot`:
  `OrderNumber` (per-tenant sequential, assigned by the Application layer), `SupplierId`,
  `PurchaseOrderState State`, `DateTimeOffset OrderedUtc`, `DateTimeOffset? ExpectedUtc`,
  `Money Subtotal`, `Money DiscountTotal`, `Money TaxTotal`, `Money GrandTotal`,
  `string CreatedByUserId`, `string? Notes`, `IReadOnlyList<PurchaseOrderLine> Lines`.
  `enum PurchaseOrderState { Draft, Submitted, PartiallyReceived, Received, Cancelled, Closed }`.
  **State machine:**
  ```
  Draft             -> Submitted | Cancelled
  Submitted         -> PartiallyReceived | Received | Cancelled
  PartiallyReceived -> Received | Closed
  Received          -> Closed
  Cancelled / Closed -> terminal
  ```
  Methods: `AddLine`, `RemoveLine`, `Submit`, `ApplyReceipt(GoodsReceipt)`, `Cancel`, `Close`.
  Invariants: lines immutable after `Submitted` (`PO_LOCKED`); totals recomputed by
  `RecalculateTotals()` after every line change and asserted equal to the line sum
  (`PO_TOTAL_MISMATCH`); `ApplyReceipt` sets `PartiallyReceived` or `Received` by comparing
  received-vs-ordered quantity per line.
- `Purchasing/PurchaseOrderLine.cs` — `sealed record PurchaseOrderLine(string LineId, string ProductId,
  string VariantId, Quantity OrderedQuantity, Quantity ReceivedQuantity, Money UnitCost,
  decimal DiscountPercent, decimal TaxPercent, string? LotNumber, DateTimeOffset? ExpiryUtc)`
  with a computed `Money LineTotal` using `Money.ApplyPercent` (A02's single rounding policy).
- `Purchasing/GoodsReceipt.cs` — `sealed class GoodsReceipt : AggregateRoot`:
  `ReceiptNumber`, `PurchaseOrderId?` (null = direct receipt without a PO), `SupplierId`,
  `DateTimeOffset ReceivedUtc`, `string ReceivedByUserId`, `string? InvoiceReference`,
  `IReadOnlyList<GoodsReceiptLine> Lines`, `GoodsReceiptState State`.
  `enum GoodsReceiptState { Draft, Posted, Reversed }`.
  `Post(IClock)` → `Posted`, **immutable afterwards** (`RECEIPT_LOCKED`), raises
  `GoodsReceiptPostedEvent` carrying the lines so the movement writer (D07/C06) creates one
  `MovementKind.Receipt` movement per line. A reversal is a new receipt with
  `ReversesReceiptId` + `ReturnOut` movements, never an edit.
- `Purchasing/GoodsReceiptLine.cs` — `sealed record GoodsReceiptLine(string LineId, string ProductId,
  string VariantId, Quantity Quantity, Money UnitCost, string? LotNumber,
  DateTimeOffset? ExpiryUtc)`.
- **Domain events** in `src/Animora.Domain/Events/InventoryEvents.cs`:
  `StockMovementRecordedEvent(MovementId, ProductId, VariantId, Kind, QuantityValue)`,
  `StockBelowReorderEvent(ProductId, VariantId, OnHandValue, ReorderLevelValue)`,
  `GoodsReceiptPostedEvent(ReceiptId, SupplierId, LineCount)`,
  `PurchaseOrderSubmittedEvent(OrderId, SupplierId, GrandTotalMinor, Currency)`.
- **New error codes:** `SERVICE_NO_STOCK`, `MOVEMENT_SIGN_INVALID`, `INSUFFICIENT_STOCK`,
  `UNIT_MISMATCH`, `CATEGORY_CYCLE`, `VARIANT_REQUIRED`, `PO_INVALID_TRANSITION`, `PO_LOCKED`,
  `PO_TOTAL_MISMATCH`, `RECEIPT_LOCKED`, `PRODUCT_SKU_DUPLICATE`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Inventory/UnitOfMeasure.cs` | create | unit entity + conversion invariant |
| `src/Animora.Domain/Inventory/ProductCategory.cs` | create | category entity + cycle guard |
| `src/Animora.Domain/Inventory/Product.cs` | create | product aggregate, no stored on-hand |
| `src/Animora.Domain/Inventory/ProductVariant.cs` | create | variant value object |
| `src/Animora.Domain/Inventory/StockMovement.cs` | create | append-only signed movement + factory |
| `src/Animora.Domain/Inventory/StockLevel.cs` | create | derived stock projection record |
| `src/Animora.Domain/Inventory/StockCalculator.cs` | create | on-hand, avg cost, sufficiency, expiry |
| `src/Animora.Domain/Purchasing/Supplier.cs` | create | supplier entity |
| `src/Animora.Domain/Purchasing/PurchaseOrder.cs` | create | PO aggregate + state machine + totals |
| `src/Animora.Domain/Purchasing/PurchaseOrderLine.cs` | create | PO line + computed total |
| `src/Animora.Domain/Purchasing/GoodsReceipt.cs` | create | receipt aggregate + post/immutability |
| `src/Animora.Domain/Purchasing/GoodsReceiptLine.cs` | create | receipt line |
| `src/Animora.Domain/Events/InventoryEvents.cs` | create | 4 inventory/purchasing events |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append the 11 codes |

**No test files in this phase.** Tests for this phase's behavior are authored in **A12**; the
"Test coverage owed to A12" section below records exactly what A12 must cover.

## Implementation steps
1. Create `Inventory/` and add `UnitOfMeasure`, `ProductCategory` (cycle guard takes the ancestor list).
2. Add `ProductVariant`, then `Product` with the service/stock invariant and the variant methods.
   Add an XML-doc note: "on-hand is never stored here — see `StockCalculator`" (R-B23).
3. Implement `StockMovement` with the `Create` factory enforcing the sign rule per kind, and zero
   mutators.
4. Implement `StockCalculator` (pure, `for` loops) and `StockLevel`.
5. Create `Purchasing/` and add `Supplier`, `PurchaseOrderLine`, `PurchaseOrder` (transition helper +
   `RecalculateTotals` + `ApplyReceipt`), `GoodsReceiptLine`, `GoodsReceipt`.
6. Add `Events/InventoryEvents.cs` and append the 11 error codes.
7. **No tests here.** Record the coverage owed to A12 (below).
8. Lightweight build check only, if needed: `build.sh full` = 0/0. Do **not** run `test.sh`.
   `run.sh` = `SMOKE RUN OK`.

## Test coverage owed to A12 (author + run in A12, not here)
- `StockCalculatorTests`: a 5-movement sequence whose on-hand matches the signed sum;
  weighted-average cost after two receipts at different costs plus one sale; `EnsureSufficient`
  allowing negative when `allowNegative = true`; a unit-code mismatch returning `UNIT_MISMATCH`.
- `StockMovementFactoryTests`: iterate **all 10** `MovementKind` values, asserting the required sign
  (a valid and an invalid sign each) and reversal linkage.
- `ProductInvariantTests`: service-no-stock, variant requirement, price setters.
- `PurchaseOrderTests`: transitions, line lock after `Submit` (`PO_LOCKED`), total match
  (`PO_TOTAL_MISMATCH`), partial receipt → `PartiallyReceived`.

## Contracts / schema added
- Entities: `UnitOfMeasure`, `ProductCategory`, `Product`, `StockMovement`, `Supplier`,
  `PurchaseOrder`, `GoodsReceipt`.
- Value objects: `ProductVariant`, `StockLevel`, `PurchaseOrderLine`, `GoodsReceiptLine`.
- Enums: `ProductKind`, `MovementKind`, `PurchaseOrderState`, `GoodsReceiptState`.
- Statics: `StockCalculator`. Events: 4 new records.
- `ErrorCodes` += 11. No migration, no `Animora.Contracts` change. **No test files** (owed to A12).

## Rules that apply here
- **ARCHITECTURE.md §11.2 / R-B23** — on-hand is derived from movements. `Product` has no stock
  property; `StockLevel` is explicitly documented as a rebuildable projection.
- **ARCHITECTURE.md §8.5** — inventory conflicts are resolved movement-additively, never by overwriting
  an absolute quantity; `StockMovement` has no mutator.
- **A-RULE 002** — a posted `GoodsReceipt` is immutable; corrections are reversing receipts.
- **CONVENTIONS §4** — `Money.ApplyPercent` is the only discount/tax rounding path (no inline rounding).
- **PERFORMANCE.md §2** — `StockCalculator` uses indexed `for` loops, no LINQ, no temporary collections.
- **CONVENTIONS §2** — `Animora.Domain` gains no reference.

## Out of scope
- **All tests for this phase's behavior → A12** (this phase writes and runs none).
- Sales invoices consuming stock → A07 (domain) / C07 (wiring) / D07 (server settlement).
- Ledger postings from receipts → A08. EF mappings, indexes, `StockLevel` caching table → A09/A10.
- Supplier payables and cheque linkage → A08. Reorder alerts surfaced in the UI → C06.
- FIFO/LIFO costing → not implemented; requires an ADR.

## Acceptance criteria
- Build (lightweight, only if run): `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **No test suite is run in this phase** (`test.sh` is A12's; do not invoke it here).
- `grep -n "OnHand" src/Animora.Domain/Inventory/Product.cs` returns nothing (no stored on-hand).
- The "Test coverage owed to A12" section is present and names every invariant A12 must assert.
- `Animora.Domain.csproj` still has zero references.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark A06 done; advance "Next phase" to `A07`; append a "Repo state" entry listing: the 13 new files, the
entities/enums/events added, the movement sign rule + weighted-average costing decision,
`ErrorCodes` += 11, **no tests written (owed to A12)**, no DI change, no mock deleted, no migration,
deviations.
