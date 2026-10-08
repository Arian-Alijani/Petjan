# Phase S01 — Server host scaffold

**Band:** M · **Track:** S (server) · **Depends on:** C11 (whole client baseline) · **Next:** S02
**Satisfies:** R-B05, R-B27 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; supersedes the deleted `PHASE-D01` file).

> Baseline verified 2026-10-08: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161, `WireContract.Version` = 1; `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
> No migration in this phase.

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
- `backend-roadmap/PROGRESS.md` — top section only (principles, Current state, Post-C11 drift, Next phase).
- `ARCHITECTURE.md` §4 (server runtime shape, startup order, SecretGuard, TLS) and §17.1 (deployment topology) — only these two `§`.
- `STACK.md` §5.1 + §11 (server package list) — only these two `§`.
- `Directory.Packages.props`, `Animora.sln`, `tests/Animora.Server.Tests/Animora.Server.Tests.csproj` (edited here).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Create the server host `src/Animora.Server` (ASP.NET Core 10 / Kestrel, one process, four route surfaces reserved) with the fixed numbered startup block, validated options, `SecretGuard`, `GlobalExceptionMiddleware` → `WireApiError`, anonymous `/health/live` + `/health/ready`, named rate-limit policies and first-party OpenAPI (Development only). No database, no identity, no business route exists yet — the host boots, answers health and rejects secrets-in-config. It also wires `tests/Animora.Server.Tests` to the new project and extends `LayeringRulesTests` so the client/server isolation rule is enforced from day one.

---

## §Reference pack

```csharp
namespace Animora.Contracts.Wire.Errors;   // (A01)
public sealed record WireApiError(string Code, string? Detail, IReadOnlyList<WireFieldError> Fields, string CorrelationId);
public sealed record WireFieldError(string Field, string Code);
namespace Animora.Contracts.Wire;
public static class WireContract { public const int Version = 1; public const string HeaderName = "X-Animora-Contract-Version"; }
```


```csharp
// Existing test layering helper to EXTEND (tests/Animora.Domain.Tests/LayeringRulesTests.cs, A01) — add facts, never weaken:
private static AssemblyName[] ReferencedAssembliesOf(Assembly a) => a.GetReferencedAssemblies();
private static bool ReferencesNameStartingWith(Assembly a, string prefix) => ReferencedAssembliesOf(a).Any(r => r.Name is not null && r.Name.StartsWith(prefix, StringComparison.Ordinal));
```
Existing pins in `Directory.Packages.props` relevant here: `Microsoft.Extensions.Options 10.0.12`, `Microsoft.Extensions.DependencyInjection 10.0.12`, `Microsoft.Extensions.Logging.Abstractions 10.0.12`, `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2`, `Microsoft.NET.Test.Sdk 17.12.0`. `Directory.Build.props`: net10.0, C# 14, Nullable, ImplicitUsings, `NuGetAuditMode=all`.
`tests/Animora.Server.Tests` today = placeholder csproj (xunit refs only, comment `No subject reference until D01 adds Animora.Server`) + `PlaceholderTests.cs`.

## §Design decisions

- **Project:** `src/Animora.Server/Animora.Server.csproj`, `<Project Sdk="Microsoft.NET.Sdk.Web">`, `net10.0` inherited. `ProjectReference`s: `Animora.Domain`, `Animora.Application`, `Animora.Contracts.Wire` only (persistence arrives in S02). `InternalsVisibleTo Animora.Server.Tests`. `public partial class Program;` at the end of `Program.cs` for `WebApplicationFactory`.
- **New pins (central, exact):** `Microsoft.AspNetCore.OpenApi` **10.0.12**, `Microsoft.AspNetCore.Mvc.Testing` **10.0.12** (tests only). No other package.
- **Surfaces reserved (ARCH §4), only constants now:** `ServerRoutes.Commands="/api/commands"`, `Sync="/sync"`, `Auth="/auth"`, `Operator="/operator"`, `Admin="/admin"`, `HealthLive="/health/live"`, `HealthReady="/health/ready"`. Later phases map endpoints under them.
- **Startup order (numbered comment block in `Program.cs`, ARCH §4 — fixed):** (1) bind+`ValidateOnStart` options; (2) logging; (3) Data Protection — **deferred to S02** (comment placeholder `// 3. DataProtection → S02`); (4) DbContext — **deferred to S02**; (5) health checks; (6) OpenAPI (`AddOpenApi`/`MapOpenApi` only when `Environment.IsDevelopment()` **or** `Server:EnableOpenApi=true`); (7) middleware: `GlobalExceptionMiddleware` → HSTS/HTTPS redirect (non-Development) → request logging (skip `/health/*`) → [tenant/authN/authZ/entitlement slots — comment, added S04/S05] → routing → endpoints.
- **Options (`ServerOptions`, section `Server`):** `DataRootPath` (required, absolute, must exist/writable at readiness), `EnableOpenApi` (bool, default false), `PublicBaseUrl` (optional absolute URI). Validated by `ServerOptionsValidator : IValidateOptions<ServerOptions>` (`ValidateOnStart`). Failure → process exits with a clear message (misconfiguration fails at boot).
- **`SecretGuard`** (static, `Animora.Server.Hosting`): scans the bound `IConfiguration` **provider values coming from JSON files** for any key whose name matches `(?i)(password|pwd|secret|connectionstring|apikey|privatekey)` **and** whose value looks literal (contains `Password=`/`Pwd=`/`Host=` with `;`, or is non-empty for password/secret/apikey/privatekey keys). Throws `SecretInConfigurationException(string keyPath)` (message names the key, never the value). Environment-variable and user-secrets providers are exempt. Called in step (1).
- **`GlobalExceptionMiddleware`:** catches any unhandled exception → HTTP 500 `WireApiError(Code="UNEXPECTED", Detail = Development ? ex.Message : null, Fields=[], CorrelationId)`; never leaks a stack outside Development; `OperationCanceledException` with `RequestAborted` set → no body (client went away). Correlation id: reuse inbound `X-Correlation-Id` (≤64 chars, `[A-Za-z0-9-_]`) else `IdGenerator.NewId()`; echo it as response header `X-Correlation-Id`.
- **Health (anonymous, no secrets):** `/health/live` — process only, never touches a dependency, 200 `{"status":"live"}`. `/health/ready` — runs `IReadinessProbe` list (interface in `Animora.Server.Hosting`: `string Name {get;} Task<ReadinessResult> CheckAsync(CancellationToken ct)`); S01 ships `DataRootWritableProbe`; S02 adds `DatabaseProbe` + `KeyRingProbe`. Any `Unhealthy` ⇒ 503 `{"status":"not-ready","failed":["<probe name>"]}` (names only, never exception text).
- **Rate limiting:** `AddRateLimiter` (built-in `Microsoft.AspNetCore.RateLimiting`) with **five named fixed-window policies** `auth`(10/min per IP), `commands`(120/min per tenant-or-IP), `sync`(60/min), `files`(60/min), `operator`(30/min); rejection → 429 `WireApiError(Code="RATE_LIMITED")` + `Retry-After`. Policy-name constants in `RateLimitPolicies`. Windows are options-driven (`Server:RateLimits:<policy>:PermitLimit/WindowSeconds`), validated > 0. Health endpoints are not limited.
- **TLS:** Kestrel certificate comes from configuration path/env only; **no `SslProtocols` assignment anywhere** (OS-negotiated = default). Add a source-scan test asserting the literal `SslProtocols.Tls` does not occur under `src/Animora.Server`.
- **Offline/client isolation:** `LayeringRulesTests` gains `Server_DoesNotReferenceClientProjects` (asserts `Animora.Server` references none of `Animora.Client.Services|Animora.Persistence.Sqlite|Animora.Infrastructure.Local|Animora.Desktop|Animora.Sync.Custom`) and `Client_DoesNotReferenceServerProjects` (Desktop/Client.Services/Infrastructure.Local/Persistence.Sqlite reference no `Animora.Server*`, `Animora.Persistence.PostgreSql`, `Animora.Web.Admin`). Domain.Tests gets a `ProjectReference` to `Animora.Server` **only if** needed to load the assembly — prefer loading by `Assembly.Load("Animora.Server")` after `Animora.Server.Tests` is built; simplest: put the server-side fact in `tests/Animora.Server.Tests/ServerLayeringTests.cs` and the client-side fact stays in Domain.Tests via `AssemblyName` string checks (no project ref).
- **No new ErrorCodes in Domain**; the two wire-only codes `UNEXPECTED` (already `ErrorCodes.Unexpected`) and `RATE_LIMITED` are string constants in `Animora.Server.Hosting.ServerErrorCodes` (server-transport codes are not domain codes).

