# Plan — Inventory / Product / History audit remediation (2026-10-03)

> **Self-contained execution plan.** A fresh model must implement every task below **without
> re-auditing the repo**. All file paths, verified current code behavior, root cause, chosen fix,
> and acceptance criteria are embedded here. Read §0 once, then execute tasks top-to-bottom.
>
> **🔒 LOCKED RULE — push after EVERY task:** after completing **each** task below, immediately run
> `scripts/git/push.sh "type(scope): message"`. **Not pushing = project death.**
> - Exit `3` (no credentials) → call the `setup_github_environment` tool once, then re-run the same command.
> - Exit `2` (rebase conflict, repo left clean) → resolve preferring remote, then re-run.
> - Work on branch `genspark_ai_developer` (use `scripts/git/push.sh -b genspark_ai_developer "msg"` on the FIRST push to switch/create it; subsequent pushes stay on it).
>
> **If a heavy fix or a large block of code is written mid-task**, push it immediately with a clear,
> understandable message — do not batch it with later work.
>
> **Source of this plan:** `docs/INVENTORY_PRODUCT_HISTORY_AUDIT_2026-10-03.md` (34 findings: 3 P0, 11 P1, 20 P2).
> **The final task (T20) deletes that audit file only.** Do **not** touch any other docs/roadmap files.

---

## 0. Context (read once, do not re-derive)

### 0.1 App + architecture
- **App:** Animora — self-hosted veterinary clinic management + accounting desktop app. .NET 10,
  Avalonia + MVVM (CommunityToolkit.Mvvm), Persian/RTL UI, Jalali calendar, local **encrypted SQLite**,
  offline-first (no network server wired yet).
- **Scope of this plan:** three screens — **Inventory (انبار و کالا)**, **Product modals
  (کالای جدید / جزئیات کالا / تغییر موجودی)**, **History unified ledger (تاریخچه)** — plus their
  Contracts, Client.Services, Application use cases, Domain, and SQLite queries where a fix requires it.
- **Backend phase state:** Inventory/Purchasing = **C06 done**, History = **C10 done** (real backend).
  Absent server/Datasync (Tracks D–E) is **not** a defect. A success toast that claims an action
  happened when it did not **IS** a defect.

### 0.2 THE ONE ARCHITECTURAL RULE (never break)
Data flows: **View → ViewModel → `Animora.Contracts` interface (constructor DI) → real
`Animora.Client.Services` impl → `Animora.Application` dispatch (permission/validation/journal) →
`Animora.Domain` + `Animora.Persistence.Sqlite`.**
- ViewModels depend **only** on Contracts interfaces — never on EF/SQL, a concrete service, or a DI scope.
- **No sample/mock/`if(design-time)` branching in `Pages/**`.** Seed data lives in Persistence seeders.
- To add a data need: extend the interface+DTO in `Contracts/<Area>/`, implement in the matching
  `Client.Services/<Area>/` over the dispatcher, register both in `src/Animora.Desktop/App.axaml.cs`.
- A toast must never claim success unless the command returned success.

### 0.3 Build / verify (focused only — NO full test runs, NO long benchmarks)
- Fast Desktop build: `scripts/sandbox/build.sh` → must be **0 warnings / 0 errors**.
- Whole solution: `scripts/sandbox/build.sh full`. Smoke run (Xvfb): `scripts/sandbox/run.sh` → prints `SMOKE RUN OK`.
- **Rule:** build (fast) after a task that changed compiled code to confirm 0/0. Do **not** run the full
  test suite or benchmarks. Smoke-run only if you touched startup/DI/shell.

### 0.4 THE SEAM FAILURE MECHANISM (shared root cause behind P0-3, P1-6, P2-9) — understand first
The Inventory seam converts **every** failed `Result` into a thrown exception. In **I.SVC**
`InventoryQueryService.SendAsync` (lines 256–265):
```csharp
var context = await provider.BuildCommandContextAsync(...);
if (context.IsFailure) throw new ServiceException(context.Error);
var result = await dispatcher.SendAsync(command, context.Value!);
if (result.IsFailure) throw new ServiceException(result.Error);   // ← throws on ANY command failure
```
A ViewModel awaiting this inside an **unguarded `async` RelayCommand** lets `ServiceException` escape the
async state machine → UI dispatcher unhandled → crash-recovery path. Mutations currently fire
`_dialogs.Close()` + success toast **after** the `await` with **no try/catch**, so:
(a) on the happy path the toast lies («نمونه طراحی» suffix on a real persist); (b) on failure the app
faults silently instead of showing a friendly message. **`ServiceException`** carries the domain error
(it wraps `result.Error` which has a `.Code`). Guard pattern to use everywhere:
```csharp
try { await _service.XAsync(...); _dialogs.Close(); _toast.Show(success); }
catch (ServiceException ex) { _toast.Show(MapError(ex), Tone.Danger); } // keep modal OPEN on failure
```

