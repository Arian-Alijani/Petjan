# Inventory / Product / History audit — 2026-10-03

## Scope and method

- Screens: **Inventory — warehouse + products (انبار و کالا)**, **Product modals (کالا: کالای جدید / جزئیات کالا / تغییر موجودی)**, **History — unified ledger (تاریخچه)**. Static source review only — **no build, tests, app run, benchmarks, or DB execution** (per task). Findings describe code behavior; timing, render cost, and race frequency are reasoned, not measured.
- Layers reviewed per screen: `Pages/<Area>/*` (View + ViewModel + item/presentation VMs + modals) → `Animora.Contracts/<Area>/*` → `Animora.Client.Services/<Area>/*`, descending into `Animora.Application` use cases, `Animora.Domain`, and `Animora.Persistence.Sqlite` queries wherever a seam call forced it.
- Standard applied: `AGENTS.md` (MVVM seam rule, "no sample/mock branching in `Pages/**`"), `PERFORMANCE.md §1–§17`. Backend phase context: `backend-roadmap/PROGRESS.md` — Inventory/Purchasing = **C06 done**, History = **C10 done**, next phase **D01**; absent server/Datasync (Tracks D–E) is **not** a defect. A success toast that claims an action happened when it did not **is** a defect.
- Severity: **P0** = data corruption / lost money / silent loss of persisted business data / crash on a primary flow. **P1** = lost/ignored user input, broken primary flow, wrong recorded data. **P2** = incorrect secondary behavior, performance, resilience, consistency, standards, maintainability.
- **Total 34 findings: 3 P0, 11 P1, 20 P2.**

## Path aliases

| Alias | Path |
|---|---|
| I.VM | `src/Animora.Desktop/Pages/Inventory/InventoryPageViewModel.cs` |
| I.ITEM | `src/Animora.Desktop/Pages/Inventory/InventoryItemViewModels.cs` |
| I.PRES | `src/Animora.Desktop/Pages/Inventory/InventoryPresentation.cs` |
| I.SET | `src/Animora.Desktop/Pages/Inventory/InventorySettingsItemViewModels.cs` |
| I.VIEW | `src/Animora.Desktop/Pages/Inventory/InventoryPageView.axaml` |
| P.NEW | `src/Animora.Desktop/Pages/Inventory/NewProductModalViewModel.cs` |
| P.MOD | `src/Animora.Desktop/Pages/Inventory/ProductModalViewModel.cs` |
| P.MITEM | `src/Animora.Desktop/Pages/Inventory/ProductModalItemViewModels.cs` |
| P.ADJ | `src/Animora.Desktop/Pages/Inventory/StockAdjustModalViewModel.cs` |
| I.CONTR | `src/Animora.Contracts/Inventory/IInventoryQueryService.cs` + `InventoryModels.cs` |
| I.SVC | `src/Animora.Client.Services/Inventory/InventoryQueryService.cs` |
| I.MAP | `src/Animora.Client.Services/Inventory/InventoryMapping.cs` |
| I.CMD | `src/Animora.Application/UseCases/Inventory/InventoryCommands.cs` |
| I.QRY | `src/Animora.Persistence.Sqlite/Queries/InventoryQueries.cs` |
| H.VM | `src/Animora.Desktop/Pages/History/HistoryPageViewModel.cs` |
| H.ITEM | `src/Animora.Desktop/Pages/History/HistoryItemViewModels.cs` |
| H.PRES | `src/Animora.Desktop/Pages/History/HistoryPresentation.cs` |
| H.VIEW | `src/Animora.Desktop/Pages/History/HistoryPageView.axaml` |
| H.SVC | `src/Animora.Client.Services/History/HistoryQueryService.cs` |
| H.CSV | `src/Animora.Client.Services/History/CsvWriter.cs` |
| H.TEXT | `src/Animora.Desktop/Localization/HistoryText.cs` |
| H.QRY | `src/Animora.Persistence.Sqlite/Queries/ActivityFeedQueries.cs` |
| AF.MAP | `src/Animora.Application/Activity/ActivityFeedMapper.cs` |
| AF.WRITE | `src/Animora.Persistence.Sqlite/Activity/ActivityFeedWriter.cs` |

---

## P0 — data loss / corruption / broken primary flow

