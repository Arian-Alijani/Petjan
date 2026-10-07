# Plan — Session tasks (2026-10-07): Purchasing tab + Patients/Sales/Inventory UX

> **Self-contained execution plan.** A fresh model must be able to implement every task below
> **without re-auditing the repo** — all file paths, the exact current code shape, the root-cause
> analysis and the decided fix are embedded here. Read top-to-bottom once, then execute in order.
> After **each** completed task, follow the LOCKED push rule (§0). The **final** task deletes this
> file only.

---

## 0. Context + rules (read once)

- **App:** Animora — vet-clinic desktop app. .NET 10, **Avalonia + MVVM** (CommunityToolkit.Mvvm),
  Persian / RTL UI, Jalali calendar, local encrypted SQLite. Offline-first, no server.
- **Architecture rule (never break):** `View → ViewModel → Animora.Contracts interface (ctor DI) →
  Animora.Client.Services impl → Animora.Application dispatch → SQLite`. ViewModels depend **only**
  on Contracts interfaces, never on a concrete service / EF / SQL / DI scope. No seed data or
  design-time branches in `Pages/**`.
- **To add a data need to a screen:** add/extend interface+DTO in `src/Animora.Contracts/<Area>/`,
  implement in matching `src/Animora.Client.Services/<Area>/` over the dispatcher, **register both
  in `src/Animora.Desktop/App.axaml.cs`**.
- **Per-screen file pattern** in `Pages/<Area>/`: `<Screen>View.axaml` (+ `.axaml.cs`) and
  `<Screen>ViewModel.cs`. Modals/wizards are extra `*View`+`*ViewModel` pairs in the same folder.
- **Build/verify (fast only — do NOT run the full test suite or smoke run unless a task says so):**
  - Desktop only: `scripts/sandbox/build.sh` → must be **0 warnings / 0 errors**.
  - Whole solution (use after Contracts/Application/Persistence/Client.Services edits):
    `scripts/sandbox/build.sh full`.
- **Theme tokens** live in `src/Animora.Desktop/Resources/Theme/*.axaml` (Colors/Icons etc.).
  Brushes used below (confirmed to exist): `PanelBrush, BgBrush, Bg2Brush, InkBrush, Ink2Brush,
  MutedBrush, LineBrush, Line2Brush, Brand50Brush, Brand100Brush, Brand400Brush, Brand600Brush,
  Brand700Brush, OkBrush, OkBgBrush, DangerBrush/…`. Icon geometries are `IconXxx` StaticResources;
  in code-behind VMs an icon is referenced by its **string key** (e.g. `"IconBox"`) resolved via
  `IconKeyConverter` in XAML.

