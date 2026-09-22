# Phase B03 — Local identity & real login

**Band:** M · **Track:** B · **Depends on:** B02 · **Satisfies:** R-B04, R-B09
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §4, §7, §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §10.2 (authentication architecture), §10.4 (offline session)
- `STACK.md` §5.3 (Identity technology — password hashing primitives only on the client)
- `src/Animora.Domain/Identity/*` (`User`, `RoleDefinition`, `StaffMembership`, `Device`)
- `src/Animora.Application/Security/ICurrentUserProvider.cs`,
  `src/Animora.Application/Security/SingleUserProvider.cs`
- `src/Animora.Desktop/Pages/Login/LoginViewModel.cs`,
  `src/Animora.Desktop/Pages/Login/LoginWindow.axaml.cs`, `src/Animora.Desktop/App.axaml.cs`
- `src/Animora.Contracts/Identity/ICurrentUserContext.cs`,
  `src/Animora.Contracts/Navigation/UserRole.cs`

## Goal
Replace the hardcoded `admin`/`admin` gate with a real local credential store: hashed passwords,
lockout, a first-run owner-setup path, a real session with app-lock, and a real
`ICurrentUserContext`/`ICurrentUserProvider` so every command from now on carries a genuine `UserId`
and `DeviceId`.

## Design decisions (pre-made — implement, do not re-derive)
- **Package pin added:** `Microsoft.AspNetCore.Identity` `10.0.12` in `Directory.Packages.props`,
  consumed **only** by `Animora.Infrastructure.Local` and **only** for
  `PasswordHasher<TUser>`/`PasswordOptions` primitives (STACK.md §11; no Identity stores, no
  `IdentityDbContext` on the client). `LayeringRulesTests` gains a fact: no other project references it.
- **Domain additions (A03 entities extended, no new aggregate):**
  `User` gains `string PasswordHash`, `int FailedAccessCount`, `DateTimeOffset? LockoutEndUtc`,
  `DateTimeOffset? LastLoginUtc`, `bool MustChangePassword`, `string SecurityStamp` (ULID, rotated on
  every credential change — the value E07 uses for server-side invalidation).
  Methods: `RecordFailedLogin(IClock, int maxAttempts, TimeSpan window)`,
  `RecordSuccessfulLogin(IClock)`, `SetPassword(string hash, IClock)`, `Unlock(IClock)`.
  **Lockout policy (fixed):** 5 failures → 15-minute lockout; the counter resets on success or after
  the lockout expires.
- **Local tables** (`[LocalOnly]`, migration `005_LocalIdentity`):
  `LocalSession` — `Id`, `UserId`, `DeviceId`, `StartedAtUtc`, `LastActivityUtc`, `ExpiresAtUtc`,
  `IsLocked`, `EndedAtUtc`, `EndReason`. One active row at a time; the row survives a crash so a
  restart can restore or expire the session deliberately.
  `LoginAttemptLog` — append-only `Id`, `UsernameAttempted`, `SucceededOrCode`, `OccurredAtUtc`,
  `DeviceId` (added to `AppendOnlyGuard.ProtectedTypes`).
