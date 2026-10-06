# Plan — Cheques screen + Dashboard alert box + Catalog-picker visibility fix

> **Self-contained execution plan.** A fresh model must implement every task below **without
> re-auditing the repo**. All file paths, exact current behaviour, root-cause analysis and the
> chosen design are embedded. Read top-to-bottom once, then execute in order.
>
> **🔒 LOCKED PUSH RULE — after completing EACH task below, immediately run
> `scripts/git/push.sh "msg"`. Not pushing = project death. One commit per task (heavy
> mid-task code also gets its own push).** Work on branch `genspark_ai_developer`
> (first push: `scripts/git/push.sh -b genspark_ai_developer "msg"`).
>
> **Last task = delete THIS file only.** Never touch other Plan/*.md or roadmap files.

---

## 0. Context (read once — do not re-derive)

- **App:** Animora — vet-clinic management + accounting desktop app. .NET 10, Avalonia + MVVM
  (CommunityToolkit.Mvvm), Persian/RTL, Jalali calendar, local encrypted SQLite. Offline-first,
  no network server.
- **Data-flow rule (NEVER break):** `View → ViewModel → Animora.Contracts interface (ctor DI) →
  real Animora.Client.Services impl → Animora.Application dispatch → local SQLite`.
  ViewModels depend only on Contracts interfaces — never on EF/SQL/concrete service/DI scope.
  To add a screen's data need: add/extend interface+DTO in `Contracts/<Area>/`, implement it in
  `Animora.Client.Services/<Area>/` over the dispatcher, register both in
  `src/Animora.Desktop/App.axaml.cs`. RTL is set once app-wide in `App.axaml`.
- **Build/verify (this sandbox):** `scripts/sandbox/build.sh` (Desktop only, expect **0/0**);
  `scripts/sandbox/build.sh full` (whole solution); `scripts/sandbox/run.sh` (Xvfb smoke →
  prints `SMOKE RUN OK`). **No full-test runs; keep builds targeted and short.**
- **Perf standard:** `PERFORMANCE.md` numbered `§` rules. Relevant: §5 batch collection updates,
  §6 virtualise long lists, §7 cheap visual tree, §13 avoid redundant reloads.
- **Git:** branch `main` is current; do work on `genspark_ai_developer`. Latest migration = `030`
  → **next migration IDs: 031, 032…** (only if a task needs schema — none below should).

### Reusable building blocks (confirmed to exist — use, don't rebuild)
| Need | Use |
|---|---|
| Jalali date input | `src/Animora.Desktop/Controls/JalaliDatePicker.axaml(.cs)` |
| KPI card | `src/Animora.Desktop/Controls/` → StatCard (see Dashboard usage) |
| Data table | `src/Animora.Desktop/Controls/` DataTable |
| Toasts / dialogs / record sheets | `src/Animora.Desktop/Services/` (`IToastService`, `IDialogService`) + `Tone` enum |
| Theme tokens (colors/spacing/typography/icons) | `src/Animora.Desktop/Resources/Theme/*.axaml` (Icons.axaml holds `IconCheque`, `IconWallet`, `IconReceipt`, `IconBox`, `IconClipboard`, …) |
| Owner lookup (for inbound cheque counterparty) | dispatch `OwnerLookupQuery(string? Search)` → `OwnerLookupReadModel` (defined `src/Animora.Application/UseCases/Sales/SalesQueries.cs:134`) |
| Shared catalog picker | `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs` (used by Sales, Boarding, Visits) |

---

# TASK A — Fix catalog-picker item/service visibility (Boarding & Visits) ⭐ do FIRST

**User symptom:** after adding a کالا (good) in Inventory, it does not appear when adding items in
Boarding (پانسیون) or during a Visit (ویزیت); sometimes خدمات (services) are also missing.

## A.1 Root-cause analysis (already traced — do NOT re-audit the backend)

The **backend read path is already correct** (a prior fix — commits `90a3717` T6, `973cea2` T7 —
made the catalog "services-first" and included PetShop goods). Verified facts:

- **Visit picker catalog** = `RxCatalogQuery` → `src/Animora.Persistence.Sqlite/Queries/RxCatalogQueries.cs`.
  Returns **one row per active variant** of **every** active product (`p.IsActive && v.IsActive`),
  plus a product-level fallback for active products with no active variant. `Service → "Service"`,
  everything else (Medicine/Consumable/Food/**PetShop**/Equipment/Vaccine) → `"Item"`. Services
  ordered first, 1000+500 row window. **This is correct.**
