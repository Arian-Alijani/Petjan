# Plan — Sales/Cashier + History + Patients UX fixes (2026-10-06)

> Self-contained worklist. A fresh model can implement every task from THIS FILE ALONE —
> all repo investigation is already done and distilled below. **Do not re-investigate what is
> already stated here.** Read a file only when the task's "Edit" step points you at it.

## Hard rules (LOCKED)
- After **each** completed task below → run `scripts/git/push.sh "type(scope): [Task N] ..."`.
  If push is skipped the project fails. If push exits `3` → call `setup_github_environment` tool once, re-run.
- Branch: `genspark_ai_developer` (already checked out, synced on `origin/main`).
- Build after code changes: `scripts/sandbox/build.sh` (fast mode; must be 0 errors). **No full/long test runs.**
- Targeted edits only. No unrelated refactors. Keep RTL/Persian conventions.
- The **last** task is to delete THIS file only (leave the other 3 Plan/*.md untouched).

## Repo orientation (already discovered — don't re-derive)
- Desktop UI (Avalonia, MVVM, CommunityToolkit): `src/Animora.Desktop/Pages/...`
- Client service seam (dispatch + mapping): `src/Animora.Client.Services/...`
- Use cases / behaviors / activity mapping: `src/Animora.Application/...`
- Persistence (EF Sqlite, queries, writer): `src/Animora.Persistence.Sqlite/...`
- `scripts/sandbox/build.sh` = memory-safe build. `scripts/windows/clean-run.ps1 -FreshTest` = Windows-only
  clean DB launch (**cannot run in this Linux sandbox** — reason runtime behavior from code).

---

## TASK 1 — History recording system  🔴 highest, investigate-first
**User report:** With `clean-run.ps1 -FreshTest` (empty DB, `SeedSampleData=false`, user creates all data
from the UI), nothing shows in History. Invoices must appear on BOTH the main **History** page AND the
in-page **History tab** of Sales & Cashier. Must also work for vaccine/boarding/etc. for every scenario.

### Architecture already traced — the backend chain is CORRECT (verified end-to-end)
Two independent data sources feed "history":
1. **Main History page** → `IActivityFeedQueries` → table **`ActivityEvent`** (`[LocalOnly]`, migration
   `017_ActivityFeed`). Written by `ActivityFeedHandler` for 14 domain events.
2. **Sales/Cashier History tab** → `ISalesQueryService.GetHistoryAsync` →
   `SalesQueries.GetHistoryAsync` (UNION of `Payment` + non-Draft `Invoice`). NOT the ActivityEvent table.

Write path (confirmed sound):
- Posting happens inside the **Pay** flow, not a separate "post" button:
  `src/Animora.Client.Services/Payments/InvoicePaymentCore.cs` `PayAsync` → `send(new PostInvoiceCommand)`
  then `send(new RegisterPaymentCommand)`. **Each `send()` = a separate DI scope** (`scopes.CreateAsyncScope()`
  in `CashierService.SendAsync` / `SalesQueryService`), each its own `AnimoraLocalDbContext` + own transaction.
- `Dispatcher` runs the full pipeline (`ApplicationRegistrationBuilder.Compose` wraps every behavior):
  `TransactionBehavior` (wraps handler in `uow.ExecuteAsync`) → `CommandJournalBehavior` → handler.
- `PostInvoiceCommandHandler` loads invoice via `InvoiceRepository.FindAsync` using **`.AsTracking()`**
  (events ARE collected), calls `invoice.Post(clock)` which raises `InvoicePostedEvent` (Invoice.cs ~L531).
- `LocalUnitOfWork.RunAsync`: SaveChanges → dispatch each domain event → handler writes → SaveChanges → commit.
- `ActivityFeedHandler : IDomainEventHandler<DomainEvent>` is registered (LocalStoreServiceCollectionExtensions
  ~L181); `DomainEventDispatcher` invokes base-type handlers. It maps via `ActivityFeedMapper.Map` →
  `ActivityFeedWriter.WriteAsync` → stages an `ActivityEvent` row in the same tracked context.
- Tenant is consistent everywhere: `LocalInstallationState.TenantId` returns `"T-LOCAL-DEFAULT"` pre-bootstrap
  and the bootstrapped installation is ALSO created with `DefaultTenantId` (LocalInstallationBootstrap L23).
  Write stamps (`AnimoraLocalDbContext.PrepareSave`) and reads (`context.TenantId` / manual `x.TenantId == tenant`)
  use the same value. **Tenant mismatch ruled out.**
- `ActivityDetailResolver.ResolveAsync` is null-safe (FirstOrDefault + null checks) → won't throw on fresh data.
- Default History filter = last 30 days (`HistoryPageViewModel` L226-227) → today's invoice is in range. Not the cause.

### ⇒ Conclusion: the persistence layer is NOT the bug. Focus Task 1 on these concrete, verifiable gaps:
**GAP A (most likely user-visible cause) — History views don't live-refresh.**
- Sale write calls `NotifyInvoicesChanged()` which DOES notify `DataDomain.History`
  (`SalePageViewModel` L536-538; Cashier `Pay` calls `NotifyInvoicesChanged()` L537).
- BUT `HistoryPageViewModel` has **no subscription** to `IDataChangeNotifier`/`DataDomain.History`
  (grep found zero `_changes`/`Subscribe`/`DataDomain` in it). The Sales `SaleHistoryViewModel` also has no
  change subscription (only date/search debounce reloads). So if the user is sitting on a history view when a
  sale is posted elsewhere, it never reloads. On fresh navigation it DOES reload (ctor/visit reload exists).
- **Action:** make History page + Sales/Cashier history tab subscribe to the data-change notifier for
  `DataDomain.History` (and `DataDomain.Sales`/`Cashier` for the tab) and reload on notification.
  Find the notifier interface the pages already receive (DI) — look at how `SalePageViewModel` obtains
  `_changes` (field type `IDataChangeNotifier`-like) and mirror that subscription in the history VMs.
  Pattern: subscribe in ctor/`OnActivated`, call the existing reload method, dispose/unsubscribe properly.

**GAP B — verify the FreshTest write actually fires (diagnostic, do NOT assume).**
- Add/confirm an EF-level smoke: the only test today (`tests/Animora.Application.Tests/ActivityFeedTests.cs`)
  tests the mapper ONLY, not end-to-end. If adding a test, keep it a single fast Application/Persistence test
  (one posted invoice → assert one `ActivityEvent` row + one `GetHistoryAsync` row). **No long/full test runs.**
- If, after wiring GAP A, you can reason the feed is written+read correctly, that is sufficient; document it.

### Files to touch
- `src/Animora.Desktop/Pages/History/HistoryPageViewModel.cs` — add change subscription + reload.
- `src/Animora.Desktop/Pages/Sales/SaleHistoryViewModel.cs` — add change subscription + reload.
- (Reference only, already correct — DO NOT change unless a concrete bug is proven:)
  `InvoicePaymentCore.cs`, `PostInvoiceCommand.cs`, `ActivityFeedHandler.cs`, `ActivityFeedWriter.cs`,
  `ActivityFeedQueries.cs`, `SalesQueries.cs`, `LocalUnitOfWork.cs`.
- STATUS: ☑ done — History page + Sales history tab now subscribe to DataDomain.History/Sales/Cashier and reload (tab also re-reads on every activation / page visit); GAP B: write+read chain re-verified by reasoning, no change needed.

---

## TASK 2 — Red "فاکتور ابطال شد" box after a SUCCESSFUL post → make it a green success box  🔴
**User report:** In Sales & Cashier, after finishing/registering an invoice a RED box "فاکتور ابطال شد!"
appears. Even if logically correct it reads like an error. Replace with a GREEN box + proper wording so the
user knows the invoice was saved correctly.

### Findings
- Cashier success path already shows the CORRECT green toast: `CashierPageViewModel.Pay` →
  `_toast.Show($"{verb} · ...", Tone.Ok)` with verb "تسویه شد"/"پرداخت ثبت شد" (L533). Its `Void` shows
  `_toast.Show("فاکتور ابطال شد", Tone.Ok)` (L564) — that is the real void action (fine, leave it).
- The RED terminal banner the user sees is the **locked-invoice banner** in
  `src/Animora.Desktop/Pages/Sales/SalePageView.axaml` (~L530-534): a `TextBlock Text="فاکتور ابطال شد"`
  shown when `SelectedInvoice.IsLocked` (Void OR Refunded). `SaleInvoiceViewModel` exposes
  `IsLocked` / `IsTerminalVoid` / `IsRefunded` / `StateLabel` (L20-22,74,78,80,110).
- Bug nuance: the banner text is HARD-CODED "فاکتور ابطال شد" and is shown for BOTH Void and Refunded, and
  (per report) appears to trigger after a normal post/settle — likely because state after settle resolves to a
  terminal/locked state and the banner has no "settled/paid success" variant.

### Action
1. In `SalePageView.axaml` replace the single hard-coded red banner with **state-driven** styling+text:
   - Void → red banner "فاکتور ابطال شد".
   - Refunded → (amber/neutral) "فاکتور مسترد شد".
   - Settled/Paid success → **green** banner with wording like "فاکتور با موفقیت ثبت و تسویه شد" (or
     "فاکتور ثبت شد" for posted-unpaid). Use `OkBrush`/`OkBgBrush` for green.
   - Bind text + tone to `SaleInvoiceViewModel` (add e.g. `BannerText`, `BannerTone`/`IsSuccessBanner`,
     `IsVoidBanner`, `IsRefundBanner` computed from `State`). Keep it minimal.
2. Confirm the Sales post/settle toast is `Tone.Ok` (green). If any `Tone.Danger` is shown on success, fix it.
- Files: `src/Animora.Desktop/Pages/Sales/SalePageView.axaml`,
  `src/Animora.Desktop/Pages/Sales/SaleInvoiceViewModel.cs`.
- STATUS: ☐ pending

---

## TASK 3 — "پرداخت و تسویه کامل" confirm modal: spacing / buttons stuck to edges  🟡
**User report:** The confirm box opened by "پرداخت و تسویه کامل" has bad design — spacing/details wrong,
especially buttons glued to the edges. Fix it.

### Findings
- The modal is `PaymentModalView.axaml` (the payment surface). `PayCtaLabel` = "پرداخت و تسویه کامل" when
  `WillSettle` (`PaymentModalViewModel` L119-121,183,192).
- Host: `ModalHost.axaml` Panel has `Padding="0"`, rounded corners, `ClipToBounds=True`. Modal content must
  supply its own padding.
- ROOT CAUSE: In `PaymentModalView.axaml` the form body is inside a `Border Padding="20,16"` (L65), but the
  **foot button row (L157-178) sits OUTSIDE that Border**, directly in the root `StackPanel`, with only
  `Margin="0,14,0,0"` (vertical). With host padding 0, the انصراف/پرداخت buttons touch the modal's left/right
  edges. The success panel (L181+) is already fine (its foot uses `Margin="20,0,20,20"`).

### Action
- Give the payment-form foot horizontal insets matching the body (left/right ~20, bottom ~16). Simplest:
  change foot `StackPanel` `Margin="0,14,0,0"` → `Margin="20,14,20,16"` (L158-159). OR move the foot INSIDE a
  single padded container wrapping body+foot. Keep the right-aligned button order; ensure a small gap from
  bottom edge. Verify visually that nothing is clipped by the host's `ClipToBounds`.
- Also sanity-check the generic `src/Animora.Desktop/Pages/Sales/Shared/ConfirmDialogView.axaml` foot
  (StackPanel at its end) has side padding; if its buttons also touch edges, apply the same inset.
- Files: `src/Animora.Desktop/Pages/Sales/PaymentModalView.axaml` (+ maybe `Shared/ConfirmDialogView.axaml`).
- STATUS: ☐ pending

---

## TASK 4 — Invoice line: remove the "zero" box; make +/- quantity buttons work  🔴
**User report:** In invoice items there's a box containing "0" → remove it. The +/- (increase/decrease
quantity) buttons don't work → fix them.

### Findings (exact XAML + data model)
- Line template: `src/Animora.Desktop/Pages/Sales/SalePageView.axaml` `ItemsControl ItemsSource="{Binding Lines}"`
  (L637). Grid `ColumnDefinitions="34,*,110,90,120,30"` (L641). Columns:
  - Col0 glyph; Col1 title/unit; **Col2 (L655-675) = the −/+ stepper rendered as plain `TextBlock`s (no
    Command) → that's why +/- do nothing**; **Col3 (L676-686) = discount box showing `DiscountText` which is
    "۰" when no discount → the "zero box" to REMOVE**; Col4 subtotal; Col5 trash icon (also no Command).
- Line VM `src/Animora.Desktop/Pages/Sales/InvoiceLineViewModel.cs` is display-only (comment "render ... without
  wiring (Phase 21)"). It exposes `QuantityText`, `DiscountText`, etc. It does NOT currently carry
  `Index`/`LineId`/`Quantity`.
- Contract `InvoiceLine` (SalesModels.cs L196-209) HAS: `int Index`, `int Quantity`, `int DiscountPercent`,
  `string LineId`. Service API exists:
  `ISalesQueryService.EditLineAsync(invoiceId, lineIndex, lineId, quantity, discountPercent)` (L109) and
  `RemoveLineAsync(invoiceId, lineIndex, lineId)` (L117). Stale index → `INVOICE_LINE_INDEX_STALE`.
- `SalePageViewModel`: builds lines at L759
  `Lines.ReplaceAll(detail.Lines.Select(line => new InvoiceLineViewModel(line)))`; has `_service`
  (ISalesQueryService), `SelectedInvoice.Id`, and `RefreshAfterMutation()`/`RefreshAfterMutationAsync()`
  (L529/540) which reload the open invoice after a mutation.

### Action
1. **Remove the zero/discount box**: delete Col3 Border (L676-686) and update the Grid `ColumnDefinitions`
   from `34,*,110,90,120,30` to drop the removed column (→ `34,*,110,120,30`). Re-check remaining `Grid.Column`
   indices so Col4 subtotal / Col5 trash shift correctly.
2. **Wire the stepper**: convert Col2's − and + `TextBlock`s into `Button`s bound to new commands on
   `InvoiceLineViewModel` (e.g. `IncreaseCommand`/`DecreaseCommand`), and wire the trash `Path` (Col5) into a
   delete `Button` (`RemoveCommand`). Decrease at quantity 1 should either disable or remove the line (choose
   the clean UX: disable `−` at qty 1).
3. **Make the VM actionable**: give `InvoiceLineViewModel` the `Index`, `LineId`, current `Quantity`, and a
   callback into the page. Cleanest wiring: inject a delegate when constructing the VM at L759, e.g.
   `new InvoiceLineViewModel(line, SelectedInvoice.Id, EditLineAndRefreshAsync)` where the page method calls
   `_service.EditLineAsync(invoiceId, line.Index, line.LineId, newQty, line.DiscountPercent)` then
   `RefreshAfterMutationAsync()`. Guard: only when `SelectedInvoice.CanEdit` (draft/editable); hide/disable
   stepper when invoice is locked/posted. Handle `INVOICE_LINE_INDEX_STALE` by just refreshing (it re-reads).
4. Keep money/qty formatting via `SalePresentation` (no arithmetic in UI beyond ±1 to quantity before calling
   the service, which recomputes totals authoritatively).
- Files: `src/Animora.Desktop/Pages/Sales/SalePageView.axaml`,
  `src/Animora.Desktop/Pages/Sales/InvoiceLineViewModel.cs`,
  `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs`.
- STATUS: ☐ pending

---

## TASK 5 — Patients page: remove the directory/settings tab strip; relocate "تنظیمات"; redesign settings  🔴
**User report:**
- There's a tab strip (فهرست بیماران و مالکین / تنظیمات) → **remove it completely**.
- Put the "تنظیمات" option onto the owners/patients (مالکین/بیماران) bar, properly sized.
- Redesign the patients/owners **settings** section: current UX is bad (must scroll down to reach SMS
  settings). Make it minimal, modern, standard-UX, focused on efficiency/speed, no long scroll.

### Findings (exact XAML)
- `src/Animora.Desktop/Pages/Patients/PatientsPageView.axaml`:
  - **Section tab strip to DELETE:** L544-571 (`Border Classes="seg"` with two `segitem`s: "فهرست بیماران و
    مالکین" → `SetSectionCommand` param `directory`, and "تنظیمات" → param `settings`).
  - **Header action area:** L533-540 has the primary "مالک و بیمار جدید" button and sibling buttons
    (export etc.) — good place for a compact "تنظیمات" button/icon.
  - **Toolbar** (directory view, L573+, `IsVisible="{Binding IsDirectorySection}"`): tier-1 has the
    مالکین/بیماران segmented switch (L584-608) + search (L612-635). This is the "نوار مالکین/بیماران".
  - **Settings section:** `ScrollViewer` L1280-1568, `IsVisible="{Binding IsSettingsSection}"`, a vertical
    `StackPanel Spacing="16"` with TWO stacked full-width cards:
    - Card 1 (L1286-1503): "نوع حیوانات و نژادها" (species/breeds add/edit/remove + breed list).
    - Card 2 (L1505-1566): "پیامک باز شدن پرونده" (toggle `Settings.FileOpenedSmsEnabled`, status text,
      template `TextBox` + `SaveSmsTemplateCommand`). It's below card 1 → requires scroll (the complaint).
- ViewModel `src/Animora.Desktop/Pages/Patients/PatientsPageViewModel.cs`: `PatientsSection` enum {Directory,
  Settings}; `_section` field; `IsDirectorySection`/`IsSettingsSection` (L92-93); `SetSectionCommand(string)`.
  Settings sub-VM accessed via `Settings.*`.

### Action
1. **Delete** the section tab strip Border (L544-571).
2. **Relocate "تنظیمات"**: add a compact, well-sized settings toggle/button into the header action row
   (near L533-540) OR into the directory toolbar bar — keep it visually consistent with existing
   `segtap`/`primary`/icon-button styles; bind it to `SetSectionCommand` with param `settings` (and a way back
   to `directory`, e.g. a back/close affordance in the settings header). Ensure dimensions match the bar
   (don't let it stretch oddly). Keep `IsDirectorySection`/`IsSettingsSection` visibility logic working.
3. **Redesign settings** to be minimal/modern with NO long scroll:
   - Put the two cards **side-by-side** in a responsive 2-column `Grid` (e.g. `ColumnDefinitions="*,*"` with
     column spacing ~16), so SMS is visible without scrolling at normal window sizes. Keep a `ScrollViewer`
     fallback only for very small heights.
   - Tighten paddings/typography for a compact, modern look; keep existing commands/bindings
     (`Settings.AddSpeciesCommand`, species list item commands, `Settings.FileOpenedSmsEnabled`,
     `Settings.FileOpenedSmsTemplate`, `Settings.SaveSmsTemplateCommand`) intact — this is a layout/UX
     restyle, not a behavior change.
   - Add a clear settings header with a back-to-directory control (since the tab strip is gone).
- Files: `src/Animora.Desktop/Pages/Patients/PatientsPageView.axaml` (primary);
  `PatientsPageViewModel.cs` only if a back/close command is needed.
- STATUS: ☐ pending

---

## TASK 6 (FINAL) — delete this plan file  🟢
- `git rm Plan/sales-history-patients-ux-fixes.md` (ONLY this file; leave the other Plan/*.md).
- Push with `scripts/git/push.sh "chore(plan): [Task پایانی] remove completed sales/history/patients plan"`.
- STATUS: ☐ pending
