# Boarding / Sales / Cashier audit — 2026-10-03

## Progress checklist (updated after every task)

- [x] X01 — DONE: WalkInBoarder find-or-create owner/pet + PaymentMode persisted (migration 024)
- [x] X02 — DONE: try/catch + danger toast on create/checkout/transfer/add/remove
- [x] X03 — DONE: shared ClinicDayWindow used by Sales KPIs and Cashier stats
- [x] X04 — DONE: removed the no-op recalculate button/command/seam (totals are domain-authoritative)
- [x] X05 — DONE: Cashier PayAsync clamps to due, returns Applied
- [x] X06 — DONE: Applied amount carried on Sales/Cashier refund+pay results; toasts show ledger amount (Cashier refund also clamped)
- [x] X07 — DONE: success screen refreshes _paid from the post-payment invoice
- [x] X08 — DONE: BoardingHistoryCriteria applies range/kinds/species/search in SQL; totals over filtered set
- [x] X09 — DONE: Sales/Cashier mutation + load commands wrapped; typed failures surface as danger toasts, modals stay open
- [x] X10 — DONE: InvoiceViewer void/delete converted to awaitable Task handlers guarded by GuardAsync (also guards edit/return/discount/insurance); no async void left in the three screens
- [x] X11 — DONE: Boarding settings: removed fake rate/toggle/add/edit stubs and hardcoded constants; tab now shows live rooms+services only
- [x] X12 — DONE: removed the (نمونه طراحی) suffix from every real boarding mutation toast
- [x] X13 — DONE: Real print preview (invoice, payment receipt, day report, stay bill) via IPrintService + shared PrintDocumentBuilder; Cashier export = real CSV save; no false success toasts
- [x] X14 — DONE: BoardingStayClassification is the single overdue/leaving/current derivation feeding card chrome, status pill, detail modal and tab predicate (also fixes always-zero overdue day count)
- [x] X15 — DONE: CreateBlankAsync no longer takes an ignored kind parameter (blank document is always a counter sale)
- [x] X16 — DONE: New stay requires a selected room (literal A-1 fallback removed); server-side CreateBoardingStayCommand already rejects occupied/unavailable rooms and the failure is toasted
- [x] X17 — DONE: Sales TakePaymentAsync re-reads the posted invoice and caps against the post-time due (Cashier already read due after post)
- [x] X18 — DONE: Sales ready list and invoice sidebar are virtualized ListBoxes (same pattern as Cashier)
- [x] X19 — DONE: all rebuilt sidebar/line/payment/catalog/owner/pet/day-cell/item/stat/tab/option lists are RangeObservableCollection + ReplaceAll (single Reset)
- [x] X20 — DONE: ReloadStatsAsync always re-renders the sidebar list (ReloadAsync now delegates to it)
- [x] X21 — DONE: Sales sidebar normalizes the query once per render and skips the debounced rebuild when the normalized query is unchanged; Cashier/Boarding already hoist the query
- [x] X22 — DONE: Owner picker search is debounced (200 ms) and guarded by a generation token so stale responses cannot overwrite newer rows
- [x] X23 — DONE: Catalog add promotes the item in the recent strip locally; no full catalog refetch/row rebuild per add
- [x] X24 — DONE: Highlight is located on a length-preserving PersianText.Fold copy (same folding as the filter) and sliced from the original title
- [x] X25 — DONE: removed the dead key parameter of Resolve; the three design-token brushes are cached statics
- [x] X26 — DONE: line Gross/Subtotal now come from InvoiceLineReadModel (domain Money.ApplyPercent) through the Sales/Cashier contracts; UI/print recomputation removed
- [x] X27 — DONE: Edit/remove line carry the rendered LineId end-to-end (contracts, Sales+Cashier services, VMs); a concurrent change fails with INVOICE_LINE_INDEX_STALE and a clear toast instead of hitting another line
- [x] X28 — DONE: Sales KPIs reuse the page's loaded invoices + ready queue; only the settled-today query is issued by GetKpisAsync
- [x] X29 — DONE: Cashier list is queried per tab + search with SQL ordering and paging (50/page, «نمایش بیشتر»); no more whole-table load on every mutation
- [x] X30 — DONE: Modal initial loads run through ModalLoadGuard: typed failure -> danger toast + modal closes; not-found cases toast instead of closing silently
- [x] X31 — DONE: OwnerPickResult reduced to the fields that affect the outcome (IsGuest, OwnerId); guest pick documented as keeping the draft's guest customer
- [x] X32 — DONE: removed the empty CheckoutRequest payload; CheckoutAsync(stayId) matches the real capability
- [x] X33 — DONE: transfer hint guillemets balanced («pet» — «owner»)
- [x] X34 — DONE: each Boarding read operation (stays, detail, history) resolves dispatcher + clock + zone + services through ONE DI scope instead of a scope per read
- [x] X35 — DONE: RefreshCards short-circuits when data version + (tab, species, normalized query) are unchanged; query normalized once per filter; re-selecting the current tab is a no-op
- [x] X36 — DONE: day cells index consumed items by date once (O(nights + items), no per-day allocation)
- [x] X37 — DONE: Add/RemoveItem return the updated StayDetail which the modal applies locally (one detail round trip); page refresh scoped to the stay list
- [x] X38 — DONE: every mock-era comment in the three screens (VMs, View code-behind, Contracts) now names the real service
- [x] X39 — DONE: no double/UI money math left for line subtotals (shared with X26; print helpers deleted)
- [ ] X40
- [ ] X41

## Scope and method

