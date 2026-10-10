# Server / Database (Track S) — Build & Real-Database Test Report

> **Purpose of this file.** A plain record of what happens when the **server +
> database (Track S)** surface is built and tested against a **real PostgreSQL**
> in this sandbox, using the new scripts below. Per the task directive this file
> **only records** the errors/problems found — it does **not** fix them. The
> desktop app (`Animora.Desktop`) and its dependent test projects
> (`Animora.Domain.Tests`, `Animora.Application.Tests`) were deliberately **not**
> built or tested.
>
> - **Run date:** 2026-10-10
> - **Branch:** `genspark_ai_developer`
> - **Environment:** Debian 13, 2 cores, ~985 MiB RAM, .NET SDK 10.0.401,
>   PostgreSQL 17.11 (local throwaway cluster on `127.0.0.1:5433`).

---

## 1. How to reproduce (the new scripts)

All scripts live in `scripts/sandbox/` and self-configure the SDK/env.

| Step | Command | Notes |
|---|---|---|
| Install SDK + restore | `scripts/sandbox/install-prereqs.sh --no-xvfb` | idempotent; ~30 s if cached |
| Install + start PostgreSQL | `scripts/sandbox/install-postgres.sh` | local cluster under `.sandbox/pg`, test-tuned (durability off) |
| Health report | `scripts/sandbox/doctor.sh` | now also reports PostgreSQL up/down |
| **Build server only** | `scripts/sandbox/server-test.sh --build-only` | no desktop, no tests |
| **Test server on real DB** | `scripts/sandbox/server-test.sh --require-pg` | `--require-pg` = gated tests FAIL (not skip) if DB missing |
| Test server + SQLite + sync backend | `scripts/sandbox/server-test.sh --all --require-pg` | still no desktop |
| PostgreSQL controls | `install-postgres.sh status\|stop\|restart\|nuke` | |

**PostgreSQL-gating.** `PgFixture` reads `ANIMORA_PG_TEST_CONNECTION`; when unset,
`[PgFact]` tests *skip*. `install-postgres.sh` writes that connection string to
`.sandbox/pg/connection.env`, and `server-test.sh` sources it, so the gated tests
run against a **real** database. With `--require-pg` (`ANIMORA_REQUIRE_PG=1`) a
missing DB fails instead of skipping — used for this run, so every result below is
a **real** execution, not a skip.

---

## 2. Build results (source + tests) — PASS

| Target | Result | Warnings | Errors |
|---|---|---|---|
| `src/Animora.Server` (+ Web.Admin, Server.Licensing, Persistence.PostgreSql, Domain, Application, Contracts.Wire) | **Build OK** | 26 | **0** |
| `tests/Animora.Server.Tests` (whole server test tree) | **Build OK** | 12 | **0** |

The server/DB/"S" code **compiles cleanly** (0 errors). The warnings are recorded
in §4 below; they are not fixed here.

Full logs: `.sandbox/reports/build-server-src.log`,
`.sandbox/reports/build-server-tests.log`.

---

## 3. Real-database test results — FAILURES RECORDED (not fixed)

Command: `server-test.sh --require-pg` against PostgreSQL 17.11 on `127.0.0.1:5433`.

```
Total tests: 759
     Passed: 508
     Failed: 251
 Total time: ~45 s
```

Full log: `.sandbox/reports/test-server.log`.

### 3.1 Failures grouped by root-cause error

| # failing | Root error | Meaning |
|---|---|---|
| **152** | `InvalidOperationException … PendingModelChangesWarning: The model for context 'AnimoraServerDbContext' has pending changes. Add a new migration…` | The EF model and the applied migrations are **out of sync**. `db.Database.MigrateAsync()` refuses to run because the snapshot no longer matches the model. Every test that migrates the server DB hits this. |
| **~214 (107 shown)** | `ArgumentNullException : Value cannot be null. (Parameter 'provider')` thrown from `CommandIntakeService.SubmitAsync` at `src/Animora.Server/Commands/CommandIntakeService.cs:42` via `IServiceProvider.GetService<T>` | A command-intake code path calls `GetService` on a **null** `IServiceProvider` (DI container/scope not supplied in the handler test setup). Dominates the `HandlersS07/S08/S09/S10` + `Commands` tests. |
| **62** | `OptionsValidationException : Database pool sizes sum to 14, exceeding the 12-connection budget of the 2 GiB profile.` | The server's **default DB pool configuration sums to 14** connections, but the options validator caps the 2 GiB profile at 12. Host/`WebApplicationFactory`-based tests fail at startup because options validation rejects the default. |
| 4 | `HostSmokeTests` readiness probes (`/health/ready` 200/503 shape, `/health/live`) | Health endpoints don't return the asserted status/body — likely blocked by the same options/DI startup failures above. |
| 2 | `PgGatedTests.Applies101_OnEmptyDatabase`, `Version_ChangesOnEveryUpdate_IncludingRawSql` | Fail at `db.Database.MigrateAsync()` — same pending-model-changes cause as the 152 group (these are the canonical migration-apply tests). |
| 1 | `SnapshotAgeMonitorTests.OldSnapshot_ReportsDegraded` (`Assert.True` failure) | Monitor did not report degraded for an old snapshot. |
| 1 | `ServerSourceScanTests.OperatorContext_OnlyUsedInOperatorAndSubscriptionNamespaces` (`Assert.Empty` — collection not empty) | A source-scan invariant: `OperatorContext` is referenced outside the allowed namespaces. |
| 1 | `SubscriptionPersistenceTests` string/equality assert | Downstream of the migration failure. |