### P0-1 — New product's initial stock is silently discarded
**Where:** P.NEW `CreateAsync` (lines ~136–159) → I.SVC `CreateProductAsync` (lines 131–153) → I.CMD `CreateProductCommand` / `ProductVariantInput` (lines 22–115).
**Problem:** The «کالای جدید» single-unit form has a «موجودی اولیه» field bound to `StockText`. `CreateAsync` parses it (`var stock = ParseInt(StockText)`) and packs it into `ProductVariant.Stock`. But `InventoryQueryService.ToInputs` maps a contract variant to `ProductVariantInput`, which **has no stock field** (stock is movement-based, `R-B23`). `CreateProductCommandHandler` never records an opening `StockMovement`. Result: every new product is created with **on-hand = 0** regardless of what the user typed, and the modal still shows the success toast. The user believes stock was set; it was not.
**Fix:** Treat opening stock as an explicit first movement. After `CreateProductCommand` succeeds, if `variant.Stock > 0`, issue an `AdjustStockCommand(productId, newVariantId, IntToScaled(stock), AdjustmentReasons.OpeningBalance)` per variant (add an `OpeningBalance` reason to `AdjustmentReasons` if absent) inside the same service call. Preferable: add an optional `OpeningStockScaled` to `ProductVariantInput` and have `CreateProductCommandHandler` emit the seed movement in the same UoW so the opening balance and the product are one atomic commit. Until one of these lands, **remove the initial-stock field** from P.NEW so the UI cannot promise what it drops.

### P0-2 — Product modal's single-unit stock edit is silently discarded
**Where:** P.MOD `CurrentVariants` (lines 482–503) + `SaveAsync` (lines 511–528) → I.SVC `SaveProductInfoAsync` (lines 76–83) → I.CMD `UpdateProductInfoCommand` / `ApplyVariants` (lines 120–219).
**Problem:** The Info tab exposes a stock stepper (`SingleStockText`, `IncrementSingle`/`DecrementSingle`) and the Variants tab cards carry an editable `StockText`. `CurrentVariants` reads these into `ProductVariant.Stock` and `SaveAsync` sends them through `SaveProductInfoAsync`. But `UpdateProductInfoCommand.ApplyVariants` only updates SKU/attribute/cost/sale/reorder/barcode via `product.UpdateVariant`; **there is no stock field and no movement is recorded.** Editing stock on the Info/Variants tab and pressing «ذخیره تغییرات» changes nothing on-hand, yet toasts success. Only the dedicated StockAdjust modal (P.ADJ) actually moves stock.
**Fix:** Make the stock controls on the Info/Variants tabs **read-only displays** (the correct architecture — on-hand is a projection, edited only through a reasoned movement), OR, if inline editing must stay, diff each variant's edited stock against its loaded on-hand in `SaveProductInfoAsync` and emit an `AdjustStockCommand` per non-zero delta (exactly as P.ADJ does) in the same call. Do not let `SaveProductInfoAsync` imply it persisted stock when the command cannot.

### P0-3 — Every inventory/product mutation toast falsely claims "design sample"
**Where:** P.MOD `SaveAsync` line 526 (`"تغییرات ذخیره شد (نمونه طراحی)"`), `DeleteAsync` line 535 (`"کالا حذف شد (نمونه طراحی)"`); P.NEW `CreateAsync` line 161 (`"کالا ساخته شد (نمونه طراحی)"`); P.ADJ `SaveAsync` line 94 (`"موجودی به‌روزرسانی شد (نمونه طراحی)"`); I.VM `AddCategory`/`SaveSettings`/`DeleteCategory` lines 404/407/429 (`"— نمونه طراحی"`).
**Problem:** These screens are on the **real C06 backend** (`PROGRESS.md`: "Inventory + Purchasing (C06)"), yet every toast still carries the Phase-18 mock suffix «نمونه طراحی» ("design sample"). The create/save/delete/adjust toasts are factually wrong — the operation **did** persist. Worse, they fire **unconditionally after an `await` with no try/catch**: if the command throws `ServiceException` (I.SVC `SendAsync` throws on any failure), the exception propagates as an unobserved async-void fault, the modal closes via `_dialogs.Close()` *only if reached* (it is, before the throw point in some paths but after in others), and the user still may have seen a success toast on the happy path while a failure path crashes silently.
**Fix:** (a) Remove the «نمونه طراحی» suffix from every real mutation toast (this is exactly what Boarding/Sales X12 did). (b) Wrap each mutation in `try { await …; _dialogs.Close(); toast.Show(success, Tone.Ok); } catch (ServiceException ex) { toast.Show(map(ex), Tone.Danger); }` — keep the modal open on failure, show a typed danger toast, and never show success unless the command returned success. The category settings actions (I.VM) are pure stubs — see P1-9.

---

## P1 — lost input / wrong recorded data / broken flow

### P1-1 — Inventory product list loads the entire catalog and filters in memory
**Where:** I.VM `LoadAsync` (lines 116–133) + `RefreshProducts` (lines 176–215); I.SVC `GetProductsAsync` (lines 31–47) sends `ProductCatalogQuery(null, null, false, 1, MaxPageSize=200)`.
**Problem:** The screen pulls up to 200 products with all variants into `_products`, then does category/stock/search filtering client-side with LINQ over the whole list on every keystroke and chip toggle. The backend already supports server-side `search`, `categoryId`, and `lowStockOnly` (I.QRY `GetCatalogAsync`), and `ProductCatalogQuery` carries those parameters — they are passed as `null/null/false`. This is the exact anti-pattern Sales/Cashier fixed (X29). It breaks silently once a clinic exceeds 200 products: product #201 is invisible with no paging UI. Violates `PERFORMANCE.md §5` (server-side filter/sort/page).
**Fix:** Push `_catFilter`, `_stockFilter`, and the normalized search term into `ProductCatalogQuery` and re-query on change (debounced search already exists at I.VM line 44). Add a bounded page window + «نمایش بیشتر» or pager, mirroring the Cashier list. Keep only the current page in `FilteredProducts`.

