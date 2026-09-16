# Phase 18 — Inventory — Product Modal & Stock Adjust

**Band:** M · **Depends on:** 17 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/assets/inventory.js` — **only** lines ~448–end
  (`openProductModal`/`renderProductModal`/`renderInfoTab`/
  `renderVariantsTab`/`renderVariantCard`/`makeVariant`/
  `syncVariantsToOptions`/`bindVariantsTabInteractions`/`renderHistoryTab`,
  `openNewProductModal`, generic `ensureModal`/`openModal`/`closeModal`,
  `openStockAdjustModal`/`renderStockAdjustModal`). Skip the list-screen
  section already read in Phase 17.

## Goal
Complete the Inventory screen: the product detail modal (info/variants/
history tabs), the new-product creation modal, and the stock-adjustment
modal.

## Contracts to add/extend (`Animora.Contracts/Inventory/`)
- Extend `IInventoryQueryService` with:
  `Task<ProductDetail> GetDetailAsync(productId)`,
  `Task SaveProductInfoAsync(productId, ProductInfoUpdate)`,
  `Task<ProductVariant> AddVariantAsync(productId, NewVariantRequest)`,
  `Task<IReadOnlyList<StockMovement>> GetHistoryAsync(productId)`,
  `Task<Product> CreateProductAsync(NewProductRequest)`,
  `Task AdjustStockAsync(productId, variantId, StockAdjustRequest)`.
- `ProductDetail`, `ProductInfoUpdate`, `NewVariantRequest`,
  `StockMovement`, `NewProductRequest`, `StockAdjustRequest` DTOs.

## Mock to add
- Extend `MockInventoryQueryService` with the above, backed by the same
  in-memory product list from Phase 17; seed a few `StockMovement`
  entries per product for the history tab.

## View/ViewModel tasks
1. `Pages/Inventory/ProductModalViewModel.cs` + `.axaml`: tab strip
   (`Info|Variants|History`), each a sub-view (mirrors `renderInfoTab`/
   `renderVariantsTab`/`renderHistoryTab`); variants tab supports add/
   edit variant rows (mirrors `renderVariantCard`/`makeVariant`/
   `bindVariantsTabInteractions`). Opened via Phase 03
   `IDialogService.ShowModal` from Phase 17's `OpenProduct`.
2. `Pages/Inventory/NewProductModalViewModel.cs` + `.axaml`: creation form
   (mirrors `openNewProductModal`), opened from Phase 17's `OpenNewProduct`.
3. `Pages/Inventory/StockAdjustModalViewModel.cs` + `.axaml`: quantity
   delta + reason form (mirrors `renderStockAdjustModal`), opened from
   Phase 17's `OpenStockAdjust`.
4. Wire all three of Phase 17's placeholder commands to real modal
   navigation.

## Out of scope
- None — this phase completes the Inventory screen entirely.

## Acceptance criteria
- App builds/runs; opening a product shows the modal with working Info/
  Variants/History tabs; adding a variant updates the list; creating a
  new product adds it to Phase 17's list; adjusting stock updates the
  displayed quantity and appends a history entry.

## PROGRESS.md update
Mark Phase 18 done; advance "Next phase" to 19; update "Repo state."
