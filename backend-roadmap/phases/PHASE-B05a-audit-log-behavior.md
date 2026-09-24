# Phase B05a — Audit table, writer, behavior & integrity sink (session 1/3 of B05)

**Band:** M · **Track:** B · **Depends on:** B04 ✅ · **Next:** B05b · Split of original B05 (B05a→B05c, one session each).
**Satisfies (with B05b+B05c):** R-B09, R-B29 · **Runnable after this phase:** desktop offline yes · server required no

---

## ⚡ Zero-exploration contract (read this first)
> Self-contained and pre-verified. **Do not re-open the repo to "understand" it.** Implement
> top-to-bottom from §Implementation steps. Names here are the **real** ones. If a compile error proves
> a snippet diverges, fix the smallest thing and log it in `PROGRESS.md`; do not pre-emptively audit.

**This is session 1 of 3.** It lands the **audit plane only**: the append-only `AuditEntry` table (part
of migration `007_AuditDiagnostics`), `IAuditWriter`/`AuditWriter`, `AuditableAttribute` + `AuditBehavior`
in the pipeline, and `IntegrityAlertSink`. **No logging/correlation/diagnostics-bundle, no crash safety
/ notification / scheduler, no tests** — those are B05b (observability) and B05c (crash safety + wiring +
tests + gate). Do not build ahead of this scope.

> **Migration note:** the original B05 used a single migration `007_AuditDiagnostics` for both the
> `AuditEntry` and the `UiDraftState` tables. To keep migrations sequential and each session
> self-contained, **B05a creates `007_AuditDiagnostics` containing the `AuditEntries` table only**, and
> **B05c adds `UiDraftStates` in a follow-on migration `008_UiDraftState`**. Record this two-migration
> split in `PROGRESS.md` (it is the only deviation from the original single-migration plan; no schema is
> dropped or renamed).

### Environment (already true — do not probe)
- `scripts/sandbox/install-prereqs.sh` once; `dotnet-ef 10.0.12` at `.sandbox/tools` (do not
  `dotnet tool install`). Add the migration with the exact command in step 2.
- Build is **only** `scripts/sandbox/build.sh full` → `0 warnings / 0 errors`. **No `test.sh`/`run.sh`
  this session** (tests + smoke gate are B05c). Sandbox RAM ≈ 985 MiB; no parallel/server-GC flags.

### Rules (condensed — do not open `CONVENTIONS.md`)
- **§2 layering:** Application → Domain only; `Persistence.Sqlite` may reference Application+Domain.
- **§4 style:** `sealed`/`sealed record`; nullable honored; async-all-the-way; `Result`/`Result<T>`;
  `IClock` injected; Ids via `IdGenerator.NewId()`.
- **§7 / R-B29:** every security and financial action writes an **append-only** audit row in the **same
  transaction** as the business change; a failed audit write **fails the operation** (auditing is not
  optional). The audit `Detail` is machine text, **never a Persian string**, ≤ 1000 chars.
- **§11/§12:** build 0/0; `PROGRESS.md` updated; commit on `genspark_ai_developer`.

## Goal (this session)
Make every security and financial action produce a durable, append-only audit row that commits or rolls
back atomically with the business change, and connect B02's integrity-mismatch path to an alert sink.

## §Reference pack (verbatim — do not re-read source)

### R1 · Append-only guard + migration/config patterns
`AppendOnlyGuard.ProtectedTypes` is a `HashSet<Type>` literal in
`src/Animora.Persistence.Sqlite/Context/AppendOnlyGuard.cs` (currently: `JournalEntry`, `JournalLine`,
`MedicalHistoryEntry`, `StockMovement`, `Payment`, `ChequeEvent`, `StoreMaintenanceLog`, plus B03a's
`LoginAttemptLog`). Adding `AuditEntry` is a one-line addition — do not restructure the guard.
Local entities are `[LocalOnly]`; `DateTimeOffset` stored as `long` epoch by convention; config pattern
+ `db.Set<T>()` repository shape per earlier phases. Migrations run `001`–`006` today (`006_CapabilitySnapshot`
from B04b); **your migration is `007_AuditDiagnostics`** (AuditEntries only — see the migration note).

