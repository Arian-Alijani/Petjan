# Plan — Multi‑account / multi‑clinic login + sync integrity (Animora desktop)

> **Audience:** an AI coding model resuming in a FRESH session. This file is self‑contained:
> every fact, file path, line reference, root cause and decision needed to implement the fixes
> is written here. You should NOT need to re‑investigate the codebase broadly — go straight to
> the named files/methods and apply the described change.
>
> **Repo:** Animora — .NET 8 / Avalonia (Desktop) offline‑first veterinary‑clinic app with a
> custom sync engine against an ASP.NET Core server. Local store = encrypted **single‑tenant**
> SQLite. Language of UI strings = Persian (fa).
>
> **LOCKED RULE (do not break):** after EACH completed task below, run
> `scripts/git/push.sh "type(scope): message"` to commit + rebase + push. Not pushing = failure.
> Branch: `genspark_ai_developer`.

---

## 0. How the login / session / sync pipeline actually works (verified facts)

Login flow (server build, which is the case in the logs):

1. `LoginViewModel.SignInAsync` (`src/Animora.Desktop/Pages/Login/LoginViewModel.cs:205`)
   → `EstablishServerSessionAsync` (`:301`) → `IOnlineAuthClient.LoginAsync`.
2. `OnlineAuthClient.LoginAsync` (`src/Animora.Client.Services/Identity/OnlineAuthClient.cs:29`):
   `POST /auth/login` → `POST /auth/device/register` → clears session →
   **`ILocalStoreMaintenance.EnsureBoundToAsync(serverTenantId)`** (`:74`) → persists session →
   mirrors capabilities → seals licence → `POST /licensing/renew`.
3. Back in VM: `SignInLocalAsync` (`:252`):
   - fresh install + Manager → `CompleteFirstRunAsync`.
   - else → **`EnsureLocalMirrorAsync`** (aligns local user id to server id) → `SignInAsync` (local).
4. `AuthenticationService.SignInAsync` → `SignInCommand` → local session opened → shell.

Tenant binding / isolation: `LocalStoreMaintenance.EnsureBoundToAsync`
(`src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs:57`):
- If installation already bound to the SAME tenant → no‑op.
- If DIFFERENT tenant → **wipes every table** except `LocalInstallation` + `Permission`,
  rebinds, re‑seeds Tenant + RoleDefinitions. **This is a destructive single‑tenant switch.**
- The DB is a single fixed file (`LocalStoreOptions.DatabasePath`), NOT per‑tenant.

Replica apply (sync pull → typed tables): `ReplicaMaterializer.ApplyAsync`
(`src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs:308`). For each staged server row it
upserts into the local typed table via `INSERT … ON CONFLICT("Id") DO UPDATE …`
(`BuildUpsert`, `:757`). On `SQLITE_CONSTRAINT` (code 19) it SKIPs the row and keeps a durable
retry in `repl_staging` under batch `__animora_pending_typed_projection__` (`DeferAsync`, `:483`).

---

## 1. ROOT CAUSES (fully diagnosed from the two attached client logs)

### BUG A — StaffMembership never syncs: `SKIP … local constraint violation` forever  ⟵ MAIN BUG
Log evidence (every sync pass, all clients/users):
```
WRN [sync.apply] ReplicaMaterializer.Apply — SKIP StaffMembership/<serverId>:
     local constraint violation; durable projection retry retained.
```
- Machine A (install a960…, users ryan/mamad): fails `StaffMembership/01M4ZR1TXJKT3Y7M5Q14ZMB7P5`.
- Machine B (install c8d897…, user owner2): fails `StaffMembership/01M4XNRZN8HA3AAHTVD2S0EH0H`.

**Why:** the local SQLite table `StaffMemberships` has a UNIQUE index
`IX_StaffMemberships_TenantId_UserId_Role` on `(TenantId, UserId, Role)`
(`src/Animora.Persistence.Sqlite/Configurations/Identity/StaffMembershipConfiguration.cs:15`).

When a user signs in, `ProvisionLocalMirrorUserCommand`
(`src/Animora.Application/UseCases/Identity/ProvisionLocalMirrorUserCommand.cs`) creates/refreshes
the local user with the **server user id** (`User.CreateWithId`) but creates its membership via
`StaffMembership.Create(tenant, userId, role, clock)` which assigns a **random local Id**
(`src/Animora.Domain/Identity/StaffMembership.cs` `Create` → `Id = IdGenerator.NewId(now)`).

