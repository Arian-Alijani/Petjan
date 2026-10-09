# Plan — Phase S02 (Server data layer & tenancy I)

> **Purpose of this file.** A self-contained, pre-digested execution checklist for Phase S02.
> A fresh AI session can execute it **top-to-bottom with almost no repo re-exploration**: every
> path, signature, exact string, and decision that was derived from reading the repo is baked in
> below. The source phase file is `backend-roadmap/phases/PHASE-S02-server-data-layer-tenancy-i.md`
> (do **not** edit or delete it — only the final task deletes *this* Plan file).
>
> **LOCKED GIT RULE (non-negotiable):** after finishing **each** task below, run
> `scripts/git/push.sh "feat(server): S02 — <short task desc>"`. If it exits `3`, call the
> `setup_github_environment` tool once and re-run. If a task produces a big/heavy fix, push it
> immediately with a clear message. **No push = project death.** Branch = `genspark_ai_developer`.

---

## 0. Pre-digested facts (read once — saves re-exploration)

### 0.1 Environment / build (do NOT probe)
- Build: `scripts/sandbox/build.sh fast` (quick) or `scripts/sandbox/build.sh full` (0 warn/0 err gate). **Never raw `dotnet build`** (mem-capped ~985 MiB).
- Test: `scripts/sandbox/test.sh Server` (filter).  Smoke: `scripts/sandbox/run.sh` → must print `SMOKE RUN OK`.
- One-time (idempotent): `scripts/sandbox/install-prereqs.sh`. `dotnet-ef` is at `.sandbox/tools/dotnet-ef` — **use that path**, never `dotnet tool install`.
- Env for EF/source scripts: `source scripts/sandbox/_env.sh` first.
- **FORBIDDEN**: full test suite / long builds for routine steps — use `fast` + targeted `Server` filter. Only run `full` + `run.sh` at the final verification task.

### 0.2 Existing tree (verified 2026-10-09)
- `src/` projects: `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions`, **`Animora.Server`** (S01). `Animora.Persistence.PostgreSql` does **not** exist yet (we create it).
- Tests project: `tests/Animora.Server.Tests` (exists from S01).
- Server migration sequence starts at **101** (client SQLite head is `031`, independent, untouched).
- Baseline: `CommandTypes` = live (88 declared, never freeze), `ErrorCodes` 161, `WireContract.Version` = 1.

### 0.3 `Animora.Server` current state (`src/Animora.Server/Program.cs`, S01)
Numbered startup block already present; S02 fills the **deferred** slots (do NOT reorder):
- `// 3. DataProtection → S02` (currently a comment placeholder).
- `// 4. DbContext → S02` (currently a comment placeholder).
- `// 5. Health checks:` currently registers only `DataRootWritableProbe`; S02 adds Database/KeyRing/Schema probes.
- `builder.Services.AddSingleton<IReadinessProbe, DataRootWritableProbe>();` is the registration pattern to copy.
- `appsettings.json` production note: only expose `Database:ProfileGiB` + pool sizes (never the connection string).
- Program uses top-level statements + `public partial class Program;` at end (keep).

### 0.4 `IReadinessProbe` contract (`src/Animora.Server/Hosting/IReadinessProbe.cs`) — **both are `internal`**
```csharp
internal sealed record ReadinessResult(bool IsHealthy) {
    public static ReadinessResult Healthy() => new(true);
    public static ReadinessResult Unhealthy() => new(false);
}
internal interface IReadinessProbe {
    string Name { get; }                                   // stable name in failed list
    Task<ReadinessResult> CheckAsync(CancellationToken ct); // never throw on expected failure
}
```
- New probes (`DatabaseProbe`, `KeyRingProbe`, `SchemaProbe`) live in `src/Animora.Server/Hosting/`, are `internal sealed`, implement `IReadinessProbe`, and are registered in Program.cs step 5 with the same `AddSingleton<IReadinessProbe, X>()` pattern. Probe names: `database`, `keyring`, `schema`.

### 0.5 Central package pins (`Directory.Packages.props`) — ADD these three `<PackageVersion>` lines (exact)
Already present & reusable: `Microsoft.EntityFrameworkCore` 10.0.12, `Microsoft.EntityFrameworkCore.Design` 10.0.12.
ADD to the "Backend track" `<ItemGroup>`:
```xml
<PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
<PackageVersion Include="Npgsql" Version="10.0.3" />
<PackageVersion Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="10.0.12" />
```