- **Boarding & Sales picker catalog** = `SalesCatalogQuery` → `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs:145`
  (Boarding delegates via `BoardingCatalogQueryHandler`, `src/Animora.Application/UseCases/Boarding/BoardingQueries.cs:55`).
  Same shape: active variants + variantless active products + active `BoardingServiceDef`s.
  **This is correct.**
- **No dispatcher-level query caching exists** (`src/Animora.Application/Behaviors/` has no caching
  behavior), so a stale cache is NOT the cause.
- **New physical goods** are created by `InventoryQueryService.CreateProductAsync`
  (`src/Animora.Client.Services/Inventory/InventoryQueryService.cs:119`) as `ProductKind.PetShop`
  with **active variants** → they satisfy the catalog filters and SHOULD appear.

Therefore the remaining defect is **frontend**, in how the shared picker builds/filters its snapshot
and in its **initial filter chip**. The two consumer call-sites:

1. **Visits** — `src/Animora.Desktop/Pages/Visits/VisitEncounterViewModel.cs`
   - `RefreshCatalogAsync()` (~line 228) loads `_service.GetCatalogAsync()` into `_catalog`.
   - `BuildCatalogSnapshot()` (~line 288) maps each `RxCatalogItem` to `CatalogItem` with
     `Kind = c.Kind == RxCatalogKind.Service ? "svc" : "item"` and builds filter chips
     (`all`/`svc`/`item`) with counts (~line 301-310).
   - `AddService()` opens picker with `initialFilter: "svc"`; `AddItem()` with `initialFilter: "item"`
     (~line 253-278).
2. **Boarding** — `src/Animora.Desktop/Pages/Boarding/StayDetailModalViewModel.cs:394` `OpenCatalog()`
   opens the shared `CatalogModalViewModel` with `loadCatalog: _service.GetCatalogAsync`
   (the Sales `CatalogSnapshot`, already carrying `svc`/`item`/per-category chips).

3. **Shared picker filter** — `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs`
   - `_filterKey` seeded from `initialFilter` (default `"all"`), ctor ~line 83-94.
   - `ApplyFilter`/visible-list logic ~line 160-176:
     ```
     if (_filterKey is "svc" or "item")  list = list.Where(x => x.Kind == _filterKey);
     else if (_filterKey != "all")       list = list.Where(x => x.GroupKey == _filterKey);
     // then search: PersianText.Contains(Normalize(Title), norm)
     ```
   - `CatalogItem.Kind` is lowercase `"svc"`/`"item"` in BOTH mappings
     (`SalesMapping.ToContract` line 251: `row.Kind == "Service" ? "svc" : "item"`; Visit
     `BuildCatalogSnapshot` same). So kind-key casing is consistent — NOT the bug.

### What to actually verify & fix (focused, during implementation)
The visible remaining failure modes to check **in this order**, fixing the first that reproduces:

- **(a) Stale picker snapshot / no refresh after creating a product mid-session.** Confirm each
  picker calls its `loadCatalog`/`RefreshCatalogAsync` **every time it opens** (not once-cached in
  the VM). Visit's `OpenCatalogPicker` passes `loadCatalog: async () => { await RefreshCatalogAsync(); return BuildCatalogSnapshot(); }` — good. Check Boarding's `StayDetailModalViewModel.OpenCatalog` passes `_service.GetCatalogAsync` directly (re-queried on open) — good. If any path caches the snapshot in a field that outlives a create, make it re-query on open.
- **(b) Chip-count / empty-group hiding.** In `CatalogModalViewModel.BuildFilters`, a chip with
  count 0 or a default `_filterKey` that matches no rows would render an empty list. Confirm when
  `initialFilter` is `"item"` and there ARE items, they show; and that newly-added goods land under
  `item` (not an unslugged per-category key the default chip filters out). **Most likely true bug:**
  the default visible list when the picker opens uses a filter that excludes the item, OR the
  per-category chip `GroupKey`/`Slug` of a brand-new product's category doesn't match, so under a
  category chip it's hidden (but under `item`/`all` it shows). Ensure the `item`/`all` chips always
  include every non-service row regardless of category slug.
