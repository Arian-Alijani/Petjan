# Phase B05b — Logging, correlation propagation & diagnostics bundle (session 2/3 of B05)

**Band:** M · **Track:** B · **Depends on:** B05a ✅ · **Next:** B05c · Split of original B05 (B05a→B05c, one session each).
**Satisfies (with B05a+B05c):** R-B09, R-B29 · **Runnable after this phase:** desktop offline yes · server required no

---

## ⚡ Zero-exploration contract (read this first)
> Self-contained and pre-verified. **Do not re-open the repo to "understand" it.** Implement
> top-to-bottom from §Implementation steps. Names here are the **real** ones. If a compile error proves
> a snippet diverges, fix the smallest thing and log it in `PROGRESS.md`; do not pre-emptively audit.

**This is session 2 of 3.** B05a landed the audit plane. **This session lands observability only**: the
Serilog file logging (zero external sink) + redaction, the end-to-end correlation propagation
(`AmbientCorrelation` + `UiCorrelationScope`), and the diagnostics-bundle builder. **No crash safety,
no notification port, no draft store, no maintenance scheduler, no Desktop `App.axaml.cs` wiring, no
tests** — those are B05c. Do not build ahead of this scope.

### Environment (already true — do not probe)
- `scripts/sandbox/install-prereqs.sh` once; `dotnet-ef 10.0.12` at `.sandbox/tools` (do not
  `dotnet tool install`). **No new migration this session.**
- Build is **only** `scripts/sandbox/build.sh full` → `0 warnings / 0 errors`. **No `test.sh`/`run.sh`
  this session** (tests + smoke gate are B05c). Sandbox RAM ≈ 985 MiB; no parallel/server-GC flags.

### Rules (condensed — do not open `CONVENTIONS.md`)
- **§2 layering:** logging setup + bundle assembly live in `Infrastructure.Local`; the correlation
  member + bundle **port** live in `Animora.Application`; `Client.Services` references Application etc.
- **§4 style:** `sealed`/`sealed record`; nullable honored; async-all-the-way; `Result`/`Result<T>`.
- **A-RULE 016 / A-RULE 032 / R-B27:** observability is **local-only** — a file sink and a zip bundle;
  **no console sink in release, no network sink at all**; removing the log directory never breaks the app.
- **PERFORMANCE.md §16:** no logging on hot paths; the dispatcher logs at most once per failed call,
  guarded by `IsEnabled`; the file sink is buffered.
- **§7:** logs and bundles are **redacted**; the bundle carries ids, never owner/pet personal data.
- **§11/§12:** build 0/0; `PROGRESS.md` updated; commit on `genspark_ai_developer`.

## Goal (this session)
Give the client rotating structured file logs with no hot-path cost and full redaction, thread a single
correlation id through handler → audit → log for every gesture, and produce an exportable, PII-free
diagnostics zip.

## §Reference pack (verbatim — do not re-read source)

### R1 · Correlation reality (extend additively)
`ICorrelationContext`/`CorrelationContext` (B01) expose `CurrentId` (`string?`) + `Begin(string? id = null)`
returning an `IDisposable` scope, `AsyncLocal`-backed. There is **no** tenant/user/device/operation on it
today. Add `AmbientCorrelation? Current { get; }` **next to** the existing two members — do **not** replace
`CurrentId`/`Begin`. `CorrelationBehavior` (stage 1 of `Dispatcher.Chain`) already calls
`Begin(CorrelationIdOf(context))` and depends on the existing shape. `CommandContext`/`QueryContext` carry
tenant/user/device/operation per-call (B01).

### R2 · Diagnostics inputs that already exist (reuse names verbatim)
`LocalStoreDiagnostics` (A11c, in `Persistence.Sqlite/Maintenance/`) fields: `DatabasePath`,
`NativeSqliteVersion`, `EncryptionActive`, `KeySource` (`None|OsKeystore|InsecureFallback`),
`SchemaVersion` (last applied migration **name**, a string), `RecoveredFromCrash`, `Quarantined`,
`DatabaseBytes`, `LastBackupUtc` — use these exact names in `environment.json`. B02's
`PendingCommandSummary` + the pending-command store provide `pipeline.json`; B05a's `AuditEntries` provide
`audit-tail.json`. `ILocalStoreMaintenance` (A11c) already exposes `BackupAsync`/`VacuumAsync` etc. (used
by the scheduler in B05c, not here). Data directory layout: `<data>/logs/`, `<data>/diagnostics/`.