- Screens: **Boarding (پانسیون)**, **Sales / POS (فروش و صندوق)**, **Cashier (صندوق و مالی)**. Static source review only — **no build, tests, app run, benchmarks, or DB execution** (per task). Findings describe code behavior; timing, race frequency, and rendered layout are unmeasured.
- Layers reviewed per screen: `Pages/<Area>/*` (View + ViewModel + item/presentation VMs + modals) → `Animora.Contracts/<Area>/*` → `Animora.Client.Services/<Area>/*`. Application/Domain/Persistence read only where a seam call forced it.
- Standard applied: `AGENTS.md` (MVVM seam rule), `PERFORMANCE.md §1–§17`, the "no sample/mock branching in `Pages/**`" rule. Not defects: absent server/Datasync (Tracks D–E), real printing/Excel subsystems (no such subsystem exists yet) — **but a success toast that claims an action happened when it did not IS a defect**.
- Severity: **P0** = data corruption / money wrong / crash on a primary flow. **P1** = lost/ignored user input, broken primary flow, access/state wrong. **P2** = incorrect secondary behavior, perf, resilience, consistency, maintainability.
- **Total 41 findings: 3 P0, 14 P1, 24 P2.**

## Path aliases

| Alias | Path |
|---|---|
| B.VM | `src/Animora.Desktop/Pages/Boarding/BoardingPageViewModel.cs` |
| B.ITEM | `src/Animora.Desktop/Pages/Boarding/BoardingItemViewModels.cs` |
| B.PRES | `src/Animora.Desktop/Pages/Boarding/BoardingPresentation.cs` |
| B.NEW | `src/Animora.Desktop/Pages/Boarding/NewStayModalViewModel.cs` |
| B.CHK | `src/Animora.Desktop/Pages/Boarding/CheckoutModalViewModel.cs` |
| B.TR | `src/Animora.Desktop/Pages/Boarding/TransferModalViewModel.cs` |
| B.DET | `src/Animora.Desktop/Pages/Boarding/StayDetailModalViewModel.cs` |
| B.HIST | `src/Animora.Desktop/Pages/Boarding/BoardingHistoryViewModel.cs` |
| B.VIEW | `src/Animora.Desktop/Pages/Boarding/BoardingPageView.axaml` |
| B.SVC | `src/Animora.Client.Services/Boarding/BoardingQueryService.cs` |
| B.CONTR | `src/Animora.Contracts/Boarding/BoardingDetail.cs` |
| S.VM | `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs` |
| S.PAY | `src/Animora.Desktop/Pages/Sales/PaymentModalViewModel.cs` |
| S.REF | `src/Animora.Desktop/Pages/Sales/RefundModalViewModel.cs` |
| S.CAT | `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs` |
| S.OWN | `src/Animora.Desktop/Pages/Sales/OwnerPickerModalViewModel.cs` |
| S.VIEW | `src/Animora.Desktop/Pages/Sales/SalePageView.axaml` |
| S.IVVM | `src/Animora.Desktop/Pages/Sales/InvoiceViewerViewModel.cs` |
| S.SVC | `src/Animora.Client.Services/Sales/SalesQueryService.cs` |
| C.VM | `src/Animora.Desktop/Pages/Cashier/CashierPageViewModel.cs` |
| C.ITEM | `src/Animora.Desktop/Pages/Cashier/CashierItemViewModels.cs` |
| C.VIEW | `src/Animora.Desktop/Pages/Cashier/CashierPageView.axaml` |
| C.SVC | `src/Animora.Client.Services/Cashier/CashierService.cs` |
| PERF | `PERFORMANCE.md` |

---

## P0 — data/money/crash

### ✅ X01 — P0 · Silent data loss · New-stay drops 6 of its fields; manual entry cannot work
- **Evidence:** B.CONTR:91–103 declares `NewStayRequest(PetName, Species, Breed, OwnerName, OwnerPhone, PatientId, Room, CheckIn, CheckOut, DailyRate, PaymentMode, Note)`. B.NEW:283–287 fills all of them. B.SVC:79–84 `CreateStayAsync` dispatches `CreateBoardingStayCommand(request.PatientId, request.Room, request.CheckIn, request.CheckOut, request.DailyRate, request.Note)` — `PetName, Species, Breed, OwnerName, OwnerPhone, PaymentMode` are never passed.
- **Failure:** the «پذیرش دستی» (manual) path (B.NEW:249–263) collects pet/owner identity for a walk-in with no `PatientId`; all of it is discarded, so the stay is created with empty identity or the command fails on a null patient. `PaymentMode` chosen in the modal (B.NEW:280) is ignored — every stay posts with the domain default. The toast still reports success (see X02).
- **Fix:** either extend `CreateBoardingStayCommand` + the Boarding domain to accept the manual-entry identity + payment mode, or remove the manual tab and make `PatientId` required at the picker. Do not accept fields the seam silently drops.
- **Acceptance:** a manual walk-in persists pet/owner/species/breed/phone; the selected payment mode round-trips on reopen.

### ✅ X02 — P0 · False confirmation · Boarding writes claim success unconditionally (no error handling)
- **Evidence:** B.NEW:287–292 `await CreateStayAsync(...)` then unconditionally `Close()` + success toast; no try/catch. Same shape: B.CHK:50–55 (checkout), B.TR:73–78 (transfer), B.DET:311–336 (add/remove item). B.SVC `QueryAsync`/`SendAsync` (B.SVC:15–35) **throw `ServiceException` on failure**.
- **Failure:** any authorization/validation/capacity failure throws out of an `async` `RelayCommand` onto the UI dispatcher → unhandled exception (crash/stutter) *and* the toast fires only on the success path, so a partial failure can both crash and have already shown "ثبت شد". For checkout/transfer the user believes the room is freed / invoice sent when it was not.
- **Fix:** wrap each mutation in try/catch, surface the typed error as a danger toast (the pattern already exists at S.VM:257–277 `BuildFromEncounterAsync`), and only close + toast success after the awaited call returns without error.
- **Acceptance:** a forced service failure shows a danger toast, keeps the modal open, and does not crash.

