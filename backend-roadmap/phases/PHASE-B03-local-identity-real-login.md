# Phase B03 — Local identity & real login

**Band:** M · **Track:** B · **Depends on:** B02 · **Satisfies:** R-B04, R-B09
**Runnable after this phase:** desktop offline yes · server required no

---

## ⚡ Zero-exploration contract (read this first — it changes how you work)

> **This phase file is self-contained and pre-verified against the current tree.** Every existing
> type, signature, constant, file path and command you need is reproduced verbatim in the
> **§Reference pack** below. **Do not re-open the repository to "understand" it, and do not
> re-derive anything.** Implement top-to-bottom from §Implementation plan. The only reads you may
> need are the exact files listed under **§Files to modify** (because you are editing them), and
> only when you edit them.
>
> If — and only if — a compile error proves a snippet here diverges from the tree, fix the smallest
> thing and record it in the `PROGRESS.md` deviation log. Do **not** pre-emptively audit.
>
> **Method names in this file are the REAL ones already in the code.** Where an older draft proposed
> a different name (e.g. `RecordFailedLogin`), the real member is used and the alias is noted inline.
> There is no naming ambiguity left to resolve by reading source.

### Environment (already true in the sandbox — do not probe these)
- **Run `scripts/sandbox/install-prereqs.sh` once at the start** (idempotent): it installs the SDK,
  restores packages, and installs **`dotnet-ef 10.0.12` to `.sandbox/tools`** (per PROGRESS.md/A10a).
  There is **no** `.config/dotnet-tools.json`; the tool binary is `.sandbox/tools/dotnet-ef`. Do
  **not** `dotnet tool install` it yourself and do **not** hunt for it — after install-prereqs it is
  at that path. If a later invocation reports it missing, that only means install-prereqs has not run
  yet; run it, then retry. Add migrations with the exact command in step 2 below.
- Build/test/run are **only** these three scripts (memory-capped; do not invoke raw `dotnet build`):
  - `scripts/sandbox/build.sh full` → expect `0 warnings / 0 errors`
  - `scripts/sandbox/test.sh` → expect `TESTS OK`
  - `scripts/sandbox/run.sh` → expect `SMOKE RUN OK`
- Sandbox RAM ≈ 985 MiB. Do not add parallel/server-GC flags.

### Rules to obey (condensed — do not open `CONVENTIONS.md` for these)
- **§2 layering:** `Animora.Domain` references nothing; `Animora.Application` references only Domain;
  `Persistence.Sqlite`/`Infrastructure.Local` may reference Application+Domain; `Animora.Contracts`
  references no `Animora.*`; `Animora.Client.Services` references Application+Contracts+Persistence+
  Infrastructure. **`Microsoft.AspNetCore.Identity` may be referenced by `Infrastructure.Local` ONLY.**
- **§4 style:** `sealed` classes; `sealed record` for DTOs/VOs; nullable honored (no `!` without a
  reason comment); async-all-the-way with `CancellationToken ct = default`; library projects use
  `ConfigureAwait(false)` (but `Animora.Client.Services` methods returning to a ViewModel do **not**);
  no exceptions for expected flow — return `Result`/`Result<T>` with an `ErrorCode`; `IClock` injected,
  never `DateTimeOffset.UtcNow`; Ids via `IdGenerator.NewId()` (ULID), never `Guid`.
- **§7 security:** passwords use ASP.NET Core Identity `PasswordHasher<T>` primitives only — no custom
  crypto; security stamp rotates on every credential change; a failed sign-in never reveals whether a
  user exists.
- **§9 testing:** every phase adds tests for what it introduces; no test asserts a Persian UI string;
  no test uses wall-clock time (inject `IClock`).
- **§11 runnability / §12 DoD:** `build.sh full` 0/0, `test.sh` green, `run.sh` OK; no forbidden dep;
  `PROGRESS.md` updated (done + next phase + repo-state entry + deviations); commit on branch
  `genspark_ai_developer`.