Then sync pulls the server's StaffMembership for the SAME user — same `(TenantId, UserId, Role)`
but a DIFFERENT (server) `Id`. The upsert conflicts on `Id`, so it's treated as an INSERT, which
then violates the UNIQUE `(TenantId, UserId, Role)` index already occupied by the local mirror row
→ `SQLITE_CONSTRAINT` → SKIP → retried forever (harmless to business data, but it permanently
pollutes the trace, stalls the identity projection, and means peer staff rows/roles authored on the
server never become visible locally).

**This is the "sync error" the user reported across different clients.** It is deterministic and
reproduces on every login.

### BUG B — Switching to another clinic is blocked by unsynced data (Task 2 & 3)
`EnsureBoundToAsync` (`LocalStoreMaintenance.cs:78‑80`):
```csharp
if (await db.Set<PendingCommand>().IgnoreQueryFilters()
    .AnyAsync(x => x.Status != Settled && x.Status != Reversed, ct))
    return Result.Fail(ErrorCodes.SyncPendingChanges, "tenant_switch_requires_settlement");
```
So when the user tries to log into clinic B while clinic A still has unsettled commands, the login
fails with `SYNC_PENDING_CHANGES`; UI shows (LoginViewModel `MapError` `:398`):
> «کلینیک قبلی تغییرات ارسال‌نشده دارد؛ ابتدا وارد همان کلینیک شوید و همگام‌سازی را کامل کنید.»

Because the store is single‑tenant with a destructive wipe, there is **no mechanism to keep clinic
A's unsynced data aside** and let the user enter clinic B, then return to A later and finish syncing.
User requirement (Task 3): the warning must stay clean, the user MUST be allowed to switch, and
clinic A's unsynced data must be preserved (isolated) so they can return and sync it.

### NON‑BUG (context only — do NOT try to "fix" the server licence)
- First session on machine A: `/licensing/renew` returned **409 LICENSE_INVALID**, so `/sync/manifest`
  kept returning **403 ENTITLEMENT_REQUIRED / license_not_active**. In the later successful sessions
  `/licensing/renew` returned **200** and sync proceeded. This is a **server‑side licence/subscription
  state** issue for that tenant, NOT a client bug. Out of scope for code changes. (Only relevance:
  confirms the manifest 403 is entitlement, not auth.)

---

### VERIFIED call‑chain facts (gathered 2026‑10‑16, so T2/T3 need no re‑investigation)
- `ILocalStoreMaintenance.EnsureBoundToAsync(string tenantId, CancellationToken ct)`
  (`src/Animora.Persistence.Sqlite/Maintenance/ILocalStoreMaintenance.cs:9`) is the ONLY tenant‑bind
  entry point. It is called from exactly two places:
  - `OnlineAuthClient.LoginAsync` (`src/Animora.Client.Services/Identity/OnlineAuthClient.cs:74‑76`)
    inside a `scopes.CreateAsyncScope()`; a failure returns the login as failed.
  - `OfflineSessionAuthorizer` (`src/Animora.Client.Services/Identity/OfflineSessionAuthorizer.cs:47`).
- `EnsureBoundToAsync` today performs a **row‑level wipe** (not a file swap): same‑tenant → `state.Set`
  + `Ok`; different‑tenant → under `TransitionGate` + a transaction it (1) hard‑fails with
  `SyncPendingChanges`/`tenant_switch_requires_settlement` if any `PendingCommand.Status` is not
  `Settled`/`Reversed` (`LocalStoreMaintenance.cs:78‑80`), else (2) `DELETE FROM` every table except
  `LocalInstallation`+`Permission`, (3) `installation.BindTenant(tenantId)`, (4) re‑seed Tenant +
  RoleDefinitions, (5) reset appointment‑settings sidecar, (6) commit, (7) `state.Set`, clear
  `SessionContextCache`, invalidate `IClinicTimeZoneProvider`.
