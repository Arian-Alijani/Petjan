# Progress & Project State — Animora Backend & Server

> **Read this file first, every session. Update it at the end of every phase.** It is the single source
> of truth for "what already exists" and "what to do next". Never skip the update step.

## Standing principles (in force in EVERY phase, never traded off)

1. **Offline-first correctness.** The desktop must keep full functionality with the server unreachable,
   on a real local database. Local state may be provisional; it is never lost, never silently
   overwritten, and never invented. The server is the single global authority for settlement.
2. **Backend-ready clean layering.** Domain ← Application ← Infrastructure/Presentation, dependencies
   inward only. No EF Core / Datasync / SQL / HTTP type ever appears in a Desktop View or ViewModel. No
   client code in a server project or vice versa. (`CONVENTIONS.md` §1–§2.)
3. **Runnability.** Through the end of Track C, `scripts/sandbox/build.sh full` = 0 warnings / 0 errors
   and `scripts/sandbox/run.sh` = SMOKE RUN OK on a machine with no server, no SQL Server and no
   network. From Track D on, the desktop must still start and operate offline. (`CONVENTIONS.md` §11.)
4. **No mock left behind.** Every `Animora.Contracts` interface ends this roadmap with a real
   implementation; every `Mock*` file is deleted in the phase that replaces it. No bounded context ever
   keeps both. (`CONVENTIONS.md` §10; audited in C11.)

> These restate — never replace — `CONVENTIONS.md` and `ROADMAP.md`. If wording differs, those govern.

## Current state (authoring-time snapshot — no phase executed yet)

| Area | State |
|---|---|
| `src/Animora.Desktop/` | **Complete.** 17 screens, ~300 `.cs` files, shell/nav/dialogs/toasts/theme/localization. `App.axaml.cs` is the composition root (DI). Builds 0 warnings / 0 errors. |
| `src/Animora.Contracts/` | 20 interfaces + DTOs, one folder per bounded context. The frontend↔backend seam. |
| `src/Animora.Desktop/Mocks/` | 22 in-memory files (20 `Mock*` services + `AppointmentStore` + `VisitEncounterStore`) — the only implementations today. All sample data lives here. |
| `src/Animora.Domain/`, `src/Animora.Application/`, `src/Animora.Sync.Abstractions/` | Empty placeholders (csproj + README only). |
| Server / SQL Server / sync / auth / licensing / admin web | **Do not exist.** |
| Login | `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` — hardcoded `admin`/`admin`; `TryAuthenticate` is the documented swap seam (replaced in B03). |
| `Directory.Packages.props` | Avalonia 12.1.2 (+Desktop/Fluent/Inter/DataGrid), CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 10.0.12. `ManagePackageVersionsCentrally=true`. |
| `Directory.Build.props` | `net10.0`, `LangVersion 14.0`, `Nullable enable`, `ImplicitUsings enable`. |
| Solution | `Animora.sln` contains Contracts, Domain, Application, Sync.Abstractions, Desktop. |
| Scripts | `scripts/sandbox/build.sh [fast\|full\|release\|restore]`, `run.sh` (Xvfb smoke, prints `SMOKE RUN OK`), `install-prereqs.sh`, `doctor.sh`, `bench.sh`. `test.sh` is added by A01. |
| Frontend history | `windows-frontend-roadmap/PROGRESS.md` (phases 01–29 done). Its `phases/**` must never be read. |

## Next phase

**Next phase: `A04`** — `backend-roadmap/phases/PHASE-A04-*.md`

## Phase status