- **Application surface** (`src/Animora.Application/UseCases/Identity/`):
  - `SignInCommand(string Username, string Password, bool RememberDevice)` → `SignInResult`
    (`sealed record SignInResult(string UserId, string DisplayName, StaffRole Role,
    IReadOnlyList<string> Permissions, bool MustChangePassword, DateTimeOffset ExpiresAtUtc)`).
    **Not** `IJournaledCommand` — a sign-in is not business intent.
  - `SignOutCommand(string Reason)` → `Unit`. `LockSessionCommand`/`UnlockSessionCommand(string
    Password)`. `ChangePasswordCommand(string Current, string New)`.
    `CompleteFirstRunSetupCommand(string ClinicName, string OwnerFullName, string Username,
    string Password)` → creates the `Tenant` name, the owner `User`, its `StaffMembership`
    (`StaffRole.Manager`) and the `Device` row; runs **once** (guarded by `LocalInstallation`).
  - `CurrentSessionQuery` → `SessionSnapshot(string? UserId, string? DisplayName, StaffRole? Role,
    bool IsLocked, bool IsFirstRun, DateTimeOffset? ExpiresAtUtc)`.
  - Error codes used: `CREDENTIALS_INVALID`, `ACCOUNT_LOCKED`, `ACCOUNT_DISABLED`,
    `SESSION_EXPIRED`, `PASSWORD_POLICY_VIOLATION`, `FIRST_RUN_ALREADY_COMPLETED`.
    **A failed sign-in always returns `CREDENTIALS_INVALID`** — never "no such user" vs "wrong
    password" (no user enumeration).
- **Password policy (fixed):** minimum 8 characters, at least one letter and one digit; no complexity
  theatre beyond that; validated by `PasswordPolicy.Validate(string)` in
  `Animora.Application/Security/`. Hashing via `PasswordHasher<UserHashSubject>` with
  `PasswordHasherOptions { CompatibilityMode = IdentityV3, IterationCount = 210_000 }`, wrapped by
  `Infrastructure.Local/Security/PasswordHashingService.cs` implementing the Application port
  `IPasswordHasher` (`string Hash(string password)`,
  `PasswordVerificationOutcome Verify(string hash, string password)` →
  `{ Failed, Success, SuccessRehashNeeded }`). A `SuccessRehashNeeded` result silently upgrades the
  stored hash inside the same transaction.
- **Session service:** `Application/Security/ISessionService.cs` +
  `Infrastructure.Local/Security/LocalSessionService.cs`:
  `Task<Result<SessionSnapshot>> RestoreAsync(...)` (reads `LocalSession`; expired → ends it with
  `SESSION_EXPIRED`), `StartAsync`, `TouchAsync` (updates `LastActivityUtc`, throttled to once per
  60 s to avoid write churn), `LockAsync`, `UnlockAsync`, `EndAsync`.
  **Idle policy (fixed):** 30 minutes idle → auto-lock (locked, not signed out); absolute session
  lifetime 12 hours → sign-out. Both values are `LocalSessionOptions`.
  `RememberDevice` extends only the **device trust** flag on `Device`, never the session lifetime.
- **Real `ICurrentUserProvider`:** `Application/Security/SessionCurrentUserProvider.cs` replaces
  `SingleUserProvider` (deleted). It builds `CommandContext`/`QueryContext` from the active
  `LocalSession` + `LocalInstallation`, returning `Result.Fail(SESSION_EXPIRED)` when no session is
  active — so **no command can be dispatched without an authenticated session**.
- **Real `ICurrentUserContext`** (the `Animora.Contracts` seam) in
  `src/Animora.Client.Services/Identity/CurrentUserContextService.cs`: reads the session snapshot and
  maps `StaffRole` → `Animora.Contracts.Navigation.UserRole` via
  `Client.Services/Mapping/RoleMapping.cs` (`Manager→Manager`, `Doctor→Doctor`,
  `Receptionist→Receptionist`, `Cashier→Cashier`, `Accountant→Manager` until the enum is extended —
  documented in the mapping file). `Name`/`RoleLabel`/`Initials` come from the `User` row;
  **Persian role labels stay in the Desktop resource layer** — the service returns the label the
  Desktop already uses via `RoleLabels.For(UserRole)` (an existing Desktop helper; if absent, the
  service returns the stored `RoleDefinition.DisplayName`, which the seeder seeds in Persian).