### 0.6 Domain types reused (from §Reference pack — do NOT redefine)
```csharp
namespace Animora.Domain.Primitives;
public abstract class SyncableEntity {               // base for ALL tenant/synced tables
    string Id { get; init; }           // 26-char ULID
    string TenantId { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    string? Version { get; set; }      // null only before first save; column is NOT NULL uuid
    bool Deleted { get; set; }
}
public abstract class AggregateRoot : SyncableEntity, IHasDomainEvents { ... }
public static class IdGenerator { public static string NewId(); } // 26-char ULID
namespace Animora.Domain.Time; public interface IClock { DateTimeOffset UtcNow { get; } }
```
```csharp
// Animora.Domain Tenant (A03) — mapped by this phase. Private setters ARE EF-mappable.
public sealed class Tenant : AggregateRoot {
    public const string DefaultTimezone = "Asia/Tehran";
    public const string LocalDefaultId  = "T-LOCAL-DEFAULT";
    public string DisplayName { get; private set; }
    public string? LegalName  { get; private set; }
    public string Timezone    { get; private set; }
    public string DefaultCurrency { get; private set; }
    public TenantStatus Status { get; private set; }   // enum TenantStatus (A03), mapped as integer
    public static Tenant Create(/* read Tenant.cs ONLY if seeding in a test */);
    public void SetStatus(TenantStatus s);
    public void Rename(string displayName, string? legalName = null);
}
```
- `ErrorCodes` reused (NO new codes this phase): `TenantMismatch("TENANT_MISMATCH", Forbidden)`, `ConcurrencyConflict`. `ErrorCode` struct in `Animora.Domain.Results`.

### 0.7 Layering rules (hard gates — tests enforce)
- Only `Animora.Persistence.PostgreSql` may reference Npgsql / call `UseNpgsql`.
- `Animora.Server` may reference the new PG project (DI wiring only: `Program.cs` / `Hosting/*`).
- `Animora.Server` must NEVER reference: `Animora.Client.Services`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Sync.Custom`, `Animora.Desktop`.
- New PG project refs: `Animora.Domain` + `Animora.Application` + `Animora.Contracts.Wire`. Add `InternalsVisibleTo Animora.Server.Tests`.
- C# style: `sealed` by default; async public methods take `CancellationToken ct = default` + `.ConfigureAwait(false)`; no `.Result`/`.Wait()`; expected failures → `Result`/`Result<T>`; `IClock`/`TimeProvider` injected (no `DateTime.Now`); ids are ULID strings, never `Guid` in business code (Guid allowed ONLY internally in the version codec/converter).

---

## 1. Task list (execute in order; push after each ✅)

> Status legend: `[ ]` todo · `[~]` in progress · `[x]` done. After marking a task `[x]`, **push** (locked rule).

---

### [x] T01 — Add package pins (`Directory.Packages.props`)
**Do:** Add the three `<PackageVersion>` lines from §0.5 into the Backend-track `<ItemGroup>` (near the EF Core 10 pins, line ~29-31). Keep exact versions; no floating ranges.
**Why:** PG + DataProtection EF store packages must resolve centrally before the project can reference them.
**Verify:** `grep -n "Npgsql\|DataProtection.EntityFrameworkCore" Directory.Packages.props` shows the 3 new lines.
**Push:** `scripts/git/push.sh "feat(server): S02 — add Npgsql + DataProtection EF package pins"`

---

### [x] T02 — Create `Animora.Persistence.PostgreSql` project + wire to solution & server
**Do:**
1. `source scripts/sandbox/_env.sh`; confirm absence: `ls src/Animora.Persistence.PostgreSql` → should error (not exist).
2. Create `src/Animora.Persistence.PostgreSql/Animora.Persistence.PostgreSql.csproj` (`<Project Sdk="Microsoft.NET.Sdk">`), with:
   - `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` — **match the style of an existing non-web lib csproj** (e.g. `src/Animora.Persistence.Sqlite/Animora.Persistence.Sqlite.csproj`; read it ONCE if unsure about common props/`Directory.Build.props` inheritance).
   - ProjectReferences: `..\Animora.Domain\Animora.Domain.csproj`, `..\Animora.Application\Animora.Application.csproj`, `..\Animora.Contracts.Wire\Animora.Contracts.Wire.csproj`.
   - PackageReferences (versions come from central pins — NO `Version=` attr): `Npgsql.EntityFrameworkCore.PostgreSQL`, `Npgsql`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets=all`), `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`.
   - `<ItemGroup><InternalsVisibleTo Include="Animora.Server.Tests" /></ItemGroup>`.
