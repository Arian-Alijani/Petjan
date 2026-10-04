# Plan — Sales / Visits / Boarding remediation (2026-10-04)

> **Self-contained execution plan.** A fresh model must implement every task below **without
> re-auditing the repo**. All file paths, exact current code, root-cause analysis, chosen fix,
> and acceptance criteria are embedded here. Read §0 once, then execute tasks T1→T7 top-to-bottom.
>
> **LOCKED RULE — push after every task:** after completing **each** task, run
> `scripts/git/push.sh "type(scope): message"`. **Not pushing = project death.** If it exits code
> `3` (no credentials) call the `setup_github_environment` tool once, then re-run. Exit `2` =
> rebase conflict (repo left clean) — resolve then re-run.
>
> **Heavy-fix rule:** if a task's edit grows large/heavy mid-way, push it immediately with a clear
> message before continuing.
>
> **Final task (T8):** delete **only** this plan file. Do not touch other docs/roadmap files.

---

## 0. Context (read once)

- **App:** Animora — vet clinic desktop app, **.NET 10, Avalonia + MVVM**, local encrypted SQLite.
- **Build (fast, Desktop only):** `scripts/sandbox/build.sh` → must be **0 warnings / 0 errors**.
  **No full build / no full test run** per user rule. Make focused edits only.
- **Data-flow rule (never break):** View → ViewModel → `Animora.Contracts` interface → real
  `Animora.Client.Services` impl → `Animora.Application` dispatch (permission/capability/validation/
  journal) → Domain + SQLite. ViewModels never touch EF/SQL. Seed data lives in Persistence seeders.
- **RTL / Persian digits / Jalali** helpers: `src/Animora.Desktop/Localization/` and
  `PersianNumberFormatter` (`ToPersianDigits` / `ToAsciiDigits`).

### Shared seam fact (needed for T2, T3)
The Client.Services seam turns a failed `Result` into a thrown **`ServiceException`**
(`src/Animora.Client.Services/ServiceException.cs`), which **derives from
`InvalidOperationException`** and carries `.Code` (raw `SCREAMING_SNAKE` error code) + `.Category`.
- ViewModels that `catch (InvalidOperationException)` **do** catch it (no crash).
- **But** some call sites show the **raw code** straight to the user (e.g. `_toast.Show(result.Error …)`),
  so the operator sees an English code like `CASH_SESSION_REQUIRED` instead of Persian text.
- The Persian mapper to reuse: `PatientsPresentation.CommandFailureMessage(ex, fallback)`
  (`src/Animora.Desktop/Pages/Patients/PatientsPresentation.cs`) maps `ServiceException.Category`
  → Persian message with a fallback.

### Error-code → Persian: the single place to extend
Where a **result-level** (not exception) raw code must become Persian, add a small mapping helper
next to the call site (or extend the presentation class for that page). Prefer **one** reusable
switch over `ServiceException.Code` / result error code → Persian, used by both Sales and Cashier.

---

## Task index & status

| ID | Title | Status |
|----|-------|--------|
| T1 | Visit-type filters show English → use Persian labels from appointment settings | ☑ DONE |
| T2 | "Start visit" often fails — root-cause & fix | ☑ DONE |
| T3 | Pay / full-settle shows raw `CASH_SESSION_REQUIRED` in Sales & Cashier | ☑ DONE |
| T4 | Boarding add product/service picker opens *behind* (clipped) — make it a modal like Sales | ☑ DONE |
| T5 | All displayed statuses/texts must be Persian (enum/code leaks) | ☑ DONE |
| T6 | Standardize & unify invoice / file / all such numbers | ☐ TODO |
| T7 | Verify Sales & Cashier history shows data correctly | ☐ TODO |
| T8 | **Final:** delete this plan file | ☐ TODO |

---

## T1 — ☑ DONE — Visit-type filters: English → Persian labels synced with appointment settings

**Symptom (user):** In **Visits** and **Pet file**, the visit-type filter chips show English text.
They must show the **Persian** labels and stay in sync with the visit/appointment types created in
**Settings → Appointments (نوبت‌ها)**.