- **(c) `Product.IsActive` / variant active on create.** Re-confirm (read only) that
  `CreateProductCommandHandler` (`src/Animora.Application/UseCases/Inventory/InventoryCommands.cs:76`)
  creates the product `IsActive = true` (domain default) and variants active. It does. So (c) is a
  non-issue unless a create path sets inactive — don't spend time here unless (a)/(b) don't repro.

**Expected fix size:** small, frontend-only (`CatalogModalViewModel.cs` and/or the two consumer
VMs). If it turns out a consumer caches the snapshot, make it re-query on open. Do NOT touch the
persistence catalog queries (already correct).

## A.2 Service-creation gap (secondary, part of "sometimes can't see services")
- The Inventory «کالای جدید» modal (`NewProductModalViewModel.cs`) **always** sends
  `NewProductRequest(..., IsService: false, ...)` (line ~169) — i.e. the UI offers **no way to
  create a Service** from the product modal. Services are created elsewhere (Sales «نرخ خدمات» /
  `CreateServiceRateCommand`). This is by design — `CreateProductCommandHandler` hard-rejects
  `ProductKind.Service` (`InventoryCommands.cs:82`), and `InventoryQueryService.CreateProductAsync`
  routes `IsService==true` to `CreateServiceRateCommand` (line 123-133).
- **Decision:** do NOT change this architecture. If, after fixing A.1, the user still can't see a
  specific *service*, the cause is the same picker/refresh issue (a service added in Sales not
  showing in the Visit/Boarding picker until reopen). The A.1 fix covers it because both services
  and items flow through the same catalog query + picker.

## A.3 Acceptance
- Create a new good in Inventory → open a Visit → «افزودن کالا» → the new good is listed and
  addable. Same via «افزودن خدمت» for a service added in Sales. Same in Boarding stay-detail
  «افزودن از کاتالوگ».
- `scripts/sandbox/build.sh` → 0/0. **➡ PUSH (task A).**

---

# TASK B — Dashboard alert box (always-visible critical alerts)

**Goal:** a dedicated, always-in-view alerts box on the dashboard surfacing only **urgent**
items: cheque alerts (received/paid), recorded-expense alerts, and other urgent cross-section
items. Must be visible on first open, colour-coded by urgency, and each row must clearly show
which section it belongs to (cheque vs expense vs other).

## B.1 Current state (already traced)

The **alert data already exists end-to-end** and currently gets *merged into the manager
dashboard's secondary list* — there is no dedicated box yet.

- **Backend alert source:** `DashboardSnapshotQuery` →
  `src/Animora.Application/UseCases/Dashboard/DashboardSnapshotQuery.cs`.
  - `DashboardAlertRow(string Kind, string Title, string Subtitle, DateOnly? DueDay, long AmountMinor, string State, string Severity)` (line ~37).
  - `BuildAlertsAsync` (line ~110) gathers: **RecurringExpense** alerts (via
    `IRecurringExpenseQueries`, `access.Finance`) with State `Overdue`(Danger)/`Due`(Warn)/`DueSoon`,
    and **Cheque** alerts (via `IChequeQueries.GetAlertsAsync`, `access.Cheques`) — kinds
    `Bounced`/`Overdue`/`DueSoon`. Ordered by severity, capped at `MaxAlerts = 8`. Already
    permission-gated.
  - Cheque alert rules: `src/Animora.Domain/Finance/ChequeAlertRules.cs` — `DueWithin3Days`
    (within `DueWithinDays = 3`), `OverdueUncleared`, `BouncedUnresolved`.
- **Contracts:** `DashboardSnapshot` (`src/Animora.Contracts/Dashboard/DashboardModels.cs`) exposes
  `PrimaryList`/`SecondaryList` of `DashboardListItem(AvatarText,Title,Subtitle,TagText,TagTone,IconKey,Kind,Status,TitleKey)`
  and tone enum `DashboardTone{Neutral,Danger,Warn,Info,Ok,Brand,Lilac,Peach}`.
