# Phase 17 — Inventory — Product List & Settings

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
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

## PROGRESS.md update
Mark Phase 17 done; advance "Next phase" to 18; update "Repo state."