**Root cause (verified):**
`src/Animora.Client.Services/Visits/VisitTypeCatalog.cs` builds the list with the **code as the label**:
```csharp
public static IReadOnlyList<VisitTypeInfo> All { get; } = AppointmentTypeCatalog.All
    .Select(type => new VisitTypeInfo(type.Code, type.Code, "IconBriefcase", "#0d9488",
        type.DefaultDurationMinutes)).ToArray();
```
`VisitTypeInfo` signature (`src/Animora.Contracts/Visits/VisitsTriageModels.cs`): `(Id, Label, Icon, Color, DefaultDurationMinutes)` — so **arg #2 is the display Label** and it is set to the English `type.Code` (e.g. `CHECKUP`).

The Persian labels already exist in
`src/Animora.Client.Services/Appointments/AppointmentsMapping.cs` → `TypeLabel(string code)`:
`CHECKUP→"معاینه"`, `VACCINE→"واکسیناسیون"`, `SURGERY→"جراحی"`, `DENTAL→"دندان‌پزشکی"`,
`GROOMING→"آرایش و شست‌وشو"`, `FOLLOWUP→"پیگیری"`, `CONSULT→"مشاوره"`, `_→"سایر"` (but it is `private`).

Two gaps:
1. **Built-in types** render their code instead of the Persian label.
2. **Custom types created in Settings → Appointments** (`AppointmentSettings.CustomTypes`,
   `src/Animora.Contracts/Appointments/AppointmentSettingsModels.cs`, persisted via
   `src/Animora.Client.Services/Appointments/AppointmentSettingsFileStore.cs`) are **not included
   at all** in the visit-type filter list.

**Consumers (so the fix flows through):**
- `VisitsTriagePageViewModel.cs` → `foreach (var type in _service.GetVisitTypes())` (builds chips)
  and filters booked rows by `row.TypeId == ActiveTypeFilter` (so the chip **Id** must still equal
  the type code; only the **Label** changes). `ResolveType` fallback label is already `"ویزیت عمومی"`.
- `VisitsTriageService.GetVisitTypes()` returns `VisitTypeCatalog.All`.
- Pet-file visit filter consumes the same `GetVisitTypes()` source (same catalog) — fixing the
  catalog fixes both screens.

**Fix (minimal, keeps Ids = codes):**
1. In `AppointmentsMapping.cs`, change `TypeLabel` from `private` to **`internal static`** (so the
   visits layer can reuse the single source of truth). Do **not** duplicate the switch.
2. In `VisitTypeCatalog.cs`, set the label to the Persian value:
   ```csharp
   .Select(type => new VisitTypeInfo(type.Code, AppointmentsMapping.TypeLabel(type.Code),
       "IconBriefcase", "#0d9488", type.DefaultDurationMinutes))
   ```
   (Add `using Animora.Client.Services.Appointments;` — verify exact namespace of `AppointmentsMapping`.)
3. **Custom types from settings:** `VisitTypeCatalog.All` is a static property (no I/O), so custom
   types cannot be injected there. Instead extend **`VisitsTriageService.GetVisitTypes()`** to
   **merge** the built-in Persian catalog with the tenant's `AppointmentSettings.CustomTypes`
   (each custom type already has a Persian `Label` + `Id`). Read settings through the same store the
   Appointments page uses (`AppointmentSettingsFileStore` / its Contracts interface — confirm the
   injected interface name). Map each custom type to `new VisitTypeInfo(custom.Id, custom.Label,
   "IconBriefcase", "#0d9488", custom.DurationMinutes or default)`. De-dupe by Id (built-ins win on
   label if codes collide). Keep the method cheap (settings read is a small file / cached).
   - If injecting the settings store into `VisitsTriageService` is heavy, acceptable fallback:
     return built-ins with Persian labels now (fixes the visible English bug) **and** leave a
     clearly-marked TODO for custom-type merge. Prefer the full merge if the store is already DI-available.

**Acceptance:**
- Visits + Pet-file type chips read Persian (معاینه، واکسیناسیون، …), never English codes.
- A custom type added in Settings → Appointments appears as a filter chip with its Persian label.
- Filtering still works (chip Id == stored `TypeId`); build 0/0.