3. Add to solution: `dotnet sln Animora.sln add src/Animora.Persistence.PostgreSql/Animora.Persistence.PostgreSql.csproj`.
4. Add ProjectReference from `src/Animora.Server/Animora.Server.csproj` → the new PG project (add a line in its first `<ItemGroup>` of ProjectReferences).
**Why:** Isolates Npgsql to one project (layering gate #3) and makes it buildable + referenceable by the server.
**Verify:** `dotnet sln Animora.sln list | grep PostgreSql` present; `grep PostgreSql src/Animora.Server/Animora.Server.csproj` present.
**Push:** `scripts/git/push.sh "feat(server): S02 — scaffold Animora.Persistence.PostgreSql project + wire sln/server ref"`

---

### [x] T03 — Tenancy layer (`src/Animora.Persistence.PostgreSql/Tenancy/`)
Namespace `Animora.Persistence.PostgreSql.Tenancy`. Create files:
1. **`ITenantContext.cs`** — `public interface ITenantContext { string? TenantId { get; } bool IsOperator { get; } }`.
2. **`NullTenantContext.cs`** — `public sealed class NullTenantContext : ITenantContext { public string? TenantId => null; public bool IsOperator => false; }` (default DI registration; S05 replaces with request-bound one).
3. **`TenantContextMissingException.cs`** — `public sealed class TenantContextMissingException : Exception` (message: "No tenant in context for a tenant-filtered query.").
4. **`TenantMismatchException.cs`** — `public sealed class TenantMismatchException : Exception` (maps to `ErrorCodes.TenantMismatch` at the caller; carries attempted vs context tenant for the caller's audit, e.g. ctor `(string? contextTenant, string entityTenant)`).
5. **`TenantStampingInterceptor.cs`** — `public sealed class TenantStampingInterceptor : SaveChangesInterceptor`. On `SavingChanges`/`SavingChangesAsync`, iterate `ChangeTracker.Entries<SyncableEntity>()` where State is `Added` or `Modified`:
   - If `entry.Entity.TenantId` is null/empty ⇒ set it from `ITenantContext.TenantId` (if context tenant also null ⇒ throw `TenantContextMissingException`).
   - If non-empty AND `!= context.TenantId` ⇒ throw `TenantMismatchException` (do NOT copy tenant from the incoming entity, ever).
   - Operators (`IsOperator == true`) may ONLY write `Tenant` rows (entity type `Tenant`); writing any other type as operator ⇒ throw `TenantMismatchException`.
   - Takes `ITenantContext` via ctor injection.
6. **`NoUnnamedIgnoreFiltersInterceptor.cs`** — `public sealed class NoUnnamedIgnoreFiltersInterceptor : IQueryExpressionInterceptor`. Override `QueryCompilationStarting` (expression visitor): if the expression tree contains the **unnamed** `IgnoreQueryFilters()` overload, OR a named-list overload whose list contains `"Tenant"` while `ITenantContext.IsOperator` is false ⇒ throw `InvalidOperationException("IgnoreQueryFilters() without names is banned")`. (Named `IgnoreQueryFilters(["SoftDelete"])` is allowed.)
**Notes for implementer:** `SaveChangesInterceptor`/`IQueryExpressionInterceptor` live in `Microsoft.EntityFrameworkCore.Diagnostics`. Interceptors are registered on the DbContext in T08 (`AddInterceptors(...)`).
**Verify (build only):** `scripts/sandbox/build.sh fast` compiles the new project.
**Push:** `scripts/git/push.sh "feat(server): S02 — tenancy context, exceptions, stamping + ignore-filter-ban interceptors"`

---

### [x] T04 — Concurrency layer (`.../Concurrency/`)
Namespace `Animora.Persistence.PostgreSql.Concurrency`. Create:
1. **`StringGuidConverter.cs`** — `public sealed class StringGuidConverter : ValueConverter<string?, Guid>`. Model→DB: `s => s == null ? Guid.Empty : Guid.Parse(s)`; DB→Model: `g => g.ToString()` (canonical lowercase; `Guid.ToString()` default "D" is already lowercase hyphenated). **Column is NOT NULL** — in practice `Version` is null only pre-first-save, and the DB trigger always writes a value; the converter never persists `Guid.Empty` for a real row.
2. **`EntityVersionCodec.cs`** — `public static class EntityVersionCodec { public static string ToWire(Guid g) => g.ToString("D"); public static bool TryParse(string? s, out Guid g) => Guid.TryParse(s, out g); }` (wire form = canonical lowercase hyphenated; equality only, per phase).
3. **`VersionTrigger.cs`** — `public static class VersionTrigger`:
   - `public static void Apply(MigrationBuilder mb, string table)` → emits `CREATE TRIGGER trg_<table>_version BEFORE INSERT OR UPDATE ON "<table>" FOR EACH ROW EXECUTE FUNCTION animora_set_version();` (via `mb.Sql(...)`).
   - `public static void Drop(MigrationBuilder mb, string table)` → `DROP TRIGGER IF EXISTS trg_<table>_version ON "<table>";`.
   - The `animora_set_version()` function itself is created once in migration 101 (T10), NOT here.
**Verify:** `scripts/sandbox/build.sh fast` ok.
**Push:** `scripts/git/push.sh "feat(server): S02 — version concurrency codec, string<->guid converter, migration trigger helper"`

---

### [x] T05 — Model conventions (`.../Conventions/ServerModelConventions.cs`)
Namespace `Animora.Persistence.PostgreSql.Conventions`. `public static class ServerModelConventions`:
1. `public static void ApplyTenancy(ModelBuilder modelBuilder, Func<string> tenantAccessor)` — for every entity type assignable to `SyncableEntity`:
   - `HasQueryFilter("Tenant", e => e.TenantId == tenantAccessor())` and `HasQueryFilter("SoftDelete", e => !e.Deleted)` (EF 10 **named** filters, kept separable).
   - Map `Version`: `Property(e=>e.Version).HasColumnName("Version").HasColumnType("uuid").HasConversion<StringGuidConverter>().ValueGeneratedOnAddOrUpdate().IsConcurrencyToken()`. **Never** `IsRowVersion()`/`xmin`.
   - Columns (CONVENTIONS §5): `Id varchar(26)`, `TenantId varchar(26)`, `UpdatedAt timestamptz(6)` via a `DateTimeOffset`↔UTC converter (offset 0; **never** set `Npgsql.EnableLegacyTimestampBehavior`), `Deleted boolean NOT NULL DEFAULT false`, enums as `integer`.
   - Reflection note: iterate `modelBuilder.Model.GetEntityTypes()` filtered by `typeof(SyncableEntity).IsAssignableFrom(t.ClrType)`; apply the named filter via `modelBuilder.Entity(clrType).HasQueryFilter(name, lambda)` — build the lambda generically (use `EntityTypeBuilder` or the `Expression`-based overload). The filter MUST reference `tenantAccessor()` (the context's `CurrentTenantId` getter that throws `TenantContextMissingException` when null) so a null tenant throws instead of returning cross-tenant rows.
2. `public static void TenantIndex(EntityTypeBuilder entity, params string[] cols)` — creates an index with **`TenantId` first** then `cols`, named `IX_<Table>_<Cols>` (Cols joined, no TenantId in the name per the phase example `IX_Tenants_TenantId_Status`… — follow the phase's literal example: name includes TenantId first: `IX_Tenants_TenantId_Status`). Use `entity.HasIndex(new[]{"TenantId"}.Concat(cols).ToArray()).HasDatabaseName($"IX_{table}_TenantId_{string.Join("_", cols)}")`.
**Why:** single source for the tenancy/version/column shape reused by all later server migrations & contexts.
**Verify:** build fast ok.
**Push:** `scripts/git/push.sh "feat(server): S02 — ServerModelConventions (named filters, uuid version, tenant-first index)"`

---

### [x] T06 — Options + validator (`.../Options/`)
Namespace `Animora.Persistence.PostgreSql.Options`.
1. **`DatabaseOptions.cs`** — `public sealed class DatabaseOptions`, `public const string SectionName = "Database";`. Props:
   - `string ConnectionString { get; set; } = "";` (bound **only** from env `Database__ConnectionString`; `SecretGuard` already blocks JSON — do NOT put it in appsettings).
   - `int OltpWriteMax { get; set; } = 8;` · `int OltpReadMax { get; set; } = 4;` · `int KeyRingMax { get; set; } = 1;` · `int ProfileGiB { get; set; } = 2;` (2 or 4).
2. **`DatabaseOptionsValidator.cs`** — `public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>`. Rules:
   - Each of the three pool maxes `>= 1`.
   - `ProfileGiB` must be 2 or 4.
   - Sum `OltpWriteMax + OltpReadMax + KeyRingMax` ≤ **12** when `ProfileGiB==2`, ≤ **20** when `ProfileGiB==4`; else `ValidateOptionsResult.Fail(...)`.
   - (ConnectionString emptiness is a readiness concern, not necessarily a boot-fail here — keep the validator focused on the pool budget so the pure tests in §Acceptance pass: `RejectsPoolSumOver12_On2GiB`, `Allows20_On4GiB`.)
**Verify:** build fast ok.
**Push:** `scripts/git/push.sh "feat(server): S02 — DatabaseOptions + pool-budget validator"`

---

### [x] T07 — Tenant mapping (`.../Configurations/TenantConfiguration.cs`)
Namespace `Animora.Persistence.PostgreSql.Configurations`. `public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>`:
- Table `Tenants` (schema `public`). PK `Id` (`varchar(26)`). `TenantId varchar(26)` (= own Id so the generic Tenant filter applies).
- `DisplayName`, `LegalName` (nullable), `Timezone`, `DefaultCurrency varchar(3)`, `Status` (integer enum), `UpdatedAt timestamptz(6)`, `Version uuid` (shape applied by `ServerModelConventions`), `Deleted boolean default false`.
- Index via `ServerModelConventions.TenantIndex(builder, "Status")` → `IX_Tenants_TenantId_Status`.
- PK already gives the unique on `Id` (phase: `UX_Tenants_Id` = PK only — no extra unique index needed beyond the PK).
- Use `private set` accessors — EF maps them like the SQLite context already does; configure via `.Property(...)` metadata only (no public setters required).
**Verify:** build fast ok.
**Push:** `scripts/git/push.sh "feat(server): S02 — Tenant entity EF configuration"`

---

### [x] T08 — Main DbContext + design-time factory (`.../Context/`)
Namespace `Animora.Persistence.PostgreSql.Context`.
1. **`AnimoraServerDbContext.cs`** — `public sealed class AnimoraServerDbContext : DbContext`:
   - Ctor takes `DbContextOptions<AnimoraServerDbContext>` + `ITenantContext`. Store the tenant context.
   - `public string CurrentTenantId => _tenant.TenantId ?? throw new TenantContextMissingException();` (filter accessor; EF re-evaluates per query).
   - `DbSet<Tenant> Tenants`.
   - `OnModelCreating`: `ApplyConfigurationsFromAssembly(typeof(AnimoraServerDbContext).Assembly)` (picks up `TenantConfiguration`), then `ServerModelConventions.ApplyTenancy(modelBuilder, () => CurrentTenantId)`.
   - `OnConfiguring` is NOT where the data source is chosen (that's DI/T09); but ensure interceptors (`TenantStampingInterceptor`, `NoUnnamedIgnoreFiltersInterceptor`) are added — prefer registering them via DI `AddInterceptors` in `AddDbContext` options (T09) so they receive `ITenantContext`.
2. **`AnimoraServerDbContextFactory.cs`** — `public sealed class AnimoraServerDbContextFactory : IDesignTimeDbContextFactory<AnimoraServerDbContext>`:
   - Reads env `ANIMORA_PG_DESIGN_CONNECTION`; falls back to a **dummy local** string (e.g. `Host=localhost;Database=animora_design;Username=design;Password=design`). **Never contacts a DB** at design time (EF migrations don't need a live DB).
   - Builds `DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(conn)`; passes a `NullTenantContext` so the ctor is satisfiable.
**Why:** enables `dotnet-ef migrations add` (T10) with no live DB, and gives the runtime context.
**Verify:** build fast ok.
**Push:** `scripts/git/push.sh "feat(server): S02 — AnimoraServerDbContext + design-time factory"`

---

### [x] T09 — Key-ring context + DI extension (`.../KeyRing/`, `.../DependencyInjection/`)
1. **`KeyRing/AnimoraKeyRingDbContext.cs`** (namespace `Animora.Persistence.PostgreSql.KeyRing`) — `public sealed class AnimoraKeyRingDbContext : DbContext, IDataProtectionKeyContext`:
   - `public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();`
   - Own history table: in `OnModelCreating` or via migrations config, use `__EFMigrationsHistory_KeyRing` (set on the context's model config; EF-ef tool `--context` isolates its own history — ensure the migration uses the separate history table name). Ctor takes `DbContextOptions<AnimoraKeyRingDbContext>`.
   - `IDataProtectionKeyContext` + `DataProtectionKey` are in `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`.
2. **`DependencyInjection/PersistenceServiceCollectionExtensions.cs`** (namespace `Animora.Persistence.PostgreSql.DependencyInjection`) — `public static class PersistenceServiceCollectionExtensions`, method `public static IServiceCollection AddAnimoraPostgreSql(this IServiceCollection services, IConfiguration configuration)`:
   - Bind `DatabaseOptions` (section `Database`) + `.ValidateOnStart()`; register `DatabaseOptionsValidator` as `IValidateOptions<DatabaseOptions>`.
   - Register `ITenantContext` → `NullTenantContext` as singleton (default; S05 overrides).
   - Register the two interceptors (so they can get `ITenantContext`): `TenantStampingInterceptor`, `NoUnnamedIgnoreFiltersInterceptor` (scoped/singleton as appropriate — interceptors resolving a scoped `ITenantContext` should be registered so DI can supply it; simplest: register interceptors as scoped and add via the options action that resolves from the service provider).
   - Three keyed `NpgsqlDataSource` singletons: keys `"oltp-write"`, `"oltp-read"`, `"keyring"`. Each built from a `NpgsqlDataSourceBuilder(connectionString)` with: `ApplicationName=Animora.Server`, `MaxPoolSize` = the matching option (`OltpWriteMax`/`OltpReadMax`/`KeyRingMax`), `MinPoolSize=0` **forced** on every source, `CommandTimeout=30`s (set via connection string or data source config). Do **NOT** enable `EnableRetryOnFailure` here (that's the S03 ExecutionStrategy wrapper).
   - `AddDbContext<AnimoraServerDbContext>(...)` using the **`oltp-write`** keyed data source (`UseNpgsql(dataSource)`), and `.AddInterceptors(stamping, noUnnamedIgnore)`.
   - `AddDbContext<AnimoraKeyRingDbContext>(...)` using the **`keyring`** keyed data source. Configure its `MigrationsHistoryTable("__EFMigrationsHistory_KeyRing")` in the `UseNpgsql` options.
   - (Read-only/no-tracking `oltp-read` context is consumed by S03 — just register the data source now.)
**Verify:** build fast ok.
**Push:** `scripts/git/push.sh "feat(server): S02 — key-ring context + AddAnimoraPostgreSql DI (keyed data sources, pools, interceptors)"`

---

### [x] T10 — Generate + hand-edit migrations (`.../Migrations/Server/`, `.../Migrations/KeyRing/`)
**Prereq:** `source scripts/sandbox/_env.sh`. If `.sandbox/tools/dotnet-ef` missing → `scripts/sandbox/install-prereqs.sh` once, then retry. **Never** `dotnet tool install`.
1. Generate server migration (next free ordinal = **101**; confirm `ls src/Animora.Persistence.PostgreSql/Migrations/Server` empty first):
```
.sandbox/tools/dotnet-ef migrations add 101_ServerFoundation \
  --project src/Animora.Persistence.PostgreSql \
  --startup-project src/Animora.Persistence.PostgreSql \
  --context AnimoraServerDbContext --output-dir Migrations/Server
```
2. Generate key-ring migration:
```
.sandbox/tools/dotnet-ef migrations add 101_KeyRing \
  --project src/Animora.Persistence.PostgreSql \
  --startup-project src/Animora.Persistence.PostgreSql \
  --context AnimoraKeyRingDbContext --output-dir Migrations/KeyRing
```
3. **Hand-edit `101_ServerFoundation.Up(MigrationBuilder migrationBuilder)`:**
   - **First** statement: create the function —
     ```csharp
     migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION animora_set_version() RETURNS trigger AS $$ BEGIN NEW.""Version"" := gen_random_uuid(); RETURN NEW; END; $$ LANGUAGE plpgsql;");
     ```
   - Ensure the `Tenants` table's `Version` column default is `gen_random_uuid()` and NOT NULL (`"Version" uuid NOT NULL DEFAULT gen_random_uuid()`); if the generator emitted it without the default, add `.Annotation`/`defaultValueSql: "gen_random_uuid()"` on that column.
   - **Last** statement of `Up`: `VersionTrigger.Apply(migrationBuilder, "Tenants");` (emits `trg_Tenants_version`).
   - **`Down`:** first `VersionTrigger.Drop(migrationBuilder, "Tenants");` then drop function `migrationBuilder.Sql("DROP FUNCTION IF EXISTS animora_set_version();");` then the generator's table drop.
4. The key-ring migration (`101_KeyRing`) usually needs no hand edit beyond confirming it targets `__EFMigrationsHistory_KeyRing` (guaranteed by the context's MigrationsHistoryTable config in T09).
**Verify (acceptance #2):** `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c 101_ServerFoundation` ≥ 1 AND `.../KeyRing | grep -c 101_KeyRing` ≥ 1. `scripts/sandbox/build.sh fast` ok.
**Push:** `scripts/git/push.sh "feat(server): S02 — migrations 101_ServerFoundation (function+trigger) + 101_KeyRing"`

---

### [x] T11 — Readiness probes + `ServerSchemaVersion` (`src/Animora.Server/Hosting/`)
Namespace `Animora.Server.Hosting`. All probes `internal sealed : IReadinessProbe` (match S01 pattern, §0.4):
1. **`DatabaseProbe.cs`** — Name `"database"`. `SELECT 1` with a **2 s** timeout against the `oltp-write` (or a dedicated) `NpgsqlDataSource`; ALSO verify `animora_set_version` exists (e.g. `SELECT 1 FROM pg_proc WHERE proname='animora_set_version'`). Any failure/timeout ⇒ `Unhealthy()` (catch expected Npgsql/timeout exceptions; never leak text).
2. **`KeyRingProbe.cs`** — Name `"keyring"`. Resolve `AnimoraKeyRingDbContext` and confirm the key ring is readable (e.g. `await db.DataProtectionKeys.AnyAsync(ct)` inside try/catch; existence of the table/readability is enough — zero keys is still healthy). Unreachable ⇒ `Unhealthy()` (no in-memory fallback — A-RULE 027).
3. **`SchemaProbe.cs`** — Name `"schema"`. Compare applied migration head vs expected. Add a small helper `ServerSchemaVersion` (in the PG project, namespace `Animora.Persistence.PostgreSql`, `public static class ServerSchemaVersion { public const string ExpectedHead = "101_ServerFoundation"; }`). Probe reads applied migrations (`db.Database.GetAppliedMigrationsAsync(ct)`) and is `Unhealthy()` if the last applied != `ExpectedHead`. Runtime startup NEVER calls `Migrate()` — only this probe checks.
**Note:** probes depend on DB; they must degrade to `Unhealthy` (not throw) when DB is down so the offline/no-DB smoke still boots (health just reports not-ready). `scripts/sandbox/run.sh` smoke must still print `SMOKE RUN OK` with no PostgreSQL — ensure probe construction doesn't force a connection at DI/build time.
**Verify:** build fast ok.
**Push:** `scripts/git/push.sh "feat(server): S02 — Database/KeyRing/Schema readiness probes + ServerSchemaVersion"`

---

### [x] T12 — Wire `Program.cs` steps 3/4/5 + appsettings note
**Do (edit `src/Animora.Server/Program.cs`; do NOT reorder the numbered block):**
- **Step 3** (replace the `// 3. DataProtection → S02` placeholder):
  ```csharp
  builder.Services.AddDataProtection()
      .SetApplicationName("Animora.Server")
      .PersistKeysToDbContext<AnimoraKeyRingDbContext>();
  ```
- **Step 4** (replace `// 4. DbContext → S02`):
  ```csharp
  builder.Services.AddAnimoraPostgreSql(builder.Configuration);
  ```
  (add `using Animora.Persistence.PostgreSql.DependencyInjection;` and `using Animora.Persistence.PostgreSql.KeyRing;` at top.)
- **Step 5**: after the existing `DataRootWritableProbe` registration, add:
  ```csharp
  builder.Services.AddSingleton<IReadinessProbe, DatabaseProbe>();
  builder.Services.AddSingleton<IReadinessProbe, KeyRingProbe>();
  builder.Services.AddSingleton<IReadinessProbe, SchemaProbe>();
  ```
- `appsettings.json`: add a `"Database"` section exposing ONLY `"ProfileGiB": 2`, `"OltpWriteMax": 8`, `"OltpReadMax": 4`, `"KeyRingMax": 1` — **no ConnectionString** (comment that it's env-only: `Database__ConnectionString`). Keep `appsettings.Development.json` consistent (no secret).
- Do NOT edit `Hosting/ServerOptions`.
**Verify:** `scripts/sandbox/build.sh fast` ok.
**Push:** `scripts/git/push.sh "feat(server): S02 — Program.cs DataProtection+DbContext+probes wiring, appsettings Database note"`

---

### [x] T13 — Tests (`tests/Animora.Server.Tests/Persistence/` + `PgFixture.cs`)
Read the existing test project ONCE (`tests/Animora.Server.Tests/*.csproj`) to confirm it references the PG project (add a ProjectReference to `Animora.Persistence.PostgreSql` if missing) and uses xUnit. Create:
1. **`PgFixture.cs`** — reads env `ANIMORA_PG_TEST_CONNECTION`; if unset → tests that need it **skip with a reason** (xUnit `Assert.Skip` / `[SkippableFact]` pattern, or a fixture flag the tests check). Under `ANIMORA_REQUIRE_PG=1` with no connection ⇒ **fail**. When present, creates a uniquely named schema/database per run.
2. **Pure tests (no DB — MUST pass locally; acceptance #5):**
   - `DatabaseOptionsValidatorTests.RejectsPoolSumOver12_On2GiB` ; `…Allows20_On4GiB`.
   - `EntityVersionCodecTests.RoundTrips_CanonicalLowercase`.
   - `ServerModelTests` (build `AnimoraServerDbContext` with a **dummy** conn string + `NullTenantContext`, inspect `context.Model` only — never open a connection): `EveryTenantIndex_StartsWithTenantId`, `EverySyncableEntity_HasNamedTenantAndSoftDeleteFilters`, `VersionIsConcurrencyToken_NotRowVersion`.
   - `NoUnnamedIgnoreFiltersInterceptorTests.Throws_OnUnnamedIgnore`.
   - `TenantStampingInterceptorTests.Stamps_FromContext_Never_FromEntity` ; `.Mismatch_Throws_TenantMismatch`.
   - `TenantContextTests.NullTenant_Throws_TenantContextMissing`.
3. **PG-gated tests (skip locally; acceptance #6):** `VersionTriggerTests.Version_ChangesOnEveryUpdate_IncludingRawSql`, `MigrationTests.Applies101_OnEmptyDatabase`, `KeyRingTests.KeysSurviveContextRecreation`.
**Verify:** `scripts/sandbox/test.sh Server` → green; PG-gated show **skipped** locally (expected).
**Push:** `scripts/git/push.sh "test(server): S02 — pure tenancy/version/validator tests + PG-gated fixture"`

---

### [x] T14 — Source-scan guard test (`ServerSourceScanTests`)
In `tests/Animora.Server.Tests` add `ServerSourceScanTests` (acceptance #4) that greps repo source and fails on violations:
- `IgnoreQueryFilters()` (unnamed) present anywhere in `src/Animora.Persistence.PostgreSql` or `src/Animora.Server` ⇒ fail.
- `IsRowVersion` or `xmin` present in `src/Animora.Persistence.PostgreSql` ⇒ fail.
- `EnableLegacyTimestampBehavior` present anywhere in `src` ⇒ fail.
- (If an S01 `ServerSourceScanTests`/layering test already exists, extend it instead of duplicating — check `tests/Animora.Server.Tests` for an existing scan/layering test first.)
**Verify:** `scripts/sandbox/test.sh Server` green.
**Push:** `scripts/git/push.sh "test(server): S02 — source-scan guard (no unnamed IgnoreQueryFilters / rowversion / legacy timestamp)"`

---

### [ ] T15 — Full verification gate (acceptance #1/#3/#7)
Run (ONLY here do the heavy full build + smoke):
1. `scripts/sandbox/build.sh full` → **0 warnings / 0 errors**.
2. `scripts/sandbox/run.sh` → **`SMOKE RUN OK`** (desktop offline, no PostgreSQL).
3. Acceptance #3: `grep -rln "Npgsql" src --include=*.csproj --include=*.cs | grep -v Animora.Persistence.PostgreSql` → must list **only** `Animora.Server` DI wiring (`Program.cs`/`Hosting`), never Domain/Application/Desktop/Client/Sync.
4. Acceptance #4: the four greps return nothing / only allowed matches.
5. Acceptance #7: `git diff --stat origin/main -- src/Animora.Persistence.Sqlite src/Animora.Domain src/Animora.Desktop` → **empty** (we touched none of those).
If any gate fails, fix (push the fix immediately with a clear `fix(server): S02 — <what>` message), then re-run.
**Push:** `scripts/git/push.sh "chore(server): S02 — full build/smoke/acceptance verification green"`

---

### [ ] T16 — Update `backend-roadmap/PROGRESS.md`
Per the phase's "PROGRESS.md update" section (append-only; never rewrite old entries):
- "Phase status" table: set `S02` row → `✅ Done` (add date `2026-10-09` + one-line summary).
- Advance **"Next phase" → `S03`** (update the `## Next phase` section text that currently says S02).
- Append ONE `### S02` entry to "Repo state log": what exists now — new project `Animora.Persistence.PostgreSql`; tables `Tenants`; function `animora_set_version()`; trigger `trg_Tenants_version`; key-ring table `DataProtectionKeys`; migration heads **server `101_ServerFoundation`**, **key ring `101_KeyRing`**; **no new ErrorCodes** (reuses `TenantMismatch`); public seams `ITenantContext`, `ServerModelConventions.ApplyTenancy/TenantIndex`, `VersionTrigger.Apply/Drop`, `EntityVersionCodec`, `DatabaseOptions`, keyed data sources `oltp-write|oltp-read|keyring`; test classes added (list from T13/T14).
- Log any deviation in "Decision & deviation log" (only if a snippet diverged from the tree during implementation).
**Push:** `scripts/git/push.sh "docs(server): S02 — PROGRESS.md status Done, Next S03, repo-state log"`

---

### [ ] T17 — (FINAL) Delete this Plan file
- `git rm Plan/PHASE-S02-TODO.md` (and `rmdir Plan` if it's now empty — optional).
- Do **NOT** touch the roadmap phase file `backend-roadmap/phases/PHASE-S02-...md` (leave it; it's out of this task's scope — only this Plan file is deleted).
**Push:** `scripts/git/push.sh "chore: S02 — remove completed Plan todo file"`

---

## 2. Final acceptance recap (all must hold before T17)
1. `build.sh full` 0/0 ; `run.sh` = `SMOKE RUN OK`.
2. `Migrations/Server` has `101_ServerFoundation` ; `Migrations/KeyRing` has `101_KeyRing`.
3. Npgsql confined to `Animora.Persistence.PostgreSql` (+ server DI wiring only).
4. No unnamed `IgnoreQueryFilters()`, no `IsRowVersion`/`xmin`, no `EnableLegacyTimestampBehavior`.
5. All 10 pure tests pass; 3 PG-gated tests skip locally.
6. `git diff --stat` on `Sqlite`/`Domain`/`Desktop` empty.
7. PROGRESS.md updated (S02 Done, Next S03).