### LOCKED push rule (critical — no push = failure)
Work on branch **`genspark_ai_developer`**. After **each** completed task run:
```
scripts/git/push.sh "type(scope): message"
```
It stages-all → commits → rebases onto origin → pushes. If it exits code **3** (no creds) → call the
`setup_github_environment` tool once, then re-run the same command. First push may need
`scripts/git/push.sh -b genspark_ai_developer "msg"` to create/switch the branch. If a heavy fix or
large code block is written mid-task, push it too (don't batch many tasks into one push).

### Task status legend
`[ ]` todo · `[~]` in progress · `[x]` done

---

## TASKS OVERVIEW (do in this order; small UX wins first, big feature last)

1. **[x] T1** — Patients page: turn «تنظیمات» into a 3rd segmented tab (styled like the owners/patients tabs), placed after «بیماران». Remove the old outline button.
2. **[x] T2** — Inventory «حذف دسته» confirm dialog: fix the cramped/overflowing layout (width mismatch + danger button not red).
3. **[x] T3** — Sales payment modal: Card first + default-selected; replace the ugly pay-button color/icon with a standard green, minimal/modern style, icon on the **left** of the text.
4. **[x] T4** — Sales payment modal: Mixed («ترکیبی») mode lets the admin split the amount across methods (e.g. part card, part cash/cheque).
5. **[x] T5** — Sales payment modal: when a payment (or a split part) is by Cheque, prompt whether to register it in the Cheques module; wire a real cheque-registration path that links the cheque to the operation/invoice, surfaced in History + owner profile + the Cheques screen (cheque date etc. asked here).
6. **[ ] T6** — Inventory: new **«خرید و تأمین»** (supplier purchases / invoices) tab — create & record supplier purchase invoices; integrate with main History + inventory history, stock, statistics; extract purchased unit cost as a product's purchase price; pick existing inventory items with quantity/cost auto-fill; register brand-new goods; cover multiple scenarios.
7. **[ ] T_FINAL** — Delete **this** plan file only, then push.

---

## T1 — Patients «تنظیمات» as a segmented tab

**Goal:** The settings entry currently is a standalone `outline` button sitting at the end of the
search toolbar row. Make it a **third segment** in the entity-switch segmented control (same visual
as the «مالکین» / «بیماران» segments), ordered **after** «بیماران». Remove the old outline button.

**File:** `src/Animora.Desktop/Pages/Patients/PatientsPageView.axaml`
**VM:** `src/Animora.Desktop/Pages/Patients/PatientsPageViewModel.cs`

### Exact current shape (confirmed)
- The entity switch is a `Border Classes="seg"` at **~line 555**, containing a
  `Grid ColumnDefinitions="*,*"` with two `Border Classes="segitem" Classes.on="{Binding IsOwnersView}"`
  / `Classes.on="{Binding IsPetsView}"` cells. Each cell holds a `Button Classes="segtap lg"` with
  `Command="{Binding SetViewModeCommand}" CommandParameter="owners"|"pets"`, an icon
  (`IconUsers` / `IconPaw`) and a label («مالکین» / «بیماران»).
- The **old settings button** is a `Button Grid.Column="2" Classes="outline"` at **~line 609**, with
  `Command="{Binding SetSectionCommand}" CommandParameter="settings"`, icon `IconTag`, text «تنظیمات»,
  tooltip «تنظیمات مالکین و بیماران». It lives in the tier-1 `Grid ColumnDefinitions="*,Auto,Auto"`
  (seg | search | settings-button) at ~line 554.
- VM sections: `SetSectionCommand` (param `"settings"`/`"directory"`) toggles `IsSettingsSection` /
  `IsDirectorySection`; `SetViewModeCommand` (param `"owners"`/`"pets"`) toggles `IsOwnersView`/`IsPetsView`
  and is directory-only. The whole toolbar/header are gated `IsVisible="{Binding IsDirectorySection}"`,
  and the settings pane is gated `IsVisible="{Binding IsSettingsSection}"` (ScrollViewer at ~line 1263).
  Grep the VM for `IsDirectorySection`, `IsSettingsSection`, `SetSectionCommand`, `IsOwnersView`,
  `IsPetsView`, `SetViewModeCommand` to confirm property/command names before editing.

### Decided implementation
1. **XAML — segmented control (~line 555):** change the inner grid to **three** equal columns
   `ColumnDefinitions="*,*,*"`. Keep columns 0/1 (مالکین/بیماران) as-is. Add a **column 2**
   `Border Classes="segitem"` with `Classes.on="{Binding IsSettingsSection}"` containing a
   `Button Classes="segtap lg"` → `Command="{Binding SetSectionCommand}" CommandParameter="settings"`,
   icon `IconTag` (or `IconTools`/`IconSettings` if present — keep `IconTag` to match the removed
   button), label «تنظیمات».
2. **Keep the seg container visible on the settings pane too.** The seg is inside a `Border` that is
   `IsVisible="{Binding IsDirectorySection}"` — so on the settings pane the tab bar would disappear,
   which breaks the "tab" metaphor. **Fix:** move the entity-switch seg out from under the
   `IsDirectorySection` gate so the seg (tab bar) stays visible in both sections; keep the **search
   field** and **tier-2 species chips** gated on `IsDirectorySection` (they're directory-only). Simplest
   robust approach: wrap just the seg `Border` so its own `IsVisible` is unconditional, and keep the
   search column + chips with `IsVisible="{Binding IsDirectorySection}"`. When settings is active, the
   `owners`/`pets` segments must still work: `SetViewModeCommand` is directory-scoped — have each of the
   owners/pets segment buttons first switch back to the directory section. Verify the VM: if
   `SetViewModeCommand` does **not** already call `SetSection("directory")`, add that call at its top
   (so clicking مالکین/بیماران from the settings tab returns to the directory). Keep `IsSettingsSection`
   driving the settings-pane visibility unchanged.
3. **Remove** the old `Button Grid.Column="2" Classes="outline" … CommandParameter="settings"` (~line
   609) and collapse the tier-1 grid from `*,Auto,Auto` to `*,Auto` (seg | search).
4. **Segment `on` state for settings:** reuse the existing `seg`/`segitem`/`segtap`/`on` styles (defined
   in this view or a shared style). No new style needed — `Classes.on="{Binding IsSettingsSection}"`
   drives the active look exactly like the other two.

### Verify
- `scripts/sandbox/build.sh` → 0/0. Visually: three equal tabs, «تنظیمات» last; clicking it shows the
  settings pane with the tab bar still visible and the tab highlighted; clicking مالکین/بیماران returns
  to the directory with search+chips back. No leftover outline button.

### PUSH
`scripts/git/push.sh "feat(patients): [T1] move settings into a segmented tab after بیماران, drop the outline button"`

---

## T2 — Inventory «حذف دسته» confirm dialog layout fix

**Goal:** The category-delete confirmation looks cramped: buttons stuck to the edges, text spilling
out. Fix spacing/containment and make the danger confirm button actually red.

### Root cause (confirmed)
- The shared confirm dialog view `src/Animora.Desktop/Pages/Sales/Shared/ConfirmDialogView.axaml` is a
  `UserControl` with `Width="480" MaxWidth="480"`.
- Inventory opens it with **maxWidth 420**:
  `src/Animora.Desktop/Pages/Inventory/InventoryPageViewModel.cs` ~line 424:
  `_dialogs.ShowModal(new …Shared.ConfirmDialogViewModel(_dialogs, "danger", "IconTrash", "حذف دسته",
  "دسته ", row.Label, " حذف شود؟", <note>, …), 420);`
- `ModalHost` (`src/Animora.Desktop/Controls/ModalHost.axaml`) sets the host `Panel.MaxWidth` to the
  passed value (420) but the **content control forces its own 480 width** → the content is wider than
  its host panel, so inner content (head grid, body WrapPanel, foot StackPanel with
  `HorizontalAlignment="Right"`) gets clipped / pushed to the edges and long body text overflows.
- Secondary bug: the confirm button in `ConfirmDialogView.axaml` is **hard-coded** to `Brand600Brush`
  regardless of tone; the VM exposes `IsDanger` (`Tone == "danger"`) but the XAML ignores it, so the
  danger delete button is brand-green, not red.

### Decided fix (minimal, no behavior change for Sales callers)
1. **Width coherence.** Easiest safe fix: in `InventoryPageViewModel.cs` change the `ShowModal(…, 420)`
   call to `ShowModal(…, 480)` so host panel width matches the content's intrinsic 480. (Sales callers
   already use the default 520 or 480; the content's own `MaxWidth=480` keeps it at 480.) **Also**
   confirm the body note text wraps: the body `TextBlock`s already have `TextWrapping="Wrap"`, so the
   overflow was purely the 420/480 clash — fixing the width removes the spill.
2. **Danger button color.** In `ConfirmDialogView.axaml`, make the confirm button honor the tone.
   Replace the inline `Button.Styles` (currently always Brand600 → Brand700 on hover) with a binding to
   tone: add `Classes.danger="{Binding IsDanger}"` on the confirm `Button`, and add two style selectors:
   - `Selector="Button.cf-btn"` default → `Brand600Brush` / hover `Brand700Brush`, `Foreground=White`.
   - `Selector="Button.cf-btn.danger"` → `Background={StaticResource DangerBrush}` (confirm the exact
     danger brush key in `Resources/Theme/Colors.axaml`; likely `DangerBrush` / `Danger600Brush`), hover
     a slightly darker danger, `Foreground=White`, `BorderThickness=0`.
   Keep `IsDanger` as the single source of truth (no new VM field).
3. **Foot spacing sanity.** The foot `StackPanel` uses `Margin="20,12,20,18" Spacing="8"
   HorizontalAlignment="Right"` — fine once width matches. No change needed unless, after the width fix,
   the buttons still look tight; if so bump foot `Spacing` to `10`.

### Verify
- `scripts/sandbox/build.sh` → 0/0. Open Inventory → settings → delete a category: dialog is 480 wide,
  title/body/buttons have margins, long category names wrap, and the confirm button is **red**.
- Confirm Sales flows that reuse `ConfirmDialogViewModel` still look correct (they pass `"danger"` for
  destructive confirms and non-danger tones elsewhere — the tone binding now correctly colors them).

### PUSH
`scripts/git/push.sh "fix(inventory): [T2] correct حذف دسته confirm dialog width + make danger confirm button red"`

---

## T3 — Sales payment modal: Card-first + restyle the pay button

**Goal (on the «روش پرداخت» surface that opens when clicking «تسویه»):**
- The **Card** method must be the **first** option and **selected by default**.
- The pay («پرداخت و تسویه کامل» / «ثبت پرداخت جزئی») button currently has an ugly color + the check
  icon on the **right** of the text. Make it a **standard green** (clearly a "pay" affordance),
  minimal/modern, with a **better icon on the LEFT** of the text.

### Files
- VM: `src/Animora.Desktop/Pages/Sales/PaymentModalViewModel.cs`
- View: `src/Animora.Desktop/Pages/Sales/PaymentModalView.axaml`
- Presentation helpers: `src/Animora.Desktop/Pages/Sales/SalePresentation.cs` (method labels/icons;
  grep `MethodIconKey`, `MethodLabel`).

### Exact current shape (confirmed)
- VM ctor adds methods in this order (line ~51): `Cash(true), Card, Cheque, Credit, Mixed`. The `true`
  on `Cash` makes cash the default-active chip. `SelectedMethod` = first `IsActive` or `Cash`.
- The methods render in a `UniformGrid Columns="5"` (View ~line 88–110) — order follows the collection.
- The pay button (View ~line 158–174): `Button Classes="ps-foot"` with an inline `Button.Styles` that
  sets `Background=Brand600Brush` / hover `Brand700Brush`, `Foreground=White`. Its content is a
  horizontal `StackPanel Spacing="10"` → **[check Path icon] then [TextBlock PayCtaLabel]**. In RTL this
  renders the icon on the **right** of the text (first child = start = right). `PayCtaLabel` =
  `WillSettle ? "پرداخت و تسویه کامل" : "ثبت پرداخت جزئی"`.

### Decided implementation
1. **Card first + default.** In the VM ctor change the method-add order and default flag to:
   `Card(true), Cash, Cheque, Credit, Mixed`. Update the `LoadAsync`/`UpdatePreview` logic only if it
   assumes Cash — it uses `SelectedMethod` (first active) so changing the default flag is enough.
   `UniformGrid Columns="5"` still fits 5 chips. (Keep all five methods.)
2. **Pay button = standard green, icon left.**
   - Add a reusable green style for the pay CTA. Replace the inline `Button.Styles` with explicit green
     brushes: use the app's success/green token (`OkBrush` is the green used elsewhere; confirm in
     `Resources/Theme/Colors.axaml`). Set `Background={StaticResource OkBrush}`, `Foreground=White`,
     `BorderThickness=0`, `CornerRadius=10`, and a hover that darkens (if an `Ok700`/darker green token
     exists use it; otherwise reuse `OkBrush` with reduced opacity via a slightly darker literal —
     prefer an existing token). Keep padding/weight from `Classes="ps-foot"`.
   - **Icon on the left of the text (RTL):** since the panel is RTL, the *first* child renders on the
     right. To force the icon to the **visual left**, either put the `TextBlock` first and the `Path`
     second in the `StackPanel`, **or** set `FlowDirection="LeftToRight"` on the button's inner
     `StackPanel` and keep `[icon, text]` order. Choose the FlowDirection approach for clarity:
     `<StackPanel Orientation="Horizontal" Spacing="10" FlowDirection="LeftToRight"><Path …/><TextBlock
     …/></StackPanel>` so icon is visually left, text right, reading naturally.
   - **Better icon:** use a wallet/credit or a crisp check-circle. Prefer `IconWallet` (already used in
     the header) or `IconCheckCircle` if present; keep `IconCheck` only if neither reads better. Pick
     `IconWallet` to signal "pay". Keep `Stroke="White"`, `Width/Height=16`.
3. Keep the «انصراف» outline button unchanged.

### Verify
- `scripts/sandbox/build.sh` → 0/0. Open a sale → «تسویه»: Card chip is first and pre-selected; pay
  button is green with the icon on the left of the label.

### PUSH
`scripts/git/push.sh "feat(sales): [T3] card-first default payment method + green pay CTA with left-aligned icon"`

---

## T4 — Sales payment modal: split («ترکیبی») payment across methods

**Goal:** When the admin picks **Mixed («ترکیبی»)**, let them split the invoice amount across methods
(e.g. X تومان کارت + Y تومان نقدی + Z تومان چک), with the parts summing to the amount being settled.

### Backend fact (confirmed — no new domain work)
- `ISalesQueryService.TakePaymentAsync(invoiceId, PaymentRequest(Method, Amount, Note))`
  (`src/Animora.Contracts/Sales/ISalesQueryService.cs` ~line 90) applies **one** method+amount per call;
  the impl (`src/Animora.Client.Services/Sales/SalesQueryService.cs` ~line 269) routes through
  `InvoicePaymentCore.PayAsync` and caps to the due amount, returning a `PaymentResult` with the updated
  `InvoiceDetail` (incl. `Due`, `Paid`, `State`). **Partial payments are already supported** (the modal
  already handles `!result.Settled`).
- Therefore a split is implemented purely in the **modal VM** by calling `TakePaymentAsync` **once per
  split part** in sequence (each part is a real payment row; History/profile/ledger already record each
  payment). `PaymentMethod.Mixed` itself should **not** be sent to the backend — it's a UI mode; each
  part is sent with its concrete method (Card/Cash/Cheque/Credit).

### Decided implementation (`PaymentModalViewModel.cs` + `PaymentModalView.axaml`)
1. **Mixed mode UI.** When the active method is `Mixed`, hide the single amount row + presets and show a
   **split editor**: a small list of part rows, each with a **method picker** (Card/Cash/Cheque/Credit —
   not Mixed) and an **amount TextBox** (Persian digits via `PersianNumberFormatter`, same parse as the
   existing `Amount`). Provide «+ افزودن ردیف» to add a part and a remove (×) per row. Seed with two
   rows (Card = full due, Cash = 0) for convenience.
2. **Live validation band.** Reuse the existing preview band: compute `sumParts = Σ part amounts`.
   - `sumParts == 0` → neutral: «مبلغ هر روش را وارد کنید».
   - `sumParts < due` → partial tone: «پرداخت ترکیبی جزئی؛ مانده پس از ثبت: {due-sumParts}».
   - `sumParts == due` → success: «با این ترکیب فاکتور کاملاً تسویه می‌شود».
   - `sumParts > due` → warn: «جمع اجزا بیش از مانده است؛ مقدار اضافه ثبت نمی‌شود» (and the pay command
     is disabled, or the last part is auto-capped — prefer **disable** pay until `sumParts <= due`).
   - Update `PayCtaLabel` to reflect settle vs partial based on `sumParts`.
3. **PayAsync for Mixed.** When Mixed is active, iterate parts with amount>0 and `await
   TakePaymentAsync(invoiceId, new PaymentRequest(partMethod, partAmount, "پرداخت ترکیبی از صندوق"))`
   **sequentially**. Stop and surface a toast on the first failed part (keep already-applied parts —
   they are committed; show which succeeded). After the loop, read the final `InvoiceDetail` from the
   last successful result: if settled → `ShowSettlementSuccessAsync` (set `SuccessMethodLabel` = «ترکیبی»
   and list the parts in the success summary, or at minimum label «ترکیبی»); else close with a partial
   toast showing the remaining due.
4. **Cheque parts feed into T5.** If any part's method is `Cheque`, after that part's payment succeeds,
   trigger the **cheque-registration prompt** (T5). Design T4 so the per-part Cheque hook calls a shared
   method `MaybeRegisterChequeAsync(part)` implemented in T5 (stub it in T4 as a no-op you replace in
   T5, OR implement T5 first and call it here — order: do T4 then T5, leaving the hook call in place).
5. **Non-Mixed path unchanged** (single amount + presets as today, now with the green CTA from T3).

### Notes / gotchas
- Keep `SelectedMethod` returning the first active chip; add an `IsMixed => SelectedMethod == Mixed`
  gate for XAML visibility. `OnAmountTextChanged`/part-amount changes both call `UpdatePreview`.
- `_onChanged?.Invoke()` must fire after the whole split completes (refreshes sidebar/KPIs) — call it
  once after the loop (and once per part is harmless but one call after is cleaner).
- Each `TakePaymentAsync` caps to the current remaining due, so ordering parts matters only if the user
  over-fills; the `sumParts <= due` gate prevents that.

### Verify
- `scripts/sandbox/build.sh` → 0/0 (and `full` if you touched Contracts — you shouldn't need to; this is
  VM/XAML-only). Manual logic check: Mixed with Card=due → settles; Card+Cash summing to due → settles;
  partial sum → stays open with correct remaining due.

### PUSH
`scripts/git/push.sh "feat(sales): [T4] mixed payment splits amount across methods, each applied as a real payment"`

---

## T5 — Cheque payment → prompt + register in Cheques, linked to the invoice

**Goal:** When a payment (whole, or a Mixed part from T4) uses **Cheque**, ask the admin whether that
cheque needs to be **registered in the Cheques module**. If yes, open a cheque-registration form (asking
cheque number, amount pre-filled, **issue/due dates (Jalali)**, bank/branch/account/sayad, note) and
register it via the real `IChequesService`, **linked to the current owner and this invoice/operation**.
The cheque must be correctly reflected in: **main History**, the **owner profile**, and the **Cheques
screen** — each clearly showing it belongs to this sale/invoice.

### Backend facts (confirmed)
- `IChequesService` (`src/Animora.Contracts/Cheques/IChequesService.cs`):
  - `Task<string> RegisterAsync(NewChequeRequest request)` → returns new cheque id.
  - `Task<IReadOnlyList<ChequeParty>> FindOwnersAsync(string? search, …)` (inbound party lookup).
- `NewChequeRequest` (`src/Animora.Contracts/Cheques/ChequeModels.cs` ~line 114):
  `(ChequeDirectionDto Direction, string Number, long AmountMinor, string IssueDate, string DueDate,
  string PartyId, string? Bank=null, string? Branch=null, string? Account=null, string? SayadId=null,
  string? Note=null)`. Dates are Jalali `yyyy/MM/dd` strings; `AmountMinor` is **IRT (تومان)**.
  For a customer paying by cheque, `Direction = Inbound` and `PartyId = ownerId`.
- Client impl: `src/Animora.Client.Services/Cheques/ChequesService.cs` →
  `src/Animora.Application/UseCases/Finance/Cheques/ChequeCommands.cs` (`RegisterChequeCommand` with
  `Direction, Number, AmountMinor, DueUtc, IssuedUtc, OwnerId/SupplierId, …`). Registering is already
  audited → it appears in main History under `HistoryEventKind.Cheque` (confirmed `Cheque` kind exists
  in `src/Animora.Contracts/History/HistoryModels.cs` line ~33).
- The invoice detail available in the modal (`InvoiceDetail` from `TakePaymentAsync`/`GetInvoiceAsync`)
  carries owner identity: fields incl. `OwnerName`, `IsGuest`, and an **owner id** — **CONFIRM the owner
  id field name** on `InvoiceDetail` (grep `record InvoiceDetail` in
  `src/Animora.Contracts/Sales/SalesModels.cs`; likely `OwnerId`). For a guest invoice there is no
  owner → the cheque cannot be inbound-linked to an owner; in that case **skip the prompt** (or inform
  the user a cheque needs a registered owner).

### Linkage requirement — how to make the cheque reference "this invoice"
- `NewChequeRequest` has **no explicit invoice-id field**. Two options:
  1. **Minimal (no backend change):** put the invoice reference into the cheque **`Note`**, e.g.
     `"بابت فاکتور {ReceiptNo/InvoiceNumber} — {OwnerName}"`. This shows in the Cheques detail, History
     note, and the owner profile cheque list. **Decide on this unless the user needs structured linkage.**
  2. **Structured (backend change — larger):** extend `NewChequeRequest` + `RegisterChequeCommand` +
     the domain `Cheque` aggregate with an optional `SourceInvoiceId` and surface it in `ChequeItem`
     and the detail. Only do this if option 1 is judged insufficient; it touches Domain/Application/
     Persistence (migration) + Contracts + Client.Services. **Default to option 1** to stay in scope;
     note option 2 in the PR body as a follow-up.
- Owner-profile visibility: the owner profile already lists the owner's cheques (the Profile/Owner sheet
  reads cheques by owner). Since the cheque is registered Inbound with `PartyId = ownerId`, it will
  appear there automatically. **Verify** by grepping the owner profile VM for cheque usage
  (`src/Animora.Desktop/Pages/Profile/**` or `Pages/Patients/OwnerSheetViewModel.cs`) — if it filters by
  owner id, no extra work; if the profile has no cheque section, that's out of scope for this task (note
  it) — the Cheques screen + History are the hard requirements.