### P1-2 — History detail renders raw entity IDs instead of names
**Where:** H.TEXT `Detail` (lines 37–49) + `Label` (lines 51–57); fed by AF.MAP `Draft` args (e.g. `new { x.OwnerId }`, `new { x.PetId, x.VaccineCode }`).
**Problem:** The feed stores language-neutral arg JSON containing only IDs (`{"PetId":"01H...","OwnerId":"01H..."}`). `HistoryText.Detail` renders them verbatim as «بیمار: 01HXYZ…، مالک: 01HABC…». The user sees opaque ULIDs, never the pet/owner/product name. The whole "unified ledger" value proposition — a human-readable audit trail — is defeated for every row's detail line.
**Fix:** The feed must store display-resolved names at write time (AF.WRITE is the single write authority and runs inside the source aggregate's UoW, so the names are available). Extend `ActivityEventDraft` with a small resolved-label map (e.g. `PetName`, `OwnerName`, `ProductName`) populated by the mapper from the event, and render those. Avoid lazy per-row lookups at read time (N+1, `PERFORMANCE.md §5`). Rebuild path (`RebuildActivityFeedAsync`) must resolve the same way for consistency.

### P1-3 — History CSV export leaks Gregorian-UTC dates, raw JSON and untranslated keys
**Where:** H.CSV `WriteAsync` (lines 19–22) + H.VM `ExportCsvAsync` (lines 468–487).
**Problem:** `CsvWriter` writes `row.OccurredUtc.ToString("yyyy/MM/dd")` and `"HH:mm"` — **Gregorian, UTC**, not the clinic-local Jalali shown on screen (the rest of the app, H.SVC `HistoryMapping.Map`, converts to the clinic zone + `PersianCalendar`). It writes `row.DetailArgsJson` raw (the same opaque JSON as P1-2) and `row.TitleKey` as the stable key; H.VM then does a **post-hoc string `Replace`** of the header and the 14 known title keys over the whole CSV body. This is fragile (a title key appearing inside a detail value would be corrupted), still leaves dates Gregorian-UTC, status as the raw enum name, and detail as JSON. An exported ledger does not match the on-screen ledger.
**Fix:** Do the formatting **once, server-side, in the mapper**, not by string replacement in the VM. Have `ActivityExportQuery` project already-localized rows (Jalali local date/time via the clinic zone, translated title, resolved detail, translated status) or pass the `TimeZoneInfo` + a formatting delegate into `CsvWriter`. Delete the `Replace` chain in H.VM. Keep the UTF-8 BOM (already correct for Excel).

### P1-4 — Product modal History tab never reloads after a stock adjustment
**Where:** P.MOD `OpenStockAdjust` is on I.VM (lines 357–362), but the product modal's own History tab is built once in `LoadAsync`/`BuildHistory` (lines 392–479) and has no refresh hook; I.VM `Refresh` (lines 380–399) reloads the *page* list, not an open modal.
**Problem:** If the user opens a product, reads the History tab, then (from the list) adjusts stock, the modal's timeline and KPIs are stale. More concretely: the StockAdjust modal's `onChanged` → I.VM `Refresh` rebuilds the page but does not touch an already-open ProductModal instance. The movement exists in the DB but the visible history does not reflect it until the modal is reopened.
**Fix:** Either reload the product modal's detail on tab activation (re-call `_service.GetDetailAsync` when `IsHistoryTab` becomes true), or subscribe the open modal to `IDataChangeNotifier`(`DataDomain.Inventory`) and rebuild `BuildHistory`/`RebuildKpis`. The page ViewModel already uses `RefreshOnDataChange`; mirror it in the modal.

### P1-5 — `ProductModalViewModel` save drops the category, glyph and attribute-name changes to the command
**Where:** P.MOD `SaveAsync` (lines 511–528) builds a full `ProductInfoUpdate` (name/code/brand/category/glyph/description/attributeName/variants); I.SVC `SaveProductInfoAsync` (lines 76–83) forwards to `UpdateProductInfoCommand`, which accepts only `Name`, `Brand`, `Notes`, prices, reorder, variants (I.CMD lines 120–128).
**Problem:** `ProductInfoUpdate.CategoryId`, `.Code`, `.Glyph`, and `.AttributeName` are accepted by the contract and edited in the UI (category dropdown, code field) but **never reach the domain** — `UpdateProductInfoCommand` has no category/sku/glyph parameter and `SaveProductInfoAsync` does not pass them. Changing a product's category or code in the modal and saving succeeds with a toast but **does not change the category or code**. Glyph is a pure UI derivation (I.MAP `GlyphOf` from kind) so editing it is meaningless, yet the field is editable.
**Fix:** Extend `UpdateProductInfoCommand` with `CategoryId` and `Sku` and apply them in the handler (the domain `Product` must expose a reclassify/re-sku mutator with its invariants, incl. SKU uniqueness). Pass them from `SaveProductInfoAsync`. Remove the editable glyph control (or persist a real presentation column) rather than letting the user edit a value the system recomputes.

### P1-6 — Variant SKU edits in the Variants tab are not uniqueness-guarded before send
**Where:** P.MITEM `InventoryVariantCardViewModel.ToVariant` (lines 106–114) carries the user-typed `Sku`; I.SVC `ToInputs` (lines 183–196) passes it straight through; domain enforces `PRODUCT_SKU_DUPLICATE` on apply.
**Problem:** The Variants tab lets the user type arbitrary SKUs. Two cards with the same SKU, or a blank SKU on a multi-variant product, is only caught deep in the domain and surfaces as a thrown `ServiceException` (I.SVC `SendAsync`) — which, per P0-3, is currently unguarded, so it faults async-void instead of showing a friendly message. The whole save is rejected atomically, losing all other edits in the modal.
**Fix:** Validate SKU non-empty + intra-modal uniqueness in `SaveAsync` before calling the service; show an inline field error. Pair with the P0-3 try/catch so a domain `PRODUCT_SKU_DUPLICATE` maps to a specific Persian message and keeps the modal open.

### P1-7 — Stock-adjust absolute-target bridge can race and under/over-correct
**Where:** I.SVC `AdjustStockAsync` (lines 158–181); P.ADJ `SaveAsync` (lines 81–96).
**Problem:** The contract carries an **absolute** target stock; the command takes a **signed delta**. `AdjustStockAsync` reads current on-hand via `GetDetailAsync`, computes `delta = target − current`, and sends it. Between the read and the `AdjustStockCommand`, another sale/receipt can change on-hand, so the applied delta sets the wrong absolute value (classic read-modify-write race, `PERFORMANCE.md`/offline-correctness). Also P.ADJ loops rows and awaits one `AdjustStockAsync` per changed row — each opens its own DI scope and its own `GetDetailAsync` round-trip (N reads + N commands for N variants).
**Fix:** Make the adjustment atomic and delta-native end to end: have the StockAdjust UI capture the **delta** the user intends (the stepper already works in deltas) and send that, or add a `SetOnHandCommand` that computes the delta inside the handler's transaction against the freshly-read on-hand (no client round-trip gap). Batch all variant deltas of one product into a single command/scope instead of per-row scopes.

### P1-8 — History amount summary totals are page-wide income/expense but the on-screen "net" mixes scopes
**Where:** H.VM `ApplyPage` (lines 395–432) uses `page.IncomeTotal`/`page.ExpenseTotal` (whole filtered set, correct) for the summary bar, but `RenderFeed` (lines 436–464) computes each day group's `net` from **only the current page's rows** (lines 452–453).
**Problem:** Two different scopes are presented together without distinction: the summary bar is over the entire filtered result, while each day-group header's net is over just the rows that happen to be on the current page. A day split across two pages shows a partial net on each page, which the user reads as the day's true net. Misleading for any day with more events than fit on a page.
**Fix:** Either compute day-group nets server-side over the full filtered set (so they are page-independent), or clearly scope the group header as "net of shown rows". Given the ledger's audit intent, server-side per-day aggregation is the correct answer; the group header should never silently depend on page size.

### P1-9 — Inventory category settings are non-functional stubs on a real backend
**Where:** I.VM `AddCategory` (404), `SaveSettings` (407), `DeleteCategory` (419–430), `PickTone` (410–416); I.SET tone/toggle VMs.
**Problem:** The entire «تنظیمات کالاها» tab — add category, rename, retone, delete, and the three inventory toggles (low-stock alert, expiry tracking, auto-code) — is design-only: every action toasts «نمونه طراحی» or mutates a throwaway VM. `DeleteCategory` even guards "category has products" client-side but then just toasts success without deleting. On a C06-complete backend this is a screen full of controls that look live and do nothing; the toggles especially imply persisted settings that are never read anywhere.
**Fix:** Either wire these to real category/settings commands (category CRUD + a user-preferences-backed toggle set, like C10's `UserPreferences`), or — if category management is genuinely out of scope for this phase — hide/disable the controls and remove the misleading success toasts (the Boarding X11 precedent: delete fake settings rather than fake their success).

### P1-10 — `GetHistoryAsync` and `AddVariantAsync` contract members are implemented but never called
**Where:** I.CONTR declares `GetHistoryAsync` (line 44) and `AddVariantAsync` (line 41); I.SVC implements both (lines 85–129). No Desktop caller exists (grep across `Pages/**` returns none; the product modal builds history from `GetDetailAsync().Movements` instead).
**Problem:** Two public seam methods carry real dispatch cost and documentation but are dead from the UI. `AddVariantAsync` in particular duplicates logic that `SaveProductInfoAsync` already does (append via `ApplyVariants`), and the Variants tab adds variants purely client-side (`AddManualVariant`) then relies on save — so the dedicated append path is never exercised, meaning its distinct behavior (immediate persist + SKU auto-unique) is untested by any flow.
**Fix:** Either adopt `AddVariantAsync` for the Variants tab's «تنوع دستی» (immediate persist, matching the contract's documented intent) and `GetHistoryAsync` for the modal's History tab paging, or remove both from the contract + service to cut dead surface. Do not leave two parallel ways to append a variant.

### P1-11 — `AdjustStockAsync` loses the user-chosen reason; always "Correction"
**Where:** I.SVC `BridgeAdjustReason = AdjustmentReasons.Correction` (line 29) + `AdjustStockAsync` (line 179); P.ADJ offers no reason input.
**Problem:** Every manual stock adjustment is recorded with reason `Correction`, regardless of why (damage, loss, recount, opening balance). The domain supports a reason taxonomy (`AdjustmentReasons`) and the movement stores it; the history timeline and audit trail therefore cannot distinguish a write-off from a recount. This degrades the ledger's forensic value and any future shrinkage reporting.
**Fix:** Add a reason selector to the StockAdjust modal, carry it on `StockAdjustRequest`, and pass it through to `AdjustStockCommand.ReasonCode`. Keep `Correction` only as the default.

---

## P2 — secondary behavior / performance / standards / maintainability

### P2-1 — No `x:CompileBindings` on any of the three screens' XAML
**Where:** I.VIEW, P.* views, H.VIEW (grep: zero `x:CompileBindings`). Views set `x:DataType` but not compiled bindings.
**Problem:** Bindings fall back to reflection — slower per-binding, and binding typos fail silently at runtime instead of at build. `PERFORMANCE.md` favors compiled bindings for hot item templates (product rows, history rows re-template on every page/filter change).
**Fix:** Add `x:CompileBindings="True"` at each `UserControl`/`DataTemplate` root (the `x:DataType` is already present) and fix any binding errors surfaced. High value on the row item templates.

### P2-2 — History feed search scans `DetailArgsJson` with `LIKE %term%`
**Where:** H.QRY `Filter` (lines 33–37): `x.DetailArgsJson.Contains(search)`.
**Problem:** A leading-wildcard `LIKE` over a JSON text column is a full scan, un-indexable, and matches on opaque IDs (so a user searching a pet name matches nothing useful — see P1-2). Combined with `Title`/`EntityId`/`ActorName` OR-contains, every search is an O(n) scan of the feed. `PERFORMANCE.md §5` (indexed server reads).
**Fix:** Once P1-2 stores resolved names, search those indexed columns, not the JSON blob. Add a dedicated searchable text column (title + resolved names + entity) with an index, and query it; drop the JSON `Contains`.

### P2-3 — Inventory low-stock filter uses a correlated per-variant subquery with double-casts
**Where:** I.QRY `InventoryStockQuery.LowStockProducts` (lines 23–28).
**Problem:** For each product, each variant runs a correlated `StockOnHandSnapshot` subquery, with `(double)` casts on both sides to dodge SQLite's lexicographic TEXT compare of the reorder level (reorder is stored as TEXT via the non-summable converter). This is both a performance smell (per-variant correlated subquery over the bounded 1000-row scan) and a correctness smell (float compare of quantities). The method is also only reachable when `lowStockOnly` is pushed to the query — which the current UI never does (P1-1), so it is effectively dead today.
**Fix:** Store the reorder level in a numerically comparable, scaled integer column (consistent with `OnHandScaled`) so the compare is integer `<=` with no cast; or precompute a per-variant `IsLow` flag in the snapshot maintainer (`StockSnapshotUpdater`) and filter on an indexed boolean. Then wire P1-1 to actually use it.

### P2-4 — Inventory catalog paging happens in memory after a 1000-row scan
**Where:** I.QRY `GetCatalogAsync` (lines 39–82): `Take(MaxCatalogScan=1000)` then builds all read models, then `rows.GetRange(skip, …)` for the page.
**Problem:** The query always loads up to 1000 products + variants + on-hand map, materializes every read model, and only then slices the requested page in memory. For low-stock filtering it must (it filters post-projection), but for the common unfiltered/category case it defeats SQL paging. `PERFORMANCE.md §5`.
**Fix:** When `lowStockOnly` is false, apply `Skip/Take` in SQL (`ToPagedAsync`, already used by `GetMovementHistoryAsync`) and load on-hand only for the page's products. Keep the scan-then-filter path solely for the low-stock case, and bound that with the P2-3 indexed flag.

### P2-5 — `GetDetailAsync` caps movement history at 100 with no paging
**Where:** I.QRY `GetDetailAsync` (lines 93–97): `.Take(MaxMovementHistory=100)`.
**Problem:** The product modal's History tab shows at most the latest 100 movements with no "load more". A busy product silently hides older history — the audit tab looks complete but is truncated. The dedicated paged path (`GetMovementHistoryAsync` + the contract's `GetHistoryAsync`) exists but is unused (P1-10).
**Fix:** Use the paged movement query for the History tab with a «نمایش بیشتر»; or at minimum show a "showing latest 100" notice so truncation is explicit.

