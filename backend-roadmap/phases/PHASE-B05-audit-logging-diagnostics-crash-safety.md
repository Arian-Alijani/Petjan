# Phase B05 — Audit, logging, diagnostics, crash safety

**Band:** M · **Track:** B · **Depends on:** B04 · **Satisfies:** R-B09, R-B29
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §7, §8, §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §15 (observability), §10 (audit obligations), §17.3 (client recovery),
  §17.4 (zero-Internet), §21 (A-RULE 016, A-RULE 032)
- `PERFORMANCE.md` §12, §16
- `src/Animora.Application/Correlation/*`, `src/Animora.Application/Dispatch/Dispatcher.cs`
- `src/Animora.Persistence.Sqlite/Maintenance/ILocalStoreMaintenance.cs`,
  `src/Animora.Infrastructure.Local/Startup/LocalStoreDiagnostics.cs`
- `src/Animora.Desktop/App.axaml.cs`, `src/Animora.Desktop/Services/ToastService.cs`

## Goal
Make the client observable and crash-proof: an append-only local audit log for every security and
financial action, correlation ids flowing through the whole chain, rotating structured file logs with
no hot-path cost, an exportable diagnostics bundle, and a global exception handler with safe restart
and in-flight UI recovery. This is the last phase before mock retirement begins.

## Repo reality check (verified against the current tree — read this before coding, skip re-exploring)
1. **`ICorrelationContext`/`CorrelationContext` (B01) already exist and are simpler than this
   phase's "AmbientCorrelation" proposal**: `ICorrelationContext.CurrentId` (`string?`) +
   `Begin(string? id = null)` returning an `IDisposable` scope, `AsyncLocal`-backed. There is
   **no** `TenantId`/`UserId`/`DeviceId`/`OperationId` carried by it today — only the correlation id
   itself; those other four values live only in `CommandContext`/`QueryContext`, which are per-call,
   not ambient. Adding `AmbientCorrelation`/`ICorrelationContext.Current` per the design decision is
   a **new, additive** member on the existing interface (add `AmbientCorrelation? Current { get; }`
   next to the existing two members) — do not replace `CurrentId`/`Begin`, `CorrelationBehavior`
   (stage 1 of `Dispatcher.Chain`) already calls `Begin(CorrelationIdOf(context))` and depends on
   the existing shape.
2. **`AppendOnlyGuard.ProtectedTypes`** is a `HashSet<Type>` literal in
   `Persistence.Sqlite/Context/AppendOnlyGuard.cs` (currently: `JournalEntry`, `JournalLine`,
   `MedicalHistoryEntry`, `StockMovement`, `Payment`, `ChequeEvent`, `StoreMaintenanceLog`). Adding
   `AuditEntry` is a one-line addition to that literal — do not restructure the guard.
