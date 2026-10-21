# Plan — Operator Console (کنسول اپراتور انیمورا) Revamp

> **AUDIENCE:** a fresh AI model with no prior context. Everything needed to implement is embedded below — do **NOT** re-audit the whole repo. Only open the exact files named here, and only as each task needs.
>
> **SCOPE / TARGET:** This is the **vendor/owner** operator console — the browser panel the system owner (who sells accounts to clinics) uses to control clinics, monitor, and administer. It is the Blazor SSR project **`src/Animora.Web.Admin/`** served by `src/Animora.Server/`. It is NOT the clinic admin and NOT the desktop app. All routes are under `/admin`, policy `AdminOnly`, RTL Persian + English.
>
> **HARD RULE (LOCKED):** After completing **each** task below, run `scripts/git/push.sh "message"` and confirm it prints `✓`. If a push is skipped = project failure. If push exits code `3`, call the `setup_github_environment` tool once, then re-run. If a single task produces a large/heavy change mid-way, push it immediately with a clear message.
>
> **BUILD/TEST RULE (LOCKED):** NO full-solution build, NO long test runs. This is a huge .NET solution. Validate by reading/reasoning and, at most, a single targeted `dotnet build src/Animora.Web.Admin/Animora.Web.Admin.csproj` **only if truly needed** (it may be slow — prefer static review). Do not run the whole test suite.

---

## 0. Ground truth — architecture facts (already discovered, do not re-verify)

### Project layout for the operator console
- **UI (Blazor SSR, Razor):** `src/Animora.Web.Admin/`
  - Pages: `Pages/*.razor` (Dashboard, Tenants, TenantNew, TenantDetail, TenantStaff, TenantNotify, Plans, …).
  - Nav: `Layout/NavMenu.razor`. Layout shell: `Layout/MainLayout.razor`.
  - Components: `Components/` (`Pager.razor`, `StatusBadge.razor`, `FormError.razor`, `ConfirmCheckbox.razor`, `LiveStatusPanel.razor`, `LiveLogPanel.razor`).
  - DTOs/forms: `Models/AdminModels.cs`. Port interfaces: `Ports/AdminPorts.cs`.
  - Localization: `Localization/AdminStrings.cs` (dotted keys, `(En, Fa)` tuples). Accessed via `Loc.T("key")`. Missing key renders the key string itself (visible gap, never a crash).
  - CSS: `wwwroot/admin.css` (dependency-free, RTL via CSS logical properties, has `.cards`, `.table-wrap`, `.quick`, `.badge`, `.pager`, etc.).
  - Base component: `Models/AdminPageComponent.cs` gives `Loc`, `Error`, `Cancellation`, `Capture(...)`.
- **Adapters (bind UI ports → server services):** `src/Animora.Server/Admin/Admin*Adapter.cs` and `AdminTenantQueries.cs`. DI wiring: `src/Animora.Server/Admin/AdminServiceCollectionExtensions.cs`.
- **Data/queries (Postgres):** `src/Animora.Persistence.PostgreSql/Operator/`
  - `OperatorReadQueries.cs` — `TenantsAsync`, `TenantAsync`, `PlansAsync`, `StaffAsync`, `ProductsAsync`, `DashboardAsync`, `LicensesAsync`.
  - `OperatorNotificationAdministration.cs` — `SendAsync(tenantId, draft, actor, correlation, ct)`.
  - `OperatorPlanAdministration.cs` — `CreateAsync`, `DeactivateAsync`.
- **Domain:** `src/Animora.Domain/Subscriptions/` (`Plan.cs`, `Subscription.cs`, `BillingPeriod.cs`). `Plan` fields: `Code, Name, DefaultSeatLimit, MonthlyAmountMinor, Currency="IRT", TrialDays (0..365), IsActive, CreatedUtc`. `Plan.Create(code,name,seats,amountMinor,trialDays,now,currency="IRT")`. **Plan has NO duration field — it is monthly-amount based.** Do not add DB columns / migrations in this revamp (too heavy/risky). Duration customization is done via the subscription **extend** op; seat customization via `SeatLimitOverride`.