### ✅ X03 — P0 · Money/time correctness · Cashier "today" stats ignore the clinic time zone
- **Evidence:** C.SVC:40–43 `GetStatsAsync` computes the day window from `DateTimeOffset.UtcNow.UtcDate` directly. The Sales equivalent (S.SVC:105–132 + `ClinicDayWindow` S.SVC:412–417) correctly converts through `IClinicTimeZoneProvider`.
- **Failure:** "دریافتی" (collected today) and "مغایرت صندوق" are bucketed on UTC midnight, not clinic-local midnight. For any non-UTC clinic (Iran is UTC+3:30) payments in the 3.5-hour boundary land in the wrong day; the drawer-variance figure is wrong every evening. Two screens reporting the same day disagree.
- **Fix:** resolve `IClock` + `IClinicTimeZoneProvider` in C.SVC and build the window with the same helper Sales uses; share one `ClinicDayWindow`.
- **Acceptance:** Cashier "today" window equals Sales "today" window for the same clinic zone, verified across the local-midnight boundary.

---

## P1 — lost input / broken flow / wrong state

### ✅ X04 — P1 · Dead action · `RecalculateAsync` recomputes nothing but reports success
- **Evidence:** C.SVC:150 `RecalculateAsync(id) => RequireDetailAsync(id)` — a plain re-read, no command. C.VM:428–439 awaits it and toasts «فاکتور باز محاسبه شد».
- **Failure:** the «بازمحاسبه» button promises a recalculation; it only refetches the already-current detail. If totals were ever stale this does not fix them; the success claim is false.
- **Fix:** either dispatch a real recompute command, or remove the button and its toast. If totals are always server-authoritative, document that and drop the control.
- **Acceptance:** either a measurable recompute occurs, or the affordance no longer exists.

### ✅ X05 — P1 · Overpayment · Cashier `PayAsync` does not cap amount to due
- **Evidence:** C.VM:449–457 sends the raw parsed amount (or `Detail.Due` when blank). C.SVC:79–98 `PayAsync` forwards `Money.Irt(request.Amount)` to `RegisterPaymentCommand` with **no `Math.Min(amount, due)`**. Contrast S.SVC:286–291 which caps `amount = Math.Min(request.Amount, due)` and rejects `<= 0` with `PAYMENT_EXCEEDS_DUE`.
- **Failure:** a cashier can register a payment larger than the balance; whether it over-pays or throws depends solely on domain guards. Behavior diverges from the Sales payment path for the same operation.
- **Fix:** cap and validate in C.SVC exactly as S.SVC does (clamp to `[1, due]`, return a typed error otherwise); or centralize payment in one service both pages call.
- **Acceptance:** an amount above due is clamped (or rejected) identically on both screens.

### ✅ X06 — P1 · Wrong amount shown · Refund success toast reports the requested amount, not the applied amount
- **Evidence:** S.REF:65–77 toasts `SalePresentation.Money(amount)` using the user-entered value, but S.SVC:314–336 `RefundAsync` applies `Math.Min(request.Amount, current.PaidMinor)`. C.VM:494–517 has the same shape (toasts `amount`, C.SVC:106–110 does not clamp at all → see X05).
- **Failure:** entering more than the paid amount shows "X عودت شد" for X while the ledger credited less (Sales) or an unclamped value (Cashier). The receipt the user reads is wrong.
- **Fix:** display the amount returned by the result (`result.Invoice` paid delta), not the input; have the result carry the actually-applied figure.
- **Acceptance:** toast amount equals the ledger entry for a deliberate over-refund attempt.

### ✅ X07 — P1 · Stale balance · Refund-from-success uses the pre-payment paid value
- **Evidence:** S.PAY:29–31 caches `_paid` from the invoice **at load** (before the payment). S.PAY:266–271 `RefundFromSuccess` opens `RefundModalViewModel(..., _paid, ...)`. For a freshly-settled invoice loaded as unpaid, `_paid` was `0`.
- **Failure:** immediately after settling, "عودت" offers a max-refund of the stale (often 0) amount, so the just-collected money cannot be refunded from the success screen.
- **Fix:** pass `result.Invoice.Paid` (post-payment) into the refund modal, not the cached `_paid`.
- **Acceptance:** refund max on the success screen equals the amount just collected.

### ✅ X08 — P1 · Lost filters · Boarding history ignores date range, kinds, and species
- **Evidence:** B.HIST:402–404 builds `BoardingHistoryFilter(FromDate, ToDate, Search, _selectedKinds, _species, _sort, page, size)`. B.SVC:99–106 `GetHistoryAsync` passes only `filter.Search, filter.Sort, filter.Page, filter.PageSize` to `BoardingHistoryQuery` — `FromDate/ToDate/Kinds/Species` are dropped. (Sales does honor them: S.SVC:151–168.)
- **Failure:** every range chip, kind chip, and the species select in the Boarding history toolbar is inert; the summary totals (inflow/outflow/net) are computed over the unfiltered set, so the figures shown next to an active filter are wrong.
- **Fix:** extend `BoardingHistoryQuery` + its persistence to accept range/kinds/species and apply them server-side (match the Sales history query surface).
- **Acceptance:** selecting «۷ روز اخیر» + kind «پرداخت» changes both rows and summary totals.