3. **`AuditBehavior`'s stated chain position ("immediately after `CommandJournalBehavior`") assumes
   B02 shipped `CommandJournalBehavior` into `Dispatcher.Chain`.** As of this writing `B02 is not
   started`; `Dispatcher.Chain` today is exactly: `Correlation → ExceptionMapping → Validation →
   Authorization → Entitlement → Transaction` (6 entries, `Dispatcher.cs`). If B02 has landed by the
   time B05 runs, insert `AuditBehavior` right after wherever `CommandJournalBehavior` was placed
   (inside `TransactionBehavior`, so both write in the same DB transaction); if for any reason B02's
   chain position differs from its own phase file, follow what B02 actually shipped (check
   `PROGRESS.md`'s B02 "Repo state" entry), not this phase's assumption. Either way, `AuditBehavior`
   must run **inside** the transaction opened by `TransactionBehavior` (i.e. after it in the outer→
   inner order), never outside it, or the "audit row commits/rolls back with the business change"
   acceptance criterion cannot hold.
4. **`ILocalStoreMaintenance`/`LocalStoreDiagnostics` (A11c) already exist** with exactly this
   shape: `BackupAsync`, `RestoreAsync(string backupPath, ct)`, `QuarantineAndRebuildAsync`,
   `VacuumAsync(ct)` returning `Result<int>` (pages freed) — `MaintenanceScheduler` calls these
   directly, no new port needed. `LocalStoreDiagnostics` fields are: `DatabasePath`,
   `NativeSqliteVersion`, `EncryptionActive`, `KeySource` (enum `None|OsKeystore|InsecureFallback`),
   `SchemaVersion` (the **last applied migration name**, a string, not a number),
   `RecoveredFromCrash`, `Quarantined`, `DatabaseBytes`, `LastBackupUtc` — use these exact names when
   building `environment.json` for the diagnostics bundle.
5. **`IToastService`/`IDialogService` have no severity/notification concept today** — `IToastService
   .Show(string text, Tone tone = Tone.Ok)` (`Tone`: presumably `Ok|Warn|Danger|Info` per
   `ToastService.cs`'s icon switch) is the only signal. `ToastNotificationAdapter` maps
   `NotificationSeverity` → `Tone` (`Info→Info`, `Warning→Warn`, `Error|NeedsAttention→Danger`) and
   calls `IToastService.Show`; for `NotificationAction`/`TargetRoute` there is no existing "toast with
   action button" — either extend `ToastMessage`/`ToastHost` minimally (a small `.axaml` addition,
   acceptable here — this phase is not under the Track-C "zero `.axaml`" rule) or fall back to
   `IDialogService.ShowModal` for actionable notifications and plain toast for informational ones;
   record whichever you choose in `PROGRESS.md`.
6. **`App.axaml.cs` still has no reference to `Animora.Client.Services`/`Animora.Application` unless
   B03 already added it** (see B03's reality check item 2). If B03 has landed, the project reference
   and `AddAnimoraLocalInfrastructure()`/`AddAnimoraApplication()` calls already exist — just extend
   the existing `ConfigureServices()` with the logging/exception-hook/notification-port lines listed
   below; do not re-add the project reference or re-call those two extension methods.
7. **`LocalStoreStartup` (A11d) already logs steps internally via `_steps`/`Steps`** (visible to
   `Animora.Persistence.Tests` via `InternalsVisibleTo`) — this is separate from the Serilog file
   sink this phase adds; do not conflate the two. `GlobalExceptionHandler`/`CrashRecoveryService`
   are new types this phase adds in `Infrastructure.Local/Startup/`, alongside (not replacing)
   `LocalStoreStartup`.

## Design decisions (pre-made — implement, do not re-derive)
- **Audit table** (`[LocalOnly]`, migration `007_AuditDiagnostics`):
  `AuditEntry` — `Id` (ULID), `TenantId`, `UserId`, `DeviceId`, `OccurredAtUtc`,
  `Category` (`AuditCategory`), `Action` (string, `<context>.<verb>`), `EntityType`, `EntityId`,
  `OperationId`, `CorrelationId`, `Outcome` (`AuditOutcome`), `BeforeRef`, `AfterRef`,
  `AmountMinor`, `AmountCurrency`, `Detail` (≤ 1000 chars, machine detail, never a Persian string).
  `enum AuditCategory { Security, Financial, Clinical, Configuration, Integrity, Maintenance }`.
  `enum AuditOutcome { Success, Denied, Failed }`.
  Added to `AppendOnlyGuard.ProtectedTypes`. Indexes:
  `IX_AuditEntries_OccurredAtUtc`, `IX_AuditEntries_Category_OccurredAtUtc`,
  `IX_AuditEntries_EntityType_EntityId`, `IX_AuditEntries_OperationId`.
  `BeforeRef`/`AfterRef` are **references** (`"<EntityType>:<EntityId>:<Version>"`), never full
  payload copies — the audit log must not become a second database.
- `Application/Auditing/IAuditWriter.cs` — `Task<Result> WriteAsync(AuditRecord record,
  CancellationToken ct)` and `void Enlist(AuditRecord record)` (buffered into the current unit of work,
  written in the **same transaction** as the business change).
  `AuditRecord` is a `sealed record` mirroring the columns.
  `Persistence.Sqlite/Auditing/AuditWriter.cs` implements both.
- `Application/Auditing/AuditableAttribute.cs` — `[Auditable(AuditCategory.Financial,
  "sales.invoice.post")]` on a command record; read at **registration** (like B04's permission
  attribute) and stored on the `HandlerEntry`.
  `Application/Behaviors/AuditBehavior.cs` — placed immediately **after**
  `CommandJournalBehavior`; enlists a `Success` record on a successful `Result`, a `Denied` record when
  the authorization/entitlement behavior short-circuited, and a `Failed` record otherwise. It never
  throws and never blocks: a failed audit write fails the whole transaction (auditing a financial
  action is not optional — CONVENTIONS §7).
- `Application/Auditing/IIntegrityAlertSink.cs` (declared in B02, implemented here):
  `IntegrityAlertSink` writes an `AuditCategory.Integrity` entry **and** raises a
  `NeedsAttention`-class notification through `IUserNotificationPort` (below).
- **Correlation ids end to end:** `CorrelationContext` (B01) is extended with an
  `AmbientCorrelation` record (`CorrelationId`, `TenantId`, `UserId`, `DeviceId`, `OperationId?`)
  exposed as `ICorrelationContext.Current`. Every log line, audit row and (from E06) outbound request
  carries all five. The Desktop starts a correlation scope per user gesture in
  `NavigationService`/command execution via a single helper
  `Client.Services/Diagnostics/UiCorrelationScope.cs` — no ViewModel change is required.
- **Logging (zero external sink — A-RULE 016/032):**
  - Package pins added: `Serilog` `4.2.0`, `Serilog.Extensions.Logging` `9.0.0`,
    `Serilog.Sinks.File` `6.0.0` (STACK.md §9 allows "Serilog-or-builtin"; file sink only).
    **No console sink in release, no network sink at all.**
  - `Infrastructure.Local/Diagnostics/LoggingSetup.cs` — `ILoggerFactory ConfigureLocalLogging(
    LocalLoggingOptions options)`: rolling file at `<data>/logs/animora-.log`, daily roll,
    `retainedFileCountLimit = 14`, `fileSizeLimitBytes = 25 MB`, `rollOnFileSizeLimit = true`,
    shared = false, buffered = true with a 2-second flush, minimum level `Information`
    (`Warning` for `Microsoft.EntityFrameworkCore.*`).
  - `LocalLoggingOptions` — `MinimumLevel`, `RetainedFiles`, `FileSizeLimitBytes`,
    `EnableSqlLogging` (default `false`; enabling it also forces
    `EnableSensitiveDataLogging = false` — SQL parameters are never logged).
  - **Hot-path rule (PERFORMANCE.md §16):** no logging inside `Dispatcher`'s per-call path except a
    single `LogWarning`/`LogError` on a failed `Result`, guarded by `IsEnabled`. Rendering, binding
    and list virtualization paths log nothing.
  - **Redaction:** `Diagnostics/LogRedaction.cs` — a `Destructure`/enricher pass that removes
    values of properties named `Password`, `PasswordHash`, `Passphrase`, `Token`, `Key`,
    `Signature`, `NationalId`, `Phone` (replaced with `"[redacted]"`).
- **Diagnostics bundle:** `Application/Diagnostics/IDiagnosticsBundleBuilder.cs` +
  `Infrastructure.Local/Diagnostics/DiagnosticsBundleBuilder.cs`:
  `Task<Result<string>> BuildAsync(CancellationToken ct)` writes
  `<data>/diagnostics/animora-diag-<yyyyMMdd-HHmmss>.zip` containing
  `environment.json` (OS, RID, app version, `LocalStoreDiagnostics`, native SQLite version),
  `schema.json` (applied migrations + row counts per table),
  `pipeline.json` (`PendingCommandSummary` + the 50 newest non-settled commands, payloads **excluded**),
  `audit-tail.json` (the 500 newest audit rows, `Detail` truncated),
  `logs/` (the newest 3 log files), and `redaction.txt` describing what was removed.
  **No personal data**: owner/pet names, phones and national ids are excluded by construction — the
  bundle carries ids only.
- **User-facing failure notification seam:** `Animora.Contracts/Diagnostics/IUserNotificationPort.cs`
  (**Contracts addition**) — `void Notify(NotificationSeverity severity, string code,
  string? entityRef, NotificationAction? action)`; `enum NotificationSeverity { Info, Warning,
  Error, NeedsAttention }`; `NotificationAction(string ActionCode, string? TargetRoute)`.
  Implemented by `Client.Services/Diagnostics/ToastNotificationAdapter.cs`, which maps a code to the
  existing `IToastService`/`IDialogService` (the Persian text lives in the Desktop mapping table
  `Animora.Desktop/Diagnostics/NotificationText.cs`). This is the seam E06 fills with the full
  sync-failure UX contract; B05 only establishes it.
- **Crash safety:**
  - `Infrastructure.Local/Startup/GlobalExceptionHandler.cs` — subscribes to
    `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and
    Avalonia's dispatcher unhandled-exception hook (registered from `App.axaml.cs`). On a fault it:
    1. writes a `CrashReport` file (`<data>/crashes/crash-<ts>.json`: exception type/message/stack,
       correlation snapshot, `LocalStoreDiagnostics`, last 50 log lines);
    2. writes an `AuditCategory.Maintenance` entry when the store is still usable;
    3. for a **recoverable** UI-thread exception, shows the existing error dialog and keeps the app
       alive; for an unrecoverable one, flushes logs and exits with code 70.
  - `CrashRecoveryService` — on the next start, if an unhandled crash marker exists:
    run `PRAGMA quick_check`, reclaim expired `PendingCommand`/`OutboxMessage` leases (the claim
    tokens from B02 make this safe), report `RecoveredFromCrash = true` in diagnostics, and offer the
    diagnostics bundle through `IUserNotificationPort`.
  - **In-flight UI recovery:** `Client.Services/Diagnostics/DraftStateStore.cs` — a `[LocalOnly]
    UiDraftState` table (`Id`, `ScreenKey`, `UserId`, `PayloadJson`, `UpdatedAtUtc`) with
    `SaveAsync`/`LoadAsync`/`ClearAsync` and a debounce of 2 seconds. Only **explicitly opted-in**
    screens use it; the phase wires **none** — C03 (encounter) and C07 (invoice draft) opt in.
- **Maintenance scheduling:** `Client.Services/Diagnostics/MaintenanceScheduler.cs` — a
  `PeriodicTimer`-based background loop (started after first frame) that calls
  `ILocalStoreMaintenance.BackupAsync` daily and `VacuumAsync` weekly, both skipped while a command
  pass is running, each writing a `StoreMaintenanceLog` + audit row.
- **New error codes:** `AUDIT_WRITE_FAILED`, `DIAGNOSTICS_BUNDLE_FAILED`, `CRASH_RECOVERY_REQUIRED`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `Directory.Packages.props` | modify | pin Serilog 4.2.0 + Extensions.Logging 9.0.0 + Sinks.File 6.0.0 |
| `src/Animora.Persistence.Sqlite/Entities/AuditEntry.cs` | create | append-only audit row |
| `src/Animora.Persistence.Sqlite/Entities/UiDraftState.cs` | create | in-flight UI draft row |
| `src/Animora.Persistence.Sqlite/Configurations/Diagnostics/*.cs` | create | 2 configurations + indexes |
| `src/Animora.Persistence.Sqlite/Context/AppendOnlyGuard.cs` | modify | add `AuditEntry` |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `007_AuditDiagnostics` + snapshot |
| `src/Animora.Application/Auditing/IAuditWriter.cs` | create | audit port + `AuditRecord` |
| `src/Animora.Application/Auditing/AuditableAttribute.cs` | create | registration-time audit binding |
| `src/Animora.Application/Auditing/IIntegrityAlertSink.cs` | modify | (declared B02) documented contract |
| `src/Animora.Application/Behaviors/AuditBehavior.cs` | create | enlist success/denied/failed records |
| `src/Animora.Application/Correlation/CorrelationContext.cs` | modify | `AmbientCorrelation` + `Current` |
| `src/Animora.Application/Diagnostics/IDiagnosticsBundleBuilder.cs` | create | bundle port |
| `src/Animora.Persistence.Sqlite/Auditing/AuditWriter.cs` | create | in-transaction audit writer |
| `src/Animora.Persistence.Sqlite/Auditing/IntegrityAlertSink.cs` | create | integrity audit + notification |
| `src/Animora.Infrastructure.Local/Diagnostics/LoggingSetup.cs` | create | Serilog file logging config |
| `src/Animora.Infrastructure.Local/Diagnostics/LogRedaction.cs` | create | sensitive-property redaction |
| `src/Animora.Infrastructure.Local/Options/LocalLoggingOptions.cs` | create | level/retention/size options |
| `src/Animora.Infrastructure.Local/Diagnostics/DiagnosticsBundleBuilder.cs` | create | zip bundle assembly |
| `src/Animora.Infrastructure.Local/Startup/GlobalExceptionHandler.cs` | create | 3 hooks + crash report |
| `src/Animora.Infrastructure.Local/Startup/CrashRecoveryService.cs` | create | quick_check + lease reclaim |
| `src/Animora.Contracts/Diagnostics/IUserNotificationPort.cs` | create | Contracts **addition** (notify seam) |
| `src/Animora.Contracts/Diagnostics/NotificationModels.cs` | create | severity + action records |
| `src/Animora.Client.Services/Diagnostics/ToastNotificationAdapter.cs` | create | code → toast/dialog |
| `src/Animora.Client.Services/Diagnostics/UiCorrelationScope.cs` | create | per-gesture correlation scope |
| `src/Animora.Client.Services/Diagnostics/DraftStateStore.cs` | create | debounced draft persistence |
| `src/Animora.Client.Services/Diagnostics/MaintenanceScheduler.cs` | create | daily backup / weekly vacuum |
| `src/Animora.Desktop/Diagnostics/NotificationText.cs` | create | code → Persian text map |
| `src/Animora.Desktop/App.axaml.cs` | modify | logging, exception hooks, notification port, scheduler |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 3 codes |
| `tests/Animora.Persistence.Tests/AuditWriterTests.cs` | create | in-transaction write, append-only, no PII |
| `tests/Animora.Application.Tests/AuditBehaviorTests.cs` | create | success/denied/failed records + rollback |
| `tests/Animora.Application.Tests/CorrelationFlowTests.cs` | create | ids flow handler → audit → log |
| `tests/Animora.Persistence.Tests/CrashRecoveryTests.cs` | create | lease reclaim, quick_check, marker clear |
| `tests/Animora.Application.Tests/DiagnosticsBundleTests.cs` | create | contents present, no PII, redaction note |

## Implementation steps
1. Pin the three Serilog packages; add `LocalLoggingOptions`, `LoggingSetup`, `LogRedaction`.
2. Add `AuditEntry` + `UiDraftState` + configurations + `AppendOnlyGuard` entry + migration
   `007_AuditDiagnostics`.
3. Add `IAuditWriter`/`AuditRecord` and `AuditWriter` (enlist into the current UoW).
4. Add `AuditableAttribute`, read it at registration, and implement `AuditBehavior` right after
   `CommandJournalBehavior`. Apply the attribute to the existing commands from B02/B03
   (`SignInCommand` → `Security/identity.signin`, `ChangePasswordCommand` →
   `Security/identity.password.change`, `CompleteFirstRunSetupCommand` →
   `Configuration/identity.firstrun`).
5. Implement `IntegrityAlertSink` and connect B02's fingerprint-mismatch path to it.
6. Extend `CorrelationContext` with `AmbientCorrelation`/`Current`; add `UiCorrelationScope`.
7. Add `IUserNotificationPort` + models to Contracts, `ToastNotificationAdapter` to Client.Services,
   `NotificationText` to the Desktop.
8. Implement `DiagnosticsBundleBuilder` with the six bundle members and the PII exclusion rule.
9. Implement `GlobalExceptionHandler` + `CrashRecoveryService`; register the hooks in `App.axaml.cs`
   **before** anything else in `OnFrameworkInitializationCompleted`.
10. Add `DraftStateStore` (no screen opts in yet) and `MaintenanceScheduler` (started post-first-frame).
11. Append the 3 error codes; write the five test classes.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`; confirm a log file is
    produced and contains no redacted value.

## Contracts / schema added
- `Animora.Contracts` **additions**: `IUserNotificationPort`, `NotificationSeverity`,
  `NotificationAction`.
- Local tables: `AuditEntries`, `UiDraftStates` (migration `007_AuditDiagnostics`).
- Application: `IAuditWriter`/`AuditRecord`, `AuditableAttribute`, `AuditBehavior`,
  `IDiagnosticsBundleBuilder`, `AmbientCorrelation`.
- Persistence: `AuditWriter`, `IntegrityAlertSink`.
- Infrastructure: `LoggingSetup`, `LogRedaction`, `LocalLoggingOptions`, `DiagnosticsBundleBuilder`,
  `GlobalExceptionHandler`, `CrashRecoveryService`.
- Client services: `ToastNotificationAdapter`, `UiCorrelationScope`, `DraftStateStore`,
  `MaintenanceScheduler`. Desktop: `NotificationText`.
- DI: logging, exception hooks, `IUserNotificationPort`, `MaintenanceScheduler`. `ErrorCodes` += 3.
  No mock deleted in this phase.

## Rules that apply here
- **CONVENTIONS §7 / R-B29** — every security and financial action writes an append-only audit row in
  the same transaction; a failed audit write fails the operation.
- **A-RULE 016 / A-RULE 032 / R-B27** — observability is local-only: a file sink and a zip bundle, no
  network sink, no cloud telemetry, and removing the log directory never breaks the app.
- **PERFORMANCE.md §16** — no logging on hot paths; the dispatcher logs at most once per failed call,
  guarded by `IsEnabled`; the file sink is buffered.
- **A-RULE 026 / R-B09** — crash recovery reclaims expired claims rather than reprocessing blindly, so
  no effect is duplicated.
- **CONVENTIONS §7** — logs and bundles are redacted; the bundle contains ids, never owner/pet
  personal data.
- **CONVENTIONS §10 item 6** — no `.axaml` change; the Desktop edit is `App.axaml.cs` plus one new
  non-UI mapping file.

## Out of scope
- The full sync-failure UX wording and routing → E06 (this phase only creates the port).
- Server-side logging, metrics, OpenTelemetry, health endpoints → D01, D13.
- Screens opting into `DraftStateStore` → C03, C07.
- Exporting audit data to the operator → D13/E02.
- Any mock deletion → Track C.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `AuditWriterTests` green: an audit row commits with its business change and rolls back with it;
  updating or deleting an `AuditEntry` throws; no column contains an owner/pet name or phone.
- `AuditBehaviorTests` green: a successful command writes `Success`; a permission-denied command writes
  `Denied` and no business row; a handler failure writes `Failed`; a forced audit-write failure rolls
  the whole transaction back with `AUDIT_WRITE_FAILED`.
- `CorrelationFlowTests` green: one `CorrelationId` appears in the handler, the audit row and the log
  line for the same gesture, and differs across gestures.
- `CrashRecoveryTests` green: after a simulated crash with claimed rows, the next start reclaims the
  expired leases, `quick_check` runs, `RecoveredFromCrash = true`, and no command is processed twice.
- `DiagnosticsBundleTests` green: the zip contains all six members; `grep` over the extracted bundle
  finds no value of a redacted property; the bundle is produced in under 3 seconds on the test DB.
- A log file exists under `<data>/logs/` after a smoke run, rolls by size, and contains no password,
  token or phone value.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark B05 done; advance "Next phase" to `C01`; append a "Repo state" entry listing: the Serilog pins,
`AuditEntries`/`UiDraftStates` + migration `007_AuditDiagnostics`, `IAuditWriter`/`AuditBehavior` and
the audit attribute usage, `IntegrityAlertSink`, correlation propagation, logging + redaction setup,
the diagnostics bundle contents, `GlobalExceptionHandler`/`CrashRecoveryService`, the
`Animora.Contracts` addition (`IUserNotificationPort`), `ToastNotificationAdapter`, `DraftStateStore`,
`MaintenanceScheduler`, the DI changes, `ErrorCodes` += 3, the 5 test classes, no mock deleted,
deviations. **Note in the entry that Track C begins next and the mock-retirement protocol
(CONVENTIONS §10) is now in force.**
