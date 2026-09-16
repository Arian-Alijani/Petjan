# Phase 22 — Cashier Screen (Invoice & Payment Management)

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` (status-tab filter, stats, invoice list, detail panel with lines/totals/payment/history/actions are styled in the shared file).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/cashier.html` (full)
- `Animora-UI/assets/cashier.js` — full file (524 lines): status-tab
  filter, stats, invoice list, detail panel (lines/totals/payment panel/
  payment history/actions), pay/void/refund/print actions.

## Goal
Replace the Cashier placeholder with the invoice management screen:
status-tab filtered invoice list + a detail panel showing lines/totals/
payment history and pay/void/refund/print actions. Note: this screen is
distinct from Sale/POS (Phases 20/21) — it is the cash-register/settlement
view, not the item-adding workspace; do not merge them or skip this phase
assuming Phase 20/21 covers it (see `ROADMAP.md` for the Phase 11
hand-off clarification).

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/Cashier/`)
- `ICashierService` — `Task<IReadOnlyList<CashierInvoice>>
  GetInvoicesAsync()`, `Task<CashierInvoiceDetail> GetDetailAsync(invoiceId)`,
  `Task PayAsync(invoiceId, PayRequest)`, `Task VoidAsync(invoiceId)`,
  `Task<RefundResult> RefundAsync(invoiceId, RefundRequest)`.
- `CashierInvoice`, `CashierInvoiceDetail`, `PayRequest` DTOs — mirror
  `renderList`/`renderDetail`/`renderLines`/`renderTotals`/
  `renderPaymentPanel`/`renderPaymentHistory` fields. Reuse
  `RefundRequest`/`RefundResult` from `Animora.Contracts/Sales` if the
  shape is identical — otherwise define a local equivalent to respect the
  Cashier/Sales bounded-context boundary (`ARCHITECTURE.md` §6); prefer a
  local copy over a cross-context reference unless a phase author judges
  the two are genuinely the same domain concept — record the decision in
  `PROGRESS.md`.

## Mock to add
- `MockCashierService` — 6–10 sample invoices across statuses (mirrors
  `tabMatches`'s vocabulary), each with 1–4 lines and 0–2 payment-history
  entries.

## View/ViewModel tasks
1. `Pages/Cashier/CashierPageViewModel.cs`: `ActiveTab` (status filter),
   stat properties (mirrors `renderStats`), `Invoices` (filtered,
   mirrors `renderList`), `SelectedInvoice` + its lines/totals/payment
   history, commands `SelectInvoice`, `Pay`, `Void`, `Refund`, `Print`.
2. `Pages/Cashier/CashierPageView.axaml(.cs)`: stat row, tab strip,
   invoice list (left), detail panel (right: lines table via Phase 03
   `DataTable`, totals block, payment panel/history, action buttons —
   mirrors `renderDetail`/`renderActions`).
3. Pay/Refund open small inline forms or Phase 03 modals (per the
   reference's own inline-vs-modal choice — `doPay`/`doRefund` appear to
   act directly on visible panel state rather than opening a separate
   modal; mirror that: keep payment/refund as inline panel interactions,
   not new modal screens, unless the reference markup shows otherwise).
   `Print` may be a no-op/toast stub (no real printing pipeline in scope).
4. Update `Shell/NavigationMenu.cs`'s cashier-related nav target (reached
   via Dashboard quick actions per `assets/dashboard.js`, same pattern as
   Reception/Purchase) to `CashierPageViewModel`.

## Out of scope
- Any Sale/POS catalog/item-adding behavior — that stays exclusively in
  Phases 20/21.

## Acceptance criteria
- App builds/runs; Cashier screen shows filtered invoice list + detail
  panel; pay/void/refund update the invoice's status/totals/history
  correctly and reflect back in the list.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 22 done; advance "Next phase" to 23; record the
Sales-vs-Cashier refund-contract decision; update "Repo state."
