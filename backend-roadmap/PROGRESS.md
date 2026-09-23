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

**Next phase: `A02`** — `backend-roadmap/phases/PHASE-A02-*.md`

## Phase status

| ID | Status | Date | Notes |
|---|---|---|---|
| A01 | ✅ Done | 2026-09-23 | Backend track scaffold: 4 src + 5 test projects, 10 pins, WireContract v1, test.sh, LayeringRulesTests. Build 0/0, TESTS OK, SMOKE RUN OK. |
| A02 | ⬜ Not started | | |
| A03 | ⬜ Not started | | |
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