### Decided implementation
1. **New modal** `ChequePromptModalView(.axaml/.axaml.cs)` + `ChequePromptModalViewModel.cs` in
   `Pages/Sales/` (reuses `IChequesService`, injected). Two-step or single form:
   - Step A (confirm): «آیا این چک در بخش چک‌ها ثبت شود؟» with «ثبت چک» / «فعلاً نه». If «فعلاً نه» →
     close, payment already recorded (no cheque row).
   - Step B (form): fields — شمارهٔ چک (Number, required), مبلغ (AmountMinor, pre-filled with the cheque
     part amount, editable but default = part amount), **تاریخ صدور** (IssueDate) + **تاریخ سررسید**
     (DueDate) using the app's `JalaliDatePicker` control (`src/Animora.Desktop/Controls/` — confirm
     name; it outputs Jalali `yyyy/MM/dd`), بانک/شعبه/حساب/شناسهٔ صیاد (optional), یادداشت (prefilled
     with the invoice-reference note from option 1). Submit → `await _cheques.RegisterAsync(new
     NewChequeRequest(ChequeDirectionDto.Inbound, number, amountMinor, issueDate, dueDate, ownerId,
     bank, branch, account, sayadId, note))`. Toast success; on failure surface the mapped error.
   - Notify data change so the Cheques screen + History refresh: call the `IDataChangeNotifier.Notify`
     with `DataDomain.Cheques, DataDomain.History` (inject `IDataChangeNotifier` or reuse the modal's
     dialog/toast services; grep how other modals notify — e.g. Cashier/Sales VMs call a notifier after
     a successful write).
