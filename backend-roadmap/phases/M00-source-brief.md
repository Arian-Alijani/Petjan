# M00 source brief (scratch — owned by PHASE-M00, deleted/kept by T9)

> Shared input for M00 authoring tasks T3–T8. Written by T1/T2. Never edit by a non-M00 phase.

## 1. Baseline snapshot (verified 2026-10-08 against the tree + `PROGRESS.md`)

| Fact | Value | How verified |
|---|---|---|
| Client migration head | **031** (`20261007120000__031_TreatmentPlans.cs`); ordinals `001`–`031` all physical | `ls src/Animora.Persistence.Sqlite/Migrations` |
| Next client ordinal | `032` — **but never hard-code**: list the folder and take the next free ordinal on the owning `DbContext` | PROGRESS "Post-C11 drift" |
| Server migration start | **101** (own sequence on the server `DbContext`; client stays on its own) | ROADMAP §3.5 |
| `CommandTypes` | **88 declared** in `Animora.Contracts.Wire.Commands.CommandTypes` (`All` lists 88). PROGRESS prose says 86 — stale; **the live file is the only truth**, phases say "derived from live `CommandTypes.cs` at execution time" and never freeze a count | `grep -c 'const string' CommandTypes.cs` = 88 |
| `CommandTypes` families (live) | sales 19 · finance 12 · inventory 10 · boarding 8 · patients 7 · vaccination 7 · reception 6 · visits 6 · purchasing 5 · scheduling 5 · cashier 3 | script over the file |
| `ErrorCodes` | **161** declared (`Animora.Domain.Results.ErrorCodes`, `static readonly ErrorCode … = new("CODE", ErrorCode.<Kind>)`) | `grep -c 'public static readonly'` |
| `src/` projects (9) | `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (empty scaffold: csproj refs Domain + Contracts.Wire, README only) | `ls src` |
| `tests/` projects (5) | `Animora.Domain.Tests`, `Animora.Application.Tests`, `Animora.Persistence.Tests`, `Animora.Server.Tests` (placeholder, no subject ref), `Animora.Sync.Tests` (placeholder) | `ls tests` |
| Wire version | `WireContract.Version = 1`, header `X-Animora-Contract-Version` | `WireContract.cs` |
| Post-C11 replicated aggregates | `Animora.Domain.Clinical.TreatmentPlan` (+ `TreatmentOccurrence`, `VaccineCatalogItem`) and `Animora.Domain.Finance.RecurringExpense` (+ `RecurringExpensePayment`) | PROGRESS "Post-C11 drift" |
| Post-C11 command families | `vaccination.treatment.*` (create/update/cancel/occurrence.done), `vaccination.catalog.add`, `finance.recurring.*` (add/edit/toggle/pay) | `CommandTypes.cs` |
| Statistics module | desktop-only (`Animora.Contracts/Statistics`); **no server/replication surface** | PROGRESS |
| Server projects | **none exist** — created by the S-phases; solution + `Animora.Server.Tests` reference added by S01 | `ls src` |

Environment (restate in every phase): build `scripts/sandbox/build.sh [fast|full]`, tests `scripts/sandbox/test.sh`,
run `scripts/sandbox/run.sh`; `dotnet-ef` at `.sandbox/tools` (never `dotnet tool install`). Server stack:
PostgreSQL 18 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, ASP.NET Core 10, EF Core 10 (pins land in S01/S02).
Forbidden tech: Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11, per-tenant database,
hard-coded TLS version, Native AOT with EF Core.

## 2. Regeneration map (29 phases: S01–S20 server, G01–G09 sync/integration/release)

Bands: all **M** except where marked **S**. No XL. Offline invariant (“desktop offline yes”) holds for every row.
`Depends on` cites only lower S/G IDs or completed A/B/C phases (`C11` = whole client baseline).

| New ID | Title | Band | Track | Depends on | R-B satisfied | Covers (ROADMAP §2 inventory) |
|---|---|---|---|---|---|---|
| S01 | Server host scaffold (`Animora.Server`, config, health, rate-limit policies `auth/commands/tables/files/operator`, solution + test wiring) | M | S | C11 | R-B05, R-B27 | D01 |
| S02 | Server data layer & tenancy I — PostgreSQL `ServerDbContext`, tenant discriminator + global filter, `ITenantContext`, migration **101** | M | S | S01 | R-B03, R-B11, R-B19 | D02 (half 1) |
| S03 | Server data layer & tenancy II — tenant-scoped repositories/UoW, tenancy guard (write-path stamping, cross-tenant tests) | S | S | S02 | R-B11, R-B19 | D02 (half 2, split to hold band) |
| S04 | Identity server — ASP.NET Core Identity, bearer + refresh, device-bound sessions | M | S | S03 | R-B04, R-B20 | D03 |
| S05 | Tenant context & authorization policies (Authn ≠ Authz ≠ Entitlement ordering, permission policies) | M | S | S04 | R-B04, R-B19, R-B20 | D04 |
| S06 | Business API — command intake & idempotency (server settlement, `ON CONFLICT` dedupe, `CommandFingerprint`, numeric→UUID version seam, audit) | M | S | S05 | R-B13, R-B14, R-B15, R-B29 | D05 |
| S07 | Server handlers I-a — patients, reception, visits, vaccination (incl. `TreatmentPlan`) | M | S | S06 | R-B01, R-B17 | D06 (half 1) |
| S08 | Server handlers I-b — scheduling & boarding | M | S | S07 | R-B01, R-B17 | D06 (half 2) |
| S09 | Server handlers II-a — sales & cashier | M | S | S08 | R-B01, R-B17, R-B22 | D07 (half 1) |
| S10 | Server handlers II-b — inventory, purchasing, finance (incl. `RecurringExpense`, cheques, ledger) | M | S | S09 | R-B01, R-B17, R-B22, R-B23 | D07 (half 2) |
| S11 | Commit-safe tenant journal & monotonic position (PostgreSQL critical-state replication write path; `TreatmentPlan` + `RecurringExpense` in the replicated set) | M | S | S06, S10 | R-B03, R-B11, R-B13 | D08 (half 1) |
| S12 | Immutable 256-KiB batch export (REPEATABLE READ), offline window + retention, GC crash atomicity, wire version gate | M | S | S11 | R-B03, R-B11, R-B31 | D08 (half 2) |
| S13 | Outbox, background workers, resilience (atomic claim, retry/breaker/timeout/bulkhead) | M | S | S10 | R-B07, R-B09, R-B25 | D09 |
| S14 | Subscription context & operator API | M | S | S05 | R-B05, R-B30 | D10 (half 1) |
| S15 | Self-service onboarding intake (async idempotent provisioning intent from the vendor website) | S | S | S14 | R-B30, R-B36 | D10 (half 2) |
| S16 | Licensing & anti-piracy (signed license, device fingerprint binding, offline grace, clock-rollback, fail-closed) | M | S | S15 | R-B06, R-B29, R-B30, R-B36 | D11 |
| S17 | Files — valet key + resumable transfer (`Staged→…→Available`, reconciliation job) | M | S | S13 | R-B26 | D12 |
| S18 | Server observability & weak-server performance (bounded pools, `MinPoolSize=0`, loopback OTLP, local-only telemetry) | M | S | S13 | R-B07, R-B27, R-B35 | D13 |
| S19 | Admin web I — operator panel core (`Animora.AdminWeb`, Blazor Web App Static-SSR-first, operator auth, tenant/subscription/license views) | M | S | S16 | R-B05 | E01 |
| S20 | Admin web II — insight seams (`ITenantInsightQuery` read-model port only; cross-tenant analytics deferred) | S | S | S19 | R-B05, R-B11, R-B35 | E02 |
| G01 | `Animora.Sync.Abstractions` — `IStateSyncEngine`/`ISyncEngine`, `SyncScope`, `SyncResult`, `NullSyncEngine` fallback + DI | S | G | S12 | R-B03, R-B13 | E03 (half 1) |
| G02 | Custom immutable-batch HTTP adapter (client refs **no** PostgreSQL driver, **no** server project) | M | G | G01 | R-B03, R-B13, R-B31 | E03 (half 2) |
| G03 | Sync orchestration & status (scheduler, `SyncCursor` integration, status surfaced to `ISyncStatusService`) | M | G | G02 | R-B03 | E04 |
| G04 | Conflict resolution & non-destructive pull (preserve pending intent; posted-invoice conflicts reversal-only) | M | G | G03 | R-B03, R-B17, R-B18 | E05 |
| G05 | Command transport & settlement reconciliation (replaces `NullCommandTransport`) | M | G | G04, S10 | R-B03, R-B09, R-B16 | E06 |
| G06 | Online auth, entitlement & device lifecycle (replaces `AllowAllEntitlementGate`; credential-mirror fallback) | M | G | G05, S16 | R-B04, R-B06, R-B20, R-B21, R-B36 | E07 |
| G07 | Validation gates I — mandatory PostgreSQL CI, tenancy/RLS gate, restore gate (`STACK.md` §12, ARCH §19) | M | G | G06, S18 | R-B19, R-B32 | E08 (half 1) |
| G08 | Validation gates II — performance proof & offline budget (weak-server/weak-client capacity, layering audit) | M | G | G07, S20 | R-B07, R-B08, R-B10, R-B32 | E08 (half 2) |
| G09 | Release engineering (topology, install/upgrade/rollback runbook, backup/restore rehearsal, packaging, SBOM) | M | G | G08 | R-B27, R-B31, R-B33 | E09 |

**Totals:** S01–S20 (20) + G01–G09 (9) = **29 regenerated phases**. Bands: **S = 4** (S03, S15, S20, G01), **M = 25**, **XL = 0**.

**Phase-count note.** ROADMAP §0 caps the *original* 50 phases; M00 rule 1 states the regenerated phase *count is free,
per-phase weight is not*. With A (12) + B (5) + C (11) = 28 complete phases + 29 regenerated = **57 total**. T9 rewrites
ROADMAP §0/§1 totals accordingly and records the deviation (splits: D02, D06, D07, D08, D10, E03, E08 each into two).

### Coverage matrix (ROADMAP §2 D/E row → regenerated phase)

| Old row | New phase(s) | Old row | New phase(s) |
|---|---|---|---|
| D01 host | S01 | E01 admin I | S19 |
| D02 data+tenancy | S02, S03 | E02 insight seams | S20 |
| D03 identity | S04 | E03 engine + HTTP adapter | G01, G02 |
| D04 tenant ctx + authz | S05 | E04 orchestration | G03 |
| D05 command intake | S06 | E05 conflict | G04 |
| D06 handlers I | S07, S08 | E06 transport + settlement | G05 |
| D07 handlers II | S09, S10 | E07 online auth/entitlement/device | G06 |
| D08 journal/batch | S11, S12 | E08 gates + perf | G07, G08 |
| D09 outbox/workers | S13 | E09 release | G09 |
| D10 subscription/operator/onboarding | S14, S15 | | |
| D11 licensing | S16 | | |
| D12 files | S17 | | |
| D13 observability/perf | S18 | | |

Post-C11 drift coverage: `TreatmentPlan` handlers → S07; `RecurringExpense` handlers → S10; both in replicated set → S11/S12;
88-type `CommandTypes` handler inventory split S07 (patients/reception/visits/vaccination), S08 (scheduling/boarding),
S09 (sales/cashier), S10 (inventory/purchasing/finance); Statistics has no server surface (stated in S11 Out of scope).

### Artifact single-introducer table (ROADMAP §3.3 guard for T3–T8)

| Artifact | Introduced by |
|---|---|
| `Animora.Server` project, `ServerDbContext`, migration 101, `ITenantContext` | S01 (project), S02 (context/101/tenant) |
| Tenant repositories/UoW/tenancy guard | S03 |
| Identity tables + token service | S04 (migration = next free server ordinal) |
| Permission policies, entitlement-order middleware | S05 |
| Server command journal table, `ICommandIntake`, settlement store | S06 |
| Server aggregate tables + handlers per context | S07–S10 (each its own next-free migration) |
| `TenantJournal`, position allocator | S11 |
| Batch export endpoint, retention/GC | S12 |
| Server outbox + worker host | S13 |
| `Subscription` aggregate, operator API | S14 |
| `ProvisioningIntent` + intake endpoint | S15 |
| License issuer, device binding, grace | S16 |
| File lifecycle + valet key | S17 |
| Telemetry/pool config | S18 |
| `Animora.AdminWeb` | S19 |
| `ITenantInsightQuery` | S20 |
| `IStateSyncEngine`, `NullSyncEngine` | G01 |
| HTTP adapter | G02 |
| Sync scheduler/status | G03 |
| Conflict policy engine | G04 |
| `HttpCommandTransport` | G05 |
| Online entitlement gate + device lifecycle | G06 |
| CI/RLS/restore gates | G07 |
| Perf proof / budgets | G08 |
| Runbook + packaging | G09 |