- The store is a SINGLE encrypted SQLite file at `LocalStoreOptions.DatabasePath`; sidecars are the
  `-wal`/`-shm` files (`SideSuffixes = ["","-wal","-shm"]`) and the appointment‑settings JSON
  (`SettingsSidecarSuffix`). `Store.DataDirectory` = the dir containing the db.
- Existing reusable file plumbing in `LocalStoreMaintenance`: `Open(path, sessionPragmas)` (keyed
  connection), `source.BackupDatabase(destination)` (hot copy, used by `BackupAsync`), `QuickCheck`,
  `MoveToQuarantine`, `UniquePath`, `SqliteConnection.ClearAllPools()`. Use these — do not hand‑roll.
- `IDialogService` (`src/Animora.Desktop/Services/IDialogService.cs`) has NO built‑in confirm; reuse the
  existing **`ConfirmDialogViewModel`** (`src/Animora.Desktop/Pages/Sales/Shared/ConfirmDialogViewModel.cs`)
  — ctor `(IDialogService, tone, iconKey, title, bodyLead, bodyStrong, bodyTrail, bodyNote, confirmLabel,
  cancelLabel, Action? onConfirm)`; it closes the dialog then invokes `onConfirm`. Show it via
  `_dialogs.ShowModal(vm, 520)`. Tones: `danger|warn|brand|neutral`.
- Pending count for the message: `PendingCommandSummaryQuery` → `PendingCommandSummary.Total`
  (`src/Animora.Application/UseCases/Sync/PendingCommandSummaryQuery.cs`). LoginViewModel does not yet
  take an `IDispatcher`/mediator; `IAuthenticationService` is what it holds. Simplest: thread the count
  through the `SyncPendingChanges` failure detail OR add a tiny query port the VM can call. **Chosen:
  put the count in the failure `Detail`** (format `tenant_switch_requires_settlement:<N>`) so no new VM
  dependency is needed — `EnsureBoundToAsync` already has the db to `Count()` the unsettled commands.
- `OnlineLoginOutcome` (`IOnlineAuthClient.cs:12`) is a record; `MapError` lives in `LoginViewModel`
  (`:353`) and already has a `SYNC_PENDING_CHANGES` Persian string (`:398`) — that string becomes the
  dialog body, not a dead‑end error, after T3.

---

## 2. DECISIONS (so the implementing model does not re‑derive them)

**Fix A — reconcile StaffMembership on its business key (chosen).**
In `ReplicaMaterializer`, before upserting a `StaffMembership` row, delete any local row with the
same `(TenantId, UserId, Role)` under a DIFFERENT `Id` (the random local mirror row), exactly
mirroring the existing `HandoffMap.PurgeLegacySql` pattern (`ReplicaMaterializer.cs:100‑111`).
Then the server row inserts cleanly and becomes the single source of truth; the local mirror row
is superseded. The mirror membership's purpose (a row so local sign‑in/capabilities work before the
first sync) is preserved because the server row carries the same user+role.
- The `StaffMembership` map already reads `UserId` (Required) and `Role` into `values`
  (`ReplicaMaterializer.cs:289‑294`), so both are available at upsert time.
- Scope the purge to `StaffMembership` only (do not add a generic purge that could delete unrelated
  rows). Implement as a small special‑case block in `ApplyAsync` right before the savepoint/upsert,
  OR add an optional `BusinessKeyPurgeSql` to `EntityMap` used only by StaffMembership. Prefer the
  minimal, local special‑case to avoid touching every entity.
- Also fix the SOURCE so new mirrors stop minting conflicting ids: in
  `ProvisionLocalMirrorUserCommand`/`StaffMembership.Create`, when a membership already exists for
  the user it is fine; the durable fix is the purge above. (Optional hardening: make the mirror
  membership idempotent to the server row — but the purge alone resolves the reported bug.)

**Fix B — allow clinic switch while preserving the outgoing clinic's unsynced data (chosen).**
Keep the single‑tenant model; add a **per‑tenant stash** of the whole local store around a switch:
- When `EnsureBoundToAsync` is about to switch away from tenant X and X has unsettled commands,
  instead of hard‑failing: **stash** X's current DB file(s) to a per‑tenant location
  (e.g. `<DataDirectory>/clinics/<tenantId>/animora.db`, reuse the existing backup/quarantine file
  plumbing in `LocalStoreMaintenance` — `Open`, `BackupDatabase`, `SideSuffixes`), then proceed with
  the normal wipe+rebind to the new tenant.