2. **Hook from the payment flow.** In `PaymentModalViewModel`:
   - Single-method path: after a successful `TakePaymentAsync` where `SelectedMethod == Cheque`, open the
     cheque prompt (only if the invoice has a real owner id; guests → skip). Open it **after** the
     success screen is shown or chain it before `ShowSettlementSuccessAsync` — simplest: on successful
     cheque payment, open the prompt modal via `_dialogs.ShowModal(new ChequePromptModalViewModel(…))`;
     when it closes, continue the normal success/partial handling. Pass the applied amount + owner id +
     an invoice-reference string (ReceiptNo or invoice number + owner name).
   - Mixed path (T4): the shared `MaybeRegisterChequeAsync(partAmount)` hook referenced in T4 opens the
     same prompt for each Cheque part.
3. **Error mapping:** reuse `Animora.Desktop.Localization.ErrorMessages.ForResult`/the cheque error codes
   already mapped for the Cheques screen (grep `ChequesPresentation`/the Cheques VM for the mapping).

### Gotchas
- Confirm the exact `InvoiceDetail` owner-id property name before coding; if there is none, derive the
  owner id from the sidebar selection / the sale VM and pass it into the payment modal ctor.
- The cheque amount must be in تومان (minor = تومان here, matching the rest of Sales `Money`); do **not**
  multiply by 1000.
