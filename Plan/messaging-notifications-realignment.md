# Plan — Realign Messaging & Notifications to Vendor-Admin → Clinic (read-only)

> Self-contained execution plan. A fresh model can execute this **without re-auditing the repo**.
> Everything decided below (files, line anchors, exact edits, reasons) is final — don't re-derive it.
> Branch is `genspark_ai_developer`. **After finishing EACH task, run `scripts/git/push.sh "msg"`**
> (locked rule — no push = failure). Last task deletes THIS file only.

---

## 0. The problem in one paragraph (ground truth)

Animora is a **vendor-hosted SaaS**: the vendor (app owner) runs one central server + a browser
**admin panel** (`src/Animora.Web.Admin`); clinics buy a subscription, download the **offline-first
Avalonia desktop app**, and sync. The messaging/notification feature shipped last session (git T0–T9,
commits `0e2a83aa`..`d2da4b3b`) was built **wrong**: it lets the **clinic Manager on the Windows
desktop** compose messages and grant subscription rewards to their own staff. That is backwards.

**Target system:** only the **vendor/app-owner, in the browser admin panel**, may send messages (with a
chosen tag/kind for styling) and grant extra subscription/rewards to clinics and their users. Clinics &
their users **only receive and read** these on the desktop — read-only, no reply, fully synced,
standards-compliant, with a matching nice style. The desktop must stop being an author.

---

## 1. Key architecture facts you MUST rely on (already verified — do not re-check)

1. **Server + sync (Tracks D–E) are "authored but NOT wired into the desktop."** Per
   `AGENTS.md` and `backend-roadmap/PROGRESS.md`: the desktop runs **offline-only** today; there is no
   live server→desktop delivery pipeline running. The notification feature was deliberately built
   **local-only** (`[LocalOnly]` rows, "no sync needed"). Therefore a full live end-to-end vendor→desktop
   delivery is **out of scope**; wiring it is future sync work. **Do NOT attempt to build/run the server
   or sync pipeline.**
2. Because of (1), the realistic, correct, non-breaking scope is **two moves**:
   - **Desktop/client side → make strictly read-only** (remove all authoring: this is the security fix
     the user emphasized). This fully removes the "clinic can send/grant" capability from the client.
   - **Admin panel (browser) side → add the vendor authoring UI** (send message w/ tag+kind; grant
     reward = subscription days), wired to a **server-side adapter+port** persisting to the server DB so
     it is ready to be delivered by sync later. The reward reuses the **existing** subscription-extend
     path (`IAdminSubscriptionCommands.ExtendAsync`, already live to PostgreSQL).
3. **Build/verify**: `scripts/sandbox/build.sh` (Desktop, must be **0 warnings / 0 errors**),
   `scripts/sandbox/build.sh full` (whole solution). Smoke-run `scripts/sandbox/run.sh` only if you touch
   startup/DI/shell. **No full test suite / no long build** (user rule). Build the projects you changed.
4. **Layering rule (never violate):** Desktop ViewModels depend only on `Animora.Contracts` interfaces.
   No EF/SQL/HTTP in Desktop. Admin pages depend on `Animora.Web.Admin.Ports` interfaces; the concrete
   adapter lives in `src/Animora.Server/Admin/` over PostgreSQL. `Animora.Domain` has zero project refs.

---

## 2. Exact current-state file map (what exists, what it does, what to change)

### A. Contracts (`src/Animora.Contracts/Notifications/`)
- `INotificationService.cs` — has READ methods (`GetFeedAsync`, `GetUnreadCountAsync`, `MarkReadAsync`,
  `MarkAllReadAsync`, `FeedChanged`) **and WRITE methods to remove** (`CreateAsync`, `GrantRewardAsync`,
  `GetRecipientsAsync`, `bool CanManage`).
- `NotificationModels.cs` — keep `NotificationKind`, `NotificationAudienceKind`, `NotificationDto`,
  `NotificationFeed`. **Remove** `CreateNotificationRequest`, `GrantRewardRequest`, `NotificationRecipient`
  (they are the authoring DTOs; nothing read-only uses them).

