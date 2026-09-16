# Phase 21 — Sale/POS — Catalog & Payment Modals

**Band:** M · **Depends on:** 20 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `sale.css` (catalog overlay + filters/recent, payment/refund/custom-item/confirm modals, settlement success).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/assets/sale-page.js` — **only** lines ~855–1483 (`doPayment`/
  `doVoid`/`doRefund`/`openRefundModal`, `openConfirm`,
  `showSettlementSuccess`, `openCatalog`/`closeCatalog`/`catalogAllItems`/
  `catalogFiltered`/`highlight`/`renderCatFilters`/`renderCatRecent`/
  `renderCatalog`/`updateHl`/`addToInvoice`, `openCustomItemModal`/
  `addCustomLine`, `bindCatalogGlobal`, `openPicker`/`selectOwner`/
  `selectGuest`). Skip everything already read in Phase 20.

## Goal
Complete the Sale/POS screen: the product/service catalog picker (add
items to the current invoice), the payment modal (with success
confirmation), void/refund flows, the custom-line-item modal, and the
owner/guest picker used when starting a blank sale.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add/extend (`Animora.Contracts/Sales/`)
- Extend `ISalesQueryService` with:
  `Task<IReadOnlyList<CatalogItem>> GetCatalogAsync()`,
  `Task AddLineAsync(invoiceId, CatalogItem, int qty)`,
  `Task AddCustomLineAsync(invoiceId, CustomLineRequest)`,
  `Task<PaymentResult> TakePaymentAsync(invoiceId, PaymentRequest)`,
  `Task VoidAsync(invoiceId)`,
  `Task<RefundResult> RefundAsync(invoiceId, RefundRequest)`,
  `Task<OwnerPickResult> PickOwnerAsync(string query)`.
- `CatalogItem`, `CustomLineRequest`, `PaymentRequest`, `PaymentResult`,
  `RefundRequest`, `RefundResult`, `OwnerPickResult` DTOs.

## Mock to add
- Extend `MockSalesQueryService` with the above; a 10–15 item catalog
  spanning a couple of categories (mirrors `catalogAllItems`); recent
  items list; a small owner directory for the picker (mirrors
  `selectOwner`/`selectGuest`).

## View/ViewModel tasks
1. `Pages/Sales/CatalogModalViewModel.cs` + `.axaml`: search/filter chips
   (mirrors `renderCatFilters`), recent-items strip (mirrors
   `renderCatRecent`), full catalog grid with highlight-on-search
   (mirrors `renderCatalog`/`highlight`/`updateHl`), add-to-invoice action
   (mirrors `addToInvoice`). Opened via Phase 03 `IDialogService.ShowModal`
   from Phase 20's "add item" placeholder.
2. `Pages/Sales/CustomItemModalViewModel.cs` + `.axaml`: title/price/qty
   form (mirrors `openCustomItemModal`/`addCustomLine`).
3. `Pages/Sales/PaymentModalViewModel.cs` + `.axaml`: payment method +
   amount form, confirmation dialog (mirrors `openConfirm` pattern reused
   generically — consider making a small reusable `ConfirmDialogViewModel`
   in `Pages/Sales/Shared` if other flows need the same yes/no pattern),
   success screen (mirrors `showSettlementSuccess`). Wired to Phase 20's
   "take payment" placeholder.
4. `Pages/Sales/RefundModalViewModel.cs` + `.axaml`: refund amount/reason
   form (mirrors `openRefundModal`).
5. `Pages/Sales/OwnerPickerModalViewModel.cs` + `.axaml`: search + pick
   existing owner, or "guest sale" option (mirrors `openPicker`/
   `selectOwner`/`selectGuest`) — wired to Phase 20's `NewBlankInvoice`
   flow when it needs an owner/guest before creating the blank invoice.
6. Wire Void as a direct confirm-then-call command (mirrors `doVoid`)
   from the invoice panel's existing action area (Phase 20).

## Out of scope
- None — this phase completes the Sale/POS screen entirely.

## Acceptance criteria
- App builds/runs; adding catalog items and custom items updates the
  invoice's line list and totals (back on Phase 20's panel); taking
  payment shows the success confirmation and updates invoice status;
  void/refund flows work and update status/history; starting a blank
  sale prompts the owner/guest picker first.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 21 done; advance "Next phase" to 22; update "Repo state."