### R2 · Dispatcher chain reality (where `AuditBehavior` goes)
`Dispatcher.Chain` order today (`src/Animora.Application/Dispatch/Dispatcher.cs`) after B02/B03/B04:
`Correlation → ExceptionMapping → Validation → Authorization → Entitlement → (CommandJournal if B02) →
Transaction`. `AuditBehavior` must run **inside** the transaction opened by `TransactionBehavior`
(i.e. after it in outer→inner order), immediately **after** `CommandJournalBehavior` if B02 shipped it —
otherwise the "audit row commits/rolls back with the business change" criterion cannot hold. If B02's
actual chain position differs from its own phase file, follow what B02 shipped (`PROGRESS.md` B02 "Repo
state"), not this assumption. `IPipelineBehavior.Wrap<TRequest,TResult>(next, PipelineDescriptor)` is the
behavior contract; `PipelineDescriptor` already carries a permission code field (B04a) — B05a adds the
audit binding the same way (attribute read once at registration, stored on the entry).

### R3 · B02 integrity seam (implement here)
`Application/Auditing/IIntegrityAlertSink.cs` was **declared** in B02 (fingerprint-mismatch path). B05a
**implements** it as `IntegrityAlertSink`: writes an `AuditCategory.Integrity` entry **and** raises a
`NeedsAttention`-class notification through `IUserNotificationPort`. **`IUserNotificationPort` is created
in B05c** — for B05a, take it as an injected dependency (a `TryAdd`-registered no-op is acceptable until
B05c wires the real adapter; record this in `PROGRESS.md`). The B02 fingerprint-mismatch caller is
connected to `IntegrityAlertSink` in this session.

### R4 · UoW enlistment
`IUnitOfWork` (`ExecuteAsync`/`SaveAsync`) opens the transaction; `AuditWriter.Enlist(record)` buffers a
record into the current unit of work so it is written in the **same transaction** as the business change
(the buffer is flushed by the writer within the active `ExecuteAsync` scope). Existing commands from
B02/B03 are the ones carrying `[Auditable]` (see D2).

## §Design decisions (implement — do not re-derive)
- **Audit table** (`[LocalOnly]`, part of migration `007_AuditDiagnostics`):
  `AuditEntry` — `Id` (ULID), `TenantId`, `UserId`, `DeviceId`, `OccurredAtUtc`, `Category`
  (`AuditCategory`), `Action` (string, `<context>.<verb>`), `EntityType`, `EntityId`, `OperationId`,
  `CorrelationId`, `Outcome` (`AuditOutcome`), `BeforeRef`, `AfterRef`, `AmountMinor`, `AmountCurrency`,
  `Detail` (≤ 1000 chars, machine detail, never Persian).
  `enum AuditCategory { Security, Financial, Clinical, Configuration, Integrity, Maintenance }`.
  `enum AuditOutcome { Success, Denied, Failed }`. Added to `AppendOnlyGuard.ProtectedTypes`.
  Indexes: `IX_AuditEntries_OccurredAtUtc`, `IX_AuditEntries_Category_OccurredAtUtc`,
  `IX_AuditEntries_EntityType_EntityId`, `IX_AuditEntries_OperationId`.
  `BeforeRef`/`AfterRef` are **references** (`"<EntityType>:<EntityId>:<Version>"`), never full payload
  copies — the audit log must not become a second database.
- `Application/Auditing/IAuditWriter.cs` — `Task<Result> WriteAsync(AuditRecord record, CancellationToken ct)`
  and `void Enlist(AuditRecord record)` (buffered into the current UoW; written in the same transaction).
  `AuditRecord` is a `sealed record` mirroring the columns.
  `Persistence.Sqlite/Auditing/AuditWriter.cs` implements both.
- `Application/Auditing/AuditableAttribute.cs` — `[Auditable(AuditCategory.Financial, "sales.invoice.post")]`
  on a command record; read at **registration** (like B04a's permission attribute) and stored on the
  entry.
  `Application/Behaviors/AuditBehavior.cs` — placed immediately **after** `CommandJournalBehavior`
  (R2); enlists a `Success` record on a successful `Result`, a `Denied` record when the authorization/
  entitlement behavior short-circuited, and a `Failed` record otherwise. It never throws and never
  blocks, but a **failed audit write fails the whole transaction** (§7).
- `Application/Auditing/IIntegrityAlertSink.cs` (declared B02) → `Persistence.Sqlite/Auditing/IntegrityAlertSink.cs`
  writes an `AuditCategory.Integrity` entry **and** raises a `NeedsAttention` notification via
  `IUserNotificationPort` (R3).
- **Attribute application (existing commands):** apply `[Auditable]` to the B02/B03 commands —
  `SignInCommand` → `Security/identity.signin`, `ChangePasswordCommand` → `Security/identity.password.change`,
  `CompleteFirstRunSetupCommand` → `Configuration/identity.firstrun`.
- **New error code (this session):** `AUDIT_WRITE_FAILED`. (The other two B05 codes,
  `DIAGNOSTICS_BUNDLE_FAILED` / `CRASH_RECOVERY_REQUIRED`, are B05b/B05c.)

## Files to create / modify
| Path | Action | Contents |
|---|---|---|
| `src/Animora.Persistence.Sqlite/Entities/AuditEntry.cs` | create | append-only audit row |
| `src/Animora.Persistence.Sqlite/Configurations/Diagnostics/AuditEntryConfiguration.cs` | create | mapping + 4 indexes |
| `src/Animora.Persistence.Sqlite/Context/AppendOnlyGuard.cs` | modify | add `AuditEntry` |
| `src/Animora.Persistence.Sqlite/Migrations/*007_AuditDiagnostics*` | create | via `dotnet-ef` (step 2) — AuditEntries only + snapshot |
| `src/Animora.Application/Auditing/IAuditWriter.cs` | create | audit port + `AuditRecord` |
| `src/Animora.Application/Auditing/AuditableAttribute.cs` | create | registration-time audit binding |
| `src/Animora.Application/Auditing/IIntegrityAlertSink.cs` | modify | documented contract (declared B02) |
| `src/Animora.Application/Behaviors/AuditBehavior.cs` | create | enlist success/denied/failed records |
| `src/Animora.Persistence.Sqlite/Auditing/AuditWriter.cs` | create | in-transaction audit writer |
| `src/Animora.Persistence.Sqlite/Auditing/IntegrityAlertSink.cs` | create | integrity audit + notification |
| `src/Animora.Application/Dispatch/HandlerRegistry.cs` | modify | store the audit binding on the entry (like B04a's code field) |
| `src/Animora.Application/ApplicationServiceCollectionExtensions.cs` | modify | register `AuditBehavior` in the chain; apply `[Auditable]` |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append `AUDIT_WRITE_FAILED` |
| `backend-roadmap/PROGRESS.md` | modify | mark B05a done, next → B05b, repo-state entry, deviations (incl. the 007/008 migration split) |

## §Implementation steps
1. Add `AuditEntry` + configuration + `AppendOnlyGuard` entry.
2. Generate the migration (AuditEntries only):
   ```bash
   cd /home/user/webapp && .sandbox/tools/dotnet-ef migrations add 007_AuditDiagnostics \
     --project src/Animora.Persistence.Sqlite --startup-project src/Animora.Persistence.Sqlite \
     --context AnimoraLocalDbContext
   ```
   Verify with `dotnet-ef migrations list … --context AnimoraLocalDbContext` (shows `007`). If the tool
   is absent, run `scripts/sandbox/install-prereqs.sh` once, then retry.
3. Add `IAuditWriter`/`AuditRecord` + `AuditWriter` (enlist into the current UoW).
4. Add `AuditableAttribute`, read it at registration (extend the entry like B04a), implement
   `AuditBehavior` right after `CommandJournalBehavior` (R2). Apply `[Auditable]` to the three commands
   (D2's list).
5. Implement `IntegrityAlertSink` and connect B02's fingerprint-mismatch path to it.
6. Append `AUDIT_WRITE_FAILED`.
7. **Gate:** `scripts/sandbox/build.sh full` = 0/0.
8. `PROGRESS.md` update + commit on `genspark_ai_developer`.

## Contracts / schema added
- Local table `AuditEntries` (migration `007_AuditDiagnostics`, AuditEntries only).
- Application: `IAuditWriter`/`AuditRecord`, `AuditableAttribute`, `AuditBehavior`; the audit binding on
  the pipeline entry.
- Persistence: `AuditWriter`, `IntegrityAlertSink`. `ErrorCodes` += 1 (`AUDIT_WRITE_FAILED`).
  No mock deleted this session.

## Rules that apply here
- **CONVENTIONS §7 / R-B29** — append-only audit row in the same transaction; a failed audit write fails
  the operation.
- **A-RULE 026 / R-B09** — audit references (not payload copies) so the log is not a second database.

## Out of scope (sibling sessions)
- Logging (Serilog), correlation propagation, diagnostics bundle → **B05b**.
- Crash safety (`GlobalExceptionHandler`/`CrashRecoveryService`), `IUserNotificationPort` +
  `ToastNotificationAdapter` + Persian text, `DraftStateStore` (+ `UiDraftState` migration `008`),
  `MaintenanceScheduler`, Desktop wiring, all tests + `run.sh` gate → **B05c**.

## Acceptance criteria
- `scripts/sandbox/build.sh full` = 0/0.
- `dotnet-ef migrations list … --context AnimoraLocalDbContext` shows `007_AuditDiagnostics`.
- `AuditEntry` appears in `AppendOnlyGuard.ProtectedTypes`; `AUDIT_WRITE_FAILED` is in `ErrorCodes.cs`.
- `AuditBehavior` is registered in the chain after `CommandJournalBehavior` (grep the chain builder), and
  `[Auditable]` is present on `SignInCommand`/`ChangePasswordCommand`/`CompleteFirstRunSetupCommand`.

## PROGRESS.md update
Mark B05a done; advance "Next phase" to `B05b`; append a "Repo state" entry listing: `AuditEntries` +
migration `007_AuditDiagnostics` (AuditEntries only), `IAuditWriter`/`AuditWriter`, `AuditableAttribute`
+ its registration-time resolution + `AuditBehavior` position, `IntegrityAlertSink` and the B02 hookup,
the audit-attribute usage on the three commands, `ErrorCodes` += 1, and the **007/008 migration-split
deviation** with its justification.
