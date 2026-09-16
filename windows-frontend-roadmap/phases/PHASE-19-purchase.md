# Phase 19 — Purchase & Suppliers Screen

**Band:** S · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
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

## PROGRESS.md update
Mark Phase 19 done; advance "Next phase" to 20; update "Repo state."