- Keep the JalaliDatePicker's string output format exactly `yyyy/MM/dd` (ASCII) as `NewChequeRequest`
  expects.

### Verify
- `scripts/sandbox/build.sh` → 0/0 (Desktop-only if you used option 1; no Contracts change). Pay a sale
  with Cheque → prompt appears → register → the cheque shows on the Cheques screen (Inbound, this owner,
  note referencing the invoice), and a `Cheque` event appears in History.

### PUSH
`scripts/git/push.sh "feat(sales): [T5] cheque payments prompt to register in Cheques, linked to the invoice/owner"`

---

## T6 — Inventory «خرید و تأمین» (supplier purchases / invoices) tab  — the big one

**Goal:** Add a new tab to the Inventory page to **create & record supplier purchase invoices**
(purchases from suppliers). Requirements from the user:
- Integrate with the **main History** + the **inventory (per-product) history**.
- Keep **stock** in sync (a recorded purchase increases on-hand).
- Feed **statistics/آمار**.
- **Extract the purchased unit cost as a product's purchase price** (update the variant `Cost`).
- Let the admin **pick existing inventory items** and auto-fill data like quantity/cost.
- Allow **registering a brand-new product** inline.
- **Cover multiple scenarios** (existing vs new items, with/without a prior purchase order, direct
  receipt).

