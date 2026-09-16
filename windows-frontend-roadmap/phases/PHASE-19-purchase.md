# Phase 19 — Purchase & Suppliers Screen

**Band:** S · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` (this screen is static markup with no screen-specific CSS — all boxes/KPIs/tables are styled in the shared file).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/purchase.html` (full — 111 lines). Note: unlike other
  screens, this reference page has **no dedicated `assets/*.js` file** —
  it only loads `icons.js`/`layout.js` and is otherwise static markup
  (KPI stat row + a purchase-orders/suppliers content area). Treat the
  HTML's own markup as the complete behavioral reference for this phase.

## Goal
Replace the Purchase placeholder with a screen matching `purchase.html`:
KPI stat row (active orders, this-month purchases, supplier debt — mirror
the three `.stat` cards) and a purchase-orders list/table with a "new
purchase invoice" action. Since the reference has no interactive JS
behavior beyond static markup, this phase produces a **correctly-styled,
data-bound-to-mock static view** — no complex interaction is expected or
required beyond what the static HTML shows.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/Purchasing/`)
- `IPurchasingQueryService` — `Task<PurchasingSnapshot> GetSnapshotAsync()`.
- `PurchasingSnapshot` DTO — the 3 KPI values + a list of
  `PurchaseOrderSummary` (supplier name, order date, amount, status —
  infer the table's columns from `purchase.html`'s markup structure
  beyond line 40, which this phase's reading must include in full).

## Mock to add
- `MockPurchasingQueryService` — fixed KPI numbers matching the
  reference's sample values (۶ active orders, ۱۲۸م this month, ۳۲م
  supplier debt) + 5–8 sample purchase orders.

## View/ViewModel tasks
1. `Pages/Purchasing/PurchasingPageViewModel.cs`: loads
   `PurchasingSnapshot`, exposes KPI properties + `Orders` collection,
   `NewPurchaseInvoice` command (toast-only stub — no deeper flow exists
   in the reference to mirror).
2. `Pages/Purchasing/PurchasingPageView.axaml(.cs)`: KPI stat row (Phase
   03 `StatCard` ×3, with delta chips matching the reference's ۱۲٪↑/۴٪↓/
   ۹٪↓ pattern), orders table/list, "new purchase invoice" button.
3. Update `Shell/NavigationMenu.cs`'s nav item pointing at
   `purchase.html`'s equivalent (dashboard quick-action "خرید و تأمین" —
   confirm whether a sidebar entry exists; if not, this is reached only
   via the Dashboard quick action per `assets/dashboard.js`'s `href:`
   list, same pattern as Phase 07's Reception) to target
   `PurchasingPageViewModel`.

## Out of scope
- Any deeper purchase-order detail/edit flow — none exists in the
  reference to mirror; do not invent one.

## Acceptance criteria
- App builds/runs; reaching this screen (via Dashboard quick action or
  sidebar, whichever applies) shows the 3 KPI cards and the orders list
  matching `purchase.html`'s visual structure.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 19 done; advance "Next phase" to 20; update "Repo state."