### B. Client service (`src/Animora.Client.Services/Notifications/`)
- `NotificationService.cs` (26 KB) — implements the interface over local SQLite. **Remove** the write
  members (`CreateAsync`, `GrantRewardAsync`, `GetRecipientsAsync`, `CanManage`), the `RequireManager`,
  `IsManager`, `ResolveAudienceAsync`, `IsActiveUserAsync` helpers used only by writes, and the
  reward message composition. **Keep** everything read/feed/sweep/system-notice related.
  - **IMPORTANT — keep system notices:** `IssueSystemNoticesAsync` / `EnsureSubscriptionExpiryNoticesAsync`
    generate the "subscription ending" notices **locally for the manager**. These are *client-generated
    read-only status notices*, NOT vendor messages, and are legitimate offline UX. **Keep them.** They
    still write `NotificationRow`s but are automatic/local, not user-authored.
  - `GrantRewardAsync` used `RewardGrant` + `RewardOverlay`. The **reward-overlay mechanism stays**
    (rewards granted by the vendor must still extend the local effective subscription once delivered),
    but the **client no longer CREATES** `RewardGrant` rows — they will arrive via sync later. So keep
    `RewardOverlay.cs` and the `RewardGrant` entity read path; only remove client-side *creation*.
- `RewardOverlay.cs` — **keep as-is** (reads `RewardGrant` rows, applies to account status). Used by
  `UserProfileService`. Do not touch.

### C. Persistence (`src/Animora.Persistence.Sqlite/`)
- `Entities/NotificationRow.cs`, `NotificationReceiptRow.cs`, `RewardGrant.cs` — **keep all.** They are
  the local store rows the read feed + overlay need, and are the sync landing target. The
  `CreatedByUserId` on `NotificationRow` now means "the vendor operator / system", which is fine.
- `Configurations/Notifications/NotificationConfigurations.cs`, migration
  `Migrations/20261021120000__040_Notifications.cs` — **keep.** No schema change needed (the rows are
  reused; authoring just moves to the vendor side + sync later).

### D. Desktop UI (`src/Animora.Desktop/Pages/Notifications/`)
- `NotificationsPageView.axaml` — **remove** the two header buttons `OpenGrantRewardCommand`
  ("اعطای جایزه", lines ~89–95) and `OpenComposeCommand` ("پیام جدید", lines ~96–102). Keep
  "خواندن همه" (mark-all-read) + filters + cards. Keep the whole read experience & styling.
- `NotificationsPageViewModel.cs` — **remove** `CanManage` property, `OpenCompose`, `OpenGrantReward`
  commands, and the `CanManage = _service.CanManage` assignments (`OnNavigatedTo`, `LoadAsync`). Remove
  `IDialogService _dialogs` usage *only if* it becomes unused (it is passed to the removed modals; verify
  no other use — currently only compose/reward use it, so the ctor param + field can be removed).
- **Delete files** (authoring modals — no longer exist anywhere):
  `ComposeNotificationView.axaml`, `ComposeNotificationView.axaml.cs`, `ComposeNotificationViewModel.cs`,
  `GrantRewardView.axaml`, `GrantRewardView.axaml.cs`, `GrantRewardViewModel.cs`.
