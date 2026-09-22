# Phase A09 — Local persistence I: DbContext + core mappings

**Band:** M · **Track:** A · **Depends on:** A03, A05 · **Satisfies:** R-B02, R-B24
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §4, §5, §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `STACK.md` §3.3 (local store, money/time storage), §6.4 (named filters), §15 (T-RULE 003/004/013)
- `ARCHITECTURE.md` §11.3 (read vs write models), §11.4 (concurrency)
- `src/Animora.Domain/Primitives/*`, `src/Animora.Domain/ValueObjects/Money.cs`,
  `src/Animora.Domain/ValueObjects/Quantity.cs`, `src/Animora.Domain/Time/IClock.cs`
- `src/Animora.Domain/Identity/*`, `src/Animora.Domain/Patients/*`, `src/Animora.Domain/Clinical/*`,
  `src/Animora.Domain/Scheduling/*`, `src/Animora.Domain/Boarding/*`
- `src/Animora.Persistence.Sqlite/Animora.Persistence.Sqlite.csproj`

## Goal
Create the real local SQLite store: `AnimoraLocalDbContext`, the shared conventions (value converters,
naming, indexes, named query filters, local-only detection) and the mappings + first migration for the
A03–A05 entities. After this phase a real `animora.db` file can be created and migrated; nothing in the
desktop consumes it yet.

## Design decisions (pre-made — implement, do not re-derive)
- **Folders:** `src/Animora.Persistence.Sqlite/` → `Context/`, `Configurations/Identity/`,
  `Configurations/Patients/`, `Configurations/Clinical/`, `Configurations/Scheduling/`,
  `Configurations/Boarding/`, `Conventions/`, `Converters/`, `Migrations/`, `Options/`.
- `Options/LocalStoreOptions.cs` — `sealed class LocalStoreOptions`:
  `string DatabasePath` (default `%LOCALAPPDATA%/Animora/animora.db`, macOS
  `~/Library/Application Support/Animora/animora.db`), `bool EncryptionEnabled` (default `false` until
  A11 flips the seam), `int BusyTimeoutMs = 5000`, `int MaxPageSize = 200`. Bound via
  `Microsoft.Extensions.Options`. Section name `"LocalStore"`.
- `Context/AnimoraLocalDbContext.cs` — `sealed class AnimoraLocalDbContext : DbContext`:
  - ctor `(DbContextOptions<AnimoraLocalDbContext> options, ILocalTenantContext tenant, IClock clock)`.
  - `OnModelCreating`: `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnimoraLocalDbContext).Assembly)`
    (assembly scan happens **once at model build**, never per query — PERFORMANCE.md §3 allows this),
    then `ModelConventions.Apply(modelBuilder)`.
  - `SaveChangesAsync` override: for every `Added`/`Modified` `SyncableEntity` set
    `UpdatedAt = clock.UtcNow` and, when `TenantId` is empty, stamp `tenant.TenantId`; a
    `SyncableEntity` whose `TenantId` differs from `tenant.TenantId` throws
    `InvalidOperationException` (defect, not `Result`). Domain-event dispatch is **not** here (A10).
  - **No** `EnsureCreated`, no seeding call (A10 owns seeding).
- `Context/ILocalTenantContext.cs` — `interface ILocalTenantContext { string TenantId { get; } }`;
  `Context/SingleTenantContext.cs` — `sealed class SingleTenantContext : ILocalTenantContext` returning
  the tenant id persisted in `LocalInstallation` (A11 fills it; until then the constant
  `"T-LOCAL-DEFAULT"`). Rationale: the local store is single-tenant per installation, but the filter
  exists from day one so no phase retrofits tenancy (ROADMAP §3.6).
