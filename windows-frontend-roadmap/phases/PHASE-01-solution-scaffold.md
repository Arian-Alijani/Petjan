# Phase 01 — Solution & Project Scaffold

**Band:** S · **Depends on:** none · **Satisfies:** R-7, R-19

## Read before starting
- `CONVENTIONS.md` §1 (folder layout), §5 (DI pattern), §6 (Definition of Done)
- `STACK.md` §2 (Platform & Language), §11 (Canonical Package List — client + shared rows only)
- Do **not** read `Animora-UI` for this phase — there is no visual output yet.

## Goal
Stand up the empty multi-project .NET 10 solution skeleton described in
`CONVENTIONS.md` §1, so every later phase has somewhere correct to add
files. No UI, no styling — just a solution that builds and runs an empty
Avalonia window.

## Tasks
1. At repo root, create `Directory.Build.props`:
   - `<TargetFramework>net10.0</TargetFramework>`
   - `<LangVersion>14.0</LangVersion>`
   - `<Nullable>enable</Nullable>`
   - `<ImplicitUsings>enable</ImplicitUsings>`
2. At repo root, create `Directory.Packages.props` with
   `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
   and pin (exact versions, no floating ranges, per `STACK.md` §11):
   - `Avalonia` `12.1.2`
   - `Avalonia.Desktop` `12.1.2`
   - `Avalonia.Fonts.Inter` `12.1.2` (remove later if unused — placeholder only)
   - `Avalonia.Diagnostics` `12.1.2` (Debug-only reference)
   - `CommunityToolkit.Mvvm` (latest stable compatible with .NET 10 — record exact version used)
   - `Microsoft.Extensions.DependencyInjection` (latest stable 10.x-compatible)
   - Do **not** add `CommunityToolkit.Datasync.*`, `EntityFrameworkCore.Sqlite`,
     or any SQL/sync package in this phase — out of scope until the backend
     roadmap (see `ROADMAP.md` §4 non-goals).
3. Create solution `Animora.sln` at repo root.
4. Create class library projects (empty, just a placeholder `.gitkeep`-style
   `Class1.cs` deleted, no content needed beyond the `.csproj`):
   - `src/Animora.Contracts/Animora.Contracts.csproj`
   - `src/Animora.Domain/Animora.Domain.csproj`
   - `src/Animora.Application/Animora.Application.csproj`
   - `src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj`
   Add a one-line `README.md` in each explaining it is a placeholder for a
   future roadmap phase (Domain/Application/Sync) — except `Contracts`,
   which explains it will hold DTOs/interfaces added by *this* roadmap's UI
   phases.
5. Create the Avalonia app project `src/Animora.Desktop/Animora.Desktop.csproj`
   (`OutputType=WinExe`, references `Animora.Contracts`, `CommunityToolkit.Mvvm`,
   `Microsoft.Extensions.DependencyInjection`, `Avalonia`, `Avalonia.Desktop`).
6. Add `App.axaml` / `App.axaml.cs` and `Program.cs` with the standard
   Avalonia bootstrap (`AppBuilder.Configure<App>().UsePlatformDetect()...`).
7. Set up the composition root in `App.axaml.cs`: build an
   `IServiceCollection` → `IServiceProvider` in `OnFrameworkInitializationCompleted`,
   store it on a static/singleton accessor (e.g. `App.Services`) so later
   phases can resolve ViewModels/services. No services are registered yet
   beyond what's needed to construct an empty `MainWindow`.
8. Add `src/Animora.Desktop/Shell/MainWindow.axaml` (+ `.cs`): a bare
   `Window` with `Title="Animora"`, `Width=1440`, `Height=900`, empty
   `Grid` content (a placeholder `TextBlock` reading "Animora Desktop —
   Phase 01 scaffold" is acceptable).
9. Add all five projects to `Animora.sln`.
10. Verify: `dotnet restore && dotnet build Animora.sln` succeeds; running
    `dotnet run --project src/Animora.Desktop` opens a window.
11. Add a root `.gitignore` covering `bin/`, `obj/`, `.vs/` if not already present.

## Out of scope
- Any theming, controls, navigation, or screen content (Phases 02+).
- Any package related to SQLite/EF Core/Datasync/SQL Server.
- Any macOS/Web project.

## Acceptance criteria (in addition to `CONVENTIONS.md` §6 Definition of Done)
- `Animora.sln` builds with all 5 projects.
- The desktop app launches to a plain window without error.
- `Directory.Packages.props` contains only the packages listed above, each
  pinned to an exact version (record the exact `CommunityToolkit.Mvvm` and
  `Microsoft.Extensions.DependencyInjection` versions chosen in
  `PROGRESS.md`'s decision log, since `STACK.md` doesn't pin these two).

## PROGRESS.md update
Mark Phase 01 done; set "Next phase" to 02; record chosen exact package
versions for `CommunityToolkit.Mvvm` / `Microsoft.Extensions.DependencyInjection`
in the decision log; update "Repo state" to describe the scaffold.
