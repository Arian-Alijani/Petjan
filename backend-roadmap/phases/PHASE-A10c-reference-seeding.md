# Phase A10c — Idempotent first-run reference seeding (session 3/4 of A10)

**Track:** A · **Depends on:** A10b ✅ · **Next:** A10d

## Session rules
- Read ONLY this file + "Read only" files. No other phase files, UI, mocks, tests.
- Rules: `CONVENTIONS.md` §§5, 12 (seed rule: Seed/ module in Persistence.Sqlite, never Mocks).
- **No tests / no `test.sh`. No migration** (A10d). Never seed demo owners/pets/invoices, credentials or secrets.

## Read only
`Context/AnimoraLocalDbContext.cs` (DbSets), `Context/SingleTenantContext.cs`, `Context/LocalStoreServiceCollectionExtensions.cs`, `Repositories/LocalUnitOfWork.cs` (API only), `Domain/Primitives/LocalOnlyEntity.cs`; the domain catalogs only by name/signature: `Tenant`, `PermissionCodes`, `DefaultRolePermissions`, `RoleDefinition`, `ChartOfAccounts`, `ExpenseCategories`, `AppointmentTypeCatalog`, `VaccineSchedule`, `AccountingPeriod`, unit/category/room types.

## Tasks
1. `Seed/SeedMarker.cs` — `[LocalOnly] sealed class SeedMarker : LocalOnlyEntity` with `SeedKey`, `SchemaVersion`; unique index on `SeedKey`; configuration in `Seed/` (applied by the context).
2. `Seed/LocalSeeder.cs` — `Task<Result> SeedAsync(CancellationToken ct = default)` using the scoped context + UoW transaction. Each logical block: check its marker (indexed lookup, **not** `AnyAsync()` on a business table) → insert rows + marker in one transaction; rerun / partial / concurrent-safe via unique indexes. Propagate failed `Result`s from domain factories.
3. Blocks (exact content):
   - Default tenant **`T-LOCAL-DEFAULT`**: `Tenant.Create` generates a ULID, so add a narrow explicit bootstrap factory/path setting both `Id` and `TenantId` to the sentinel (reserved exception, documented). Never seed a tenant hidden by the filter.
   - Full `PermissionCodes` catalog + `DefaultRolePermissions` + all **5** `RoleDefinition`s.
   - `ChartOfAccounts.Default` **20** ledger accounts.
   - Units **PCS BOX ML MG G KG DOSE** (7); root product categories **MED VAC FOOD ACC SRV** (5); rooms **R-01…R-08** (8).
   - One current **open** `AccountingPeriod`: real UTC range from injected `IClock`; opaque persisted `PeriodCode` from a seed-time period-code provider/parameter (platform calendar boundary). No Jalali via Gregorian formatting, no hardcoded month, no wall clock in Domain/Application.
   - Static catalogs `ExpenseCategories` (9), `AppointmentTypeCatalog` (7), `VaccineSchedule` (6): if DB rows are needed add small `[LocalOnly]` reference rows in `Seed/` (domain stays the authority); otherwise document in code why they stay static and do not claim rows exist. Pick one, state it in PROGRESS.
4. Register `LocalSeeder` scoped (+ period-code provider) in `AddAnimoraLocalStore`. Do not call seeding from the desktop.

## Gate
- `build.sh full` = 0/0; `dotnet ef dbcontext info --project src/Animora.Persistence.Sqlite` succeeds (SeedMarker and any reference rows are in the model).

## Handoff
PROGRESS: row A10c ✅ + note (seed blocks, marker keys, tenant bootstrap path, catalog decision, period-code source); Next → `A10d`. Commit `feat(store): idempotent local reference seeding (A10c)`. Final answer: done / deviations / blockers.
