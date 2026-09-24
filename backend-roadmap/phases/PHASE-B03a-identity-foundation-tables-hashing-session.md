# Phase B03a — Identity foundation: package, domain, tables, hashing & session (session 1/3 of B03)

**Band:** S · **Track:** B · **Depends on:** B02 ✅ · **Next:** B03b · Split of original B03 (B03a→B03c, one session each).
**Satisfies (with B03b+B03c):** R-B04, R-B09 · **Runnable after this phase:** desktop offline yes · server required no

---

## ⚡ Zero-exploration contract (read this first — it changes how you work)

> **This file is self-contained and pre-verified against the current tree.** Every existing
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

**This is session 1 of 3.** It lands the *foundation only*: the Identity package pin, the `User`
domain addition, the 4 error codes, the two local tables + migration `005_LocalIdentity`, and the
password-hashing + session-service infrastructure. **No use cases, no provider swap, no Desktop, no
tests** — those are B03b (application layer) and B03c (contracts/desktop/tests + gate). Do not build
ahead of this scope.

### Environment (already true in the sandbox — do not probe these)
- **Run `scripts/sandbox/install-prereqs.sh` once at the start** (idempotent): it installs the SDK,
  restores packages, and installs **`dotnet-ef 10.0.12` to `.sandbox/tools`** (per PROGRESS.md/A10a).
  There is **no** `.config/dotnet-tools.json`; the tool binary is `.sandbox/tools/dotnet-ef`. Do
  **not** `dotnet tool install` it yourself and do **not** hunt for it — after install-prereqs it is
  at that path. If a later invocation reports it missing, that only means install-prereqs has not run
  yet; run it, then retry. Add the migration with the exact command in step 2 below.
- Build/run are **only** these scripts (memory-capped; do not invoke raw `dotnet build`):
  - `scripts/sandbox/build.sh full` → expect `0 warnings / 0 errors`
  - `scripts/sandbox/run.sh` → expect `SMOKE RUN OK` (only after Desktop is wired — that is B03c;
    in this session confirm the build only)
- **No `test.sh` in this session** — tests are authored in B03c.
- Sandbox RAM ≈ 985 MiB. Do not add parallel/server-GC flags.

### Rules to obey (condensed — do not open `CONVENTIONS.md` for these)
- **§2 layering:** `Animora.Domain` references nothing; `Animora.Application` references only Domain;
  `Persistence.Sqlite`/`Infrastructure.Local` may reference Application+Domain; `Animora.Contracts`
  references no `Animora.*`. **`Microsoft.AspNetCore.Identity` may be referenced by
  `Infrastructure.Local` ONLY.**
- **§4 style:** `sealed` classes; `sealed record` for DTOs/VOs; nullable honored (no `!` without a
  reason comment); async-all-the-way with `CancellationToken ct = default`; library projects use
  `ConfigureAwait(false)`; no exceptions for expected flow — return `Result`/`Result<T>` with an
  `ErrorCode`; `IClock` injected, never `DateTimeOffset.UtcNow`; Ids via `IdGenerator.NewId()` (ULID),
  never `Guid`.
- **§7 security:** passwords use ASP.NET Core Identity `PasswordHasher<T>` primitives only — no custom
  crypto; security stamp rotates on every credential change.
- **§11 runnability / §12 DoD:** `build.sh full` 0/0; no forbidden dep; `PROGRESS.md` updated
  (done + next phase + repo-state entry + deviations); commit on branch `genspark_ai_developer`.

---

## Goal (this session)
Lay the durable substrate the rest of B03 builds on: real hashed-credential support on the `User`
aggregate, the local session/attempt-log tables with migration `005_LocalIdentity`, and the
`IPasswordHasher`/`PasswordPolicy`/`ISessionService` ports with their `Infrastructure.Local`
implementations. After this session the login *machinery* exists but nothing consumes it yet.

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

### R2 · `Result` / `ErrorCode` (`src/Animora.Domain/Results/`)
```csharp
public readonly record struct Result {                       // and Result<T> with the same shape
    bool IsSuccess; bool IsFailure; ErrorCode Error; string? Detail;
    static Result Ok();                       static Result Fail(ErrorCode error, string? detail = null);
    // Result<T>:  static Result<T> Ok(T value);  static Result<T> Fail(ErrorCode error, string? detail = null);
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

### R3 · Repository + EF configuration + migration patterns
```csharp
// src/Animora.Persistence.Sqlite/Repositories/IdentityRepository.cs  (impl pattern to copy)
public sealed class IdentityRepository(AnimoraLocalDbContext db) : IIdentityRepository {
    public Task<User?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<User>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
    public void Add(User entity) => db.Set<User>().Add(entity); /* ... */ }