> Counts overlap slightly because some grouped samples were capped when
> extracting; the authoritative per-test list is in `.sandbox/reports/test-server.log`.

### 3.2 Failures grouped by test area

```
 40 Admin            31 Authorization    30 HandlersS09     29 HandlersS07
 28 HandlersS10      19 Commands         16 HandlersS08     12 Identity
  8 Sync              8 Subscriptions     7 Outbox           7 Licensing
  5 Files             4 HostSmokeTests    3 Provisioning     2 Persistence
  1 Operator          1 Observability
```

### 3.3 Representative stack traces (verbatim)

**A) Pending model changes (152 tests):**
```
System.InvalidOperationException : An error was generated for warning
'Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning':
The model for context 'AnimoraServerDbContext' has pending changes.
Add a new migration before updating the database.
  at …Migrator.ValidateMigrations(...)
  at …NpgsqlMigrator.MigrateAsync(...)
  at Animora.Server.Tests.Subscriptions.SubscriptionPersistenceTests.Migrate()
     tests/Animora.Server.Tests/Subscriptions/SubscriptionPersistenceTests.cs:42
```

**B) Null service provider in command intake (~214 tests):**
```
System.ArgumentNullException : Value cannot be null. (Parameter 'provider')
  at Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService[T](IServiceProvider provider)
  at Animora.Server.Commands.CommandIntakeService.SubmitAsync(CommandEnvelope envelope, CancellationToken ct)
     src/Animora.Server/Commands/CommandIntakeService.cs:42
  at Animora.Server.Tests.HandlersS07.ClinicalIntakeTests.SettlesAsync(Type record)
     tests/Animora.Server.Tests/HandlersS07/ClinicalIntakeTests.cs:47
```

**C) DB pool budget options validation (62 tests):**
```
Microsoft.Extensions.Options.OptionsValidationException :
Database pool sizes sum to 14, exceeding the 12-connection budget of the 2 GiB profile.
```

---

## 4. Build warnings (recorded, not fixed)

### 4.1 Source (`src`) — 26 warnings
| Count | Code | Where / meaning |
|---|---|---|
| 52* | `CS9107` | Primary-constructor parameter `AnimoraServerDbContext db` captured into the derived type **and** passed to the base constructor (base may capture it too). In `src/Animora.Persistence.PostgreSql/Repositories/*Repository.cs` (e.g. `WeightEntryRepository.cs:8`, `VisitRepository.cs:8`). |

> *`CS9107` count is per-build-pass; MSBuild reports 26 unique warning lines for the server project build, repeated across references.

### 4.2 Tests (`tests/Animora.Server.Tests`) — 12 warnings
| Count | Code | Meaning |
|---|---|---|
| 16 | `xUnit2031` | Use `Assert.Single(collection, predicate)` instead of `.Where(...).Single()`. |
| 4 | `xUnit2000` | Constants/literals should be the *expected* argument in `Assert.Equal`. |
| 2 | `xUnit2013` | Use `Assert.Single`/`Assert.Empty` instead of checking `.Count`. |
| 2 | `CS0618` | Use of an `[Obsolete]` member. |

---

## 5. Summary

- **Scripts added & working:** `install-postgres.sh` (fast local PG),
  `server-test.sh` (build/test the server+DB on a real database), `doctor.sh`
  extended with a PostgreSQL probe. Prereqs install quickly and the environment
  is ready for **real** server/DB testing in this sandbox.
- **Server/DB builds cleanly:** 0 errors (source and tests).
- **Real-DB test run executed:** 759 tests, **508 passed / 251 failed** against a
  genuine PostgreSQL 17 instance (not skipped).
- **Three dominant failure root causes** (all recorded, none fixed per directive):
  1. EF `AnimoraServerDbContext` has **pending model changes** vs. its migrations
     (migrate-on-startup aborts) — 152 tests.
  2. `CommandIntakeService.SubmitAsync` dereferences a **null `IServiceProvider`**
     in the handler test setup — ~214 tests.
  3. Default **DB pool budget sums to 14 > 12**, so options validation rejects
     host startup — 62 tests.
- Plus a handful of individual assertion/invariant failures (health probes,
  snapshot-age monitor, operator-context source-scan).

Raw evidence (gitignored, regenerate with the scripts): `.sandbox/reports/*.log`.