- **Desktop changes (minimal, listed exhaustively):**
  1. `App.axaml.cs`: `AddAnimoraLocalStore()` + `AddAnimoraApplication()` are now registered; the
     `ICurrentUserContext` line changes from `MockCurrentUserContext` to `CurrentUserContextService`;
     `Animora.Desktop` gains the project reference to `Animora.Client.Services`; `LocalStoreStartup`
     is kicked off as a background task after the first frame.
  2. `LoginViewModel`: `TryAuthenticate` and the two `Demo*` constants are **deleted**; the VM takes
     `IDispatcher` + `ICurrentUserProvider`-free constructor injection of a new
     `Animora.Contracts/Identity/IAuthenticationService` (added to Contracts — an addition, not a
     reshape) with `Task<AuthOutcome> SignInAsync(string username, string password, bool
     rememberDevice, CancellationToken ct)`, `Task SignOutAsync(...)`, `Task<bool> IsFirstRunAsync(...)`,
     `Task<AuthOutcome> CompleteFirstRunAsync(FirstRunRequest request, CancellationToken ct)`.
     `AuthOutcome(bool Succeeded, string? ErrorCode, string? DisplayName)`.
     `SignIn` becomes an async relay command (`await`, `IsBusy` around it) and maps `ErrorCode` →
     the existing Persian strings via a `switch` in the VM (the only place codes become Persian).
  3. `LoginWindow.axaml.cs`: no change. `LoginWindow.axaml`: **no change** (the first-run fields are
     introduced by a new `FirstRunView` only if `IsFirstRunAsync` is true — implemented as an
     existing-dialog flow through `IDialogService`, so **no new .axaml page is added**; the first-run
     dialog reuses the generic input dialog already present in `DialogService`).
  4. `MockCurrentUserContext.cs` is **deleted** (its only consumer is the DI line above).
- `Animora.Client.Services/Identity/AuthenticationService.cs` implements
  `IAuthenticationService` over `IDispatcher` (`SignInCommand`, `SignOutCommand`,
  `CompleteFirstRunSetupCommand`, `CurrentSessionQuery`).
- **Seeding change:** `LocalSeeder` no longer needs a demo user; `CompleteFirstRunSetupCommand` is the
  only user-creation path. If `LocalSeeder` created any placeholder user in A10, remove it here and
  note it in `PROGRESS.md`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `Directory.Packages.props` | modify | pin `Microsoft.AspNetCore.Identity 10.0.12` |
| `src/Animora.Domain/Identity/User.cs` | modify | hash/lockout/security-stamp fields + 4 methods |
| `src/Animora.Persistence.Sqlite/Entities/LocalSession.cs` | create | durable session row |
| `src/Animora.Persistence.Sqlite/Entities/LoginAttemptLog.cs` | create | append-only attempt log |
| `src/Animora.Persistence.Sqlite/Configurations/Identity/*.cs` | modify/create | user columns + 2 new configurations |
| `src/Animora.Persistence.Sqlite/Context/AppendOnlyGuard.cs` | modify | add `LoginAttemptLog` |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `005_LocalIdentity` + snapshot |
| `src/Animora.Application/Security/IPasswordHasher.cs` | create | hashing port + outcome enum |
| `src/Animora.Application/Security/PasswordPolicy.cs` | create | fixed policy validation |
| `src/Animora.Application/Security/ISessionService.cs` | create | session port + `SessionSnapshot` |
| `src/Animora.Application/Security/SessionCurrentUserProvider.cs` | create | real context builder |
| `src/Animora.Application/Security/SingleUserProvider.cs` | delete | replaced by the real provider |
| `src/Animora.Application/UseCases/Identity/*.cs` | create | 6 commands/queries + handlers + validators |
| `src/Animora.Infrastructure.Local/Security/PasswordHashingService.cs` | create | Identity hasher wrapper |
| `src/Animora.Infrastructure.Local/Security/LocalSessionService.cs` | create | restore/start/touch/lock/end |
| `src/Animora.Infrastructure.Local/Options/LocalSessionOptions.cs` | create | idle 30 min, absolute 12 h |
| `src/Animora.Contracts/Identity/IAuthenticationService.cs` | create | Contracts **addition** (auth seam) |
| `src/Animora.Contracts/Identity/AuthModels.cs` | create | `AuthOutcome`, `FirstRunRequest` |
| `src/Animora.Client.Services/Identity/AuthenticationService.cs` | create | Contracts impl over `IDispatcher` |
| `src/Animora.Client.Services/Identity/CurrentUserContextService.cs` | create | real `ICurrentUserContext` |
| `src/Animora.Client.Services/Mapping/RoleMapping.cs` | create | `StaffRole` ↔ `UserRole` |
| `src/Animora.Desktop/Animora.Desktop.csproj` | modify | reference `Animora.Client.Services` |
| `src/Animora.Desktop/App.axaml.cs` | modify | add local store + application DI; swap `ICurrentUserContext`; register auth; start `LocalStoreStartup` off first frame |
| `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` | modify | delete demo creds; async sign-in via `IAuthenticationService`; error-code → Persian map; first-run flow via `IDialogService` |
| `src/Animora.Desktop/Mocks/MockCurrentUserContext.cs` | delete | replaced |
| `tests/Animora.Application.Tests/SignInTests.cs` | create | success, wrong password, lockout, disabled, enumeration-safety |
| `tests/Animora.Application.Tests/FirstRunSetupTests.cs` | create | creates tenant/user/membership/device once only |
| `tests/Animora.Application.Tests/SessionLifecycleTests.cs` | create | idle lock, absolute expiry, restore after crash, lock/unlock |
| `tests/Animora.Application.Tests/PasswordPolicyTests.cs` | create | policy + rehash-on-verify upgrade |

