# Phase D01 — Server host scaffold

**Band:** M · **Track:** D · **Depends on:** C11 · **Satisfies:** R-B05
**Runnable after this phase:** desktop offline yes · server required no (the server is new and
independent; the desktop does not yet talk to it)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §4, §7, §8, §9, §11, §12
- `backend-roadmap/PROGRESS.md` (the "Track C complete" marker)
- `STACK.md` §5.1, §5.5, §9, §11 · `ARCHITECTURE.md` §4, §17.3, §17.4, §15
- `A-RULE 016`, `A-RULE 027`, `A-RULE 028`, `A-RULE 032`
- `Directory.Packages.props`, `Directory.Build.props`, `Animora.sln`
- `scripts/sandbox/build.sh`, `scripts/sandbox/test.sh`

## Goal
`Animora.Server` and `Animora.Persistence.SqlServer` exist as real, buildable ASP.NET Core 10
projects with configuration, health endpoints, structured logging, a persisted Data Protection key
ring and first-party OpenAPI — starting successfully with **no** database, no network egress and no
secret in a config file. No business endpoint and no EF model yet.

## Design decisions (pre-made — implement, do not re-derive)
- **Projects:** `src/Animora.Server/` (`Microsoft.NET.Sdk.Web`) and
  `src/Animora.Persistence.SqlServer/` (class library). Both `net10.0`, nullable, added to
  `Animora.sln` in a `server` solution folder. `Animora.Server` references
  `Animora.Application`, `Animora.Domain`, `Animora.Contracts.Wire`, `Animora.Persistence.SqlServer`
  — and **never** `Animora.Client.Services`, `Animora.Persistence.Sqlite`, `Animora.Desktop`
  (asserted by an extended `LayeringRulesTests` fact).
- **Central package pins added to `Directory.Packages.props`** (exact versions, no ranges):
  `Microsoft.EntityFrameworkCore.SqlServer` 10.x (same minor as the existing EF Core 10 pin),
  `Microsoft.AspNetCore.OpenApi`, `Microsoft.Extensions.Diagnostics.HealthChecks`,
  `AspNetCore.HealthChecks.SqlServer`, `Serilog.AspNetCore`, `Serilog.Sinks.File`,
  `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`,
  `Microsoft.AspNetCore.Mvc.Testing` (test project only). **No** Redis, no broker, no cloud SDK
  (T-RULE / CONVENTIONS §1 forbidden list).
- **Options model** (`src/Animora.Server/Configuration/`), each a `sealed class` bound with
  `AddOptions<T>().BindConfiguration(section).ValidateDataAnnotations().ValidateOnStart()` so a
  misconfiguration fails at startup, never at first request:
  | Type | Section | Key members |
  |---|---|---|
  | `ServerOptions` | `Animora:Server` | `InstanceName`, `PublicBaseUrl`, `DataRootPath` |
  | `DatabaseOptions` | `Animora:Database` | `ConnectionStringName`, `CommandTimeoutSeconds` (30), `EnableRetryOnFailure` (true), `MaxRetryCount` (5) |
  | `SecurityOptions` | `Animora:Security` | `DataProtectionApplicationName`, `KeyRingRetentionDays` (180), `RequireHttpsMetadata` (true) |
  | `ObservabilityOptions` | `Animora:Observability` | `LogDirectory`, `RetainedFileCount` (31), `OtlpEndpoint` (**must be loopback or empty** — validated) |
  Secrets (connection string, signing keys) come from environment variables or the OS secret store,
  **never** from `appsettings*.json` — a startup check refuses to run if a connection string appears
  literally in a bound configuration file (`SecretInConfigurationException`).
- **Kestrel / TLS:** HTTPS configured with `SslProtocols.None` (OS negotiation, A-RULE 028) — never
  a hard-coded protocol version. HTTP/1.1 + HTTP/2 enabled; HTTP/3 off (not required, adds surface).
  Request body limit 32 MB (raised only for the file endpoints in D12). Kestrel limits
  (`MaxConcurrentConnections` 512, `KeepAliveTimeout` 2 min) in `ServerOptions`-driven code, not
  magic numbers.
- **Data Protection** persisted to the database via
  `PersistKeysToDbContext<DataProtectionKeyContext>` (a **separate**, tiny `DbContext` in
  `Animora.Persistence.SqlServer/DataProtection/`, so the key ring survives a business-schema
  rebuild) with `SetApplicationName(SecurityOptions.DataProtectionApplicationName)` — a stable name
  is mandatory or every restart invalidates tokens (A-RULE 027). When the key-ring table is
  unreachable the server **refuses to start** (fail closed, CONVENTIONS §7).
  Migration `101_DataProtectionKeys` lives in the DataProtection context's own migrations assembly
  folder.