---

## Goal
Replace the hardcoded `admin`/`admin` gate with a real local credential store: hashed passwords,
lockout, a first-run owner-setup path, a durable session with app-lock, and a real
`ICurrentUserContext`/`ICurrentUserProvider` so every command from now on carries a genuine `UserId`
and `DeviceId`.

---

## §Reference pack — existing code you build on (verbatim; do not re-read source)

### R1 · `User` aggregate (`src/Animora.Domain/Identity/User.cs`) — ALREADY has the credential machinery
`sealed class User : AggregateRoot`. **Members that already exist (use these; do NOT add duplicates):**
```
Constants:  const int MaxFailedAttempts = 5;
            static readonly TimeSpan LockoutDuration      = TimeSpan.FromMinutes(15);
            static readonly TimeSpan DefaultFailureWindow = TimeSpan.FromMinutes(15);
Props:      string  UserName {get;private set;}          // lowercased on Create
            string  DisplayName {get;private set;}
            string? PasswordHash {get;private set;}       // null until a credential is set  ← already the exact name
            string  SecurityStamp {get;private set;}      // ULID
            UserStatus Status {get;private set;}          // enum Active=0, Disabled=1, Locked=2
            DateTimeOffset? LastLoginUtc {get;private set;}
            int     FailedAttemptCount {get;private set;}
            DateTimeOffset? LockoutEndUtc {get;private set;}
            (private DateTimeOffset? _firstFailureUtc)
Factory:    static User Create(string tenantId, string userName, string displayName, IClock clock)
Methods:    bool RegisterFailedAttempt(IClock clock, int threshold=MaxFailedAttempts, TimeSpan? window=null)
                 → returns true when THIS attempt triggered the lockout (this IS "RecordFailedLogin")
            void ClearFailures()
            bool IsLockedOut(DateTimeOffset nowUtc)
            void RegisterSuccessfulLogin(IClock clock)   // calls ClearFailures (this IS "RecordSuccessfulLogin")
            void RotateSecurityStamp()
            void SetPasswordHash(string passwordHash)     // rotates the stamp already (this IS "SetPassword")
            void Disable()
            void Enable()                                 // clears lockout/failures (this IS "Unlock")
```
**Only genuinely missing → ADD in this phase:** `bool MustChangePassword {get; private set;}` + a
setter method `void RequirePasswordChange()` and clear it inside `SetPasswordHash`. **Add nothing
else to `User`.** Do not add `FailedAccessCount`/`RecordFailedLogin`/`SetPassword`/`Unlock` — they
are the members above under different names.

### R2 · Application abstractions (`src/Animora.Application/Abstractions/`)
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

### R3 · `Result` / `ErrorCode` (`src/Animora.Domain/Results/`)
```csharp
public readonly record struct Result {                       // and Result<T> with the same shape
    bool IsSuccess; bool IsFailure; ErrorCode Error; string? Detail;
    static Result Ok();                       static Result Fail(ErrorCode error, string? detail = null);
    // Result<T>:  static Result<T> Ok(T value);  static Result<T> Fail(ErrorCode error, string? detail = null);
    //             T Value; T OrElse(T fallback); Bind(...) }
}
public readonly record struct ErrorCode(string Code, string Category) {
    // Category constants: Validation, NotFound, Conflict, Forbidden, Integrity, Infrastructure, Entitlement
}
```
`ErrorCodes` is the central catalog (`src/Animora.Domain/Results/ErrorCodes.cs`), grouped by region.
**Already declared (REUSE — do not duplicate):** `ValidationFailed`, `NotFound`, `StoreUnavailable`,
`Unexpected`, and in the *Identity (Phase A03)* region: `TenantSuspended`, `UserDisabled`
(`USER_DISABLED`), `UserLockedOut` (`USER_LOCKED_OUT`), `MembershipNotActive`, `DeviceRevoked`,
`CapabilityNotGranted`. Pattern for a new code:
`public static readonly ErrorCode X = new("SCREAMING_SNAKE", ErrorCode.Category);`