- **Mapping:** `src/Animora.Client.Services/Dashboard/DashboardQueryService.cs` →
  `DashboardMapping.Map` builds the manager `SecondaryList` via `MergeAlerts` (line ~) which calls
  `AlertItem(DashboardAlertRow)`:
  - Cheque alerts → `Kind="AlertCheque"`, `IconKey="IconCheque"`, titles
    «چک برگشتی نیازمند پیگیری»/«چک سررسیدگذشته تسویه نشده»/«چک نزدیک به سررسید», tone
    Danger (Bounced) else Warn.
  - Recurring-expense alerts → `Kind="AlertRecurring"`, `IconKey="IconWallet"`, tone by severity.
  So today alerts are **mixed with recent activity** in one "هشدارها و فعالیت‌های اخیر" list — not
  a prominent, standalone box.
- **Dashboard views:** `src/Animora.Desktop/Pages/Dashboard/` — `DashboardPageView.axaml`
  (host, 157 lines) switches between `ManagerDashboardView.axaml` (445 lines, the main one),
  `CashierDashboardView.axaml`, `DoctorDashboardView.axaml`. VM:
  `DashboardPageViewModel.cs` (445 lines) exposes the snapshot's lists/stats to the views.

## B.2 Design & implementation

**Approach — expose alerts as their own list, render as a dedicated top box.** Minimal, clean,
reuses the existing alert pipeline; do NOT invent new backend queries.

1. **Contracts** (`DashboardModels.cs`): add a dedicated collection to `DashboardSnapshot`, e.g.
   `IReadOnlyList<DashboardListItem> Alerts` (frozen like the others, default empty). Keep existing
   `PrimaryList`/`SecondaryList` intact for non-manager roles. This separates urgent alerts from the
   "recent activity" list.
2. **Mapping** (`DashboardQueryService.cs` / `DashboardMapping.Map`): populate the new `Alerts` list
   from `slice.Alerts` via the existing `AlertItem` for **all roles that have alert access**
   (currently only manager/receptionist branch builds them; cheque/expense alerts are gated by
   `access.Cheques`/`access.Finance` so they are safe to surface for any role that passes the gate).
   Keep `SecondaryList` as recent-activity only (stop merging alerts into it) **for the manager
   view**, OR leave `MergeAlerts` but also expose the standalone `Alerts` — choose: **expose
   standalone `Alerts` and remove alerts from `SecondaryList`** so they aren't duplicated. Ensure
   each row keeps a section discriminator: `Kind` is `"AlertCheque"` or `"AlertRecurring"` — the
   View uses this to render the section label/icon/colour.
3. **ViewModel** (`DashboardPageViewModel.cs`): add an `AlertRows` observable collection (and a
   `HasAlerts`/`AlertCount` computed) populated from `snapshot.Alerts`. Follow the existing batch
   pattern used for other lists (`RangeObservableCollection.ReplaceAll`, PERFORMANCE §5). If empty,
   the box renders an "بدون هشدار فوری" calm state (still visible, not collapsed, so layout is
   stable — but visually de-emphasised).
4. **View — the box** (`ManagerDashboardView.axaml`, and host where needed): add an **alerts box as
   the first/most-prominent element** so it's seen on first open without scrolling. Requirements:
   - Position at the top of the content region (above the KPI stat row or immediately beside it in a
     clearly dominant slot). **Re-flow the existing boxes** (stat cards, revenue pulse, doctor-load,
     species donut) so spacing/sizes stay consistent with the current grid; verify no overflow at
     common window sizes. Use existing spacing tokens from `Resources/Theme/Spacing.axaml` — do not
     hardcode new gaps.
   - **Colour coding by urgency:** Danger (red) for bounced cheques / overdue expenses; Warn
     (amber) for due-soon. Use the existing `Tone`→brush resources already used by tag pills and
     `StatCard` (map `DashboardTone.Danger/Warn` → theme Danger/Warn brushes). Do NOT introduce new
     raw hex; reuse theme tokens.
   - **Per-row section identity:** each alert row shows a leading icon + a short section tag so it's
     obvious whether it's a cheque or an expense (or other): cheque → `IconCheque` + «چک», recurring
     expense → `IconWallet` + «هزینه». Title + subtitle (due date in Jalali + amount in تومان) come
     straight from `AlertItem` (already formatted: `JalaliDay`, `N0` + «تومان»).
   - **Only urgent items.** Do not add non-urgent rows. The box is bounded (existing `MaxAlerts=8`);
     if more exist, show a «+N مورد دیگر» affordance that routes to the relevant screen (Cheques/
     Finance) — keep it simple, no new data query.
   - Each row/box click should navigate to the owning screen (cheque → the new Cheques screen from
     Task C; expense → Finance/expenses). Use `INavigationService` as other dashboard quick-actions
     do (see `DashboardQuickAction.cs`).