- **Health endpoints:** `/health/live` (process only, always 200 once started, no DB touch) and
  `/health/ready` (DB connectivity + Data Protection key ring + `DataRootPath` writability).
  Both are anonymous but bound to a configurable path prefix; `/health/ready` returns the
  per-check status without any secret or connection string in the payload.
- **Logging:** Serilog with a rolling file sink under `ObservabilityOptions.LogDirectory`
  (31 files retained), a console sink in Development only, and an **enricher** adding
  `InstanceName` + `RequestId`. `LogRedaction` (B05) is reused via a shared
  `Animora.Application/Diagnostics/LogRedaction.cs` — the same redaction list serves client and
  server. Request logging uses Serilog's `UseSerilogRequestLogging` with a sampling filter that
  skips `/health/*`. **No external sink, ever** (A-RULE 032/016).
- **OpenAPI:** `AddOpenApi("business")` + `MapOpenApi("/openapi/business.json")`, gated to
  Development **and** an explicitly opted-in `Animora:Server:ExposeOpenApi` flag. NSwag is **not**
  referenced in this phase (it arrives in D08 for `/tables/*` only, per STACK.md §5.1).
- **Startup order** (`Program.cs`, documented as a numbered comment block):
  1. bind + validate options → 2. logging → 3. Data Protection → 4. DbContext registration
  (D02 fills the model) → 5. health checks → 6. OpenAPI → 7. middleware pipeline
  (exception handler → HSTS/HTTPS redirect → Serilog request logging → routing → endpoints).
  A `GlobalExceptionMiddleware` maps any unhandled exception to a `WireApiError` (B02) with a
  correlation id and **never** leaks a stack trace outside Development.
- **`scripts/sandbox/serve.sh`** — new script: `dotnet run --project src/Animora.Server` with
  `ASPNETCORE_ENVIRONMENT=Development`, printing `SERVER READY` once `/health/live` answers, and
  exiting non-zero after a 60 s timeout. `scripts/sandbox/build.sh full` must include the new
  projects.