**Push:** `scripts/git/push.sh "fix(visits): T1 Persian visit-type filter labels synced with appointment settings"`

---

## T2 — ☑ DONE — "Start visit" frequently fails with a generic failure toast

**Symptom (user):** Clicking **شروع ویزیت (start visit)** often shows "شروع ویزیت ناموفق بود".

**Root cause (verified — a state mismatch between the booked rail and the start handler):**
- Booked-rail query `src/Animora.Persistence.Sqlite/Queries/TriageQueries.cs` (line ~43) lists
  appointments where **only** `State != Cancelled`. So **`NoShow`** and **`Completed`** appointments
  can still appear as "booked" rows.
- Start handler `src/Animora.Application/UseCases/Visits/StartVisitCommands.cs` (line ~73) rejects:
  ```csharp
  if (appointment is { State: AppointmentState.NoShow or AppointmentState.Cancelled
      or AppointmentState.Completed }) return Result<...>.Fail(ErrorCodes.TicketStateInvalid);
  ```
  → Clicking start on a NoShow/Completed row ⇒ `TICKET_STATE_INVALID` ⇒ generic failure toast.
- Secondary path: `DoctorNotAvailable` (lines ~57, ~60, ~158) when the selected doctor is not an
  **Active** `StaffRole.Doctor` membership. The doctor rail query already filters
  `Role == Doctor && Status == Active`, so this is less likely but must still surface a clear message.

**Fix (two parts):**
1. **Align the booked rail with what can actually start.** In `TriageQueries.cs` booked query, also
   exclude `NoShow` and `Completed` (mirror the top query at line ~26 which already excludes both):
   ```csharp
   && appointment.State != AppointmentState.Cancelled
   && appointment.State != AppointmentState.NoShow
   && appointment.State != AppointmentState.Completed
   ```
   This removes the rows that are guaranteed to fail on start. Keep `InProgress`/bookable states.
2. **Persian, specific error messages** in `VisitsTriagePageViewModel.cs` start commands
   (`StartFromTicketAsync` and `StartNewAsync`, both under `RunGuardedAsync`). The guard catches the
   `ServiceException`; map `.Code` / `.Category` to a specific Persian message instead of only the
   generic toast:
   - `TICKET_STATE_INVALID` → "این نوبت دیگر قابل شروع نیست (لغو/عدم‌حضور/تکمیل‌شده)."
   - `DOCTOR_NOT_AVAILABLE` → "پزشک انتخاب‌شده در دسترس نیست."
   - `WALK_IN_PHONE_REQUIRED` → "برای ویزیت بدون نوبت، شماره تماس لازم است."
   - fallback → existing "شروع ویزیت ناموفق بود".
   Reuse `PatientsPresentation.CommandFailureMessage(ex, fallback)` if it already covers these
   categories; otherwise add a tiny local `switch` on `ServiceException.Code`.

**Acceptance:** Booked rail only lists startable rows; starting a valid booked/walk-in visit
succeeds; failures show a specific Persian reason, never a bare English code. Build 0/0.

**Push:** `scripts/git/push.sh "fix(visits): T2 start-visit root cause — align booked rail with startable states + Persian errors"`

---

## T3 — ☑ DONE — Pay / full-settle shows raw `CASH_SESSION_REQUIRED` in Sales & Cashier

**Symptom (user):** Pressing **پرداخت (pay)** / **تسویه کامل (full settle)** in **Sales (فروش)** and
**Cashier (صندوق)** pops a notification reading like "cash session required" (user wrote it
phonetically as «کش ... ریکوارد» — it is the English code, **not** a cache error).

