# Plan — Statistics («آمار») screen + Expense ledger + Dashboard alerts

> **Self-contained execution plan.** A fresh model must be able to implement every task below
> **without re-auditing the repo**. All file paths, the exact state of the codebase, the
> architecture decisions and the chosen design are embedded here. Read this file top-to-bottom
> once, then execute task by task. Do **not** read the deleted commits (see §0.4).

---

## 0. Context (read once — do not re-derive)

### 0.1 App
**Animora** — self-hosted veterinary clinic management + accounting **desktop** app.
.NET 10, **Avalonia + MVVM (CommunityToolkit.Mvvm)**, Persian / RTL UI, Jalali calendar,
local **encrypted SQLite**, offline-first, no network server. Nine `src/` projects.

### 0.2 The one architectural rule (never break)
Data flows: **View → ViewModel → `Animora.Contracts` interface (constructor DI) → real
`Animora.Client.Services` impl → `Animora.Application` dispatch (command/query) → SQLite.**
- ViewModels depend **only** on Contracts interfaces, never on a concrete service, EF/SQL type,
  or DI scope.
- No sample/seed/`if(design-time)` data in `Pages/**`; seed lives in Persistence seeders.
- To add a data need: add/extend interface + DTO in `Contracts/<Area>/`, implement it in the
  matching `Animora.Client.Services/<Area>/` service **over the dispatcher** (scope-per-call),
  register both in `App.axaml.cs`. RTL is set once in `App.axaml`.

### 0.3 Build / verify (fast only — NO full test run, NO smoke run unless a task says so)
- Desktop compile: `scripts/sandbox/build.sh` → must be **0 warnings / 0 errors**.
- Whole solution (use after Domain/Application/Persistence edits):
  `scripts/sandbox/build.sh full` → **0/0**.
- Do **not** run the long test suite or Xvfb smoke run unless a task explicitly requires it.

### 0.4 LOCKED — push after every task (critical, non-negotiable)
After **each** completed task: run from repo root
```
scripts/git/push.sh "type(scope): [Task N] message"
```
It stages all → commits → fetch+rebase → push, prints one `✓`/`✗`. Exit code `3` = no creds →
call the **`setup_github_environment`** tool once, then re-run the same command. **No push = project
death.** Work on branch **`genspark_ai_developer`** (first push: `scripts/git/push.sh -b genspark_ai_developer "..."`).
If a single task produces a large/heavy change mid-way, push it with a clear message before continuing.

**Do NOT look at the deleted feature commits.** Commit `ce69f17` removed the *previous* Finance/
Expenses/Cheque/Sales-report UI. We rebuild from scratch, our own design. Never `git show` those
files or copy their code. (This plan already extracted everything reusable from them.)

### 0.5 Project path cheat-sheet (confirmed to exist)
```
src/Animora.Domain/Finance/            ← Expense, ExpenseCategories, FinanceRanges, Cheque,
                                          ChequeAlertRules, ChartOfAccounts(AccountCodes),
                                          PostingRules, JournalEntry/Line, LedgerAccount, AccountingPeriod
src/Animora.Application/UseCases/<Area>/  ← command/query handlers + <Area>Registration.cs
src/Animora.Application/Ports/          ← repository/query port interfaces
src/Animora.Application/Finance/LedgerPostingHandler.cs  ← posts journal entries from events
src/Animora.Persistence.Sqlite/Queries/ ← read-only EF projections (the read side)
src/Animora.Persistence.Sqlite/Repositories/ ← write repositories
src/Animora.Persistence.Sqlite/Entities/ ← local read-model facts (DailyFinanceFact, ProductSalesFact...)
src/Animora.Persistence.Sqlite/Finance/  ← FinanceProjectionStore, number allocators
src/Animora.Persistence.Sqlite/Configurations/Finance/  ← EF configs
src/Animora.Persistence.Sqlite/Migrations/  ← EF migrations
src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs  ← Persistence DI
src/Animora.Contracts/<Area>/           ← DTOs + I<Area>QueryService interface (Desktop↔services seam)
src/Animora.Client.Services/<Area>/     ← real service impl over the dispatcher
src/Animora.Desktop/Pages/<Area>/       ← screens (<X>View.axaml + .axaml.cs + <X>ViewModel.cs)
src/Animora.Desktop/App.axaml.cs        ← DI composition root (services + ViewModels)
src/Animora.Desktop/App.axaml           ← app-wide resource dictionaries (merge style files here)
src/Animora.Desktop/Shell/NavigationMenu.cs  ← sidebar items (static)
src/Animora.Desktop/Shell/NavPermissions.cs  ← nav key → permission + page→key maps
src/Animora.Desktop/Shell/ViewFactory.cs     ← ViewModel→View compile-time registry (add one line per screen)
src/Animora.Desktop/Resources/Theme/*.axaml  ← design tokens (Colors/Typography/Spacing/Icons)
src/Animora.Desktop/Resources/Controls/*.axaml ← per-control styles
src/Animora.Desktop/Controls/           ← reusable controls (StatCard, DonutRing, DataTable, JalaliDatePicker...)
```