- When binding TO a tenant that has a stash, **restore** that stash instead of wiping to empty, so
  returning to clinic A brings back its unsynced commands and local data; sync can then complete.
- This must be gated behind an explicit user choice (do not silently switch): surface the pending
  state in the UI and let the user confirm "enter the other clinic (keep this clinic's unsynced data
  for later)". The confirmation lives in the login flow (see Fix C).
- Hard isolation requirement: a stash is keyed strictly by `tenantId`; a restore only ever loads the
  stash whose tenant matches the tenant being bound. One clinic's rows must never surface in another.
- If you judge the full stash/restore too large for one pass, the MINIMUM acceptable behaviour is:
  (a) detect the pending‑data situation, (b) let the user explicitly choose to switch, (c) back up
  clinic A's store to a clearly‑labelled per‑tenant backup before wiping (so nothing is lost and it
  can be restored manually/automatically later). Never silently discard unsynced data.

**Fix C — clean UX for the unsynced‑data warning + switch affordance (chosen).**
- Replace the dead‑end `SYNC_PENDING_CHANGES` error with a clean dialog/flow that (1) clearly states
  the current clinic has N unsynced changes, (2) offers "go back and sync" AND "switch anyway, keep
  them for later", (3) proceeds via Fix B when the user chooses to switch.
- Pending count/summary is available via `PendingCommandSummaryQuery`
  (`src/Animora.Application/UseCases/Sync/PendingCommandSummaryQuery.cs`) → `.Total`.
- Keep all new strings in Persian, consistent with existing `MapError` tone.

---

## 3. TASK LIST (execute top‑to‑bottom; push after each)

- [x] **T0. Plan created** — this file. (push)

- [x] **T1. Fix BUG A — StaffMembership sync constraint violation. (DONE)**
  - Implemented: `ReplicaMaterializer.cs` adds `StaffMembershipPurgeSql` and, inside the typed-projection
    savepoint, deletes the local StaffMembership row with the same `(TenantId,UserId,Role)` under a
    different id before the server row's upsert. Added regression test
    `SyncedStaffMembership_SupersedesTheRandomIdLocalMirror_ForTheSameUserAndRole` in
    `tests/Animora.Sync.Tests/ReplicaMaterializerTests.cs`. (dotnet not available in sandbox → CI validates.)
  - Original notes below:
  - File: `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs`.
  - In `ApplyAsync`, for `row.Entity == "StaffMembership"` and a non‑tombstone payload, BEFORE the
    `CreateSavepointAsync`/upsert, run a purge:
    `DELETE FROM "StaffMemberships" WHERE "TenantId"={tenant} AND "UserId"={values["UserId"]}
     AND "Role"={values["Role"]} AND "Id" <> {row.EntityId}`.
    Use `ExecuteSqlRawAsync` with positional args, matching the `HandoffMap.PurgeLegacySql` style.
  - Rationale + key facts: see §1 BUG A / §2 Fix A. UNIQUE index is `(TenantId,UserId,Role)`.
  - Validate: add/adjust a focused unit test proving a server StaffMembership row with a different Id
    but same (tenant,user,role) replaces the local mirror row without a constraint skip. Look under
    `tests/` for an existing ReplicaMaterializer/sync apply test to extend (search `ReplicaMaterializer`
    or `repl_staging`). Do NOT run the full/long test suite — only the targeted test.
  - (push) `fix(sync): reconcile StaffMembership by business key so server row supersedes local mirror`

