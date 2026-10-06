# Plan — Boarding prepay/postpay + discharge + catalog-visibility fix (2026-10-06)

> **Self-contained execution plan.** A fresh model must implement every task below **without
> re-auditing the repo**. All file paths, verified current code behavior, root cause, chosen fix,
> and acceptance criteria are embedded here. Read §0 once, then execute tasks T1→T9 top-to-bottom.
>
> **🔒 LOCKED RULE — push after EVERY task:** after completing **each** task below, immediately run
> `scripts/git/push.sh "type(scope): message"`. **Not pushing = project death.**
> - First push switches/creates the branch: `scripts/git/push.sh -b genspark_ai_developer "msg"`.
>   Subsequent pushes stay on it: `scripts/git/push.sh "msg"`.
> - Exit `3` (no credentials) → call the `setup_github_environment` tool once, then re-run the same command.
> - Exit `2` (rebase conflict, repo left clean) → resolve preferring remote, then re-run.
> - **If a heavy fix or a large code block is written mid-task, push it immediately** with a clear message — do not batch.
>
> **The final task (T9) deletes THIS plan file only.** Do **not** touch any other docs/roadmap files.

---

## 0. Context (read once, do not re-derive)

### 0.1 App + architecture
- **App:** Animora — self-hosted veterinary clinic management + accounting desktop app. **.NET 10**,
  Avalonia + MVVM (CommunityToolkit.Mvvm), Persian/RTL UI, Jalali calendar, local **encrypted SQLite**,
  offline-first (no server wired yet). All real work is under `src/`.
- **THE ONE RULE:** data flows **View → ViewModel → `Animora.Contracts` interface (ctor DI) → real
  `Animora.Client.Services` impl → `Animora.Application` dispatch (permission/validation/journal) →
  `Animora.Domain` + `Animora.Persistence.Sqlite`.** ViewModels depend only on Contracts interfaces.
  No mock/design-time branching in `Pages/**`. Seed lives in Persistence seeders.
- **Do NOT open** `Animora-UI/**`, `Database-Sampel/**`, `windows-frontend-roadmap/phases/**`,
  `backend-roadmap/phases/**`. They are irrelevant to these tasks and waste tokens.

### 0.2 Build / verify (SDK may need restore)
- dotnet SDK 10.0.x was installed to `/home/user/.dotnet` but may be absent in a fresh sandbox.
  If `dotnet` is missing, it is auto-provisioned by the sandbox prereq step; otherwise re-run the
  install from `.sandbox-install.log`. To build a single project (fast, do this — NOT a full solution build/test):
  `export PATH="$HOME/.dotnet:$PATH" && dotnet build src/Animora.Desktop/Animora.Desktop.csproj -v q --nologo`
- **No full-solution test runs. No long builds.** Build only the project(s) you touched (usually
  `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Desktop`). A build of `Animora.Desktop`
  transitively compiles everything it depends on, which is enough to catch compile breaks.

### 0.3 THE BOARDING DATA MODEL (verified — memorize this, it drives T1–T5)

**Domain state machine** (`src/Animora.Domain/Boarding/BoardingStay.cs`):
```
Reserved -> Active -> ReadyForInvoice -> Invoiced -> CheckedOut   (+ Cancelled from Reserved/Active)
```
- `BoardingStay.Reserve(... paymentMode)` creates in `Reserved`; `Activate(clock)` → `Active`.
  `CreateBoardingStayCommandHandler` always calls Reserve **then** Activate, so a new stay is `Active`.
- `PaymentMode` (`StayPaymentMode { Postpaid=0, Prepaid=1 }`) is stored on the aggregate and persisted
  (migration `__024_BoardingPaymentMode`).
- `RequestCheckout(clock)` → `ReadyForInvoice`. `AttachInvoice(id)` → `Invoiced`. `CompleteCheckout(clock)`
  → `CheckedOut` (requires InvoiceId set, else `STAY_INVOICE_REQUIRED`).
- `FrozenTotalMinor` is the auditable checkout snapshot, set by `FreezeTotalForCheckout`.
- `AreItemsLocked => State >= Invoiced`. Items editable only while `Active` (AddStayItem/RemoveStayItem
  handlers guard `State != Active`).

**The billing-handoff seam** (how a stay becomes a cashier invoice):
- `CheckoutStayCommandHandler` (`BoardingCommands.cs`): computes `StayChargeCalculator.Accrued`,
  `RequestCheckout` (→ReadyForInvoice), `FreezeTotalForCheckout`, then
  `handoffs.Add(tenantId, stayId, ownerId, petId, now, payloadJson)`. It does **NOT** itself invoice or
  CompleteCheckout — the stay stays in `ReadyForInvoice` until the cashier posts the invoice.
- `IBoardingBillingHandoffWriter.Add(...)` → `BoardingBillingHandoffWriter` →
  `BoardingBillingHandoff.Create(...)` (status `Ready`). Entity:
  `src/Animora.Persistence.Sqlite/Entities/BoardingBillingHandoff.cs`. Keyed by `StayId`.
  `HandoffStatus { Ready, Invoiced, Cancelled }`.
- A `Ready` handoff surfaces in the صندوق/cashier queue: `SalesQueries.cs` (`stays` leg, ~line 45,
  `h.Status == Ready`). The cashier creates a draft via `CreateInvoiceFromStayCommand(HandoffId)`
  (`CreateInvoiceFromStayCommand.cs`) → `SalesHandoffFactory.CreateAsync(... "Boarding" ...)` which reads
  `LinesJson` (`RoomRateMinor`, `TotalMinor`, `Items[]`) into invoice lines (room charge line + each item).
- Posting the invoice: `PostInvoiceCommand.cs` lines 47–114. It resolves `source="Boarding"` /
  `sourceId = invoice.BoardingStayId`, calls `posting.MarkHandoffInvoicedAsync("Boarding", stayId, invoiceId)`
  (finds the handoff by `StayId` — **singular**, `FirstOrDefaultAsync(h => h.StayId == sourceId)` in
  `SalesPostingData.cs`), then `stay.AttachInvoice` + `stay.CompleteCheckout` → stay becomes `CheckedOut`.
- ⚠️ **CRITICAL CONSTRAINT for T1/T4:** the handoff is keyed by `StayId` and the posting path assumes
  **exactly one Ready handoff per stay**. Any prepay design that creates a *second* handoff for the same
  stay (e.g. room-at-creation + extras-at-checkout) would break `MarkHandoffInvoicedAsync`/
  `GetHandoffStatusAsync` (they take the first row). See T1 fix for how we avoid this.