### R4 · Security seam (`src/Animora.Application/Security/`)
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
`AnimoraLocalDbContext` is the local DbContext; repositories take it via primary constructor and use
`db.Set<T>()`. Default query behavior is `NoTracking`; use `.AsTracking()` for write paths.

### R6 · EF configuration + migration patterns
```csharp
// Config pattern (src/Animora.Persistence.Sqlite/Configurations/Identity/UserConfiguration.cs — EXISTS)
public sealed class UserConfiguration : IEntityTypeConfiguration<User> {
    public void Configure(EntityTypeBuilder<User> builder) {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.UserName })            // ← already unique; do not re-add
            .HasDatabaseName("IX_Users_TenantId_Username").IsUnique(); } }
```
Existing migrations live in `src/Animora.Persistence.Sqlite/Migrations/` and are numbered:
`001_LocalCoreSchema`, `002_LocalBusinessSchema`, `003_LocalHardening`, `004_CommandPipeline` (+
`AnimoraLocalDbContextModelSnapshot.cs`). **Your migration is `005_LocalIdentity`.** SQLite column
types used by convention: `TEXT` for strings/ids (`maxLength` set), `INTEGER` for `long`/`bool`/enum,
and `DateTimeOffset` is stored as a `long` epoch column (see `004`: `CreatedAtUtc = table.Column<long>`).
`AppendOnlyGuard` in `src/Animora.Persistence.Sqlite/Context/AppendOnlyGuard.cs` has a
`ProtectedTypes` set — append the new `LoginAttemptLog` type to it.

### R7 · DI entry points (append your registrations here — do NOT restructure these files)
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
- `AddAnimoraLocalStore(...)` in
  `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` registers every
  repository as `services.AddScoped<IFooRepository, FooRepository>();` (there is a long list ending
  with `IIdentityRepository`). **Add here** the three new repositories
  (`IStaffMembershipRepository`, `IDeviceRepository`, `IRoleDefinitionRepository`).
- `AddAnimoraLocalInfrastructure(...)` in
  `src/Animora.Infrastructure.Local/LocalInfrastructureServiceCollectionExtensions.cs` calls
  `AddAnimoraLocalStore` first, then registers `IClock`/`IMonotonicClock`, the secret store,
  `ILocalInstallationIdentity → LocalInstallationIdentity`, `ILocalStoreInitializer →
  LocalStoreStartup`. **Add here** `IPasswordHasher → PasswordHashingService` and
  `ISessionService → LocalSessionService` + `LocalSessionOptions`. `AddAnimoraLocalInfrastructure`
  is the entry point the Desktop must call (it wires the whole stack incl. `LocalStoreStartup`).

### R8 · Use-case file shape (copy this exact layout — one file per use case)
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

### R9 · Desktop composition root reality
`src/Animora.Desktop/Animora.Desktop.csproj` today references **only `Animora.Contracts`** — this
phase adds `<ProjectReference Include="..\Animora.Client.Services\Animora.Client.Services.csproj" />`
(that one reference pulls in Application + Persistence + Infrastructure transitively).
`App.axaml.cs` `ConfigureServices()` is a `ServiceCollection` builder ending in
`BuildServiceProvider()`; it currently has `services.AddSingleton<ICurrentUserContext,
MockCurrentUserContext>();` and `services.AddTransient<LoginViewModel>();`. `LoginViewModel` today is
front-end-only with `DemoUser`/`DemoPassword`/`TryAuthenticate` and raises `event EventHandler
LoginSucceeded`. `IDialogService` exposes `ShowModal(object content, double maxWidth)`, `ShowDrawer`,
`ShowSheet`, `SaveTextFileAsync`, `Close` — **there is no generic input dialog**, so first-run is a
net-new `FirstRunView`/`FirstRunViewModel` shown via `ShowModal(...)`.
`Animora.Contracts/Navigation/UserRole.cs` enum members: `Manager, Receptionist, Cashier, Doctor`
(no `Accountant`).