| ID | Status | Date | Notes |
|---|---|---|---|
| A01 | ✅ Done | 2026-09-23 | Backend track scaffold: 4 src + 5 test projects, 10 pins, WireContract v1, test.sh, LayeringRulesTests. Build 0/0, TESTS OK, SMOKE RUN OK. |
| A02 | ✅ Done | 2026-09-23 | Domain kernel: 15 files (primitives, Money/Quantity, ULID IdGenerator, clock ports, Result/ErrorCodes seed 11). 4 test classes, 24 domain tests. Build 0/0, TESTS OK, SMOKE RUN OK. |
| A03 | ✅ Done | 2026-09-23 | Identity spine: 11 files in `Domain/Identity` (Tenant, User, StaffRole, Permission, PermissionCodes 42, RoleDefinition, DefaultRolePermissions, StaffMembership, Device, CapabilitySnapshot, OfflineCapabilityPolicy). ErrorCodes += 6. 3 test classes (+21 facts → 45). Build 0/0, TESTS OK, SMOKE RUN OK. |
| A04 | ⬜ Not started | | |
| A05 | ⬜ Not started | | |
| A06 | ⬜ Not started | | |
| A07 | ⬜ Not started | | |
| A08 | ⬜ Not started | | |
| A09 | ⬜ Not started | | |
| A10 | ⬜ Not started | | |
| A11 | ⬜ Not started | | |
| B01 | ⬜ Not started | | |
| B02 | ⬜ Not started | | |
| B03 | ⬜ Not started | | |
| B04 | ⬜ Not started | | |
| B05 | ⬜ Not started | | |
| C01 | ⬜ Not started | | |
| C02 | ⬜ Not started | | |
| C03 | ⬜ Not started | | |
| C04 | ⬜ Not started | | |
| C05 | ⬜ Not started | | |
| C06 | ⬜ Not started | | |
| C07 | ⬜ Not started | | |
| C08 | ⬜ Not started | | |
| C09 | ⬜ Not started | | |
| C10 | ⬜ Not started | | |
| C11 | ⬜ Not started | | |
| D01 | ⬜ Not started | | |
| D02 | ⬜ Not started | | |
| D03 | ⬜ Not started | | |
| D04 | ⬜ Not started | | |
| D05 | ⬜ Not started | | |
| D06 | ⬜ Not started | | |
| D07 | ⬜ Not started | | |
| D08 | ⬜ Not started | | |
| D09 | ⬜ Not started | | |
| D10 | ⬜ Not started | | |
| D11 | ⬜ Not started | | |
| D12 | ⬜ Not started | | |
| D13 | ⬜ Not started | | |
| E01 | ⬜ Not started | | |
| E02 | ⬜ Not started | | |
| E03 | ⬜ Not started | | |
| E04 | ⬜ Not started | | |
| E05 | ⬜ Not started | | |
| E06 | ⬜ Not started | | |
| E07 | ⬜ Not started | | |
| E08 | ⬜ Not started | | |
| E09 | ⬜ Not started | | |

Status values: `⬜ Not started` · `🟨 In progress` · `✅ Done` · `⛔ Blocked (reason)`.

## Repo state log

> Append one entry per completed phase, newest first. Required fields:
> **files added**, **contracts added**, **DI changes**, **mocks deleted**, **migrations created**,
> **tests added**, **verification** (build/test/smoke results), **deviations**.

### A03 — Domain: identity, tenant, staff, device (2026-09-23)

