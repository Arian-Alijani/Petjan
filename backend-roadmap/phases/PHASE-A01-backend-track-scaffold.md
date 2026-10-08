# Phase A01 — Backend track scaffold

**Band:** S · **Track:** A · **Depends on:** — · **Satisfies:** R-B10, R-B12, R-B31
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §9, §11, §12, §13
- `backend-roadmap/PROGRESS.md` (state + next-phase pointer)
- `STACK.md` §11 (canonical package list), §15 (T-RULE 003, T-RULE 010)
- `ARCHITECTURE.md` §5 (layering), §8.6 (contract versioning)
- `Directory.Packages.props`, `Directory.Build.props`, `Animora.sln`
- `scripts/sandbox/build.sh`, `scripts/sandbox/_env.sh`
- `src/Animora.Domain/Animora.Domain.csproj`, `src/Animora.Application/Animora.Application.csproj`

## Goal
Create every project the backend track needs, wired into `Animora.sln` with centrally pinned packages,
plus the five test projects and a `scripts/sandbox/test.sh` runner. Establish the layering-enforcement
test and the wire contract version constant. **Zero behavior change** — the desktop still runs entirely
on mocks after this phase.

## Design decisions (pre-made — implement, do not re-derive)
- **New projects** (all `net10.0`, nullable enabled via `Directory.Build.props`, no `TargetFramework`
  in the csproj):
  `Animora.Contracts.Wire`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`,
  `Animora.Client.Services`. `Animora.Domain`, `Animora.Application`, `Animora.Sync.Abstractions`
  already exist as placeholders — keep them, do not recreate.
- **Project references** (exactly these, nothing more):
  | Project | References |
  |---|---|
  | `Animora.Domain` | *(none)* |
  | `Animora.Contracts.Wire` | *(none)* |
  | `Animora.Application` | `Animora.Domain`, `Animora.Contracts`, `Animora.Contracts.Wire` |
  | `Animora.Persistence.Sqlite` | `Animora.Domain`, `Animora.Application` |
  | `Animora.Infrastructure.Local` | `Animora.Domain`, `Animora.Application` |
  | `Animora.Client.Services` | `Animora.Application`, `Animora.Contracts`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local` |
  | `Animora.Sync.Abstractions` | `Animora.Domain`, `Animora.Contracts.Wire` |
- **`Animora.Desktop` does NOT gain a reference to `Animora.Client.Services` in this phase** — that
  reference is added by C01, the first phase that actually swaps a service. A01 only creates projects.
- **Package pins added to `Directory.Packages.props`** (exact versions, no ranges):
  ```
  Microsoft.EntityFrameworkCore               10.0.12
  Microsoft.EntityFrameworkCore.Sqlite        10.0.12
  Microsoft.EntityFrameworkCore.Design        10.0.12
  Microsoft.Extensions.Logging.Abstractions   10.0.12
  Microsoft.Extensions.Options                10.0.12
  SQLitePCLRaw.bundle_e_sqlite3               3.0.3
  SourceGear.sqlite3                          3.50.4.5
  xunit                                       2.9.2
  xunit.runner.visualstudio                   2.8.2
  Microsoft.NET.Test.Sdk                      17.12.0
  ```
  `SQLitePCLRaw.bundle_e_sqlite3` + `SourceGear.sqlite3` are pinned here (STACK.md §3.3) so the native
  SQLite ≥ 3.50.2 requirement is satisfiable; only `Animora.Persistence.Sqlite` consumes them.
  **Do not** add Datasync, Npgsql/PostgreSQL, ASP.NET Core or encryption-bundle pins — later phases own those.
- **Package consumption in this phase:** `Animora.Persistence.Sqlite` references
  `Microsoft.EntityFrameworkCore.Sqlite` + `Microsoft.EntityFrameworkCore.Design` +
  `SQLitePCLRaw.bundle_e_sqlite3` + `SourceGear.sqlite3`. `Animora.Application` and
  `Animora.Infrastructure.Local` reference `Microsoft.Extensions.Logging.Abstractions` and
  `Microsoft.Extensions.Options`. Nothing else.
- **Wire contract version:** `Animora.Contracts.Wire/WireContract.cs` —
  `public static class WireContract { public const int Version = 1; public const string HeaderName = "X-Animora-Contract-Version"; }`.
  Bumped only by a phase that changes a wire shape (ROADMAP §3.8).