### P2-6 — Product rows rebuild all derived brushes/strings on every filter keystroke
**Where:** I.VM `RefreshProducts` (lines 206–211) does `FilteredProducts.ReplaceAll(items.Select(p => new InventoryProductRowViewModel(...)))`; I.ITEM ctor (lines 145–201) recomputes status, sums, price range, percent, four status brushes, variant sub-VMs per row.
**Problem:** Every search keystroke (post-debounce) and every chip toggle re-instantiates every row VM from scratch, re-summing variants and re-deriving brushes. The brush caches (`BrushCache`/`GradientBrushCache`) soften allocation, but the per-row LINQ sums (`Count`, `Sum`, `Select(...).ToList()`, `Min`/`Max`) and full sub-VM rebuild are redone wholesale. `PERFORMANCE.md §2` (no per-item recompute) / §13 (single Reset).
**Fix:** Once filtering is server-side (P1-1), rows change only when the page changes, which bounds this. Additionally, hoist the per-product derivations (status, totals, price range) into the read model/mapper so the row VM is a thin projection, not a calculator.

### P2-7 — History day-group net uses `GroupBy` + two filtered `Sum`s per group in the VM
**Where:** H.VM `RenderFeed` (lines 444–461).
**Problem:** For each day group it does `group.Where(AmountSign==1).Sum()` and a second `group.Where(AmountSign==-1).Sum()` — two passes per group, allocating closures. Minor at 25–100 rows/page but it is per-render and avoidable.
**Fix:** Single fold per group accumulating income/expense in one loop; or compute nets server-side (also fixes P1-8).