## Implementation steps
1. Pin the Identity package; extend `User` with the credential/lockout/security-stamp members and
   their methods + tests-visible invariants.
2. Add `LocalSession` + `LoginAttemptLog`, their configurations, the `AppendOnlyGuard` entry, and
   migration `005_LocalIdentity`.
3. Add `IPasswordHasher` + `PasswordPolicy` (Application) and `PasswordHashingService`
   (Infrastructure.Local) with the fixed hasher options.
4. Add `ISessionService`/`SessionSnapshot` + `LocalSessionService` + `LocalSessionOptions`.
5. Write the six use cases with validators; `SignInCommandHandler` order:
   load user (by username, tenant-scoped) → lockout check → verify → on failure
   `RecordFailedLogin` + append `LoginAttemptLog` + return `CREDENTIALS_INVALID` → on success
   `RecordSuccessfulLogin`, rehash if needed, start the session, return permissions from
   `DefaultRolePermissions` + the membership's role.
6. Replace `SingleUserProvider` with `SessionCurrentUserProvider` and delete the old file; update
   `AddAnimoraApplication` registration.
7. Add `IAuthenticationService` + `AuthModels` to `Animora.Contracts`, then the
   `Client.Services` implementations (`AuthenticationService`, `CurrentUserContextService`,
   `RoleMapping`).
8. Desktop: add the project reference, register the local store + application + auth services, swap the
   `ICurrentUserContext` line, start `LocalStoreStartup` as a post-first-frame background task, rewrite
   `LoginViewModel` per the design decisions, delete `MockCurrentUserContext.cs`.
9. Verify the login screen behavior manually: first run shows the setup flow; the created credentials
   sign in; a wrong password shows the existing Persian error; 5 failures lock for 15 minutes.
10. Write the four test classes.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- `Animora.Contracts` **additions**: `IAuthenticationService`, `AuthOutcome`, `FirstRunRequest`
  (no existing member reshaped).
- Domain: `User` credential/lockout/stamp members. Local tables `LocalSessions`, `LoginAttemptLogs`
  (migration `005_LocalIdentity`).
- Ports/impl: `IPasswordHasher`, `PasswordPolicy`, `ISessionService`/`LocalSessionService`,
  `PasswordHashingService`, `SessionCurrentUserProvider`, `LocalSessionOptions`.