### Terminology map (Persian request ↔ code)
- "کلینیک" = **tenant** (`Tenant`, routes `/admin/tenants`). "یوزر کلینیک" = staff membership / user (`OperatorStaffRow`).
- "پلن اشتراک" = `Plan`. "اشتراک" = `Subscription`.
- "پیام / اعلان" = `TenantNotification` (operator → clinic users, read-only on desktop, no reply).

### Key record/form shapes (from `Models/AdminModels.cs`)
- `TenantSummary(Id, Code, Name, Status)` — **currently has NO subscription/expiry/seats info.**
- `TenantListQuery(Status?, Search?, Page=1, PageSize=50)`.
- `AdminPage<T>(Items, Page, PageSize, Total, Stamp?)`; `AdminPage.Bounds` clamps (pageSize max 200).
- `TenantDetail(Core, Subscription?, SeatsUsed, Events, DevicesCount, LicensesCount, GrantSequence)`.
- `SubscriptionSummary(Id, Status, PlanId, Seats, PeriodEnd, ConcurrencyStamp)`.
- `PlanSummary(Id, Code, Name, Seats, MonthlyAmountMinor, TrialDays, Active, Stamp)`.
- `CreateTenantForm{ Code, Name, OwnerUserName, OwnerName, PlanCode="standard", Seats?, StartTrial=true, IdempotencyKey }`.
- `CreatePlanForm{ Code, Name, Seats=1, AmountMinor, TrialDays }`.
- `SendNotificationForm{ AudienceKind(0=all,1=role,2=user), RoleValue?, UserValue?, Kind(0..5), Tag?, Title?, Body?, ScheduledForUtc?, ExpiresAtUtc?, Confirmed }`.
- `OperatorStaffRow(MembershipId, UserId, Name, UserName, Role, Active, Devices)` and `StaffListQuery(Role?, Search?, Page=1, PageSize=50)` via `IAdminStaffQueries.ListAsync(tenantId, query, ct)`.

### Notification facts (TenantNotify)
- `TenantNotify.razor` route `/admin/tenants/{Id}/notify`. Audience kind 2 ("one user") uses `Form.UserValue` as a **free-text `<InputText maxlength=26>`** → THIS IS THE BUG: operator must type a user id they don't have. Must become a **dropdown** of that clinic's users (reuse `IAdminStaffQueries.ListAsync`). Server (`OperatorNotificationAdministration.SendAsync`, kind 2) validates the user id is an active membership of the tenant — so the dropdown must post the real `UserId`.
- `OperatorNotificationAdministration.SendAsync` targets **one tenant**. There is NO broadcast (send to all / to a category) capability yet → must be added for task 5.
- Kinds (style): 0 Announcement, 1 Success, 2 Warning, 3 Promotion, 4 System, 5 Reward. Roles: Manager, Doctor, Receptionist, Cashier, Accountant.
- `AdminNotificationAdapter` is the bridge; `IAdminNotificationCommands.SendAsync(tenantId, form, ct)` in `Ports/AdminPorts.cs`.

### The "one clinic per page" bug (task 2) — ROOT-CAUSE ANALYSIS
- `Tenants.razor` passes `PageSize=50`; `OperatorReadQueries.TenantsAsync` clamps/pages correctly with `Take(size)`. So **it is not a server paging limit.** The visible "one per page" symptom is almost certainly that the **real dataset is small during testing** OR the perception comes from the table UX. REGARDLESS, the concrete deliverable is: **guarantee ≥15 rows visible at once** and make the list dense + sortable + filterable. Concretely: default `PageSize` stays ≥50, add explicit page-size handling so it can never silently become 1, and verify `Pager` math. The important real work is enriching the list (expiry/seats) and sorting by remaining days. If, while implementing, you find any place that passes `pageSize:1` or mis-binds `[SupplyParameterFromQuery] PageSize`, fix it and note it.