---

## 1. STATE OF THE WORLD — what already exists vs. what we build

**Key insight (saves a full re-audit):** commit `ce69f17` removed only the **presentation +
application-use-case + read-query + contract + client-service** layers of the old finance feature.
It **kept the entire domain + posting pipeline + persistence write/projection infra**. So the
accounting backbone for expenses and revenue is already live; we are rebuilding the read/query +
contracts + services + UI on top of it, plus **new** recurring-expense + dashboard-alert work.

### 1.1 ✅ ALREADY EXISTS — reuse as-is, do NOT recreate
| Concern | Where | Notes |
|---|---|---|
| `Expense` aggregate (Draft→Posted→Reversed, `Post(IClock)` raises `ExpensePostedEvent`) | `Domain/Finance/Expense.cs` | Create/Edit/SetSettlement/AttachReceipt/Post/MarkReversed/LinkJournalEntry. `Amount` is `Money`, `IncurredUtc` is the accounting date, `CategoryCode` resolves debit account. Correction = reversing expense, never edit-after-post. |
| Expense categories + account mapping | `Domain/Finance/ExpenseCategories.cs` | 9 codes: `RENT, UTILITIES, SALARY, SUPPLIES, MAINTENANCE, MARKETING, TAX, TRANSPORT, OTHER`. `SALARY`→acct 6100, rest→6000. Persian display names are **not** defined here — add a presentation map in Desktop (Task 7). |
| Finance date windows (Persian labels) | `Domain/Finance/FinanceRanges.cs` | `FinanceRanges.All` = today/7d/30d/90d/ytd/custom with Persian labels; `FinanceRanges.Resolve(id, todayLocalDay, from?, to?)` → `FinanceDayRange(FromDay,ToDay)` as `yyyyMMdd` ints. Reuse for the stats range picker. |
| Posting: `ExpensePostedEvent`→journal entry | `Application/Finance/LedgerPostingHandler.cs` + `Domain/Finance/PostingRules.ForExpensePosted` | Already registered as a domain-event handler (confirmed in Persistence DI). Posting an expense writes the journal entry automatically. |
| Expense write repo + number allocator | `Persistence/Repositories/ExpenseRepository.cs`, `Persistence/Finance/ExpenseNumberAllocator.cs` | Ports `IExpenseRepository`, `IExpenseNumberAllocator` exist and are **DI-registered**. |
| Ledger/period repos + journal allocator | `Persistence/Repositories/{LedgerRepository,PeriodRepository}.cs`, `Persistence/Finance/JournalNumberAllocator.cs` | Registered. `IPeriodRepository.FindContainingAsync/FindNextOpenAsync` used by posting. |
| **Revenue/expense/COGS daily facts** | `Persistence/Entities/DailyFinanceFact.cs` + `Persistence/Finance/FinanceProjectionStore.cs` | **This is the stats backbone.** Per clinic-local day (`LocalDay` = `yyyyMMdd` int), already accumulates: `RevenueMinor, CogsMinor, ExpenseMinor, DiscountMinor, InsuranceMinor, RefundMinor, InvoiceCount, PaidInvoiceCount`. Fed from InvoicePosted/Voided/Refunded, StockMovement(Sale→cogs), **ExpensePosted→ExpenseMinor**. Unique index `(TenantId, LocalDay)`. |
| **Per-product daily sales facts** | `Persistence/Entities/ProductSalesFact.cs` | Per `(TenantId, LocalDay, ProductId, VariantId)`: `QuantitySold, RevenueMinor, CogsMinor`. Backs "best-selling products". Unique index exists. |
| Owner spend snapshot | `Persistence/Entities/OwnerFinancialSnapshot.cs` | Per owner total spend + invoice count + last-invoice date. Backs "top clients". |
| Cheque read queries | `Application/Ports/IChequeQueries.cs` + `Persistence/Queries/ChequeQueries.cs` | **Still present + DI-registered.** Exposes `GetListAsync`, `GetCashflowAsync`, `GetBankBreakdownAsync`, `GetAlertsAsync(today)`. `ChequeAlertRow(Id, Kind, DueDay, AmountMinor)` where Kind reflects due-soon/overdue/bounced. Use `GetAlertsAsync` for dashboard cheque alerts. |
| Cheque alert date rules | `Domain/Finance/ChequeAlertRules.cs` | `DueWithin3Days`, `OverdueUncleared`, `BouncedUnresolved`. Pure. |
| Dashboard snapshot shape | `Contracts/Dashboard/DashboardModels.cs` | `DashboardSnapshot` already has a **secondary "alerts" list** for the manager role (`SecondaryListTitle` + `SecondaryList` of `DashboardListItem`). This is our hook for rent/cheque alerts. `DashboardListItem(AvatarText,Title,Subtitle,TagText,TagTone,IconKey?,Kind,Status,TitleKey?)`; tones: Neutral/Danger/Warn/Info/Ok/Brand/Lilac/Peach. |
| Dashboard query/service/VM | `Application/UseCases/Dashboard/DashboardSnapshotQuery.cs`, `Persistence/Queries/DashboardQueries.cs`, `Client.Services/Dashboard/DashboardQueryService.cs`, `Pages/Dashboard/*` | `DashboardSlice`/`DashboardAccess` already carry `Finance`, `Cheques` permission flags and an `Expense`/`Cheque` activity kind. Manager dashboard already renders `SecondaryList`. We **extend** the alert-building here, not rebuild it. |
| Permission codes | `Domain/Identity/PermissionCodes.cs` | `FinanceRead="finance.read"`, `FinancePost="finance.post"`, `FinancePeriodClose`, `ReportsRead="reports.read"`, `ChequesRead="cheques.read"`, `ChequesManage`. Already in the catalog. Manager role has all; default Receptionist lacks finance/reports/cheques. |
| Account codes | `Domain/Finance/ChartOfAccounts.cs` | `AccountsPayable="2000"`, `OperatingExpense="6000"`, `SalaryExpense="6100"`, cash/bank 1000/1010. |
| Reusable UI controls | `Desktop/Controls/` | `StatCard` (icon/value/label/delta), `DonutRing`, `DataTable`, `JalaliDatePicker`, `Tone` enum. Check exact ctor/props by opening the one you use. |
| Latest migration | `Persistence/Migrations/` | newest id = **`028`** (`__028_PersianBoardingServiceTitles`). **Next ids: `029`, `030`…** |