- `Conventions/ModelConventions.cs` — static `Apply(ModelBuilder)`:
  1. Table name = entity name pluralized with a hand-written `Pluralize` helper (no dependency);
     entity `Pet` → table `Pets`.
  2. Every `string` key/FK property → `TEXT`, `MaxLength(26)` for id columns, `IsRequired()`.
  3. Every free-text property → explicit `MaxLength` from `FieldLengths` (below); no unbounded `TEXT`
     except `Note`/`Memo`/`Description`-suffixed properties, which get `MaxLength(2000)`.
  4. Every `SyncableEntity`: named query filters via
     `entity.HasQueryFilter("Tenant", e => e.TenantId == tenant.TenantId)` and
     `entity.HasQueryFilter("SoftDelete", e => !e.Deleted)` — **two separately named filters**
     (STACK.md §6.4). Index `IX_<Table>_TenantId_UpdatedAt` on `(TenantId, UpdatedAt)` for delta reads.
  5. Every enum → `HasConversion<int>()`.
  6. Every `DateTimeOffset`/`DateTimeOffset?` → `UtcTicksConverter`.
  7. Every `Money` property → `ComplexProperty` with columns `<Prop>Minor` (`INTEGER`) +
     `<Prop>Currency` (`TEXT(3)`); every `Quantity` → `<Prop>Value` (`TEXT` decimal-as-string via
     `DecimalStringConverter`) + `<Prop>Unit` (`TEXT(12)`).
  8. A type carrying `[LocalOnly]` gets **no** tenant/soft-delete filter and is registered in
     `ModelConventions.LocalOnlyEntityTypes` (a `IReadOnlySet<string>` of CLR type names) for E03.
- `Converters/UtcTicksConverter.cs` — `ValueConverter<DateTimeOffset, long>`:
  `d => d.ToUniversalTime().UtcTicks`, `t => new DateTimeOffset(t, TimeSpan.Zero)`. Plus
  `NullableUtcTicksConverter`.
- `Converters/DecimalStringConverter.cs` — `ValueConverter<decimal, string>` using
  `InvariantCulture` `"G29"`; SQLite has no decimal ordering, so any column needing ordering stores a
  separate scaled `long` — the only such column today is none (quantities are never sorted on).
- `Conventions/FieldLengths.cs` — `const int` catalog: `Id = 26`, `Code = 32`, `Name = 120`,
  `Phone = 20`, `NationalId = 16`, `Title = 200`, `ShortText = 400`, `LongText = 2000`,
  `Currency = 3`, `UnitCode = 12`.
- **Configurations added in this phase** (one file each, `<Entity>Configuration`):
  - Identity: `Tenant`, `User`, `Permission`, `RoleDefinition`, `StaffMembership`, `Device`.
    Unique indexes `IX_Users_TenantId_Username`, `IX_StaffMemberships_TenantId_UserId_Role`,
    `IX_Devices_TenantId_Fingerprint`. `CapabilitySnapshot` is an owned JSON column
    (`ToJson()` on `Device.Capabilities`).
  - Patients/Clinical: `Owner`, `Pet`, `WeightEntry`, `DocumentReference`, `Visit`,
    `VaccinationRecord`, `MedicalHistoryEntry`. `PetVitals` owned; `PrescriptionLine` and
    `VisitServiceLine` owned collections mapped `ToJson()` (they are never queried individually).
    Indexes: `IX_Owners_TenantId_Phone`, `IX_Owners_TenantId_FullName`, `IX_Pets_TenantId_OwnerId`,
    `IX_Pets_TenantId_Name`, `IX_Visits_TenantId_PetId_StartedUtc`,
    `IX_Visits_TenantId_State_StartedUtc`, `IX_VaccinationRecords_TenantId_DueUtc_State`,
    `IX_MedicalHistoryEntries_TenantId_PetId_OccurredUtc`.
  - Scheduling/Boarding: `Appointment`, `ReceptionQueueEntry`, `Room`, `BoardingServiceDef`,
    `BoardingStay`. `AppointmentSlot` owned (`StartUtc`/`EndUtc` columns);
    `StayItem`/`StayRoomAssignment` owned collections `ToJson()`.
    Indexes: `IX_Appointments_TenantId_StartUtc_DoctorUserId`,
    `IX_Appointments_TenantId_State_StartUtc`, `IX_Appointments_TenantId_PetId`,
    `IX_ReceptionQueueEntries_TenantId_State_EnqueuedUtc`,
    `IX_BoardingStays_TenantId_State_CheckInUtc`, `IX_Rooms_TenantId_Code` (unique).
  - **Relationship rule:** every FK into a filtered entity is modeled **optional**
    (`.IsRequired(false)` on the navigation) to avoid the EF Core required-navigation-with-filter
    pitfall (STACK.md §6.4); referential integrity is enforced by domain invariants, not by a
    required navigation.
  - `DeleteBehavior.Restrict` everywhere. No cascade delete in the local store (soft delete only).
- `Migrations/001_LocalCoreSchema` — created with `dotnet ef migrations add 001_LocalCoreSchema`
  (design-time factory below). **Never edited by a later phase.**