### R3 · STACK/package reality
STACK.md §9 allows "Serilog-or-builtin"; **file sink only**. Package pins are added to
`Directory.Packages.props` centrally and referenced (no version) from `Infrastructure.Local`.

## §Design decisions (implement — do not re-derive)
- **Correlation ids end to end:** extend `CorrelationContext` (B01) with an `AmbientCorrelation` record
  (`CorrelationId`, `TenantId`, `UserId`, `DeviceId`, `OperationId?`) exposed as `ICorrelationContext.Current`.
  Every log line and audit row carries all five (E06 later adds outbound requests). The Desktop starts a
  correlation scope per user gesture via a single helper `Client.Services/Diagnostics/UiCorrelationScope.cs`
  — **no ViewModel change required** (the Desktop wires the scope into navigation/command execution in
  B05c; this session ships the helper + the ambient member).
- **Logging (zero external sink — A-RULE 016/032):**
  - Package pins added: `Serilog` `4.2.0`, `Serilog.Extensions.Logging` `9.0.0`, `Serilog.Sinks.File`
    `6.0.0`. **No console sink in release, no network sink at all.**
  - `Infrastructure.Local/Diagnostics/LoggingSetup.cs` — `ILoggerFactory ConfigureLocalLogging(
    LocalLoggingOptions options)`: rolling file at `<data>/logs/animora-.log`, daily roll,
    `retainedFileCountLimit = 14`, `fileSizeLimitBytes = 25 MB`, `rollOnFileSizeLimit = true`,
    `shared = false`, `buffered = true` with a 2-second flush, minimum level `Information`
    (`Warning` for `Microsoft.EntityFrameworkCore.*`).
  - `LocalLoggingOptions` — `MinimumLevel`, `RetainedFiles`, `FileSizeLimitBytes`, `EnableSqlLogging`
    (default `false`; enabling it also forces `EnableSensitiveDataLogging = false` — SQL parameters are
    never logged).
  - **Hot-path rule (PERFORMANCE.md §16):** no logging inside `Dispatcher`'s per-call path except a single
    `LogWarning`/`LogError` on a failed `Result`, guarded by `IsEnabled`. Rendering/binding/virtualization
    log nothing.
  - **Redaction:** `Infrastructure.Local/Diagnostics/LogRedaction.cs` — a `Destructure`/enricher pass that
    removes values of properties named `Password`, `PasswordHash`, `Passphrase`, `Token`, `Key`,
    `Signature`, `NationalId`, `Phone` (replaced with `"[redacted]"`).
- **Diagnostics bundle:** `Application/Diagnostics/IDiagnosticsBundleBuilder.cs` +
  `Infrastructure.Local/Diagnostics/DiagnosticsBundleBuilder.cs`:
  `Task<Result<string>> BuildAsync(CancellationToken ct)` writes
  `<data>/diagnostics/animora-diag-<yyyyMMdd-HHmmss>.zip` containing:
  `environment.json` (OS, RID, app version, `LocalStoreDiagnostics`, native SQLite version),
  `schema.json` (applied migrations + row counts per table),
  `pipeline.json` (`PendingCommandSummary` + the 50 newest non-settled commands, payloads **excluded**),
  `audit-tail.json` (the 500 newest audit rows, `Detail` truncated),
  `logs/` (the newest 3 log files), and `redaction.txt` describing what was removed.
  **No personal data**: owner/pet names, phones and national ids are excluded by construction — ids only.
  On failure → `DIAGNOSTICS_BUNDLE_FAILED`.
- **New error code (this session):** `DIAGNOSTICS_BUNDLE_FAILED`. (`AUDIT_WRITE_FAILED` was B05a;
  `CRASH_RECOVERY_REQUIRED` is B05c.)