**Charge calculation** (`src/Animora.Domain/Boarding/StayChargeCalculator.cs`):
- `NightsBetween(start,end,tz)` = `Max(1, endDay.DayNumber - startDay.DayNumber)` — **calendar-day**
  difference in clinic tz. `Accrued(stay, services, asOf, tz)` uses `end = min(ActualCheckOut?, asOf)`,
  `nights = NightsBetween(CheckIn, end)`, `total = DailyRate*nights`, plus items (BoardingService PerDay ⇒
  `UnitPrice*nights`; else `UnitPrice.ApplyPercent(qty*100)`).
- ⚠️ **Inconsistency to confirm in T3:** `BoardingStay.BillableDays(endUtc)` uses
  `Max(1, ceil((end-CheckIn).TotalHours/24))` (24-hour windows), but `StayChargeCalculator.NightsBetween`
  uses calendar-day-number difference. These can differ (e.g. check-in 23:00, check-out next-day 02:00:
  ceil(3h/24)=1 vs dayNumber diff=1 → same here; but check-in 01:00 day N, checkout 23:00 day N:
  ceil(22h/24)=1, dayNum diff 0→Max(1)=1 → same; check-in day N 23:00, checkout day N+2 01:00:
  hours=26→ceil=2; dayNum diff=2 → same). They mostly agree but the authoritative rule the invoice uses
  is `NightsBetween` (calendar nights). The UI (`BoardingStayClassification.Of`, `StayDetailModalViewModel`)
  also uses calendar-day `DayNumber` diff for nights. **T3 must verify the three sites agree and the
  `StayReadyForInvoiceEvent` carries `BillableDays` which is the only ceil-hours user — confirm it is not
  used for money.** (It is only an event payload; money uses `Accrued`.)

