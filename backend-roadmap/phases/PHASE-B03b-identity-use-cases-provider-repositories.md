# Phase B03b — Identity use cases, provider swap & repositories (session 2/3 of B03)

**Band:** M · **Track:** B · **Depends on:** B03a ✅ · **Next:** B03c · Split of original B03 (B03a→B03c, one session each).
**Satisfies (with B03a+B03c):** R-B04, R-B09 · **Runnable after this phase:** desktop offline yes · server required no

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

**This is session 2 of 3.** B03a already landed: `Microsoft.AspNetCore.Identity` pin,
`User.MustChangePassword`, the 4 error codes, `LocalSessions`/`LoginAttemptLogs` + migration
`005_LocalIdentity`, and the `IPasswordHasher`/`PasswordPolicy`/`ISessionService` ports **with their
`Infrastructure.Local` implementations already registered**. **This session lands the application
layer only**: the 7 use cases, the real `ICurrentUserProvider`, the repository additions and the
application-side DI wiring. **No `Animora.Contracts`, no `Client.Services`, no Desktop, no tests** —
those are B03c. Do not build ahead of this scope.

### Environment (already true in the sandbox — do not probe these)
- **Run `scripts/sandbox/install-prereqs.sh` once at the start** (idempotent). `dotnet-ef 10.0.12` is
  at `.sandbox/tools/dotnet-ef`; do **not** `dotnet tool install` it. **No new migration this
  session** (`005_LocalIdentity` from B03a is the only Identity migration).
- Build is **only** `scripts/sandbox/build.sh full` → expect `0 warnings / 0 errors`. **No `test.sh`
  / `run.sh` this session** (Desktop is wired and tests authored in B03c).
- Sandbox RAM ≈ 985 MiB. Do not add parallel/server-GC flags.

### Rules to obey (condensed — do not open `CONVENTIONS.md` for these)
- **§2 layering:** `Animora.Application` references only Domain; `Persistence.Sqlite` may reference
  Application+Domain. **`Microsoft.AspNetCore.Identity` stays in `Infrastructure.Local` ONLY.**
- **§4 style:** `sealed`/`sealed record`; nullable honored; async-all-the-way with
  `CancellationToken ct = default`; library projects `ConfigureAwait(false)`; no exceptions for
  expected flow — return `Result`/`Result<T>`; `IClock` injected; Ids via `IdGenerator.NewId()` (ULID).
- **§7 security:** a failed sign-in never reveals whether a user exists; security stamp rotates on
  every credential change (already handled by `User.SetPasswordHash`).
- **§11/§12:** `build.sh full` 0/0; no forbidden dep; `PROGRESS.md` updated; commit on
  `genspark_ai_developer`.

---

## Goal (this session)
Turn the B03a machinery into working authentication use cases and a real command/query security
context: sign-in with lockout, first-run owner setup, sign-out, lock/unlock, change-password, a
current-session query, the `SessionCurrentUserProvider` that sources `UserId` from the live session,
and the repositories those handlers need. Every command from now on carries a genuine `UserId`/`DeviceId`.

---

## §Reference pack — existing code you build on (verbatim; do not re-read source)

### R1 · Application abstractions (`src/Animora.Application/Abstractions/`)
```csharp
public sealed record CommandContext(string TenantId, string UserId, string DeviceId, string OperationId,
    string CorrelationId, string? CausationId, string? DependsOnOperationId,
    long? ExpectedAggregateVersion, DateTimeOffset CreatedAtUtc);
public sealed record QueryContext(string TenantId, string UserId, string CorrelationId);
public interface ICommand<TResult>;                         // marker, named <Verb><Aggregate>Command
public interface IQuery<TResult>;                           // marker, named <Subject>Query
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{ Task<Result<TResult>> HandleAsync(TCommand command, CommandContext context, CancellationToken ct); }
public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{ Task<Result<TResult>> HandleAsync(TQuery query, QueryContext context, CancellationToken ct); }
public interface IDispatcher {
    Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CommandContext ctx, CancellationToken ct = default);
    Task<Result<TResult>> QueryAsync<TResult>(IQuery<TResult> query, QueryContext ctx, CancellationToken ct = default); }
public interface IUnitOfWork {
    Task<Result> SaveAsync(CancellationToken ct = default);
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default); }
```

### R2 · `Result`/`ErrorCode` + the codes B03a added
`Result`/`Result<T>` shape per B03a §R2. **Already declared (REUSE):** `ValidationFailed`, `NotFound`,
`UserDisabled` (`USER_DISABLED`), `UserLockedOut` (`USER_LOCKED_OUT`). **Added by B03a (USE these):**
`CredentialsInvalid` (`CREDENTIALS_INVALID`), `SessionExpired` (`SESSION_EXPIRED`),
`PasswordPolicyViolation` (`PASSWORD_POLICY_VIOLATION`), `FirstRunAlreadyCompleted`
(`FIRST_RUN_ALREADY_COMPLETED`). Do not add any error code this session.