**Root cause (verified):**
`src/Animora.Application/UseCases/Sales/RegisterPaymentCommand.cs` requires an **open cash session**
when the method is **Cash** (the default method in both screens):
```csharp
if (c.Method == PaymentMethod.Cash) {
    drawer = await sessions.GetOpenSessionAsync(context.TenantId, context.UserId, ct);
    if (drawer is null || (sessionId is not null && sessionId != drawer.Id))
        return Result<Unit>.Fail(ErrorCodes.CashSessionRequired);   // CASH_SESSION_REQUIRED (Validation)
    ...
}
```
`ICashSessionContext` **is** registered (`LocalStoreServiceCollectionExtensions.cs` line ~127), so
with **no open session for the current user**, every cash payment fails. Default method is Cash in
both screens:
- Cashier: `CashierPageViewModel.Pay()` line ~524 → `_toast.Show(result.Error ?? "خطا در ثبت پرداخت", Tone.Danger)` — shows **raw** `CASH_SESSION_REQUIRED`.
- Sales: `PaymentModalViewModel` (`Methods.Add(... PaymentMethod.Cash, true)` default) → line ~221 shows `result.Error ?? "خطا…"` — same raw leak.

A session **can** be opened (`src/Animora.Application/UseCases/Cashier/CashSessionCommands.cs`) and
demo sessions are seeded (`FinanceSampleData.cs`: 3 sessions, last left **Open** for `SeedUser`), but
**the Cashier screen exposes no UI to open a session**, and `GetOpenSessionAsync` filters by
`CashierUserId == userId` — so a non-seed user (or real tenant) always hits the failure.

**Fix (two layers — do both):**
1. **Never leak the raw code.** In both `CashierPageViewModel.Pay()` and `PaymentModalViewModel` pay
   path, translate the result error to Persian before showing. Add one shared helper (e.g. extend the
   presentation class) mapping result error codes → Persian, at minimum:
   - `CASH_SESSION_REQUIRED` → "برای پرداخت نقدی باید نشست صندوق باز باشد."
   - plus the common ones: `INVOICE_NOT_POSTED`, `PAYMENT_EXCEEDS_DUE`, `INVOICE_ALREADY_SETTLED`
     (confirm exact codes in `ErrorCodes.cs`) → sensible Persian.
   - fallback → existing "خطا در ثبت پرداخت".
2. **Make cash payable without a manual session in the local desktop flow.** Decide ONE (prefer A):
   - **(A) Ensure-open-on-pay (recommended):** before registering a cash payment, if no open session
     exists for the user, **auto-open** one via the existing cash-session open command (silent,
     zero opening float) so the single-operator desktop flow "just works". Implement in the
     Client.Services layer (`CashierService.PayAsync` / `SalesQueryService` pay path through
     `InvoicePaymentCore`) — not in the ViewModel. Guard so it only auto-opens for the current
     tenant+user and only when the chosen method is Cash.
   - **(B) Add an "open cash session" affordance** in the Cashier UI and require the operator to open
     it — more work, worse UX for a single-operator clinic. Only if (A) is deemed unsafe.
   Whichever is chosen, **layer-1 Persian mapping is still mandatory** as a safety net.

**Acceptance:** Cash pay / full-settle succeeds in Sales and Cashier without the operator manually
opening a session (option A), and any remaining failure shows Persian text — never a raw English
code. Build 0/0.

**Push:** `scripts/git/push.sh "fix(cashier,sales): T3 cash pay no longer fails on missing session; Persian error mapping"`

---

## T4 — ☑ DONE — Boarding "add product/service" picker opens behind / won't open; make it a modal like Sales

**Symptom (user):** On a boarded animal's stay, the control to pick a product/service "goes
underneath" and never opens. Wanted: same open behavior as the Sales/Cashier catalog picker.

**Root cause (verified):**
`src/Animora.Desktop/Pages/Boarding/StayDetailModalView.axaml` (lines ~386–418) uses an **in-modal
`Popup`** (`IsOpen="{Binding IsAddDropdownOpen}"`, `PlacementTarget` = `#AddPickerButton`) nested
inside the stay-detail modal. The modal is hosted by `DialogService.ShowModal` inside
`src/Animora.Desktop/Controls/ModalHost.axaml`, whose inner `Border x:Name="Panel"` has
**`ClipToBounds="True"`** (line ~21). The picker sits near the bottom of the modal, so the popup
extends past the panel bounds and is **clipped/hidden** ("رفته زیر").

