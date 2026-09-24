# Phase B05 — Audit, logging, diagnostics, crash safety (index only — split into 3 sessions)

**Band:** M · **Track:** B · **Depends on:** B04 · **Satisfies:** R-B09, R-B29
**Runnable after this phase:** desktop offline yes · server required no

Do not implement from this file. Execute one sub-phase per session, in order; each file is self-contained:

| Session | File | Scope |
|---|---|---|
| B05a | `PHASE-B05a-audit-log-behavior.md` | append-only `AuditEntry` table + migration `007_AuditDiagnostics` (AuditEntries only), `IAuditWriter`/`AuditWriter`, `AuditableAttribute` + `AuditBehavior` (in-transaction), `IntegrityAlertSink` (B02 hookup), `ErrorCodes` += `AUDIT_WRITE_FAILED` |
| B05b | `PHASE-B05b-logging-correlation-diagnostics.md` | Serilog file logging (zero external sink) + `LogRedaction`/`LocalLoggingOptions`, `AmbientCorrelation`/`ICorrelationContext.Current` + `UiCorrelationScope`, `IDiagnosticsBundleBuilder`/`DiagnosticsBundleBuilder` (PII-free zip), `ErrorCodes` += `DIAGNOSTICS_BUNDLE_FAILED` |
| B05c | `PHASE-B05c-crash-safety-notifications-tests.md` | `IUserNotificationPort` + `ToastNotificationAdapter` + `NotificationText`, `GlobalExceptionHandler`/`CrashRecoveryService`, `UiDraftState` + migration `008_UiDraftState` + `DraftStateStore`, `MaintenanceScheduler`, `App.axaml.cs` wiring, `ErrorCodes` += `CRASH_RECOVERY_REQUIRED`, all B05 tests + build/test/run gate |

B05 is ✅ only when B05c is done. The split is equal-weight by design (audit → observability → crash
safety/wiring/tests); quality and acceptance rigor are unchanged — every original B05 acceptance
criterion is preserved and assigned to a session (all tests + the runnable gate land in B05c).

> **Migration deviation (recorded once here):** the original B05 used a single migration
> `007_AuditDiagnostics` for both `AuditEntries` and `UiDraftStates`. To keep each session
> self-contained and migrations sequential, `007_AuditDiagnostics` (B05a) carries `AuditEntries` only and
> `UiDraftStates` ships as `008_UiDraftState` (B05c). No schema is dropped or renamed; both sessions log
> the split in `PROGRESS.md`.

## Goal (whole phase)
Make the client observable and crash-proof: an append-only local audit log for every security and
financial action, correlation ids flowing through the whole chain, rotating structured file logs with no
hot-path cost, an exportable diagnostics bundle, and a global exception handler with safe restart and
in-flight UI recovery. This is the last phase before mock retirement begins.

## Out of scope (whole phase — deferred to the named phases)
- The full sync-failure UX wording and routing → E06 (this phase only creates the port).
- Server-side logging, metrics, OpenTelemetry, health endpoints → D01, D13.
- Screens opting into `DraftStateStore` → C03, C07.
- Exporting audit data to the operator → D13/E02.
- Any mock deletion → Track C.