---

## TASK LIST (do in order; push after EACH)

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done.

---

### [x] T1 — Dashboard page (`Pages/Dashboard.razor`)
**Goal:** clean, minimal, modern, performant summary for the owner. Show server/service status (concise) + user/clinic counts; remove clutter.

**What exists now:** `Dashboard.razor` renders: version/uptime line, a Readiness probes table, a Workers table, a `.cards` row (ExpiringSoon, PastDue, dead-lettered, commands, outbox, PG pool), a `LiveStatusPanel`, and an Insight line. Data from `IAdminStatusReader.GetStatusAsync` → `AdminStatus(Readiness, Workers, Counters, Pg, Version, UptimeSeconds, ExpiringSoon, PastDue)` and `IAdminInsightStatus`.

**Do:**
1. **Add total clinics (tenants) count.** `AdminStatus` has no tenant count. Minimal path: in `Dashboard.razor` also inject `IAdminTenantQueries` and call `ListAsync(new(Page:1,PageSize:1))` — the returned `AdminPage.Total` gives the full tenant count without pulling rows. Render it as a prominent card ("تعداد کلینیک‌ها"). (Do NOT add new DB queries/columns.) Optionally also an "active clinics" card by calling `ListAsync(new("Active",null,1,1)).Total`. Keep it to ≤2 extra lightweight count queries (PageSize=1 ⇒ cheap COUNT + tiny page).
2. **Summarize status instead of two big tables.** Collapse Readiness + Workers into a compact, minimal presentation:
   - Keep a single concise "وضعیت سرویس‌ها" area: show overall health as one badge (OK if every probe status is healthy, otherwise Warn/Bad) plus a small list of only non-OK probes. Keep the full lists available but visually secondary (e.g., inside `<details>` so it's collapsed by default) to stay minimal.
   - Keep the key counters as `.cards` (commands running, outbox pending, dead letters, PG pool). Dead-letters card keeps its `bad` style when `>0`.
3. **Remove redundant quick-link clutter** at the top (`.quick` nav duplicates the sidebar). Keep at most 2–3 genuinely useful shortcuts (Clinics, New clinic). The sidebar already navigates everything.
4. **Modern/minimal styling:** reuse existing `.cards`/`.card` classes; if needed add a small CSS block in `wwwroot/admin.css` (e.g. a hero number style, a `.health-ok/.health-bad` chip). Keep it dependency-free, RTL-safe (logical properties), low-token.
5. **Localization:** add any new keys to `AdminStrings.cs` (e.g. `dash.clinics`, `dash.activeClinics`, `dash.health`, `dash.healthy`, `dash.unhealthy`, `dash.details`). Keep both En+Fa.
6. **Performance:** do not add polling beyond the existing `LiveStatusPanel`. The `PageSize:1` count calls are cheap. Keep `OnInitializedAsync` sequential awaits minimal.

**Acceptance:** dashboard shows clinic count + active count, a single compact health indicator, essential counters, no duplicated quick links; visually minimal/modern; no new DB schema; strings localized.

---

### [x] T2 — Clinics list page (`Pages/Tenants.razor` + `OperatorReadQueries.TenantsAsync` + `TenantSummary`)
**Goal:** list all registered clinics, show ≥15 at once, add-new entry here, good search/filter, and **sort by remaining subscription days ascending (least first)**.

**Do:**
1. **Ensure ≥15 visible / fix the "1 per page" perception.** In `Tenants.razor`: keep default `PageSize=50`. Verify the `[SupplyParameterFromQuery] PageSize` can't become 1 by accident (clamp to a sane minimum, e.g. `if (PageSize < 15) PageSize = 50;` before calling the query). The server `TenantsAsync` already clamps 1..200. Confirm `Pager` uses the same `PageSize`.
2. **Enrich each row with subscription info** so sorting by remaining days is possible and the operator sees status at a glance. `TenantSummary` currently = `(Id,Code,Name,Status)`. Extend it to also carry subscription period end + seats:
   - Edit `Models/AdminModels.cs`: `TenantSummary(string Id, string Code, string Name, string Status, DateTimeOffset? PeriodEndUtc=null, string? SubStatus=null, int SeatsLimit=0, int SeatsUsed=0)` (use defaults so other callers compile).
   - Edit `OperatorReadQueries.TenantsAsync` to left-join the subscription per tenant and project `CurrentPeriodEndUtc`, `Status`, `SeatLimit`, and a seats-used count. Keep it ONE query (project a subscription subquery per tenant to avoid N+1). SeatsUsed can reuse the same distinct-active-membership count pattern used in `TenantAsync` (line ~42) but if that is expensive per-row, it is acceptable to show only SeatsLimit and skip SeatsUsed in the list (seats-used already shown on detail). **Prefer: PeriodEndUtc + SubStatus + SeatsLimit in the list; SeatsUsed optional.**
   - Edit `AdminTenantQueries.ListAsync` (in `src/Animora.Server/Admin/AdminTenantQueries.cs`) to map the new fields.
3. **Sort by remaining days ascending** (`CurrentPeriodEndUtc` asc, nulls last), replacing / augmenting the current `OrderBy(DisplayName)`. Add a sort selector so the operator can switch (e.g. `sort=expiry` default asc, `sort=name`, `sort=expiry-desc`). Implement sort in `TenantsAsync` via a `switch` like `ProductsAsync` already does. Default = expiry ascending (least remaining first) per the request.
4. **"Add new clinic" entry on this page:** there is already `<a href="/admin/tenants/new">`. Keep/strengthen it (prominent button). (The nav-menu copy is removed in T3.)
5. **Search + filter:** search already matches name/code (ILIKE). Keep it. Status filter already present (`Active/Suspended/Closed/expiring/past-due/dead-lettered`). Make the filter bar clean/modern.
6. **Render remaining-days column** in the table: compute `days = ceil((PeriodEndUtc - now).TotalDays)`; show "N روز مانده" / "N روز گذشته" (reuse the pattern in `TenantDetail.razor` `RemainingText`). Color the badge: bad if negative/past-due, warn if ≤14, ok otherwise.
7. **Localization + CSS** for new column headers/sort labels (`tenants.remaining`, `tenants.sort`, `tenants.sortExpiry`, `tenants.sortName`, `tenants.seats`, `tenants.subStatus`, `tenants.daysLeft`, `tenants.daysOver`, etc.).

**Acceptance:** opening `/admin/tenants` shows many clinics at once (≥15 when data exists), each with remaining-days + status + seats; default order = least remaining days first; working search/filter/sort; add-new button present.

---

### [ ] T3 — New clinic page (`Pages/TenantNew.razor`, `Layout/NavMenu.razor`, create-tenant flow)
**Goal:** remove "New clinic" from the sidebar (keep it only on the clinics page), and make the create form minimal/modern/clearer; add more subscription plan presets; allow full custom days + user count.

**Do:**
1. **Remove nav entry.** In `Layout/NavMenu.razor` delete the line `<NavLink href="/admin/tenants/new">…newTenant…</NavLink>`. Keep the route/page itself. The clinics page (T2) is the entry point. (Dashboard quick-link to new clinic may remain.)
2. **Clearer, minimal form.** `TenantNew.razor` currently: Code, Name, OwnerUserName, OwnerName, Plan (select of active plans), Seats (override), StartTrial checkbox. Improve: group with clear legends, add helper hints per field (some exist: `new.seatsHint`, `new.demoHint`). Make labels unambiguous (e.g. clarify "Owner" = clinic manager account). Keep the plan `<InputSelect>` showing `Name (Code) — seats`. Modernize spacing/layout via existing CSS (no heavy redesign).
3. **"Custom days + user count" (کاستوم‌سازی کامل):**
   - **User count:** already supported via `Form.Seats` (`SeatLimitOverride`). Keep & clarify it (min 1, max 1000). Ensure empty = use plan's seat limit.
   - **Custom days:** the plan has only `TrialDays`. Add an **optional custom subscription length in days** at creation. Implementation note: the create path is `AdminSubscriptionAdapter.CreateTenantAsync` → `ProvisioningEngine.ProvisionAsync(ProvisioningRequest(...StartTrial))` → `Subscription.Start(tenant, plan, now, actor, startTrial, seatOverride)`. The simplest low-risk way to honor custom days WITHOUT touching the provisioning engine/migrations: after a successful create, if the operator entered custom days, immediately call the existing **extend** subscription op (`IAdminSubscriptionCommands.ExtendAsync(subscriptionId, form{Days})`). BUT the create receipt returns `TenantId`, not subscription id, and extend needs a stamp. **Decision:** Keep it simple and robust — add a "روز اشتراک سفارشی" number field to the New-clinic form that, when > 0, the page uses to show a post-create hint linking to the tenant detail where the operator can Extend by that many days; OR, if you can cleanly fetch the subscription (via `IAdminTenantQueries.GetAsync(tenantId)` → `Subscription.ConcurrencyStamp`), chain an `ExtendAsync` call right after create. Prefer the chained `GetAsync` + `ExtendAsync` approach so it's one action; guard with try/catch and surface any error. Do NOT add DB columns.
   - Document in code comments why extend is reused (single mechanism, mirrors TenantDetail "reward"/"extend").
4. **More subscription plan presets (marketing-standard).** The create form lists plans from the DB (`Plans.ListAsync`). To give the owner more standard options, SEED/ENSURE a sensible set of plan presets exists. Two acceptable implementations (pick the lower-risk one):
   - (Preferred) Add the presets as **quick-create suggestions on the Plans page** (T4) and/or a one-click "ایجاد پلن‌های پیش‌فرض" action, so the owner populates real DB plans. Then New-clinic automatically lists them.
   - Provide a documented standard preset set to seed: e.g. `trial` (14-day trial, 2 users, 0), `starter` (3 users, monthly), `standard` (6 users), `pro` (12 users), `clinic-plus` (25 users), `enterprise` (unlimited-ish 100 users). Amounts in IRT minor units — leave amount as operator-entered or sensible defaults; name them in Persian in the UI via plan `Name`. Keep codes `^[a-z0-9-]{2,32}$`.
   - **Do not hardcode fake plans into the dropdown that don't exist in DB** — provisioning validates `PlanCode` against active DB plans (`PlanNotFound` otherwise).
5. **Localization + CSS** for any new fields (`new.customDays`, `new.customDaysHint`, `new.seedPlans`, etc.).

**Acceptance:** sidebar no longer shows "New clinic"; create form is cleaner with clear hints; seat override + custom-days supported (custom days via reused extend path, no schema change); more plan presets available through the Plans page; provisioning still validates plan code.

---

### [ ] T4 — Plans page (`Pages/Plans.razor`)
**Goal:** keep behavior the same; just make it more minimal/modern. Optionally host the "seed default presets" action from T3.

**Do:**
1. Keep the plans table (Code, Name, Seats, Amount, Status, Deactivate) and the create-plan form (`CreatePlanForm`: Code, Name, Seats, AmountMinor, TrialDays). Don't change the data contract.
2. Modernize: tidy the table (it already uses `.table-wrap`), lay the create form fields in a clean grid/`.filters`-style row, clarify amount units (IRT minor → show hint), show TrialDays column (currently `PlanSummary.TrialDays` exists but isn't displayed). Add TrialDays to the table for completeness.
3. (Optional, ties to T3.4) add a single "ایجاد پلن‌های پیش‌فرض بازاریابی" button that creates the standard preset plans if missing (calls `IAdminPlanCommands.CreateAsync` per preset, ignoring already-exists `ConcurrencyConflict`). Keep idempotent + confirm-guarded. If this adds complexity/risk, defer and just note it; the primary requirement is visual polish.
4. Localization + CSS as needed (`plans.trialDays` already exists — verify; `plans.amountHint`, `plans.seedDefaults`).

**Acceptance:** plans page visually cleaner/modern, shows trial days, same data behavior; optional preset-seed button works idempotently.

---

### [ ] T5 — Messages & notifications tab (NEW broadcast page + fix per-clinic user dropdown)
**Goal:** add a top-level "پیام و اعلان" tab that can (a) message **all clinics**, (b) message **specific clinics by category/filter**, (c) target **roles**. AND fix the per-clinic notify so selecting a specific user is a **dropdown of that clinic's users** (not a typed user id).

#### T5a — Fix per-clinic user selection (`Pages/TenantNotify.razor`)
**Do:**
1. Inject `IAdminStaffQueries` into `TenantNotify.razor`. In `OnInitializedAsync`, load the clinic's users: `Queries.ListAsync(Id, new StaffListQuery(null, null, 1, 200), Cancellation)` → `AdminPage<StaffAccountSummary>` (has `UserId`, `Name`, `UserName`, `Role`).
2. Replace the `Form.UserValue` free-text `<InputText>` with an `<InputSelect @bind-Value="Form.UserValue">` whose options are the loaded users: `value="@u.UserId"` text `@u.Name (@u.UserName) — @Loc.T("role."+u.Role)`. Add a placeholder "—" option.
3. Keep server validation intact (kind 2 checks the user id is an active membership). Since options post real `UserId`, validation passes.
4. If the clinic has no users, show a hint and disable the "one user" audience option (or show empty select). Keep audience kinds 0/1 working.
5. Localization: reuse `notify.user`; maybe add `notify.userPick`, `notify.noUsers`.

**Acceptance:** on `/admin/tenants/{id}/notify`, choosing "یک کاربر مشخص" shows a dropdown of that clinic's real users; sending to a picked user succeeds without typing any id.

#### T5b — New global Messages tab (broadcast to all / by category / by role)
**Do:**
1. **Nav:** add `<NavLink href="/admin/notifications">@Loc.T("nav.notifications")</NavLink>` to `Layout/NavMenu.razor` (place it after Plans, logically grouped). Add `nav.notifications` string.
2. **New page** `Pages/Notifications.razor`, route `/admin/notifications`, `[Authorize(Policy="AdminOnly")]`, inherits `AdminPageComponent`. A composer form to broadcast:
   - **Target scope selector:** (0) all clinics, (1) clinics by status/category (reuse the Tenants status filters: Active / Suspended / expiring / past-due / etc.), optionally a search term to narrow. (2) a specific set of clinics (multi — simplest: a free multi-select of clinic codes/ids loaded from `IAdminTenantQueries.ListAsync`, PageSize up to 200).
   - **Role targeting within each clinic:** reuse the per-clinic `AudienceKind` (0 all users / 1 role). For broadcast, support "all users" or "a role" across the targeted clinics. (Per-specific-user doesn't make sense in broadcast — omit kind 2 here.)
   - **Message fields:** Kind/style (0..5), Tag, Title, Body, ScheduledForUtc?, ExpiresAtUtc?, Confirmed (reuse `SendNotificationForm` shape; create a dedicated `BroadcastNotificationForm` to carry scope + target filter + role + the message fields).
3. **Back-end capability (NEW):** add a broadcast method that fans out one `OperatorNotificationDraft` to every targeted tenant, each stored via the existing single-tenant `OperatorNotificationAdministration.SendAsync` (which already writes notification + audit atomically per tenant). Implementation path:
   - Add `BroadcastAsync(...)` to `OperatorNotificationAdministration` (or a new `OperatorNotificationBroadcast` service in the same folder) that: resolves the target tenant ids via `OperatorReadQueries.TenantsAsync(status, search, page, 200)` paging through all matches (or an explicit id list), then loops calling the existing per-tenant send logic. Reuse the existing validation + audit. Return a summary `(int Sent, int Skipped, IReadOnlyList<string> Errors)`.
   - Add a port `IAdminBroadcastCommands` in `Ports/AdminPorts.cs` with `Task<Result<BroadcastReceipt>> BroadcastAsync(BroadcastNotificationForm form, CancellationToken ct)`.
   - Add an adapter `AdminBroadcastAdapter` in `src/Animora.Server/Admin/` bridging the port to the service (actor = `AdminSession.Actor`, correlation from `IHttpContextAccessor`).
   - Register both in `AdminServiceCollectionExtensions.cs` (`services.AddScoped<...>`), mirroring `AdminNotificationAdapter`/`OperatorNotificationAdministration` wiring.
   - Add `BroadcastNotificationForm` + `BroadcastReceipt(int Sent,int Skipped,IReadOnlyList<string> Errors)` to `Models/AdminModels.cs`.
   - **Keep it safe:** require `Confirmed`; cap the number of tenants per broadcast reasonably; wrap each per-tenant send so one failure doesn't abort the whole run (collect into `Errors`). Each per-tenant send keeps its own transaction + audit (do NOT wrap thousands in one transaction).
4. **Note on delivery:** existing per-tenant notify stores the message server-side; actual desktop delivery is a TODO (sync feed) — mirror that; the console's job is authoring + storing + audit. Reuse the existing "saved, delivered when sync active" success message (`notify.sent`).
5. **Localization + CSS:** `notify.broadcast.title`, `notify.scope`, `notify.scope.all`, `notify.scope.category`, `notify.scope.specific`, `notify.scope.hint`, `notify.result` (e.g. "به N کلینیک ارسال شد"), plus the category/status labels (reuse `tfilter.*`).

**Acceptance:** a top-level "پیام و اعلان" page can send a message to all clinics, to clinics filtered by category/status, and to a chosen role; results summarized ("sent to N clinics"); per-clinic user targeting is a dropdown. No DB migrations; each send audited; failures collected not fatal.

---

### [ ] T6 — Final verification pass (lightweight, NO full build)
**Do:**
1. Re-read each changed `.razor`/`.cs` for: correct `Loc.T` keys all added to `AdminStrings.cs` (both En+Fa), records still compile (defaulted new params so other callers are unaffected), DI registrations added, no dangling references to the removed nav link.
2. Confirm `TenantSummary` field additions didn't break other consumers (grep usages of `new TenantSummary(` and `TenantSummary` — only `AdminTenantQueries` constructs it; defaults cover the rest).
3. Confirm `SendNotificationForm`/new forms bound correctly in Blazor (`[SupplyParameterFromForm]`).
4. (Optional, only if uncertain) a single targeted build of the Web.Admin + Server projects — skip if static review is confident.

**Acceptance:** all touched code is internally consistent; strings complete; no broken references.

---

### [ ] T7 — Cleanup: delete this plan file (FINAL TASK)
**Do:** delete `Plan/operator-console-revamp.md` only. Do NOT touch `Plan/reports/`, roadmap files, or anything else. Then run the final `scripts/git/push.sh "chore(plan): remove operator-console revamp plan after completion"`.

---

## Implementation guardrails (apply to every task)
- **Push after each task** with a conventional message, e.g. `feat(admin-dashboard): …`, `feat(admin-clinics): …`, `feat(admin-notify): …`, `refactor(admin-nav): …`. If a task's change is large, push mid-task too.
- **No DB migrations / no new columns** — reuse existing queries, extend/override subscription ops, and add defaulted record fields only.
- **Minimal, modern, RTL-safe CSS** in `wwwroot/admin.css` using logical properties; reuse existing classes first.
- **Every new/changed UI string** goes in `AdminStrings.cs` with both En+Fa.
- **Performance:** avoid N+1 (project subqueries in one EF query like `ProductsAsync`/`TenantAsync` already do); dashboard counts use `PageSize:1` for cheap totals.
- **Token-efficient edits:** prefer small `Edit` ops over rewriting whole files; open only files named in the current task.
- **Do not** modify desktop app, sync, server auth, or unrelated layers.
