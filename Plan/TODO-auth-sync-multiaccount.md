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

- [ ] **T2. Fix BUG B + Task3 — allow clinic switch, preserve outgoing clinic's unsynced data.**
  - File: `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` (`EnsureBoundToAsync`
    and new stash/restore helpers reusing `Open`/`BackupDatabase`/`SideSuffixes`/quarantine plumbing).
  - Behaviour: on switch‑away with unsettled commands → stash by tenant; on bind‑to a tenant with a
    stash → restore it; strict per‑tenant isolation. See §2 Fix B for the exact contract and the
    minimum‑acceptable fallback.
  - Add an explicit opt‑in signal (parameter/flag or a separate method) so the destructive/stash path
    only runs when the user confirmed in the UI (wired in T3). Default = current safe behaviour
    (still fail with `SYNC_PENDING_CHANGES`) when not confirmed, so nothing regresses.
  - Validate with a targeted maintenance test (stash A → switch to B → switch back to A restores A's
    pending commands; B never sees A's rows). Targeted test only.
  - (push) `feat(store): isolate & preserve per-clinic unsynced data across tenant switch`

- [ ] **T3. Fix UX (Task 1 & 3) — clean warning + switch affordance in login.**
  - Files: `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` (+ its `.axaml` if a dialog is needed),
    `IDialogService` for the confirm dialog, `PendingCommandSummaryQuery` for the count.
  - When `EstablishServerSessionAsync` would fail with `SYNC_PENDING_CHANGES`, show a clean Persian
    confirm dialog ("این کلینیک N تغییر همگام‌نشده دارد … بازگشت و همگام‌سازی / ورود به کلینیک دیگر و
    نگه‌داری تغییرات برای بعد"). On "switch anyway" → re‑drive login with the confirmed flag so T2's
    stash+switch path runs; then continue the normal mirror + local sign‑in.
  - Also ensure Task 1 (same machine, multiple user accounts of the SAME clinic, e.g. doctor then
    cashier) stays seamless — that path does NOT switch tenants (same tenantId), so after T1 it should
    be clean. Verify no regression: logging in as ryan (Doctor) then mamad (Cashier) on the same
    tenant must not produce the StaffMembership skip anymore and must keep data integrity.
  - Validate: targeted VM/dialog test or manual reasoning; no full build/long test.
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