### P2-8 — `HistoryText.Detail` parses JSON per row on the UI thread every render
**Where:** H.TEXT `Detail` (lines 37–49), called from H.VM `RenderFeed` (lines 447–451) for each row's `Detail`.
**Problem:** Each row re-parses its arg JSON (`JsonDocument.Parse`) and rebuilds the detail string on every feed render (page change, filter, sort). JSON parsing per visible row per render is wasteful and runs on the UI thread. (Also moot for usefulness until P1-2.)
**Fix:** Resolve/format detail once at write time (P1-2) and store the display string, or memoize per event id. The feed row VM should receive a ready string.

### P2-9 — `async void` command handlers fire-and-forget across all three screens
**Where:** P.MOD `SaveAsync`/`DeleteAsync` are `[RelayCommand] async Task` (good) but invoked by fire-and-forget patterns elsewhere: I.VM `Refresh` → `_ = ReloadAsync()` (line 383), `OnNavigatedTo` → `_ = ReloadPageAsync()` (line 111); H.VM `SortIndex`/`PageSizeIndex` setters → `_ = ReloadAsync(...)` (lines 158/175), `OnSearchTextChanged` debounce → `_ = ReloadAsync(...)`.
**Problem:** Multiple `_ = …Async()` discards. Exceptions inside these (e.g., `ServiceException` from a failed query) are unobserved — no danger toast, no load-state error unless routed through `TrackLoadAsync`. I.VM `ReloadAsync` (lines 386–399) is **not** wrapped in `TrackLoadAsync` (unlike `ReloadPageAsync`), so a failure during the post-mutation refresh is swallowed. H.VM `ReloadAsync` has a try/finally but no catch — a thrown `ServiceException` (H.SVC `GetPageAsync` throws on failure) faults the discarded task silently.
**Fix:** Route every reload through the page's load-state tracker (`TrackLoadAsync`) or wrap in try/catch → danger toast. Never discard a task that can throw a service failure without an observer. Mirror the Sales/Cashier X09 guard pattern.