### 1.2 ❌ MISSING — we build these
- **Expense READ queries**: there is no `GetExpenses/GetExpenseStats` query anymore (old
  `FinanceQueries.cs`/`FinanceReportingQueries.cs` were deleted). Build new read projections over
  `Expense` + `DailyFinanceFact` + `ProductSalesFact`.
- **Expense COMMANDS (use-cases)**: `AddExpense`/`PostExpense`/`ReverseExpense`/`PayFixedExpense`
  were deleted. Rebuild as journaled commands.
- **Contracts + Client.Services** for a new **`Statistics`** area (DTOs, interface, impl).
- **Desktop `Statistics` screen** (the «آمار» page): dashboard overview + sub-sections
  (clients, revenue/sales, expenses).
- **Recurring/fixed expense** domain + persistence + reminder (NEW — nothing exists for this).
- **Dashboard alert extension**: inject rent-due + cheque-due alerts into the manager
  `SecondaryList`.

### 1.3 Naming decision (locked)
The sidebar label is **«آمار»** (Statistics). Internally use area name **`Statistics`** for the new
Contracts/Client.Services/Pages folders and **`Finance`** namespace for Application use-cases
(matches the `Domain/Finance` + removed `UseCases/Finance` convention; recreate `UseCases/Finance/`).
Nav key: **`statistics`**. Permission to reveal the nav entry: **`PermissionCodes.ReportsRead`**
(`reports.read`) — it is the "view analytics" capability and Manager-only by default.

---

## 2. DESIGN — the «آمار» screen (what to build, decided)

A single page `StatisticsPageView` with an **internal section switcher** (segmented tabs along the
top, RTL), **not** separate sidebar entries. Minimal, modern, data-dense but calm. Sections:

1. **نمای کلی (Overview / default)** — the management dashboard:
   - Top row of **KPI StatCards**: درآمد امروز · درآمد این ماه · سود ناخالص (revenue−cogs−expense)
     · تعداد مراجعین (today/period) · میانگین فاکتور. Each with a delta vs. previous comparable period.
   - **Revenue trend chart** (area/line) over the selected range (reuse `DailyFinanceFact` series;
     mirror the existing `DashboardRevenuePulse` normalization math in
     `Pages/Dashboard/RevenueChartMath.cs`).
   - **Revenue-by-source breakdown** (donut): ویزیت‌ها vs فروش کالا vs پانسیون vs سایر. Derive source
     split from invoice line kinds / fact columns.
   - **Range picker** (today/7d/30d/90d/ytd/custom) using `FinanceRanges` — drives every section.

2. **مراجعین (Clients/Patients)** — customer distribution:
   - Donut: توزیع گونه (reuse the dashboard species donut query), new vs returning clients,
     top clients by spend (from `OwnerFinancialSnapshot`), visits-per-day trend.

3. **درآمد و فروش (Revenue & Sales)**:
   - Sales per day (bar), **best-selling products** table (from `ProductSalesFact`: qty + revenue),
     revenue by source comparison, paid-vs-unpaid invoice ratio.

4. **هزینه‌ها (Expenses)** — the expense ledger + stats (see §3).

Charts: prefer existing in-repo chart primitives (`DonutRing`, the dashboard pulse/area renderer).
If a new chart shape is needed, build a lightweight Avalonia-drawn control under `Controls/`
following the existing `DonutRing`/`RevenueChartMath` pattern — **no third-party chart lib**
(check `STACK.md` before adding any package; default = none).

All money formatted via the existing Persian money/number formatters in
`Desktop/Localization/` (find the helper used by Sales/Dashboard; reuse it). Dates via Jalali
helpers. Empty/loading/error states required (match Dashboard/Patients conventions).

---

## 3. DESIGN — Expense ledger + fixed/recurring expenses (decided)

### 3.1 One-off expense entry (uses existing domain)
UI to create → post an `Expense`: category (9 Persian-labeled categories), title, amount, incurred
date (Jalali), settlement method (cash/card/credit→payable/cheque), optional supplier, optional
note, optional receipt file. Flow: command creates draft `Expense` → `Post(clock)` → posting handler
writes journal entry → `FinanceProjectionStore` rolls `ExpenseMinor` into `DailyFinanceFact`. The
expenses section then shows:
- expense list (filter by category + date range), grouped/categorized totals,
- per-category breakdown donut, expenses trend over range, comparison vs previous period,
- correction = create a reversing expense (`ReversesExpenseId` + `MarkReversed`), never edit-after-post.

### 3.2 Fixed / recurring expense (NEW domain work)
Need: define recurring costs (e.g. اجاره ثابت, حقوق) that **re-appear every month**, are **reminded**
when due, and have a **«پرداخت» button** that posts that month's real `Expense`.

**New domain aggregate** `Domain/Finance/RecurringExpense.cs`:
- Fields: `Id, TenantId, CategoryCode, Title, Money Amount, int DayOfMonth (1–31, Jalali day),
  bool IsActive, string? SupplierId, string? Note, DateTimeOffset CreatedUtc`.
- Methods: `Create(...)`, `Edit(...)`, `Activate/Deactivate()`. Pure, `Result<T>` return style
  (mirror `Expense.cs`). No events needed (it is a template, not a ledger row).
- **Payment tracking**: track which months were already paid so a month is not reminded twice and
  not double-paid. Simplest robust design: a sibling value/entity
  `RecurringExpensePayment(RecurringExpenseId, PeriodKey=yyyyMM-Jalali, ExpenseId, PaidUtc)`.
  When «پرداخت» is pressed for a due month, create+post a normal `Expense` (so it flows into the
  ledger + facts) and record a `RecurringExpensePayment` linking the template to that posted expense.
- **Due logic** (pure, in domain): a recurring item is "due this month" if the current Jalali month
  has reached `DayOfMonth` AND no `RecurringExpensePayment` exists for the current `PeriodKey`.
  Expose a pure helper `RecurringExpenseSchedule.IsDue(dayOfMonth, todayJalali, alreadyPaidThisPeriod)`
  and `NextDueJalaliDate(...)`.