- [x] **T2. Fix BUG B + Task 2/3 — allow clinic switch, preserve outgoing clinic's unsynced data. (DONE)**
  - Implemented: `ILocalStoreMaintenance`/`LocalStoreMaintenance` 3-arg `EnsureBoundToAsync(tenantId, preserveOutgoingUnsynced, ct)`,
    per-tenant stash `<data>/clinics/<tenantId>/animora.db` (verified by quick_check), restore on bind-to, count in failure detail;
    test `tests/Animora.Persistence.Tests/TenantSwitchStashTests.cs`. CI validates (no local build).
  File: `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` + the interface
  `ILocalStoreMaintenance.cs`. Chosen mechanism (file‑level per‑tenant stash, reusing existing plumbing):
  - **Signature change:** add an overload/optional param
    `EnsureBoundToAsync(string tenantId, bool preserveOutgoingUnsynced, CancellationToken ct)`
    (keep the old 2‑arg signature delegating with `preserveOutgoingUnsynced: false` so
    `OfflineSessionAuthorizer` and tests do not change). Update `ILocalStoreMaintenance`.
  - **Pending detail with count (for T3):** when the different‑tenant branch finds unsettled commands
    AND `preserveOutgoingUnsynced == false`, keep failing but put the count in the detail:
    `Result.Fail(ErrorCodes.SyncPendingChanges, $"tenant_switch_requires_settlement:{count}")` where
    `count = await db.Set<PendingCommand>().IgnoreQueryFilters().CountAsync(x => x.Status != Settled && x.Status != Reversed, ct)`.
  - **Stash on switch‑away (preserve == true):** BEFORE the wipe, if the current (outgoing)
    `installation.TenantId` is a real tenant (not `DefaultTenantId`), hot‑copy the live db to a
    per‑tenant stash path and snapshot the settings sidecar:
    `stashDb = <DataDirectory>/clinics/<outgoingTenantId>/animora.db` (via `Directory.CreateDirectory`
    + `UniquePath`‑style but DETERMINISTIC per tenant — overwrite the previous stash for that tenant so
    only the latest state is kept). Implement `StashCurrentAsync(outgoingTenantId, ct)` using
    `using var src = Open(Store.DatabasePath); using var dst = Open(stashDb, sessionPragmas:false);
    src.BackupDatabase(dst);` then copy the `SettingsSidecarSuffix` file next to `stashDb` if present.
    `QuickCheck(stashDb)` must be `"ok"` or fail the whole switch (never wipe without a good stash).
  - **Restore on bind‑to (always, independent of the flag):** at the top of the different‑tenant branch,
    if `<DataDirectory>/clinics/<tenantId>/animora.db` exists and `QuickCheck` = ok, RESTORE it instead
    of wiping‑to‑empty: after the normal wipe of the live file is not needed — instead copy the stash
    over the live db (`SqliteConnection.ClearAllPools()`, close EF connections first), re‑open, verify
    the restored `LocalInstallation.TenantId == tenantId`, then DELETE the consumed stash folder. Because
    restoring replaces the whole file the destructive table‑wipe path is skipped for that case. Keep the
    seed‑Tenant/RoleDefinition path ONLY for the no‑stash (brand‑new clinic) case.
  - **Hard isolation:** a stash dir is named strictly by `tenantId`; a restore only ever loads
    `clinics/<tenantId>/…`. Never read another tenant's folder. One clinic's rows can never surface in
    another because each stash is a complete, separate encrypted db file.
  - **Minimum‑acceptable fallback** (if the full file‑swap restore proves too large in one pass): still
    (a) add the flag + count detail, (b) when `preserve == true` STASH the outgoing db with
    `BackupDatabase` to `clinics/<outgoingTenantId>/` before the existing row‑wipe, (c) on bind‑to a
    tenant that has a stash, log it and leave a clearly‑named backup so nothing is lost (manual restore).
    Never silently discard unsynced data.
  - **Order of operations (critical):** drain `TransitionGate` (already held), snapshot/close EF conns,
    stash outgoing → restore incoming OR wipe+seed → `BindTenant` → commit → publish `state.Set`, clear
    `SessionContextCache`, invalidate zone. A failure before commit must leave the live store on the OLD
    tenant (do not delete a stash until after the successful bind that consumed it).
  - Validate with a targeted maintenance test in `tests/` (find an existing `LocalStoreMaintenance`/
    `EnsureBoundTo` test to extend): bind A, write a pending command, switch to B with
    `preserveOutgoingUnsynced:true`, assert B is empty of A's pending; switch back to A, assert A's
    pending command is restored and B's rows are absent. Targeted test only — NO full/long suite.
  - (push) `feat(store): isolate & preserve per-clinic unsynced data across tenant switch`