Sales does it the clean way: a **separate modal** `CatalogModalViewModel` /
`CatalogModalView.axaml` opened via `_dialogs.ShowModal(...)` — no nested popup, no clipping.

**Fix — mirror Sales:** replace the in-modal `Popup` picker with a **dedicated catalog modal**.
- Reuse `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs` + `CatalogModalView.axaml` if its
  item shape (catalog items with `Ref`/`Title`/price) is compatible with Boarding's add source;
  otherwise create a small `BoardingCatalogModalViewModel` modeled on it.
- Wire the boarding "+ افزودن" button to `_dialogs.ShowModal(new <CatalogModal>VM(...), width)`;
  on selection, call the **existing** `StayDetailModalViewModel.AddItemAsync` logic with the chosen
  item's `Ref` + quantity (the add/commit code at lines ~349–376 stays, including its
  `catch (InvalidOperationException)` → `PatientsPresentation.CommandFailureMessage` Persian toast and
  `LoadAsync()` refresh). Keep `SelectedAddOption`/`AddQuantityText` semantics or pass the picked item
  straight through.
- Remove the now-dead `Popup`, `IsAddDropdownOpen`, `ToggleAddDropdown` and the inline options list
  from view + VM (confirm names before deleting; don't leave dangling bindings).
- The Boarding add source must feed the same catalog the modal expects — reuse the boarding service's
  catalog/add-options provider as the modal's item source.

**Acceptance:** On a boarded animal, "+ افزودن" opens a proper centered modal (identical feel to
Sales/Cashier), fully visible (not clipped), lets the operator search & pick, and the item is added
to the stay; failures show Persian. Build 0/0. This is a larger change — **push it on completion**
(or sooner if it grows heavy).

**Push:** `scripts/git/push.sh "fix(boarding): T4 replace clipped in-modal popup with Sales-style catalog modal"`

---

## T5 — ☑ DONE — All displayed statuses/texts must be Persian (enum/code leaks)

**Symptom (user):** Some statuses/texts (invoice items, states, etc.) still render English.

**Known leak points (verified) + method:**
- **Cashier/Sales history `Kind`:** the SQLite projection emits English kind strings
  (`"Payment"`, `"Refund"`, `"Void"`, `"Accepted"`) — `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs`
  `GetHistoryAsync`. These are converted for **display** in `SalesMapping.ToContract(zone)` /
  `HistoryFilterKind` (`src/Animora.Client.Services/Sales/SalesMapping.cs` ~255–300). Verify the
  **display** label is Persian everywhere the row is shown (chips, rows, CSV should keep machine
  codes but UI must be Persian). See T7 for the filter-value side.
- **General rule:** any `enum.ToString()` or raw error `.Code` reaching a `Text=`/toast is a leak.
  Audit (targeted, not full-repo) these high-traffic presentation files and ensure each enum→label
  goes through a Persian mapper:
  - `src/Animora.Desktop/Pages/Sales/SalePresentation.cs`, `CashierPresentation.cs`
  - invoice **state** labels, payment **method** labels, history **kind** labels
  - `PaymentMethod`, `InvoiceState`/`CashierInvoiceState`, `VisitState`, boarding stay state.
- **Error codes:** fixed structurally in T2 (visits) and T3 (pay). Apply the same "map code→Persian
  before display" rule anywhere else a `result.Error`/`ex.Code` is shown raw (grep for
  `_toast.Show(result.Error` and `.Show(ex.Message`).

**Fix:** add/extend Persian label switches in the relevant `*Presentation.cs` and mapping files;
replace any direct `enum.ToString()`/raw-code bindings with the mapper. Prefer **one** Persian label
helper per concept (method, state, kind) reused across Sales + Cashier.

**Acceptance:** No English enum/state/kind/error text is visible in Sales, Cashier, Visits, Boarding
invoice/item/status surfaces. Build 0/0.

**Push:** `scripts/git/push.sh "fix(ui): T5 Persian labels for all displayed statuses/states/kinds"`

---

## T6 — Standardize & unify invoice / file / all such numbers

**Symptom (user):** Invoice numbers, file/record numbers, and all such identifiers must be
standardized, unified and sensible.

