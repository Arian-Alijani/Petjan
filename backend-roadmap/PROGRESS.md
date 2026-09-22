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

**Next phase: `A01`** — `backend-roadmap/phases/PHASE-A01-backend-track-scaffold.md`

## Phase status

| ID | Status | Date | Notes |
|---|---|---|---|
| A01 | ⬜ Not started | | |
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

_(empty — no phase executed yet)_

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

_(empty — no deviation yet)_