5. **Other roles:** Cashier/Doctor dashboards may also show the box if their access gates pass
   (cheque/expense alerts only appear when permitted). Keep it consistent; if a role has no alert
   access the box is simply the calm empty state.

## B.3 Constraints / gotchas
- `DashboardSnapshot` records freeze collections at construction (`DashboardCollections.Freeze`) —
  follow the same pattern when adding the `Alerts` property (init-only + freeze).
- Snapshot carrying alerts is cached only `AlertCacheSeconds = 5` (see `DashboardQueryService`), so
  paying/clearing an item clears its alert on the next read — don't change this.
- Keep `DashboardFailureKind` handling intact; the box must handle the empty + error states
  gracefully (no crash when `Alerts` is empty/null).

## B.4 Acceptance
- Open dashboard → alert box visible immediately (above the fold), correctly colour-coded, each row
  labelled by section, only urgent items. Empty state shows calm "no urgent alerts".
- A near-due / bounced cheque and an overdue recorded expense both appear with correct tone + Jalali
  due date + amount. Clicking routes to the right screen.
- Other boxes re-flowed cleanly (no overlap/overflow) at normal window size; smoke-run OK.
- `scripts/sandbox/build.sh` → 0/0; `scripts/sandbox/run.sh` → `SMOKE RUN OK`. **➡ PUSH (task B).**
- **Heavy-change note:** B touches Contracts + mapping + VM + XAML. If it becomes large, push in
  logical chunks (e.g. "contracts+mapping" then "view+vm") per the locked push rule.

---

# TASK C — Build the Cheques screen (چک‌ها)

**Goal:** a highly practical, minimal/modern, UX-first screen to manage **received (inbound) and
issued (outbound) cheques** and their full lifecycle; consistent with the rest of the app;
registers various scenarios with dates (Jalali). Near-due cheques already feed the dashboard alert
box (Task B) — reuse that, don't duplicate.

## C.1 Current state (already traced — important: frontend + Application layer are MISSING)

- **Domain — complete:** `src/Animora.Domain/Finance/Cheque.cs` is a full aggregate:
  - Enums `ChequeDirection{Inbound=received-asset, Outbound=issued-liability}` and
    `ChequeState{Registered,InPortfolio,Deposited,Cleared,Bounced,Returned,Cancelled,Settled}`.
  - **State machine** (`IsTransitionAllowed`): `Registered→InPortfolio|Cancelled`;
    `InPortfolio→Deposited|Returned|Cancelled`; `Deposited→Cleared|Bounced`;
    `Bounced→InPortfolio|Returned|Settled`; `Cleared/Returned/Cancelled/Settled` terminal.
  - Factory `Register(tenantId, chequeNumber, direction, Money amount, dueUtc, issuedUtc, ownerId?,
    supplierId?, bankName?, bankBranch?, accountNumber?, sayadId?, invoiceId?, paymentId?, note?)`.
    Invariants: positive amount; **inbound requires ownerId**, **outbound requires supplierId**
    (`CHEQUE_COUNTERPARTY_REQUIRED`).
  - Mutators (each returns `Result`, each needs `actorUserId` + `IClock`): `UpdateMetadata` (only
    while `Registered`), `DeleteDraft`, `MoveToPortfolio`, `Deposit`, `MarkCleared`,
    `MarkBounced(reason)`, `Return(reason)`, `Cancel(reason)`, `SettleInCash`, `LinkJournalEntry`.
    `MarkCleared`/`MarkBounced` raise ledger events (`PostingRules` turns them into journal
    entries). Append-only `History` of `ChequeEvent(EventId,FromState,ToState,OccurredUtc,ActorUserId,Reason)`.