**Current state (verified — inconsistent schemes):**
| Entity | Allocator | Current format |
|--------|-----------|----------------|
| Invoice | `src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs` | `YY-NNNNNN` (e.g. `04-000001`), **no prefix** |
| Expense | `src/Animora.Persistence.Sqlite/Finance/ExpenseNumberAllocator.cs` | `EXP-YY-NNNNNN` |
| Journal | `src/Animora.Persistence.Sqlite/Finance/JournalNumberAllocator.cs` | `periodCode-NNNNN` (**5** digits) |
| Reception ticket | `ReceptionTicketCommands.cs` / `QueueEntryResolver.cs` | plain incrementing int |

All share `LocalSequenceAllocator` for atomic sequence values. The inconsistency: invoices lack a
type prefix while expenses have `EXP-`; journal uses 5 digits vs invoice/expense 6; tickets are bare
ints with no period scoping.

**Fix — one consistent scheme:** adopt a uniform pattern **`<PREFIX>-<YY>-<NNNNNN>`** (6-digit,
year/period-scoped sequence), applied consistently:
- Invoice → `INV-YY-NNNNNN`
- Expense → `EXP-YY-NNNNNN` (already close; just normalize digit width to 6)
- Journal → `JV-YY-NNNNNN` (widen to 6 digits)
- Reception ticket → keep a short human daily counter **if** it is a UI queue number, but if it is a
  persisted document id, bring it under the same `TKT-YY-NNNNNN` scheme. **Decide by checking whether
  the ticket number is shown as a document id or a daily queue position** before changing — do not
  break the daily-queue UX.

Implementation notes:
- Centralize the format in a single helper (e.g. a `DocumentNumber.Format(prefix, periodCode, seq)`
  using `CultureInfo.InvariantCulture` and `:000000`) and call it from each allocator so the pattern
  can never drift again. Keep digits **ASCII** at storage level; UI applies `ToPersianDigits` for
  display only.
- **Migration/back-compat:** existing rows keep their old numbers (do **not** rewrite history). Only
  **new** allocations use the unified format. If any UI parses the old format, make the parser
  tolerant of both.
- Verify display sites (`SalePresentation.cs`, `CashierPresentation.cs`, print documents) render the
  number via Persian digits where appropriate and don't assume a fixed length.

**Acceptance:** New invoices/expenses/journals (and tickets if in scope) share the
`PREFIX-YY-NNNNNN` shape; one helper owns the format; old data untouched; build 0/0.

**Push:** `scripts/git/push.sh "refactor(numbering): T6 unify document numbers to PREFIX-YY-NNNNNN"`

---

## T7 — Verify Sales & Cashier history shows data correctly

**Symptom (user):** Ensure the **history (تاریخچه)** section in Sales & Cashier works and actually
displays data.

**Verified facts:**
- Both screens back their history with the **same** query:
  `SalesQueryService.GetHistoryAsync(CashierHistoryFilter)` →
  `SalesHistoryQuery` → `SalesQueries.GetHistoryAsync(...)` (SQLite). The client method is
  **well-guarded** (returns an empty page instead of throwing).
- `SaleHistoryViewModel.ReloadAsync` is invoked fire-and-forget (`_ = ReloadAsync(...)`) and has an
  `IsLoading` finally but **no `catch`**. Because `GetHistoryAsync` swallows failures and returns an
  empty page, this won't crash — but it means a backend problem shows as a **silently empty list**
  rather than an error. Add a defensive `catch` that surfaces a Persian toast so "empty due to error"
  is distinguishable from "genuinely no rows".
- **Filter mismatch (data-visibility bug):** `SalesMapping.HistoryFilterKind` maps
  `CashierHistoryKind.Edited → "Edited"` and `.Deleted → "Deleted"`, but the SQLite projection only
  ever produces `"Payment" | "Refund" | "Void" | "Accepted"`. So selecting the **Edited** or
  **Deleted** chip filters to a value that matches **no** rows ⇒ history looks empty. Also confirm the
  multi-select logic: filter kind is applied only when exactly one kind is selected (count == 1) —
  verify multi-select shows all, and single-select of a non-existent kind is the only empty case.