## Files to create / modify
| Path | Action | Contents |
|---|---|---|
| `Directory.Packages.props` | modify | pin Serilog 4.2.0 + Extensions.Logging 9.0.0 + Sinks.File 6.0.0 |
| `src/Animora.Infrastructure.Local/Animora.Infrastructure.Local.csproj` | modify | + 3 Serilog package refs (no version) |
| `src/Animora.Application/Correlation/CorrelationContext.cs` | modify | `AmbientCorrelation` + `Current` (additive) |
| `src/Animora.Infrastructure.Local/Diagnostics/LoggingSetup.cs` | create | Serilog file logging config |
| `src/Animora.Infrastructure.Local/Diagnostics/LogRedaction.cs` | create | sensitive-property redaction |
| `src/Animora.Infrastructure.Local/Options/LocalLoggingOptions.cs` | create | level/retention/size options |
| `src/Animora.Application/Diagnostics/IDiagnosticsBundleBuilder.cs` | create | bundle port |
| `src/Animora.Infrastructure.Local/Diagnostics/DiagnosticsBundleBuilder.cs` | create | zip bundle assembly |
| `src/Animora.Client.Services/Diagnostics/UiCorrelationScope.cs` | create | per-gesture correlation scope helper |
| `src/Animora.Infrastructure.Local/LocalInfrastructureServiceCollectionExtensions.cs` | modify | register logging + bundle builder |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append `DIAGNOSTICS_BUNDLE_FAILED` |
| `backend-roadmap/PROGRESS.md` | modify | mark B05b done, next → B05c, repo-state entry, deviations |

## §Implementation steps
1. Pin the three Serilog packages; add `LocalLoggingOptions`, `LoggingSetup`, `LogRedaction`.
2. Extend `CorrelationContext` with `AmbientCorrelation`/`Current`; add `UiCorrelationScope`.
3. Implement `DiagnosticsBundleBuilder` with the six bundle members and the PII-exclusion rule; register
   logging + the bundle builder in `AddAnimoraLocalInfrastructure`.
4. Append `DIAGNOSTICS_BUNDLE_FAILED`.
5. **Gate:** `scripts/sandbox/build.sh full` = 0/0.
6. `PROGRESS.md` update + commit on `genspark_ai_developer`.

## Contracts / schema added
- Application: `AmbientCorrelation` (+ `ICorrelationContext.Current`), `IDiagnosticsBundleBuilder`.
- Infrastructure: `LoggingSetup`, `LogRedaction`, `LocalLoggingOptions`, `DiagnosticsBundleBuilder`.
- Client services: `UiCorrelationScope`. Package pins: 3 Serilog packages. `ErrorCodes` += 1
  (`DIAGNOSTICS_BUNDLE_FAILED`). No migration, no mock deleted this session.

## Rules that apply here
- **A-RULE 016 / A-RULE 032 / R-B27** — file sink + zip bundle only; no network sink; log dir removable.
- **PERFORMANCE.md §16** — no hot-path logging; dispatcher logs ≤ once per failed call; sink buffered.
- **CONVENTIONS §7** — logs and bundle redacted; ids only, never owner/pet personal data.

## Out of scope (sibling sessions)
- Audit table/writer/behavior/integrity sink → **B05a** (already done).
- Crash safety, `IUserNotificationPort` + adapter + Persian text, `DraftStateStore` (+ migration `008`),
  `MaintenanceScheduler`, `App.axaml.cs` wiring, all tests + `run.sh` gate → **B05c**.

## Acceptance criteria
- `scripts/sandbox/build.sh full` = 0/0.
- `grep -rn "Serilog" Directory.Packages.props` shows the 3 pins;
  `grep -rn "Serilog" src/Animora.Infrastructure.Local/Animora.Infrastructure.Local.csproj` shows the
  references and **no** `csproj` outside `Infrastructure.Local` references a Serilog sink.
- `grep -rn "AmbientCorrelation\|Current" src/Animora.Application/Correlation/CorrelationContext.cs`
  shows the additive member (existing `CurrentId`/`Begin` intact).
- `DIAGNOSTICS_BUNDLE_FAILED` is present in `ErrorCodes.cs`; `DiagnosticsBundleBuilder` writes the six
  named members (verified in B05c's `DiagnosticsBundleTests`).

## PROGRESS.md update
Mark B05b done; advance "Next phase" to `B05c`; append a "Repo state" entry listing: the Serilog pins,
`LoggingSetup`/`LogRedaction`/`LocalLoggingOptions`, the `AmbientCorrelation`/`Current` additive member +
`UiCorrelationScope`, the diagnostics bundle contents + `IDiagnosticsBundleBuilder`/`DiagnosticsBundleBuilder`,
the DI additions, `ErrorCodes` += 1, and any deviation with its justification.