- **Files added (src, 11 — all in `src/Animora.Domain/Identity/`; the A02 `.gitkeep` placeholder removed):**
  - `Tenant.cs` — `sealed class Tenant : AggregateRoot` + `enum TenantStatus { Active, Suspended, Closed }`.
    `DisplayName`, `LegalName?`, `Timezone` (IANA, default `Asia/Tehran` via `DefaultTimezone` const),
    `DefaultCurrency` (default `CurrencyScale.Irt` = `"IRT"`), `Status`. `Create(...)` sets
    `TenantId == Id` so the tenant row is covered by the same uniform tenant filter; `SetStatus`, `Rename`.
  - `User.cs` — `sealed class User : AggregateRoot` + `enum UserStatus { Active, Disabled, Locked }`.
    `UserName` (lowercase invariant, unique per tenant), `DisplayName`, `PasswordHash?`, `SecurityStamp`
    (ULID), `Status`, `LastLoginUtc?`, `FailedAttemptCount`, `LockoutEndUtc?`. Policy constants
    `MaxFailedAttempts = 5`, `LockoutDuration = 15 min`, `DefaultFailureWindow = 15 min`. Methods
    `RegisterFailedAttempt(IClock, threshold, window)` (returns whether it triggered the lockout; a streak
    older than the window is discarded), `ClearFailures()` (never re-activates a `Disabled` account),
    `IsLockedOut(nowUtc)` (fail-closed when `LockoutEndUtc` is null), `RegisterSuccessfulLogin(IClock)`,
    `RotateSecurityStamp()`, `SetPasswordHash(string)` (rotates the stamp), `Disable()`, `Enable()`.
  - `StaffRole.cs` — `enum StaffRole { Manager, Doctor, Receptionist, Cashier, Accountant }`.
    Mapping to `Animora.Contracts.Navigation.UserRole` deferred to C10 (Contracts untouched).
  - `Permission.cs` — `sealed class Permission` (`Code` PK, `Category`, `RiskLevel Risk`) +
    `enum RiskLevel { Low, Normal, High, Critical }`; `Create(code, risk, category?)`, `CategoryOf(code)`
    (first dotted segment).
  - `PermissionCodes.cs` — **42 seed codes** as `const string` members in 5 regions (patients/reception,
    clinical, appointments/boarding, inventory/purchasing, sales/payments, finance/reporting,
    administration), exactly the strings listed in the phase file. `All` (ordered `IReadOnlyList<string>`),
    `Exists(code)`, `RiskOf(code)` (Critical set of 7 → High set of 6 → `*.read` = Low → Normal),
    `CreateCatalog()`.
  - `RoleDefinition.cs` — `sealed class RoleDefinition : AggregateRoot`: `Role`, `DisplayKey`
    (`role.<lowercase>`), `IsSystem`, `PermissionCodes` (`IReadOnlySet<string>` over a private ordinal
    `HashSet`). `CreateSystem(tenantId, role, IClock)` seeds from `DefaultRolePermissions`;
    `Grant`/`Revoke` throw `InvalidOperationException` for the system `Manager` definition and
    `ArgumentException` for an undeclared code; `Has(code)`.
  - `DefaultRolePermissions.cs` — the role→permission matrix exactly as tabled, built **only** from
    `PermissionCodes` constants (`using P = ...` alias) so a rename is a compile error. Manager = all 42;
    Doctor 16; Receptionist 14; Cashier 12; Accountant 14. `All`, `For(role)` (empty set = fail closed).
  - `StaffMembership.cs` — `sealed class StaffMembership : AggregateRoot` + `enum MembershipStatus
    { Active, Suspended, Ended }`: `UserId`, `Role`, `Status`, `JoinedUtc`, `EndedUtc?`,
    `ExtraPermissionCodes`, `DeniedPermissionCodes`. `Create`, `IsActive`, `ChangeRole`, `Suspend`,
    `Reinstate` (an ended membership is never reopened), `End(IClock)` (idempotent, keeps the first
    instant), `GrantExtra`/`Deny` (mutually exclusive — deny wins), `ClearOverride`. Documented as the
    **only** `(User, Tenant)` link; `TenantId` is resolved from it server-side (A-RULE 012).
  - `Device.cs` — `sealed class Device : AggregateRoot` + `enum DeviceStatus { Pending, Active, Revoked }`:
    `UserId`, `DeviceName`, `InstallationIdHash` (SHA-256 hex; the identity itself never leaves the device),
    `Platform`, `Status`, `RegisteredUtc`, `LastContactUtc?`, `RevokedUtc?`, `RevocationReason?`.
    `Register(...)` → `Pending`; `Approve()` (throws for a revoked device), `IsUsable`,
    `RecordContact(IClock)`, `Revoke(reason, IClock)` **idempotent** — a second call keeps the first
    instant and reason so the audit trail stays truthful.
  - `CapabilitySnapshot.cs` — `sealed record CapabilitySnapshot(TenantId, UserId, DeviceId, Role,
    CapabilityExpiry, IssuedUtc, SnapshotExpiryUtc, SecurityStamp, Signature)`. `Allows(code, nowUtc)`
    **fails closed**: blank/unknown code → false, snapshot expired → false, capability expiry `<= nowUtc`
    → false (so a zero window never grants). `IsExpired(nowUtc)`. Helper
    `BuildExpiry(codes, issuedUtc)` applies `OfflineCapabilityPolicy` per code — signing/verification
    stays server-side (D03/E07); `Signature` is an opaque base64 string here.
  - `OfflineCapabilityPolicy.cs` — the authoritative windows: `Low = 30 d`, `Normal = 14 d`,
    `High = 72 h`, `Critical = 0`; `WindowFor(RiskLevel)` (unknown level → `TimeSpan.Zero`),
    `WindowForCode(code)`. Documented as ADR-gated values.