- **Application ports — exist, but NO use-case commands/queries dispatched:**
  - `src/Animora.Application/Ports/IChequeRepository.cs` (`FindAsync/RequireAsync/Add/AddRange`).
  - `src/Animora.Application/Ports/IChequeQueries.cs` — read models already defined:
    `ChequeRow(Id,Direction,Number,Sayad,BankKey,Branch,Party,AmountMinor,IssuedUtc,DueUtc,State,InvoiceId,Account,Note)`,
    `ChequeCashflowWeek(WeekStart,IncomingMinor,OutgoingMinor)`, `ChequeBankRow(BankKey,AmountMinor,Count)`,
    `ChequeAlertRow(Id,Kind,DueDay,AmountMinor)`; methods `GetListAsync(page,size,ct)`,
    `GetCashflowAsync(weeks,today,ct)`, `GetBankBreakdownAsync(ct)`, `GetAlertsAsync(today,ct)`.
  - **There are NO `*Command`/`*Query` use-case types for cheques** in
    `src/Animora.Application/UseCases/` (confirmed via grep — only Sales/Finance reference cheques).
    So nothing is dispatchable yet for register/transition/list from the Contracts seam.
- **Persistence — complete:** `ChequeQueries.cs` (implements `IChequeQueries`),
  `ChequeRepository.cs`, `ChequeConfiguration.cs`. Cheque table exists since migration `002`.
  Registered in DI: `LocalStoreServiceCollectionExtensions.cs:98` (`IChequeQueries`) & `:132`
  (`IChequeRepository`). **No schema work needed.**
- **Contracts / Client.Services / Desktop Pages — DO NOT EXIST for Cheques.** There is no
  `src/Animora.Contracts/Cheques/`, no `src/Animora.Client.Services/Cheques/`, no
  `src/Animora.Desktop/Pages/Cheques/`. Cheques is **absent from the nav menu**
  (`src/Animora.Desktop/Shell/NavigationMenu.cs`) and from `NavPermissions.cs`.
- **Permissions — exist:** `PermissionCodes.ChequesRead = "cheques.read"`,
  `ChequesManage = "cheques.manage"` (`src/Animora.Domain/Identity/PermissionCodes.cs:129,132`;
  role grants in `DefaultRolePermissions.cs`).

**Implication:** the Cheques screen must be built across **four layers**: (1) Application use-cases
(commands + a dispatched query) over the existing ports; (2) Contracts interface + DTOs;
(3) Client.Services implementation; (4) Desktop page + nav registration.

## C.2 Implementation steps (in order)

### C.2.1 Application use-cases — `src/Animora.Application/UseCases/Finance/` (new `Cheques/` subfolder)
Model exactly on an existing CRUD+command area. **Reference pattern:**
`src/Animora.Application/UseCases/Finance/ExpenseCommands.cs` (command+validator+handler trio with
`[Auditable(...)]`, `CommandContext`, repo+`IClock` DI, returning `Result<T>`) and
`SalesQueries.cs`/`SalesRegistration.cs` for a dispatched query + the `AddCommand/AddQuery`
registration block. Create:

- **Query:** `ChequeListQuery(int Page, int Size)` → `IReadOnlyList<ChequeRow>`/`PagedResult<ChequeRow>`
  handler delegating to `IChequeQueries.GetListAsync`. Also optionally `ChequeCashflowQuery`,
  `ChequeBankBreakdownQuery` if the UI shows those widgets (keep scope practical — the user asked for
  a *management* screen, so at minimum the list; cashflow/bank are nice-to-have, add only if it stays
  minimal). A `ChequeDetailQuery(string id)` returning the cheque row + its `History` for a detail
  sheet. (Add a `ChequeHistoryRow` read model to `IChequeQueries` if detail/history is shown — mirror
  the existing read-model style; implement in `ChequeQueries.cs`.)
