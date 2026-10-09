# Phase S02 — Server data layer & tenancy I — PostgreSQL context, tenant filter, migration 101

**Band:** M · **Track:** S (server) · **Depends on:** S01 · **Next:** S03
**Satisfies:** R-B03, R-B11, R-B19 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; regenerates scope-inventory row D02 (half 1; Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

> Baseline verified 2026-10-08: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161, `WireContract.Version` = 1; `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
> **Next-free-ordinal rule.** Never hard-code a migration number from this file. List the physical files of the owning `DbContext`'s `Migrations/` folder and take the next free ordinal (server sequence starts at **101**; the client SQLite sequence — head `031` today — is independent and untouched by server phases). Match on the `NNN` token, not a file-name prefix.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. Every existing type, signature, path and command you need is in
> **§Reference pack**; every new name is fixed in **§Design decisions**. Do **not** re-open the repo to "understand" it and
> do not re-derive decisions. If — and only if — a compile error proves a snippet diverges from the tree, fix the smallest
> thing and log it in the `PROGRESS.md` deviation log. Names here are the real ones; there is no alias to resolve.
> Implement exactly this scope: nothing from a later phase, nothing "while we're here".

**Environment (do not probe):**
- Build / test / run only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]` (expect 0 warnings / 0 errors), `scripts/sandbox/test.sh [filter]`,
  `scripts/sandbox/run.sh` (desktop smoke → `SMOKE RUN OK`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent).
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — use it; **never** `dotnet tool install`.
- Server stack is locked (`STACK.md` §0/§5/§6/§11): ASP.NET Core 10 + Kestrel, EF Core **10.0.12** line,
  PostgreSQL 18 via `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** / `Npgsql` **10.0.3** (only in `Animora.Persistence.PostgreSql`).
- PostgreSQL integration tests read the connection string from env var `ANIMORA_PG_TEST_CONNECTION`; when unset they
  **skip with a reason** locally (CONVENTIONS §1) and **fail** under `ANIMORA_REQUIRE_PG=1` (mandatory CI/release gate, owned by G07).
- **Forbidden tech (restated, binding):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11,
  per-tenant database, hard-coded TLS version (`SslProtocols.None` only), Native AOT with EF Core, `UpdatedAt`/`nextval`/`xmin` sync cursors.

**Offline invariant.** The desktop must keep starting and operating fully offline with no server, no PostgreSQL and no network (Standing principle 1, CONVENTIONS §11). This phase adds nothing the desktop needs at startup.

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §9.1–§9.2 (tenancy shape, three layers), §PG.2 (UUID `Version`), §PG.4 (pool budget), §10.6 (key ring) — only these `§`.
- `STACK.md` §6.1, §6.2, §6.4 and §11 — only these `§`.
- `src/Animora.Server/Hosting/IReadinessProbe.cs`, `src/Animora.Server/Program.cs` (edited here).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Create `Animora.Persistence.PostgreSql` (the only project allowed to reference Npgsql) with `AnimoraServerDbContext`, the shared-schema tenancy model (`TenantId varchar(26)` leading every tenant index, EF Core 10 **named** filters `"Tenant"` + `"SoftDelete"`), the DB-trigger-generated `uuid Version` concurrency token, tenant write-stamping + `IgnoreQueryFilters()` ban interceptors, the `Tenants` table via server migration `101`, a separate database-backed Data Protection key-ring context, bounded Npgsql data sources with the validated pool sum, and DB/key-ring readiness probes. It lands the tenancy **mechanism**; repositories/UoW/guard tests are S03.

---

## §Reference pack

```csharp
namespace Animora.Domain.Time;  public interface IClock { DateTimeOffset UtcNow { get; } /* + monotonic helpers per A02 */ }
namespace Animora.Domain.Primitives;
public abstract class SyncableEntity { string Id { get; init; }   /* ULID */  string TenantId { get; set; }  DateTimeOffset UpdatedAt { get; set; }  string? Version { get; set; }  bool Deleted { get; set; } }
public abstract class AggregateRoot : SyncableEntity, IHasDomainEvents { IReadOnlyList<DomainEvent> DomainEvents; void ClearDomainEvents(); protected void Raise(DomainEvent e); }
public static class IdGenerator { public static string NewId(); /* 26-char ULID */ }
```

```csharp
// Animora.Domain.Results (A02) — reuse, never redefine
public readonly record struct ErrorCode(string Code, string Category)   // categories: Validation|NotFound|Conflict|Forbidden|Integrity|Infrastructure|Entitlement
{ public const string Validation="Validation", NotFound="NotFound", Conflict="Conflict", Forbidden="Forbidden", Integrity="Integrity", Infrastructure="Infrastructure", Entitlement="Entitlement";
  public static readonly ErrorCode None; public bool IsNone { get; } }
public readonly record struct Result      { bool IsSuccess, IsFailure; ErrorCode Error; string? Detail; static Result Ok(); static Result Fail(ErrorCode e, string? detail = null); }
public readonly record struct Result<T>   { bool IsSuccess, IsFailure; T? Value; ErrorCode Error; string? Detail; static Result<T> Ok(T v); static Result<T> Fail(ErrorCode e, string? detail = null); Map/Bind/OrElse; implicit operator Result<T>(T) }
// ErrorCodes (161 today): `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);` — inside `#region <context>`. Server phases ADD a new #region; never rename/remove.
// Already declared & reusable: ValidationFailed, NotFound, ConcurrencyConflict("CONCURRENCY_CONFLICT",Conflict), PermissionDenied, PermissionUnknown, TenantMismatch("TENANT_MISMATCH",Forbidden),
//   IdempotencyFingerprintMismatch(Integrity), DependencyUnsettled("DEPENDENCY_UNSETTLED",Conflict), EntitlementRequired(Entitlement), TenantSuspended, UserDisabled, UserLockedOut, DeviceRevoked,
//   CredentialsInvalid, SessionExpired, PasswordPolicyViolation, SettlementInvalidTransition, CommandAlreadySettled, HandlerNotRegistered, DispatchFailed, Unexpected
```
```csharp
// Domain Tenant (A03) — mapped by this phase (private setters are EF-mappable, same as the SQLite context)
public sealed class Tenant : AggregateRoot {
    public const string DefaultTimezone = "Asia/Tehran"; public const string LocalDefaultId = "T-LOCAL-DEFAULT";
    public string DisplayName { get; private set; } public string? LegalName { get; private set; } public string Timezone { get; private set; }
    public string DefaultCurrency { get; private set; } public TenantStatus Status { get; private set; }   // enum TenantStatus (A03)
    public static Tenant Create(/* A03 factory — read its signature from Tenant.cs ONLY if you need to seed in tests */);
    public void SetStatus(TenantStatus s); public void Rename(string displayName, string? legalName = null);
}
// Application seam already used by handlers (B01):  IUnitOfWork (see S01 reference) — the PG implementation is S03.
```
`Animora.Server/Hosting/IReadinessProbe` (S01): `string Name {get;} Task<ReadinessResult> CheckAsync(CancellationToken ct)`; `ServerOptions.DataRootPath` (S01).

## §Design decisions

- **New project** `src/Animora.Persistence.PostgreSql` (`Microsoft.NET.Sdk`), refs `Animora.Domain` + `Animora.Application` + `Animora.Contracts.Wire`. Pins (exact, central): `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3**, `Npgsql` **10.0.3**, `Microsoft.EntityFrameworkCore` (existing 10.0.12), `Microsoft.EntityFrameworkCore.Design` (existing, `PrivateAssets=all`), `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` **10.0.12**. `Animora.Server` gains a `ProjectReference` to it. `InternalsVisibleTo Animora.Server.Tests`.
- **`ITenantContext`** (`Animora.Persistence.PostgreSql.Tenancy`): `string? TenantId {get;}` and `bool IsOperator {get;}`. A `NullTenantContext` (TenantId null) is the default registration; S05 registers the request-bound one. **A tenant-filtered query with a null tenant throws `TenantContextMissingException`** (never returns cross-tenant rows) — implemented in the filter expression via `EF.Property`‑free closure: the context exposes `string CurrentTenantId => _tenant.TenantId ?? throw new TenantContextMissingException()`, and the filter is `e => e.TenantId == CurrentTenantId` (EF re-evaluates per query).
- **Named filters (EF 10):** in `OnModelCreating`, for every entity type assignable to `SyncableEntity`: `HasQueryFilter("Tenant", e => e.TenantId == CurrentTenantId)` and `HasQueryFilter("SoftDelete", e => !e.Deleted)`. Helper `ServerModelConventions.ApplyTenancy(ModelBuilder, Func<string> tenantAccessor)`. A maintenance path may call `IgnoreQueryFilters(["SoftDelete"])` only.
- **`IgnoreQueryFilters()` ban:** (a) `ServerSourceScanTests` greps `src/Animora.Persistence.PostgreSql`, `src/Animora.Server` for `IgnoreQueryFilters()` with no argument ⇒ fail; (b) runtime `NoUnnamedIgnoreFiltersInterceptor : IQueryExpressionInterceptor` throws `InvalidOperationException("IgnoreQueryFilters() without names is banned")` when the expression tree contains the unnamed overload or a named list containing `"Tenant"` while `ITenantContext.IsOperator` is false.
- **Write stamping:** `TenantStampingInterceptor : SaveChangesInterceptor` — for every added/modified `SyncableEntity`: if `entry.TenantId` is empty ⇒ set from context; if non-empty and `!= context.TenantId` ⇒ add a `TenantMismatchSignal` and throw `TenantMismatchException` (→ `ErrorCodes.TenantMismatch`; the caller (S06/S05) writes the security audit row — this phase only throws). Never copies a tenant from the incoming entity. Operators (`IsOperator`) may only write `Tenant` rows.
- **UUID `Version`:** every `SyncableEntity` table has `"Version" uuid NOT NULL DEFAULT gen_random_uuid()` and a `BEFORE INSERT OR UPDATE` row trigger `trg_<table>_version` calling function `animora_set_version()` (`NEW."Version" := gen_random_uuid(); RETURN NEW;`). EF mapping: `Property(e=>e.Version).HasColumnType("uuid").HasConversion(StringGuidConverter).ValueGeneratedOnAddOrUpdate().IsConcurrencyToken()`; **not** `IsRowVersion()`/`xmin`. `StringGuidConverter` = `ValueConverter<string?,Guid>` (null ⇄ `Guid.Empty` is **not** allowed — column is NOT NULL; the in-memory property is `null` only before first save). Wire form = canonical lowercase hyphenated string (`EntityVersionCodec.ToWire(Guid)`/`TryParse`, `Animora.Persistence.PostgreSql.Concurrency`, equality only).
- **Migration helper** `VersionTrigger.Apply(MigrationBuilder mb, string table)` and `VersionTrigger.Drop(...)` (static, public) — every later server migration that creates a `SyncableEntity` table calls it. `animora_set_version()` itself is created once in migration 101.
- **Column conventions (CONVENTIONS §5):** `Id varchar(26)`, `TenantId varchar(26)`, `UpdatedAt timestamptz(6)` (UTC offset 0 via `DateTimeOffset` ⇄ UTC converter; `Npgsql.EnableLegacyTimestampBehavior` **never** set), enums as `integer`, `Money` as `<N>Minor bigint` + `<N>Currency varchar(3)`. Every tenant-scoped index has **`TenantId` first**: helper `ServerModelConventions.TenantIndex(entity, cols…)` names `IX_<Table>_<Cols>`. Soft delete = `Deleted boolean NOT NULL DEFAULT false`.
- **Tables in migration `101_ServerFoundation`:** function `animora_set_version()`; table `Tenants` (`Id`,`TenantId` (= own Id, so the generic filter applies; operators read via operator context), `DisplayName`,`LegalName`,`Timezone`,`DefaultCurrency`,`Status`,`UpdatedAt`,`Version`,`Deleted`) + unique index `UX_Tenants_Id`… (PK only) + `IX_Tenants_TenantId_Status`; trigger via `VersionTrigger.Apply`. Migrations history table `__EFMigrationsHistory` (default) in schema `public`.
- **Key ring:** `AnimoraKeyRingDbContext : DbContext, IDataProtectionKeyContext` (`DbSet<DataProtectionKey> DataProtectionKeys`), own history table `__EFMigrationsHistory_KeyRing`, its own tiny migration folder `Migrations/KeyRing`, ordinal `101_KeyRing`. `Program.cs` step 3 becomes `AddDataProtection().SetApplicationName("Animora.Server").PersistKeysToDbContext<AnimoraKeyRingDbContext>()`. A missing/unreachable key ring ⇒ `KeyRingProbe` unhealthy ⇒ readiness 503 and (S04+) auth routes refuse; **no in-memory fallback** (A-RULE 027).
- **Pools (ARCH §PG.4):** `DatabaseOptions` (section `Database`): `ConnectionString` (**from env `Database__ConnectionString` only** — `SecretGuard` already forbids JSON), `OltpWriteMax` (default 8), `OltpReadMax` (default 4), `KeyRingMax` (default 1), `ProfileGiB` (2|4, default 2). Validator: sum ≤ **12** when `ProfileGiB=2`, ≤ **20** when 4; each ≥ 1; `MinPoolSize=0` forced on every data source. Three `NpgsqlDataSource`s registered as keyed singletons (`"oltp-write"`, `"oltp-read"`, `"keyring"`); `AnimoraServerDbContext` uses `oltp-write`; read-only/no-tracking contexts use `oltp-read` (S03 consumes). `ApplicationName=Animora.Server`, `CommandTimeout` 30 s, `EnableRetryOnFailure(3, 5 s, null)` **only** via the S03 `ExecutionStrategy` wrapper — do not enable blindly here.
- **Probes:** `DatabaseProbe` (`SELECT 1`, 2 s timeout, also verifies `animora_set_version` exists), `KeyRingProbe` (key ring readable). Names `database`, `keyring`.
- **Migrations at deploy time (CONVENTIONS §5):** runtime startup never calls `Migrate()`; it only checks the applied head equals `ServerSchemaVersion.ExpectedHead` ("101_ServerFoundation" for the main context) → readiness probe `schema` unhealthy otherwise. A CLI verb `Animora.Server migrate` is **not** added here (deployment runbook is G09); migrations are applied with the `dotnet-ef` bundle.

## §Implementation plan

1. `source scripts/sandbox/_env.sh`; confirm `ls src/Animora.Persistence.PostgreSql` absent. Add pins to `Directory.Packages.props`.
2. Create the project + add to `Animora.sln` (`dotnet sln Animora.sln add src/Animora.Persistence.PostgreSql/Animora.Persistence.PostgreSql.csproj`); add the `ProjectReference` from `Animora.Server`.
3. Write `Tenancy/` (`ITenantContext`, `NullTenantContext`, `TenantContextMissingException`, `TenantMismatchException`, `TenantStampingInterceptor`, `NoUnnamedIgnoreFiltersInterceptor`), `Concurrency/` (`StringGuidConverter`, `EntityVersionCodec`, `VersionTrigger`), `Conventions/ServerModelConventions.cs`, `Options/DatabaseOptions.cs` + validator, `Context/AnimoraServerDbContext.cs`, `Context/AnimoraServerDbContextFactory.cs` (design-time, reads `ANIMORA_PG_DESIGN_CONNECTION`, falls back to a dummy local string — never contacts a DB), `Configurations/TenantConfiguration.cs`, `KeyRing/AnimoraKeyRingDbContext.cs`, `DependencyInjection/PersistenceServiceCollectionExtensions.cs` (`AddAnimoraPostgreSql(IServiceCollection, IConfiguration)`).
4. Generate migrations (design-time needs no live DB): `.sandbox/tools/dotnet-ef migrations add 101_ServerFoundation --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server` and `.sandbox/tools/dotnet-ef migrations add 101_KeyRing --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraKeyRingDbContext --output-dir Migrations/KeyRing`. Success: the `.cs` files appear. Then **hand-edit** `101_ServerFoundation.Up` to start with `migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION animora_set_version() RETURNS trigger AS $$ BEGIN NEW.""Version"" := gen_random_uuid(); RETURN NEW; END; $$ LANGUAGE plpgsql;")` and end with `VersionTrigger.Apply(migrationBuilder, "Tenants")`; `Down` drops trigger then function. If the tool is absent run `scripts/sandbox/install-prereqs.sh` once, then retry; never `dotnet tool install`.
5. Edit `Program.cs`: step 3 (DataProtection → key-ring context), step 4 (`AddAnimoraPostgreSql`), register `DatabaseProbe`/`KeyRingProbe`/`SchemaProbe`; edit `Hosting/ServerOptions` none. Production config note in `appsettings.json`: only `Database:ProfileGiB`, pool sizes.
6. Tests (below) in `tests/Animora.Server.Tests` (+ a `PgFixture` that reads `ANIMORA_PG_TEST_CONNECTION`, creates a uniquely named schema/database per run, skips with reason when unset, fails when `ANIMORA_REQUIRE_PG=1`). Pure tests (no DB) cover converters, validator, source scans and model metadata (`AnimoraServerDbContext` built with a dummy connection string, `context.Model` inspected only).
7. `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green (PG tests show *skipped* locally, which is expected); `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
8. `PROGRESS.md` per recipe (record migration heads: server `101_ServerFoundation`, key ring `101_KeyRing`); commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `Directory.Packages.props`, `Animora.sln`, `src/Animora.Server/Animora.Server.csproj`, `src/Animora.Server/Program.cs` (edit) | pins, project, reference, steps 3–4 + probes |
| `src/Animora.Persistence.PostgreSql/Animora.Persistence.PostgreSql.csproj` | project |
| `.../Tenancy/*.cs`, `.../Concurrency/*.cs`, `.../Conventions/ServerModelConventions.cs` | tenancy + version mechanism |
| `.../Options/DatabaseOptions.cs`, `DatabaseOptionsValidator.cs`, `.../DependencyInjection/PersistenceServiceCollectionExtensions.cs` | pool budget + registration |
| `.../Context/AnimoraServerDbContext.cs`, `AnimoraServerDbContextFactory.cs`, `.../Configurations/TenantConfiguration.cs` | context + Tenant mapping |
| `.../KeyRing/AnimoraKeyRingDbContext.cs` | Data Protection store |
| `.../Migrations/Server/*101_ServerFoundation*`, `.../Migrations/KeyRing/*101_KeyRing*` | generated + hand-edited |
| `src/Animora.Server/Hosting/DatabaseProbe.cs`, `KeyRingProbe.cs`, `SchemaProbe.cs` | readiness |
| `tests/Animora.Server.Tests/Persistence/*.cs`, `PgFixture.cs` | tests |

## Contracts / schema added

Tables: `Tenants`. Functions: `animora_set_version()`. Triggers: `trg_Tenants_version`. Key-ring table `DataProtectionKeys`. Public seams for later phases: `ITenantContext`, `ServerModelConventions.ApplyTenancy/TenantIndex`, `VersionTrigger.Apply/Drop`, `EntityVersionCodec`, `DatabaseOptions`, keyed `NpgsqlDataSource`s `oltp-write|oltp-read|keyring`. No new `ErrorCodes` (reuses `TenantMismatch`).

## Rules that apply here

- **Layering (CONVENTIONS §2, `ARCHITECTURE.md` §6):** `Animora.Domain` references nothing; `Animora.Application` → Domain + Contracts + Contracts.Wire;
  only `Animora.Persistence.PostgreSql` may reference Npgsql/`UseNpgsql`; `Animora.Server` must never reference `Animora.Client.Services`,
  `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Sync.Custom` or `Animora.Desktop`; the client never references a server project.
- **C# style (§4):** `sealed` by default; `sealed record` DTOs; nullable honored (no `!` without a why-comment); every async public method takes
  `CancellationToken ct = default` and `ConfigureAwait(false)` in library projects; no `.Result`/`.Wait()`; expected failures return `Result`/`Result<T>`
  with an `ErrorCode`, never exceptions; `IClock`/`TimeProvider` injected (no `DateTime.Now`/`UtcNow`); ids are ULID strings via `IdGenerator`, never `Guid` in business code.
- **EF (§5):** EF Core 10 only; no lazy loading/proxies; reads `AsNoTracking()` + projections; named filters `"Tenant"` and `"SoftDelete"` kept separable;
  paging mandatory (max page 200); migrations additive and never edited later; `EnsureCreated` forbidden outside test fixtures.
- **Security (§7):** Authn → Authz → Entitlement in that order, never merged; `TenantId` resolved server-side from membership only; secrets never in config
  files/logs; defaults fail closed; no custom crypto; append-only audit for every security/financial action.

## Out of scope (owning phase)

- Tenant-scoped repositories, `IUnitOfWork` for PostgreSQL, cross-tenant isolation tests → **S03**.
- Identity tables, membership, roles, devices → **S04/S05**. Command idempotency store → **S06**. Journal tables/triggers → **S11**.
- RLS (`set_config('animora.tenant_id',…)`) is **not** enabled — gated hardening, only after G07 passes (ROADMAP §5). The hook `ITenantSessionInitializer` is S03.
- Backup/restore + PITR runbook → **G09**; mandatory PostgreSQL CI → **G07**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
2. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c 101_ServerFoundation` ≥ 1 and `…/KeyRing | grep -c 101_KeyRing` ≥ 1.
3. `grep -rln "Npgsql" src --include=*.csproj --include=*.cs | grep -v Animora.Persistence.PostgreSql` lists **only** `Animora.Server` DI wiring files (`Program.cs`/`Hosting`), never Domain/Application/Desktop/Client/Sync.
4. `grep -rn "IgnoreQueryFilters()" src/Animora.Persistence.PostgreSql src/Animora.Server` returns nothing; `grep -rn "IsRowVersion\|xmin" src/Animora.Persistence.PostgreSql` returns nothing; `grep -rn "EnableLegacyTimestampBehavior" src` returns nothing.
5. Pure tests pass: `DatabaseOptionsValidatorTests.RejectsPoolSumOver12_On2GiB`, `…Allows20_On4GiB`, `EntityVersionCodecTests.RoundTrips_CanonicalLowercase`, `ServerModelTests.EveryTenantIndex_StartsWithTenantId`, `ServerModelTests.EverySyncableEntity_HasNamedTenantAndSoftDeleteFilters`, `ServerModelTests.VersionIsConcurrencyToken_NotRowVersion`, `NoUnnamedIgnoreFiltersInterceptorTests.Throws_OnUnnamedIgnore`, `TenantStampingInterceptorTests.Stamps_FromContext_Never_FromEntity`, `TenantStampingInterceptorTests.Mismatch_Throws_TenantMismatch`, `TenantContextTests.NullTenant_Throws_TenantContextMissing`.
6. PG-gated tests (skip locally, mandatory in G07): `VersionTriggerTests.Version_ChangesOnEveryUpdate_IncludingRawSql`, `MigrationTests.Applies101_OnEmptyDatabase`, `KeyRingTests.KeysSurviveContextRecreation`.
7. `git diff --stat -- src/Animora.Persistence.Sqlite src/Animora.Domain src/Animora.Desktop` empty.

## PROGRESS.md update

At the end of this phase: set the `S02` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S03`**,
append one `### S02` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S02 server data layer & tenancy i` and sync per the repo git workflow.