### 0.5 Path aliases (confirmed to exist — do NOT re-search)
| Alias | Path |
|---|---|
| I.VM | `src/Animora.Desktop/Pages/Inventory/InventoryPageViewModel.cs` |
| I.ITEM | `src/Animora.Desktop/Pages/Inventory/InventoryItemViewModels.cs` |
| I.PRES | `src/Animora.Desktop/Pages/Inventory/InventoryPresentation.cs` |
| I.SET | `src/Animora.Desktop/Pages/Inventory/InventorySettingsItemViewModels.cs` |
| I.VIEW | `src/Animora.Desktop/Pages/Inventory/InventoryPageView.axaml` |
| I.SETVIEW | `src/Animora.Desktop/Pages/Inventory/InventorySettingsView.axaml` |
| P.NEW | `src/Animora.Desktop/Pages/Inventory/NewProductModalViewModel.cs` |
| P.NEWVIEW | `src/Animora.Desktop/Pages/Inventory/NewProductModalView.axaml` |
| P.MOD | `src/Animora.Desktop/Pages/Inventory/ProductModalViewModel.cs` |
| P.MITEM | `src/Animora.Desktop/Pages/Inventory/ProductModalItemViewModels.cs` |
| P.MODVIEW | `src/Animora.Desktop/Pages/Inventory/ProductModalView.axaml` |
| P.ADJ | `src/Animora.Desktop/Pages/Inventory/StockAdjustModalViewModel.cs` |
| P.ADJVIEW | `src/Animora.Desktop/Pages/Inventory/StockAdjustModalView.axaml` |
| I.CONTR | `src/Animora.Contracts/Inventory/IInventoryQueryService.cs` + `InventoryModels.cs` |
| I.SVC | `src/Animora.Client.Services/Inventory/InventoryQueryService.cs` |
| I.MAP | `src/Animora.Client.Services/Inventory/InventoryMapping.cs` |
| I.CMD | `src/Animora.Application/UseCases/Inventory/InventoryCommands.cs` |
| I.QRY | `src/Animora.Persistence.Sqlite/Queries/InventoryQueries.cs` |
| DOM.REASON | `src/Animora.Domain/Inventory/AdjustmentReasons.cs` |
| DOM.VARIANT | `src/Animora.Domain/Inventory/ProductVariant.cs` |
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
| DI | `src/Animora.Desktop/App.axaml.cs` |

### 0.6 Verified facts (checked against live code — use directly, no re-derivation)
- **`ServiceException`** is thrown by I.SVC `SendAsync` on both context + command failure. It wraps an
  error object with `.Code` (and message). The reference crash/guard precedent is Boarding/Sales
  **X09/X11/X12** (toast cleanup + try/catch) and Patients `IsAccessFailure`/`CommandFailureMessage`.
- **`DOM.REASON` (`AdjustmentReasons`) current codes:** `Count`, `Damage`, `Expiry`, `Theft`,
  `Correction` — in an `All` list (line 14). **There is NO `OpeningBalance` code yet** → T1 must add it.
- **`DOM.VARIANT.DefaultAttributeName` = `"DEFAULT"`** (line 47). Mapping treats `DEFAULT` as "no dimension".
- **`«نمونه طراحی»` mock-suffix toasts (8 occurrences) confirmed at:**
  - P.NEW line **161** `"کالا ساخته شد (نمونه طراحی)"`
  - P.MOD line **526** `"تغییرات ذخیره شد (نمونه طراحی)"`, line **535** `"کالا حذف شد (نمونه طراحی)"`
  - P.ADJ line **94** `"موجودی به‌روزرسانی شد (نمونه طراحی)"`
  - I.VM line **404** `"افزودن دسته — نمونه طراحی"`, **407** `"تنظیمات ذخیره شد — نمونه طراحی"`,
    **429** `"دسته حذف شد — نمونه طراحی"`
  - I.SETVIEW line **156** static descriptive text `"...(نمونه طراحی)."` (settings tab subtitle)
- **All 4 modal mutations are unguarded** (`_dialogs.Close()` + toast after `await`, no try/catch):
  P.NEW `CreateAsync` (159–162), P.MOD `SaveAsync` (525–526) + `DeleteAsync` (534–535), P.ADJ `SaveAsync` (~81–94).
- **I.SVC `CreateProductAsync`** (131–153) discards variant stock (no `ProductVariantInput.Stock`; no opening movement).
- **I.SVC `AttributeNameOf`** (202–206) ignores the `variants` param (P2-13: documented fallback not implemented).
- **I.SVC `AdjustStockAsync`** (158–181) reads on-hand via `GetDetailAsync`, computes `delta = target − current`,
  always uses `BridgeAdjustReason = Correction` (line 29). Read-modify-write race (P1-7) + reason loss (P1-11).