- **Files modified (src, 1):** `Results/ErrorCodes.cs` — new `#region Identity (Phase A03)` **appended**
  (no existing entry reordered) with 6 codes, all category `Forbidden`: `TENANT_SUSPENDED`,
  `USER_DISABLED`, `USER_LOCKED_OUT`, `MEMBERSHIP_NOT_ACTIVE`, `DEVICE_REVOKED`,
  `CAPABILITY_NOT_GRANTED`. Catalog total 11 → 17.
- **Contracts added:** 6 entities (`Tenant`, `User`, `Permission`, `RoleDefinition`, `StaffMembership`,
  `Device`), 6 enums (`TenantStatus`, `UserStatus`, `StaffRole`, `RiskLevel`, `MembershipStatus`,
  `DeviceStatus`), 1 value object (`CapabilitySnapshot`), 3 statics (`PermissionCodes`,
  `DefaultRolePermissions`, `OfflineCapabilityPolicy`). **No `Animora.Contracts` change**, no wire DTO,
  no EF configuration, no persistence.
- **DI changes:** none. **Mocks deleted:** none. **Migrations created:** none.
- **Package/project references added:** **none** — `Animora.Domain.csproj` is still an empty
  `Microsoft.NET.Sdk` project with zero `PackageReference`/`ProjectReference` (CONVENTIONS §2);
  `LayeringRulesTests` still green.
- **Tests added (3 classes, 21 facts; `Animora.Domain.Tests` 24 → 45):**
  - `UserLockoutTests` (5) — the 5th failure sets `Locked` + `LockoutEndUtc = now + 15 min`; attempts
    outside the window restart the streak; a successful login clears status/count/end and stamps
    `LastLoginUtc`; `ClearFailures` does not revive a `Disabled` account; `SetPasswordHash` normalizes
    the user name and rotates the stamp. Uses a local advancing `TestClock : IClock` — no wall clock.
  - `RolePermissionMatrixTests` (8 methods / 10 cases) — every string in every role's set resolves to a declared code;
    the catalog has no duplicates and `CreateCatalog` risk-matches `RiskOf`; the risk table is asserted
    literally (7 Critical, 6 High, every `*.read` Low, `patients.write` Normal); Manager == `All`;
    Doctor/Receptionist/Cashier hold **no** Critical code; Accountant's Critical subset is exactly
    `{finance.period.close, payments.reverse}`; the system Manager definition rejects `Grant`/`Revoke`
    and a non-Manager one accepts idempotent grant/revoke but rejects an unknown code; membership carries
    the only `(User, Tenant)` link, deny beats extra, and `End` is idempotent.
  - `CapabilitySnapshotTests` (6) — an expired snapshot denies even a `Low` read; an unknown/ungranted/
    blank code is denied; a `Critical` code is denied at `IssuedUtc` because its window is zero;
    a `High` code is allowed at +71 h and denied at +73 h while the snapshot itself is still valid
    (capability expiry enforced independently); the four offline windows are the published numbers;
    device revocation is idempotent and a revoked device cannot be re-approved.
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**;
  `scripts/sandbox/test.sh` = **TESTS OK** (Domain 45/45, other 4 projects 1/1 each);
  `scripts/sandbox/run.sh` = **SMOKE RUN OK**.
