# Plan — Server↔Client Integration Audit & Fixes

> **Purpose of this file.** A self-contained, pre-digested work order so a *fresh* AI session can
> implement without re-investigating the repo. Every task below already contains the distilled facts,
> exact file paths, line anchors, root-cause conclusions and the intended change. **Do not re-explore
> what is already stated here.** Open only the one or two files a task names.
>
> **LOCKED RULE — push after every task.** After completing *each* task below, run
> `scripts/git/push.sh "type(scope): message"` (branch `genspark_ai_developer`). If it exits `3`, call
> the `setup_github_environment` tool once, then re-run. **No push = project death.** If a heavy fix /
> large code change happens mid-task, push it immediately with a clear message.
> There is a GitHub Actions CI that runs build/test — do **not** run full/long local builds or tests.
>
> **Status legend:** ⬜ not started · 🔄 in progress · ✅ done.

---

## 0. Context the implementer must hold (read once, ~2 min)

**Product.** Animora = vendor-hosted multi-tenant SaaS vet-clinic desktop app. One C# solution, .NET 10,
Avalonia MVVM. Client is **offline-first** over local **encrypted SQLite**; the **central server**
(ASP.NET Core + PostgreSQL) is the authority. Clean layering: Domain ← Application ←
Infrastructure/Presentation (no EF/HTTP in VM/View; no client code in server project).

**Live server used for the trace in the task:** `http://188.212.99.234:5000` (Kestrel). It is REAL and
RUNNING; endpoints `/health/live`, `/sync/manifest`, `/auth/login`, `/auth/device/register`,
`/auth/keys`, `/auth/capabilities`, `/licensing/*`, `/auth/staff*`, `/sync/batches` all respond.

**The two real client↔server layers (keep them separate — they already are):**
- **Online plane** (`src/Animora.Client.Services/Identity/`): `OnlineAuthClient` → `/auth/*`,
  `CapabilityMirrorWriter` → `/auth/capabilities`+`/auth/keys`, `OnlineApi` (shared HTTP seam, bearer
  handler auto-attaches stored token when `bearer: null`). Talks to the real server.
- **Local plane** (`src/Animora.Application/UseCases/Identity/` + `src/Animora.Infrastructure.Local/`):
  `SignInCommand` etc. dispatch against the local SQLite store. This is what actually opens the shell
  session (`CommandContext.UserId`).

**Sign-in orchestration lives in** `src/Animora.Desktop/Pages/Login/LoginViewModel.cs`:
1. `EstablishServerSessionAsync` → `OnlineAuthClient.LoginAsync` (MANDATORY when server configured).
2. `SignInLocalAsync` → if `IAuthenticationService.IsFirstRunAsync()` **true** → `CompleteFirstRunAsync`
   (creates local owner); **else** → local `SignInAsync(user, pwd)`.

**`IsFirstRun` is computed as "no local User row exists"** —
`src/Animora.Infrastructure.Local/Security/LocalSessionService.cs:233`
(`isFirstRun: !await db.Set<User>().AnyAsync(ct)`). So it is `true` only until the very first local user
exists; afterwards always `false`.