### P2-10 — Stale-response guard exists in History but not in Inventory
**Where:** H.VM uses a `_generation` token (lines 54, 369, 379–391) — correct. I.VM has none: `LoadAsync`/`ReloadAsync` just overwrite `_products` whenever they complete.
**Problem:** Inventory reloads can be triggered concurrently (navigation + `RefreshOnDataChange` on Sales/Cashier/Purchasing changes + modal `onChanged`). Two overlapping reloads can interleave so an older response's `AddRange` lands after a newer one, showing stale data. `PERFORMANCE.md §6` (stale-response guard).
**Fix:** Add the same generation-token guard to I.VM `LoadAsync`/`ReloadAsync` (or serialize reloads), discarding a response whose token is superseded.

### P2-11 — `RefreshOnDataChange` reloads the whole catalog on any Sales/Cashier/Purchasing change
**Where:** I.VM ctor line 65: `RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Sales, DataDomain.Cashier, DataDomain.Purchasing)`.
**Problem:** Any sale, payment, or purchase anywhere re-pulls the entire 200-product catalog + variants + on-hand, even if the Inventory screen is not visible (singleton page). Combined with P1-1's full load, this is a heavy, frequent refetch. `PERFORMANCE.md §5/§6`.
**Fix:** Gate the refresh on visibility (only reload when the page is active; otherwise set a dirty flag consumed on `OnNavigatedTo`), and once server-side paging lands (P1-1) reload only the current page.

