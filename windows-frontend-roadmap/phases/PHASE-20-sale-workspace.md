# Phase 20 — Sale/POS — Invoice Workspace

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/sale.html` (full)
- `Animora-UI/assets/sale-page.js` — **only** lines 1–~452 and ~608–855
  and ~1483–end (`readyEncounters`/`invoicesAll`/`matchQuery`/
  `matchEncQuery`, `renderSeg`/`renderItemInv`/`renderItemEnc`,
  `renderSidebar`, `selectInvoice`, `buildFromEncounter`,
  `newBlankInvoice`, `renderInvoice`/`renderEmpty`, then
  `renderTerminalBanner`/`renderPayCTA`/`renderLine`/`renderHistory`,
  `bindInvoice` (structure only — payment/void/refund bodies are Phase
  21), and the final `renderKPIs`). This gives the sidebar (ready
  encounters + invoice list, searchable) and the invoice content
  read/line-display, without the catalog/payment/refund modal internals.

## Goal
Replace the Sale/POS placeholder with the two-pane invoice workspace:
left sidebar (ready-encounters + all-invoices, searchable/segmented), and
the main invoice panel (line items read-only display, totals, status
banner) — enough to select/create/view an invoice. Adding items via
catalog and taking payment are Phase 21.

## Contracts to add (`Animora.Contracts/Sales/`)
- `ISalesQueryService` — `Task<IReadOnlyList<ReadyEncounterRef>>
  GetReadyEncountersAsync()`, `Task<IReadOnlyList<InvoiceSummary>>
  GetInvoicesAsync()`, `Task<InvoiceDetail> GetInvoiceAsync(invoiceId)`,
  `Task<InvoiceDetail> CreateFromEncounterAsync(encounterId)`,
  `Task<InvoiceDetail> CreateBlankAsync(InvoiceKind kind)`.
- `ReadyEncounterRef`, `InvoiceSummary`, `InvoiceDetail`,
  `InvoiceLine` DTOs (mirror `renderItemEnc`/`renderItemInv`/
  `renderLine`/`renderInvoiceContent`'s fields — patient/owner or guest,
  lines with qty/price, subtotal/total, status).

## Mock to add
- `MockSalesQueryService` — 3–5 ready encounters (can reuse Phase 11's
  `EncounterSummary` shape conceptually, but keep this contract
  self-contained per bounded-context rules — `ARCHITECTURE.md` §6), 5–8
  sample invoices in varying statuses.

## View/ViewModel tasks
1. `Pages/Sales/SalePageViewModel.cs`: `SearchText`, `Segment`
   (ready-encounters vs. all-invoices, mirrors `renderSeg`), filtered
   lists (mirrors `matchQuery`/`matchEncQuery`), `SelectedInvoice`,
   commands `SelectInvoice`, `BuildFromEncounter`, `NewBlankInvoice`
   (guest sale).
2. `Pages/Sales/SalePageView.axaml(.cs)`: two-column layout — sidebar
   (search box, segment toggle, item list, mirrors `renderSidebar`) and
   main invoice panel (mirrors `renderInvoiceContent`'s read-only parts:
   header, line list via `renderLine`, totals, status banner via
   `renderTerminalBanner`, and history via `renderHistory`), plus a
   compact KPI strip (mirrors `renderKPIs`) above the panel or in the
   page header (verify placement against `sale.html`'s `.sw-mini-kpis`
   slot).
3. An "add item" button and a "take payment" CTA are visible but, for
   this phase, wired to toast placeholders ("Catalog — Phase 21" /
   "Payment — Phase 21"); Phase 21 replaces both.
4. Update `Shell/NavigationMenu.cs`'s `sale` nav item to target
   `SalePageViewModel`.

## Out of scope
- Catalog picker, payment/refund/void modals, custom-item modal,
  owner/guest picker modal (Phase 21).

## Acceptance criteria
- App builds/runs; Sale nav item shows the sidebar + invoice panel;
  searching/segmenting the sidebar filters correctly; selecting a ready
  encounter builds a new invoice from it and displays it; selecting an
  existing invoice displays its lines/totals/status; add-item/take-payment
  buttons show the Phase 21 placeholder without error.

## PROGRESS.md update
Mark Phase 20 done; advance "Next phase" to 21; update "Repo state."
