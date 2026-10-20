# Animora — Full-System Audit Index

> Navigation + ground truth for the code-level audit. This directory holds **problem reports**
> (findings), not fixes. Each report file audits one layer/subsystem, hunts the 13 defect classes,
> and records findings in the standard format below. A fresh reader can open any report standalone.

## Product one-liner

Animora = commercial **vendor-hosted (SaaS) multi-tenant** veterinary clinic management + accounting.
The vendor runs ONE central ASP.NET Core server; customers register + buy a subscription on the public
site, download the **offline-first Avalonia/.NET 10 desktop app** (Persian/RTL, Jalali), log in, and
sync to the central server. "Self-hosted" in specs means the vendor self-operates its server — never
per-clinic hosting.

The **.NET solution is the only code under audit** (`Animora.sln`, 14 `src/` projects + `tests/`).
`Animora-UI/` (throwaway HTML design reference) is **never opened**. `Database-Sampel/`/`db-sample/`
(binary dump) is skipped.

## Layer map

| Project | Concern |
|---|---|
| `src/Animora.Domain/` | entities/aggregates/VOs per bounded context; ZERO project refs by design |
| `src/Animora.Application/` | command/query dispatch pipeline, behaviors, outbox, ports, security plane |
| `src/Animora.Contracts/` | DTO + query/service interfaces (Desktop↔services seam), 24 interfaces |
| `src/Animora.Contracts.Wire/` | server↔client transport DTOs (CommandEnvelope, SettlementResult, errors) |
| `src/Animora.Persistence.Sqlite/` | LOCAL encrypted EF store: DbContext, Migrations 001–033, repos, queries, seed, maintenance/backup/quarantine |
| `src/Animora.Infrastructure.Local/` | OS keystore/encryption, native SQLite assertion, clocks, local file store |
| `src/Animora.Client.Services/` | real impls of Contracts over Application+Persistence; + Sync/ orchestrator |
| `src/Animora.Desktop/` | Avalonia UI (17 screens + Statistics), DI root App.axaml.cs, Shell, Pages, Controls, Localization |
| `src/Animora.Server/` | ASP.NET Core 10 central host: Sync, Commands, Workers, Auth, Authorization, Subscriptions, Provisioning, Licensing, Admin, Operator, Files, Insight, Observability, Resilience, Signing, Hints, Handlers, Hosting |
| `src/Animora.Persistence.PostgreSql/` | server EF (Npgsql): Tenancy, Replication, Outbox, Identity, Commands, Subscriptions, Licensing, Operator, Files, migrations 101–111 + KeyRing 101 |
| `src/Animora.Server.Licensing/` | license issuance (ECDSA-P256 signer) + verification (envelope/payload/policy/capability gate) |
| `src/Animora.Sync.Custom/` | client-side sync engine: HttpStateSyncEngine, HttpCommandTransport, SqliteSyncApplyStore, ReplicaMaterializer, SettlementReconciler, ShadowResyncHandler, CommandIdRemapService, SyncFeedClient |
| `src/Animora.Sync.Abstractions/` | empty scaffold (ISyncEngine placeholder) — note if still empty |
| `src/Animora.Web.Admin/` | Domain-only admin RCL (SSR operator pages) |

## Known baseline facts

- Client SQLite migrations `001`–`033` (physical head `033_OnlineEntitlement`); `020`,`022`,`024`–`033`
  use a double underscore before the ordinal. Server PG head `111_ServerOutbox` (101–111), KeyRing `101`.
  Several high-number server/PG migrations authored but unapplied/unexecuted.
- `CommandTypes` = **94** types (39 non-financial + 47 money/stock = 86, plus the +8 new-family types:
  `vaccination.treatment.*` ×4 + `finance.recurring.*` ×4); `ErrorCodes` = **161** codes.
  Live `CommandTypes.cs` / `ErrorCodes.cs` are the single source of truth.
- New aggregates beyond the original replicated set: `Clinical.TreatmentPlan` (+occurrences, vaccine
  catalog), `Finance.RecurringExpense` (+payment). New command families `vaccination.treatment.*` (4),
  `finance.recurring.*` (4). New desktop module **Statistics** has no server/replication surface yet.
- Local store: append-only audit sink, buffered/redacted rolling file logging, PII-free diagnostics
  bundle export, maintenance/backup/quarantine (`RebuildStockSnapshots`, `RebuildFinanceProjections`,
  `RebuildActivityFeed`), `FinanceProjectionWorker` draining the B02 outbox off the OLTP path.
- Server implementation status mixed: S01–S06 done; S07–S13 + licensing/admin S14–S20 "authored" but
  build/test/smoke UNEXECUTED. Treat "authored but unverified" code as a first-class risk source.

## The 13 audit targets (defect classes)

1. Performance problems + optimization opportunities (HIGH PRIORITY).
2. Bugs (functional defects in current code).
3. Latent bugs — correct-now, breaks-over-time (unbounded growth, time/clock, culture, overflow, ordering, retries).
4. Performance-improvement opportunities (indexes, allocations, N+1, async/sync, caching, batching).
5. Cross-layer inconsistencies (Domain↔App↔Persistence↔Contracts↔UI drift).
6. Server↔client inconsistencies (wire DTOs, command/error codes, versioning, time, pagination).
7. Sync system — exhaustive push/pull failure scenarios.
8. Any defect of any kind.
9. Perf is emphasized — weight it heavily.
10. Multi-tenant clinic/account provisioning.
11. The whole sales + accounting system.
12. Correct, complete, standards-compliant data isolation between tenants.
13. The backup system (local + server), in depth.

## Finding format (used in every report)

```
### <AREA>-<NN> <short title>
- Severity: Critical | High | Medium | Low
- Class: perf | bug | latent | inconsistency | sync | isolation | provisioning | sales-accounting | backup
- Location: <path>:<line-or-symbol>
- Evidence: <exact code fact — quote the decisive line(s)>
- Why it is a problem: <mechanism; for latent, the trigger/time horizon>
- Scenario / impact: <what breaks, for whom, when>
- Confidence: High | Medium (needs runtime check)
```

## Report status

| Report | Scope | Status |
|---|---|---|
| `01-domain.md` | Domain layer (T2) | done |
| `02-application.md` | Application layer (T3) | done |
| `03-contracts-wire.md` | Contracts + Wire (T4) | done |
| `04-persistence-sqlite.md` | Local SQLite persistence (T5) | done |
| `05-infra-clientservices.md` | Infrastructure.Local + Client.Services (T6) | done |
| `06-sync-system.md` | Sync system deep audit (T7) | done |
| `07-server-auth.md` | Server host, auth & authorization (T8) | done |
| `08-tenancy-isolation.md` | Multi-tenant provisioning + isolation (T9) | done |
| `09-sales-accounting.md` | Sales + accounting system (T10) | done |
| `10-backup.md` | Backup system (T11) | done |
| `11-desktop-crosslayer.md` | Desktop UI/DI + cross-layer (T12) | done |
| `12-synthesis.md` | Cross-cutting synthesis + perf roll-up (T13) | done |

## Prior-art docs to mine (cite + extend, don't re-run)

- `docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md` (SYNC-C/H/M, SRV-M, PH, RM-H, CODE, OPT, IMP)
- `docs/CLIENT_SERVER_CLINIC_SYNC_DIAGNOSTIC_2026-10-14.md`
- `docs/SERVER_REVIEW_REPORT.md`, `backend-roadmap/PROGRESS.md` (Post-C11 drift, Maintenance drift ledger)