### ✅ X09 — P1 · Crash risk · Non-Boarding mutation commands have no error handling either
- **Evidence:** No try/catch around the awaited service calls in: S.VM `NewBlankInvoice` (280–298), `VoidAsync` (372–389), `SelectInvoice` (217–240); S.PAY `PayAsync` is guarded by `result.Ok` (ok) but S.CAT `AddAsync` (211–224), S.OWN `SelectOwnerAsync/SelectGuestAsync` (69–92) are not; C.VM `Pay/Void/Refund/AddLine/SetDiscount/SetInsurance/Recalculate` (428–567) are not; S.IVVM `VoidConfirmed`/`DeleteConfirmed` are `async void` (see X10). Sales/Cashier services return a `*Result.Ok` for *some* ops but **throw** for others (`AddLineAsync`, `PickOwnerAsync`, `Ensure(...)` at C.SVC:155–158, `RequireDetailAsync` throwing `INVOICE_NOT_FOUND`).
- **Failure:** the throwing ops (add line, pick owner, any `Require`/`Ensure` miss) escape onto the UI dispatcher as an unhandled exception. The mix of "returns Result" and "throws" across one service is inconsistent, so callers cannot uniformly guard.
- **Fix:** make each service method's failure mode uniform (prefer returning a result), and wrap every command-invoking `RelayCommand` in try/catch → danger toast. Reuse `PatientsPresentation.CommandFailureMessage` as S.VM already does.
- **Acceptance:** forcing each mutation to fail shows a danger toast and never crashes.

### ✅ X10 — P1 · §4 violation · `async void` command handlers lose exceptions
- **Evidence:** S.IVVM:507 `private async void VoidConfirmed()` and S.IVVM:538 `private async void DeleteConfirmed()`. PERF §4: "never `async void` … cannot be awaited, loses exceptions, and cannot be tested."
- **Failure:** an exception in void/delete is lost (no toast, no logging) or crashes the process; the confirm-dialog callback cannot await completion before the sheet refreshes.
- **Fix:** convert to `private Task`-returning methods invoked as `() => _ = VoidConfirmedAsync()` from the confirm callback (the rest of the file already uses this pattern), and add try/catch.
- **Acceptance:** no `async void` remains in the three screens; a failing void surfaces a toast.

### ✅ X11 — P1 · Fake persistence · Boarding settings actions are design-sample stubs
- **Evidence:** B.VM:422–439 `SaveRates`, `SaveGeneral`, `AddRoom`, `AddService`, `EditService` all just `_toast.Show("… (نمونه طراحی)")`. The rate rows + toggles are hardcoded literals in B.VM:266–280 (`BuildSettings`) — not loaded from any service.
- **Failure:** the entire Boarding «تنظیمات» tab is non-functional: nightly rates, SMS/auto-transfer/capacity toggles, add-room, add/edit-service do nothing and are seeded from UI-side constants. This violates the AGENTS.md "no sample data in `Pages/**`" rule and misleads the operator (a toast says "ذخیره شد").
- **Fix:** back the settings tab with real Contracts+service members (read + write), or hide the tab until implemented. Remove the `(نمونه طراحی)` toasts and the hardcoded `RateRows`/`SettingToggles`.
- **Acceptance:** rates/toggles load from and persist to the backend; no `نمونه طراحی` string remains.

### ✅ X12 — P1 · Misleading success · `(نمونه طراحی)` appended to real success toasts
- **Evidence:** real backend mutations append "(نمونه طراحی)" to their success toast: B.CHK:54 (checkout — real `CheckoutAsync`), B.TR:77 (transfer — real `TransferAsync`), B.NEW:291 (create stay — real `CreateStayAsync`), B.DET:315,331 (remove/add item — real). The services these call are the real SQLite-backed implementations (Track C complete).
- **Failure:** operations that genuinely persist tell the user they are a "design sample", eroding trust and contradicting the real behavior. (Distinct from X11, where the action truly is a stub.)
- **Fix:** drop the "(نمونه طراحی)" suffix from every toast whose underlying call is a real mutation.
- **Acceptance:** real mutations show a plain success message; the phrase survives only where the action is a genuine stub (ideally nowhere — see X11/X13).

### ✅ X13 — P1 · Non-functional buttons · Print / Export are stubs across all three screens
- **Evidence:** B.DET:339 `Print`; S.VM:433 `PrintDay`; S.PAY:273 `Print`; S.IVVM:552 `Print`; C.VM:522,525,528 `Print`/`PrintInvoice`/`Export` — all `_toast.Show("… (نمونه طراحی)" | "نسخهٔ بعدی" | "به‌زودی")`.
- **Failure:** every receipt/invoice print and the Excel export are inert; the cashier's day-report, payment receipt, and invoice print do nothing. For a cashier/accounting product these are core, not cosmetic.
- **Fix:** implement a print/export path (even a minimal PDF/HTML receipt), or disable+label the buttons as unavailable rather than claiming an action occurred.
- **Acceptance:** each button produces an artifact or is visibly disabled; no false success toast.

### ✅ X14 — P1 · Wrong card classification · `IsStayLeaving` vs card `leaving` disagree
- **Evidence:** B.ITEM:126 computes card `leaving = Status==Active && remaining<=1 && !overdue` where `remaining = Max(0, CheckOut-today)`. The tab/count predicate B.ITEM:210–213 `IsStayLeaving` uses `Max(0, Days(CheckOut, today)) <= 1 && !IsStayOverdue`. `remaining` in the card is clamped and the two `overdue` inputs differ subtly (the card passes its own `overdue`, the static uses `IsStayOverdue`). A stay with `CheckOut == today` is `remaining==0` → card treats it as leaving/urgent, but the status pill path (B.ITEM:232–237) maps `remaining 0` to «تخلیه امروز» (warn) not «فردا تخلیه».
- **Failure:** the same stay can be counted under the «تخلیه امروز» tab (X via `TabMatches` "leaving") while its card chrome is driven by the urgent (warn) branch, and the tab count (`TabMatches` B.VM:211–225) uses yet another combination. Card border/progress tone, status pill, and the tab it falls under can disagree.
- **Fix:** compute `overdue`/`leaving`/`remaining` once in a single helper and feed the card, the pill, and the tab predicate from it. Remove the duplicated inline logic.
- **Acceptance:** for every stay, tab membership, status pill, and card tone are derived from one classification and never contradict.

