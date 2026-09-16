# Progress & Project State — Windows Frontend

> **Read this file first, every session.** It is the single source of
> truth for "what already exists" and "what to do next." Update it at the
> end of every phase you execute — do not skip this step.

## Current state

- **Next phase to execute:** `02` — Design Tokens & Theme Resources
- **Repo state:** `Animora.sln` (classic format) builds with all 5 projects on
  .NET 10 (`net10.0`, C# 14). Four placeholder class libraries
  (`Animora.Contracts`, `Animora.Domain`, `Animora.Application`,
  `Animora.Sync.Abstractions`) and the Avalonia app `Animora.Desktop`
  (WinExe) exist. `Animora.Desktop` launches an empty 1440×900 `MainWindow`
  ("Animora Desktop — Phase 01 scaffold") via a DI composition root
  (`App.Services`). Central package pins live in `Directory.Packages.props`;
  shared build settings in `Directory.Build.props`. No theming/controls/
  navigation/screens yet.
- **Roadmap status:** Phase 01 done. 1 of 29 phases implemented.

## Phase checklist

| # | Phase | Status | Notes |
|---|---|---|---|
| 01 | Solution & Project Scaffold | ⚠️ Done with deviation | Avalonia.Diagnostics dropped (no 12.x); Fluent theme package added (see log) |
| 02 | Design Tokens & Theme Resources | ⬜ Not started | |
| 03 | Base Control Library | ⬜ Not started | |
| 04 | RTL & Localization Utilities | ⬜ Not started | |
| 05 | App Shell: Sidebar, Topbar, Navigation | ⬜ Not started | |
| 06 | Dashboard Screen | ⬜ Not started | |
| 07 | Reception & Queue Screen | ⬜ Not started | |
| 08 | Patients & Owners — List/Grid | ⬜ Not started | |
| 09 | Patients & Owners — Pet File / Owner Sheet | ⬜ Not started | |
| 10 | Visits — Doctor Rail & Triage | ⬜ Not started | |
| 11 | Visits — Clinical Encounter Wizard | ⬜ Not started | |
| 12 | Vaccination Screen | ⬜ Not started | |
| 13 | Appointments — Calendar Views | ⬜ Not started | |
| 14 | Appointments — Booking Panel | ⬜ Not started | |
| 15 | Boarding — Board & List | ⬜ Not started | |
| 16 | Boarding — Stay Modals | ⬜ Not started | |
| 17 | Inventory — Product List & Settings | ⬜ Not started | |
| 18 | Inventory — Product Modal & Stock Adjust | ⬜ Not started | |
| 19 | Purchase & Suppliers Screen | ⬜ Not started | |
| 20 | Sale/POS — Invoice Workspace | ⬜ Not started | |
| 21 | Sale/POS — Catalog & Payment Modals | ⬜ Not started | |
| 22 | Cashier Screen | ⬜ Not started | |
| 23 | Cheques — List Views & Side Panels | ⬜ Not started | |
| 24 | Cheques — Add/Edit Drawer & Bulk Actions | ⬜ Not started | |
| 25 | Finance — Overview & Income Tabs | ⬜ Not started | |
| 26 | Finance — Expense & Profit Tabs | ⬜ Not started | |
| 27 | History — Unified Ledger | ⬜ Not started | |
| 28 | Profile & Settings Screen | ⬜ Not started | |
| 29 | Cross-Screen Navigation Wiring & Visual QA | ⬜ Not started | |

Status legend: ⬜ Not started · 🔄 In progress · ✅ Done · ⚠️ Done with
deviation (see log below).

## Decision / deviation log

> Append one entry per deviation from the roadmap/conventions, newest on
> top. If a phase followed the plan exactly, no entry is needed.

### Phase 01 — Solution & Project Scaffold

- **Exact package versions chosen** (STACK.md doesn't pin these two):
  - `CommunityToolkit.Mvvm` = **8.4.2** (latest stable; net10.0-compatible).
  - `Microsoft.Extensions.DependencyInjection` = **10.0.12** (latest stable
    10.x line, aligned with .NET 10).
- **Deviation — `Avalonia.Diagnostics` removed.** The phase listed it as a
  Debug-only *placeholder* ("remove later if unused"). Avalonia 12.x has **no
  `Avalonia.Diagnostics` 12.x release** (the DevTools moved into the core
  `Avalonia` package; highest standalone version is 11.3.x). Pinning it to
  12.1.2 makes `dotnet restore` fail (NU1102). Dropped the package to keep an
  exact-pin, no-floating-range policy; DevTools remain available via the core
  package when a later phase wires `AttachDevTools`.
- **Deviation — `Avalonia.Themes.Fluent` 12.1.2 added.** In Avalonia 12.x the
  Fluent theme is a **separate package** (decoupled from the `Avalonia`
  metapackage); `<FluentTheme />` in `App.axaml` fails to resolve without it
  (AVLN2000). Added the pin so the app has a base theme to render the empty
  window. This is the standard base theme every later phase needs anyway.
- **Note — `Animora.sln` in classic format.** The .NET 10 SDK's
  `dotnet new sln` now emits `.slnx` by default; created with
  `--format sln` to honor the phase's explicit `Animora.sln` filename.
- **Note — launch verified indirectly.** The build sandbox is headless
  Linux, so the WinExe cannot open a window here; a `dotnet run` reaches the
  X11 platform init and fails only on `XOpenDisplay` — confirming bootstrap +
  DI + XAML load run correctly. The actual window opens on Windows (target).

## How to update this file at the end of a phase

1. Change that phase's row: `⬜ Not started` → `✅ Done` (or `⚠️ Done with
   deviation` if you deviated — add a log entry explaining why/what).
2. Update **"Next phase to execute"** to the following phase number.
3. Update **"Repo state"** with a one/two-line factual summary of what now
   exists (e.g. "`src/Animora.Desktop` builds and runs; shows Dashboard +
   Sidebar; Patients/Visits/etc. screens are still placeholders").
4. Do not rewrite history rows for already-`Done` phases.
5. Commit this file together with the phase's code changes.