### P2-12 — `ToneOf`/`IconOf`/`GlyphOf` derive presentation from IDs/names; unstable and lossy
**Where:** I.MAP `ToneOf` (lines 105–110), `IconOf` (lines 116–121), `GlyphOf` (lines 89–98).
**Problem:** Thumbnail tone is `sum(id chars) % 6`, category icon is `sum(name chars) % N`, glyph is keyed off product kind. Renaming a category changes its icon; the glyph cannot be customized (P1-5 notes the editable glyph field is therefore a no-op). These are deterministic but arbitrary and surprise users (a rename silently re-skins the row).
**Fix:** If presentation matters, persist real `tone`/`icon`/`glyph` columns on category/product; otherwise document them as purely cosmetic and remove the editable glyph control (P1-5). At minimum key tone off an immutable id, not mutable text (id is already used for tone — keep that; fix icon to use category id, not display name).

### P2-13 — `AttributeNameOf` ignores the product's existing dimension
**Where:** I.SVC `AttributeNameOf` (lines 202–206): returns the requested name or `DEFAULT`, never the variants' current attribute name (the `variants` parameter is unused).
**Problem:** The parameter `variants` is accepted and ignored; the XML doc claims it falls back to "the name already on the product's variants" but the code does not. Saving a multi-variant product without re-sending the attribute name can collapse it to `DEFAULT`, silently converting a multi-dimension product into an implicit single-unit one in the mapping (I.MAP `AttributeOf` treats `DEFAULT` as "no dimension").
**Fix:** Implement the documented fallback: when `requested` is blank, read `variants[0].AttributeName` before defaulting. Remove the unused parameter if the fallback is intentionally dropped — but the doc/behavior must match.

### P2-14 — New-product multi-variant path creates options with no per-variant pricing
**Where:** P.NEW `CreateAsync` (lines 120–134): when `HasVariants`, it builds `attributeOptions` from a comma-split string and sends `variants = []`; I.SVC `CreateProductAsync` then synthesizes one `DEFAULT` variant (lines 137–143) — **ignoring the attribute options entirely**.
**Problem:** Choosing «چند‌تنوعی» and entering options (e.g. «۱kg، ۵kg») creates a product with a **single DEFAULT variant**, not one variant per option. The attribute name and options are dropped on the floor (the service only looks at `request.Variants`, which is empty). The multi-variant create flow is effectively broken — it silently produces a single-unit product.
**Fix:** In `CreateProductAsync`, when `request.Variants` is empty but `AttributeOptions` is non-empty, expand options into one `ProductVariantInput` per option (sharing `AttributeName`), each with a unique SKU. Or have P.NEW build the variant list client-side from the options before calling the service. Add a focused test for "create multi-variant product → N variants persisted".

### P2-15 — Settings toggles and tone picker mutate throwaway state with no persistence contract
**Where:** I.SET `InventorySettingToggleViewModel` (lines 96–109) has `IsOn` with no backing store; I.VM `SettingToggles` built in `BuildSettings` (lines 237–245); `PickTone` (I.VM 410–416) mutates row VM only.
**Problem:** Toggles and tones are pure view state; nothing reads them, nothing saves them. (Subset of P1-9, but called out for the data model: there is no settings DTO/contract at all for inventory, so even wiring them later has no seam.)
**Fix:** Introduce an inventory-settings contract backed by `UserPreferences` (C10 precedent) if these are to be real; otherwise remove.

### P2-16 — Duplicated stock-status derivation in two places, risk of drift
**Where:** I.VM `StockStatus` (lines 217–223) and I.ITEM ctor (lines 162–165) each compute the same `out/low/ok` status from variant reorder comparisons.
**Problem:** The same rule is written twice with the same literals. A change to the low-stock rule must be made in both or the list filter and the row badge disagree.
**Fix:** Single helper (e.g. on the read model or a shared static) used by both the filter predicate and the row VM.