**State → contract Status mapping** (`src/Animora.Client.Services/Boarding/BoardingMapping.cs` line ~55):
`State == Active ? BoardingStatus.Active : BoardingStatus.Done`. So **Reserved is filtered out of the
stay list upstream? NO** — `BoardingQueries.GetStaysAsync` returns ALL states (top 200 by CheckInUtc, no
state filter). `Transferred = State >= ReadyForInvoice`. Thus a stay in `ReadyForInvoice` / `Invoiced` /
`CheckedOut` all map to `Done` and currently appear on the management page. (This is T2's root cause.)

### 0.4 UI entry points (verified file map)
| Concern | File |
|---|---|
| New-stay modal (prepay/postpay radios) VM | `src/Animora.Desktop/Pages/Boarding/NewStayModalViewModel.cs` |
| New-stay modal view (payment radios markup) | `src/Animora.Desktop/Pages/Boarding/NewStayModalView.axaml` (payment block ~line 269–302, styles ~line 60–70) |
| Payment option VM | `BoardingModalItemViewModels.cs` → `BoardingPaymentOptionViewModel` |
| Stay detail modal VM (add items, checkout, print) | `StayDetailModalViewModel.cs` |
| Stay detail modal view | `StayDetailModalView.axaml` |
| Checkout confirm modal VM | `CheckoutModalViewModel.cs` |
| Management page (tabs, list, open-detail) VM | `BoardingPageViewModel.cs` |
| Tab/day classification | `BoardingStayClassification.cs` |
| Client service seam | `src/Animora.Client.Services/Boarding/BoardingQueryService.cs` |
| Contract interface | `src/Animora.Contracts/Boarding/IBoardingQueryService.cs` |
| Contract DTO | `src/Animora.Contracts/Boarding/BoardingStay.cs`, `BoardingDetail.cs` |
| Domain aggregate | `src/Animora.Domain/Boarding/BoardingStay.cs` |
| App commands | `src/Animora.Application/UseCases/Boarding/BoardingCommands.cs` |
| App queries | `src/Animora.Application/UseCases/Boarding/BoardingQueries.cs` |
| SQLite queries | `src/Animora.Persistence.Sqlite/Queries/BoardingQueries.cs` |
| Handoff writer/entity | `Repositories/BoardingBillingHandoffWriter.cs`, `Entities/BoardingBillingHandoff.cs` |
| Shared catalog picker VM | `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs` |
| Sales catalog query (boarding+sales picker source) | `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs` → `GetCatalogAsync` (~line 145) |
| Catalog mapping (Kind "Service"→"svc") | `src/Animora.Client.Services/Sales/SalesMapping.cs` (~line 202, 250) |
| Visit (Rx) catalog query | `src/Animora.Persistence.Sqlite/Queries/RxCatalogQueries.cs` |
| Invoice-from-handoff draft | `src/Animora.Application/UseCases/Sales/CreateInvoiceFromStayCommand.cs` |
| Invoice posting (attach+checkout stay) | `src/Animora.Application/UseCases/Sales/PostInvoiceCommand.cs` |

---

## TASK LIST

### ✅/⬜ Legend: set `[x]` when done + pushed. All start `[ ]`.

---

### [x] T0 — Branch bootstrap + baseline build
**Do:** ensure we are on `genspark_ai_developer`; confirm the Desktop project builds clean BEFORE any edit
(so later failures are attributable).
```
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Animora.Desktop/Animora.Desktop.csproj -v q --nologo   # expect 0 errors
```
If dotnet missing, provision SDK first (see §0.2). **Push:** this task writes no code; the FIRST real push
(in T1) uses `-b genspark_ai_developer`. If you want to establish the branch now, run
`scripts/git/push.sh -b genspark_ai_developer "chore(plan): add boarding/inventory fix plan"` to push THIS
plan file.
**Accept:** baseline build is green; branch is `genspark_ai_developer`.

---

### [x] T1 — Prepay/Postpay: make the radios work + wire real invoice behavior
**Problem (user):** the two options (پیش‌پرداخت / پس‌پرداخت) in the new-stay modal "don't select and don't
work". Prepay must create the invoice **immediately** at registration (in فروش + صندوق); postpay must create
it **at checkout** (existing behavior). Payment type must be recorded & shown consistently in history.

**Root cause A — visual selection dead (the "radios don't select" symptom):**
In `NewStayModalView.axaml` the payment style selectors target **`Border.pay-opt` / `Border.pay-opt.is-selected`**
(styles block ~line 60–70) but the actual control in the `ItemsControl.ItemTemplate` is a
**`Button Classes="pay-opt"`** (~line 280). So the `is-selected` style NEVER applies and the inner `Ellipse`
radio dot has a hard-coded white fill with no binding → zero visual feedback. The `ChoosePaymentCommand`
logic in the VM is actually correct (it toggles `IsSelected` on each `BoardingPaymentOptionViewModel`), and
`SubmitAsync` reads `PaymentOptions.FirstOrDefault(p => p.IsSelected)?.Mode` correctly. So the *data* works;
only the *visual* is broken, which makes the user think nothing is selectable.

**Fix A:** make the `is-selected` style apply to the `Button` and give visible feedback:
- Change the two style selectors to `Button.pay-opt` and `Button.pay-opt.is-selected` (keep the same
  setters: selected → `Brand600Brush` border + `Brand50Brush` background). Add a base `Button.pay-opt`
  setter block (transparent bg, `LineBrush` border 1.5, corner 10, padding 14,10,
  `HorizontalAlignment=Stretch`, `HorizontalContentAlignment=Left`) so unselected looks like a card.
- Bind the inner radio `Ellipse` fill/stroke to selection: add a bool→brush or use
  `Classes.is-selected` on the Ellipse too, OR simplest: bind `Ellipse.Fill` to a VM brush. Preferred
  low-risk approach: add two styles — `Button.pay-opt.is-selected /template/ ...` is awkward for the inner
  Ellipse, so instead give the Ellipse `x:Name="Dot"` and add styles
  `Button.pay-opt.is-selected Ellipse#Dot { Fill=Brand600Brush; Stroke=Brand600Brush }` and default
  `Button.pay-opt Ellipse#Dot { Fill=White; Stroke=LineBrush }`. Also bold the selected title via
  `Button.pay-opt.is-selected TextBlock { Foreground=Brand700Brush }` if desired.
- Verify `ChoosePaymentCommand` is reachable: the `CommandParameter="{Binding}"` passes the option VM; keep.
- Default selection: VM currently adds Postpaid first with `IsSelected=true`. Keep Postpaid as default.

**Root cause B — prepay does nothing at creation:**
`CreateBoardingStayCommandHandler` stores `PaymentMode` but takes no billing action for prepaid. There is no
handoff/invoice created at registration, so a prepaid stay never shows up in فروش/صندوق until checkout — i.e.
prepay behaves identically to postpay. This is the functional half of "doesn't work".

**Fix B (prepay invoice at registration) — design chosen to respect the one-handoff-per-stay constraint
(§0.3):**
- Inject `IBoardingBillingHandoffWriter` + `IClock` (already has clock) + `IClinicTimeZoneProvider` +
  `IBoardingQueries` (already has `queries`) into `CreateBoardingStayCommandHandler`.
- After `repository.Add(stay)`, if `c.PaymentMode == StayPaymentMode.Prepaid`:
  - Compute the **room-only** prepay charge for the *planned* stay:
    `nights = StayChargeCalculator.NightsBetween(from, until, tz)` and `roomTotal = DailyRate * nights`.
    (At registration there are no items yet, so the prepay invoice is the room charge for the planned
    nights. This matches the user requirement: "the invoice is created at that moment in sales & cashier".)
  - Build the SAME payload shape `CreateInvoiceFromStayCommand` expects for Boarding:
    `{ RoomRateMinor, TotalMinor = roomTotal.Minor, Currency, Items: [] }`.
  - `handoffs.Add(context.TenantId, stay.Id, stay.OwnerId, stay.PetId, now, payloadJson)`.
  - This makes a `Ready` handoff appear immediately in the صندوق queue, exactly like a postpay checkout
    handoff. The cashier posts it → invoice created in فروش + صندوق. ✔
- ⚠️ **One-handoff-per-stay:** because the prepay handoff is created at registration AND the postpay path
  also creates a handoff at checkout, a prepaid stay must NOT create a second full handoff at checkout.
  See T4 for the extras-at-checkout handling (which is the only case a prepaid stay needs a 2nd billing
  event). For the base prepay flow, checkout of a prepaid stay with NO extra items must NOT create another
  room handoff. Implement in `CheckoutStayCommandHandler`:
  - If `stay.PaymentMode == Prepaid`: the room is already (pre)invoiced. On checkout, only create a handoff
    for **unbilled extra items** (items added after the prepay invoice). If there are no such items, do NOT
    create a handoff — instead complete the stay directly. BUT completing directly needs an InvoiceId
    (domain `CompleteCheckout` requires it). The prepay invoice IS that invoice once posted.
  - **Problem:** at registration the prepay handoff is only `Ready`, not yet posted; the stay has no
    InvoiceId until the cashier posts it. So a prepaid stay cannot be `CheckedOut` until its prepay invoice
    is posted. Decision: **prepay checkout path** = when admin discharges a prepaid stay:
    - If the prepay invoice is still unposted (handoff Ready / not Invoiced): the stay cannot complete
      checkout (no InvoiceId). Surface a clear domain/result error and a Persian toast: "فاکتور پیش‌پرداخت
      این اقامت هنوز در صندوق تسویه نشده است؛ ابتدا آن را در صندوق ثبت کنید." (Do NOT silently succeed.)
    - If the prepay invoice IS posted (stay already `Invoiced` via PostInvoiceCommand's AttachInvoice):
      checkout should just `CompleteCheckout`. **However** `PostInvoiceCommand` currently calls BOTH
      `AttachInvoice` AND `CompleteCheckout` immediately on posting (lines 108–111), which would move a
      prepaid stay straight to `CheckedOut` at prepay-invoice time — wrong, because the pet is still
      boarding. **This is the crux of the prepay wiring and must be handled in T4** (see T4 for the
      `PostInvoiceCommand` change that makes CompleteCheckout conditional on payment mode / discharge).
  - For **T1 scope**, implement ONLY: (a) Fix A visual, (b) create the prepay room handoff at registration,
    (c) in `CheckoutStayCommandHandler`, branch on PaymentMode so a prepaid stay's checkout creates a handoff
    containing **only the extra items added since registration** (room excluded), and leave the
    Attach/Complete lifecycle refinement to T4. Keep the postpaid branch exactly as today (full room+items
    handoff). Add a short code comment pointing at T4 for the lifecycle nuance.

**Root cause C — payment type recording/visibility in history:**
- The stay card + detail modal already render the pay pill via `BoardingPresentation.PayLabel/PayTone`
  (verified in `StayDetailModalViewModel.Apply`). The invoice created from the prepay handoff is
  `InvoiceKind.Boarding` — the صندوق/history already shows boarding invoices. Ensure the prepay invoice and
  the (possible) extras invoice are distinguishable in history: the invoice LinesJson for prepay has only a
  room line (Items empty) and for extras has only item lines. That is already human-distinguishable. **No
  new history schema needed.** If a quick, cheap label helps, you MAY add the Persian word
  «پیش‌پرداخت»/«تسویه» into the draft invoice's generated title/number prefix in `SalesHandoffFactory`
  ONLY if it is low-risk; otherwise skip (not required for acceptance).

**Files:** `NewStayModalView.axaml` (styles + Ellipse), `BoardingCommands.cs`
(`CreateBoardingStayCommandHandler` ctor + prepay handoff; `CheckoutStayCommandHandler` prepaid branch).
Possibly `BoardingRegistration.cs` if DI of the handoff writer into the create handler needs registration
(the writer is already DI-registered for checkout — verify it resolves; no new registration expected).

**Accept:**
- Clicking either payment option visibly selects it (border + filled dot) and toggles the other off.
- Registering a **prepaid** stay immediately produces a `Ready` boarding handoff → it appears in the
  صندوق "آمادهٔ صدور" queue right after registration (confirm via code path; a build + reading the queue
  query is enough — no runtime test required).
- Registering a **postpaid** stay produces NO handoff at registration; the handoff appears only at checkout
  (unchanged).
- `Animora.Desktop` builds clean.
**Push:** `scripts/git/push.sh -b genspark_ai_developer "feat(boarding): working prepay/postpay selection + prepay invoice at registration"`

---

### [x] T2 — Discharged stays must leave the management page (only in history)
**Problem (user):** after a stay is تخلیه (discharged) it must no longer show on the boarding management
page; its info should be visible only in تاریخچه.

**Root cause (verified):** `BoardingQueries.GetStaysAsync` (SQLite) returns every stay (top 200 by
CheckInUtc, **no state filter**). `BoardingMapping` maps any non-`Active` state to `BoardingStatus.Done`, so
`ReadyForInvoice`/`Invoiced`/`CheckedOut` stays all appear on the page (under the "all" and "done" tabs).
The history tab already has its own query (`BoardingHistoryQuery` → `BoardingQueries.Project` history leg,
which includes checkouts) so discharged stays are already in history.

**Fix (choose server-side filter — cleanest):**
- In `BoardingQueries.GetStaysAsync`, filter to stays that are still on the floor:
  `.Where(s => s.State == StayState.Active || s.State == StayState.Reserved || s.State == StayState.ReadyForInvoice)`
  — i.e. exclude `Invoiced`, `CheckedOut`, `Cancelled`.
  - Rationale: a `ReadyForInvoice` stay (discharge clicked, cashier not yet posted) should arguably still be
    visible because the pet may still be physically present and the room still occupied. **Decision:** the
    user's words are "when discharged, don't show it". Discharge = clicking تخلیه = `RequestCheckout` →
    `ReadyForInvoice`. So to satisfy the literal requirement, **exclude `ReadyForInvoice` too**:
    `.Where(s => s.State == StayState.Active || s.State == StayState.Reserved)`.
  - ⚠️ But prepay (T1/T4) changes lifecycle: a prepaid stay may be `Invoiced` while the pet is STILL
    boarding (prepay invoice posted at registration). Excluding `Invoiced` would hide a pet that is still
    present. **Therefore the correct predicate is based on physical presence, not billing state:** show a
    stay while `ActualCheckOutUtc is null` AND `State != Cancelled`. Implement as:
    `.Where(s => s.ActualCheckOutUtc == null && s.State != StayState.Cancelled)`.
    - `ActualCheckOutUtc` is only stamped by `CompleteCheckout` (the true end of the stay). This is the
      single correct "has physically left" signal and is robust to both prepay and postpay. Use it.
- Because now the list only contains present stays, the management tabs ("done" tab) will show nothing for
  departed pets — that is correct. Consider removing/relabeling the "done" tab in `BoardingPageViewModel`
  `BuildTabs` **only if** it now always shows 0; low priority, do it if trivial, else leave (the tab simply
  shows 0 and all departed stays live in history). Keep scope tight.
- Verify history still shows departed stays: `BoardingHistoryQuery` path is independent of `GetStaysAsync`
  and already includes checkouts (`IncludeCheckOut`), so no change needed there.

**Files:** `src/Animora.Persistence.Sqlite/Queries/BoardingQueries.cs` (`GetStaysAsync`). Optionally
`BoardingPageViewModel.cs` (tab cleanup, only if trivial).

**Accept:** a stay whose `ActualCheckOutUtc` is set (fully checked out) no longer appears on the management
page but still appears in تاریخچه. Active + reserved + awaiting-invoice (present) stays still show. Build clean.
**Push:** `scripts/git/push.sh "fix(boarding): hide departed stays from management page, keep them in history"`

---

### [x] T3 — Verify price / elapsed-days / nights logic is correct & consistent
**Problem (user):** make sure the price, elapsed-days, nights calc is right.

**Verified facts (so you don't re-derive):**
- **Money nights (authoritative for invoicing):** `StayChargeCalculator.NightsBetween` =
  `Max(1, endDay.DayNumber - startDay.DayNumber)` in clinic tz. Used by `Accrued` (the checkout charge) and
  mirrored by the detail modal totals which compute `nights * DailyRate` from
  `BoardingStayClassification.Of` (also DayNumber diff). ✔ consistent.
- **UI nights/elapsed/remaining:** `BoardingStayClassification.Of(stay, today)` uses DayNumber diffs:
  `nights = Max(1, CheckOut - CheckIn)`, `elapsed = clamp(today - CheckIn, 0..nights)`,
  `remaining = active ? Max(0, CheckOut - today) : 0`, `overdueDays = active ? Max(0, today - CheckOut) : 0`.
- **The one divergent rule:** `BoardingStay.BillableDays(endUtc)` = `Max(1, ceil(hours/24))`. It is ONLY
  used to populate `StayReadyForInvoiceEvent.BillableDays` (an event payload) — NOT the money total.
  Confirm by grep: `BillableDays(` has callers only in `BoardingStay.RequestCheckout` (event) — money comes
  from `Accrued`. **So there is no money bug from the ceil-vs-calendar divergence.**

**Do (verification + only-if-needed fix):**
1. `grep -rn "BillableDays(" src --include=*.cs` → confirm the only caller is the event. If a money/display
   path uses it, align it to `NightsBetween`.
2. Confirm the detail modal's "اقامت (N شب × rate)" and total use the same `nights` as `Accrued` would at
   checkout. In `StayDetailModalViewModel.Apply`, `nights = cls.Nights` (DayNumber diff of planned dates).
   At checkout `Accrued` uses actual `end` (min(now/actual, asOf)). **Document the intended difference:**
   before checkout the modal shows the *planned* nights cost; at checkout the *actual* elapsed nights are
   billed. This is correct (planned vs actual). No change unless the user reports a specific wrong number.
3. Edge cases to eyeball (no code change unless wrong): same-day (checkIn==checkOut blocked by
   `checkOut <= checkIn` guard in both VM submit and domain Reserve → min 1 night enforced). Overnight
   crossing tz boundary — calendar-day diff is tz-correct because it converts to clinic tz first.
4. If T4 adds check-out date editing, re-verify nights recompute after a date change (the modal re-reads
   detail, so `cls.Nights` updates). 

**Files:** read-only verification; edit only if step 1/2 reveals a real money/display mismatch. If you edit,
it is likely `StayChargeCalculator.cs` or `BoardingStayClassification.cs`.

**Accept:** written confirmation (in the push message) that money uses calendar `NightsBetween` everywhere
and `BillableDays(ceil)` is event-only; any mismatch found is fixed; build clean.
**Push:** `scripts/git/push.sh "chore(boarding): verify/align nights & charge calculation (money uses calendar nights)"`
(If no code change was needed, still push a tiny clarifying code comment in `StayChargeCalculator.cs` so the
task produces a commit — never skip the push.)

---

### [x] T4 — Stay-detail modal: editable check-out date (+/- days) and prepay-extras lifecycle
**Problem (user):** clicking a current stay opens its detail; there must be the ability to change the
check-out date (increase/decrease the number of days). Also: for a PREPAID stay, if the admin adds
items/services after the (paid) stay, discharge must open an invoice for ONLY those extras, and history must
clearly record that the stay's own room cost was already paid.

**Current state (verified):** `StayDetailModalViewModel` has `CheckOutText`/`CheckInText` as display-only
strings (`BoardingPresentation.Long(...)`). There is NO command/endpoint to change the planned checkout
date. The domain `BoardingStay` has no "reschedule" mutator. So this is net-new.

**Fix 4a — add a reschedule-checkout capability (full vertical slice):**
1. **Domain:** add `BoardingStay.RescheduleCheckout(DateTimeOffset newPlannedCheckOutUtc)` returning
   `Result`. Rules: legal only while `State == Active` (and maybe `Reserved`); must be strictly after
   `CheckInUtc` (else `ErrorCodes.StayDatesInvalid`); set `PlannedCheckOutUtc`. (Do NOT touch
   `ActualCheckOutUtc`.) Keep it a pure state mutation; no event required (or raise a lightweight event if
   the existing event infra makes it trivial — optional).
2. **Application:** add `RescheduleStayCheckoutCommand(string StayId, DateOnly NewCheckOut)` +
   validator + handler in `BoardingCommands.cs` (`[RequiresPermission(PermissionCodes.BoardingManage)]`,
   `[Auditable(...)]`, follow the exact shape of the other boarding commands). Handler: find stay, convert
   `NewCheckOut` to UTC via `ClinicCalendar.ToUtcTicks(..., tz)` (same pattern as the create handler),
   call `stay.RescheduleCheckout(until)`. Register it in `BoardingRegistration.cs`.
3. **Contract:** add `Task RescheduleCheckoutAsync(string stayId, DateOnly newCheckOut)` to
   `IBoardingQueryService`; implement in `BoardingQueryService` (mirror `CheckoutAsync` — `SendAsync` the
   command, then nothing to return, or return the fresh detail).
4. **UI:** in `StayDetailModalView.axaml` entry strip, replace the read-only check-out `TextBlock` with an
   editable control when `IsEditable` (stay Active): either a `JalaliDatePicker` bound to a new
   `CheckOutEditText` TwoWay property (reuse the control used in `NewStayModalView.axaml` lines ~259–265),
   plus two small +/- buttons ("＋۱ روز" / "−۱ روز") that call commands to add/subtract one day. In
   `StayDetailModalViewModel`: add `CheckOutEditText`, `AddDayCommand`, `RemoveDayCommand`, and a
   `SaveCheckoutCommand` (or auto-save on change) that parses via `BoardingPresentation.ParseJalali`,
   guards `> CheckIn`, calls `_service.RescheduleCheckoutAsync`, then `Apply(await GetDetailAsync)` to
   refresh nights/total. Show a success/danger toast. When not editable (Done), keep read-only text.
   +/- buttons simply adjust the bound date and invoke the same save path.
5. Recompute: after save, the modal re-reads detail so `cls.Nights`, the day timeline, and totals update —
   verifying T3's recompute path.

**Fix 4b — prepaid extras at discharge (ties into T1 Fix B / §0.3 one-handoff constraint):**
This is the subtle lifecycle piece. Target behavior:
- **Prepay base:** room cost invoiced at registration (T1). The stay, once that invoice is posted, is
  "room-paid".
- **Prepay + later extras:** admin adds items (AddStayItem works while `Active`). At discharge, a NEW
  invoice must be raised for ONLY the extra items (room excluded, already paid). History must show both:
  the original prepay (room) invoice and the extras invoice, with the room clearly marked paid.

**Lifecycle problem to solve:** `PostInvoiceCommand` currently does `AttachInvoice` + `CompleteCheckout`
together whenever an invoice with a `BoardingStayId` is posted. For prepay this is wrong twice over: (1) the
prepay (room) invoice is posted at registration while the pet is still boarding — it must NOT complete
checkout; (2) a prepaid stay needs to accept a SECOND invoice (extras) later, but the domain state machine
only allows `ReadyForInvoice → Invoiced → CheckedOut` once.

**Chosen design (minimal, keeps one-handoff-per-stay for the posting lookup):**
- Introduce an explicit distinction between a *prepay (advance) invoice* and the *final checkout invoice*.
  Simplest robust approach that avoids reworking the handoff key:
  - **Prepay room invoice at registration:** do NOT route it through the stay lifecycle at all. i.e. the
    prepay invoice should attach to the stay for traceability but must NOT trigger
    `AttachInvoice/CompleteCheckout`. Implement by making `PostInvoiceCommand`'s stay-completion block
    conditional: only `AttachInvoice + CompleteCheckout` when the stay is in `ReadyForInvoice` (the postpay
    checkout path and the prepay *final* discharge path both pass through `RequestCheckout` → `ReadyForInvoice`
    BEFORE their handoff is posted). For a prepay room invoice posted at registration the stay is still
    `Active` (never `RequestCheckout`-ed), so `AttachInvoice` would fail the transition — guard it:
    `if (stay.State == StayState.ReadyForInvoice) { AttachInvoice; CompleteCheckout; }` and for an `Active`
    stay just record the invoice id on the stay via a new lightweight `RecordAdvanceInvoice(invoiceId)`
    (store first prepay invoice id, no state change) OR simply skip (traceability via invoice.BoardingStayId
    is already enough). **Preferred:** skip the stay mutation entirely for an `Active` stay — the
    `invoice.BoardingStayId` link already provides traceability; the handoff is marked `Invoiced` by
    `MarkHandoffInvoicedAsync` which is keyed by StayId and finds the single Ready handoff.
  - ⚠️ **One-handoff-per-stay collision:** the prepay registration handoff and a later extras handoff both
    have the same `StayId`. `MarkHandoffInvoicedAsync`/`GetHandoffStatusAsync`/`FindHandoffAsync` use the
    handoff **Id** (FindHandoffAsync by Id) for draft creation but **StayId** for status/mark in
    `SalesPostingData`. Two Ready handoffs with the same StayId would make the StayId-keyed
    `FirstOrDefaultAsync(h => h.StayId == sourceId)` ambiguous. **Mitigation:** ensure at most ONE `Ready`
    handoff per stay at any time:
    - Registration prepay handoff is created `Ready`; it becomes `Invoiced` when the cashier posts it.
    - The extras handoff is created at discharge ONLY — by then the prepay handoff is expected `Invoiced`
      (if the clinic settled the prepay). If the prepay handoff is still `Ready` at discharge, block
      discharge with the Persian toast from T1 ("فاکتور پیش‌پرداخت ... هنوز تسویه نشده"). This guarantees ≤1
      `Ready` handoff per stay, so the StayId-keyed lookups stay unambiguous. Enforce in
      `CheckoutStayCommandHandler` (prepaid branch): query existing handoffs for the stay; if any is still
      `Ready`, fail with a clear error; else create the extras-only `Ready` handoff and `RequestCheckout`.
    - For the **final discharge of a prepaid stay WITH extras**: `CheckoutStayCommandHandler` sets
      `RequestCheckout` (→ ReadyForInvoice), freezes total = extras-only charge, writes an extras-only
      handoff. When the cashier posts THAT invoice, `PostInvoiceCommand` sees `State == ReadyForInvoice` →
      `AttachInvoice + CompleteCheckout` → stay `CheckedOut`. ✔
    - For the **final discharge of a prepaid stay with NO extras**: there is nothing new to bill. Create NO
      handoff. But the stay must still reach `CheckedOut` so it leaves the floor (T2 uses
      `ActualCheckOutUtc`). Path: `CheckoutStayCommandHandler` prepaid-no-extras branch →
      `RequestCheckout` then, because no cashier step will happen, it must complete itself. But
      `CompleteCheckout` needs an InvoiceId. Use the prepay room invoice id as the attached invoice:
      `AttachInvoice(prepayInvoiceId)` + `CompleteCheckout(clock)` directly in the handler (requires the
      prepay invoice to be posted → its id is discoverable via the `Invoiced` boarding handoff's InvoiceId
      or `invoice.BoardingStayId`). If the prepay invoice is not yet posted, block discharge (same toast).
      Add a query to fetch the posted prepay invoice id for the stay (extend `IBoardingQueries` or reuse
      an existing invoice-by-stay read). This is the most involved sub-step; implement carefully and push
      it the moment it compiles.
- **History clarity:** since the room invoice (prepay) and extras invoice are separate `InvoiceKind.Boarding`
  invoices both linked by `BoardingStayId`, history already shows both. To make "room already paid" explicit,
  the extras-invoice LinesJson contains only item lines (no room line), and the prepay invoice contains only
  the room line — already distinguishable. Optionally add a care-note/stamp; not required for acceptance.

**Scope control:** 4b is the hardest part. Implement it incrementally and **push each compiling increment**
(e.g. push after the `PostInvoiceCommand` guard change; push after the extras-handoff branch; push after the
no-extras self-complete). Do NOT attempt it all in one giant uncommitted edit.

**Files:** `BoardingStay.cs` (RescheduleCheckout [+ maybe RecordAdvanceInvoice]), `BoardingCommands.cs`
(reschedule cmd + prepaid checkout branch), `BoardingRegistration.cs`, `IBoardingQueryService.cs`,
`BoardingQueryService.cs`, `StayDetailModalView(.axaml/.cs)`, `StayDetailModalViewModel.cs`,
`PostInvoiceCommand.cs` (conditional attach/complete), possibly `IBoardingQueries.cs`/`BoardingQueries.cs`
(prepay-invoice-id lookup), `CheckoutStayCommandHandler`.

**Accept:**
- From a current stay's detail modal you can pick a new check-out date and use +/-۱ روز buttons; the stay's
  nights and total update; invalid dates (≤ check-in) are rejected with a toast.
- Prepaid stay with extras: discharge creates an extras-ONLY cashier invoice; posting it completes checkout.
- Prepaid stay with no extras: discharge (after prepay invoice settled) completes checkout with no duplicate
  billing; if prepay unsettled, discharge is blocked with a clear Persian message.
- Postpaid unchanged (full room+items invoice at checkout).
- At most one `Ready` boarding handoff per stay at any time. Build clean.
**Push:** multiple pushes as above; final one e.g.
`scripts/git/push.sh "feat(boarding): editable checkout date + prepaid extras-only discharge invoicing"`

---

### [ ] T5 — Cross-screen consistency sweep for boarding
**Problem (user):** find any scenario inconsistencies / integration gaps with the rest of the app.

**Known consistency points to re-verify after T1–T4 (targeted, not a full audit):**
1. **Room occupancy:** rooms free up on `CompleteCheckout` (closes room history) and
   `CountActiveStaysInRoomAsync` counts `State == Active` only. After T4, a prepaid stay that is `Invoiced`
   at registration but still `Active` keeps occupying its room correctly (it IS Active). A `ReadyForInvoice`
   stay still counts as NOT active for capacity → its room reads free even though the pet is present. Verify
   this matches desired behavior; if a `ReadyForInvoice` stay should still hold the room, adjust
   `CountActiveStaysInRoomAsync` to include `ReadyForInvoice`. Decide and align.
2. **Dashboard / activity feed:** boarding events (`StayActivatedEvent`, `StayReadyForInvoiceEvent`,
   `StayCheckedOutEvent`) feed the activity feed (`ActivityFeedMapper`). Confirm the prepay registration
   path still raises `StayActivatedEvent` (it does — Activate is always called). No extra event needed for
   the prepay invoice (the invoice's own financial activity covers it).
3. **History inflow/outflow:** `BoardingQueryService.GetHistoryAsync` returns `InflowMinor/OutflowMinor`.
   Ensure prepay invoices don't double-count. (History counts check-in/check-out events, not invoices, so
   no double count — verify the history leg is event-based.)
4. **Toast truthfulness:** every success toast must reflect a real state change (no "تخلیه شد" when the
   stay was only queued). After T4, align `CheckoutModalViewModel`'s toast wording with the actual outcome
   (queued-for-invoice vs completed), especially for prepaid-no-extras (completed) vs postpaid (queued).

**Do:** targeted reads of the files named above; fix only real mismatches surfaced. Keep edits minimal.
**Accept:** documented (in push message) which consistency points were checked and any fix applied. Build clean.
**Push:** `scripts/git/push.sh "fix(boarding): cross-screen consistency (room occupancy / toasts / history)"`
(Push even if only comments/small fixes — never skip.)

---

### [ ] T6 — Catalog visibility: products/services missing in boarding & visit pickers
**Problem (user):** after adding a product, it doesn't appear when adding items in a boarding stay or during
a visit; sometimes even services don't appear. Find & fix all such cases.

**Verified facts (so you don't re-derive):**
- Two DIFFERENT catalog sources feed the pickers:
  - **Sales/Boarding picker** (`CatalogModalViewModel`) → `IBoardingQueryService.GetCatalogAsync` /
    `ISalesQueryService.GetCatalogAsync` → `SalesQueries.GetCatalogAsync`. Includes: one row per ACTIVE
    variant of each ACTIVE product, PLUS a product-level fallback row for each ACTIVE product with NO active
    variant, PLUS active `BoardingServiceDef`s. Mapping `SalesMapping.ToContract` sets
    `Kind = row.Kind=="Service" ? "svc" : "item"`.
  - **Visit (Rx) picker** → `IVisitEncounterService.GetCatalogAsync` → `RxCatalogQuery` →
    `RxCatalogQueries.GetAsync`. Includes: one row per ACTIVE variant of each ACTIVE product (services first),
    PLUS product-level fallback for active products with no active variant. **Does NOT include
    `BoardingServiceDef`s** (by design — boarding add-ons are boarding-only).
- `CatalogModalViewModel.Render()` filters by `_filterKey`: `"svc"/"item"` compare to `x.Kind` (already
  `"svc"/"item"` after mapping — CORRECT), other keys compare `x.GroupKey` (= `Slug(Category)`). Services
  are intentionally excluded from per-category chips (they live only under the «خدمات» chip). The catalog is
  loaded fresh every time the modal opens (no caching layer — verified: `Client.Services/Caching` has only
  `TenantScopedCache` and the catalog isn't cached).
- Product creation (`CreateProductCommand`): a stock product is created ACTIVE with each variant ACTIVE
  (`NewVariant` → IsActive true). **`Kind == Service` is REJECTED** by `CreateProductCommandHandler`
  (`ServiceNoStock`) — services must be made via the rate editor (`CreateServiceRateCommand`), which creates
  a `ProductKind.Service` product WITH one active variant (`SRV-...-1`). So services DO appear as variant
  rows in both catalogs.

**Likely real root causes to investigate (in priority order) — the queries look correct, so the bug is at
the edges:**
1. **Tenant scoping / freshness:** confirm `GetCatalogAsync` is not filtered by a stale tenant and that a
   just-created product shares `context.TenantId`. Check whether a modal opened BEFORE the product was
   created keeps a stale in-memory `_all` list — `CatalogModalViewModel` loads once per open; if the user
   opens the picker, switches to inventory in another area without closing, adds a product, then returns to
   the already-open picker, it won't refresh. Verify the real UX flow: in this desktop app the picker is a
   modal; adding a product requires closing it, so re-open reloads. If there's any path where the picker
   stays open, add a refresh. Low likelihood but cheap to confirm.
2. **Category = null → chip bucket:** in `GetCatalogAsync`, products with no category get `"سایر"`. In
   `SalesMapping`, items seed a per-category chip via `Slug(item.Category)`. If `Slug("سایر")` collides or
   produces an empty key, those items could be filtered out when a category chip is active. Verify `Slug`
   (in `SalesMapping`) never returns empty/duplicate for Persian category names; confirm the default "همه"
   and "دارو و کالا" chips always show all items regardless of category. **Most probable visible bug:** a
   newly created product with a brand-new category shows under "همه"/"item" but the user was on a specific
   category chip. Confirm "همه" shows everything.
3. **Services missing symptom:** the most probable cause is that a service created via the rate editor has
   its single variant, so it is a variant row with `Kind=Service`→`svc`; it shows ONLY under «خدمات» (svc)
   chip and «همه», never under item/category chips. If the user is on the item/category filter they won't
   see services — this is by design but may be the user's confusion. Verify the «خدمات» chip count is
   correct and the chip is reachable. If a service's variant is somehow inactive, the variantless fallback
   still surfaces it. Confirm `CreateServiceRateCommand` makes the variant active (it does — last ctor arg
   `true`).
4. **Deactivated-variant products:** a product whose ONLY variant was deactivated still surfaces via the
   variantless fallback at `DefaultSalePrice`. Confirm `DefaultSalePrice` is non-zero for such products
   (multi-variant goods store DefaultSalePrice=0 → they'd look free). Check the fallback only fires for
   products with NO active variant; a multi-variant product with ≥1 active variant never hits the fallback.
5. **RxCatalog (visit) parity:** `RxCatalogQueries` comment says a prior bug (only Service/Medicine/
   Consumable returned, PetShop missing) was fixed to include all active product kinds. Re-confirm the
   current query returns EVERY active product kind as "Item". If any kind is still filtered, fix it.

**Do:**
- Read `SalesMapping.Slug` and the chip-building block (`SalesMapping.ToContract`, ~lines 202–247) to
  confirm "همه" count == items.Length and no item is dropped. Fix any dropping (e.g. ensure items with a
  null/empty category still appear under "همه" and "دارو و کالا").
- Confirm `GetCatalogAsync` + `RxCatalogQueries` both return newly-created products (active product + active
  variant). If a real gap exists (e.g. a product kind excluded, a tenant filter, a zero-price fallback that
  hides an item), fix it at the query/mapping layer.
- If the only issue is UX confusion (services only under «خدمات» chip), make the «همه» tab the default when
  opening the picker for boarding/visit (it already defaults to the passed `initialFilter`; boarding opens
  with `"all"`, visit `AddService` opens `"svc"`, `AddItem`/goods opens its filter — verify these defaults
  are sensible so the user always sees their new row). Adjust defaults if they hide freshly added rows.
- Do NOT over-engineer. Fix concrete dropping/visibility bugs; leave by-design separations (boarding
  services not in visit picker) intact.

**Files:** `src/Animora.Client.Services/Sales/SalesMapping.cs` (Slug + chip build + ToContract),
`src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs` (`GetCatalogAsync`),
`src/Animora.Persistence.Sqlite/Queries/RxCatalogQueries.cs`, possibly
`src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs` (filter defaults / refresh).

**Accept:** a newly created product (any stock kind) and a newly created service appear in BOTH the boarding
stay item picker and the visit picker under «همه» and their correct kind chip; no active product/service is
dropped by category/price/kind filters; prices are correct (variant SalePrice, not 0). Build clean.
**Push:** `scripts/git/push.sh "fix(catalog): ensure all active products & services appear in boarding/visit pickers"`

---

### [ ] T7 — Wire-up & build verification pass
**Do:** after T1–T6, build the three touched projects and the Desktop head:
```
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Animora.Application/Animora.Application.csproj -v q --nologo
dotnet build src/Animora.Persistence.Sqlite/Animora.Persistence.Sqlite.csproj -v q --nologo
dotnet build src/Animora.Desktop/Animora.Desktop.csproj -v q --nologo
```
Fix any compile errors. **No full test suite run** (per budget rule). If a new EF migration is required for
any schema change (none expected — T1–T6 use existing tables/fields; `RescheduleCheckout` only mutates
existing columns), create it with `dotnet ef migrations add` under `Animora.Persistence.Sqlite` ONLY if the
model snapshot demands it; otherwise skip.
**Accept:** all three projects + Desktop build with 0 errors.
**Push:** `scripts/git/push.sh "chore(boarding,catalog): build-green verification after fixes"`

---

### [ ] T8 — Final self-review against the user's 5 boarding asks + catalog ask
Re-read the user's original requirements and tick each:
1. Prepay/postpay selectable + functional; prepay → invoice at registration; postpay → invoice at checkout;
   recorded & shown in history; prepay + later extras → extras invoice at discharge with room shown as paid. (T1, T4)
2. Discharged stays leave the management page, visible only in history. (T2)
3. Price / days / nights logic verified correct. (T3)
4. Clicking a current stay → editable check-out date (+/- days). (T4)
5. Any cross-screen inconsistency found & fixed. (T5)
6. Products/services now visible in boarding & visit pickers. (T6)
Fix any gap discovered; push the fix. If all pass, proceed to T9.
**Push:** only if a fix was made.

---

### [ ] T9 — FINAL TASK: delete this plan file only
**Do:** `rm Plan/boarding-prepay-postpay-inventory-fix.md` (delete ONLY this file; leave the other Plan/*.md
and all roadmap/docs untouched).
**Push:** `scripts/git/push.sh "chore(plan): remove completed boarding/catalog fix plan"`
**Accept:** this file is gone; working tree clean; branch pushed.

---

## Appendix A — exact snippets you'll need (verified)

**A1. Prepay handoff payload shape** (what `CreateInvoiceFromStayCommand` reads for Boarding — build the
same JSON in the prepay registration path, with empty Items):
```json
{ "RoomRateMinor": <DailyRate.Minor>, "TotalMinor": <room*nights>, "Currency": "<charge.Currency>", "Items": [] }
```
The postpay checkout payload (existing, in `CheckoutStayCommandHandler`) additionally fills `Items[]` with
`{ ItemId, Kind, CatalogItemId, Title, Quantity, UnitPriceMinor }`.

**A2. UTC conversion pattern (clinic tz)** — reuse from `CreateBoardingStayCommandHandler`:
```csharp
var tz = zones.GetClinicTimeZone();
var until = new DateTimeOffset(ClinicCalendar.ToUtcTicks(newCheckOutDateOnly, 0, tz), TimeSpan.Zero);
```

**A3. Posting guard to add in `PostInvoiceCommand` (T4)** — only complete the stay lifecycle when the stay
was actually sent to billing (ReadyForInvoice); never for a prepay invoice posted while the stay is Active:
```csharp
if (invoice.BoardingStayId is { } stayId)
{
    var stay = await boarding.FindAsync(stayId, ct);
    if (stay is null) return Result<Unit>.Fail(ErrorCodes.StayNotFound);
    if (stay.State == StayState.ReadyForInvoice)   // discharge invoice (postpay, or prepay-extras/final)
    {
        var attached = stay.AttachInvoice(invoice.Id); if (attached.IsFailure) return ...;
        var done = stay.CompleteCheckout(clock);      if (done.IsFailure) return ...;
    }
    // else: prepay advance invoice posted while Active — link only (invoice.BoardingStayId already set); no state change.
}
```

**A4. Management-page presence filter (T2)** — in `BoardingQueries.GetStaysAsync`:
```csharp
var stays = db.Set<BoardingStay>().AsNoTracking()
    .Where(s => s.ActualCheckOutUtc == null && s.State != StayState.Cancelled)
    .OrderByDescending(s => s.CheckInUtc).ThenBy(s => s.Id).Take(200);
```

**A5. ≤1 Ready handoff per stay invariant (T1/T4)** — before creating an extras handoff at discharge,
reject if a Ready handoff for the stay still exists (add a count/exists query on `BoardingBillingHandoff`
by `StayId && Status==Ready`). Keeps `SalesPostingData`'s StayId-keyed lookups unambiguous.

## Appendix B — DO-NOT list
- Do not run the full test suite or a full `dotnet build Animora.sln`. Build only touched projects + Desktop.
- Do not add mock/design-time data to `Pages/**`.
- Do not create a second simultaneously-`Ready` boarding handoff for the same stay.
- Do not change the handoff key from `StayId` (the posting path depends on it).
- Do not edit `Animora-UI/`, `backend-roadmap/phases/**`, `windows-frontend-roadmap/phases/**`, other
  `Plan/*.md`, or `docs/**`.
- Push after EVERY task. Not pushing = failure.