---

## §Design decisions (pre-made — implement exactly, do not re-derive)

### D1 · Package pin
Add `Microsoft.AspNetCore.Identity` version **`10.0.12`** to `Directory.Packages.props` (a
`<PackageVersion Include="..." Version="10.0.12" />` line in the existing `<ItemGroup>`). Reference it
**only** from `Animora.Infrastructure.Local.csproj` (`<PackageReference Include=
"Microsoft.AspNetCore.Identity" />`, no version — versions are central). Add a `LayeringRulesTests`
fact asserting no other project references it.

### D2 · Domain (`User` only — see R1)
Add `MustChangePassword` + `RequirePasswordChange()`; clear the flag inside `SetPasswordHash`.
Lockout/hash/stamp are already present. **No new aggregate, no other new member.**

### D3 · Local tables (`[LocalOnly]`, migration `005_LocalIdentity`)
- **`LocalSession`** entity — one active row at a time; survives a crash. Columns:
  `Id` (ULID), `UserId`, `DeviceId`, `StartedAtUtc`, `LastActivityUtc`, `ExpiresAtUtc`,
  `IsLocked` (bool), `EndedAtUtc?`, `EndReason?` (string).
- **`LoginAttemptLog`** entity — append-only. Columns: `Id` (ULID), `UsernameAttempted`,
  `Outcome` (string: `"OK"` or the error code), `OccurredAtUtc`, `DeviceId`. Add its type to
  `AppendOnlyGuard.ProtectedTypes`.
- Datetimes are stored as `long` epoch (converter convention already in the model); model both as
  `DateTimeOffset`/`DateTimeOffset?` on the entity and let the existing convention convert.

### D4 · Password policy & hashing
- **`PasswordPolicy.Validate(string password) → Result`** in `Animora.Application/Security/`.
  Fixed policy: **≥ 8 chars, at least one letter and one digit.** Nothing else. Fail →
  `Result.Fail(ErrorCodes.PasswordPolicyViolation)`.
- **`IPasswordHasher`** port in `Animora.Application/Security/`:
  ```csharp
  public interface IPasswordHasher {
      string Hash(string password);
      PasswordVerificationOutcome Verify(string hash, string password); }
  public enum PasswordVerificationOutcome { Failed, Success, SuccessRehashNeeded }
  ```
