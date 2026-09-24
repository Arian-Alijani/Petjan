# Phase B05c — Crash safety, notification seam, maintenance & tests (session 3/3 of B05)

**Band:** M · **Track:** B · **Depends on:** B05b ✅ · **Next:** C01 · Split of original B05 (B05a→B05c, one session each).
**Satisfies (completes with B05a+B05b):** R-B09, R-B29 · **Runnable after this phase:** desktop offline yes · server required no

---

## ⚡ Zero-exploration contract (read this first)
> Self-contained and pre-verified. **Do not re-open the repo to "understand" it.** Implement
> top-to-bottom from §Implementation steps. Names here are the **real** ones. If a compile error proves
> a snippet diverges, fix the smallest thing and log it in `PROGRESS.md`; do not pre-emptively audit.

**This is session 3 of 3 — the B05 close-out, and the last phase before Track C mock retirement begins.**
B05a landed the audit plane; B05b landed logging/correlation/diagnostics. **This session lands crash
safety + the user-notification seam + in-flight recovery + maintenance scheduling + the Desktop wiring**,
and **all B05 tests** plus the runnable gate. B05 is ✅ only when this session's gate is green.

### Environment (already true — do not probe)
- `scripts/sandbox/install-prereqs.sh` once; `dotnet-ef 10.0.12` at `.sandbox/tools` (do not
  `dotnet tool install`). Add the `UiDraftState` migration with the exact command in step 3.
- Build/test/run are **only** these scripts: `scripts/sandbox/build.sh full` → `0 warnings / 0 errors`;
  `scripts/sandbox/test.sh` → `TESTS OK`; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
- Sandbox RAM ≈ 985 MiB; no parallel/server-GC flags.

### Rules (condensed — do not open `CONVENTIONS.md`)
- **§2 layering:** crash types in `Infrastructure.Local/Startup`; notification **port** in
  `Animora.Contracts`; adapter/draft store/scheduler in `Client.Services`; Persian text in the Desktop.
- **§4 style:** `sealed`/`sealed record`; nullable honored; async-all-the-way; `Result`/`Result<T>`.
- **A-RULE 026 / R-B09:** crash recovery reclaims **expired claims** (B02 claim tokens) rather than
  reprocessing blindly, so no effect is duplicated.
- **§9 testing:** no test asserts a Persian UI string; no wall-clock (inject `IClock`).
- **§10 item 6:** the Desktop edit is `App.axaml.cs` + one new non-UI mapping file
  (`NotificationText.cs`). A **small** `.axaml` addition is acceptable here **only** if an actionable
  toast truly needs it (this phase is not under the Track-C zero-`.axaml` rule); prefer
  `IDialogService.ShowModal` for actionable notifications and record the choice in `PROGRESS.md`.
- **§11/§12:** build 0/0, tests green, run OK; `PROGRESS.md` updated (mark **B05** done overall + note
  Track C begins next); commit on `genspark_ai_developer`.

## Goal (this session)
Make the client crash-proof and recoverable: a global exception handler with safe restart and crash
report, a next-start recovery that reclaims leases, an in-flight UI draft store, a user-notification
seam the sync UX (E06) later fills, daily/weekly maintenance scheduling, and the Desktop wiring that
starts it all — proved by the full B05 test battery.

## §Reference pack (verbatim — do not re-read source)

### R1 · A11c/A11d + B02 pieces to reuse
`ILocalStoreMaintenance` (A11c) exposes `BackupAsync`, `RestoreAsync`, `QuarantineAndRebuildAsync`,
`VacuumAsync` returning `Result<int>` (pages freed) — the scheduler calls these directly, no new port.
`LocalStoreDiagnostics` (A11c) fields per B05b §R2. `LocalStoreStartup` (A11d) logs its steps internally
via `_steps`/`Steps` (`InternalsVisibleTo Animora.Persistence.Tests`) — separate from the Serilog sink;
`GlobalExceptionHandler`/`CrashRecoveryService` are **new** types alongside it, in
`Infrastructure.Local/Startup/`. B02 claim tokens on `PendingCommand`/`OutboxMessage` make lease reclaim
safe. B05a's `IUserNotificationPort` dependency in `IntegrityAlertSink` is **satisfied for real here**.

