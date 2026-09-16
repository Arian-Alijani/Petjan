# Phase 21 — Sale/POS — Catalog & Payment Modals

**Band:** M · **Depends on:** 20 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
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

## PROGRESS.md update
Mark Phase 21 done; advance "Next phase" to 22; update "Repo state."