- **Test projects** under `tests/`: `Animora.Domain.Tests`, `Animora.Application.Tests`,
  `Animora.Persistence.Tests`, `Animora.Sync.Tests`, `Animora.Server.Tests`. Each references
  `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` and its subject project
  (`Animora.Server.Tests` references nothing yet — it gets `Animora.Server` in D01; leave it with a
  single placeholder test so the runner is green).
- **`Directory.Build.props` addition:** `<NuGetAuditMode>all</NuGetAuditMode>` and
  `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` (leave the existing warning discipline as-is;
  build must stay 0 warnings). Add `<EnableNETAnalyzers>true</EnableNETAnalyzers>`.
- **Layering test:** `tests/Animora.Domain.Tests/LayeringRulesTests.cs` with one `[Fact]` per rule,
  asserting on `Assembly.GetReferencedAssemblies()` names:
  - `Animora.Domain` references no assembly whose name starts with `Animora.` other than itself, and
    none starting with `Microsoft.EntityFrameworkCore` or `CommunityToolkit.Datasync`.
  - `Animora.Application` references neither `Microsoft.EntityFrameworkCore*` nor
    `CommunityToolkit.Datasync*`.
  - `Animora.Contracts` references no `Animora.*` assembly.
  Later phases extend this class; they never weaken an existing fact.
- **`scripts/sandbox/test.sh`:** sources `scripts/sandbox/_env.sh` exactly as `build.sh` does, then runs
  `dotnet test Animora.sln --nologo -v minimal` (no `--no-build`; restore/build first via the same
  configuration `build.sh` uses). Accepts one optional arg `<project-name-filter>` mapped to
  `--filter FullyQualifiedName~<arg>`. Exits non-zero on any failing test. Prints `TESTS OK` on success.