## §Implementation plan

1. `scripts/sandbox/install-prereqs.sh` (once). Confirm `ls src/Animora.Server` is empty/absent.
2. Add the two pins to `Directory.Packages.props` (`Microsoft.AspNetCore.OpenApi` 10.0.12, `Microsoft.AspNetCore.Mvc.Testing` 10.0.12).
3. Create `src/Animora.Server/Animora.Server.csproj` (Sdk.Web, refs above, `PackageReference Microsoft.AspNetCore.OpenApi`, `InternalsVisibleTo`). Add to `Animora.sln` with `dotnet sln Animora.sln add src/Animora.Server/Animora.Server.csproj` (use `scripts/sandbox` env: `source scripts/sandbox/_env.sh` first).
4. Write `Hosting/ServerOptions.cs`, `ServerOptionsValidator.cs`, `SecretGuard.cs` (+ `SecretInConfigurationException`), `GlobalExceptionMiddleware.cs`, `CorrelationHeader.cs`, `IReadinessProbe.cs`, `DataRootWritableProbe.cs`, `RateLimitPolicies.cs`, `RateLimitOptions.cs`, `ServerRoutes.cs`, `ServerErrorCodes.cs`.
5. Write `Program.cs` with the numbered block (steps 1–7 above) and `public partial class Program;`. Add `appsettings.json` (only non-secret defaults: `Server:DataRootPath` omitted → must come from env `Server__DataRootPath`; `Logging`, `Server:RateLimits`) and `appsettings.Development.json` (`EnableOpenApi=true`, `DataRootPath="./.data"`).
6. Edit `tests/Animora.Server.Tests/Animora.Server.Tests.csproj`: add `ProjectReference` to `Animora.Server`, `PackageReference Microsoft.AspNetCore.Mvc.Testing`, remove the "No subject reference" comment; delete `PlaceholderTests.cs`. Add tests (below). Edit `tests/Animora.Domain.Tests/LayeringRulesTests.cs` (client-side fact).
7. `scripts/sandbox/build.sh full` → **0 warnings / 0 errors**. `scripts/sandbox/test.sh Server` → green. `scripts/sandbox/run.sh` → `SMOKE RUN OK` (desktop unaffected).
8. Smoke the host once: `ASPNETCORE_URLS=http://127.0.0.1:5188 Server__DataRootPath=$PWD/.sandbox/server-data dotnet run --project src/Animora.Server --no-build` in the background, `curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5188/health/live` → `200`, `/health/ready` → `200`, then stop it. (Skip if memory-capped; the `WebApplicationFactory` tests already prove it.)
9. Update `PROGRESS.md` per the recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `Directory.Packages.props` (edit) | +2 pins |
| `Animora.sln` (edit) | adds `Animora.Server` |
| `src/Animora.Server/Animora.Server.csproj` | Sdk.Web host project |
| `src/Animora.Server/Program.cs` | numbered startup block; `public partial class Program;` |
| `src/Animora.Server/Hosting/*.cs` | options, validator, `SecretGuard`, exception middleware, readiness probes, rate-limit policies, routes, transport error codes |
| `src/Animora.Server/appsettings.json`, `appsettings.Development.json` | non-secret defaults |
| `tests/Animora.Server.Tests/Animora.Server.Tests.csproj` (edit) | subject reference + `Mvc.Testing`; placeholder removed |
| `tests/Animora.Server.Tests/HostSmokeTests.cs`, `SecretGuardTests.cs`, `GlobalExceptionMiddlewareTests.cs`, `RateLimitTests.cs`, `ServerLayeringTests.cs`, `SourceScanTests.cs` | tests |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` (edit) | client-must-not-reference-server fact |

## Contracts / schema added

No wire DTO, no ErrorCodes entry, no migration, no table. New public server types are internal to `Animora.Server` except `Program`. `ServerRoutes`/`RateLimitPolicies`/`ServerErrorCodes` string constants become the single source other server phases reference.

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

- Database, `DbContext`, Data Protection key ring, readiness DB/key probes → **S02**.
- Identity/auth endpoints, tenant context, authorization, entitlement slot → **S04/S05** (S16 fills entitlement).
- `/api/commands` intake → **S06**; `/sync/*` → **S11/S12**; `/operator/*` → **S14**; `/admin` → **S19**; files → **S17**.
- OpenTelemetry/metrics/pool profile → **S18**. Deployment packaging/runbook → **G09**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` exits 0 with `0 Warning(s)`/`0 Error(s)`; `scripts/sandbox/run.sh` prints `SMOKE RUN OK`.
2. `scripts/sandbox/test.sh Server` passes these named tests: `HostSmokeTests.Live_Returns200_WithoutDependencies`, `HostSmokeTests.Ready_Returns200_WhenDataRootWritable`, `HostSmokeTests.Ready_Returns503_NamesFailedProbeOnly_WhenDataRootMissing`, `SecretGuardTests.Throws_ForLiteralConnectionStringInJson`, `SecretGuardTests.Allows_EnvironmentProvider`, `GlobalExceptionMiddlewareTests.UnhandledException_Returns500WireApiError_NoStack_InProduction`, `GlobalExceptionMiddlewareTests.Echoes_CorrelationId`, `RateLimitTests.Auth_Policy_Returns429_WithRetryAfter_AfterLimit`, `ServerLayeringTests.Server_DoesNotReferenceClientProjects`, `SourceScanTests.No_HardCoded_SslProtocols`.
3. `grep -rn "SslProtocols\." src/Animora.Server` returns nothing; `grep -rn "Npgsql" src/Animora.Server` returns nothing (S01 has no DB).
4. `git diff --stat -- src/Animora.Domain src/Animora.Application src/Animora.Persistence.Sqlite src/Animora.Desktop` is empty (client untouched).
5. `grep -c "AddRateLimiter" src/Animora.Server/Program.cs` ≥ 1 and the five policy names `auth|commands|sync|files|operator` all appear in `RateLimitPolicies.cs`.

## PROGRESS.md update

At the end of this phase: set the `S01` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S02`**,
append one `### S01` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S01 server host scaffold` and sync per the repo git workflow.