### R3 · Security seam & the provider to replace (`src/Animora.Application/Security/`)
```csharp
public interface ICurrentUserProvider {
    Task<Result<CommandContext>> BuildCommandContextAsync(string? operationId, string? dependsOn,
        long? expectedVersion, CancellationToken ct);
    Result<QueryContext> BuildQueryContext(); }
```
`SingleUserProvider` (to be DELETED) is `ICurrentUserProvider` built from `ILocalInstallationIdentity`
+ `ICorrelationContext` + `OperationIdFactory` + `IClock`, with `const string LocalOwnerUserId =
"U-LOCAL-OWNER"`. Correlation id fallback: `correlation.CurrentId ?? IdGenerator.NewId()`.
`OperationIdFactory.New()` mints a ULID. `ICorrelationContext.CurrentId` is `string?`.
B03a's `ISessionService`/`SessionSnapshot` is the session source (see B03a §D5 for the interface).

### R4 · B03a ports (already exist — consume them)
```csharp
// Animora.Application/Security/IPasswordHasher.cs
public interface IPasswordHasher { string Hash(string password);
    PasswordVerificationOutcome Verify(string hash, string password); }
public enum PasswordVerificationOutcome { Failed, Success, SuccessRehashNeeded }
// Animora.Application/Security/PasswordPolicy.cs
public static class PasswordPolicy { public static Result Validate(string password); }  // ≥8, letter+digit
// Animora.Application/Security/ISessionService.cs  (StartAsync/UnlockAsync/EndAsync/LockAsync/TouchAsync/RestoreAsync)
```

### R5 · Installation identity + repository patterns
```csharp
// src/Animora.Application/Ports/ILocalInstallationIdentity.cs
public interface ILocalInstallationIdentity { string TenantId { get; } string DeviceId { get; } }
// src/Animora.Application/Ports/IIdentityRepository.cs  (ADD FindByUsernameAsync)
public interface IIdentityRepository {
    Task<User?> FindAsync(string id, CancellationToken ct = default);
    Task<User>  RequireAsync(string id, CancellationToken ct = default);
    void Add(User entity); void AddRange(IEnumerable<User> entities); }
// src/Animora.Persistence.Sqlite/Repositories/IdentityRepository.cs  (impl pattern to copy)
public sealed class IdentityRepository(AnimoraLocalDbContext db) : IIdentityRepository {
    public Task<User?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<User>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
    public void Add(User entity) => db.Set<User>().Add(entity); /* ... */ }
```
`AnimoraLocalDbContext` default query behavior is `NoTracking`; use `.AsTracking()` for write paths.
Domain identity types (A03) already present: `StaffMembership` (with `StaffRole`), `Device`,
`RoleDefinition`, `DefaultRolePermissions` (role → permission codes), `Tenant.LocalDefaultId`.

### R6 · DI entry points (append your registrations here — do NOT restructure these files)
- `AddAnimoraApplication(this IServiceCollection, Action<ApplicationRegistrationBuilder>? configure)`
  in `src/Animora.Application/ApplicationServiceCollectionExtensions.cs`. It currently does
  `services.TryAddScoped<ICurrentUserProvider, SingleUserProvider>();` → **change to
  `SessionCurrentUserProvider`**. Register each new use case via the `builder` inside `configure`,
  or add lines next to the existing `builder.AddQuery<...>()` calls. Registration API:
  ```csharp
  builder.AddCommand<TCommand, TResult, THandler>();   // THandler : ICommandHandler<TCommand,TResult>
  builder.AddQuery<TQuery, TResult, THandler>();
  builder.AddValidator<T, TValidator>();               // TValidator : IValidator<T>
  ```
  (The identity use cases are *registered* here; the Desktop passes the `configure` lambda in B03c —
  for this session, register them in whatever way `AddAnimoraApplication` already exposes so the build
  resolves them; if a caller-supplied lambda is the only path, add a small internal helper the Desktop
  will call in B03c and document it.)
- `AddAnimoraLocalStore(...)` in
  `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` registers every
  repository as `services.AddScoped<IFooRepository, FooRepository>();` (a long list ending with
  `IIdentityRepository`). **Add here** the three new repositories (`IStaffMembershipRepository`,
  `IDeviceRepository`, `IRoleDefinitionRepository`).