- **Deviations:** none material. Additions beyond the literal file list, all inside phase scope and
  needed to keep the types usable without re-deriving rules later: factory methods (`Create`/`Register`/
  `CreateSystem`) because every aggregate has private setters and an `IClock`-supplied instant;
  `User.IsLockedOut`/`RegisterSuccessfulLogin`/`Enable`/`Disable`; `Permission.CategoryOf`;
  `PermissionCodes.Exists`/`RiskOf`/`CreateCatalog`; `DefaultRolePermissions.For`;
  `StaffMembership` override/lifecycle helpers; `Device.Approve`/`RecordContact`/`IsUsable`;
  `CapabilitySnapshot.BuildExpiry`; `OfflineCapabilityPolicy.WindowForCode`. `RoleDefinition.Grant`
  additionally rejects undeclared codes (fail-closed, CONVENTIONS §7). No `IPermissionEvaluator`,
  no hashing, no signing, no EF mapping — those stay in B03/B04/D03/A09 as scoped.

### A02 — Domain kernel (2026-09-23)

- **Files added (src, 15 — all in `src/Animora.Domain/`, all new folders):**
  - `Primitives/SyncableEntity.cs` — syncable shape base: `Id` (init, ULID), `TenantId`,
    `UpdatedAt`, `Version` (nullable, server-issued), `Deleted`; protected EF ctor; **no EF attribute**.
    XML docs state `UpdatedAt`/`Version` are transport bookkeeping only (A-RULE 009/010).
  - `Primitives/LocalOnlyEntity.cs` — `Id` + `CreatedAtUtc`; never replicated.
  - `Primitives/LocalOnlyAttribute.cs` — `[LocalOnly]` class marker (consumed by A09 model builder, E03 scope).
  - `Primitives/IHasDomainEvents.cs` — `IReadOnlyList<DomainEvent> DomainEvents` + `ClearDomainEvents()`.
  - `Primitives/AggregateRoot.cs` — `SyncableEntity` + private `List<DomainEvent>` + `protected Raise(...)`;
    buffer exposed read-only.
  - `Primitives/IdGenerator.cs` — monotonic 26-char Crockford base32 ULID (48-bit ms + 80-bit
    `RandomNumberGenerator` randomness), lock-guarded same-ms increment; `NewId()` additionally clamps a
    backwards OS-clock jump to the last emitted ms so process-wide ordering holds; `NewId(DateTimeOffset)`
    honours the caller's instant verbatim (tests).
  - `Events/DomainEvent.cs` — `abstract record DomainEvent(string EventId, string TenantId,
    DateTimeOffset OccurredAtUtc)`; no `INotification`, no MediatR.
  - `ValueObjects/Money.cs` — `readonly record struct Money(long Minor, string Currency)`: `ZeroIrt`,
    `Irt(long)`, `Of(long,string)` (3-letter uppercase validation), `Zero(string)`, `Scale`, `IsZero`,
    `IsNegative`, `Abs`; `+ - unary- *` (int/long, both operand orders) with `checked` arithmetic and named
    alternatives (`Add`/`Subtract`/`Negate`/`Multiply`); `IComparable<Money>`/`IComparable` + `< <= > >=`;
    currency mismatch throws `InvalidOperationException` (defect, not control flow);
    `Allocate(int)` largest-remainder (sign-aware, conserves every minor unit);
    `ApplyPercent(decimal)` with `MidpointRounding.AwayFromZero` — the product's single rounding site.
  - `ValueObjects/CurrencyScale.cs` — `Of(currency)`: `IRT` → 0, `USD`/`EUR` → 2, default 2; `Irt` const.
  - `ValueObjects/Quantity.cs` — `readonly record struct Quantity(decimal Value, string UnitCode)`:
    `Zero(unit)`, `IsZero`, `IsNegative`, `Abs`, `+ - unary-` with ordinal unit-code check (throws on
    mismatch). Negative values intentionally legal (movements are signed).
  - `Time/IClock.cs` — `DateTimeOffset UtcNow`. `Time/IMonotonicClock.cs` — `ElapsedTicks`,
    `HighWaterUtc` (never decreases), `Observe(DateTimeOffset serverUtc)`; A-RULE 011 documented.
    Implementations deferred to A11 as specified.
  - `Results/ErrorCode.cs` — `readonly record struct ErrorCode(string Code, string Category)` + the 7
    category consts (`Validation`, `NotFound`, `Conflict`, `Forbidden`, `Integrity`, `Infrastructure`,
    `Entitlement`) + `None`/`IsNone`.
  - `Results/ErrorCodes.cs` — central catalog seeded with the 11 cross-cutting codes:
    `VALIDATION_FAILED`, `NOT_FOUND`, `CONCURRENCY_CONFLICT`, `PERMISSION_DENIED`, `TENANT_MISMATCH`,
    `IDEMPOTENCY_FINGERPRINT_MISMATCH`, `DEPENDENCY_UNSETTLED`, `ENTITLEMENT_REQUIRED`,
    `OFFLINE_CAPABILITY_EXPIRED`, `STORE_UNAVAILABLE`, `UNEXPECTED`. Append-only by contract.
  - `Results/Result.cs` — `Result` and `Result<T>` (`IsSuccess`/`IsFailure`/`Value`/`Error`/`Detail`),
    `Ok`/`Fail` factories, implicit `T → Result<T>` (+ named `FromValue`), `Map`, `Bind` (all arities),
    `ToResult`, `OrElse`. `Detail` documented as a developer string, never localized text.
  - `Identity/` created empty (`.gitkeep`) — filled by A03.