### ✅ X15 — P1 · Blank-invoice kind ignored
- **Evidence:** S.SVC:186–192 `CreateBlankAsync(InvoiceKind kind)` ignores `kind` and always sends `CreateBlankInvoiceCommand(null, "مشتری مهمان", null)`. S.VM:281–283 only ever calls it with `InvoiceKind.Sale`, but the parameter is part of the seam.
- **Failure:** any future caller requesting a non-Sale blank (e.g. a purchase/return document) silently gets a Sale. A parameter that cannot affect the outcome is a latent correctness trap.
- **Fix:** honor `kind` in the command, or remove the parameter from the Contracts member until supported.
- **Acceptance:** `CreateBlankAsync(kind)` either produces that kind or the parameter does not exist.

### ✅ X16 — P1 · Capacity/room not validated on new stay
- **Evidence:** B.NEW:281 picks the room id with a fallback to `"A-1"` literal; no occupancy/capacity check before `CreateStayAsync`. Rooms carry occupancy (B.CONTR around `RoomInfo`), but the modal never consults it.
- **Failure:** two stays can be booked into the same room/slot; the hardcoded `"A-1"` fallback can target a non-existent/occupied room if the rooms list is empty or the selection is lost.
- **Fix:** require a valid selected room, validate availability against live occupancy (server-side in the create command), and remove the `"A-1"` literal fallback.
- **Acceptance:** booking an occupied/over-capacity room is rejected with a clear message; no literal room id in the UI.

### ✅ X17 — P1 · Due read before post can be stale (payment)
- **Evidence:** S.SVC:265–302 `TakePaymentAsync` reads `current.Summary.DueMinor` (line 286) from the pre-post read model, then (for a draft) runs `PostInvoiceCommand` (277–284), then caps `amount` against the **pre-post** `due`.
- **Failure:** if posting changes the payable total (tax/rounding/insurance finalized at post), the cap uses the wrong due; an otherwise-full settlement can be under/over by the post delta.
- **Fix:** re-read the due after posting and cap against the posted figure (or have the command return the posted due).
- **Acceptance:** for an invoice whose total changes at post, a "full" payment settles exactly to zero due.

---

## P2 — perf / resilience / consistency / maintainability

### ✅ X18 — P2 · PERF §6 · Sales sidebar and ready list are not virtualized
- **Evidence:** S.VIEW:930 `ReadyList` and S.VIEW:1006 `InvoiceRows` are bound to plain `ItemsControl` (no virtualizing panel). `GetInvoicesAsync` loads up to `MaxPageSize=200` (S.SVC:62–72) and the ready queue is unbounded. The Cashier invoice list (C.VIEW:402 `ListBox.records`) and the Boarding grid (B.VIEW:326) *are* virtualized — Sales is the outlier.
- **Failure:** every invoice/ready row is realized eagerly; with hundreds of invoices the sidebar builds the full visual tree on each `RenderSidebar()` (which also runs on every debounced keystroke, X21).
- **Fix:** host both lists in a virtualizing `ListBox`/`ItemsRepeater` with a bound-active style, matching the Cashier pattern.
- **Acceptance:** only visible rows are realized; frame time is flat as the invoice count grows.