- **`PasswordHashingService`** in `Infrastructure.Local/Security/` implements it over
  `PasswordHasher<UserHashSubject>` with `PasswordHasherOptions { CompatibilityMode =
  PasswordHasherCompatibilityMode.IdentityV3, IterationCount = 210_000 }`. `UserHashSubject` is a
  trivial private empty class (the hasher's generic subject is unused). Map
  `PasswordVerificationResult.{Failed→Failed, Success→Success, SuccessRehashNeeded→SuccessRehashNeeded}`.
  A `SuccessRehashNeeded` verify silently re-hashes into the same write transaction.

### D5 · Session service & options
- **`ISessionService`** in `Animora.Application/Security/` + `SessionSnapshot`:
  ```csharp
  public sealed record SessionSnapshot(string? UserId, string? DisplayName, StaffRole? Role,
      bool IsLocked, bool IsFirstRun, DateTimeOffset? ExpiresAtUtc);
  public interface ISessionService {
      Task<Result<SessionSnapshot>> RestoreAsync(CancellationToken ct);   // expired → end with SESSION_EXPIRED
      Task<Result<SessionSnapshot>> StartAsync(string userId, string deviceId, CancellationToken ct);
      Task<Result> TouchAsync(CancellationToken ct);                      // throttled: ≤ 1 write / 60 s
      Task<Result> LockAsync(CancellationToken ct);
      Task<Result<SessionSnapshot>> UnlockAsync(string password, CancellationToken ct);
      Task<Result> EndAsync(string reason, CancellationToken ct); }
  ```
- **`LocalSessionService`** in `Infrastructure.Local/Security/` implements it over the `LocalSession`
  table + `IClock`. **`LocalSessionOptions`** in `Infrastructure.Local/Options/`:
  `IdleTimeout = TimeSpan.FromMinutes(30)` (idle → **lock**, not sign-out),
  `AbsoluteLifetime = TimeSpan.FromHours(12)` (→ sign-out with `SESSION_EXPIRED`).
  `RememberDevice` sets the device-trust flag only; it never extends session lifetime.

### D6 · Real `ICurrentUserProvider`
`SessionCurrentUserProvider` in `Animora.Application/Security/` replaces (deletes)
`SingleUserProvider`. Builds `CommandContext`/`QueryContext` from the active `LocalSession` (via
`ISessionService`) + `ILocalInstallationIdentity` for `TenantId`/`DeviceId`; **returns
`Result.Fail(ErrorCodes.SessionExpired)` when no session is active** — so no command can dispatch
without an authenticated session. Reuse the `SingleUserProvider` body shape from R4 (operation-id
minting, correlation fallback) but source `UserId` from the session.

### D7 · Error codes (add exactly 4 — see R3 for the pattern & the reuse list)
Add to the Identity region of `ErrorCodes.cs`:
`CredentialsInvalid` (`CREDENTIALS_INVALID`, Forbidden), `SessionExpired` (`SESSION_EXPIRED`,
Forbidden), `PasswordPolicyViolation` (`PASSWORD_POLICY_VIOLATION`, Validation),
`FirstRunAlreadyCompleted` (`FIRST_RUN_ALREADY_COMPLETED`, Conflict).
**Reuse `UserLockedOut` / `UserDisabled`** for the lock/disable cases (do NOT add
`ACCOUNT_LOCKED`/`ACCOUNT_DISABLED` — they would be semantic duplicates). Record this substitution in
the `PROGRESS.md` deviation log. **A failed sign-in always returns `CredentialsInvalid`** — never
distinguish "no such user" from "wrong password" (no user enumeration).

### D8 · Application use cases (`src/Animora.Application/UseCases/Identity/`)
Each in its own file, shape per R8. `IJournaledCommand` is **not** applied to sign-in/session
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
  → `SignInResult` (or `Unit` + auto sign-in — pick `SignInResult` and start the session). Runs
  **once**, guarded by `LocalInstallation` first-run state; a second call → `FirstRunAlreadyCompleted`.
  **It does NOT create a `Tenant`** — the tenant/roles/permissions are already seeded by `LocalSeeder`
  during `LocalStoreStartup` (block key `identity.tenant`, `Tenant.LocalDefaultId`). It creates:
  the owner `User` (via `User.Create` + `SetPasswordHash`), its `StaffMembership(StaffRole.Manager)`,
  and the `Device` row — inside `Tenant.LocalDefaultId`.
- `CurrentSessionQuery` → `SessionSnapshot`.

### D9 · New repositories (patterns per R5) + `FindByUsernameAsync`
Add to `IIdentityRepository` (+ impl):
`Task<User?> FindByUsernameAsync(string tenantId, string userName, CancellationToken ct = default)`
→ `db.Set<User>().AsTracking().FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserName ==
userName.Trim().ToLowerInvariant(), ct)`. Add three new repositories with the same shape and register
each in `AddAnimoraLocalStore`: `IStaffMembershipRepository`/`StaffMembershipRepository`,
`IDeviceRepository`/`DeviceRepository`, `IRoleDefinitionRepository`/`RoleDefinitionRepository`
(each `FindAsync`/`Add`; add only what the handlers actually call).

### D10 · `Animora.Contracts` additions (auth seam — additions only, no reshape)
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

### D11 · `Animora.Client.Services` implementations
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

### D12 · Desktop changes (minimal, exhaustive)
1. `Animora.Desktop.csproj`: add the `Animora.Client.Services` project reference (R9).
2. `App.axaml.cs` `ConfigureServices()`:
   - Call `services.AddAnimoraLocalInfrastructure();` then `services.AddAnimoraApplication(builder =>
     { /* register the identity use cases */ });`.
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

### D13 · Seeding
`LocalSeeder` keeps seeding tenant/roles/permissions; it must **not** seed a demo user
(`CompleteFirstRunSetupCommand` is the only user-creation path). If A10 left a placeholder user,
remove it and note it in `PROGRESS.md`.

---

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `Directory.Packages.props` | modify | pin `Microsoft.AspNetCore.Identity 10.0.12` |
| `src/Animora.Domain/Identity/User.cs` | modify | add `MustChangePassword` + `RequirePasswordChange()`; clear in `SetPasswordHash` |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | +4 codes (D7); reuse `UserLockedOut`/`UserDisabled` |
| `src/Animora.Persistence.Sqlite/Entities/LocalSession.cs` | create | durable session row (D3) |
| `src/Animora.Persistence.Sqlite/Entities/LoginAttemptLog.cs` | create | append-only attempt log (D3) |
| `src/Animora.Persistence.Sqlite/Configurations/Identity/LocalSessionConfiguration.cs` | create | key + one-active index |
| `src/Animora.Persistence.Sqlite/Configurations/Identity/LoginAttemptLogConfiguration.cs` | create | key + index |
| `src/Animora.Persistence.Sqlite/Context/AppendOnlyGuard.cs` | modify | add `LoginAttemptLog` to `ProtectedTypes` |
| `src/Animora.Persistence.Sqlite/Migrations/*005_LocalIdentity*` | create | via `dotnet-ef` (step 2) — 2 tables + `MustChangePassword` column + snapshot |
| `src/Animora.Application/Ports/IIdentityRepository.cs` | modify | + `FindByUsernameAsync` |
| `src/Animora.Application/Ports/IStaffMembershipRepository.cs`, `IDeviceRepository.cs`, `IRoleDefinitionRepository.cs` | create | ports (D9) |
| `src/Animora.Persistence.Sqlite/Repositories/IdentityRepository.cs` | modify | + `FindByUsernameAsync` impl |
| `src/Animora.Persistence.Sqlite/Repositories/StaffMembershipRepository.cs`, `DeviceRepository.cs`, `RoleDefinitionRepository.cs` | create | impls (D9) |
| `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` | modify | register 3 new repos |
| `src/Animora.Application/Security/IPasswordHasher.cs` | create | port + outcome enum (D4) |
| `src/Animora.Application/Security/PasswordPolicy.cs` | create | fixed policy (D4) |
| `src/Animora.Application/Security/ISessionService.cs` | create | port + `SessionSnapshot` (D5) |
| `src/Animora.Application/Security/SessionCurrentUserProvider.cs` | create | real provider (D6) |
| `src/Animora.Application/Security/SingleUserProvider.cs` | delete | replaced |
| `src/Animora.Application/ApplicationServiceCollectionExtensions.cs` | modify | `SessionCurrentUserProvider` + register use cases |
| `src/Animora.Application/UseCases/Identity/*.cs` | create | 7 commands/queries + handlers + validators (D8) |
| `src/Animora.Infrastructure.Local/Security/PasswordHashingService.cs` | create | Identity hasher wrapper (D4) |
| `src/Animora.Infrastructure.Local/Security/LocalSessionService.cs` | create | restore/start/touch/lock/unlock/end (D5) |
| `src/Animora.Infrastructure.Local/Options/LocalSessionOptions.cs` | create | idle 30 min, absolute 12 h |
| `src/Animora.Infrastructure.Local/Animora.Infrastructure.Local.csproj` | modify | + `Microsoft.AspNetCore.Identity` package ref |
| `src/Animora.Infrastructure.Local/LocalInfrastructureServiceCollectionExtensions.cs` | modify | register hasher + session service + options |
| `src/Animora.Contracts/Identity/IAuthenticationService.cs` | create | auth seam (D10) |
| `src/Animora.Contracts/Identity/AuthModels.cs` | create | `AuthOutcome`, `FirstRunRequest` |
| `src/Animora.Client.Services/Identity/AuthenticationService.cs` | create | impl over `IDispatcher` (D11) |
| `src/Animora.Client.Services/Identity/CurrentUserContextService.cs` | create | real `ICurrentUserContext` |
| `src/Animora.Client.Services/Mapping/RoleMapping.cs` | create | `StaffRole` ↔ `UserRole` |
| `src/Animora.Desktop/Animora.Desktop.csproj` | modify | reference `Animora.Client.Services` |
| `src/Animora.Desktop/App.axaml.cs` | modify | local infra + application DI; swap `ICurrentUserContext`; register auth; start `LocalStoreStartup` off first frame |
| `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` | modify | delete demo creds; async sign-in; code→Persian map; first-run flow |
| `src/Animora.Desktop/Pages/Login/FirstRunView.axaml` (+`.axaml.cs`) | create | first-run form |
| `src/Animora.Desktop/Pages/Login/FirstRunViewModel.cs` | create | first-run VM |
| `src/Animora.Desktop/Mocks/MockCurrentUserContext.cs` | delete | replaced |
| `tests/Animora.Application.Tests/SignInTests.cs` | create | success, wrong password, unknown user, lockout, disabled, enumeration-safety |
| `tests/Animora.Application.Tests/FirstRunSetupTests.cs` | create | creates user/membership/device once only |
| `tests/Animora.Application.Tests/SessionLifecycleTests.cs` | create | idle lock, absolute expiry, restore after crash, lock/unlock |
| `tests/Animora.Application.Tests/PasswordPolicyTests.cs` | create | policy + rehash-on-verify upgrade |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | modify | fact: only `Infrastructure.Local` references `Microsoft.AspNetCore.Identity` |
| `backend-roadmap/PROGRESS.md` | modify | mark B03 done, next → B04, repo-state entry, deviations |

---

## §Implementation plan (do these in order; commands are exact)

1. **Package + Domain + error codes.** Pin `Microsoft.AspNetCore.Identity 10.0.12` (D1); add the
   `User.MustChangePassword` member (D2); add the 4 error codes (D7). Reference the package from
   `Animora.Infrastructure.Local.csproj` only.
2. **Local tables + migration.** Add `LocalSession`/`LoginAttemptLog` entities + configurations;
   append `LoginAttemptLog` to `AppendOnlyGuard.ProtectedTypes`. Then generate the migration with the
   installed tool (no tool install needed):
   ```bash
   cd /home/user/webapp && .sandbox/tools/dotnet-ef migrations add 005_LocalIdentity \
     --project src/Animora.Persistence.Sqlite --startup-project src/Animora.Persistence.Sqlite \
     --context AnimoraLocalDbContext
   ```
   If `.sandbox/tools/dotnet-ef` is absent, run `scripts/sandbox/install-prereqs.sh` once, then retry.
   Verify: `.sandbox/tools/dotnet-ef migrations list --project src/Animora.Persistence.Sqlite
   --startup-project src/Animora.Persistence.Sqlite --context AnimoraLocalDbContext` shows `005`.
3. **Hashing + policy** (D4): `IPasswordHasher`+`PasswordPolicy` (Application), `PasswordHashingService`
   (Infrastructure.Local).
4. **Session** (D5): `ISessionService`/`SessionSnapshot`, `LocalSessionService`, `LocalSessionOptions`.
5. **Use cases** (D8) with validators; wire `SignInCommandHandler` order exactly as specified.
6. **Provider swap** (D6): create `SessionCurrentUserProvider`, delete `SingleUserProvider`, update
   `AddAnimoraApplication` (`SessionCurrentUserProvider` + register the use cases).
7. **Repositories** (D9): `FindByUsernameAsync` + 3 new repos + their DI lines.
8. **Contracts + Client.Services** (D10, D11): auth seam + implementations + role mapping.
9. **Desktop** (D12): project reference; DI wiring; `LocalStoreStartup` off first frame; rewrite
   `LoginViewModel`; add `FirstRunView`/VM; delete `MockCurrentUserContext.cs`.
10. **Seeding** (D13): ensure no demo user is seeded.
11. **Tests**: the four Application test classes + the `LayeringRulesTests` fact.
12. **Gate checks:** `scripts/sandbox/build.sh full` = 0/0 · `scripts/sandbox/test.sh` = `TESTS OK` ·
    `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
13. **PROGRESS.md** update + commit on `genspark_ai_developer`.

---

## Contracts / schema added
- `Animora.Contracts` additions: `IAuthenticationService`, `AuthOutcome`, `FirstRunRequest`.
- Domain: `User.MustChangePassword` (+ method). Local tables `LocalSessions`, `LoginAttemptLogs`
  (migration `005_LocalIdentity`).
- Ports/impl: `IPasswordHasher`, `PasswordPolicy`, `ISessionService`/`LocalSessionService`,
  `PasswordHashingService`, `SessionCurrentUserProvider`, `LocalSessionOptions`, +3 repositories.
- Use cases: `SignInCommand`, `SignOutCommand`, `LockSessionCommand`, `UnlockSessionCommand`,
  `ChangePasswordCommand`, `CompleteFirstRunSetupCommand`, `CurrentSessionQuery`.
- Client services: `AuthenticationService`, `CurrentUserContextService`, `RoleMapping`.
- DI: `ICurrentUserContext` → `CurrentUserContextService`; `IAuthenticationService` →
  `AuthenticationService`; local infra + application registered. Mock deleted:
  `MockCurrentUserContext.cs`. `ErrorCodes` += 4 (not 6 — `UserLockedOut`/`UserDisabled` reused).

## Rules that apply here
- **CONVENTIONS §7** — Identity primitives only; no custom crypto; stamp rotates on every credential
  change; failures never reveal whether a user exists.
- **A-RULE 023** — authentication only; permissions in `SignInResult` are data, not an authorization
  decision (B04 evaluates them).
- **A-RULE 029** — the session is policy-driven with explicit expiry; no silent extension.
- **CONVENTIONS §10 items 4–6** — DI swaps are one line per service; `LoginWindow.axaml` unchanged;
  `LoginViewModel` is the only ViewModel touched (plus the net-new `FirstRunViewModel`).
- **PERFORMANCE.md §4/§12** — sign-in fully async; `LocalStoreStartup` never blocks first frame;
  `TouchAsync` throttled (≤ 1 write / 60 s).
- **R-B09** — the session row is durable, so a crash cannot leave a half-authenticated state.

## Out of scope
- Permission evaluation, screen/action gating, capability snapshots → B04.
- Server-side identity, bearer/refresh tokens, device revocation → D03, E07.
- Role management UI → C10 (Profile) consumes B04's catalog.
- Password reset by an operator → D03/E01.
- Any other mock deletion → the owning Track C phase.

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
Mark B03 done; advance "Next phase" to `B04`; append a "Repo state" entry listing: the Identity
package pin, `User.MustChangePassword`, `LocalSessions`/`LoginAttemptLogs` + migration
`005_LocalIdentity`, the hashing/session ports and implementations, the 7 use cases, the 3 new
repositories + `FindByUsernameAsync`, the `Animora.Contracts` additions, the `Client.Services`
identity services, the DI changes (local infra + application + 2 service lines), the `LoginViewModel`
rewrite + `FirstRunView`/VM, **mock deleted: `MockCurrentUserContext.cs`**, `ErrorCodes` += 4 (with
the `UserLockedOut`/`UserDisabled` reuse deviation), the 4 Application test classes + the
`LayeringRulesTests` fact, and any deviation with its justification.