- **Files modified (src, 1):** `src/Animora.Domain/README.md` — folder-layout table + the zero-dependency,
  no-Guid, no-system-clock, no-decimal-money, append-only-ErrorCodes rules.
- **Contracts added:** domain primitives + value objects + clock ports + results listed above.
  No `Animora.Contracts` change, no `Animora.Contracts.Wire` change, no DbContext, no wire DTO.
- **DI changes:** none (Domain has no composition root; clock implementations and registration are A11).
- **Mocks deleted:** none (Track C protocol not yet in effect).
- **Migrations created:** none.
- **Package/project references added:** **none** — `Animora.Domain.csproj` remains an empty
  `Microsoft.NET.Sdk` project with zero `PackageReference` and zero `ProjectReference` (CONVENTIONS §2).
- **Tests added (4 classes, 24 facts total in `Animora.Domain.Tests`):**
  - `MoneyTests` — exact minor-unit arithmetic/comparison; currency mismatch throws on `+`/`-`/`CompareTo`
    and bad-code `Of` throws; `Allocate` conserves the sum and keeps shares within one unit (incl. a
    negative-amount case); `Allocate(3)` of 100 IRT = `34/33/33`; `ApplyPercent` away-from-zero
    (`105 × 50% → 53`, `-105 × 50% → -53`).
  - `QuantityTests` — add/sub keep unit and allow negatives; unit mismatch throws.
  - `IdGeneratorTests` — 26 chars, Crockford alphabet only, cross-ms sortability, same-ms strict
    monotonicity with identical timestamp prefix, and the 10 000-id loop asserting zero duplicates and
    strictly ascending ordinal order.
  - `ResultTests` — Ok/Fail state + code/category/detail, implicit conversion + `FromValue`/`OrElse`,
    `Map` transform/propagate, `Bind` chain/short-circuit, `ToResult`.
  - `LayeringRulesTests` unchanged and still green (Domain still references no `Animora.*` assembly).
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**;
  `scripts/sandbox/test.sh` = `TESTS OK` (Domain 24 passed, other 4 projects 1 placeholder each);
  `scripts/sandbox/run.sh` = `SMOKE RUN OK` (desktop untouched);
  `grep -rn "DateTime.Now\|DateTimeOffset.UtcNow\|Guid.NewGuid" src/Animora.Domain` returns nothing.