### R2 · Toast/dialog reality
`IToastService.Show(string text, Tone tone = Tone.Ok)` (`Tone`: `Ok|Warn|Danger|Info`) is the only signal;
`IDialogService.ShowModal(object content, double maxWidth)` for actionable content. There is no existing
"toast with action button" — `ToastNotificationAdapter` maps `NotificationSeverity → Tone`
(`Info→Info`, `Warning→Warn`, `Error|NeedsAttention→Danger`) and calls `IToastService.Show`; for
`NotificationAction`/`TargetRoute` fall back to `IDialogService.ShowModal` (or a minimal `.axaml`
addition — record the choice). `App.axaml.cs` already calls `AddAnimoraLocalInfrastructure()`/
`AddAnimoraApplication()` (B03) and has the logging/correlation pieces available (B05b).

### R3 · Migration reality
Migrations run `001`–`007` today (`007_AuditDiagnostics` from B05a is AuditEntries only). **The
`UiDraftState` table ships as migration `008_UiDraftState` this session** (the deliberate two-migration
split recorded in B05a). Local entity conventions per earlier phases.

## §Design decisions (implement — do not re-derive)
- **User-facing notification seam:** `Animora.Contracts/Diagnostics/IUserNotificationPort.cs`
  (**Contracts addition**) — `void Notify(NotificationSeverity severity, string code, string? entityRef,
  NotificationAction? action)`; `enum NotificationSeverity { Info, Warning, Error, NeedsAttention }`;
  `NotificationAction(string ActionCode, string? TargetRoute)`. Implemented by
  `Client.Services/Diagnostics/ToastNotificationAdapter.cs` (R2), with Persian text in
  `Animora.Desktop/Diagnostics/NotificationText.cs`. This is the seam **E06** fills with the full
  sync-failure UX; B05 only establishes it.