### ✅ X19 — P2 · PERF §5 · `Clear()`+`Add()` loops on bound collections
- **Evidence:** plain `ObservableCollection` rebuilt item-by-item: S.VM `Lines`/`Payments` in `ShowInvoice` (588–598) and `ReadyList`/`InvoiceRows` in `RenderSidebar` (520–536); S.CAT `Rows`/`Recent` (140–164); S.OWN `Rows` (59–63); B.NEW `KnownPets` (110–117); B.DET `DayCells`/`Items` (239–263); C.VM `Stats`/`Tabs`/`InsurerOptions` in `ReloadAsync`/`LoadAsync` (208–218, 195–200). `RangeObservableCollection.ReplaceAll` exists and is used elsewhere (C.VM:298, B.VM:254).
- **Failure:** each rebuild raises one `CollectionChanged` + layout pass per item (O(n) notification storm) instead of one `Reset`.
- **Fix:** make these `RangeObservableCollection<T>` and call `ReplaceAll(...)` (as Cashier's `InvoiceRows`/`Stats` already do).
- **Acceptance:** a list rebuild raises a single `Reset`.

### ✅ X20 — P2 · Consistency · Cashier list not re-rendered after a mutation reloads invoices
- **Evidence:** C.VM `ReloadStatsAsync` (600–610) refetches `Stats`, `Tabs`, and `_invoices` but does **not** call `RenderList()`. After `Pay/Void/Refund`, `RefreshAsync` (586–598) calls `ReloadStatsAsync` then re-shows the detail, but the left list is only re-rendered inside `ShowDetail` → `RenderList` (344–361). Inline line/discount mutations go through `RunMutationAsync` (569–579) → `ShowDetail` → `RenderList` (ok), but the footer `Pay/Void/Refund` path refreshes `_invoices` and relies on the subsequent `ShowDetail` (via `RefreshAsync`) to render — if the detail refetch returns null (`ShowEmptyDetail`, 594–597) the list is **not** re-rendered and shows stale rows/state pills.
- **Failure:** after voiding the selected invoice (detail becomes terminal/empty) the sidebar can still show the pre-void state/tab bucket until the next interaction.
- **Fix:** call `RenderList()` in `ReloadStatsAsync` (or in `RefreshAsync` after the reload), independent of whether a detail is shown.
- **Acceptance:** post-void the list row moves to «ابطال» and tab counts update without a second click.

### ✅ X21 — P2 · §13 · Sidebar re-render runs full filter+group on every keystroke
- **Evidence:** S.VM:438 `OnSearchTextChanged` debounces `RenderSidebar`, which recomputes seg counts, filters, groups, and rebuilds two `ObservableCollection`s from scratch (506–556). No short-circuit when the normalized query is unchanged; `MatchesInvoice`/`MatchesEncounter` (558–570) re-`Normalize` the query **per row** instead of once.
- **Failure:** O(rows) normalization allocations per keystroke on top of the non-virtualized rebuild (X18/X19).
- **Fix:** normalize the query once per render (hoist out of the per-row predicate, PERF §2), and skip the rebuild when inputs are unchanged (PERF §13). Same per-row `Normalize` hoist applies to C.VM:286–291 and B.VM:237–240.
- **Acceptance:** one normalization per render; no rebuild when the query is unchanged.

### ✅ X22 — P2 · §2/§13 · Owner picker queries the backend on every keystroke, no debounce/guard
- **Evidence:** S.OWN:54 `OnQueryChanged => _ = SearchAsync()` with no `Debouncer` (every other search field debounces: S.VM:436, C.VM:132, B.VM:339, B.HIST:39). No generation token, so out-of-order responses can clobber (contrast B.HIST:397–423 stale-guard).
- **Failure:** a `GetOwnersAsync` round trip per character; a slow early response can overwrite a newer one, showing results for a stale query.
- **Fix:** add a `Debouncer` (~160–220 ms) and a generation token like B.HIST.
- **Acceptance:** typing "علی" issues ≤1–2 queries and always shows the latest query's results.

### ✅ X23 — P2 · §2/§13 · Catalog refetches the whole catalog after each add, just for "recent"
- **Evidence:** S.CAT:211–224 `AddAsync` → `RefreshRecentAsync` (235–241) calls `GetCatalogAsync()` again (full items+filters+recent) only to refresh the recent strip, then `Render()` rebuilds all rows.
- **Failure:** a full catalog round trip + full row rebuild on every single add; on a fast POS flow this stutters.
- **Fix:** update the recent list locally (promote the just-added item) or expose a lightweight `GetRecentAsync`; avoid re-pulling the whole catalog.
- **Acceptance:** adding an item does not refetch the full catalog.

### ✅ X24 — P2 · Correctness · Catalog highlight uses a different matcher than the filter
- **Evidence:** S.CAT filtering uses `PersianText.Normalize` + `PersianText.Contains` (131–135), but the highlight split uses `item.Title.IndexOf(match, CurrentCultureIgnoreCase)` on the **raw** title (S.CAT:315–317).
- **Failure:** rows that match only after Persian normalization (Arabic yeh/kaf, diacritics, Persian digits) are shown but not highlighted, or highlighted at the wrong offset.
- **Fix:** compute the highlight span on the normalized text/offset used for matching, or normalize both consistently.
- **Acceptance:** every matched row highlights the matched substring.

### ✅ X25 — P2 · Dead code / theme bypass · `CashierInvoiceRowViewModel.Resolve` ignores its key
- **Evidence:** C.ITEM:128 `private static IBrush Resolve(string key, string fallback) => BrushCache.Solid(fallback);` — the `key` (e.g. "DangerBrush") is never used; it always returns the hardcoded hex fallback (C.ITEM:98–100).
- **Failure:** the theme/token brushes are never consulted; a theme change to danger/ok/muted will not affect these rows. Misleading API (looks like a resource lookup).
- **Fix:** either resolve the real themed brush by key, or delete the `key` parameter and call `BrushCache.Solid(hex)` directly and document the literals as design tokens.
- **Acceptance:** either theme tokens drive the color, or the dead parameter is gone.

### ✅ X26 — P2 · Layering · Line subtotals are recomputed client-side (business logic in UI)
- **Evidence:** C.ITEM:154 and S.IVVM:664–665 compute `Round(unit*qty*(100-disc)/100)` in the ViewModel. AGENTS.md: ViewModels must not hold business rules; totals are the domain's job.
- **Failure:** the UI's rounding can diverge from the domain's (MidpointRounding, insurance/tax interaction), showing a per-line subtotal that does not sum to the server total. Duplicated formula to maintain in two places.
- **Fix:** have the detail read model carry each line's computed subtotal; bind to it instead of recomputing.
- **Acceptance:** displayed line subtotals come from the server and sum to the invoice total exactly.

### ✅ X27 — P2 · Resilience · Index-based line removal races the live list
- **Evidence:** B.SVC:64–71 `RemoveItemAsync(stayId, index)` re-reads detail and maps `index`→`ItemId` (good), but S.SVC `EditLineAsync`/`RemoveLineAsync` (209–226) and C.SVC `UpdateLineAsync`/`RemoveLineAsync` (112–129) resolve `LineId` from a fresh `InvoiceDetailQuery` at mutation time while the UI passed an `index` captured at render time. If the list changed between render and click (concurrent write / refresh), the index points at a different line.
- **Failure:** editing/removing "line 2" can hit the wrong line after an interleaving change; the guard only checks bounds, not identity equality with what the user saw.
- **Fix:** pass the `LineId` the row was built from (carry it on the row VM) and have the command match by id, rejecting if the id is gone (stale) rather than re-deriving from a possibly-changed index.
- **Acceptance:** a line edit after a concurrent insert/remove either hits the intended line by id or fails with a "stale" message.

### ✅ X28 — P2 · Duplicate work · KPIs refetch the invoice list already loaded by the page
- **Evidence:** S.VM `ReloadListsAsync` (481–488) fetches `GetInvoicesAsync()`, and `RefreshKpisAsync` (490–502) → S.SVC:105–132 `GetKpisAsync` runs **another** `InvoiceListQuery(null,1,200)` plus `ReadyForBillingQuery` (also already fetched via `GetReadyEncountersAsync`). So a page refresh issues the invoice-list and ready queries twice each.
- **Failure:** redundant dispatcher round trips per refresh/mutation; `GetKpisAsync` re-enumerates 200 invoices to sum open due that the page already has in `_invoices`.
- **Fix:** compute open-count/open-due from the already-loaded `_invoices`, or have one call return list+KPIs; avoid re-issuing the same queries.
- **Acceptance:** a page refresh issues each underlying query once.

### ✅ X29 — P2 · Scaling · Cashier invoice list pages the whole table into memory every reload
- **Evidence:** C.SVC:20–31 `GetInvoicesAsync` loops `CashierInvoiceListQuery(All, page, 200)` until exhausted, materializing **all** invoices; called on initial load *and* on every mutation via `ReloadStatsAsync` (C.VM:608–609).
- **Failure:** O(total invoices) fetched + mapped on each pay/void/refund/line-edit; unbounded growth over a clinic's lifetime. Filtering/sorting/paging is then redone client-side (C.VM:277–304).
- **Fix:** query server-side by tab + page + search and bind one page (the history tab already does this via `SalesHistoryQuery`); do not pull the whole table for the list.
- **Acceptance:** the list fetch is bounded to one page regardless of total invoice count.

### ✅ X30 — P2 · §4/startup · Modal VMs fire-and-forget `LoadAsync` with no failure surface
- **Evidence:** constructors kick `_ = LoadAsync()` with no catch: B.NEW:41, B.DET:53, S.PAY:52, S.CAT:50, S.IVVM:49, S.OWN:40. If the load throws (service failure), the modal opens blank with no error and the exception is swallowed/escapes.
- **Failure:** a failed detail/catalog/owner load shows an empty modal with no explanation; `PaymentModalViewModel.LoadAsync` closes silently on null (S.PAY:124–127) which looks like the button did nothing.
- **Fix:** wrap `LoadAsync` bodies in try/catch → danger toast + close, and show a loading/failed state instead of a blank modal.
- **Acceptance:** a forced load failure shows an error and does not leave an empty modal.

### ✅ X31 — P2 · UX/state · `OwnerPickResult` identity fields are dropped by the seam
- **Evidence:** S.OWN:78–80 passes `OwnerName/PetName/Mobile` in `OwnerPickResult`, but S.SVC:243–251 `PickOwnerAsync` uses only `pick.OwnerId` (sends `AssignInvoiceOwnerCommand(invoiceId, OwnerId, null)`); the display fields are ignored, and for a guest pick nothing is sent at all.
- **Failure:** fields that look meaningful are inert (latent trap like X15); a guest selection is a no-op server-side, so a draft created as guest stays guest with no explicit record of the choice.
- **Fix:** drop the unused fields from the request, or persist them; make the guest branch explicit server-side if "guest" must be recorded.
- **Acceptance:** the request carries only fields that affect the outcome.

### ✅ X32 — P2 · Consistency · Boarding `CheckoutRequest` is an empty payload the command ignores
- **Evidence:** B.CONTR:72 `public sealed record CheckoutRequest;` (empty by design), B.SVC:76–77 `CheckoutAsync(stayId, request)` ignores `request` entirely and sends `CheckoutStayCommand(stayId)`.
- **Failure:** harmless today but the parameter invites callers to think checkout accepts options. Minor seam noise.
- **Fix:** either remove the unused parameter, or document it as a reserved placeholder (it is already commented as such — acceptable if left, but flag for future).
- **Acceptance:** signature matches actual capability, or the placeholder intent is explicit.

### ✅ X33 — P2 · Markup correctness · Transfer modal hint has mismatched quote glyphs
- **Evidence:** B.TR:42–46 builds the hint as `HintLead="فاکتور «"`, `HintPet=PetName`, `HintMiddle="» — "`, `HintOwner=OwnerName`, `HintTail="» به صورت پیش‌نویس …"`. The owner name is wrapped by a trailing `»` (HintTail) with no matching opening `«`, and the pet already closed with `»` in HintMiddle.
- **Failure:** the rendered sentence reads «فاکتور «{pet}» — {owner}» …» — the owner is closed by a `»` that never opened; visually wrong Persian punctuation.
- **Fix:** balance the guillemets (open before owner or drop the stray `»`).
- **Acceptance:** the hint renders with balanced «…» around each quoted name.

### ✅ X34 — P2 · §2 · Boarding stays map recomputes services/calendar per list build
- **Evidence:** B.SVC:44–50 `GetStaysAsync` runs `BoardingServicesQuery` and `Calendar()` then `rows.Select(r => r.ToContract(services, now, zone))`; `GetDetailAsync` (52–59) and `GetHistoryAsync` (99–106) each independently call `Calendar()` (a fresh scope + two service resolves) per call.
- **Failure:** every stay refresh resolves the clock + zone through a new DI scope; `RefreshAfterChangeAsync` (B.VM:301–308) calls stays + rooms + services sequentially (3+ round trips) on each write.
- **Fix:** resolve clock/zone once per operation (already partly done) and batch the stays+rooms+services reads; cache the clinic zone for the session (it is stable).
- **Acceptance:** a write-refresh issues the minimal set of reads; zone is resolved once.

### ✅ X35 — P2 · §13 · Boarding page rebuilds rows+row-groups on every tab/species/search change
- **Evidence:** B.VM `RefreshCards` (250–264) re-runs `Filtered()` (LINQ with per-row `Normalize`, 227–248), rebuilds `FilteredStays`, then re-chunks into `StayRows` groups of 3 — on every tab click (`SetTab` 318–325), species change (166–171), and debounced keystroke (341–345).
- **Failure:** full filter+sort+chunk+two `ReplaceAll`s per interaction even when the result set is unchanged; `Filtered()` normalizes the query per row (PERF §2).
- **Fix:** hoist the normalized query out of the predicate; short-circuit when `(tab, species, query)` is unchanged (PERF §13).
- **Acceptance:** re-selecting the current tab does no rebuild; one normalization per filter.

### ✅ X36 — P2 · §2 · Boarding detail builds per-day item lists with O(days×items) LINQ
- **Evidence:** B.DET:240–251 loops `for i in 0..nights` and inside each iteration does `items.Where(it => it.At == date).ToList()` — O(nights × items) with a `ToList` allocation per day.
- **Failure:** for a long stay with many consumed items this is quadratic with per-day allocations; runs on every detail open and after each add/remove (via `LoadAsync`).
- **Fix:** group items by date once (`ILookup`/`Dictionary`) before the loop and index into it.
- **Acceptance:** day-cell build is O(nights + items) with no per-day allocation.

### ✅ X37 — P2 · §4 pattern · Detail mutations reload via full `LoadAsync` instead of applying the returned detail
- **Evidence:** B.DET `AddItemAsync`/`RemoveItemAsync` (311–336) call the service then `await LoadAsync()` (full detail refetch + full rebuild of header/timeline/day-cells/items/totals) and then `_onChanged` which reloads the whole page (B.VM `RefreshAfterWrite` → `RefreshAfterChangeAsync`, 294–308: stays + rooms + services + history).
- **Failure:** adding one consumed item triggers a detail refetch + a full page reload (stays/rooms/services/history) — many round trips for a single-line change.
- **Fix:** have the add/remove service return the updated detail and `Apply` it locally; debounce/scope the page refresh to Boarding data only.
- **Acceptance:** adding an item does one detail round trip and a scoped refresh.

### ✅ X38 — P2 · Stale comment / doc drift · Comments still reference removed mocks
- **Evidence:** C.ITEM:135,141 "Writes its edits back … so the **mock** recomputes"; S.IVVM:23 "routed through `ISalesQueryService` so the **mock** records…". AGENTS.md states the `Mocks/` folder was removed (Track C) and services are real.
- **Failure:** misleading docs; a reader may assume a mock still mediates these writes.
- **Fix:** update the comments to reference the real service.
- **Acceptance:** no "mock" reference remains in these three screens' comments.

### ✅ X39 — P2 · Precision · `double` arithmetic for money subtotals
- **Evidence:** C.ITEM:154 and S.IVVM:664 use `line.UnitPrice * line.Quantity * (100 - disc) / 100.0` in `double` then `Math.Round(..., AwayFromZero)` implicitly. B.PRES money formatting takes `long` (good), but the line math is floating.
- **Failure:** `double` on large Rial amounts can lose precision beyond 2^53 and round differently than the domain's integer/`Money` math (see X26), producing off-by-one Rial discrepancies.
- **Fix:** do line math in integer/`Money` units server-side (ties to X26) and bind the result.
- **Acceptance:** no `double` money math in the UI; subtotals match the domain to the Rial.

### X40 — P2 · Resilience · `ReadyCount`/`GetReadyCount` swallow all exceptions silently
- **Evidence:** S.PAY:253–264 `GetReadyCountAsync` `catch { return 0; }` (bare catch). While defensive, it hides a real backend failure as "0 ready", and the success screen then shows «فاکتور جدید» instead of the next-visit CTA.
- **Failure:** a transient failure is indistinguishable from "no ready visits"; masks errors that should at least be logged.
- **Fix:** catch narrowly (or let the caller decide), and keep the last known count rather than forcing 0 on error.
- **Acceptance:** a backend failure does not silently masquerade as an empty queue.

### X41 — P2 · Maintainability · Payment/method logic duplicated across Sales and Cashier
- **Evidence:** two parallel payment stacks: S.PAY/S.SVC (`PaymentMethod`, cap-to-due, settlement) and C.VM/C.SVC (`CashierPaymentMethod`, no cap, different method→domain switch at C.SVC:88–95 mapping `Mixed→Transfer`, `Credit→Credit`). Preset amounts differ (S.PAY: due/half/zero; C.VM:240 fixed 50k–1M literals). Method enums are separate (`PaymentMethod` vs `CashierPaymentMethod`).
- **Failure:** the same business operation (take payment, pick method) has two implementations that already diverge (X05 cap, X06 toast, mapping of Mixed), so fixes must be applied twice and drift silently (as they have).
- **Fix:** extract one payment service/method model shared by both screens; both pages call it.
- **Acceptance:** one code path registers payments; method mapping and cap behavior are identical on both screens.

---

## Cross-cutting themes (fix once, clears many)

1. **Uniform command error handling.** X02, X09, X10, X30 all stem from mutation/load calls lacking try/catch while services throw. Adopt one rule: every `RelayCommand`/`LoadAsync` that calls a service is wrapped → danger toast; and make services return results instead of throwing. Clears X02, X09, X10, X30, X40.
2. **One payment/money/time core.** X03, X05, X06, X17, X26, X39, X41 are the same root: money/time logic duplicated or done UI-side. A shared payment service + server-computed subtotals + clinic-zone day window fixes all seven.
3. **Seam honesty.** X01, X08, X15, X31, X32 are Contracts members whose fields the service drops. Audit every request record: each field must affect the outcome or be removed.
4. **Stop faking success.** X04, X11, X12, X13, X38 — remove `(نمونه طراحی)`/"به‌زودی" from real mutations, implement or disable the stubs, and delete mock-era comments.
5. **List hygiene.** X18, X19, X20, X21, X28, X29, X35, X36, X37 — virtualize unbounded lists, use `ReplaceAll`, page server-side, hoist normalization, short-circuit unchanged renders. Make Sales match the already-correct Cashier/Boarding list patterns.

## To reach 10/10

Fix all 3 P0 + 14 P1 first (data/money/flow correctness), then the P2 perf/consistency set (theme 5) and the seam/honesty cleanups (themes 3–4). After that, the two screens have: no lost input, no false confirmations, correct money/time, bounded virtualized lists, one payment core, and no mock-era residue.
