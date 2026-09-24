# Phase B03c — Identity contracts, desktop wiring, seeding & tests (session 3/3 of B03)

**Band:** M · **Track:** B · **Depends on:** B03b ✅ · **Next:** B04a · Split of original B03 (B03a→B03c, one session each).
**Satisfies (completes with B03a+B03b):** R-B04, R-B09 · **Runnable after this phase:** desktop offline yes · server required no

---

## ⚡ Zero-exploration contract (read this first — it changes how you work)

> **This file is self-contained and pre-verified against the current tree.** Every existing type,
> signature, constant, file path and command you need is reproduced verbatim in the **§Reference
> pack** below. **Do not re-open the repository to "understand" it, and do not re-derive anything.**
> Implement top-to-bottom from §Implementation plan. The only reads you may need are the exact files
> listed under **§Files to modify** (because you are editing them), and only when you edit them.
>
> If — and only if — a compile error proves a snippet here diverges from the tree, fix the smallest
> thing and record it in the `PROGRESS.md` deviation log. Do **not** pre-emptively audit.

**This is session 3 of 3 — the B03 close-out.** B03a landed the foundation (package, domain, tables,
migration `005`, hashing/session infra); B03b landed the application layer (7 use cases,
`SessionCurrentUserProvider`, repositories, application DI). **This session lands the outward-facing
seam and proves the whole thing**: the `Animora.Contracts` auth interface, the `Client.Services`
implementations, the Desktop login/first-run rewrite, the seeding change, and **all B03 tests** plus
the runnable gate. B03 is ✅ only when this session's gate is green.

### Environment (already true in the sandbox — do not probe these)
- **Run `scripts/sandbox/install-prereqs.sh` once at the start** (idempotent). `dotnet-ef 10.0.12` is
  at `.sandbox/tools/dotnet-ef`; do not `dotnet tool install`. **No new migration this session.**
- Build/test/run are **only** these three scripts (memory-capped; do not invoke raw `dotnet build`):
  - `scripts/sandbox/build.sh full` → expect `0 warnings / 0 errors`
  - `scripts/sandbox/test.sh` → expect `TESTS OK`
  - `scripts/sandbox/run.sh` → expect `SMOKE RUN OK`
- Sandbox RAM ≈ 985 MiB. Do not add parallel/server-GC flags.

### Rules to obey (condensed — do not open `CONVENTIONS.md` for these)
- **§2 layering:** `Animora.Contracts` references no `Animora.*`; `Animora.Client.Services` references
  Application+Contracts+Persistence+Infrastructure. **`Microsoft.AspNetCore.Identity` stays in
  `Infrastructure.Local` ONLY** (the `LayeringRulesTests` fact this session adds proves it).
- **§4 style:** `sealed`/`sealed record`; nullable honored; async-all-the-way; **`Animora.Client.Services`
  methods returning to a ViewModel do NOT `ConfigureAwait(false)`**; return `Result`/`Result<T>`;
  `IClock` injected; Ids via `IdGenerator.NewId()`.
- **§7 security:** a failed sign-in never reveals whether a user exists.
- **§9 testing:** every phase adds tests for what it introduces; **no test asserts a Persian UI
  string**; no test uses wall-clock time (inject `IClock`).
- **§10 items 4–6:** DI swaps are one line per service; `LoginWindow.axaml` unchanged; `LoginViewModel`
  is the only existing ViewModel touched (plus the net-new `FirstRunViewModel`).
- **§11/§12:** `build.sh full` 0/0, `test.sh` green, `run.sh` OK; no forbidden dep; `PROGRESS.md`
  updated (mark **B03** done overall); commit on `genspark_ai_developer`.

---

## Goal (this session)
Expose the B03b use cases through a clean `Animora.Contracts` seam, implement it in
`Client.Services`, rewire the Desktop so real login and first-run setup work against the local
database, remove the login mock, ensure no demo user is seeded, and author every B03 test so the
full acceptance battery is green.

---

