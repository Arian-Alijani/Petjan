# Phase 17 — Inventory — Product List & Settings

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `inventory.css` (subnav, product table rows, settings panels, header/new button).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/inventory.html` (full)
- `Animora-UI/assets/inventory-data.js` — full file (297 lines): sample
  data shape reference only.
- `Animora-UI/assets/inventory.js` — **only** lines 1–447 (toast, `render`,
  header/new button, `convertFa`, subnav render/bind, `renderViewParts`/
  `bindViewBody`, `renderProducts`/`renderProductRow`/`bindProducts`,
  `findVariant`, `renderSettings`/`bindSettings`, `emptyState`). Stop
  before `openProductModal` (line ~448) — that's Phase 18.

## Goal
Replace the Inventory placeholder with the product list screen: subnav
(categories/sections), product rows (with variant/stock display), and the
Settings sub-view (categories/units management). Opening the product
modal is stubbed to Phase 18.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/Inventory/`)
- `IInventoryQueryService` — `Task<IReadOnlyList<Product>> GetProductsAsync()`,
  `Task<IReadOnlyList<CategoryInfo>> GetCategoriesAsync()`.
- `Product` DTO (name, category, variants with stock/price — mirrors
  `renderProductRow`/`findVariant` fields).
- `CategoryInfo`, `UnitInfo` DTOs (mirrors `renderSettings`).

## Mock to add
- `MockInventoryQueryService` — 10–15 sample products across 3–4
  categories, each with 1–3 variants.

## View/ViewModel tasks
1. `Pages/Inventory/InventoryPageViewModel.cs`: `ActiveSubnav`
   (category/section filter, mirrors `renderSubnav`/`bindSubnav`),
   `FilteredProducts`, `Categories`, `Units`, commands `SetSubnav`,
   `OpenProduct(Product)` (placeholder toast → "Phase 18" for now),
   `OpenNewProduct()` (same placeholder), `OpenStockAdjust(Product)`
   (same placeholder).
2. `Pages/Inventory/InventoryPageView.axaml(.cs)`: header + "new product"
   button, subnav strip, product rows (name/category/variant chips/
   stock/price, mirrors `renderProductRow`), empty-state when a filtered
   set is empty (mirrors `emptyState`).
3. `Pages/Inventory/InventorySettingsView.axaml(.cs)`: categories/units
   management sub-view (mirrors `renderSettings`), reachable via the
   subnav's settings entry.
4. Update `Shell/NavigationMenu.cs`'s `inventory` nav item to target
   `InventoryPageViewModel`.

## Out of scope
- Product modal (info/variants/history tabs) and stock-adjust modal
  (Phase 18).

## Acceptance criteria
- App builds/runs; Inventory nav item shows subnav + product rows
  filtering correctly by subnav selection; Settings sub-view shows
  categories/units; clicking a product/new-product/stock-adjust shows the
  Phase 18 placeholder toast without error.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 17 done; advance "Next phase" to 18; update "Repo state."