**Persistence (NEW)**: EF configs + a **migration `029`** adding `RecurringExpenses` and
`RecurringExpensePayments` tables (follow the index-light migration template of
`20261002120000_023_PatientsPerfIndexes.cs`; remember the triple: `*.cs` + `*.Designer.cs` + update
`AnimoraLocalDbContextModelSnapshot.cs`; tenant/soft-delete filters auto-applied by
`Conventions/ModelConventions.cs`). Repos + ports mirroring `IExpenseRepository`.

**Application**: `AddRecurringExpenseCommand`, `EditRecurringExpenseCommand`,
`ToggleRecurringExpenseCommand`, and `PayRecurringExpenseForPeriodCommand` (creates+posts the real
`Expense`, records the payment). A read query `RecurringExpensesQuery` returning each template +
its current-month due/paid state. All journaled via the standard command pipeline + validators.

**UI**: in the Expenses section, a "هزینه‌های ثابت" panel listing templates with status chips
(«پرداخت‌شده» / «سررسید نزدیک» / «پرداخت») and a «پرداخت» button per due item. Add/edit modal.

---

## 4. DESIGN — Dashboard management alerts (decided, high priority)

Extend the **manager** dashboard `SecondaryList` ("هشدارها") so it surfaces actionable management
alerts, ordered by urgency. Sources:
- **Fixed-expense due** (e.g. "اجاره نزدیک است"): from `RecurringExpensesQuery` due-but-unpaid items
  → `DashboardListItem` with `Kind="Expense"`, tone `Warn` (due soon) / `Danger` (overdue),
  `TagText` = «سررسید پرداخت». Only when the viewer has `finance.read`.
- **Cheque due/overdue/bounced**: from `IChequeQueries.GetAlertsAsync(today)` →
  `DashboardListItem` `Kind="Cheque"`, tone by `ChequeAlertRow.Kind`. Only when viewer has
  `cheques.read` (both the port and the `access.Cheques` flag already exist).

Design this as an **extensible alert aggregator** so future alert kinds (e.g. check runs, low stock
reminders) plug in without reshaping the dashboard. Implement the aggregation inside the existing
`DashboardSnapshotQuery`/`DashboardQueries` read (respect the existing `DashboardAccess` permission
flags and the projection-lag freshness), map to `DashboardListItem` in `DashboardQueryService`.
Give the alert rows a strong, scannable visual treatment (icon + tone pill + due date) in the
manager view — this section is **high priority** and must look polished.

**Important:** route admission/permissions stay as they are; do not add any JS/app access control.
Finance/cheque data must be gated by the existing permission flags so a Receptionist never sees them.

---

## 5. TASK LIST (execute in order; push after each — §0.4)

> Status legend: `[ ]` todo · `[~]` in progress · `[x]` done. Keep this list updated as you go.
> Build gate after each code task: `scripts/sandbox/build.sh` (Desktop) or `full` after
> Domain/Application/Persistence edits — must be **0/0** before pushing.

### Phase A — Backend data/read layer (no UI yet)

- [ ] **Task 1 — Expense read queries + ports.**
  Add `IStatisticsQueries` (or extend an existing finance port) in `Application/Ports/` exposing:
  (a) `GetOverviewAsync(FinanceDayRange, ct)` → revenue/cogs/expense/net/invoiceCount/avgInvoice
  for the range + previous comparable range (deltas), sourced from `DailyFinanceFact`;
  (b) `GetRevenueSeriesAsync(FinanceDayRange, bucket, ct)` → per-bucket revenue (day/week/month per
  `FinanceRangeDefinition.Bucket`); (c) `GetRevenueBySourceAsync(range)` (visit/product/boarding/other);
  (d) `GetExpensesAsync(range, category?, page, size)` → expense rows; (e) `GetExpenseStatsAsync(range)`
  → per-category totals + trend + previous-period comparison; (f) `GetTopProductsAsync(range, take)`
  from `ProductSalesFact`; (g) `GetTopClientsAsync(take)` from `OwnerFinancialSnapshot`;
  (h) `GetSpeciesDistributionAsync()` (reuse dashboard species logic). Implement in a new
  `Persistence/Queries/StatisticsQueries.cs` (`AsNoTracking`, clinic-calendar day buckets like
  `ChequeQueries`/`DashboardQueries` do — **never** SQLite date functions; use `ClinicCalendar`).
  Register in `Context/LocalStoreServiceCollectionExtensions.cs`.
  **Build `full` → 0/0. Push:** `scripts/git/push.sh -b genspark_ai_developer "feat(finance): [Task 1] statistics read queries + port"`.