// Config pattern (src/Animora.Persistence.Sqlite/Configurations/Identity/UserConfiguration.cs — EXISTS)
public sealed class UserConfiguration : IEntityTypeConfiguration<User> {
    public void Configure(EntityTypeBuilder<User> builder) {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.UserName })            // ← already unique; do not re-add
            .HasDatabaseName("IX_Users_TenantId_Username").IsUnique(); } }
```
`AnimoraLocalDbContext` is the local DbContext; repositories take it via primary constructor and use
`db.Set<T>()`. Default query behavior is `NoTracking`; use `.AsTracking()` for write paths.
Existing migrations live in `src/Animora.Persistence.Sqlite/Migrations/` and are numbered:
`001_LocalCoreSchema`, `002_LocalBusinessSchema`, `003_LocalHardening`, `004_CommandPipeline` (+
`AnimoraLocalDbContextModelSnapshot.cs`). **Your migration is `005_LocalIdentity`.** SQLite column
types used by convention: `TEXT` for strings/ids (`maxLength` set), `INTEGER` for `long`/`bool`/enum,
and `DateTimeOffset` is stored as a `long` epoch column (see `004`: `CreatedAtUtc = table.Column<long>`).
`AppendOnlyGuard` in `src/Animora.Persistence.Sqlite/Context/AppendOnlyGuard.cs` has a
`ProtectedTypes` set — append the new `LoginAttemptLog` type to it.

### R4 · DI entry point for infrastructure (append your registrations here — do NOT restructure)
`AddAnimoraLocalInfrastructure(...)` in
`src/Animora.Infrastructure.Local/LocalInfrastructureServiceCollectionExtensions.cs` calls
`AddAnimoraLocalStore` first, then registers `IClock`/`IMonotonicClock`, the secret store,
`ILocalInstallationIdentity → LocalInstallationIdentity`, `ILocalStoreInitializer →
LocalStoreStartup`. **Add here** `IPasswordHasher → PasswordHashingService` and
`ISessionService → LocalSessionService` + `LocalSessionOptions`.
`ISecretStore` (A11a) is the secret store used elsewhere; `IClock` is injected, never wall-clock.

---

## §Design decisions (pre-made — implement exactly, do not re-derive)

### D1 · Package pin
Add `Microsoft.AspNetCore.Identity` version **`10.0.12`** to `Directory.Packages.props` (a
`<PackageVersion Include="..." Version="10.0.12" />` line in the existing `<ItemGroup>`). Reference it
**only** from `Animora.Infrastructure.Local.csproj` (`<PackageReference Include=
"Microsoft.AspNetCore.Identity" />`, no version — versions are central). The `LayeringRulesTests` fact
asserting no other project references it is authored in **B03c** (tests session).

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
- Configurations under `Configurations/Identity/`: `LocalSessionConfiguration` (key + one-active
  index), `LoginAttemptLogConfiguration` (key + index on `OccurredAtUtc`).

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
  (A `SuccessRehashNeeded` verify is silently re-hashed by the caller in B03b's sign-in handler.)

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
- `UnlockAsync(password)` in this session verifies the password against the session's user via
  `IPasswordHasher` (the `IIdentityRepository.FindAsync` lookup by `UserId` already exists — R3).

### D6 · Error codes (add exactly 4 — see R2 for the pattern & the reuse list)
Add to the Identity region of `ErrorCodes.cs`:
`CredentialsInvalid` (`CREDENTIALS_INVALID`, Forbidden), `SessionExpired` (`SESSION_EXPIRED`,
Forbidden), `PasswordPolicyViolation` (`PASSWORD_POLICY_VIOLATION`, Validation),
`FirstRunAlreadyCompleted` (`FIRST_RUN_ALREADY_COMPLETED`, Conflict).
**Reuse `UserLockedOut` / `UserDisabled`** for the lock/disable cases (do NOT add
`ACCOUNT_LOCKED`/`ACCOUNT_DISABLED` — they would be semantic duplicates). Record this substitution in
the `PROGRESS.md` deviation log. (These codes are *added* here; they are *consumed* by B03b/B03c.)

---

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `Directory.Packages.props` | modify | pin `Microsoft.AspNetCore.Identity 10.0.12` |
| `src/Animora.Domain/Identity/User.cs` | modify | add `MustChangePassword` + `RequirePasswordChange()`; clear in `SetPasswordHash` |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | +4 codes (D6); reuse `UserLockedOut`/`UserDisabled` |
| `src/Animora.Persistence.Sqlite/Entities/LocalSession.cs` | create | durable session row (D3) |
| `src/Animora.Persistence.Sqlite/Entities/LoginAttemptLog.cs` | create | append-only attempt log (D3) |
| `src/Animora.Persistence.Sqlite/Configurations/Identity/LocalSessionConfiguration.cs` | create | key + one-active index |
| `src/Animora.Persistence.Sqlite/Configurations/Identity/LoginAttemptLogConfiguration.cs` | create | key + index |
| `src/Animora.Persistence.Sqlite/Context/AppendOnlyGuard.cs` | modify | add `LoginAttemptLog` to `ProtectedTypes` |
| `src/Animora.Persistence.Sqlite/Migrations/*005_LocalIdentity*` | create | via `dotnet-ef` (step 2) — 2 tables + `MustChangePassword` column + snapshot |
| `src/Animora.Application/Security/IPasswordHasher.cs` | create | port + outcome enum (D4) |
| `src/Animora.Application/Security/PasswordPolicy.cs` | create | fixed policy (D4) |
| `src/Animora.Application/Security/ISessionService.cs` | create | port + `SessionSnapshot` (D5) |
| `src/Animora.Infrastructure.Local/Security/PasswordHashingService.cs` | create | Identity hasher wrapper (D4) |
| `src/Animora.Infrastructure.Local/Security/LocalSessionService.cs` | create | restore/start/touch/lock/unlock/end (D5) |
| `src/Animora.Infrastructure.Local/Options/LocalSessionOptions.cs` | create | idle 30 min, absolute 12 h |
| `src/Animora.Infrastructure.Local/Animora.Infrastructure.Local.csproj` | modify | + `Microsoft.AspNetCore.Identity` package ref |
| `src/Animora.Infrastructure.Local/LocalInfrastructureServiceCollectionExtensions.cs` | modify | register hasher + session service + options |
| `backend-roadmap/PROGRESS.md` | modify | mark B03a done, next → B03b, repo-state entry, deviations |

---

## §Implementation plan (do these in order; commands are exact)

1. **Package + Domain + error codes.** Pin `Microsoft.AspNetCore.Identity 10.0.12` (D1); add the
   `User.MustChangePassword` member (D2); add the 4 error codes (D6). Reference the package from
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
5. **DI**: register hasher + session service + options in `AddAnimoraLocalInfrastructure` (R4).
6. **Gate check:** `scripts/sandbox/build.sh full` = 0/0. (No `test.sh`/`run.sh` this session.)
7. **PROGRESS.md** update + commit on `genspark_ai_developer`.

---

## Contracts / schema added
- Domain: `User.MustChangePassword` (+ method). Local tables `LocalSessions`, `LoginAttemptLogs`
  (migration `005_LocalIdentity`). `ErrorCodes` += 4 (not 6 — `UserLockedOut`/`UserDisabled` reused).
- Ports/impl: `IPasswordHasher`, `PasswordPolicy`, `ISessionService`/`SessionSnapshot`,
  `PasswordHashingService`, `LocalSessionService`, `LocalSessionOptions`.
- DI: `IPasswordHasher → PasswordHashingService`, `ISessionService → LocalSessionService` +
  `LocalSessionOptions` in `AddAnimoraLocalInfrastructure`. Package pin: `Microsoft.AspNetCore.Identity`.

## Rules that apply here
- **CONVENTIONS §7** — Identity primitives only; no custom crypto; stamp rotates on every credential
  change.
- **A-RULE 029** — the session is policy-driven with explicit expiry; no silent extension.
- **R-B09** — the session row is durable, so a crash cannot leave a half-authenticated state.
- **PERFORMANCE.md §4/§12** — `TouchAsync` throttled (≤ 1 write / 60 s).

## Out of scope (owned by the sibling sessions)
- Use cases (`SignInCommand` etc.), provider swap (`SessionCurrentUserProvider`), new repositories,
  application-DI registration → **B03b**.
- `Animora.Contracts` auth seam, `Client.Services` implementations, Desktop (`LoginViewModel`,
  `FirstRunView`), seeding change, all tests, `run.sh` gate → **B03c**.
- Everything B03 defers as a whole (permission evaluation, server identity, role UI, password reset)
  stays deferred (see the B03 index file).

## Acceptance criteria
- `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `.sandbox/tools/dotnet-ef migrations list … --context AnimoraLocalDbContext` shows `005_LocalIdentity`.
- `grep -n "MustChangePassword" src/Animora.Domain/Identity/User.cs` shows the property + its clear in
  `SetPasswordHash`.
- `grep -rn "Microsoft.AspNetCore.Identity" src/*/*.csproj` matches **only**
  `Animora.Infrastructure.Local.csproj`.
- `LoginAttemptLog` appears in `AppendOnlyGuard.ProtectedTypes`.

## PROGRESS.md update
Mark B03a done; advance "Next phase" to `B03b`; append a "Repo state" entry listing: the Identity
package pin, `User.MustChangePassword`, `LocalSessions`/`LoginAttemptLogs` + migration
`005_LocalIdentity`, the hashing/session ports and implementations, the DI additions in
`AddAnimoraLocalInfrastructure`, `ErrorCodes` += 4 (with the `UserLockedOut`/`UserDisabled` reuse
deviation), and any deviation with its justification.
