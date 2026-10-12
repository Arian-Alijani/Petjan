# Plan — Staff/User creation must persist to the central server (online)

> **Self-contained execution plan. Read this file fully; it already contains every fact, file path,
> line reference and decision you need. Do NOT re-explore the repo to re-derive the diagnosis — it is
> written below. Only open a file when a task step says "edit <file>".**
>
> **🔒 PUSH RULE (locked, mandatory):** after completing EACH task below, run
> `scripts/git/push.sh "type(scope): message"` immediately. If a single task produced a large/heavy
> change mid-way, push that too. No push = project death. If push exits code `3`, call the
> `setup_github_environment` tool once, then re-run the same push command.
>
> Work on branch **`genspark_ai_developer`** (first push: `scripts/git/push.sh -b genspark_ai_developer "msg"`).

---

## 0. The problem (verbatim intent)

On the Windows client, **Settings → add a new clinic user** (the Profile page "staff" section). The newly
created user is **written only to the local SQLite database on that one machine** — it is **never sent to
the central server**, so:
- other users / other devices never see the account,
- the new user can never log in (login is online and checks the server's user table).

**Required behaviour (user's chosen approach — the SIMPLE one, confirmed):**
When an admin creates a user, the client calls the **server directly, synchronously, online** (same as login,
which is already online). The **server** creates the `User` + `StaffMembership` (+ phone preference) and is the
authority. This is simpler and needs fewer changes than the offline-journal-then-sync route. Account creation
**requires internet** (acceptable — the whole login/identity system is already online-only).

---

## 1. Diagnosis — WHY it fails today (already investigated; do not re-investigate)

### 1.1 Client path today (local-only)
- UI: `src/Animora.Desktop/Pages/Profile/ProfilePageViewModel.cs:548` builds a `CreateStaffRequest` and calls
  `IUserProfileService.AddStaffAsync`.
- Service: `src/Animora.Client.Services/Profile/UserProfileService.cs`
  - `AddStaffAsync` (line ~107) → `SendStaffAsync(new CreateStaffCommand(...))`.
  - `SendStaffAsync` (line ~121) resolves the **local** `IDispatcher` from a DI scope and calls
    `dispatcher.SendAsync(command, ...)` — i.e. it runs the command against the **local SQLite store only**.
  - Same for `SetStaffActiveAsync` / `SaveStaffAccessAsync`.
- Handler: `src/Animora.Application/UseCases/Profile/StaffManagement.cs`
  - `CreateStaffCommandHandler` creates `User.Create(...)`, `user.SetPasswordHash(hasher.Hash(pw))`,
    `users.Add(user)`, `memberships.Add(StaffMembership.Create(...))`, upserts phone preference. **All local.**

### 1.2 The reason it never reaches the server (ROOT CAUSE)
Only commands that implement the marker interface `IJournaledCommand` are journaled → pushed to the server by
the command transport. Confirmed facts:
- `CreateStaffCommand`, `SetStaffActiveCommand`, `SaveStaffAccessCommand` in `StaffManagement.cs` do **NOT**
  implement `IJournaledCommand` (verified by grep — zero hits).
- `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` has **no** `identity.*` / `user.*` / `staff.*`
  command type (verified — the catalog ends at `vaccination.*`; no identity family exists).
- `IJournaledCommand` doc comment (`src/Animora.Application/Commands/IJournaledCommand.cs`) explicitly states:
  *"A command that does not implement this marker is a purely local concern (UI preferences, profile settings)
  and never touches the journal."*

➡️ **Staff creation was deliberately modelled as a purely-local command and has NO server leg at all.**
There is also no server-side handler or endpoint that creates a user. So nothing is wrong with "sync" — the
feature simply has no server path. The user's chosen fix is to add a **direct online call**, not to make the
command journaled.

### 1.3 What DOES already exist and will be reused (do NOT rebuild these)
Server (`src/Animora.Server`):
- Online auth is live: `Auth/AuthEndpoints.cs` maps `POST /auth/{login,refresh,logout,device/register}` under
  `MapGroup(ServerRoutes.Auth)` + `RequireRateLimiting(RateLimitPolicies.Auth)`.
- Login proves the server holds the real user/membership/role/device model:
  `Auth/LoginService.cs` (tenant resolve → bind → tenant-filtered user lookup → password verify → membership).
- **Permission-gated authenticated endpoints already exist** — exact pattern to copy:
  `Auth/DeviceEndpoints.cs:25` `group.MapPost("/device/{id}/approve", ...).RequireAuthorization(PermissionCodes.DevicesManage);`
- Authenticated caller claims: `Auth/AuthenticatedCaller.cs` → `AuthenticatedCaller.TryRead(http.User)` returns
  `(UserId, TenantId, DeviceId, SecurityStamp)`.
- Server identity store: `src/Animora.Persistence.PostgreSql/Identity/EfIdentityRepository.cs`
  (`FindUserByNameAsync`, `FindUserByIdAsync`, `AddDevice`, `SaveAsync`) — **note: it has `AddDevice` but NO
  `AddUser`/`AddMembership` yet** → a small addition is needed (Task 3).
- Server tenant-scoped repos (read-only today): `Authorization/ServerStaffMembershipRepository.cs` (its
  `Add(...)` throws `NotSupportedException`), `Authorization/ServerRoleDefinitionRepository.cs`.
- Server password hasher: `IPasswordHasher<ServerIdentityUser>` (used by `LoginService` via the
  `Microsoft.AspNetCore.Identity` hasher — reuse the SAME hasher so the hash is login-compatible).
- Server clock: `Animora.Domain.Time.IClock` is available in DI (used by `LoginService`).
- Routes: `Hosting/ServerRoutes.cs` → `Auth = "/auth"`. Pipeline order in `Program.cs:159-162`:
  `UseAuthentication()` → `TenantContextMiddleware` → `UseAuthorization()`, then endpoints mapped at
  `Program.cs:180 MapAuthEndpoints()`, `190 MapDeviceEndpoints()`, `193 MapCommandEndpoints()`.
- Permission names: `Animora.Domain.Identity.PermissionCodes.UsersManage` / `.RolesManage` (used by the local
  `StaffManagement` handlers today).
- Server-side permission policy auto-builds: `Authorization/ServerPermissionPolicyProvider.cs` makes
  `RequireAuthorization(PermissionCodes.UsersManage)` "just work".

Client (`src/Animora.Client.Services`):
- The online HTTP seam to reuse verbatim: `Identity/OnlineApi.cs` →
  `api.SendAsync(HttpMethod, path, body, bearer:null, ct)` sends JSON over the shared named `HttpClient`
  (`SyncFeedClient.HttpClientName`) with the wire-version header; the `BearerAuthHandler` auto-attaches the
  stored bearer token; network faults return `Exchange.Response == null` (never throw).
  `OnlineApi.ReadAsync<T>` / `ReadErrorCodeAsync` read the body.
- **Perfect template already in the repo:** `Profile/ServerClinicAccountStatusSource.cs` — an authenticated
  server call via `OnlineApi` registered only when the server is configured.
- Online identity DI: `Identity/OnlineIdentityServiceCollectionExtensions.cs` →
  `AddAnimoraOnlineIdentity()` wires `OnlineApi` and the online clients **only when `ServerEndpointOptions`
  is configured** (`options.IsConfigured`). Called from `App.axaml.cs:426`.
- `ServerEndpointOptions` carries `BaseAddress`, `TenantCode`, `RequestTimeout` (set in `App.axaml.cs`
  `ConfigureServerEndpoint`, default server `http://188.212.99.234:5000`, tenant from `ANIMORA_TENANT_CODE`).

Domain (`src/Animora.Domain` — referenced by BOTH client and server):
- `Identity/User.cs:79` `User.Create(tenantId, userName, displayName, IClock)`;
  `:166` `SetPasswordHash(hash)`; `UserStatus`, `Enable()/Disable()`.
- `Identity/StaffMembership.cs:78` `StaffMembership.Create(tenantId, userId, StaffRole, IClock)`.
- `Identity/StaffRole` enum; `PermissionCodes`, `PasswordPolicy.Validate(password)`.

Wire DTOs (`src/Animora.Contracts.Wire/Auth/`): existing records are the shape/style to copy
(e.g. `LoginRequest`, `DeviceRegisterRequest`, `DeviceRegisterResponse`).

---

## 2. Design decision (the chosen solution — locked)

**Direct online staff-management endpoints on the existing `/auth` surface, called synchronously by the
client.** No journaling, no `CommandTypes` entry, no Datasync change.

**Server adds** (minimal, mirrors the local `StaffManagement` validation rules so behaviour matches):
- `POST /auth/staff`            → create user+membership(+phone). Gated `RequireAuthorization(PermissionCodes.UsersManage)`.
- `POST /auth/staff/{id}/active`→ enable/disable a membership. Gated `UsersManage`.
- `POST /auth/staff/access`     → set a membership's permission overrides. Gated `RolesManage`.
- `GET  /auth/staff`            → list staff (so the Profile page reads the **server** roster, not the local one). Gated `UsersManage` (or authenticated + handler gate).

Tenant is taken from the **authenticated caller's `TenantId` claim** (never from the request body) — the admin
can only create users in their own clinic. New user's password is hashed with the **server's**
`IPasswordHasher<ServerIdentityUser>` so the user can immediately log in via `/auth/login`.

**Client changes** `UserProfileService` so `AddStaffAsync` / `SetStaffActiveAsync` / `SaveStaffAccessAsync` /
`GetStaffAsync` call the server through `OnlineApi` **when the server is configured**; keep the local
dispatcher path ONLY as the offline/local-only-build fallback (server not configured). Because identity is
online-only, when the server IS configured the server call is authoritative and its failure is surfaced to the
user (no silent local-only success).

**Scope guard:** touch ONLY the staff/user path. Do NOT change invoice/finance/sync/login logic. Keep the
local `StaffManagement.cs` handlers intact (used for the offline/local-only build and by server internally if
desired) — do not delete them.

---

## 3. Tasks (do in order; push after EACH)

### ☐ TASK 1 — Wire DTOs for staff management
**File:** create `src/Animora.Contracts.Wire/Auth/StaffWire.cs` (one file, several records).
Add records (match existing `Auth/*.cs` doc-comment style, `Animora.Contracts.Wire.Auth` namespace):
- `CreateStaffServerRequest(string Name, string Username, string Password, string Role, string Phone)`
  (`ToString()` must mask the password like `LoginRequest`/`CreateStaffRequest` do).
- `StaffActiveServerRequest(string MembershipId, bool Active, string? ExpectedChangeToken)`.
- `StaffAccessServerRequest(string MembershipId, IReadOnlyList<string> PermissionCodes, string? ExpectedChangeToken)`.
- `StaffAccountWire(string MembershipId, string UserId, string Name, string Username, string Role, bool IsActive, bool IsProtected, IReadOnlyList<string> Permissions, string ChangeToken)`.
- `StaffRoleOptionWire(string RoleId, IReadOnlyList<string> DefaultPermissions)`.
- `StaffDirectoryWire(IReadOnlyList<StaffAccountWire> Members, IReadOnlyList<StaffRoleOptionWire> Roles, bool CanManageUsers, bool CanManageAccess)`.
  (Permission *labels* / risk text stay client-side in `ProfileMapping`; the wire carries only codes.)
**Why:** typed transport for the 4 endpoints. **Acceptance:** project compiles (built in a later task; no
build needed just for this file). **Push:** `scripts/git/push.sh "feat(wire): staff-management DTOs for online user provisioning"`

### ☐ TASK 2 — Server identity store: allow adding users & memberships
**File:** `src/Animora.Persistence.PostgreSql/Identity/IIdentityRepository.cs` + `EfIdentityRepository.cs`.
- Add to the interface: `void AddUser(User user);` and `void AddMembership(StaffMembership membership);` and
  (for status/access edits) `Task<StaffMembership?> FindMembershipByIdAsync(string membershipId, CancellationToken ct = default);`
  and a list read `Task<IReadOnlyList<(User User, StaffMembership Membership)>> ListStaffAsync(string tenantId, CancellationToken ct = default);`
- Implement in `EfIdentityRepository`:
  - `AddUser` → `context.Users.Add(user);`  `AddMembership` → `context.StaffMemberships.Add(membership);`
  - `FindMembershipByIdAsync` → `context.StaffMemberships.FirstOrDefaultAsync(m => m.Id == membershipId, ct)`
  - `ListStaffAsync` → join `Users`×`StaffMemberships` on `UserId`, tenant-filtered by `tenantId`, ordered by
    `JoinedUtc`; return list of tuples. (Reads are already tenant-filtered by the request-bound tenant context,
    but filter explicitly by `tenantId` too for safety.)
- `SaveAsync` already maps unique-violation `23505` → `ConcurrencyConflict`; reuse it. Username uniqueness is
  enforced both by a pre-check (Task 3) and this DB constraint.
**Why:** server currently can only read identity + add devices. **Acceptance:** compiles. **Push:**
`scripts/git/push.sh "feat(server-identity): add user/membership write + staff list to EfIdentityRepository"`

### ☐ TASK 3 — Server staff-management endpoints
**File:** create `src/Animora.Server/Auth/StaffEndpoints.cs` (mirror `DeviceEndpoints.cs` structure exactly).
- `public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)`:
  - `var group = app.MapGroup(ServerRoutes.Auth).RequireRateLimiting(RateLimitPolicies.Auth);`
  - `group.MapGet("/staff", ListAsync).RequireAuthorization(PermissionCodes.UsersManage);`
  - `group.MapPost("/staff", CreateAsync).RequireAuthorization(PermissionCodes.UsersManage);`
  - `group.MapPost("/staff/{id}/active", SetActiveAsync).RequireAuthorization(PermissionCodes.UsersManage);`
  - `group.MapPost("/staff/access", SaveAccessAsync).RequireAuthorization(PermissionCodes.RolesManage);`
- Create a small internal service `StaffProvisioningService` (same folder) injected into the handlers, holding
  the business logic so the endpoint file stays thin (follow `LoginService` style). It needs:
  `IIdentityRepository repo`, `IRoleDefinitionRepository roles` (= `ServerRoleDefinitionRepository`),
  `IPasswordHasher<ServerIdentityUser> hasher`, `IClock clock`, and the request tenant context
  (`RequestTenantContext` / `IRequestAuthorizationState` — see how `MembershipResolver` / existing authz code
  obtains `TenantId`; the caller's tenant also comes from `AuthenticatedCaller.TryRead(http.User).TenantId`).
- **Create logic** (port validation from `CreateStaffCommandHandler` verbatim so behaviour matches):
  1. read caller `AuthenticatedCaller.TryRead(http.User)`; `tenantId = caller.TenantId`.
  2. validate lengths (`Name ≤200`, `Username ≤128`, `Password ≤256`, `Phone ≤30`) + `Enum.TryParse<StaffRole>`
     → else `ValidationFailed` (400).
  3. `PasswordPolicy.Validate(password)` → else that error.
  4. username-exists check: `repo.FindUserByNameAsync(Normalize(username))` within tenant → `UsernameExists`.
  5. role must exist: `roles.FindByRoleAsync(tenantId, role, ct)` not null → else `PermissionDenied`.
  6. creating a `Manager` requires the caller to have `RolesManage` (port the same guard; the caller's
     membership/role is available via the server authz state / `MembershipResolver`).
  7. `var user = User.Create(tenantId, username, name, clock); user.SetPasswordHash(hasher.HashPassword(new ServerIdentityUser(...), password));`
     — use the SAME hashing call shape `LoginService` uses to VERIFY (check `LoginService.VerifyPassword` /
     `BurnPasswordHash` to mirror the exact `ServerIdentityUser` construction), so the stored hash verifies at
     login. `repo.AddUser(user); repo.AddMembership(StaffMembership.Create(tenantId, user.Id, role, clock));`
     then phone: store via the server's profile/preference writer **if one exists**; if the server has no
     preference store, OMIT phone server-side for now and note it (phone is a non-critical profile pref — do
     NOT invent a new table in this plan). `await repo.SaveAsync(ct)`.
  8. return `204 No Content` on success; `Fail(http, error)` (reuse `AuthEndpoints.Fail` / `StatusFor`, or a
     local equivalent) with the mapped status on failure. `UsernameExists`/`ConcurrencyConflict` → 409.
- **SetActive logic:** find membership by id, tenant-match, forbid `Manager`/self, optional change-token check
  (port from `SetStaffActiveCommandHandler`), toggle `member.Reinstate()+user.Enable()` /
  `member.Suspend()+user.Disable()`, `SaveAsync`.
- **SaveAccess logic:** port from `SaveStaffAccessCommandHandler` (validate codes against `PermissionCodes.All`,
  forbid `Manager`/self, apply `GrantExtra`/`Deny`/`ClearOverride`, `user.RotateSecurityStamp()`, `SaveAsync`).
  Capability-snapshot re-issue is a client-offline concern; server just persists the overrides.
- **List logic:** `repo.ListStaffAsync(tenantId)` → map each to `StaffAccountWire` (compute granted permissions
  = role defaults ∪ extras − denied, same as the local `StaffDirectoryQueryHandler`), compute `ChangeToken`
  (port `StaffChangeToken.For` — it's `internal` in Application; re-implement the same SHA-256-over-JSON here or
  expose it). Return `StaffDirectoryWire`.
- **Register the service** in the server DI (where `LoginService` is registered — `Auth/AuthServiceCollectionExtensions.cs`).
- **Map it** in `src/Animora.Server/Program.cs` next to `app.MapDeviceEndpoints();` (around line 190):
  add `app.MapStaffEndpoints();`.
**Acceptance:** `dotnet build src/Animora.Server` compiles (do a scoped build of the server project only — NOT
a full-solution long build). **Push (heavy task):**
`scripts/git/push.sh "feat(server): online staff-management endpoints under /auth/staff"`

### ☐ TASK 4 — Client: call the server for staff operations
**File:** `src/Animora.Client.Services/Profile/UserProfileService.cs`.
- Inject `OnlineApi? api` and `ServerEndpointOptions? options` as OPTIONAL ctor deps (they are registered only
  when the server is configured — follow how `ServerClinicAccountStatusSource` / other online services take
  `OnlineApi`). Add a private `bool ServerConfigured => api is not null && options?.IsConfigured == true;`
  — but note `UserProfileService` is currently a plain singleton; verify its ctor and DI registration
  (`App.axaml.cs:414`). If optional injection is awkward, resolve `OnlineApi` from the DI scope inside the
  method via `scope.ServiceProvider.GetService<OnlineApi>()` (returns null when not configured) — this matches
  the existing `GetService<IClinicAccountStatusSource>()` pattern already in this file (`GetClinicAccountStatusAsync`).
- `AddStaffAsync`: when server configured →
  `var ex = await api.SendAsync(HttpMethod.Post, "auth/staff", new CreateStaffServerRequest(name, username, password, roleId, phone), bearer:null, ct);`
  then: `response == null` → throw `ServiceException` mapped to a network error code
  (`NETWORK_UNAVAILABLE`); non-success → read `OnlineApi.ReadErrorCodeAsync` and throw `ServiceException(that code)`
  (so the UI shows the right Persian message — the UI already maps codes like `USERNAME_EXISTS`,
  `VALIDATION_FAILED`, etc. Confirm/extend the UI error map in `ProfilePageViewModel`/`ProfileMapping`).
  Success (204) → return. **When server NOT configured** → keep the existing local-dispatcher path (offline/
  local-only build).
- `SetStaffActiveAsync` → `POST auth/staff/{membershipId}/active` with `StaffActiveServerRequest`.
- `SaveStaffAccessAsync` → `POST auth/staff/access` with `StaffAccessServerRequest`.
- `GetStaffAsync` → when server configured, `GET auth/staff`, read `StaffDirectoryWire`, map to the existing
  `StaffDirectory` contract (reuse `ProfileMapping.RoleLabel` / `PermissionLabel` / risk text for labels — the
  wire only carries codes). When not configured → keep the existing local query path.
- Keep the role-parse guard currently in `AddStaffAsync`.
**Acceptance:** `dotnet build src/Animora.Client.Services` compiles (scoped build, not full solution). **Push:**
`scripts/git/push.sh "feat(client): route staff management to server when online-configured"`

### ☐ TASK 5 — Error-code & UX coverage
**Files:** `src/Animora.Desktop/Pages/Profile/ProfilePageViewModel.cs` (and/or `ProfileMapping`).
- Ensure the catch/handler around `AddStaffAsync`/`SetStaffActiveAsync`/`SaveStaffAccessAsync` maps the new
  server error codes to Persian text the user understands: `USERNAME_EXISTS` ("این نام کاربری قبلاً ثبت شده
  است"), `NETWORK_UNAVAILABLE` ("اتصال به سرور برقرار نشد؛ برای ساخت کاربر به اینترنت نیاز است"),
  `PERMISSION_DENIED`, `VALIDATION_FAILED`, `PASSWORD_POLICY_VIOLATION`, `CONCURRENCY_CONFLICT`,
  `SESSION_EXPIRED`. Reuse `LoginViewModel.MapError` wording where it already exists (don't duplicate
  inconsistently).
- Verify `ServiceException` carries the stable code and the VM surfaces it (grep how `AddStaffAsync` is awaited
  in the VM — it's around `ProfilePageViewModel.cs:548`).
**Acceptance:** compiles; no behavioural guesswork left. **Push:**
`scripts/git/push.sh "feat(profile): surface server staff errors with Persian UX"`

### ☐ TASK 6 — Scoped build + sanity verification (NO full/long build)
- Build only the touched projects (fast, scoped):
  `cd /home/user/webapp && dotnet build src/Animora.Contracts.Wire src/Animora.Persistence.PostgreSql src/Animora.Server src/Animora.Client.Services src/Animora.Desktop -clp:ErrorsOnly`
  — expect 0 errors. (Do NOT run `scripts/sandbox/build.sh full`, do NOT run the full test suite — that's
  the long-build prohibition.)
- Quick self-review checklist (reason through, don't launch servers):
  - [ ] New user's password hash is produced with the SAME `ServerIdentityUser`+hasher shape that
        `LoginService.VerifyPassword` consumes → login will succeed. (Re-read `LoginService` hash/verify.)
  - [ ] Tenant for the new user = caller's `TenantId` claim (never from body).
  - [ ] Endpoints gated: create/active = `UsersManage`, access = `RolesManage`.
  - [ ] Client uses server path when configured, local path only when not configured.
  - [ ] `MapStaffEndpoints()` is actually called in `Program.cs`, service registered in DI.
- If the build surfaced a heavy fix, push it with a clear message.
**Push:** `scripts/git/push.sh "chore(staff): scoped build green + integration sanity pass"`

### ☐ TASK 7 — Answer the user's explicit questions in the final chat summary
In the final reply (NOT in a repo file), state clearly:
1. **Root cause** (staff creation was a purely-local command with no server leg — §1.2).
2. **What was changed** on client and server (§2 summary).
3. **Sync/display correctness** (Task 2 — §2): since the server is now the authority and `GetStaffAsync` reads
   the server roster when online, all admins see the same up-to-date list; numbers/accounting unaffected
   (this path touches identity only, not money).
4. **⚠️ DOES THE SERVER NEED UPDATING?** — **YES.** Tasks 1–3 add new server code
   (`Animora.Contracts.Wire`, `Animora.Persistence.PostgreSql`, `Animora.Server`). The running central server
   at `http://188.212.99.234:5000` must be **rebuilt and redeployed** with these endpoints before the Windows
   client's new user-creation path works end-to-end. Tell the user explicitly they must redeploy the server.

### ☐ TASK 8 (FINAL) — Delete this plan file
- Delete ONLY this file: `rm /home/user/webapp/Plan/staff-user-server-persistence.md`
- Do NOT touch any other file, roadmap, or doc.
- **Push:** `scripts/git/push.sh "chore(plan): remove completed staff-server-persistence plan"`

---

## 4. Guardrails (apply to every task)
- Edit only files named in a task step; purposeful reads only (the diagnosis above means you should not need
  to re-grep the whole repo).
- Keep the LOCAL `StaffManagement.cs` handlers — they remain the offline/local-only-build path. Do not delete.
- No full-solution build, no long test runs. Scoped `dotnet build` of touched projects only.
- Persian/RTL UI text style must match existing strings (see `LoginViewModel.MapError`, `ProfileMapping`).
- Numbers/money: this change is identity-only; do not alter any financial/minor-unit code.
- After EACH task: `scripts/git/push.sh "msg"` (locked). Heavy mid-task change → push it too.
