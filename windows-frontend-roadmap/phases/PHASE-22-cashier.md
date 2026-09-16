# Phase 22 — Cashier Screen (Invoice & Payment Management)

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
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

## PROGRESS.md update
Mark Phase 22 done; advance "Next phase" to 23; record the
Sales-vs-Cashier refund-contract decision; update "Repo state."