- **Deviations:**
  - **D-A02-1** — The phase file placed the category consts in `Results/ErrorCode.cs` without naming a
    holder type. They are declared as consts **on `ErrorCode` itself** (`ErrorCode.Validation`, …) rather
    than in a second public type, to keep one public type per file (CONVENTIONS §3). No file count change.
  - **D-A02-2** — `IdGenerator.NewId()` obtains the instant from `TimeProvider.System`; `IClock` cannot be
    injected into a static generator and lives in the same assembly. This is an id prefix only, never a
    business sequence or licensing authority (A-RULE 011). The forbidden-call grep stays clean.
  - **D-A02-3** — Added conveniences beyond the phase list, all additive and non-breaking: `Money.Of`,
    `Money.Zero`, `Abs`, `IsZero`/`IsNegative`, named operator alternatives (CA2225 compliance),
    `Quantity.Abs`/`Negate`, `Result<T>.FromValue`/`ToResult`/`OrElse`.
  - **D-A02-4** — `src/Animora.Domain/Identity/` is tracked via a `.gitkeep` (git cannot store an empty
    directory); A03 removes it when it adds the first identity entity.

### A01 — Backend track scaffold (2026-09-23)

- **Files added (src, 4 new projects):**
  - `src/Animora.Contracts.Wire/` — `Animora.Contracts.Wire.csproj` (no refs) + `WireContract.cs`.
  - `src/Animora.Persistence.Sqlite/` — csproj (refs Domain + Application; packages EF Core Sqlite/Design,
    SQLitePCLRaw.bundle_e_sqlite3, SourceGear.sqlite3) + `README.md` (filled by A09).
  - `src/Animora.Infrastructure.Local/` — csproj (refs Domain + Application; packages Logging.Abstractions,
    Options) + `README.md` (owning phases A02 clocks, A11 keystore/encryption, B05 logging).
  - `src/Animora.Client.Services/` — csproj (refs Application, Contracts, Persistence.Sqlite,
    Infrastructure.Local) + `README.md` (Track C DI swap target).
  - Anchor types `src/Animora.Domain/AssemblyMarker.cs` and `src/Animora.Application/AssemblyMarker.cs`
    (see deviation D-A01-1) so the layering test can load empty placeholder assemblies deterministically.
- **Files modified (src):**
  - `src/Animora.Application/Animora.Application.csproj` — now refs Domain + Contracts + Contracts.Wire,
    and packages Logging.Abstractions + Options.
  - `src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj` — now refs Domain + Contracts.Wire.
- **Files added (tests, 5 new projects):** `tests/Animora.Domain.Tests` (xunit; refs Domain + Application +
  Contracts so LayeringRulesTests can load them), `tests/Animora.Application.Tests`,
  `tests/Animora.Persistence.Tests`, `tests/Animora.Sync.Tests`, `tests/Animora.Server.Tests` (xunit only,
  no subject ref until D01). Each has a placeholder test; Server.Tests keeps only the placeholder.
- **Build/infra files modified:** `Directory.Packages.props` (10 backend pins added), `Directory.Build.props`
  (`NuGetAuditMode=all`, `EnableNETAnalyzers=true`, `TreatWarningsAsErrors=false`), `Animora.sln`
  (4 src projects under `src` folder + 5 test projects under a new `tests` folder), `.gitignore`
  (`**/TestResults/`), `scripts/sandbox/test.sh` added (env + `dotnet test Animora.sln`, prints `TESTS OK`,
  optional `<project-name-filter>` arg).