## §Reference pack — existing code you build on (verbatim; do not re-read source)

### R1 · Use cases + results from B03b (consume via `IDispatcher`)
`SignInCommand(string Username, string Password, bool RememberDevice)` → `SignInResult`;
`SignOutCommand(string Reason)`; `LockSessionCommand()`; `UnlockSessionCommand(string Password)` →
`SessionSnapshot`; `ChangePasswordCommand(string Current, string New)`;
`CompleteFirstRunSetupCommand(string ClinicName, string OwnerFullName, string Username, string Password)`
→ `SignInResult`; `CurrentSessionQuery` → `SessionSnapshot`.
```csharp
public sealed record SignInResult(string UserId, string DisplayName, StaffRole Role,
    IReadOnlyList<string> Permissions, bool MustChangePassword, DateTimeOffset ExpiresAtUtc);
public sealed record SessionSnapshot(string? UserId, string? DisplayName, StaffRole? Role,
    bool IsLocked, bool IsFirstRun, DateTimeOffset? ExpiresAtUtc);
```
`IDispatcher.SendAsync`/`QueryAsync` per B03b §R1; `ICurrentUserProvider.BuildCommandContextAsync`/
`BuildQueryContext` build the context (`SessionCurrentUserProvider` from B03b).

### R2 · Error codes to map (added by B03a, consumed here)
`CredentialsInvalid` (`CREDENTIALS_INVALID`), `SessionExpired` (`SESSION_EXPIRED`),
`PasswordPolicyViolation` (`PASSWORD_POLICY_VIOLATION`), `FirstRunAlreadyCompleted`
(`FIRST_RUN_ALREADY_COMPLETED`), and the reused `UserLockedOut` (`USER_LOCKED_OUT`) / `UserDisabled`
(`USER_DISABLED`). The `Result.Error.Code` string is what maps to Persian text (Desktop only).

### R3 · Desktop composition-root reality
`src/Animora.Desktop/Animora.Desktop.csproj` today references **only `Animora.Contracts`** — this
session adds `<ProjectReference Include="..\Animora.Client.Services\Animora.Client.Services.csproj" />`
(that one reference pulls in Application + Persistence + Infrastructure transitively).
`App.axaml.cs` `ConfigureServices()` is a `ServiceCollection` builder ending in
`BuildServiceProvider()`; it currently has `services.AddSingleton<ICurrentUserContext,
MockCurrentUserContext>();` and `services.AddTransient<LoginViewModel>();`. `LoginViewModel` today is
front-end-only with `DemoUser`/`DemoPassword`/`TryAuthenticate` and raises `event EventHandler
LoginSucceeded`. `IDialogService` exposes `ShowModal(object content, double maxWidth)`, `ShowDrawer`,
`ShowSheet`, `SaveTextFileAsync`, `Close` — **there is no generic input dialog**, so first-run is a
net-new `FirstRunView`/`FirstRunViewModel` shown via `ShowModal(...)`.
`Animora.Contracts/Navigation/UserRole.cs` enum members: `Manager, Receptionist, Cashier, Doctor`
(no `Accountant`). `ILocalStoreInitializer.InitializeAsync` (A11d) is the startup that builds the DB.
`AddAnimoraLocalInfrastructure()` (extended by B03a) wires the whole local stack;
`AddAnimoraApplication(...)` (extended by B03b) registers the use cases.

### R4 · Seeding reality
`LocalSeeder` (A10c) seeds tenant/roles/permissions/etc. via idempotent blocks. It must **not** seed a
demo user — `CompleteFirstRunSetupCommand` (B03b) is the only user-creation path.

---

## §Design decisions (pre-made — implement exactly, do not re-derive)