- [x] **T3. Fix UX (Task 1 & 3) — clean warning + explicit switch affordance in login. (DONE)**
  - Implemented: `IOnlineAuthClient.LoginAsync`/`OnlineAuthClient` take `preserveUnsyncedOnSwitch` (forwarded to `EnsureBoundToAsync`);
    `LoginViewModel` shows a `warn` `ConfirmDialogViewModel` with the unsynced count on the first `SYNC_PENDING_CHANGES`, and the
    confirm re-drives sign-in with the preserve flag (no prompt loop). Same-clinic multi-user path needs no code (T1 purge is role-agnostic).
    CI validates (no local build).
  File: `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` only (reuse existing
  `ConfirmDialogViewModel`; no new axaml needed).
  - Thread the confirm choice into the online login so T2's `preserveOutgoingUnsynced` runs. Minimal
    approach without widening `IOnlineAuthClient`: add a private `bool _confirmedClinicSwitch` field on
    the VM, and in `EstablishServerSessionAsync` pass it through — BUT `IOnlineAuthClient.LoginAsync`
    does not take the flag. Chosen: widen `IOnlineAuthClient.LoginAsync` + `OnlineAuthClient.LoginAsync`
    with an optional `bool preserveUnsyncedOnSwitch = false` param forwarded to
    `EnsureBoundToAsync(login.TenantId, preserveUnsyncedOnSwitch, ct)`. (Only two impls — real + any test
    double; keep the default so nothing else changes.)
  - Flow in `SignInAsync`: on the FIRST attempt `_confirmedClinicSwitch = false`. If
    `EstablishServerSessionAsync` fails and `result.Error.Code == SYNC_PENDING_CHANGES`, DO NOT just set
    `ErrorMessage`; parse N from the detail (`tenant_switch_requires_settlement:<N>`) and show a
    `ConfirmDialogViewModel` (tone `warn`): title «ورود به کلینیک دیگر», body «این کلینیک N تغییر
    همگام‌نشده دارد. می‌توانید به همان کلینیک برگردید و همگام‌سازی را کامل کنید، یا به کلینیک دیگر وارد
    شوید؛ تغییرات این کلینیک حفظ و دفعهٔ بعد که برگشتید همگام می‌شوند.», confirm «ورود و حفظ تغییرات»,
    cancel «بازگشت». `onConfirm` sets `_confirmedClinicSwitch = true` and re‑invokes `SignInCommand`
    (re‑drives the whole sign‑in; on the retry the flag makes T2 stash+switch).
  - Keep a safety net: if the server session still returns `SYNC_PENDING_CHANGES` even with the flag
    true (should not happen), fall back to the current `ErrorMessage` so there is no infinite loop
    (guard: only prompt when `!_confirmedClinicSwitch`).
  - **Task 1 (same machine, multiple SAME‑clinic users: ryan Doctor → mamad Cashier → owner2 Manager):**
    this path keeps the SAME `tenantId`, so `EnsureBoundToAsync` short‑circuits (`installation.TenantId
    == tenantId` → `state.Set` + `Ok`) and never wipes — after T1 the StaffMembership skip is gone and
    data integrity holds. No code needed here beyond confirming the StaffMembership purge (T1) covers the
    Cashier/Manager server rows too (log shows the same skip for mamad/owner2 — T1's business‑key purge
    is role‑agnostic, so it does). State this in the commit body.
  - Validate: targeted VM reasoning / a small VM test if an existing LoginViewModel test harness exists;
    otherwise rely on CI build + the T2 maintenance test. No full/long test.
  - (push) `feat(login): clear unsynced-data warning with explicit clinic-switch choice`

- [ ] **T4. Final — delete this plan file.**
  - Delete `Plan/TODO-auth-sync-multiaccount.md` ONLY. Do not touch any other file or roadmap docs.
  - (push) `chore(plan): remove completed auth/sync multi-account plan`

---

## 4. Guardrails (from the user, mandatory)
- Investigate only what a task needs — no broad/aimless file reading.
- Standard, high‑quality, minimal‑diff changes.
- **Do NOT** run full builds or long/full test suites; run only the targeted test for the change.
- Push after every task via `scripts/git/push.sh` (locked rule). If a single task required a large/
  heavy fix, push it with a correct, understandable message.
- A GitHub Actions workflow already runs build/test on push — rely on it instead of local full runs.
- Keep tenant data isolation absolute; never leak one clinic's data into another; never lose
  unsynced data.