- **I.SVC `GetProductsAsync`** (31–47) sends `ProductCatalogQuery(null, null, false, 1, 200)` — server-side
  search/category/lowStock filters passed as null/null/false (P1-1). `MaxPageSize = 200` (line 22).
- **I.SVC `GetHistoryAsync`** (113–129) + **`AddVariantAsync`** (85–111) implemented but no Desktop caller (P1-10).

### 0.7 Execution notes
- Each task = one logical change + one push. Tasks are ordered by the audit's remediation order
  (data integrity → persist-what-user-typed → ledger readability → data path/resilience → stock correctness → tail).
- A finding touching contract/command signatures (P0-1, P0-2, P1-5, P1-7, P1-11) crosses layers
  (Contracts → Service → Application command/handler → Domain mutator). Keep the UoW atomic.
- When a P2 is fully subsumed by an earlier task, the task note says so and you just verify.
- Status legend: `[ ]` todo · `[~]` in progress · `[x]` done. Update the box when you finish + push.

---

## TASKS

### T1 — [x] Opening stock persists on product create (P0-1, P2-14)
**Findings:** P0-1 (new product's initial stock silently discarded), P2-14 (multi-variant create collapses to one DEFAULT variant, options dropped).
**Root cause (verified):**
- P.NEW `CreateAsync` parses «موجودی اولیه» into `ProductVariant.Stock`, but I.SVC `ToInputs` maps to
  `ProductVariantInput` which has **no stock field**; `CreateProductCommandHandler` records no opening movement → every new product is on-hand=0.
- P.NEW `CreateAsync` (lines 120–134): when `HasVariants`, it builds `attributeOptions` from a comma split and
  sends `variants = []`; I.SVC `CreateProductAsync` (136–143) then synthesizes a single `DEFAULT` variant,
  **ignoring the options entirely** → «چند‌تنوعی» silently makes a single-unit product.
**Fix:**
1. Add `OpeningBalance` to DOM.REASON `AdjustmentReasons` (new `public const string OpeningBalance = "OpeningBalance";` + add to the `All` list).
2. Make opening stock an explicit first movement. Preferred: add optional `OpeningStockScaled` to `ProductVariantInput`
   and have `CreateProductCommandHandler` emit the seed `StockMovement` (reason `OpeningBalance`) in the **same UoW**
   as product create (one atomic commit). Carry stock from P.NEW → `NewProductRequest`/`ProductVariant` → I.SVC `ToInputs` → command.
   Acceptable fallback if same-UoW is too invasive: after `CreateProductCommand` succeeds, for each variant with
   stock>0 issue `AdjustStockCommand(productId, variantId, IntToScaled(stock), AdjustmentReasons.OpeningBalance)` inside the same service call.
3. Multi-variant: in I.SVC `CreateProductAsync`, when `request.Variants` is empty but `AttributeOptions` is non-empty,
   expand options into one `ProductVariantInput` per option (sharing `AttributeName`, each a unique SKU). OR build the
   variant list client-side in P.NEW from the options before calling the service.
**Guard:** P.NEW `CreateAsync` must also be wrapped in the §0.4 try/catch (shared with T4 — do it here if T4 not yet done, else confirm).
**Acceptance:** Creating a single-unit product with «موجودی اولیه»=N → detail shows on-hand=N and one `OpeningBalance` movement.
Creating a multi-variant product with M options → M variants persisted, each independently stockable.
**Build:** fast build 0/0. **Push:** `scripts/git/push.sh -b genspark_ai_developer "fix(inventory): persist opening stock + multi-variant on product create (P0-1,P2-14)"`

### T2 — [x] Product modal single-unit / variant stock edit (P0-2)
**Finding:** P0-2 — Info-tab stock stepper (`SingleStockText`, `IncrementSingle`/`DecrementSingle`) and Variants-tab
card `StockText` are read into `ProductVariant.Stock` and sent via `SaveProductInfoAsync`, but
`UpdateProductInfoCommand.ApplyVariants` (I.CMD 120–219) has **no stock field / no movement** → stock edits are
silently discarded while the toast claims success. Only P.ADJ actually moves stock.
**Chosen fix (preferred — matches architecture "on-hand is a projection"):** make the stock controls on the
Info/Variants tabs **read-only displays**. Bind them one-way; remove the steppers/editable stock from P.MOD + P.MODVIEW;
direct the user to «تغییر موجودی» (P.ADJ) for stock changes.
**Alternative (only if inline editing must stay):** in `SaveProductInfoAsync`, diff each variant's edited stock
against its loaded on-hand and emit an `AdjustStockCommand` per non-zero delta (as P.ADJ does) in the same call.
**Do NOT** let `SaveProductInfoAsync` imply it persisted stock when the command cannot.
**Acceptance:** Editing stock on Info/Variants tab + «ذخیره تغییرات» either changes nothing visible (read-only) or
actually moves stock with a movement recorded — never a silent no-op with a success toast.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(inventory): stop silent stock-edit loss in product modal (P0-2)"`

### T3 — [x] Reason selector + delta-native, atomic stock adjust (P1-11, P1-7)
**Findings:** P1-11 (every manual adjustment recorded as `Correction`, reason lost), P1-7 (absolute-target bridge
read-modify-write race + per-row scope/round-trip).
**Root cause (verified):** I.SVC line 29 `BridgeAdjustReason = Correction`; `AdjustStockAsync` reads current via
`GetDetailAsync` then sends `delta = target − current` → racy. P.ADJ offers no reason input and loops rows awaiting
one `AdjustStockAsync` per changed row (N reads + N commands).
**Fix:**
1. Add a reason selector to P.ADJ + P.ADJVIEW (options from DOM.REASON: Count/Damage/Expiry/Theft/Correction, plus
   OpeningBalance from T1 if appropriate). Carry it on `StockAdjustRequest` (I.CONTR) and pass through to
   `AdjustStockCommand.ReasonCode`. Keep `Correction` as the default.
2. Make the adjustment delta-native + atomic: either have the UI send the **delta** the stepper already captures, or
   add a `SetOnHandCommand` that computes the delta inside the handler's transaction against freshly-read on-hand
   (no client round-trip gap). Batch all variant deltas of one product into a single command/scope (not per-row).
**Acceptance:** Adjusting with reason=Damage records a `Damage` movement (not `Correction`); concurrent sale between
open and save cannot corrupt the absolute result; multi-variant adjust uses one scope.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(inventory): reasoned, atomic, delta-native stock adjust (P1-7,P1-11)"`

### T4 — [x] Remove «نمونه طراحی» + guard every mutation toast (P0-3)
**Finding:** P0-3 — all real C06 mutations still carry the Phase-18 mock suffix «نمونه طراحی» AND fire
`_dialogs.Close()`+toast after an unguarded `await` (see §0.4, §0.6).
**Fix (do for ALL of these):**
- Remove the «نمونه طراحی» suffix from every real mutation toast (verified locations in §0.6):
  P.NEW:161, P.MOD:526 & 535, P.ADJ:94. (I.VM 404/407/429 are category-settings stubs → handled in T14, not here;
  I.SETVIEW:156 static text → T14.)
- Wrap each mutation in the §0.4 try/catch: show a typed Persian danger toast on `ServiceException`, keep the modal
  **open** on failure, and never show success unless the command returned. Reuse the Patients
  `IsAccessFailure`/`CommandFailureMessage` precedent for mapping access/session errors; map known domain codes
  (e.g. `PRODUCT_SKU_DUPLICATE`) to specific Persian messages (pair with T5).
**Note:** If T1/T2/T3 already wrapped their own modal, just confirm here and finish the remaining ones.
**Acceptance:** Every create/save/delete/adjust toast is accurate (no mock suffix); a forced command failure shows a
danger toast and leaves the modal open instead of crashing.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(inventory): drop mock-suffix + guard mutation toasts (P0-3)"`

### T5 — [x] Product modal persists category, code, attribute name; drop editable glyph (P1-5, P1-6, P2-13, P2-12)
**Findings:** P1-5 (`ProductInfoUpdate.CategoryId/.Code/.Glyph/.AttributeName` edited in UI but `UpdateProductInfoCommand`
accepts only Name/Brand/Notes/prices/reorder/variants → category & code changes silently dropped; glyph is UI-derived),
P1-6 (variant SKU not uniqueness-guarded before send), P2-13 (`AttributeNameOf` ignores existing dimension → multi-variant
can collapse to DEFAULT), P2-12 (tone/icon/glyph derived from mutable names → rename re-skins).
**Fix:**
1. Extend `UpdateProductInfoCommand` (I.CMD) with `CategoryId` and `Sku`; apply in handler via a domain
   reclassify/re-sku mutator on `Product` (enforce SKU uniqueness invariant). Pass them from I.SVC `SaveProductInfoAsync`.
2. Remove the editable glyph control from P.MOD/P.MODVIEW (glyph is `I.MAP GlyphOf` from kind — editing is a no-op), OR
   persist a real glyph column. Preferred: remove the editable control.
3. P2-13: implement the documented `AttributeNameOf` fallback — when `requested` is blank, read
   `variants[0].AttributeName` before defaulting to `DEFAULT` (so a save without re-sending the attr name doesn't
   collapse a multi-variant product). Remove the unused `variants` param only if you drop the fallback (doc must match behavior).
4. P1-6: in P.MOD `SaveAsync`, validate SKU non-empty + intra-modal uniqueness before calling the service; show an inline
   field error. Pair with T4's try/catch so domain `PRODUCT_SKU_DUPLICATE` maps to a specific Persian message and keeps the modal open.
5. P2-12 (light): key category icon off an immutable id rather than display name (tone already uses id — keep).
**Acceptance:** Changing category/code + save actually changes them; blank-attr save on a multi-variant product keeps its
dimension; duplicate/blank SKU is caught client-side with an inline error before send.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(inventory): persist category/code/attr, guard SKU, drop glyph edit (P1-5,P1-6,P2-13,P2-12)"`

### T6 — [x] History feed stores resolved display names at write time (P1-2, P2-8, P2-2)
**Findings:** P1-2 (detail renders raw ULIDs instead of pet/owner/product names — the whole audit-trail value defeated),
P2-8 (`HistoryText.Detail` re-parses arg JSON per row per render on the UI thread), P2-2 (search `LIKE %term%` over the
opaque JSON blob is a full scan that matches nothing useful).
**Root cause (verified):** AF.MAP `Draft` stores language-neutral arg JSON with only IDs (e.g. `{"PetId":"01H...","OwnerId":"01H..."}`).
H.TEXT `Detail` (37–49) renders them verbatim; H.VM `RenderFeed` (447–451) calls it per row every render.
**Fix (fix once, clears three):**
1. Resolve display names **at write time**: AF.WRITE is the single write authority and runs inside the source aggregate's
   UoW, so names are available. Extend `ActivityEventDraft` with a small resolved-label map (e.g. `PetName`, `OwnerName`,
   `ProductName`) populated by AF.MAP from the event; store it. Avoid lazy per-row read-time lookups (N+1).
2. The rebuild path (`RebuildActivityFeedAsync`) must resolve identically for consistency.
3. H.TEXT `Detail` renders the resolved names (no per-row JSON parse); the feed row VM receives a ready string.
4. P2-2: add/populate a dedicated indexed searchable text column (title + resolved names + entity); H.QRY `Filter`
   (33–37) searches that indexed column instead of `DetailArgsJson.Contains(search)`. Drop the JSON `Contains`.
**Acceptance:** A history row's detail reads «بیمار: Rex، مالک: …» (names, not ULIDs); searching a pet name matches;
no per-row `JsonDocument.Parse` on render.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "feat(history): store+index resolved names at write time (P1-2,P2-8,P2-2)"`

### T7 — [x] History CSV export matches the on-screen ledger (P1-3)
**Finding:** P1-3 — H.CSV `WriteAsync` (19–22) writes `OccurredUtc.ToString("yyyy/MM/dd")`+`"HH:mm"` (Gregorian, UTC,
not clinic-local Jalali), raw `DetailArgsJson`, and `TitleKey`; H.VM `ExportCsvAsync` (468–487) then does a fragile
post-hoc string `Replace` of the header + 14 known title keys over the whole CSV body. Exported ledger ≠ on-screen ledger.
**Fix:** Do the formatting **once, server-side, in the mapper** — not by string replacement in the VM. Have
`ActivityExportQuery` project already-localized rows (Jalali local date/time via the clinic `TimeZoneInfo` +
`PersianCalendar`, translated title, resolved detail from T6, translated status), OR pass `TimeZoneInfo` + a formatting
delegate into H.CSV `CsvWriter`. **Delete the `Replace` chain** in H.VM. Keep the UTF-8 BOM (already correct for Excel).
**Depends on:** T6 (resolved detail reused here).
**Acceptance:** Exported CSV dates are clinic-local Jalali, title/status translated, detail shows names — identical to screen.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(history): server-side Jalali/translated CSV export, drop Replace chain (P1-3)"`

### T8 — [x] History day-group net computed server-side over full filtered set (P1-8, P2-7)
**Findings:** P1-8 (summary bar uses `page.IncomeTotal`/`ExpenseTotal` over the whole filtered set — correct — but each
day-group header's net in H.VM `RenderFeed` (452–453) is computed from only the current page's rows → a day split across
pages shows a partial net read as the day's true net), P2-7 (per-group two filtered `Sum`s allocate closures per render).
**Fix:** Compute day-group nets **server-side over the full filtered set** (page-independent) in H.QRY/H.SVC and project
them onto the page; H.VM just renders. If server-side per-day is deferred, scope the header label explicitly as
"net of shown rows" — but server-side is the correct audit answer. Fixes P2-7's double-pass too (single fold or server agg).
**Acceptance:** A day with more events than one page shows the same full-day net on every page it appears on.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(history): page-independent day-group nets server-side (P1-8,P2-7)"`

### T9 — [ ] Inventory server-side filter/sort/page (P1-1, P2-4, P2-6, P2-11)
**Findings:** P1-1 (I.VM loads up to 200 products then filters client-side per keystroke/chip; backend already supports
server-side `search`/`categoryId`/`lowStockOnly` but they're passed null/null/false → product #201 invisible),
P2-4 (I.QRY `GetCatalogAsync` scans 1000 rows then slices page in memory), P2-6 (rows rebuild all derived brushes/strings
per keystroke), P2-11 (`RefreshOnDataChange` reloads whole catalog on any Sales/Cashier/Purchasing change, even off-screen).
**Fix (the audit's "fix once, clears many"):**
1. Push `_catFilter`, `_stockFilter`, normalized search term into `ProductCatalogQuery` (I.SVC `GetProductsAsync`) and
   re-query on change (debounce already exists at I.VM ~line 44). Add a bounded page window + «نمایش بیشتر»/pager mirroring
   the Cashier list (X29 precedent). Keep only the current page in `FilteredProducts`.
2. I.QRY: when `lowStockOnly=false`, apply `Skip/Take` in SQL (`ToPagedAsync`, already used by
   `GetMovementHistoryAsync`); load on-hand only for the page's products. Keep scan-then-filter only for low-stock.
3. P2-6: once filtering is server-side, rows change only on page change; additionally hoist per-product derivations
   (status/totals/price range) into the read model/mapper so the row VM is a thin projection.
4. P2-11: gate `RefreshOnDataChange` on page visibility (dirty flag consumed on `OnNavigatedTo` when off-screen);
   reload only the current page.
**Note:** This also unlocks P1-9's low-stock filter usefulness and feeds T10.
**Acceptance:** 201+ products are reachable via pager; filtering/search re-queries the server; off-screen Sales/Cashier
changes don't trigger a full catalog refetch.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "perf(inventory): server-side filter/sort/page + visibility-gated refresh (P1-1,P2-4,P2-6,P2-11)"`

### T10 — [ ] Inventory low-stock integer compare + reachable filter (P2-3)
**Finding:** P2-3 — I.QRY `InventoryStockQuery.LowStockProducts` (23–28) runs a correlated per-variant
`StockOnHandSnapshot` subquery with `(double)` casts on both sides (reorder stored as TEXT) → perf + float-compare
correctness smell; also effectively dead until T9 pushes `lowStockOnly`.
**Fix:** Store the reorder level in a numerically-comparable scaled integer column (consistent with `OnHandScaled`) so
the compare is integer `<=` with no cast; OR precompute a per-variant `IsLow` flag in the snapshot maintainer
(`StockSnapshotUpdater`) and filter on an indexed boolean. Then confirm T9 actually passes `lowStockOnly=true`.
**Depends on:** T9 (wiring). **Acceptance:** Low-stock chip returns correct products via an integer/boolean indexed filter, no float cast.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "perf(inventory): integer/indexed low-stock filter (P2-3)"`

### T11 — [ ] Product-modal History tab paging + refresh after adjust (P2-5, P1-4, P1-10)
**Findings:** P2-5 (`GetDetailAsync` caps movement history at 100 with no "load more" → audit tab silently truncated),
P1-4 (modal History tab built once in `LoadAsync`/`BuildHistory`, no refresh hook; after a stock adjust the open modal is
stale), P1-10 (`GetHistoryAsync` + `AddVariantAsync` implemented in I.SVC but never called from Desktop — dead surface).
**Fix:**
1. Use the paged movement query (`GetHistoryAsync` / `GetMovementHistoryAsync`) for the modal History tab with a
   «نمایش بیشتر» — this adopts the dead `GetHistoryAsync` (resolves half of P1-10). At minimum show a "showing latest 100" notice.
2. Refresh the open modal after a stock change: reload detail on History-tab activation (re-call `GetDetailAsync` when
   `IsHistoryTab` becomes true) OR subscribe the open modal to `IDataChangeNotifier(DataDomain.Inventory)` and rebuild
   `BuildHistory`/`RebuildKpis` (the page VM already uses `RefreshOnDataChange` — mirror it in the modal).
3. P1-10 `AddVariantAsync`: either adopt it for the Variants tab «تنوع دستی» (immediate persist, matching the contract's
   documented intent) OR remove it + `GetHistoryAsync` from contract/service. **Do not leave two parallel append paths.**
   Decide: if T5 kept save-based variant append working, prefer removing `AddVariantAsync` to cut dead surface.
**Acceptance:** Modal history pages past 100; adjusting stock from the list updates an already-open modal's timeline/KPIs;
no dead seam method remains unreferenced.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "feat(inventory): paged+refreshing modal history, resolve dead seam (P2-5,P1-4,P1-10)"`

### T12 — [ ] Inventory resilience: stale-guard, try/finally IsBusy, observed reloads (P2-10, P2-19, P2-9)
**Findings:** P2-10 (History has a `_generation` stale-response token; Inventory has none → overlapping reloads can land
stale data), P2-19 (I.VM `LoadAsync`/`ReloadAsync` toggle `IsBusy` without try/finally → stuck spinner on throw),
P2-9 (`_ = ReloadAsync()` fire-and-forget discards across I.VM + H.VM swallow `ServiceException`; I.VM `ReloadAsync` not
wrapped in `TrackLoadAsync`; H.VM `ReloadAsync` has try/finally but no catch; also H.VM `ApplyPage` recursive reload P2-18).
**Fix:**
1. Add a generation-token guard to I.VM `LoadAsync`/`ReloadAsync` (mirror H.VM lines 54/369/379–391) — discard a response
   whose token is superseded.
2. Wrap I.VM `LoadAsync`/`ReloadAsync` in try/finally so `IsBusy` always clears (H.VM already does this).
3. Route every reload through the page's load-state tracker (`TrackLoadAsync`) or try/catch → danger toast; never discard
   a throwing task without an observer (Sales/Cashier X09 guard pattern). Give H.VM `ReloadAsync` a catch.
4. P2-18: clamp `_page` to `totalPages` pre-query on filter changes that shrink the set, avoiding `ApplyPage`'s re-entrant
   `_ = ReloadAsync(resetPage:false)` extra round-trip (include here as it's the same resilience area).
**Acceptance:** Overlapping Inventory reloads never show stale data; a failing reload clears `IsBusy` and surfaces a danger
toast; no unobserved faulting task; page overrun doesn't trigger an extra round-trip.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(inventory,history): stale-guard, try/finally IsBusy, observed reloads (P2-9,P2-10,P2-18,P2-19)"`

### T13 — [ ] Inventory category settings: wire or remove the stubs (P1-9, P2-15)
**Findings:** P1-9 (the whole «تنظیمات کالاها» tab — add/rename/retone/delete category + 3 toggles (low-stock alert,
expiry tracking, auto-code) — is design-only: every action toasts «نمونه طراحی» or mutates a throwaway VM;
`DeleteCategory` guards "has products" client-side then toasts success without deleting), P2-15 (toggles/tone are pure
view state with no backing store / no settings DTO/contract at all).
**Decision + fix (pick one, do it fully):**
- **Option A (wire real):** add category CRUD commands + a user-preferences-backed toggle set (C10 `UserPreferences`
  precedent) with a real inventory-settings contract; wire I.VM `AddCategory`/`SaveSettings`/`DeleteCategory`/`PickTone`
  and I.SET toggle/tone VMs to it.
- **Option B (remove, preferred if category mgmt is out of scope for this phase — Boarding X11 precedent):**
  hide/disable the controls and **remove the misleading success toasts** (I.VM 404/407/429 + I.SETVIEW:156 static text).
**Note:** This owns the remaining «نمونه طراحی» occurrences not handled by T4 (I.VM 404/407/429, I.SETVIEW:156).
**Acceptance:** No control looks live but does nothing; no fake success toast; if wired, settings persist and are read back.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(inventory): wire-or-remove category settings stubs (P1-9,P2-15)"`

### T14 — [ ] Dedupe stock-status derivation (P2-16)
**Finding:** P2-16 — the out/low/ok status rule is written twice: I.VM `StockStatus` (217–223) and I.ITEM ctor (162–165),
with the same literals → list filter and row badge can drift.
**Fix:** Extract a single helper (on the read model or a shared static) used by both the filter predicate and the row VM.
**Note:** If T9 already hoisted status into the read model/mapper, this may be mostly done — verify both call sites use the one source.
**Acceptance:** One status rule, two call sites; changing the threshold changes both filter and badge together.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "refactor(inventory): single stock-status helper (P2-16)"`

### T15 — [ ] History range reset consistent with open default (P2-17)
**Finding:** P2-17 — H.VM `BuildRangeChips` (214–219) opens on "last 30 days" but `ResetFilters` (313–333) sets
`FromDate=ToDate=""` (unbounded "همه") → reset ≠ open default; all-time reset can pull a large feed.
**Fix:** Make `ResetFilters` restore the same last-30 default the screen opens with (or make the default explicit and
identical across open + reset). Prefer resetting to last-30.
**Acceptance:** «پاک‌سازی فیلترها» returns the range to exactly the state the screen opened in.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(history): reset restores last-30 default (P2-17)"`

### T16 — [ ] Add x:CompileBindings to the three screens' XAML (P2-1)
**Finding:** P2-1 — none of I.VIEW, P.* views, H.VIEW set `x:CompileBindings` (grep: zero), so bindings fall back to
reflection and typos fail silently at runtime. `x:DataType` is already present.
**Fix:** Add `x:CompileBindings="True"` at each `UserControl`/`DataTemplate` root in: I.VIEW, I.SETVIEW, P.NEWVIEW,
P.MODVIEW, P.ADJVIEW, H.VIEW. Then fast-build and fix any binding errors surfaced (highest value on hot row item templates).
**Acceptance:** All three screens compile with compiled bindings on; build is 0 warnings / 0 errors.
**Build:** fast 0/0 (expect to iterate here — surfaced binding errors are the point). **Push:** `scripts/git/push.sh "perf(inventory,history): x:CompileBindings on all three screens (P2-1)"`

### T17 — [ ] Remove redundant explicit System.* usings in History files (P2-20)
**Finding:** P2-20 — H.VM/H.ITEM/H.PRES import `System`, `System.Collections.Generic`, `System.Linq` etc. explicitly
(e.g. H.VM lines 1–4) though `Directory.Build.props` has `ImplicitUsings enable`; Inventory files don't. Consistency only.
**Fix:** Remove the redundant explicit `System.*` usings in the History files to match the implicit-usings convention used
across the rest of `Pages/**`. Keep any non-implicit namespace imports.
**Acceptance:** History files rely on implicit usings; build 0/0.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "style(history): drop redundant explicit System usings (P2-20)"`

### T18 — [ ] Final full-solution verification pass
**What:** Confirm the three screens are clean end-to-end before closing out.
**Do:** `scripts/sandbox/build.sh full` → 0/0. If any screen touched startup/DI/shell during T1–T17, `scripts/sandbox/run.sh`
→ expect `SMOKE RUN OK`. Re-scan for leftover «نمونه طراحی» in Inventory (`grep -rn "نمونه طراحی" src/Animora.Desktop/Pages/Inventory`
should be empty) and confirm no unobserved `_ = …Async()` without a tracker/catch remains in I.VM/H.VM.
**If a fix is needed here**, apply it and push with a clear message describing it (heavy fixes get their own push per the LOCKED RULE).
**Acceptance:** Full solution builds 0/0; smoke run OK if applicable; no mock suffix left in scope.
**Push:** `scripts/git/push.sh "chore(inventory,history): final verification pass for the audit remediation"`

### T19 — [ ] Open / update the Pull Request
**What:** Ensure the `genspark_ai_developer` branch has an up-to-date PR to `main` summarizing all findings resolved
(3 P0, 11 P1, 20 P2). Provide the PR URL to the user. (Pushes already happened per task; this is the PR surface.)
**Acceptance:** A single clean PR exists with a comprehensive description; URL shared.

### T20 — [ ] FINAL TASK — delete the source audit file
**What:** Delete **only** `docs/INVENTORY_PRODUCT_HISTORY_AUDIT_2026-10-03.md`. Do **not** touch any other docs, the
roadmap, or this plan's siblings.
**Do:** `git rm docs/INVENTORY_PRODUCT_HISTORY_AUDIT_2026-10-03.md`
**Push:** `scripts/git/push.sh "docs: remove actioned inventory/product/history audit (all 34 findings resolved)"`
**Acceptance:** The audit file is gone from the repo; all 34 findings have landed in earlier tasks.

---

## Finding → Task coverage map (sanity check — every one of the 34 is assigned)
- **P0-1** → T1 · **P0-2** → T2 · **P0-3** → T4 (+ stub toasts in T13)
- **P1-1** → T9 · **P1-2** → T6 · **P1-3** → T7 · **P1-4** → T11 · **P1-5** → T5 · **P1-6** → T5 ·
  **P1-7** → T3 · **P1-8** → T8 · **P1-9** → T13 · **P1-10** → T11 · **P1-11** → T3
- **P2-1** → T16 · **P2-2** → T6 · **P2-3** → T10 · **P2-4** → T9 · **P2-5** → T11 · **P2-6** → T9 ·
  **P2-7** → T8 · **P2-8** → T6 · **P2-9** → T12 · **P2-10** → T12 · **P2-11** → T9 · **P2-12** → T5 ·
  **P2-13** → T5 · **P2-14** → T1 · **P2-15** → T13 · **P2-16** → T14 · **P2-17** → T15 · **P2-18** → T12 ·
  **P2-19** → T12 · **P2-20** → T17

## Cross-cutting themes (fix once, clears many — from the audit)
1. **On-hand is movement-based; 3 write surfaces pretend to edit it** → T1, T2, T3 (one rule: stock changes only via a reasoned `StockMovement`).
2. **Feed stores IDs, UI needs names** → T6 (clears detail, CSV search, per-row JSON parse) + T7.
3. **Client load-all-then-filter vs server-side filter/page** → T9 (+ T10) collapses memory/staleness/refetch.
4. **«نمونه طراحی» / fake-success on a real backend** → T4 + T13.
5. **Unobserved async failures / stuck busy / stale** → T12.
