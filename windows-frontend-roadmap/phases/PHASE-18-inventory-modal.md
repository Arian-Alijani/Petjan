# Phase 18 — Inventory — Product Modal & Stock Adjust

**Band:** M · **Depends on:** 17 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `inventory.css` (product modal + info/variants/history tabs, variant cards, stock-adjust modal).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
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

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

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
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 18 done; advance "Next phase" to 19; update "Repo state."