**Server device lifecycle (BY DESIGN, do not "fix" the server to auto-approve):** a freshly registered
device is `Pending` (`/auth/device/register` → `{"status":"Pending"}`). A Manager approves it via
`POST /auth/device/{id}/approve`. Endpoints guarded by the `DeviceActive` coarse policy reject a
`Pending` device, **reusing the `DEVICE_REVOKED` error code** — see
`src/Animora.Server/Authorization/CoarsePolicies.cs` (`DeviceActiveHandler` fails with
`ErrorCodes.DeviceRevoked` when `DeviceActive != true`; the `DeviceActive` summary literally says *"a
Pending device is rejected"*). `/auth/capabilities` requires `DeviceActive` →
`src/Animora.Server/Auth/DeviceEndpoints.cs:27`. So `DEVICE_REVOKED` on `/auth/capabilities` for a brand
new device means **"device pending approval"**, NOT actually revoked.

---

## ROOT-CAUSE CONCLUSIONS (already derived — do not re-derive)

### RC-A — Issue #6: "I can log in with the owner but not with a user I created." (PRIMARY BLOCKER)
Trace facts: `/auth/login` for `user=naser` → **200 OK** (server accepts, role=Cashier).
`/auth/device/register` → **200 Pending**. `/auth/capabilities` → **403 DEVICE_REVOKED** (because the
device is Pending). Then: `local sign-in FAILED for 'naser' (code=CREDENTIALS_INVALID)`.

**Why:** `naser` was created on the **server** (`/auth/staff`). The **local SQLite store has no `User`
row for `naser`** (only the owner exists, created at first-run). In `LoginViewModel.SignInLocalAsync`,
because a local user already exists, `IsFirstRunAsync()` is `false`, so it calls local
`SignInAsync("naser", …)`. `SignInCommandHandler`
(`src/Animora.Application/UseCases/Identity/SignInCommand.cs`) does
`users.FindByUsernameAsync(context.TenantId, "naser")` → `null` → returns `CREDENTIALS_INVALID`.
The owner only works because first-run created *its* local row.

**Fix shape (client-side, offline-first-preserving):** After a **successful online login**, ensure a
local mirror user exists for the authenticated identity, then open the local session — regardless of
`IsFirstRun`. Concretely, add a **local-user provisioning step** that runs when online login succeeded
but local `SignInAsync` returns `CREDENTIALS_INVALID` (or proactively when no local user matches):
create/update the local `User` (+ active `StaffMembership` with the server's role mapped via
`RoleMapping`) + local password verifier from the just-verified credentials, scoped to the
server-returned `TenantId`, then retry local `SignInAsync`. The server login response (`LoginResponse`)
already carries `userId`, `tenantId`, `role`, `deviceId` (see `OnlineAuthClient.LoginAsync`), and the
plaintext password is in hand at that moment (used to write the password hash). Reuse the existing
first-run/registration path (`CompleteFirstRunSetupCommand` creates owner+Manager; you need a
**non-owner** variant that creates a user with the server role and does NOT require `IsFirstRun`).
Prefer adding a focused Application command (e.g. `ProvisionLocalMirrorUserCommand`) or extending the
registration use case, wired into `AuthenticationService` and called from `LoginViewModel`.
**Important:** the local tenant id must equal the server `TenantId` so `context.TenantId` matches at
local sign-in. Verify `ICurrentUserProvider.BuildAnonymousCommandContext()` / the local session
tenant-binding uses the server tenant (owner first-run currently invents a local tenant id — if the
server tenant id differs, local lookups will still miss; the mirror must align tenant ids, or the local
`SignIn` tenant scoping must resolve from the stored online session's `TenantId`). **Decide and document
the tenant-id alignment explicitly in the fix.**

### RC-B — Issue #6b: device-`Pending` UX + "no offline login" requirement
The user explicitly says: **no offline login; login is an online process.** Today
`OnlineAuthClient.LoginAsync` falls back to `OfflineSessionAuthorizer` on *transient/unreachable*
(`exchange.Response is null` or `IsTransient`). That is acceptable (it is unreachable-server, not a
feature toggle) BUT the login VM must **not** silently open a shell when the device is only `Pending`.
Current behavior: `MirrorAsync` fails (DEVICE_REVOKED), treated as "best effort", and the flow proceeds
to local sign-in. Two required outcomes:
1. When online login succeeds but the device is `Pending`/not approved (capabilities →
   `DEVICE_REVOKED`), surface a **clear Persian message** ("دستگاه شما در انتظار تأیید مدیر است") and
   **block** entering the shell (fail closed) — do NOT show `CREDENTIALS_INVALID`.
2. The owner's own device should auto-approve on first manager login if that is the server's intent —
   confirm via `src/Animora.Server/Auth/LoginService.cs:308` (`if (isManager && existing.Status ==
   DeviceStatus.Pending)` — manager devices are auto-approved). So **Managers never hit Pending**;
   only non-manager staff do, which matches the trace (naser=Cashier stayed Pending). The real-world
   operating model: a Manager must **approve the new staff member's device** from the staff/devices UI.
   Ensure such an approval path is reachable from the client (staff management already calls
   `/auth/staff*`; device approve/revoke endpoints exist at `/auth/device/{id}/approve|revoke` —
   check if the client exposes them; if not, note it as a sub-task but keep scope tight).

### RC-C — Issue #1 (server integrity): `/sync/batches` returns 500 UNEXPECTED on an **anonymous** call
Trace: at startup the client's connectivity check fires `POST /sync/batches {"epoch":1,"fromSequence":0}`
with **no Authorization header** → **500 Internal Server Error** (`code:"UNEXPECTED"`). The endpoint
(`src/Animora.Server/Sync/SyncEndpoints.cs:14`) is guarded by `TenantMember`+`DeviceActive` and should
answer **401/403** for an unauthenticated caller, never 500. A 500 means an unhandled exception fires
*before/around* auth (likely tenant context unbound in `BatchFreezer.OpenAsync`, or the request body is
bound before auth and the tenant filter throws). **Two-part conclusion:**
- **Client side:** the startup connectivity probe should NOT call `/sync/batches` unauthenticated. It
  already hits `/health/live` (200) and `/sync/manifest` (200, oddly also unauthenticated — manifest is
  behind the same policy yet returned 200, suggesting auth is not actually enforced pre-session; verify).
  Find where the client fires `POST /sync/batches` during connectivity/startup (search
  `sync/batches` / `OpenBatchRequest` / the sync scheduler/orchestrator in
  `src/Animora.Sync.Custom` + `src/Animora.Client.Services/Sync`) and ensure the first batch pull only
  runs **after** an authenticated, device-approved session exists.
- **Server side:** make `/sync/*` fail closed with a proper 401/403 (not 500) when
  unauthenticated/unbound-tenant. Harden `SyncEndpoints.Open` / `BatchFreezer` so an unbound tenant is a
  clean typed failure, not an unhandled exception. (Server is authored-but-unexecuted for S11–S13 — this
  is the first time it is exercised live, so a real bug is expected here.)

### RC-D — Issue #3: add user / limit & edit / delete per-user access (mostly DONE, verify + close gaps)
`src/Animora.Client.Services/Profile/UserProfileService.cs` is **server-authoritative** when a server is
configured: `AddStaffAsync`→`POST /auth/staff`, `SetStaffActiveAsync`→`POST /auth/staff/{id}/active`
(enable/disable), `SaveStaffAccessAsync`→`POST /auth/staff/access` (permission overrides),
`GetStaffAsync`→`GET /auth/staff`. Server side exists in
`src/Animora.Server/Auth/StaffProvisioningService.cs` + `StaffEndpoints.cs`
(`CreateAsync`/`SetActiveAsync`/`SaveAccess`/roster), gated by `UsersManage`/`RolesManage`.
**Gaps to verify/close:** (a) Is there a **delete/remove** staff path, or only enable/disable? The UI/
contract may only expose active-toggle — confirm whether "delete" is required vs. "disable" is the model
(disable rotates security stamp, kills tokens). (b) Role **edit** (change a user's role) — confirm the
server supports changing an existing membership's role; `StaffProvisioningService` shows create + status
+ permissions, not role-change. If missing and required, that is the real work item. (c) These staff
endpoints are reachable only by an **approved-device Manager** → cannot be end-to-end verified until
RC-A/RC-B are fixed. Order the work so staff CRUD is validated after login works.

### RC-E — Issue #2: history / provenance ("who did what") across users
`ActivityEvent` is a **local-only projection** (`src/Animora.Persistence.Sqlite/Entities/ActivityEvent.cs`
— fields `ActorUserId`, `ActorName`), rebuilt locally (`RebuildActivityFeedAsync`). Local actions get
their actor from the outbox `ActorUserId` (migration `020_OutboxActorProvenance`). The history contract
(`src/Animora.Contracts/History/HistoryModels.cs:127` `ActorRef Actor`) surfaces it.
**Integrity concern:** when records authored by *other users* arrive via sync, the activity feed must
show the correct actor (not the local device's user, not blank). Since Datasync (Track E / G) is
authored-but-unexecuted, the concrete check is: (1) does a synced record carry its originating
`ActorUserId`/name, and (2) does `RebuildActivityFeedAsync` populate `Actor` from that rather than from
the local session? Confirm the sync-applied records preserve actor provenance end-to-end; if the synced
wire record drops the actor, that is the fix.

### RC-F — Issue #5: feature parity / sync of operational data (appointments, reception, etc.)
"New data entered by one account must sync fast to all users of that clinic." Depends on the state-sync
pull (`/sync/manifest` + `/sync/batches` + `/sync/snapshots`, consumed by
`src/Animora.Sync.Custom` `HttpStateSyncEngine` + `SyncOrchestrator` + `SqliteSyncApplyStore`). Because
RC-C shows `/sync/batches` 500s on first real contact, **state sync has never actually completed a live
pull**. So: once RC-C is fixed and an authenticated+approved device can call `/sync/batches`, verify
one full manifest→batches→pages→apply cycle produces the other-user records locally, and that each
screen (Appointments, Reception, Visits, etc.) renders them. Keep verification targeted (one entity end
to end, e.g. an Appointment created by user A visible to user B), not a full regression sweep.

### RC-G — Issue #4: server technical standards / performance / code quality
Spot-check only, no broad sweep. Known anchors already noted in `backend-roadmap/PROGRESS.md`
"Post-C11 drift" (code debts: stray `.Result`, missing `CancellationToken`, pool sizing). For this plan,
the standard to enforce is: any server change in RC-C must (a) propagate `CancellationToken`, (b) return
typed `Result`/proper HTTP status (never let an exception become a 500 for an auth failure), (c) not add
EF/HTTP types across layer boundaries. Do a **focused** read of only the files you touch.

---

## TASK LIST

> Each task: do the change, then **push** (locked rule). Keep edits minimal and layer-correct.

### ✅ TASK-1 — Fix RC-A: local mirror-user provisioning so any server-authenticated user can sign in
Files: `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` (`SignInLocalAsync`),
`src/Animora.Client.Services/Identity/AuthenticationService.cs`,
`src/Animora.Application/UseCases/Identity/` (add a `ProvisionLocalMirrorUserCommand` or extend
`IdentityRegistration`; reuse `CompleteFirstRunSetupCommand` logic for the user+membership+hash write but
WITHOUT the `IsFirstRun`/owner-Manager assumption), `RoleMapping`
(`src/Animora.Client.Services/Identity/Mapping/`).
Do:
1. Add Application command that, given (tenantId, username, plaintext password, server role), creates or
   updates a local `User` (+ active `StaffMembership` of that role) scoped to the given tenant, writing
   the password hash from the plaintext. Idempotent (no-op/update if the user already exists).
2. Expose it via `IAuthenticationService` (new method, e.g. `EnsureLocalMirrorAsync(serverUserId,
   tenantId, username, password, role)` → `AuthOutcome`).
3. In `LoginViewModel.SignInLocalAsync`: after a successful **online** login, if local `SignInAsync`
   fails with `CREDENTIALS_INVALID`, call the mirror step using the online `LoginResponse`
   (`TenantId`/`UserId`/`Role`) + the entered password, then retry local `SignInAsync`.
4. **Tenant-id alignment:** ensure local sign-in's `context.TenantId` equals the server `TenantId`
   (RC-A note). If the owner first-run currently invents a different local tenant id, make the mirror use
   the server tenant id, and make the local session resolve tenant from the stored online session.
Acceptance (reason it, do not run full test): a server-created non-owner user, on a device that is
**already approved**, reaches the shell; a wrong password still returns `CREDENTIALS_INVALID`.
Push: `fix(auth): provision local mirror user after online login so server-created users can sign in`.

### ✅ TASK-2 — Fix RC-B: surface device-Pending, block shell, remove misleading error; honor "no offline login as a feature"
Files: `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` (`EstablishServerSessionAsync`,
`MapError`), `src/Animora.Client.Services/Identity/OnlineAuthClient.cs` (how `MirrorAsync`/capabilities
failure is interpreted).
Do:
1. Treat an online-login success whose capability mirror returns `DEVICE_REVOKED` **for a never-approved
   device** as a distinct state "pending approval" (not a login success, not CREDENTIALS_INVALID). Add a
   Persian message: `"دستگاه شما ثبت شد و در انتظار تأیید مدیر کلینیک است."` and keep the user on the
   login screen (fail closed, no shell).
   - Distinguish "pending" from genuinely "revoked": a device just registered this session is pending.
     Simplest reliable signal available to the client is the `/auth/device/register` response status
     (`Pending` vs `Approved`) — `DeviceLifecycleService.RegisterAsync` already sees it; thread that state
     up so the VM can tell pending from revoked.
2. Keep the unreachable-server offline fallback (that is resilience, not the forbidden "offline login
   feature"), but ensure it only authorizes a **previously-approved** mirrored session.
Acceptance: naser-style Cashier first login shows the pending message, not CREDENTIALS_INVALID, and does
not enter the shell. Manager (auto-approved per `LoginService.cs:308`) enters normally.
Push: `fix(auth): show device-pending state and block shell instead of credential error`.

### ✅ TASK-3 — Client approval path for pending staff devices (close RC-B operating loop)
Files: search client for `/auth/device` usage; `src/Animora.Server/Auth/DeviceEndpoints.cs` (reference
only — already has `/auth/device/{id}/approve|revoke`), staff UI/service
(`UserProfileService` + staff page VM).
Do: ensure a Manager can **approve/revoke a staff member's device** from the client (list pending
devices + approve). If a client surface already exists, just verify + wire error mapping; if not, add a
minimal service method (`OnlineApi.SendAsync(POST, "auth/device/{id}/approve", …)`) and a staff-screen
action. Keep it minimal and server-authoritative. If this is larger than a small change, implement the
service method + a single button action only.
Push: `feat(staff): manager can approve/revoke staff device from client`.

### ✅ TASK-4 — Fix RC-C (server): `/sync/*` must fail closed (401/403), never 500, when unauthenticated/unbound-tenant
Files: `src/Animora.Server/Sync/SyncEndpoints.cs`, `BatchFreezer` + `FeedContextFactory`/tenant binding
in `src/Animora.Persistence.PostgreSql/Replication/`, `src/Animora.Server/Hosting/` middleware order.
Do: reproduce by reasoning — an anonymous `POST /sync/batches` currently throws (tenant unbound) →
GlobalExceptionMiddleware → 500 UNEXPECTED. Ensure the authorization policy (`TenantMember` +
`DeviceActive`) is evaluated **before** any tenant-scoped DB access, so the response is 401/403. If the
body is model-bound before auth, guard `BatchFreezer.OpenAsync` against an unbound tenant with a typed
failure. Propagate `CancellationToken` (RC-G). Do NOT loosen the policy.
Also confirm why `/sync/manifest` returned **200** unauthenticated (same policy) — if auth is not
actually enforced on the sync group, that is the deeper bug: enforce it.
Push: `fix(server): sync endpoints fail closed with 401/403 instead of 500 when unauthenticated`.
> **Done:** added `SyncTenantBoundFilter` (typed 401 `SESSION_EXPIRED` if no tenant bound, runs before contract filter/body work). Live re-probe: anonymous `/sync/manifest` and `/sync/batches` both return 401 (auth is enforced on the group; the earlier 200 was not reproducible).

### ✅ TASK-5 — Fix RC-C (client): do not pull `/sync/batches` before an authenticated, approved session
Files: `src/Animora.Client.Services/Sync/*`, `src/Animora.Sync.Custom/*` (SyncOrchestrator /
SyncSchedulerHostedService / HttpStateSyncEngine), login connectivity probe in `LoginViewModel`
(`CheckConnectivityAsync` / `_connectivity`).
Do: the startup connectivity/first-pull path must gate the first `/sync/batches` on a valid bearer +
approved device. The pre-login probe should stay limited to `/health/live` (and optionally
`/sync/manifest` only if the server legitimately allows it). Ensure the sync scheduler is dormant until a
real session exists.
Push: `fix(sync): defer first batch pull until authenticated approved session exists`.
> **Done:** `SyncOrchestrator` takes an optional `ISessionStore` and returns `Deferred` (zero requests) until a stored online session exists (a Pending device never keeps one — TASK-2). Scheduler re-checks every `DeferredRetryInterval` (10 s) while deferred so the first pull follows sign-in promptly. Pre-login probe is unchanged (`/health/live` only). Tests added in `SyncOrchestratorTests`.

### ✅ TASK-6 — Verify RC-F: one end-to-end sync cycle (fast cross-user propagation)
Prereq: TASK-1/2/4/5 done. Files: `src/Animora.Sync.Custom` apply path (`SqliteSyncApplyStore`),
`SyncOrchestrator`, one feature read query (e.g. Appointments).
Do: by reading the apply/orchestrator code, confirm a full `manifest → batches → pages → apply →
SyncCursor advance` cycle writes other-users' records into local SQLite and that an Appointments-type
read query surfaces them. Confirm the scheduler re-pulls promptly (jitter/backoff but short when
healthy) so new data appears quickly for all clinic users. Fix any concrete break found; keep scope to
one entity verified end to end. (No full regression run — reason from code + the live endpoints.)
Push (only if code changed): `fix(sync): <specific fix> for cross-user data propagation`.
> **Done — concrete break found & fixed:** `SqliteSyncApplyStore` only wrote the generic `repl_base` replica; nothing projected it onto the typed tables (`Owners/Pets/Appointments/ReceptionQueueEntries`) that every screen queries, so another user's records never appeared. Added `ReplicaMaterializer` (called inside the same commit transaction): column-mapped upsert from wire payload (server `UpdatedAt/Version/Deleted` preserved via raw SQL, search keys recomputed through the shared `*SearchKeyProvider.Compose`), tombstone → soft-delete, snapshots never delete local rows, unsettled local commands on the same aggregate win, malformed rows skipped. Scheduler base interval lowered 5 min → 30 s (healthy pull = 2 cheap requests) so cross-user changes show quickly. Tests: `ReplicaMaterializerTests`. Remaining entities of the server catalog (Visit, Product, …) follow the same map pattern — add a row to `Maps` when their screens are synced.

### ✅ TASK-7 — Verify/close RC-D: staff add / role-edit / enable-disable / access-limit end to end
Prereq: TASK-1/2 (so a Manager can actually log in and operate). Files:
`src/Animora.Client.Services/Profile/UserProfileService.cs`,
`src/Animora.Server/Auth/StaffProvisioningService.cs` + `StaffEndpoints.cs`,
staff contracts (`src/Animora.Contracts/Profile/`).
Do: confirm create / enable-disable / permission-override / roster all round-trip to the server and
reflect for every device. **Check for the two possible gaps:** (a) changing an existing user's **role**
(server `StaffProvisioningService` shows create+status+permissions, not role-change — add if required by
the contract/UI), (b) **delete vs disable** (confirm the product model; if only disable is intended,
leave it; if delete is required and missing, add server endpoint + client call). Keep edits minimal and
driven by what the contract/UI already demands — no speculative features.
Push (if code changed): `feat(staff): <specific gap closed>`.
> **Done — verified, no code change needed (decisions recorded):** create (`POST /auth/staff`), enable/disable (`/auth/staff/{id}/active`, rotates the security stamp → tokens die, Manager/self protected), permission overrides (`/auth/staff/access`, rotates stamp) and roster (`GET /auth/staff`, now also device roster) all round-trip server-side through `UserProfileService`; the server is the single authority and every device sees it on its next roster read / sign-in. (a) **Role change:** neither `IUserProfileService` nor the Profile UI exposes it (UI only toggles status and edits per-permission overrides, which is the access-limit model); the server role is already re-mirrored locally at next sign-in (`ProvisionLocalMirrorUserCommand` handles `ChangeRole` + stamp rotation) so a future server-side role-change endpoint needs no client rework. Not added (speculative). (b) **Delete vs disable:** the product model is disable-only ("سوابق کاربر محفوظ است" — historical references must never be deleted); the UI's remove action is that toggle. Left as is.

### ⬜ TASK-8 — Verify RC-E: actor provenance preserved across synced history
Files: `src/Animora.Persistence.Sqlite/Entities/ActivityEvent.cs`,
`src/Animora.Persistence.Sqlite/Activity/ActivityFeedWriter.cs` + `RebuildActivityFeedAsync`,
the sync apply path + the synced wire contracts
(`src/Animora.Contracts.Wire/Sync/Contracts/*`).
Do: confirm a record authored by user A and synced to user B's device shows **A** as the Actor in
history (not B, not blank). Trace: does the synced wire record carry the originating actor id/name, and
does the local activity rebuild map it to `Actor`? If the actor is lost on apply, fix the mapping so
provenance survives sync. Targeted, not a sweep.
Push (if code changed): `fix(history): preserve actor provenance for synced records`.

### ⬜ TASK-9 — RC-G focused standards pass on touched server/client files
Do: review ONLY files changed in TASK-1..8 for: `CancellationToken` propagation, no `.Result`/`.Wait()`,
typed `Result`/correct HTTP status (no exception-as-500 on expected failures), no EF/HTTP types crossing
into VM/View/Domain, `ConfigureAwait` per CONVENTIONS (client code returning to VM intentionally omits
`ConfigureAwait(false)`; infra/server uses it). Fix nits found. No new broad refactors.
Push: `refactor: tighten cancellation/result handling on touched integration paths`.

### ⬜ TASK-10 (FINAL) — delete this plan file
Delete `Plan/server-client-integration-plan.md` (this file) ONLY. Do not touch other files or any
roadmap file. Then push: `chore(plan): remove completed server-client-integration plan`.

---

## Quick-reference facts (so no re-grep is needed)
- Login VM: `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` — `SignInAsync` (~205),
  `SignInLocalAsync` (~250), `EstablishServerSessionAsync` (~274), `MapError` (~325).
- Local sign-in handler: `src/Animora.Application/UseCases/Identity/SignInCommand.cs`
  (`FindByUsernameAsync(context.TenantId, …)` → null → `CREDENTIALS_INVALID`).
- `IsFirstRun` = no local User: `src/Animora.Infrastructure.Local/Security/LocalSessionService.cs:233`.
- Online login: `src/Animora.Client.Services/Identity/OnlineAuthClient.cs` (`LoginAsync`), persists
  session first, registers device (`Pending` for non-managers), best-effort capability mirror.
- Capability mirror (fails on Pending device): `.../Identity/CapabilityMirrorWriter.cs`.
- Device coarse policy (`Pending → DEVICE_REVOKED`): `src/Animora.Server/Authorization/CoarsePolicies.cs`.
- Manager device auto-approve: `src/Animora.Server/Auth/LoginService.cs:308`.
- Capabilities needs DeviceActive: `src/Animora.Server/Auth/DeviceEndpoints.cs:27`.
- Sync endpoints (500-on-anon bug): `src/Animora.Server/Sync/SyncEndpoints.cs` (group guarded by
  `TenantMember`+`DeviceActive`, `/batches` POST at line ~22).
- Staff (server-authoritative, already wired): `src/Animora.Client.Services/Profile/UserProfileService.cs`
  (`auth/staff`, `auth/staff/{id}/active`, `auth/staff/access`),
  server `src/Animora.Server/Auth/StaffProvisioningService.cs` + `StaffEndpoints.cs`.
- Activity/history actor (local projection): `src/Animora.Persistence.Sqlite/Entities/ActivityEvent.cs`
  (`ActorUserId`,`ActorName`); contract `src/Animora.Contracts/History/HistoryModels.cs:127` (`Actor`).
- Shared HTTP seam: `src/Animora.Client.Services/Identity/OnlineApi.cs` (`bearer: null` ⇒ handler attaches
  stored token). Server base for the trace: `http://188.212.99.234:5000`.
- Push script (LOCKED): `scripts/git/push.sh "msg"`; branch `genspark_ai_developer`; exit 3 ⇒ call
  `setup_github_environment` then re-run. CI (GitHub Actions) does build/test — don't run long local builds.