- [ ] **Task 2 — Expense commands (use-cases).**
  Recreate `Application/UseCases/Finance/` with `FinanceRegistration.AddFinanceUseCases(builder)`
  chaining the read queries (Task 1 wrapped as dispatcher queries) + commands: `AddExpenseCommand`
  (+validator) that allocates number → `Expense.Create` → `Post(clock)` → `IExpenseRepository.Add`;
  `ReverseExpenseCommand` (create reversing expense + `MarkReversed`). Mirror `SalesRegistration`
  style exactly (`AddQuery<..>()`/`AddCommand<..>()`/`AddValidator<..>()`). Chain
  `.AddFinanceUseCases()` into `App.axaml.cs`'s `AddAnimoraApplication(builder => …)`.
  **Build `full` → 0/0. Push:** `[Task 2] expense commands + finance use-case registration`.

- [ ] **Task 3 — Recurring-expense domain + persistence + migration 029.**
  Add `Domain/Finance/RecurringExpense.cs`, `RecurringExpensePayment` entity/value, and pure
  `RecurringExpenseSchedule` (§3.2). Add EF configs under `Configurations/Finance/`, ports
  `IRecurringExpenseRepository`/`IRecurringExpenseQueries`, repo + query impls, DI registration,
  and **migration `029_RecurringExpenses`** (`.cs` + `.Designer.cs` + snapshot update). Add
  commands `AddRecurringExpenseCommand`, `EditRecurringExpenseCommand`, `ToggleRecurringExpenseCommand`,
  `PayRecurringExpenseForPeriodCommand` (posts a real `Expense` + records payment) and a
  `RecurringExpensesQuery` (templates + current-period due/paid state) into the Finance registration.
  **Build `full` → 0/0.** This is a heavy task — **push it even if mid-way** with a clear message.
  **Push:** `[Task 3] recurring/fixed expenses domain, persistence (migration 029), use-cases`.

### Phase B — Contracts + Client.Services seam

- [ ] **Task 4 — Statistics contracts + client service.**
  Create `Contracts/Statistics/` with DTOs (overview, revenue series point, source slice, expense
  row, expense-category stat, top-product, top-client, species slice, recurring-expense row) and
  `IStatisticsQueryService` (+ a `StatisticsQueryException` mirroring `DashboardQueryException`'s
  failure-kind/UserMessage pattern). Implement `Client.Services/Statistics/StatisticsQueryService.cs`
  over the dispatcher (scope-per-call, map Application read models → Contracts DTOs; follow
  `Client.Services/Dashboard/DashboardQueryService.cs` as the template). Also expose expense +
  recurring **write** methods (AddExpense / PayRecurring / Add/Edit/Toggle recurring) on the
  interface, dispatching the Task 2/3 commands.
  **Build `full` → 0/0. Push:** `[Task 4] statistics contracts + client service seam`.

### Phase C — Desktop UI

- [ ] **Task 5 — Statistics page shell + navigation wiring.**
  Add `Pages/Statistics/StatisticsPageView.axaml(.cs)` + `StatisticsPageViewModel.cs`
  (derive `PageViewModelBase`; ctor-inject `IStatisticsQueryService`; load on `OnNavigatedTo`;
  loading/empty/error states). Wire: `NavigationMenu.cs` new item
  `new("statistics","آمار","IconChart"|existing icon, typeof(StatisticsPageViewModel),"کسب و کار")`
  (pick an existing icon key from `Resources/Theme/Icons.axaml`; add one if none fits);
  `NavPermissions.cs` → `["statistics"]=PermissionCodes.ReportsRead` in `ByKey` and
  `[typeof(StatisticsPageViewModel)]="statistics"` in `ByPage`; `ViewFactory.cs` one
  `Register<StatisticsPageViewModel,StatisticsPageView>(factories)` line; `App.axaml.cs`
  `services.AddSingleton<IStatisticsQueryService, StatisticsQueryService>();` +
  `services.AddSingleton<StatisticsPageViewModel>();`.
  **Build Desktop → 0/0. Smoke-run** (`scripts/sandbox/run.sh` → "SMOKE RUN OK") because this
  touches DI/nav/shell. **Push:** `[Task 5] statistics page shell + nav/DI wiring`.