**Fix:**
1. Remove (or hide) the **Edited** / **Deleted** filter chips in Sales/Cashier history, OR make the
   projection actually emit those kinds if they are meaningful. Simplest correct fix: **drop the
   chips that can never match** so the user never lands on an always-empty filter.
2. Add a `try/catch` in `SaleHistoryViewModel.ReloadAsync` → on failure show a Persian toast
   ("بارگذاری تاریخچه ناموفق بود") and leave the list empty intentionally.
3. Confirm the Jalali range chips (`today`, `7`, …) map to the correct UTC window passed to
   `SalesHistoryQuery(FromUtc, ToUtc)` so "today/7 days" actually include recent rows (quick sanity:
   seed/new payment appears under "امروز").
4. Ensure displayed **kind** labels are Persian (ties into T5).

**Acceptance:** Opening history in Sales and Cashier lists recent invoices/payments/refunds/voids
with Persian labels; no filter chip is permanently empty; a backend error shows a Persian toast
instead of a silent blank. Build 0/0.

**Push:** `scripts/git/push.sh "fix(history): T7 remove always-empty filter kinds + surface load errors"`

---

## T8 — FINAL: delete this plan file

After T1–T7 are complete and pushed:
```bash
rm Plan/sales-visits-boarding-remediation.md
scripts/git/push.sh "chore(plan): T8 remove completed Sales/Visits/Boarding remediation plan"
```
Do **not** delete or modify any other file.

---

## Quick file map (for fast navigation)

| Concern | Path |
|---|---|
| Visit-type catalog (T1) | `src/Animora.Client.Services/Visits/VisitTypeCatalog.cs` |
| Persian type labels (T1) | `src/Animora.Client.Services/Appointments/AppointmentsMapping.cs` (`TypeLabel`) |
| Appointment settings / custom types (T1) | `src/Animora.Contracts/Appointments/AppointmentSettingsModels.cs`, `src/Animora.Client.Services/Appointments/AppointmentSettingsFileStore.cs` |
| Visits triage VM / service (T1,T2) | `src/Animora.Desktop/Pages/Visits/VisitsTriagePageViewModel.cs`, `src/Animora.Client.Services/Visits/VisitsTriageService.cs` |
| Booked rail query (T2) | `src/Animora.Persistence.Sqlite/Queries/TriageQueries.cs` |
| Start-visit handler (T2) | `src/Animora.Application/UseCases/Visits/StartVisitCommands.cs` |
| Register payment / cash session (T3) | `src/Animora.Application/UseCases/Sales/RegisterPaymentCommand.cs` |
| Cashier pay VM (T3,T5) | `src/Animora.Desktop/Pages/Cashier/CashierPageViewModel.cs`, `CashierPresentation.cs` |
| Sales pay modal (T3,T5) | `src/Animora.Desktop/Pages/Sales/PaymentModalViewModel.cs`, `SalePresentation.cs` |
| Cash session open cmd (T3) | `src/Animora.Application/UseCases/Cashier/CashSessionCommands.cs` |
| Modal host (clips popup) (T4) | `src/Animora.Desktop/Controls/ModalHost.axaml` |
| Sales catalog modal to mirror (T4) | `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs`, `CatalogModalView.axaml` |
| Boarding stay detail (T4) | `src/Animora.Desktop/Pages/Boarding/StayDetailModalView.axaml`, `StayDetailModalViewModel.cs` |
| Number allocators (T6) | `src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs`, `Finance/ExpenseNumberAllocator.cs`, `Finance/JournalNumberAllocator.cs`, `Commands/LocalSequenceAllocator.cs` |
| Sales/Cashier history (T7) | `src/Animora.Desktop/Pages/Sales/SaleHistoryViewModel.cs`, `src/Animora.Client.Services/Sales/SalesQueryService.cs` + `SalesMapping.cs`, `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs` |
| Error→Persian mapper (T2,T3,T5) | `src/Animora.Desktop/Pages/Patients/PatientsPresentation.cs` (`CommandFailureMessage`) |
| Error codes (all) | `src/Animora.Domain/Results/ErrorCodes.cs` |