- **Server tests bootstrap:** `tests/Animora.Server.Tests/` gains `ServerFactory`
  (`WebApplicationFactory<Program>` with an in-test configuration that points `DataRootPath` at a
  temp folder) and `SqlServerAvailability` — a helper that probes the configured instance once and
  makes DB-dependent tests **skip with a reason** rather than fail when no SQL Server is present
  (CONVENTIONS §1).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Server/Animora.Server.csproj` | create | web SDK, net10.0, project refs |
| `src/Animora.Server/Program.cs` | create | the 7-step startup, documented |
| `src/Animora.Server/Configuration/*.cs` | create | 4 options classes + validators |
| `src/Animora.Server/Configuration/SecretGuard.cs` | create | refuses literal secrets in config files |
| `src/Animora.Server/Diagnostics/GlobalExceptionMiddleware.cs` | create | exception → `WireApiError` |
| `src/Animora.Server/Diagnostics/ServerLoggingSetup.cs` | create | Serilog + enrichers + redaction |
| `src/Animora.Server/Health/HealthEndpoints.cs` | create | live/ready mapping + checks |
| `src/Animora.Server/appsettings.json`, `appsettings.Development.json` | create | non-secret defaults only |
| `src/Animora.Persistence.SqlServer/Animora.Persistence.SqlServer.csproj` | create | class library + EF SqlServer |
| `src/Animora.Persistence.SqlServer/DataProtection/DataProtectionKeyContext.cs` | create | key-ring context |
| `src/Animora.Persistence.SqlServer/Migrations/DataProtection/*` | create | `101_DataProtectionKeys` |
| `src/Animora.Application/Diagnostics/LogRedaction.cs` | modify | move/share B05's list (no behavior change) |
| `Directory.Packages.props` | modify | pin the 8 new packages |
| `Animora.sln` | modify | add both projects to a `server` folder |
| `scripts/sandbox/serve.sh` | create | run + wait for `SERVER READY` |
| `scripts/sandbox/build.sh` | modify | include the new projects in `full` |
| `tests/Animora.Server.Tests/ServerFactory.cs` | create | `WebApplicationFactory` harness |
| `tests/Animora.Server.Tests/SqlServerAvailability.cs` | create | skip-with-reason probe |
| `tests/Animora.Server.Tests/StartupTests.cs` | create | boots, health, options validation, no secret leak |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | modify | server must not reference client projects |

## Implementation steps
1. Create both projects, add them to the solution, pin the packages centrally.
2. Write the four options classes with data annotations and `ValidateOnStart`; write `SecretGuard`.
3. Write `ServerLoggingSetup` (file sink, enrichers, redaction, `/health/*` filter).
4. Add `DataProtectionKeyContext` + migration `101_DataProtectionKeys`; wire
   `PersistKeysToDbContext` with the stable application name and the fail-closed check.
5. Configure Kestrel (`SslProtocols.None`, limits from options) and the middleware pipeline with
   `GlobalExceptionMiddleware`.
6. Map `/health/live` and `/health/ready` with the three readiness checks.
7. Add gated OpenAPI.
8. Write `serve.sh`; extend `build.sh full`.
9. Add `ServerFactory`, `SqlServerAvailability`, `StartupTests`; extend `LayeringRulesTests`.
10. Verify: `serve.sh` prints `SERVER READY` on a machine with **no SQL Server** (`/health/live` 200,
    `/health/ready` 503 with a clear reason); with SQL Server present, `/health/ready` is 200.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` still = `SMOKE RUN OK`.

## Contracts / schema added
- Projects `Animora.Server`, `Animora.Persistence.SqlServer`.
- `ServerOptions`, `DatabaseOptions`, `SecurityOptions`, `ObservabilityOptions`, `SecretGuard`,
  `GlobalExceptionMiddleware`, `ServerLoggingSetup`, `HealthEndpoints`.
- `DataProtectionKeyContext` + migration `101_DataProtectionKeys`.
- Shared `LogRedaction` in `Animora.Application/Diagnostics/`.
- Scripts: `serve.sh`; `build.sh` extended. Tests: `ServerFactory`, `SqlServerAvailability`,
  `StartupTests`.
- No `Animora.Contracts` change, no `CommandTypes`, no `ErrorCodes`, no DI change in `App.axaml.cs`.

## Rules that apply here
- **A-RULE 028** — TLS is `SslProtocols.None`; no hard-coded protocol version anywhere.
- **A-RULE 027** — the Data Protection key ring is persisted, backed by its own context, with a
  stable application name; the server fails closed without it.
- **A-RULE 016 / 032** — no external sink, no cloud dependency; the OTLP endpoint must be loopback
  or empty (validated).
- **CONVENTIONS §7** — no secret in a config file; `SecretGuard` enforces it at startup.
- **CONVENTIONS §2** — the server references no client project; the layering test proves it.
- **CONVENTIONS §1** — server tests skip with a reason when SQL Server is absent.

## Out of scope
- Any business `DbContext`, entity or migration → D02.
- Identity, authentication, tenancy → D03/D04.
- Any business endpoint or `/tables/*` → D05/D08.
- OpenTelemetry metrics/traces and rate limiting → D13.
- Admin web hosting → E01.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors (both new projects included).
- `scripts/sandbox/serve.sh` prints `SERVER READY` within 60 s on a machine with no SQL Server, and
  `/health/live` returns 200 while `/health/ready` returns 503 naming the failing check.
- `StartupTests` green: missing required option → startup throws with the option name; a literal
  connection string placed in `appsettings.json` → `SecretInConfigurationException`; an OTLP
  endpoint pointing off-box → validation failure; `/health/ready` payload contains no connection
  string or key material; an unhandled exception returns a `WireApiError` with a correlation id and
  no stack trace outside Development.
- `LayeringRulesTests` green: `Animora.Server` references none of
  `Animora.Desktop`/`Animora.Client.Services`/`Animora.Persistence.Sqlite`.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D01 done; advance "Next phase" to `D02`; append a "Repo state" entry listing: the two new
projects + solution folder, the 8 package pins, the four options classes and the fail-fast
validation, `SecretGuard`, the Data Protection key-ring context + migration `101_DataProtectionKeys`
(**D03 depends on it**), the health endpoint contract, `ServerLoggingSetup` + shared `LogRedaction`,
the gated OpenAPI document name `business`, `serve.sh` + the `SERVER READY` marker (**every later
Track D phase uses it**), `ServerFactory`/`SqlServerAvailability` (**the skip-with-reason pattern**),
the extended layering fact, deviations.