### P2-17 — History range chips default to "last 30 days" but reset clears to unbounded
**Where:** H.VM `BuildRangeChips` (lines 214–219) defaults to last-30; `ResetFilters` (lines 313–333) sets `FromDate=ToDate=""` (unbounded "همه").
**Problem:** Initial state = last 30 days; after «پاک‌سازی فیلترها» the range becomes all-time, a different default than the screen opened with. Inconsistent mental model; an all-time reset can also pull a very large feed (bounded by paging, but totals scan everything, H.QRY `PageAsync` group-by).
**Fix:** Reset to the same last-30 default the screen opens with, or make the documented default explicit and consistent across open and reset.

### P2-18 — `ApplyPage` can trigger a recursive reload when the page overruns
**Where:** H.VM `ApplyPage` (lines 399–404): if `_page > totalPages`, sets `_page = totalPages` and calls `_ = ReloadAsync(resetPage:false)` from within the apply of a just-returned page.
**Problem:** A re-entrant reload driven from inside the render of a response. With the generation guard it is safe from stale overwrite, but it issues an extra full round-trip (count + page) whenever the page index overshoots (e.g., after shrinking the result set via a filter). It can also briefly render the over-page before correcting.
**Fix:** Clamp `_page` to `totalPages` **before** issuing the query (in `ReloadAsync`, after computing totals is not possible, so clamp on filter-changes that can shrink the set, and in `NextPage` guard already prevents overrun). Prefer returning the clamped page from the service, or compute the clamp pre-query from the previous total.

### P2-19 — Inventory `IsBusy` toggled without try/finally; stuck spinner on failure
**Where:** I.VM `LoadAsync` (lines 116–133) and `ReloadAsync` (lines 386–399) set `IsBusy = true` then `false` with awaits in between and **no try/finally**.
**Problem:** If `GetCategoriesAsync`/`GetProductsAsync` throws (they return `[]` on typed failure today, but a `ServiceException` from the context build in other paths could throw), `IsBusy` is never reset and the screen is stuck busy. H.VM correctly uses try/finally (lines 372–392); Inventory does not.
**Fix:** Wrap in try/finally to always clear `IsBusy`; route failures to the load-state block (see P2-9).

### P2-20 — `using` directives inconsistent; History files import `System.*` explicitly while others rely on implicit usings
**Where:** H.VM/H.ITEM/H.PRES import `System`, `System.Collections.Generic`, `System.Linq`, etc. explicitly (H.VM lines 1–4) though the project has `ImplicitUsings enable` (`Directory.Build.props`); Inventory files do not.
**Problem:** Minor standards inconsistency — redundant explicit usings in the History files that implicit usings already provide, diverging from the Inventory files' style. Not a bug; a consistency/cleanliness item for a codebase that elsewhere relies on implicit usings.
**Fix:** Remove the redundant explicit `System.*` usings in the History files to match the implicit-usings convention used across the rest of `Pages/**`.

---

## Cross-cutting themes (fix once, clears many)

1. **On-hand is movement-based, but three write surfaces pretend to edit it directly** (P0-1, P0-2, P1-7, P1-11). Decide one rule — *stock only ever changes through a reasoned `StockMovement`* — and make every UI control either read-only or delta-emitting. This single decision closes the two P0 data-loss findings and hardens the adjust flow.
2. **The feed stores IDs, the UI needs names** (P1-2, P1-3, P2-2, P2-8). Resolve display names once at write time in `ActivityFeedWriter`/mapper (and the rebuild path), store them, index them. This fixes the detail line, the CSV, the search, and the per-row JSON parsing together.
3. **Client-side load-all-then-filter vs. server-side filter/page** (P1-1, P2-4, P2-6, P2-11). The backend already supports server-side filter/sort/page for both catalog and movements; adopt it and the memory, staleness, and refetch findings collapse.
4. **"نمونه طراحی" / fake-success toasts on a real backend** (P0-3, P1-9, P2-15). Remove the mock suffix, guard every mutation with try/catch → typed danger toast, and delete or wire the settings stubs — exactly the Boarding/Sales X09/X11/X12 cleanup already proven in this repo.
5. **Unobserved async failures** (P2-9, P2-10, P2-19). Every reload/mutation must be observed (load-state tracker or try/catch), generation-guarded, and leave `IsBusy` consistent.

## Suggested remediation order (to reach a clean bill)

1. P0-1, P0-2, P0-3 — stop silent data loss and false success (data integrity first).
2. P1-5, P1-6, P2-14, P2-13 — make product create/edit persist exactly what the user entered.
3. P1-2, P1-3, P2-2, P2-8 — make the ledger and its export human-readable and searchable.
4. P1-1, P2-4, P2-6, P2-11, P2-10, P2-19 — server-side data path + resilience.
5. P1-7, P1-11, P1-4 — correct, atomic, fully-reasoned stock adjustments.
6. P1-8, P1-9, P2-* remainder — summary scoping, settings, and the standards/cleanup tail (`x:CompileBindings`, dedupe, usings).

Clearing items 1–6 in order makes these three screens behave exactly as their UI promises — persisted, accurate, readable, and resilient — which is the precondition for a perfect score on any functional, data-integrity, performance, or standards test.