- `Context/LocalDbContextFactory.cs` — `sealed class LocalDbContextFactory :
  IDesignTimeDbContextFactory<AnimoraLocalDbContext>` pointing at a temp path, using
  `SingleTenantContext` + a fixed `IClock` stub. Design-time only.
- `Context/LocalStoreServiceCollectionExtensions.cs` — `AddAnimoraLocalStore(this IServiceCollection,
  Action<LocalStoreOptions>? configure = null)`: registers options, `ILocalTenantContext`,
  `AnimoraLocalDbContext` with `UseSqlite(connectionString)`, `EnableSensitiveDataLogging(false)`,
  `UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)` as the **default** (write paths opt in
  with `AsTracking()`), and a `SqliteConnectionInterceptor` applying the pragma baseline
  (`journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout`, `foreign_keys=ON`).
  Lifetime: `AddDbContext` scoped; the desktop resolves a scope per operation (A10 adds the factory).
- `Context/SqliteConnectionInterceptor.cs` — `DbConnectionInterceptor` executing the pragmas on
  `ConnectionOpenedAsync`. Encryption key application is a **no-op hook** (`IStoreKeyProvider`
  placeholder interface with a `NullStoreKeyProvider` default) that A11 replaces.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs` | create | path/encryption/timeout/page-size options |
| `src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs` | create | the local DbContext + stamping `SaveChangesAsync` |
| `src/Animora.Persistence.Sqlite/Context/ILocalTenantContext.cs` | create | local tenant id port |
| `src/Animora.Persistence.Sqlite/Context/SingleTenantContext.cs` | create | default single-tenant implementation |
| `src/Animora.Persistence.Sqlite/Context/LocalDbContextFactory.cs` | create | design-time factory for `dotnet ef` |
| `src/Animora.Persistence.Sqlite/Context/SqliteConnectionInterceptor.cs` | create | pragma baseline + key hook |
| `src/Animora.Persistence.Sqlite/Context/IStoreKeyProvider.cs` | create | key seam + `NullStoreKeyProvider` |
| `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` | create | `AddAnimoraLocalStore` |
| `src/Animora.Persistence.Sqlite/Conventions/ModelConventions.cs` | create | the 8 model-wide conventions |
| `src/Animora.Persistence.Sqlite/Conventions/FieldLengths.cs` | create | column-length constants |
| `src/Animora.Persistence.Sqlite/Converters/UtcTicksConverter.cs` | create | `DateTimeOffset` ↔ UTC ticks |
| `src/Animora.Persistence.Sqlite/Converters/DecimalStringConverter.cs` | create | decimal ↔ invariant string |
| `src/Animora.Persistence.Sqlite/Configurations/Identity/*.cs` | create | 6 configurations |
| `src/Animora.Persistence.Sqlite/Configurations/Patients/*.cs` | create | 4 configurations (Owner, Pet, WeightEntry, DocumentReference) |
| `src/Animora.Persistence.Sqlite/Configurations/Clinical/*.cs` | create | 3 configurations (Visit, VaccinationRecord, MedicalHistoryEntry) |
| `src/Animora.Persistence.Sqlite/Configurations/Scheduling/*.cs` | create | 2 configurations (Appointment, ReceptionQueueEntry) |
| `src/Animora.Persistence.Sqlite/Configurations/Boarding/*.cs` | create | 3 configurations (Room, BoardingServiceDef, BoardingStay) |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `001_LocalCoreSchema` + model snapshot |
| `src/Animora.Persistence.Sqlite/Animora.Persistence.Sqlite.csproj` | modify | add `Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `tests/Animora.Persistence.Tests/LocalMigrationTests.cs` | create | migration applies on a temp file DB; table/column/index assertions |
| `tests/Animora.Persistence.Tests/ConverterTests.cs` | create | UTC ticks round-trip, Money two-column round-trip, enum-as-int |
| `tests/Animora.Persistence.Tests/QueryFilterTests.cs` | create | both named filters + `IgnoreQueryFilters("SoftDelete")` keeps tenant filter |
| `tests/Animora.Persistence.Tests/LocalStoreTestFixture.cs` | create | temp-file DB fixture (create/migrate/dispose/delete) |

## Implementation steps
1. Add `LocalStoreOptions`, `FieldLengths`, the two converters and the `IStoreKeyProvider` seam.
2. Add `ILocalTenantContext` + `SingleTenantContext`, then `AnimoraLocalDbContext` with the stamping
   `SaveChangesAsync` override.
3. Write `ModelConventions.Apply` implementing conventions 1–8 in that order, collecting
   `LocalOnlyEntityTypes` as it walks the model.
4. Write the 18 entity configurations, each declaring only what the convention layer cannot infer:
   keys, owned/JSON shapes, indexes, max lengths beyond the defaults, optional navigations.
5. Add `SqliteConnectionInterceptor` + `LocalStoreServiceCollectionExtensions` +
   `LocalDbContextFactory`.
6. Generate `001_LocalCoreSchema` and commit the generated migration **and** the model snapshot.
7. Write `LocalStoreTestFixture` (temp directory, unique file name, `MigrateAsync`, delete on dispose)
   and the three test classes.
8. `LocalMigrationTests` asserts: migration applies from empty; re-applying is a no-op; every expected
   table exists; each named index exists (`PRAGMA index_list`); no column has SQLite type `REAL` or
   `NUMERIC` (proves no decimal/float money or time leaked).
9. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK` (desktop untouched).

## Contracts / schema added
- `AnimoraLocalDbContext`, `ILocalTenantContext`, `SingleTenantContext`, `LocalStoreOptions`,
  `IStoreKeyProvider`/`NullStoreKeyProvider`, `SqliteConnectionInterceptor`,
  `AddAnimoraLocalStore(...)`, `ModelConventions` (+ `LocalOnlyEntityTypes`), `FieldLengths`,
  `UtcTicksConverter`, `NullableUtcTicksConverter`, `DecimalStringConverter`.
- 18 `<Entity>Configuration` classes. Migration `001_LocalCoreSchema`.
- Named query filters `"Tenant"` and `"SoftDelete"`. No `Animora.Contracts` change, no DI change in
  `App.axaml.cs`.

## Rules that apply here
- **CONVENTIONS §5 / STACK.md §3.3** — no `decimal` or `REAL` money column; `Money` is two columns;
  time is UTC ticks.
- **STACK.md §6.4** — the two query filters are separately **named** so a maintenance path can lift
  `SoftDelete` only; required navigations into filtered entities are optional.
- **T-RULE 003** — EF Core 10 only; do not add an EF Core 11 or Datasync package here.
- **T-RULE 013** — no compiled models (named tenant filters are unproven with them).
- **A-RULE 013** — the tenant filter is unconditional; no configuration may omit it for a
  `SyncableEntity`.
- **A-RULE 009/010** — `UpdatedAt`/`Version` are stamped as transport bookkeeping only; no query
  orders business results by them.
- **PERFORMANCE.md §3** — assembly scanning happens once at model build, never per query.

## Out of scope
- Repositories, unit of work, domain-event dispatch, seeding → A10.
- Encryption, keystore, `sqlite_version()` assertion, backup/restore → A11.
- Mappings for Inventory/Purchasing/Sales/Cashier/Finance entities → A10 (migration `002`).
- Any `Animora.Contracts` implementation or DI swap → Track C.
- Local-only tables (`PendingCommand`, outbox, audit) → B02, B05.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `dotnet ef migrations list --project src/Animora.Persistence.Sqlite` shows exactly
  `001_LocalCoreSchema`.
- `LocalMigrationTests` green: migration applies to an empty temp file DB; all 18 tables and every
  named index from the design decisions exist; no `REAL`/`NUMERIC` column exists.
- `ConverterTests` green: a `DateTimeOffset` with a non-zero offset round-trips as the same instant in
  UTC; `Money(12_500, "IRT")` round-trips through `<Prop>Minor`/`<Prop>Currency`.
- `QueryFilterTests` green: a row of another tenant is invisible; a `Deleted` row is invisible;
  `IgnoreQueryFilters("SoftDelete")` reveals deleted rows but still hides the other tenant.
- `AnimoraLocalDbContext.SaveChangesAsync` throws when an entity carries a foreign `TenantId`.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`; the desktop still
  runs entirely on mocks and never opens the database.

## PROGRESS.md update
Mark A09 done; advance "Next phase" to `A10`; append a "Repo state" entry listing: the persistence
project's new files, `AnimoraLocalDbContext` + conventions + converters, the 18 configurations, the
named filters, migration `001_LocalCoreSchema`, the 4 test classes, no DI change, no mock deleted,
deviations.