- **Commands** (one handler each, loading via `IChequeRepository.RequireAsync`, calling the matching
  domain mutator with `context.UserId` + injected `IClock`, returning `Result`):
  `RegisterChequeCommand(direction, number, amountMinor, dueUtc, issuedUtc, ownerId?, supplierId?,
  bankName?, bankBranch?, accountNumber?, sayadId?, note?)` → `Cheque.Register(...)` + `repo.Add`;
  `UpdateChequeMetadataCommand`; `DeleteChequeDraftCommand`; `MoveChequeToPortfolioCommand`;
  `DepositChequeCommand`; `MarkChequeClearedCommand`; `MarkChequeBouncedCommand(reason)`;
  `ReturnChequeCommand(reason)`; `CancelChequeCommand(reason)`; `SettleChequeInCashCommand`.
  Each gated by `ChequesManage` (register/transition) / `ChequesRead` (queries) via the standard
  authorization behavior (how other commands declare required permission — check `ExpenseCommands`
  / `AuthorizationBehavior` and follow the same attribute/convention). Add `CommandTypes.*` entries
  if that pattern is required (see how `ExpenseCommands` names `CommandTypeName`).
- **Registration:** add an `AddCommand<...>()`/`AddQuery<...>()` block (new
  `ChequeRegistration.cs` or fold into Finance registration — match the folder's existing
  registration file convention) and ensure it's called from the Application composition root
  (grep for where `FinanceRegistration`/`SalesRegistration` are invoked and add alongside).
- **Money:** amounts are `Money.Irt(minor)` (IRT/تومان), same as elsewhere.

### C.2.2 Contracts — new `src/Animora.Contracts/Cheques/`
Mirror an existing Contracts area (e.g. `Inventory/` or `Boarding/`). Create:
- `ChequeModels.cs`: UI DTOs — `ChequeListItem`, `ChequeDetail` (+ `ChequeHistoryEntry`),
  a `NewChequeRequest` / transition request records, and UI enums mirroring domain
  (`ChequeDirectionDto`, `ChequeStateDto`) so Contracts has no Domain dependency (follow the
  `DashboardTone`-style "mirror enum" convention). Include display-friendly fields but no
  technical ids leaked as titles.
- `IChequesService.cs`: `Task<...> GetListAsync(...)`, `GetDetailAsync(id)`, `RegisterAsync(NewChequeRequest)`,
  and one method per transition (`DepositAsync`, `MarkClearedAsync`, `MarkBouncedAsync(id,reason)`,
  …), plus owner/supplier lookup for the register form (`GetOwnersAsync(search)` — reuse the owner
  lookup; for suppliers check whether a supplier lookup query exists, else expose a minimal one).
  Add a `ChequesServiceException` with a `UserMessage`/`Classify` like `DashboardQueryException`
  for safe Persian error surfacing.

### C.2.3 Client.Services — new `src/Animora.Client.Services/Cheques/`
Mirror `src/Animora.Client.Services/Inventory/InventoryQueryService.cs` (scope-per-call over the
dispatcher: build `QueryContext`/`CommandContext` via `ICurrentUserProvider`, `QueryAsync`/`SendAsync`,
map `Result` failures to the service exception). Create `ChequesService.cs` (+ `ChequesMapping.cs`
read-model→DTO mapping; Jalali formatting via the same helpers Dashboard/Sales use). Register in
`src/Animora.Desktop/App.axaml.cs` next to the other `AddSingleton<IXxxService, XxxService>()` +
`AddSingleton<ChequesPageViewModel>()` lines (pattern at lines ~279-341).

### C.2.4 Desktop page — new `src/Animora.Desktop/Pages/Cheques/`
Per-screen pattern = `ChequesPageView.axaml` (+ `.axaml.cs`) + `ChequesPageViewModel.cs`, plus
modal `*View/*ViewModel` pairs for register & transition dialogs. **Model the layout, controls and
theming on an existing list+modal screen** — `src/Animora.Desktop/Pages/Inventory/` (list + filter
chips + «کالای جدید» modal) is the closest analogue; reuse its DataTable, filter-chip, toast/dialog
and empty-state patterns. Requirements from the user:
- **List** of cheques with clear columns: direction (دریافتی/پرداختی), number, counterparty
  (owner/supplier name), bank, amount (تومان), **issue & due dates in Jalali**, state (coloured
  pill using `Tone`). Filter by direction and by state; search by number/counterparty.
- **Register modal** («ثبت چک جدید»): direction toggle; number; amount; **Jalali date pickers**
  (`Controls/JalaliDatePicker`) for issue & due dates; counterparty picker (owner for inbound /
  supplier for outbound — enforce the counterparty invariant in the form, surfacing
  `CHEQUE_COUNTERPARTY_REQUIRED` as a friendly message); optional bank/branch/account/Sayad/note.