- Use cases: `SignInCommand`, `SignOutCommand`, `LockSessionCommand`, `UnlockSessionCommand`,
  `ChangePasswordCommand`, `CompleteFirstRunSetupCommand`, `CurrentSessionQuery`.
- Client services: `AuthenticationService`, `CurrentUserContextService`, `RoleMapping`.
- DI: `ICurrentUserContext` → `CurrentUserContextService`; `IAuthenticationService` →
  `AuthenticationService`; local store + application registered. Mock deleted:
  `MockCurrentUserContext.cs`. `ErrorCodes` += 6.

## Rules that apply here
- **CONVENTIONS §7** — passwords use ASP.NET Core Identity primitives only; no custom crypto; the
  security stamp rotates on every credential change; failures never reveal whether a user exists.
- **A-RULE 023** — this phase covers **authentication only**; permissions returned in `SignInResult`
  are data, not an authorization decision (B04 evaluates them).
- **A-RULE 029** — the session is policy-driven with explicit expiry; there is no silent extension.
  The device-bound capability snapshot itself is B04's.
- **CONVENTIONS §10 items 4–6** — the DI swap is one line per service; no `.axaml` change; the
  `LoginViewModel` change is listed explicitly and is the only ViewModel touched.
- **PERFORMANCE.md §4/§12** — sign-in is fully async; `LocalStoreStartup` never blocks first frame;
  `TouchAsync` is throttled so session activity does not write per interaction.
- **R-B09** — the session row is durable, so a crash cannot leave a half-authenticated state.

## Out of scope
- Permission evaluation, screen/action gating, capability snapshots → B04.
- Server-side identity, bearer/refresh tokens, device revocation → D03, E07.
- Role management UI (create/edit roles) → C10 (Profile) consumes B04's catalog.
- Password reset by an operator → D03/E01 (server-side flows only).
- Any other mock deletion → the owning Track C phase.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `grep -rn "admin" src/Animora.Desktop/Pages/Login/LoginViewModel.cs` returns nothing; the file
  contains no `TryAuthenticate`.
- `src/Animora.Desktop/Mocks/MockCurrentUserContext.cs` does not exist and no DI line references it.
- `SignInTests` green: correct credentials succeed and start a session; a wrong password and an unknown
  username both return `CREDENTIALS_INVALID`; the 5th failure returns `ACCOUNT_LOCKED` and the 6th is
  rejected without a hash comparison; after 15 minutes the account unlocks.
- `FirstRunSetupTests` green: the command creates tenant name + user + `StaffRole.Manager` membership
  + device; a second invocation returns `FIRST_RUN_ALREADY_COMPLETED`.
- `SessionLifecycleTests` green: 30 minutes idle → `IsLocked`; 12 hours → signed out with
  `SESSION_EXPIRED`; a restored session after a simulated crash is either valid or explicitly expired,
  never silently extended.
- `PasswordPolicyTests` green: policy violations return `PASSWORD_POLICY_VIOLATION`; a legacy-format
  hash is transparently upgraded on a successful verify.
- Dispatching any command with no active session returns `SESSION_EXPIRED`.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`; first run shows the
  setup flow, and the created account signs in against the real local database.

## PROGRESS.md update
Mark B03 done; advance "Next phase" to `B04`; append a "Repo state" entry listing: the Identity package
pin, `User` credential members + lockout policy, `LocalSessions`/`LoginAttemptLogs` + migration
`005_LocalIdentity`, the hashing/session ports and implementations, the 7 use cases, the
`Animora.Contracts` additions (`IAuthenticationService`, `AuthOutcome`, `FirstRunRequest`), the
`Client.Services` identity services, the DI changes (local store + application + 2 service lines), the
`LoginViewModel` rewrite, **mock deleted: `MockCurrentUserContext.cs`**, `ErrorCodes` += 6, the 4 test
classes, deviations.