- **Crash safety:**
  - `Infrastructure.Local/Startup/GlobalExceptionHandler.cs` — subscribes to
    `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and Avalonia's
    dispatcher unhandled-exception hook (registered from `App.axaml.cs`). On a fault it:
    1. writes a `CrashReport` file (`<data>/crashes/crash-<ts>.json`: exception type/message/stack,
       correlation snapshot, `LocalStoreDiagnostics`, last 50 log lines);
    2. writes an `AuditCategory.Maintenance` entry when the store is still usable;
    3. for a **recoverable** UI-thread exception, shows the existing error dialog and keeps the app alive;
       for an unrecoverable one, flushes logs and exits with code 70.
  - `CrashRecoveryService` — on the next start, if a crash marker exists: run `PRAGMA quick_check`,
    reclaim expired `PendingCommand`/`OutboxMessage` leases (B02 claim tokens), report
    `RecoveredFromCrash = true`, offer the diagnostics bundle through `IUserNotificationPort`.
  - **In-flight UI recovery:** `Client.Services/Diagnostics/DraftStateStore.cs` — a `[LocalOnly]
    UiDraftState` table (`Id`, `ScreenKey`, `UserId`, `PayloadJson`, `UpdatedAtUtc`) with
    `SaveAsync`/`LoadAsync`/`ClearAsync` and a 2-second debounce. Migration `008_UiDraftState`. Only
    **explicitly opted-in** screens use it; the phase wires **none** — C03 (encounter) and C07 (invoice
    draft) opt in.
- **Maintenance scheduling:** `Client.Services/Diagnostics/MaintenanceScheduler.cs` — a
  `PeriodicTimer`-based background loop (started after first frame) that calls
  `ILocalStoreMaintenance.BackupAsync` daily and `VacuumAsync` weekly, both skipped while a command pass
  is running, each writing a `StoreMaintenanceLog` + audit row.
- **Desktop wiring (`App.axaml.cs`):** register the logging (from B05b's `LoggingSetup`), the 3 exception
  hooks (**before** anything else in `OnFrameworkInitializationCompleted`), `IUserNotificationPort →
  ToastNotificationAdapter`, the correlation scope into navigation/command execution, and start
  `MaintenanceScheduler` post-first-frame.
- **New error code (this session):** `CRASH_RECOVERY_REQUIRED`. (`AUDIT_WRITE_FAILED` was B05a;
  `DIAGNOSTICS_BUNDLE_FAILED` was B05b.)
- **Tests (all remaining B05 tests):**
  - `tests/Animora.Persistence.Tests/AuditWriterTests.cs` — in-transaction write, append-only, no PII.
  - `tests/Animora.Application.Tests/AuditBehaviorTests.cs` — success/denied/failed records + rollback on
    forced audit-write failure (`AUDIT_WRITE_FAILED`).
  - `tests/Animora.Application.Tests/CorrelationFlowTests.cs` — one id flows handler → audit → log; differs
    across gestures.
  - `tests/Animora.Persistence.Tests/CrashRecoveryTests.cs` — lease reclaim, `quick_check`, marker clear;
    no command processed twice.
  - `tests/Animora.Application.Tests/DiagnosticsBundleTests.cs` — six members present, no PII, redaction
    note; built under 3 seconds on the test DB.

## Files to create / modify
| Path | Action | Contents |
|---|---|---|
| `src/Animora.Contracts/Diagnostics/IUserNotificationPort.cs` | create | Contracts **addition** (notify seam) |
| `src/Animora.Contracts/Diagnostics/NotificationModels.cs` | create | severity + action records |
| `src/Animora.Client.Services/Diagnostics/ToastNotificationAdapter.cs` | create | code → toast/dialog |
| `src/Animora.Infrastructure.Local/Startup/GlobalExceptionHandler.cs` | create | 3 hooks + crash report |
| `src/Animora.Infrastructure.Local/Startup/CrashRecoveryService.cs` | create | quick_check + lease reclaim |
| `src/Animora.Persistence.Sqlite/Entities/UiDraftState.cs` | create | in-flight UI draft row |
| `src/Animora.Persistence.Sqlite/Configurations/Diagnostics/UiDraftStateConfiguration.cs` | create | mapping |
| `src/Animora.Persistence.Sqlite/Migrations/*008_UiDraftState*` | create | via `dotnet-ef` (step 3) + snapshot |
| `src/Animora.Client.Services/Diagnostics/DraftStateStore.cs` | create | debounced draft persistence |
| `src/Animora.Client.Services/Diagnostics/MaintenanceScheduler.cs` | create | daily backup / weekly vacuum |
| `src/Animora.Desktop/Diagnostics/NotificationText.cs` | create | code → Persian text map |
| `src/Animora.Desktop/App.axaml.cs` | modify | logging, exception hooks, notification port, correlation scope, scheduler |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append `CRASH_RECOVERY_REQUIRED` |
| `tests/Animora.Persistence.Tests/AuditWriterTests.cs` | create | in-transaction write, append-only, no PII |
| `tests/Animora.Application.Tests/AuditBehaviorTests.cs` | create | success/denied/failed records + rollback |
| `tests/Animora.Application.Tests/CorrelationFlowTests.cs` | create | ids flow handler → audit → log |
| `tests/Animora.Persistence.Tests/CrashRecoveryTests.cs` | create | lease reclaim, quick_check, marker clear |
| `tests/Animora.Application.Tests/DiagnosticsBundleTests.cs` | create | contents present, no PII, redaction note |
| `backend-roadmap/PROGRESS.md` | modify | mark B05c **and B05** done, next → C01, repo-state entry, deviations, Track-C note |

## §Implementation steps
1. Add `IUserNotificationPort` + models to Contracts; `ToastNotificationAdapter` to Client.Services;
   `NotificationText` to the Desktop. Satisfy `IntegrityAlertSink`'s dependency (B05a) with the real port.
2. Implement `GlobalExceptionHandler` + `CrashRecoveryService`.
3. Add `UiDraftState` + configuration + `DraftStateStore` (no screen opts in yet), then the migration:
   ```bash
   cd /home/user/webapp && .sandbox/tools/dotnet-ef migrations add 008_UiDraftState \
     --project src/Animora.Persistence.Sqlite --startup-project src/Animora.Persistence.Sqlite \
     --context AnimoraLocalDbContext
   ```
   Verify with `dotnet-ef migrations list … --context AnimoraLocalDbContext` (shows `008`).
4. Add `MaintenanceScheduler` (started post-first-frame).
5. Wire `App.axaml.cs`: logging, the 3 exception hooks **before** everything in
   `OnFrameworkInitializationCompleted`, the notification port, the correlation scope, the scheduler.
6. Append `CRASH_RECOVERY_REQUIRED`; write the five test classes.
7. **Gate:** `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`; confirm a log file
   is produced and contains no redacted value.
8. `PROGRESS.md` update (mark B05 done overall; note Track C begins next) + commit on `genspark_ai_developer`.

## Contracts / schema added
- `Animora.Contracts` **additions**: `IUserNotificationPort`, `NotificationSeverity`, `NotificationAction`.
- Local table `UiDraftStates` (migration `008_UiDraftState`).
- Infrastructure: `GlobalExceptionHandler`, `CrashRecoveryService`.
- Client services: `ToastNotificationAdapter`, `DraftStateStore`, `MaintenanceScheduler`.
  Desktop: `NotificationText`. `ErrorCodes` += 1 (`CRASH_RECOVERY_REQUIRED`). No mock deleted this session.

## Rules that apply here
- **A-RULE 026 / R-B09** — crash recovery reclaims expired claims; no effect duplicated.
- **CONVENTIONS §7** — the bundle/crash report contain ids, never owner/pet personal data.
- **CONVENTIONS §10 item 6** — the Desktop edit is `App.axaml.cs` + `NotificationText.cs` (+ at most a
  minimal `.axaml` addition for an actionable toast, if chosen and recorded).

## Out of scope (sibling sessions / later phases)
- Audit table/writer/behavior/integrity sink → **B05a**. Logging/correlation/diagnostics bundle → **B05b**.
- The full sync-failure UX wording and routing → E06 (this phase only creates the port).
- Server-side logging, metrics, OpenTelemetry, health endpoints → D01, D13.
- Screens opting into `DraftStateStore` → C03, C07. Exporting audit data to the operator → D13/E02.
- Any mock deletion → Track C.

## Acceptance criteria
- `scripts/sandbox/build.sh full` = 0/0.
- `dotnet-ef migrations list … --context AnimoraLocalDbContext` shows `008_UiDraftState`.
- `AuditWriterTests` green: an audit row commits with its business change and rolls back with it; updating
  or deleting an `AuditEntry` throws; no column contains an owner/pet name or phone.
- `AuditBehaviorTests` green: a successful command writes `Success`; a permission-denied command writes
  `Denied` and no business row; a handler failure writes `Failed`; a forced audit-write failure rolls the
  whole transaction back with `AUDIT_WRITE_FAILED`.
- `CorrelationFlowTests` green: one `CorrelationId` appears in the handler, the audit row and the log line
  for the same gesture, and differs across gestures.
- `CrashRecoveryTests` green: after a simulated crash with claimed rows, the next start reclaims the
  expired leases, `quick_check` runs, `RecoveredFromCrash = true`, and no command is processed twice.
- `DiagnosticsBundleTests` green: the zip contains all six members; `grep` over the extracted bundle finds
  no value of a redacted property; the bundle is produced in under 3 seconds on the test DB.
- A log file exists under `<data>/logs/` after a smoke run, rolls by size, and contains no password, token
  or phone value.
- Runnability (§11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark B05c done **and mark B05 done overall**; advance "Next phase" to `C01`; append a "Repo state" entry
listing: `IUserNotificationPort` + models + `ToastNotificationAdapter`, `GlobalExceptionHandler`/
`CrashRecoveryService`, `UiDraftStates` + migration `008_UiDraftState` + `DraftStateStore`,
`MaintenanceScheduler`, `NotificationText`, the `App.axaml.cs` wiring, `ErrorCodes` += 1, the 5 test
classes, no mock deleted, any deviation. **Note in the entry that Track C begins next and the
mock-retirement protocol (CONVENTIONS §10) is now in force.**