### R7 · Use-case file shape (copy this exact layout — one file per use case)
```csharp
using Animora.Application.Abstractions; using Animora.Domain.Results;
namespace Animora.Application.UseCases.Identity;
public sealed record FooCommand(string Bar) : ICommand<FooResult>;
public sealed record FooResult(/* ... */);
public sealed class FooCommandHandler(/* injected ports */) : ICommandHandler<FooCommand, FooResult> {
    public async Task<Result<FooResult>> HandleAsync(FooCommand command, CommandContext context, CancellationToken ct) {
        // return Result<FooResult>.Fail(ErrorCodes.X) or .Ok(new FooResult(...));
    } }
```
Validators implement `IValidator<T>`:
`ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(T instance, CancellationToken ct)` and use
`Animora.Application.Validation.Rules` helpers (e.g. `Rules.Required(value, name)` → `ValidationFailure?`).
`StaffRole` enum + `DefaultRolePermissions` (A03) map a role to its permission codes.

---

## §Design decisions (pre-made — implement exactly, do not re-derive)

### D1 · Real `ICurrentUserProvider`
`SessionCurrentUserProvider` in `Animora.Application/Security/` replaces (deletes)
`SingleUserProvider`. Builds `CommandContext`/`QueryContext` from the active `LocalSession` (via
`ISessionService`) + `ILocalInstallationIdentity` for `TenantId`/`DeviceId`; **returns
`Result.Fail(ErrorCodes.SessionExpired)` when no session is active** — so no command can dispatch
without an authenticated session. Reuse the `SingleUserProvider` body shape from R3 (operation-id
minting, correlation fallback) but source `UserId` from the session.

### D2 · Application use cases (`src/Animora.Application/UseCases/Identity/`)
Each in its own file, shape per R7. `IJournaledCommand` is **not** applied to sign-in/session
commands (a sign-in is not business intent).
- `SignInCommand(string Username, string Password, bool RememberDevice)` → `SignInResult`
  ```csharp
  public sealed record SignInResult(string UserId, string DisplayName, StaffRole Role,
      IReadOnlyList<string> Permissions, bool MustChangePassword, DateTimeOffset ExpiresAtUtc);
  ```
  Handler order: load user **by username** (tenant-scoped, via `FindByUsernameAsync`) → if null or
  locked/disabled → (locked → `UserLockedOut`; disabled → `UserDisabled`; else) append
  `LoginAttemptLog` + `RegisterFailedAttempt` when the user exists + return `CredentialsInvalid`
  (never leak which) → `Verify` hash → on `Failed`: `RegisterFailedAttempt` + append log + return
  `CredentialsInvalid` → on `Success`/`SuccessRehashNeeded`: (rehash via `SetPasswordHash` if needed)
  `RegisterSuccessfulLogin`, `StartAsync`, append `LoginAttemptLog("OK")`, return permissions from
  `DefaultRolePermissions` for the membership's `StaffRole`. **On the 5th failure return
  `UserLockedOut`; the 6th must be rejected on the lockout check without a hash comparison.**