- **Package pins added (exact, no ranges — T-RULE 010):** `Microsoft.EntityFrameworkCore 10.0.12`,
  `Microsoft.EntityFrameworkCore.Sqlite 10.0.12`, `Microsoft.EntityFrameworkCore.Design 10.0.12`,
  `Microsoft.Extensions.Logging.Abstractions 10.0.12`, `Microsoft.Extensions.Options 10.0.12`,
  `SQLitePCLRaw.bundle_e_sqlite3 3.0.3`, `SourceGear.sqlite3 3.50.4.5`, `xunit 2.9.2`,
  `xunit.runner.visualstudio 2.8.2`, `Microsoft.NET.Test.Sdk 17.12.0`. No Datasync / SQL Server /
  ASP.NET Core / encryption-bundle pins (owned by later phases).
- **Contracts added:** `Animora.Contracts.Wire.WireContract` (`Version = 1`, `HeaderName =
  "X-Animora-Contract-Version"`). No `Animora.Contracts` change, no entity, no DbContext.
- **DI changes:** none. `Animora.Desktop` still references only its existing projects and runs entirely on
  mocks; it does NOT reference `Animora.Client.Services` (that is C01).
- **Mocks deleted:** none.
- **Migrations created:** none.
- **Tests added:** `LayeringRulesTests` (3 facts: Domain has no forbidden refs; Application has no EF/
  Datasync refs; Contracts references no `Animora.*`) + 4 placeholder tests (Application/Persistence/Sync/
  Server). Domain.Tests runs 3 facts, total 7 tests across the suite.
- **Verification:** `scripts/sandbox/build.sh full --clean-restore` = 0 warnings / 0 errors (14 projects);
  `scripts/sandbox/test.sh` = `TESTS OK`, all 5 test projects discovered and green;
  `scripts/sandbox/run.sh` = `SMOKE RUN OK`; `dotnet sln list` = 9 src + 5 test projects; layering test
  confirmed RED when an EF Core reference was temporarily added to `Animora.Domain`, then reverted;
  `grep -r "CommunityToolkit.Datasync\|EntityFrameworkCore" src/Animora.Domain src/Animora.Application`
  returns nothing in source (`.cs`/`.csproj`).
- **Deviations:** see D-A01-1 below (anchor `AssemblyMarker` types added to Domain/Application).

## Decision & deviation log

> Append an entry whenever an executing session deviates from its phase file, or makes a decision the
> phase file did not pre-make. Format:

```
### <PhaseID> — <short title>            (date: YYYY-MM-DD)
Context:    what forced the decision
Decision:   what was done instead
Rule check: which A-RULE / T-RULE / CONVENTIONS § was consulted and why this complies
Impact:     phases affected + what a later session must know
```

### A01 — anchor AssemblyMarker types in Domain & Application            (date: 2026-09-23)
Context:    `Animora.Domain` and `Animora.Application` are still empty placeholders (no types). The phase
            requires `LayeringRulesTests` to "force assembly loading with a `typeof(...)` touch of one type
            per asserted assembly". With no type to touch, the assemblies could not be loaded/anchored and
            the C# compiler would omit their (unused) project references from the manifest.
Decision:   Added a single empty `public static class AssemblyMarker` to `src/Animora.Domain` and
            `src/Animora.Application` purely as a `typeof(...)` anchor. No behavior, no data, no dependency
            direction change. (`Animora.Contracts` already has real types, so it uses
            `typeof(Contracts.Navigation.UserRole)`.)
Rule check: CONVENTIONS §1 (no TFM/Nullable in the marker's project — inherited; unchanged), §2 (adds no
            reference — Domain still depends on nothing), §4 (sealed/`static` type). The phase file itself
            mandates the `typeof(...)` touch, so this realizes rather than contradicts its intent.
Impact:     A02 onward may keep or replace these markers when real Domain/Application types appear; the
            layering facts key off assembly reference sets, not the marker, so removing them later is safe
            as long as another loadable type exists in each assembly.