### D1 · `Animora.Contracts` additions (auth seam — additions only, no reshape)
```csharp
// src/Animora.Contracts/Identity/IAuthenticationService.cs
public interface IAuthenticationService {
    Task<AuthOutcome> SignInAsync(string username, string password, bool rememberDevice, CancellationToken ct = default);
    Task SignOutAsync(string reason, CancellationToken ct = default);
    Task<bool> IsFirstRunAsync(CancellationToken ct = default);
    Task<AuthOutcome> CompleteFirstRunAsync(FirstRunRequest request, CancellationToken ct = default); }
// src/Animora.Contracts/Identity/AuthModels.cs
public sealed record AuthOutcome(bool Succeeded, string? ErrorCode, string? DisplayName);
public sealed record FirstRunRequest(string ClinicName, string OwnerFullName, string Username, string Password);
```

### D2 · `Animora.Client.Services` implementations
- `Identity/AuthenticationService.cs` implements `IAuthenticationService` over `IDispatcher`
  (+ `ICurrentUserProvider` to build the context): maps `SignInCommand`/`SignOutCommand`/
  `CompleteFirstRunSetupCommand`/`CurrentSessionQuery` results → `AuthOutcome` (`Succeeded`,
  `Error.Code`, `DisplayName`). **Does not** `ConfigureAwait(false)` (returns to a ViewModel).
- `Identity/CurrentUserContextService.cs` implements `Animora.Contracts.Identity.ICurrentUserContext`
  by reading `CurrentSessionQuery` and mapping `StaffRole → UserRole` via `Mapping/RoleMapping.cs`.
- `Mapping/RoleMapping.cs`: `Manager→Manager, Doctor→Doctor, Receptionist→Receptionist,
  Cashier→Cashier, Accountant→Manager` (until the `UserRole` enum gains `Accountant`; document the
  fallback in the file). `Name`/`RoleLabel`/`Initials` come from the `User`/`RoleDefinition` row;
  Persian role labels stay in the Desktop resource layer (return the stored
  `RoleDefinition.DisplayName`, seeded in Persian, if no Desktop `RoleLabels` helper is present).

### D3 · Desktop changes (minimal, exhaustive)
1. `Animora.Desktop.csproj`: add the `Animora.Client.Services` project reference (R3).
2. `App.axaml.cs` `ConfigureServices()`:
   - Call `services.AddAnimoraLocalInfrastructure();` then `services.AddAnimoraApplication(builder =>
     { /* register the identity use cases */ });` (use whatever registration path B03b left for the
     Desktop to call).
   - Replace `AddSingleton<ICurrentUserContext, MockCurrentUserContext>()` with
     `AddSingleton<ICurrentUserContext, CurrentUserContextService>()`.
   - Register `AddSingleton<IAuthenticationService, AuthenticationService>()`.
   - After the first frame, kick off `ILocalStoreInitializer.InitializeAsync` as a background task
     (do not block the UI thread / first frame). Gate the Login form's submit on its completion
     (`LoginViewModel.IsBusy`/"starting up" state) so the user can't sign in before the store exists.
3. `LoginViewModel`: delete `DemoUser`/`DemoPassword`/`TryAuthenticate`; inject
   `IAuthenticationService`; make `SignIn` an async relay command (`await`, `IsBusy` around it) that
   calls `SignInAsync` and, on success, raises `LoginSucceeded`; on failure maps `ErrorCode` → the
   existing Persian strings via a `switch` (the ONLY place codes become Persian). On startup, if
   `IsFirstRunAsync()` is true, show `FirstRunView` via `IDialogService.ShowModal(new
   FirstRunViewModel(...))` and call `CompleteFirstRunAsync`.
   Persian map: `CREDENTIALS_INVALID → "نام کاربری یا رمز عبور نادرست است."`,
   `USER_LOCKED_OUT → "حساب موقتاً قفل شده است. بعداً تلاش کنید."`,
   `USER_DISABLED → "این حساب غیرفعال شده است."`,
   `PASSWORD_POLICY_VIOLATION → "رمز عبور باید حداقل ۸ نویسه و شامل حرف و رقم باشد."`, else a generic
   `"خطای غیرمنتظره رخ داد."`.