- **Solution folders in `Animora.sln`:** existing `src` folder for the new src projects; a new `tests`
  solution folder for the five test projects.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Contracts.Wire/Animora.Contracts.Wire.csproj` | create | empty class library, no references |
| `src/Animora.Contracts.Wire/WireContract.cs` | create | `WireContract.Version = 1` + header name constant |
| `src/Animora.Persistence.Sqlite/Animora.Persistence.Sqlite.csproj` | create | refs Domain+Application, EF Core Sqlite/Design, SQLitePCLRaw bundle, SourceGear.sqlite3 |
| `src/Animora.Persistence.Sqlite/README.md` | create | one paragraph: purpose + what phase fills it (A09) |
| `src/Animora.Infrastructure.Local/Animora.Infrastructure.Local.csproj` | create | refs Domain+Application, Logging.Abstractions, Options |
| `src/Animora.Infrastructure.Local/README.md` | create | purpose + owning phases (A02 clocks, A11 keystore/encryption, B05 logging) |
| `src/Animora.Client.Services/Animora.Client.Services.csproj` | create | refs Application, Contracts, Persistence.Sqlite, Infrastructure.Local |
| `src/Animora.Client.Services/README.md` | create | purpose: the DI swap target for mocks (Track C) |
| `src/Animora.Application/Animora.Application.csproj` | modify | add refs Domain, Contracts, Contracts.Wire + Logging.Abstractions/Options packages |
| `src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj` | modify | add refs Domain, Contracts.Wire |
| `Directory.Packages.props` | modify | add the 10 pins above |
| `Directory.Build.props` | modify | add `NuGetAuditMode=all`, `EnableNETAnalyzers=true` |
| `Animora.sln` | modify | add 4 src projects + 5 test projects + a `tests` solution folder |
| `tests/Animora.Domain.Tests/Animora.Domain.Tests.csproj` | create | xunit + ref Animora.Domain |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | create | the 3 layering facts above |
| `tests/Animora.Application.Tests/Animora.Application.Tests.csproj` | create | xunit + ref Animora.Application |
| `tests/Animora.Application.Tests/PlaceholderTests.cs` | create | one trivially-true fact so the project is green |
| `tests/Animora.Persistence.Tests/Animora.Persistence.Tests.csproj` | create | xunit + ref Animora.Persistence.Sqlite |
| `tests/Animora.Persistence.Tests/PlaceholderTests.cs` | create | one trivially-true fact |
| `tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj` | create | xunit + ref Animora.Sync.Abstractions |
| `tests/Animora.Sync.Tests/PlaceholderTests.cs` | create | one trivially-true fact |
| `tests/Animora.Server.Tests/Animora.Server.Tests.csproj` | create | xunit only (no subject ref until D01) |
| `tests/Animora.Server.Tests/PlaceholderTests.cs` | create | one trivially-true fact |
| `scripts/sandbox/test.sh` | create | env + `dotnet test Animora.sln`, prints `TESTS OK` |
| `.gitignore` | modify | add `**/TestResults/` if absent |

## Implementation steps
1. Add the 10 package pins to `Directory.Packages.props`, preserving the existing comment style and
   grouping (add a `<!-- Backend track (STACK.md §11) -->` ItemGroup).
2. Add `NuGetAuditMode=all` and `EnableNETAnalyzers=true` to `Directory.Build.props`.
3. Create the four new src projects and their `README.md` stubs. Csproj files contain only
   `<Project Sdk="Microsoft.NET.Sdk">` + `ItemGroup`s for references — no TFM, no nullable (inherited).
4. Add project references to `Animora.Application` and `Animora.Sync.Abstractions` per the table.
5. Create `Animora.Contracts.Wire/WireContract.cs`.
6. Create the five test projects with their csproj references and placeholder tests.
7. Write `LayeringRulesTests.cs`. Force assembly loading with a `typeof(...)` touch of one type per
   asserted assembly so `GetReferencedAssemblies` is populated deterministically.
8. Add every new project to `Animora.sln` (`dotnet sln add`), then create the `tests` solution folder
   and nest the five test projects in it.
9. Write `scripts/sandbox/test.sh`, `chmod +x` it, and mirror `build.sh`'s env sourcing and arg style.
10. Run `scripts/sandbox/build.sh full` — must be 0 warnings / 0 errors. Fix any analyzer warning the
    new projects introduce (do not suppress).
11. Run `scripts/sandbox/test.sh` — must print `TESTS OK`.
12. Run `scripts/sandbox/run.sh` — must print `SMOKE RUN OK` (desktop behavior unchanged).

## Contracts / schema added
- `Animora.Contracts.Wire.WireContract` (`Version = 1`, `HeaderName`).
- No entities, no DbContext, no migration, no `Animora.Contracts` change.
- Test surface: `LayeringRulesTests` (3 facts) + 4 placeholder tests.

## Rules that apply here
- **T-RULE 003** — no EF Core 11, no Datasync 9.x may resolve. Pin EF Core `10.0.12` exactly; do not add
  a Datasync pin in this phase.
- **T-RULE 010** — infrastructure packages are pinned exactly, never as ranges.
- **CONVENTIONS §2** — project references must match the table exactly; `LayeringRulesTests` proves it.
- **CONVENTIONS §1** — no `TargetFramework`/`Nullable` in new csproj files; they inherit.
- **ARCHITECTURE.md §8.6** — the contract version is a first-class constant from day one.
- **CONVENTIONS §11** — desktop must still build and smoke-run; this phase adds no runtime code path.

## Out of scope
- Any entity, value object, or DbContext → A02, A09.
- `Animora.Desktop` referencing `Animora.Client.Services` → C01.
- Datasync, Npgsql/PostgreSQL, ASP.NET Core, encryption-bundle package pins → E03, D01, D02, A11.
- `Animora.Sync.Abstractions` interface bodies → E03.
- Any DI registration change in `App.axaml.cs` → C01 onward.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `scripts/sandbox/test.sh` prints `TESTS OK`; all five test projects are discovered and green.
- `dotnet sln list` shows 9 src projects + 5 test projects.
- `LayeringRulesTests` fails if a forbidden reference is added (verify by temporarily adding an EF Core
  reference to `Animora.Domain`, observing the red test, then reverting).
- `grep -r "CommunityToolkit.Datasync\|EntityFrameworkCore" src/Animora.Domain src/Animora.Application`
  returns nothing.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`; app behavior identical
  to before the phase.

## PROGRESS.md update
Mark A01 done; advance "Next phase" to `A02`; append a "Repo state" entry listing: the 4 new src
projects + 5 test projects, the 10 package pins added, `WireContract.Version = 1`,
`scripts/sandbox/test.sh` added, `LayeringRulesTests` facts established, no DI change, no mock deleted,
no migration created, deviations (if any).