- `SignOutCommand(string Reason)` → `Unit` (use the existing unit type if any; otherwise return
  `Result` from the handler — check the codebase's convention only if the compiler complains).
- `LockSessionCommand()` → `Unit`; `UnlockSessionCommand(string Password)` → `SessionSnapshot`.
- `ChangePasswordCommand(string Current, string New)` → `Unit` (validate policy on `New`; verify
  `Current`; `SetPasswordHash`).
- `CompleteFirstRunSetupCommand(string ClinicName, string OwnerFullName, string Username, string Password)`
  → `SignInResult` (start the session on success). Runs **once**, guarded by `LocalInstallation`
  first-run state; a second call → `FirstRunAlreadyCompleted`. **It does NOT create a `Tenant`** — the
  tenant/roles/permissions are already seeded by `LocalSeeder` during `LocalStoreStartup` (block key
  `identity.tenant`, `Tenant.LocalDefaultId`). It creates: the owner `User` (via `User.Create` +
  `SetPasswordHash`), its `StaffMembership(StaffRole.Manager)`, and the `Device` row — inside
  `Tenant.LocalDefaultId`.
- `CurrentSessionQuery` → `SessionSnapshot`.

### D3 · New repositories (patterns per R5) + `FindByUsernameAsync`
Add to `IIdentityRepository` (+ impl):
`Task<User?> FindByUsernameAsync(string tenantId, string userName, CancellationToken ct = default)`
→ `db.Set<User>().AsTracking().FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserName ==
userName.Trim().ToLowerInvariant(), ct)`. Add three new repositories with the same shape and register
each in `AddAnimoraLocalStore`: `IStaffMembershipRepository`/`StaffMembershipRepository`,
`IDeviceRepository`/`DeviceRepository`, `IRoleDefinitionRepository`/`RoleDefinitionRepository`
(each `FindAsync`/`Add`; add only what the handlers actually call).

### D4 · Provider swap in application DI
Update `AddAnimoraApplication` (R6) so `ICurrentUserProvider` resolves to `SessionCurrentUserProvider`
(replacing the `SingleUserProvider` line) and the 7 use cases + their validators are registered.
Delete `SingleUserProvider.cs`.

---

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Ports/IIdentityRepository.cs` | modify | + `FindByUsernameAsync` |
| `src/Animora.Application/Ports/IStaffMembershipRepository.cs`, `IDeviceRepository.cs`, `IRoleDefinitionRepository.cs` | create | ports (D3) |
| `src/Animora.Persistence.Sqlite/Repositories/IdentityRepository.cs` | modify | + `FindByUsernameAsync` impl |
| `src/Animora.Persistence.Sqlite/Repositories/StaffMembershipRepository.cs`, `DeviceRepository.cs`, `RoleDefinitionRepository.cs` | create | impls (D3) |
| `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` | modify | register 3 new repos |
| `src/Animora.Application/Security/SessionCurrentUserProvider.cs` | create | real provider (D1) |
| `src/Animora.Application/Security/SingleUserProvider.cs` | delete | replaced |
| `src/Animora.Application/UseCases/Identity/*.cs` | create | 7 commands/queries + handlers + validators (D2) |
| `src/Animora.Application/ApplicationServiceCollectionExtensions.cs` | modify | `SessionCurrentUserProvider` + register use cases (D4) |
| `backend-roadmap/PROGRESS.md` | modify | mark B03b done, next → B03c, repo-state entry, deviations |

---

## §Implementation plan (do these in order)
1. **Repositories** (D3): `FindByUsernameAsync` on `IIdentityRepository` + impl; 3 new port/impl pairs;
   register the 3 new repos in `AddAnimoraLocalStore`.
2. **Provider** (D1): create `SessionCurrentUserProvider`; delete `SingleUserProvider`.
3. **Use cases** (D2) with validators; wire `SignInCommandHandler` order **exactly** as specified.
4. **Application DI** (D4): `AddAnimoraApplication` → `SessionCurrentUserProvider` + register the 7
   use cases and validators.
5. **Gate check:** `scripts/sandbox/build.sh full` = 0/0. (No `test.sh`/`run.sh` this session.)
6. **PROGRESS.md** update + commit on `genspark_ai_developer`.

---

## Contracts / schema added
- Application: 7 use cases (`SignInCommand`, `SignOutCommand`, `LockSessionCommand`,
  `UnlockSessionCommand`, `ChangePasswordCommand`, `CompleteFirstRunSetupCommand`,
  `CurrentSessionQuery`) + handlers + validators; `SessionCurrentUserProvider` (deletes
  `SingleUserProvider`).
- Ports/impl: `FindByUsernameAsync` on `IIdentityRepository`; 3 new repositories
  (`IStaffMembershipRepository`, `IDeviceRepository`, `IRoleDefinitionRepository`).
- DI: `ICurrentUserProvider` → `SessionCurrentUserProvider`; the 7 use cases registered; 3 repos
  registered. No new error code, no migration, no mock deleted this session.

## Rules that apply here
- **CONVENTIONS §7** — failed sign-in never reveals whether a user exists; a failed sign-in always
  returns `CredentialsInvalid`.
- **A-RULE 023** — authentication only; the `Permissions` in `SignInResult` are data, not an
  authorization decision (B04 evaluates them).
- **A-RULE 029** — session policy-driven with explicit expiry; no silent extension.
- **PERFORMANCE.md §4/§12** — sign-in fully async.

## Out of scope (owned by the sibling sessions)
- Package/domain/tables/hashing/session infra → **B03a** (already done).
- `Animora.Contracts` auth seam, `Client.Services` (`AuthenticationService`,
  `CurrentUserContextService`, `RoleMapping`), Desktop (`LoginViewModel`, `FirstRunView`/VM, DI
  wiring, mock deletion), seeding change, all tests, `run.sh` gate → **B03c**.

## Acceptance criteria
- `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `src/Animora.Application/Security/SingleUserProvider.cs` does not exist;
  `SessionCurrentUserProvider.cs` does.
- `grep -rn "FindByUsernameAsync" src/Animora.Application src/Animora.Persistence.Sqlite` shows the
  port and its implementation.
- All 7 use-case files exist under `src/Animora.Application/UseCases/Identity/` and `AddAnimoraApplication`
  references `SessionCurrentUserProvider`.

## PROGRESS.md update
Mark B03b done; advance "Next phase" to `B03c`; append a "Repo state" entry listing: the 7 use cases,
`SessionCurrentUserProvider` (replacing `SingleUserProvider`), the 3 new repositories +
`FindByUsernameAsync`, the application-DI changes, and any deviation with its justification.