4. Add `FirstRunView.axaml` + `FirstRunView.axaml.cs` + `FirstRunViewModel` under
   `Pages/Login/` (clinic name / owner name / username / password fields). This is a **net-new**
   View/VM pair (CONVENTIONS §10's "no `.axaml` change" is about Track C mock retirement, not this).
   `LoginWindow.axaml`/`.axaml.cs`: **no change.**
5. Delete `src/Animora.Desktop/Mocks/MockCurrentUserContext.cs` (its only consumer was the DI line).

### D4 · Seeding
`LocalSeeder` keeps seeding tenant/roles/permissions; it must **not** seed a demo user. If A10 left a
placeholder user, remove it and note it in `PROGRESS.md`.

### D5 · Tests (all B03 tests live here)
- `tests/Animora.Application.Tests/SignInTests.cs` — success, wrong password, unknown user, lockout,
  disabled, enumeration-safety.
- `tests/Animora.Application.Tests/FirstRunSetupTests.cs` — creates user/membership/device once only;
  second call → `FIRST_RUN_ALREADY_COMPLETED`.
- `tests/Animora.Application.Tests/SessionLifecycleTests.cs` — idle lock, absolute expiry, restore
  after crash, lock/unlock.
- `tests/Animora.Application.Tests/PasswordPolicyTests.cs` — policy + rehash-on-verify upgrade.
- `tests/Animora.Domain.Tests/LayeringRulesTests.cs` — **modify**: fact that only
  `Animora.Infrastructure.Local` references `Microsoft.AspNetCore.Identity` (D1 of B03a's pin proved
  here).

---

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Contracts/Identity/IAuthenticationService.cs` | create | auth seam (D1) |
| `src/Animora.Contracts/Identity/AuthModels.cs` | create | `AuthOutcome`, `FirstRunRequest` |
| `src/Animora.Client.Services/Identity/AuthenticationService.cs` | create | impl over `IDispatcher` (D2) |
| `src/Animora.Client.Services/Identity/CurrentUserContextService.cs` | create | real `ICurrentUserContext` |
| `src/Animora.Client.Services/Mapping/RoleMapping.cs` | create | `StaffRole` ↔ `UserRole` |
| `src/Animora.Desktop/Animora.Desktop.csproj` | modify | reference `Animora.Client.Services` |
| `src/Animora.Desktop/App.axaml.cs` | modify | local infra + application DI; swap `ICurrentUserContext`; register auth; start `LocalStoreStartup` off first frame |
| `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` | modify | delete demo creds; async sign-in; code→Persian map; first-run flow |
| `src/Animora.Desktop/Pages/Login/FirstRunView.axaml` (+`.axaml.cs`) | create | first-run form |
| `src/Animora.Desktop/Pages/Login/FirstRunViewModel.cs` | create | first-run VM |
| `src/Animora.Desktop/Mocks/MockCurrentUserContext.cs` | delete | replaced |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` (or the seeder file) | modify (if needed) | ensure no demo user seeded (D4) |
| `tests/Animora.Application.Tests/SignInTests.cs` | create | success, wrong password, unknown user, lockout, disabled, enumeration-safety |
| `tests/Animora.Application.Tests/FirstRunSetupTests.cs` | create | creates user/membership/device once only |
| `tests/Animora.Application.Tests/SessionLifecycleTests.cs` | create | idle lock, absolute expiry, restore after crash, lock/unlock |
| `tests/Animora.Application.Tests/PasswordPolicyTests.cs` | create | policy + rehash-on-verify upgrade |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | modify | fact: only `Infrastructure.Local` references `Microsoft.AspNetCore.Identity` |
| `backend-roadmap/PROGRESS.md` | modify | mark B03c **and B03** done, next → B04 (B04a), repo-state entry, deviations |

---

## §Implementation plan (do these in order)
1. **Contracts + Client.Services** (D1, D2): auth seam + implementations + role mapping.
2. **Desktop** (D3): project reference; DI wiring; `LocalStoreStartup` off first frame; rewrite
   `LoginViewModel`; add `FirstRunView`/VM; delete `MockCurrentUserContext.cs`.
3. **Seeding** (D4): ensure no demo user is seeded.
4. **Tests** (D5): the four Application test classes + the `LayeringRulesTests` fact.
5. **Gate checks:** `scripts/sandbox/build.sh full` = 0/0 · `scripts/sandbox/test.sh` = `TESTS OK` ·
   `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
6. **PROGRESS.md** update (mark B03 done overall) + commit on `genspark_ai_developer`.

---

## Contracts / schema added
- `Animora.Contracts` additions: `IAuthenticationService`, `AuthOutcome`, `FirstRunRequest`.
- Client services: `AuthenticationService`, `CurrentUserContextService`, `RoleMapping`.
- DI: `ICurrentUserContext` → `CurrentUserContextService`; `IAuthenticationService` →
  `AuthenticationService`; local infra + application registered in the Desktop. Mock deleted:
  `MockCurrentUserContext.cs`. No new migration, no new error code this session.

## Rules that apply here
- **CONVENTIONS §7** — failures never reveal whether a user exists.
- **CONVENTIONS §10 items 4–6** — DI swaps are one line per service; `LoginWindow.axaml` unchanged;
  `LoginViewModel` is the only existing ViewModel touched (plus net-new `FirstRunViewModel`).
- **PERFORMANCE.md §4/§12** — `LocalStoreStartup` never blocks first frame.
- **R-B09** — a restored session after a crash is either valid or explicitly expired.

## Out of scope (owned by the sibling sessions / later phases)
- Package/domain/tables/hashing/session infra → **B03a**. Use cases/provider/repositories/application
  DI → **B03b**.
- Permission evaluation, screen/action gating, capability snapshots → B04.
- Server-side identity, bearer/refresh tokens, device revocation → D03, E07.
- Role management UI → C10. Password reset by an operator → D03/E01. Other mock deletions → Track C.

## Acceptance criteria
- `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `grep -rn "admin" src/Animora.Desktop/Pages/Login/LoginViewModel.cs` returns nothing; the file
  contains no `TryAuthenticate`.
- `src/Animora.Desktop/Mocks/MockCurrentUserContext.cs` does not exist and no DI line references it.
- `SignInTests` green: correct credentials succeed and start a session; a wrong password and an
  unknown username both return `CREDENTIALS_INVALID`; the 5th failure returns `USER_LOCKED_OUT` and
  the 6th is rejected without a hash comparison; after 15 minutes the account unlocks.
- `FirstRunSetupTests` green: the command creates user + `StaffRole.Manager` membership + device (no
  new tenant); a second invocation returns `FIRST_RUN_ALREADY_COMPLETED`.
- `SessionLifecycleTests` green: 30 min idle → `IsLocked`; 12 h → signed out with `SESSION_EXPIRED`;
  a restored session after a simulated crash is either valid or explicitly expired, never silently
  extended.
- `PasswordPolicyTests` green: policy violations return `PASSWORD_POLICY_VIOLATION`; a legacy-format
  hash is transparently upgraded on a successful verify.
- Dispatching any command with no active session returns `SESSION_EXPIRED`.
- `LayeringRulesTests` green: only `Animora.Infrastructure.Local` references
  `Microsoft.AspNetCore.Identity`.
- Runnability (§11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`; first run shows the setup flow, and
  the created account signs in against the real local database.

## PROGRESS.md update
Mark B03c done **and mark B03 done overall**; advance "Next phase" to `B04` (`B04a`); append a "Repo
state" entry listing: the `Animora.Contracts` additions, the `Client.Services` identity services, the
DI changes (2 service lines + local infra + application), the `LoginViewModel` rewrite +
`FirstRunView`/VM, **mock deleted: `MockCurrentUserContext.cs`**, the seeding change (no demo user),
the 4 Application test classes + the `LayeringRulesTests` fact, and any deviation with its
justification.