- **Lifecycle actions** on a selected cheque, driven by `IsTransitionAllowed` so only valid
  transitions are offered (e.g. Registered → «بایگانی در پرتفوی»/«ابطال»; InPortfolio →
  «واگذاری به بانک»/«عودت»/«ابطال»; Deposited → «وصول شد»/«برگشت خورد»; Bounced →
  «بازگشت به پرتفوی»/«عودت»/«تسویه نقدی»). Transitions needing a reason (bounce/return/cancel)
  prompt for it (required). Show the append-only **history timeline** in a detail sheet
  (who/when/from→to/reason, dates in Jalali).
- **UX/design:** minimal, modern, RTL, consistent spacing/typography/colour with the rest of the
  app (reuse theme tokens + existing controls; no bespoke styling). Think about box layout and
  resize behaviour across window sizes (same discipline as Task B). Keep it genuinely practical —
  no over-engineered extras. Near-due cheques already surface in the dashboard alert box (Task B);
  the Cheques screen is the management/detail destination those alerts link to.
- **Perf:** server-side paged list (`PagedResult`), `AsNoTracking` already in persistence,
  virtualised list (PERFORMANCE §6), batch collection updates (§5), debounced search like
  Patients/Inventory.

### C.2.5 Navigation & permissions
- Add a nav entry in `src/Animora.Desktop/Shell/NavigationMenu.cs` `Items` — e.g.
  `new("cheques", "چک‌ها", "IconCheque", typeof(ChequesPageViewModel), "کسب و کار")` (group it with
  «انبار و کالا»/«آمار» under «کسب و کار», or under «مرکز فرمان» near «فروش و صندوق» — pick the most
  natural; justify in commit). Add `using Animora.Desktop.Pages.Cheques;`.
- Add `["cheques"] = PermissionCodes.ChequesRead` to `src/Animora.Desktop/Shell/NavPermissions.cs`.
- Verify the icon key exists (`IconCheque` is referenced by the dashboard already, so it's in
  `Resources/Theme/Icons.axaml`).

## C.3 Acceptance
- «چک‌ها» appears in the sidebar (for users with `cheques.read`), opens the list.
- Can register an inbound cheque (owner required) and an outbound cheque (supplier required);
  invalid/missing counterparty shows a friendly Persian error, not a crash.
- Lifecycle transitions follow the state machine (only valid actions offered); bounce/return/cancel
  require a reason; history timeline renders with Jalali dates.
- A near-due/bounced cheque shows in the dashboard alert box (Task B) and clicking it opens this
  screen.
- `scripts/sandbox/build.sh full` → 0/0 (new Application/Contracts/Client.Services projects compile);
  `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
- **This task is large — push in logical chunks** per the locked rule: (1) Application use-cases,
  (2) Contracts, (3) Client.Services + DI, (4) Desktop page + nav. Each chunk builds before push.
  **➡ PUSH after each chunk (task C).**

---

# TASK D — Final cleanup

- Delete **only this file** (`Plan/cheques-dashboard-catalog.md`). Do not touch other `Plan/*.md`,
  `AGENTS.md`, roadmaps, or any doc.
- `scripts/git/push.sh "chore(plan): remove completed cheques/dashboard/catalog plan"`. **➡ PUSH.**

---

## Execution order & push checklist (LOCKED)
1. **Task A** (catalog visibility fix) → build 0/0 → **push**.
2. **Task B** (dashboard alert box) → build 0/0 + smoke → **push** (chunk if heavy).
3. **Task C** (Cheques screen) → **push after each of the 4 layer chunks** → final full build + smoke.
4. **Task D** (delete this file) → **push**.

> Never skip a push after a task/chunk. If `push.sh` exits code 3 → call
> `setup_github_environment` tool once, then re-run. Exit code 2 → resolve rebase conflict
> (prefer remote), re-run.

## Status
- [x] A — catalog picker visibility fix
- [x] B — dashboard alert box (cheque-alert click is re-pointed to the Cheques screen in C.4)
- [x] C.1 — Application cheque use-cases
- [x] C.2 — Contracts Cheques
- [ ] C.3 — Client.Services Cheques + DI
- [ ] C.4 — Desktop Cheques page + nav/permissions
- [ ] D — delete this plan file