### CRITICAL backend discovery (confirmed — saves a full re-audit)
The entire **Purchasing backend already exists** but is **not wired to the UI and not even registered**:
- **Domain:** `src/Animora.Domain/Purchasing/` → `Supplier.cs`, `PurchaseOrder.cs`,
  `PurchaseOrderLine.cs`, `GoodsReceipt.cs`, `GoodsReceiptLine.cs`. `GoodsReceipt.Post()` freezes lines
  and the handler appends one `MovementKind.Receipt` stock movement **per line, carrying its unit cost**
  → stock + purchase cost already flow through the stock ledger.
- **Application:** `src/Animora.Application/UseCases/Purchasing/`:
  - `ReceiveGoodsCommand(SupplierId, IReadOnlyList<GoodsReceiptLineInput> Lines, PurchaseOrderId?=null,
    InvoiceReference?=null, AllowOverReceipt=false)` → records a receipt, writes stock movements, advances
    any linked order. `GoodsReceiptLineInput(ProductId, VariantId, QuantityScaled, UnitCostMinor,
    LotNumber?, ExpiryUtc?)` — **`QuantityScaled` is quantity ×1000** (the store's quantity scale).
    `UnitCostMinor` is تومان. Returns the receipt id. Permission: `InventoryReceive`. Audited under
    `AuditCategory.Inventory` / `CommandTypes.PurchasingReceiptRecord` → **shows in main History**.
  - `CreatePurchaseOrderCommand(SupplierId, Lines, ExpectedUtc?, Notes?, Submit=false)` and
    `CancelPurchaseOrderCommand(PurchaseOrderId, Reason)`.
  - `PurchasingSnapshotQuery` → `PurchasingSnapshotReadModel(ActiveOrderCount, MonthToDateSpendMinor,
    SupplierDebtMinor, Currency, IReadOnlyList<PurchaseOrderRowReadModel> Orders)` where a row is
    `(Id, OrderNumber, SupplierName, OrderedUtc, LineCount, GrandTotalMinor, Currency, PaymentTermDays,
    State)`.
  - **Registration helper `PurchasingRegistration.AddPurchasingUseCases(...)` EXISTS but is NEVER
    chained** into `AddAnimoraApplication` (grep confirmed: only its own definition references it).
    **→ The commands/query are currently NOT dispatchable. T6 must chain it in.** Find the application
    composition root (grep `AddPatientsUseCases` / `AddInventoryUseCases` / similar `.Add…UseCases()`
    chain inside `src/Animora.Application/ApplicationServiceCollectionExtensions.cs` or the registration
    builder) and add `.AddPurchasingUseCases()` in the same chain.
- **Persistence:** `src/Animora.Persistence.Sqlite/Repositories/PurchasingRepository.cs`,
  `Queries/PurchasingQueries.cs`, `Configurations/Purchasing/*` (Supplier/PurchaseOrder/GoodsReceipt EF
  configs) — all present. **Confirm these repos/queries are registered** in the Persistence DI (grep the
  Persistence service-collection extension for `IPurchasingRepository` / `IPurchasingQueries`); if the
  `PurchasingRegistration` was never chained, the port impls may or may not be DI-registered — verify and
  register if missing.
- **Missing pieces to build:** (a) a **supplier directory** read + a **create-supplier** path (there is
  **no supplier list query and no CreateSupplier command** — only cheque's `FindSuppliersAsync` returns
  supplier parties for outbound cheques; and the `PurchasingRepository` has no `AddSupplier`). (b) the
  **Contracts seam** for purchasing. (c) the **Client.Services** impl. (d) the **Desktop UI tab**.

### Scope decision (keep it shippable, cover the real scenarios)
Build a **direct goods-receipt / supplier-invoice** flow (the user said "supplier invoices & purchases
made from suppliers" — i.e. recording what was bought, not a full PO workflow). Use
`ReceiveGoodsCommand` **without** a PurchaseOrderId (direct receipt) as the core write. A full PO
lifecycle (create/submit/receive-against) is **optional** and can be deferred — note it in the PR.

**Supplier handling:** `ReceiveGoodsCommand` requires a `SupplierId`. Since there's no supplier CRUD, add
the **minimum** backend to support suppliers:
- A **supplier list query** (reuse/extend: the cheque side already queries suppliers via
  `IChequeQueries.FindSuppliersAsync` → there is a `Supplier` set in the DB. You can add a small
  `SupplierDirectoryQuery` in `UseCases/Purchasing` returning `(Id, Code, Name, …)` from a new
  `IPurchasingQueries.ListSuppliersAsync` over `db.Set<Supplier>()`), **and**
- A **create-supplier command** `CreateSupplierCommand(Code, Name, PaymentTermDays?, Phone?, …)` →
  `Supplier.Create(tenantId, code, name, …)` (factory confirmed to exist in `Supplier.cs`), persisted via
  a new `IPurchasingRepository.AddSupplier(Supplier)` (add to the port + repo). Register both in
  `PurchasingRegistration`.
- **Simplest alternative if time-boxed:** if adding supplier CRUD is too heavy, allow a **free-text
  supplier name** captured into the receipt's `InvoiceReference`/note and create/get-or-create a supplier
  by name behind the scenes. **Prefer the explicit supplier directory + create** for correctness, but a
  get-or-create-by-name helper is acceptable to cover the "multiple scenarios" requirement without a
  separate supplier-management UI.

### Contracts to add — `src/Animora.Contracts/Purchasing/` (new folder)
Create `IPurchasingService.cs` + `PurchasingModels.cs`:
- DTOs: `SupplierRef(string Id, string Code, string Name)`,
  `PurchaseLineInput(string ProductId, string VariantId, int Quantity, long UnitCost, string? Lot=null,
  string? ExpiryJalali=null)` (Quantity is whole units; the service multiplies by 1000 → `QuantityScaled`),
  `NewPurchaseItemInput(...)` for a brand-new product line (name/code/category/unit/price/cost/qty) OR
  reuse the Inventory `NewProductRequest` to create the product first then add its new variant id as a
  normal line, `RecordPurchaseRequest(string SupplierId, IReadOnlyList<PurchaseLineInput> Lines,
  string? InvoiceReference=null, string? SupplierNameForCreate=null)`,
  `PurchaseRecordResult(bool Ok, string? Error, string ReceiptId)`,
  `PurchaseListItem(string Id, string Number, string SupplierName, string DateJalali, int LineCount,
  long TotalMinor, string StateLabel)`, `PurchasingSnapshot(int ActiveOrders, long MonthSpend,
  long SupplierDebt, IReadOnlyList<PurchaseListItem> Rows)`.
- Interface `IPurchasingService`:
  - `Task<PurchasingSnapshot> GetSnapshotAsync()`
  - `Task<IReadOnlyList<SupplierRef>> FindSuppliersAsync(string? search)`
  - `Task<string> CreateSupplierAsync(string name, string? code=null)`  (get-or-create by name/code)
  - `Task<PurchaseRecordResult> RecordPurchaseAsync(RecordPurchaseRequest request)`
  - (Optional) item picker: **reuse `IInventoryQueryService.GetProductsAsync`** for picking existing
    products/variants — don't duplicate it. The "auto-fill quantity/cost" uses the variant's current
    `Cost` from `ProductVariant` (field confirmed: `ProductVariant(Id, Label, Sku, Stock, ReorderPoint,
    long Cost, long Price, Expiry)`).

### Client.Services to add — `src/Animora.Client.Services/Purchasing/` (new folder)
`PurchasingService.cs` implementing `IPurchasingService` over the dispatcher (same scope-per-call
pattern as the other services — copy the ctor/`SendAsync`/`QueryAsync` shape from
`src/Animora.Client.Services/Sales/SalesQueryService.cs`):
- `GetSnapshotAsync` → dispatch `PurchasingSnapshotQuery`, map `PurchasingSnapshotReadModel` → DTO
  (Jalali date via the shared Jalali formatter used in other mappers; grep `SalesMapping` for the date
  helper).
- `FindSuppliersAsync`/`CreateSupplierAsync` → dispatch the new supplier query/command.
- `RecordPurchaseAsync` → build `GoodsReceiptLineInput` per line (`QuantityScaled = Quantity*1000`,
  `UnitCostMinor = UnitCost`), dispatch `ReceiveGoodsCommand(SupplierId, lines, PurchaseOrderId:null,
  InvoiceReference: request.InvoiceReference)`. Map the result; translate error codes
  (ProductNotFound/VariantNotFound/etc.) to stable strings for Desktop mapping.
- **Purchase price extraction:** recording the receipt writes a `Receipt` movement with the unit cost,
  but that updates the **movement ledger / on-hand**, not necessarily the variant's displayed `Cost`.
  **Decision:** after a successful receipt, also update the product variant's `Cost` to the purchased
  unit cost so it surfaces as the product's purchase price in the catalog. Do this by calling the
  existing Inventory save path — `IInventoryQueryService.SaveProductInfoAsync` /
  the variant update — OR, cleaner, check whether the Inventory read already derives `Cost` from the
  latest receipt movement (grep `PurchasingQueries`/`InventoryQueries`/the inventory mapper for how
  `Cost` is computed). **If `Cost` is already derived from the latest receipt cost, no extra write is
  needed** (preferred — confirm first). If it's a stored field, update it via the inventory service after
  the receipt. Record the chosen path in the PR.

### DI registration — `src/Animora.Desktop/App.axaml.cs`
Register `IPurchasingService → PurchasingService` next to the other `Animora.Client.Services`
registrations (grep `ISalesQueryService` registration line and mirror it). The page VM gets it via ctor
DI.

### Desktop UI — new tab in the Inventory page
**Files:** `src/Animora.Desktop/Pages/Inventory/InventoryPageView.axaml` +
`InventoryPageViewModel.cs`, plus a new `PurchaseInvoiceModalView(.axaml/.axaml.cs)` +
`PurchaseInvoiceModalViewModel.cs` in the same folder, and a `PurchasesTabView.axaml`(+`.cs`) if the tab
body is large (mirror how `InventorySettingsView` is split out — it's a separate UserControl bound into
the page via `IsVisible="{Binding IsSettingsTab}"`).

- **Tabs:** the page builds a `Tabs` collection in the VM ctor (~line 67):
  `Tabs.Add(new InventorySubnavViewModel("products","کالاها","IconBox")); Tabs.Add(new
  InventorySubnavViewModel("settings","تنظیمات کالاها","IconTools"));` and tracks
  `[ObservableProperty] string _activeSubnav="products"` with `IsProductsTab`/`IsSettingsTab` computed
  and notified in `OnActiveSubnavChanged`. **Add** a `purchases` tab:
  `Tabs.Add(new InventorySubnavViewModel("purchases","خرید و تأمین","IconTruck"|"IconShoppingCart"));`
  (confirm an available icon key), add `IsPurchasesTab => ActiveSubnav=="purchases"`, notify it in
  `OnActiveSubnavChanged`, and keep `ShowHeaderNew => IsProductsTab` (or show a «ثبت خرید» header button
  when `IsPurchasesTab`).
- **Tab body (`IsVisible="{Binding IsPurchasesTab}"`):** KPIs (active orders / month spend / supplier
  debt from `GetSnapshotAsync`) + a table of recorded purchases (`PurchaseListItem` rows) + a «ثبت فاکتور
  خرید / خرید جدید» button that opens `PurchaseInvoiceModalViewModel`.
- **Data load:** the page already calls `RefreshOnDataChange(changes, …, DataDomain.Purchasing)` (line
  ~74) so purchases refresh automatically. Load the snapshot inside `ReloadPageAsync` when
  `IsPurchasesTab` (or always; it's cheap — ≤3 SELECTs) and publish the rows.
- **Purchase-invoice modal** (`PurchaseInvoiceModalViewModel`):
  - Supplier selector: search via `FindSuppliersAsync`, with an inline «تأمین‌کنندهٔ جدید» (name) →
    `CreateSupplierAsync`.
  - Line editor: each line either **picks an existing product/variant** (search via
    `IInventoryQueryService.GetProductsAsync`; on select auto-fill its current `Cost` as the default unit
    cost and default quantity 1) **or** is a **new product** (opens/embeds the existing new-product flow:
    call `IInventoryQueryService.CreateProductAsync(NewProductRequest)` first, then use the returned
    product/variant id as a normal line). Columns: کالا، تنوع، تعداد، قیمت خرید واحد، جمع. Add/remove
    lines; compute grand total.
  - Optional invoice reference (شمارهٔ فاکتور تأمین‌کننده) → `InvoiceReference`.
  - Submit → `RecordPurchaseAsync` → on success toast + `IDataChangeNotifier.Notify(DataDomain.Inventory,
    DataDomain.Purchasing, DataDomain.History, DataDomain.Statistics?)` and close; the inventory list &
    history & dashboard/stats refresh via the notifier.
- **History integration:** `ReceiveGoodsCommand` is `[Auditable(AuditCategory.Inventory, …)]` so it lands
  in main History automatically (kind maps to Inventory/Purchase). Per-product inventory history: the
  `Receipt` movement is read by `IInventoryQueryService.GetHistoryAsync`, so the purchase shows as an
  `in` movement on the product's History tab automatically — **verify** the movement's `Source` renders
  as a purchase/receipt label.
- **Statistics:** stats read the ledger/movements; recording receipts feeds them. No extra wiring beyond
  notifying `DataDomain` so the stats page reloads — **verify** the stats page subscribes to Inventory/
  Purchasing (grep `Pages/Statistics/*ViewModel.cs` for `RefreshOnDataChange`).

### Scenarios to cover (acceptance)
1. Purchase of an **existing** product/variant → stock increases, unit cost recorded, purchase price
   reflected, rows in main+inventory history, KPIs update.
2. Purchase that **creates a new product** inline → product appears in catalog with the entered
   cost/price, stock = purchased qty.
3. Mixed invoice (several lines, some existing + one new) in a single receipt.
4. New supplier created inline, then used on the invoice.
5. (Optional) purchase referencing an existing submitted PO — defer unless trivially supported.

### Build/verify
- After Contracts/Application/Persistence/Client.Services edits run `scripts/sandbox/build.sh full` →
  0/0. Then `scripts/sandbox/build.sh` for the Desktop. **Do not** run the full test suite or smoke run.
- Because this is a large change, **push in logical chunks** (locked rule allows/encourages pushing
  heavy work): e.g. (a) chain `AddPurchasingUseCases` + supplier query/command + repo `AddSupplier`;
  (b) Contracts seam; (c) Client.Services impl + DI; (d) Desktop tab + modal. Push after each chunk.

### PUSH (per chunk; final chunk message)
`scripts/git/push.sh "feat(inventory): [T6] supplier purchases/invoices tab — record receipts, stock+cost+history+stats integration"`

---

## T_FINAL — remove this plan file

Only after **all** of T1–T6 are `[x]` done and pushed:
```
rm Plan/session-tasks-2026-10-07.md
scripts/git/push.sh "chore(plan): [T_FINAL] remove completed session task plan"
```
Do **not** touch the other files in `Plan/` (they are unrelated pre-existing plans) and do **not** touch
any roadmap file.

---

## Quick reference — confirmed file paths (no re-search needed)

| Concern | Path |
|---|---|
| Patients page view / VM | `src/Animora.Desktop/Pages/Patients/PatientsPageView.axaml` · `PatientsPageViewModel.cs` |
| Shared confirm dialog | `src/Animora.Desktop/Pages/Sales/Shared/ConfirmDialogView.axaml` · `ConfirmDialogViewModel.cs` |
| Modal host (width) | `src/Animora.Desktop/Controls/ModalHost.axaml(.cs)` |
| Inventory delete-category call | `src/Animora.Desktop/Pages/Inventory/InventoryPageViewModel.cs` (~line 415–427) |
| Inventory page view / settings view | `src/Animora.Desktop/Pages/Inventory/InventoryPageView.axaml` · `InventorySettingsView.axaml` |
| Payment modal view / VM | `src/Animora.Desktop/Pages/Sales/PaymentModalView.axaml` · `PaymentModalViewModel.cs` |
| Sales presentation (labels/icons/money) | `src/Animora.Desktop/Pages/Sales/SalePresentation.cs` |
| Sales contracts | `src/Animora.Contracts/Sales/SalesModels.cs` · `ISalesQueryService.cs` |
| Sales service impl | `src/Animora.Client.Services/Sales/SalesQueryService.cs` · `SalesMapping.cs` |
| Cheques contracts | `src/Animora.Contracts/Cheques/ChequeModels.cs` · `IChequesService.cs` |
| Cheques service impl | `src/Animora.Client.Services/Cheques/ChequesService.cs` · `ChequesMapping.cs` |
| Purchasing domain | `src/Animora.Domain/Purchasing/*` |
| Purchasing use cases | `src/Animora.Application/UseCases/Purchasing/*` (Commands/Queries/ReadModels/Registration) |
| Purchasing ports | `src/Animora.Application/Ports/IPurchasingQueries.cs` · `IPurchasingRepository.cs` |
| Purchasing persistence | `src/Animora.Persistence.Sqlite/Repositories/PurchasingRepository.cs` · `Queries/PurchasingQueries.cs` · `Configurations/Purchasing/*` |
| Inventory contracts | `src/Animora.Contracts/Inventory/InventoryModels.cs` · `IInventoryQueryService.cs` |
| History contracts | `src/Animora.Contracts/History/HistoryModels.cs` (has `Purchase` + `Cheque` + `Inventory` kinds) |
| Data-change notifier | `src/Animora.Desktop/Services/IDataChangeNotifier.cs` (`DataDomain` enum incl. `Purchasing`) |
| DI composition root (Desktop) | `src/Animora.Desktop/App.axaml.cs` |
| App use-case registration chain | `src/Animora.Application/ApplicationServiceCollectionExtensions.cs` (add `.AddPurchasingUseCases()`) |