- `NotificationItemViewModels.cs` — read-only item VMs + the `NotificationAudienceOption` /
  `NotificationKindOption` option records used ONLY by compose/reward. **Remove** those two option records
  (verify: grep shows they're referenced only in the deleted modals). Keep `NotificationItemViewModel`,
  `NotificationChipViewModel`.
- `NotificationsPresentation.cs` — keep `KindLabel`, `Palette`, `FormatWhen`, `Failure`. The `RoleLabel`
  helper is used by compose; **remove `RoleLabel`** if grep confirms it's only used by the deleted modals
  (NotificationService uses `ProfileMapping.RoleLabel`, not this one — confirmed). Keep the rest.

### E. Desktop shell / DI
- `src/Animora.Desktop/App.axaml.cs` — DI block lines ~553–560 register `NotificationService`,
  `INotificationService`, `NotificationsPageViewModel`, `NotificationBadgeState`, `NotificationBridge`.
  **Keep all** (read side still needs them). Lines ~288 (`.Start()` due-sweep) and ~292
  (`NotificationBridge.Start()`) **keep** (feed still arrives & toasts). No DI removal needed unless the
  VM ctor signature changes (it will — drop `IDialogService` from the VM registration if it's a
  parameterized `AddSingleton`; check: it's `services.AddSingleton<NotificationsPageViewModel>();` which
  uses ctor injection, so just removing the ctor param is enough).
- `Shell/TopbarViewModel.cs`, `SidebarViewModel.cs` — only use `NotificationBadgeState` (the bell/unread
  badge). **No change** (badges are read-only already).
- `Shell/NavigationMenu.cs` line ~49 — the "اعلان‌ها و پیام‌ها" nav entry → **keep** (read page stays).

### F. Admin panel (`src/Animora.Web.Admin`) + server adapter (`src/Animora.Server/Admin`)
This is where authoring MOVES TO. Pattern to copy (already in repo):
- Ports interface in `src/Animora.Web.Admin/Ports/AdminPorts.cs` (e.g. `IAdminSubscriptionCommands`).
- Form/record models in `src/Animora.Web.Admin/Models/AdminModels.cs` (e.g. `SubscriptionActionForm`).
- Razor page in `src/Animora.Web.Admin/Pages/*.razor` + nav entry in `Layout/NavMenu.razor` + strings in
  `Localization/AdminStrings.cs` (bilingual `(en, fa)` tuples) + route registered via `Routes.razor`.
- Concrete adapter in `src/Animora.Server/Admin/*Adapter.cs` implementing the port, registered in
  `src/Animora.Server/Admin/AdminServiceCollectionExtensions.cs`.
- **Reward = subscription extension** already exists end-to-end: `IAdminSubscriptionCommands.ExtendAsync`
  → `AdminSubscriptionAdapter.MutateAsync(id,"extend",...)` → `ISubscriptionService.MutateAsync` → PG.
  The admin "grant reward" action should **reuse `ExtendAsync`** per tenant (days + reason) — do NOT
  invent a second subscription path. Existing `op.extend` string = "تمدید اشتراک".
- **Message to clinic** is the genuinely new admin surface. It needs a new port
  `IAdminNotificationCommands` + a new server adapter that **persists the message to the server DB
  outbox** so sync delivers it. BUT server-side notification tables do not exist yet (only the client
  `[LocalOnly]` ones do). **Decision to keep this task realistic & non-breaking:** author the admin
  message-composer UI + port + a server adapter that writes to a **new server-side `TenantNotification`
  outbox table** (minimal: tenant, audience, kind, tag, title, body, created-at, scheduled/expiry) in
  `src/Animora.Persistence.PostgreSql` with one new migration, returning a `MutationReceipt`. This is the
  vendor's authoritative message store; delivery to desktop is explicitly deferred to the sync track
  (leave a `TODO(sync, Tracks D–E)` comment). This keeps the authoring where it belongs and the data
  authoritative, without pretending the unwired pipeline is live.

> If, while implementing F, the server-side message table proves too large a change to do cleanly in one
> pass (new PG migration + EF config + adapter + page), it is acceptable to split it: first ship the
> **reward-via-extend** admin action (pure reuse, small), push it, then ship the **message composer**
> (new table) as its own task + push. Each sub-step must still build 0/0 and be pushed.

---

## 3. Security / correctness requirements (must hold at the end)

- The desktop/client has **no code path** that creates a `NotificationRow` from user input or creates a
  `RewardGrant`. (System expiry notices are the only client-written rows, and they are automatic/local.)
- `INotificationService` on the client exposes **only** read + mark-read + `FeedChanged`.
- No `CanManage`/permission UX hint remains on the client notifications surface.
- Admin authoring is guarded by the existing operator auth (`AdminSession`, operator cookie) — reuse the
  same pattern as other admin commands; never add a new unauthenticated endpoint.
- `scripts/sandbox/build.sh` = 0/0 after the desktop changes; `build.sh full` = 0/0 after the admin/server
  changes (or build just the changed server/admin projects with `dotnet build` if `full` is too slow —
  but it must compile cleanly).
- No dangling references: after deleting the modal files + option records, grep the solution for
  `ComposeNotification`, `GrantReward`, `CreateNotificationRequest`, `GrantRewardRequest`,
  `NotificationRecipient`, `CanManage` (notifications) → zero hits outside admin/server.

---

## 4. TASK LIST (execute top-to-bottom; push after EACH)

> Status legend: `[ ]` todo · `[~]` in progress · `[x]` done. Push command shown per task.

### [x] T1 — Contracts: make `INotificationService` read-only
- Edit `src/Animora.Contracts/Notifications/INotificationService.cs`: delete `CreateAsync`,
  `GrantRewardAsync`, `GetRecipientsAsync`, `bool CanManage`. Update the XML summary (remove "admin
  write"/"compose" wording; state it is a read-only receiver feed; vendor authoring is in the admin panel).
- Edit `NotificationModels.cs`: delete `CreateNotificationRequest`, `GrantRewardRequest`,
  `NotificationRecipient`. Keep enums + `NotificationDto` + `NotificationFeed`.
- Build will fail until T2/T3 — that's expected; do T1→T3 then build before pushing **once at T3**.
  (Exception to per-task push ONLY for the compile interdependency T1–T3: still push after T3 which is the
  first green state. If you prefer, you may do T1–T3 as one commit — acceptable since they're one atomic
  contract change.) **Push:** done at end of T3.

### [x] T2 — Client service: drop authoring members
- Edit `src/Animora.Client.Services/Notifications/NotificationService.cs`: remove `CreateAsync`,
  `GrantRewardAsync`, `GetRecipientsAsync`, `CanManage`, and the now-unused private helpers
  `RequireManager`, `IsManager`, `ResolveAudienceAsync`, `IsActiveUserAsync`, the `AudRole`/`AudUser`
  constants IF only used by removed code (verify: `AudRole` is also used by `IssueSystemNoticesAsync` →
  **keep `AudRole`**; `AudUser` was only reward/compose → check and remove if unused; `AudAll` used by
  `VisibleTo` → keep). Keep all read/sweep/system-notice logic and `FeedChanged`.
- Remove now-unused `using`s (e.g. `Animora.Domain.Identity` if `StaffRole` no longer referenced — but
  `IsManager`/system notices may still use `StaffRole`; keep what compiles).
- Keep `RewardOverlay.cs`, `RewardGrant` entity untouched.

### [x] T3 — Desktop notifications page → read-only + delete modals; build 0/0; PUSH
- `NotificationsPageView.axaml`: remove the two authoring header buttons (grant reward + new message).
- `NotificationsPageViewModel.cs`: remove `CanManage`, `OpenCompose`, `OpenGrantReward`, `_dialogs`
  field + ctor param (if now unused), and the `CanManage` assignments. Update class XML summary.
- Delete the 6 modal files (Compose*, GrantReward*).
- `NotificationItemViewModels.cs`: remove `NotificationAudienceOption` + `NotificationKindOption` records
  (grep-confirm unused elsewhere first).
- `NotificationsPresentation.cs`: remove `RoleLabel` if unused after deletions (grep-confirm).
- `App.axaml.cs`: no removals needed except confirm the VM still constructs (ctor param drop is fine with
  DI). Keep bridge + due-sweep start lines.
- Grep the whole `src/` for `ComposeNotification|GrantReward|CreateNotificationRequest|GrantRewardRequest|NotificationRecipient` and `CanManage` in notifications → ensure **zero** hits in Desktop/Contracts/Client.Services.
- Run `scripts/sandbox/build.sh` → **must be 0/0**. Fix anything.
- **Push:** `scripts/git/push.sh "refactor(notifications): make desktop client a read-only receiver; remove clinic-side compose/reward authoring"`

### [x] T4 — Admin panel: "grant reward (extra subscription days)" action (reuse ExtendAsync)
- This likely already exists as a subscription "extend" op on the tenant detail page
  (`src/Animora.Web.Admin/Pages/TenantDetail.razor` or `TenantLicenses.razor` — **open the tenant pages
  and grep for `op.extend`/`ExtendAsync` first**). If an extend/reward action is already present per
  tenant, confirm it's adequate and add a clearly-labelled **"اعطای جایزه (روز اشتراک رایگان)"** affordance
  (days + reason) that calls `IAdminSubscriptionCommands.ExtendAsync`. If absent, add it.
- Add bilingual strings to `Localization/AdminStrings.cs` (e.g. `reward.grant`, `reward.days`,
  `reward.reason`, `reward.success`).
- Build the admin + server projects: `dotnet build src/Animora.Web.Admin src/Animora.Server` (or
  `scripts/sandbox/build.sh full`) → **0/0**.
- **Push:** `scripts/git/push.sh "feat(admin): vendor grants subscription-day rewards to a clinic via existing extend path"`

### [x] T5 — Admin panel: "send message to clinic/users" composer (new vendor authoring)
- **PostgreSQL store:** add a `TenantNotification` entity + EF config in
  `src/Animora.Persistence.PostgreSql` (fields: Id, TenantId, AudienceKind, AudienceValue, Kind, Tag,
  Title, Body, CreatedByOperatorId, CreatedAtUtc, ScheduledForUtc?, ExpiresAtUtc?) + one new migration
  (next free server ordinal — **read `backend-roadmap/PROGRESS.md` "Post-C11 drift" migration rule** for
  the correct numbering before picking a number; server PG head is `111_ServerOutbox`, so next is `112`).
  Keep it minimal; mirror the client `NotificationRow` field semantics so sync mapping is trivial later.
- **Port:** add `IAdminNotificationCommands` to `Ports/AdminPorts.cs` with
  `Task<Result<MutationReceipt>> SendAsync(string tenantId, SendNotificationForm form, CancellationToken ct)`.
- **Form:** add `SendNotificationForm` to `Models/AdminModels.cs` (AudienceKind, AudienceValue?, Kind,
  Tag?, Title, Body, ScheduledForUtc?, ExpiresAtUtc?, Confirmed) — **the Kind field is the "tag/style"
  selector the user asked for** (Info/Success/Warning/Promo/System). Validate lengths server-side.
- **Adapter:** add `AdminNotificationAdapter.cs` in `src/Animora.Server/Admin/` implementing the port,
  writing a `TenantNotification` row via the server unit-of-work; register it in
  `AdminServiceCollectionExtensions.cs`. Add a `// TODO(sync, Tracks D–E): deliver to desktop via the
  state-sync feed once wired.` comment (honest about delivery).
- **UI:** add a Razor composer (reuse the look of existing admin forms; RTL Persian labels) — either a new
  page `Pages/TenantNotify.razor` + nav/route, or a section on `TenantDetail.razor`. Audience = all
  users / a role / one user; Kind = style selector; Title/Body; optional schedule+expiry. Add strings.
- Build admin + server + PG projects → **0/0** (`dotnet build` the three, or `build.sh full`).
- **Push:** `scripts/git/push.sh "feat(admin): vendor composes tagged messages to a clinic's users (server-authored, read-only on desktop)"`

### [x] T6 — Final styling & consistency pass on the desktop receiver
- Verify the desktop notifications page still looks right with the authoring buttons gone (header no longer
  has primary/outline action buttons except "خواندن همه"). If the header looks sparse/unbalanced, tidy the
  layout (keep the unread summary + mark-all-read). Ensure Reward-kind messages (received from vendor
  later) render with the existing `IconSpark`/gold palette (already in `Palette`). No functional change.
- Confirm `NotificationKind.Reward` + `Promo` styles are intact in `NotificationsPresentation.Palette`.
- Build desktop `scripts/sandbox/build.sh` → **0/0**. Smoke-run `scripts/sandbox/run.sh` (touched shell/UI)
  → expect `SMOKE RUN OK`.
- **Push:** `scripts/git/push.sh "style(notifications): polish read-only receiver header after authoring removal"`

### [ ] T7 — Delete this plan file (final task)
- `rm Plan/messaging-notifications-realignment.md` (delete ONLY this file; leave `Plan/reports/**` and all
  other files untouched; do NOT touch any roadmap file).
- **Push:** `scripts/git/push.sh "chore(plan): remove completed messaging-notifications realignment plan"`

---

## 5. Guardrails / notes for the executing model
- **Do NOT** build/run the ASP.NET server or sync; they are unwired. Just compile the projects you edit.
- **Do NOT** run the full test suite or long builds. Targeted `dotnet build` / `build.sh` only.
- If a task's change turns out large/heavy (e.g. T5's PG migration + config), push it as its own commit
  with a clear message (push rule covers this).
- Keep every edit idiomatic & minimal; preserve Persian/RTL wording quality.
- If a grep reveals an unexpected extra consumer of something you planned to delete, adapt: keep the
  symbol if still needed; the intent is "client is read-only, vendor authors." Correctness over literal
  deletion.
- After T5, if server-side delivery wiring is explicitly requested later, that's the sync track — out of
  this plan's scope.