- [ ] **Task 6 — Overview section (KPIs + revenue trend + source donut + range picker).**
  Implement the overview section (§2.1) with StatCards, the revenue area/line chart (reuse
  `RevenueChartMath`), the revenue-by-source `DonutRing`, and the `FinanceRanges` range picker that
  re-queries on change. Persian money/number/Jalali formatting throughout.
  **Build Desktop → 0/0. Push:** `[Task 6] statistics overview section`.

- [ ] **Task 7 — Clients + Revenue/Sales sections.**
  Implement §2.2 and §2.3 (species donut, new/returning, top clients, sales-per-day bar,
  best-selling products table, source comparison, paid/unpaid ratio). Add the Persian
  expense-category + source display-name map as a small presentation helper under `Pages/Statistics/`.
  **Build Desktop → 0/0. Push:** `[Task 7] statistics clients + revenue/sales sections`.

- [ ] **Task 8 — Expenses section + add-expense modal + fixed-expense panel.**
  Implement §3: expense list (category + range filters), per-category donut + totals, trend +
  previous-period comparison, add-expense modal (category/title/amount/Jalali date/settlement/
  supplier/note/receipt) posting via the service, correction via reversing expense. Build the
  "هزینه‌های ثابت" panel: recurring templates with status chips + «پرداخت» button (calls
  `PayRecurringExpenseForPeriod`), add/edit recurring modal. Great UX, empty/loading/error states.
  **Build Desktop → 0/0. Push:** `[Task 8] expenses section + add-expense + fixed-expense panel`.

### Phase D — Dashboard alerts

- [ ] **Task 9 — Manager dashboard alert aggregator.**
  Extend `DashboardSnapshotQuery`/`DashboardQueries` to aggregate fixed-expense-due (from recurring
  query, when `access.Finance`) + cheque due/overdue/bounced (from `IChequeQueries.GetAlertsAsync`,
  when `access.Cheques`) into the manager `SecondaryList` as `DashboardListItem` rows (§4), ordered
  by urgency, each with a clear tone pill + due date. Map in `DashboardQueryService`. Polish the
  manager alert card visual in `Pages/Dashboard/ManagerDashboardView.axaml`. Keep permission gating
  via the existing `DashboardAccess` flags — never show finance/cheque alerts to roles without the
  permission. Make the aggregator easy to extend with future alert kinds.
  **Build `full` → 0/0. Smoke-run** (dashboard is startup surface). **Push:**
  `[Task 9] dashboard management alerts (rent-due + cheque) aggregator`.

### Phase E — Finish

- [ ] **Task 10 — Integration pass + final verify.**
  Quick targeted check that: nav entry shows for Manager and is hidden for Receptionist; stats data
  reacts to the range picker; posting an expense updates the expenses section + overview + dashboard;
  paying a fixed expense clears its due alert. Fix any integration gaps (if a fix is large, push it
  separately with a clear message). Final `scripts/sandbox/build.sh full` → **0/0** and one smoke
  run. **Push:** `[Task 10] statistics/expenses/alerts integration pass`.

- [ ] **Task پایانی (final) — Delete this plan file only.**
  `git rm Plan/statistics-expenses-alerts.md` (touch no other file, no roadmap edits).
  **Push:** `scripts/git/push.sh "chore(plan): [Task پایانی] remove completed statistics plan"`.

---

## 6. Guardrails (apply to every task)
- Read files **only** when a task needs them; the tables above already capture the structure — no
  blind grepping / no re-auditing deleted commits.
- Match existing conventions: `Result<T>` in domain/app, `AsNoTracking` + `ClinicCalendar` in
  queries, scope-per-call in client services, `PageViewModelBase` + `CommunityToolkit.Mvvm`
  `[ObservableProperty]`/`[RelayCommand]` in VMs, tone/StatCard/DonutRing reuse in XAML.
- Performance: cite `PERFORMANCE.md §` rules where relevant (batch collection updates, virtualize
  long lists, cheap visual tree, avoid redundant reloads). Expense/stats lists must be paged +
  virtualized like Patients.
- No new NuGet packages unless `STACK.md` already allows it; no chart third-party libs.
- Keep everything RTL + Jalali + Persian-digit correct. Gate finance/cheque data by the existing
  permission flags; never implement access control in app/UI JS.
- **After every task: build 0/0 → `scripts/git/push.sh "..."`. No push = failure.**
