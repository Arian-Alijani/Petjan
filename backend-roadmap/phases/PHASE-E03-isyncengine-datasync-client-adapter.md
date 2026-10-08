# Phase E03 — ISyncEngine + custom immutable-batch HTTP adapter

**Band:** M · **Track:** E · **Depends on:** D08 · **Satisfies:** R-B03, R-B13, R-B31
**Runnable after this phase:** desktop offline yes · server required no (NullSyncEngine when absent)

> Filename retained for roadmap continuity. Launch uses Animora.Sync.Custom, NOT a Datasync client
> queue/timestamp cursor. No client PostgreSQL driver or server project reference is permitted.

## Read before starting
- `ARCHITECTURE.md` §8, §16-CT–§18-CT, §48–§50, §PG; `STACK.md` §4/§6/§11/§12.
- `backend-roadmap/CONVENTIONS.md` §1–§9/§11; PROGRESS current state + D08/A11/C11 entries.
- `SyncContractCatalog`, `WireContract`, D08 manifest/endpoints/budget/error/codec contracts.
- Local DbContext/configurations/migrations, single-writer/startup/key interceptors and pending journal.
- `A-RULE 001`, `A-RULE 009`, `A-RULE 010`, `A-RULE 022`, `T-RULE 001`, `T-RULE 006`, `T-RULE 007`.

## Goal
Project-owned ISyncEngine / IStateSyncEngine port has no infrastructure types. Custom HTTP adapter
pulls immutable batches into encrypted SQLite staging, atomically updates authoritative base and
checkpoint while preserving pending overlay, and supports nondestructive shadow resync. Business
command settlement remains E06, not generic state push.

## Design decisions (binding)
- `Animora.Sync.Abstractions` stays package-free: ISyncEngine.SynchronizeAsync / FullResyncAsync,
  SyncScope (direction/entity scope/catchup), SyncResult, SyncConflict, ISyncDiagnostics and
  ISyncCredentialProvider. Provide NullSyncEngine returning Offline. Public methods take ct.
  IStateSyncEngine is the Application-owned state port; ISyncEngine is the outer client facade;
  facade delegates state operations to it, never a second competing replication algorithm.
- `Animora.Sync.Custom` references Abstractions/Application/Persistence.Sqlite/Contracts.Wire only.
  It uses BCL HttpClient/JSON and existing EF Core SQLite 10 pins. No CommunityToolkit.Datasync
  package, OfflineDbContext, library op queue or delta-token table. No Npgsql/server persistence.
  App composition root conditionally AddAnimoraCustomSync when ServerBaseUrl exists; no config
  retains NullSyncEngine and C11 status service until E04. UI Contracts are unchanged.
- Local replica tables remain separate `repl_*` transport rows, with GenerationId. A normal EF
  `AnimoraReplicaDbContext` maps only catalog contracts plus invisible staged manifests/pages,
  active/shadow generation and completed-batch markers. Its own migration sequence/history starts
  `001_ReplicaTables`. C11 SyncCursor is extended by a SEPARATE next-free local-context migration
  (derive physical ordinal; never edit prior migrations). SQLite types TEXT/INTEGER only.
- Both contexts share the EXACT same cipher/key/connection interceptor and single-writer scheduler;
  startup migrates local→replica→projections deterministically. Crash/key/wrong-key/plain-reader and
  two-context ownership tests required. If atomic business projection + checkpoint spans contexts,
  share one SqliteConnection + transaction via UseTransaction; never TransactionScope/independent
  commits. Context state is cleared after failure. No context touches another context's migration
  history/table set except reviewed shared-transaction projection ports.
- Authoritative checkpoint key = `(TenantId,DeviceId,ScopeId,ProfileVersion,ContractVersion,
  ServerFeedEpoch,TenantFeedGeneration,ActiveGenerationId)`. CompletedVersion is an opaque lossless
  decimal TEXT string; LastPulledAtUtc/error counters are diagnostics, not timestamp tokens.
  Existing LastSyncedAt is display-only. Per-entity counters are diagnostic mirrors, NOT independently
  committed critical cursors. Scope/catalog changes invalidate profile and require shadow rebuild.
- Pass: credential/contract/scope preflight; push guarded nonfinancial state mutations from durable
  mutation journal (only Owner/Pet/WeightEntry) in stable order with unchanged MutationId/fingerprint;
  request delta from completed checkpoint; download/hash-check every bounded page into invisible
  SQLite staging idempotently `(BatchId,PageOrdinal)`; verify manifest/counts/contracts/dependencies;
  acquire local writer and exact compare-and-swap FromVersion/generation; apply base+overlay+
  business projections+completion+ToVersion in ONE transaction; then emit UI invalidation.
  Revalidate local user/session/generation after download before apply, so late old-user responses
  cannot write after logout/profile switch. Receipts never advance the state checkpoint.
- Upserts follow manifest parents-before-children, removals reverse it. Whole apply is bounded by
  D08 export budget; 200-row SaveChanges chunks are flushes inside that transaction, never earlier
  visible commits. Projection failure rolls back all authoritative changes/checkpoint; retry from
  durable staging/old checkpoint. Pending command/state intent is independent overlay: update
  base, rebase if proven safe or preserve/quarantine conflict, never skip the authoritative base
  and pretend it completed. Projector emits no fresh business command/local outbox echo.
- Per-entity failure cannot partially advance a critical scope. SyncOutcome.Completed only after
  atomic apply; PartiallyCompleted may describe download/mutation progress with unchanged pull
  checkpoint, never visible partial authoritative data. Duplicate completed BatchId = no-op;
  stale/out-of-order FromVersion re-requested, never numeric MAX. Expired/corrupt batch clears only
  incomplete staging, not the active base or mutation/command journal.
- No automatic field merge in E03. Domain/authorization decides E05 conflict policy. Medical
  preserved copies stay local-only; never push IsConflictPreservedCopy. Finance read projections
  come from server for cross-device totals, not recomputed from this device's command receipts.
- FullResyncAsync preserves unsent business/state journal, drafts and conflict inbox; audit reason,
  fetch full snapshot into new encrypted shadow, validate and rebase under policy, atomic active
  pointer+checkpoint switch. Cannot safely rebase ⇒ NeedsAttention and keep OLD active generation;
  never delete it or pending operations. Failure/kill midway leaves pending work readable and
  recoverable. Retired generations GC after supported rollback window only.
- SyncOptions: nullable ServerBaseUrl, page=50/cap200, HTTP timeout30s, staged byte/item/apply budgets
  from D08, MaxConcurrentPasses=1, ProjectionBatchSize=200; derive entity set from catalog, no 25
  literal limit. Insecure HTTP dev-only loopback; validate URI/bytes/deadlines. Inject clocks and
  typed HttpClient with bearer + contract headers; verify served version, authority and bindings.
- Version gate: absent server ⇒ Offline/no writes; 401/403 ⇒ Rejected; unsupported low/high contract
  maps upgrade actions; mismatched echo ⇒ Rejected+audit. Credential provider default AUTH_REQUIRED;
  E07 supplies real refresh. Library awaits ConfigureAwait(false), cancellation passed throughout.

## Files to create / modify
| Path | Action | Purpose |
|---|---|---|
| `src/Animora.Sync.Abstractions/*` | create | package-free ports/results/scope/diagnostics/credentials/default offline |
| `src/Animora.Application/Ports/IStateSyncEngine.cs` | create if absent | Application state port, no HTTP/EF/provider types |
| `src/Animora.Sync.Custom/Animora.Sync.Custom.csproj` | create | custom client-only dependency boundary |
| `src/Animora.Sync.Custom/Store/*` | create | replica/staging/generation/completion maps and writer ports |
| `src/Animora.Sync.Custom/Migrations/*` | create | replica context own baseline/history |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | next-free local checkpoint extension migration |
| `src/Animora.Sync.Custom/Projection/*` | create | explicit mappings, atomic base/overlay/projection |
| `src/Animora.Sync.Custom/Transport/*` | create | batch/mutation/preflight HTTP, bearer/contract handlers |
| `src/Animora.Sync.Custom/CustomSyncEngine.cs` / `CustomSyncDiagnostics.cs` | create | facade/algorithm/status |
| `src/Animora.Sync.Custom/SyncOptions.cs` / `DependencyInjection/*` | create | bounded options and AddAnimoraCustomSync |
| `Animora.sln` / `src/Animora.Desktop/App.axaml.cs` | modify | project + conditional composition root |
| `tests/Animora.Sync.Tests/*` | create | port/migration/encryption/batch/CAS/atomicity/overlay/resync tests |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | extend | Custom imports no Npgsql/server/Datasync types |

## Implementation steps
1. Define package-free facade/state ports and offline defaults; reuse D08 wire/catalog contracts.
2. Add Custom project, EF replica context and separate migration histories; exact shared cipher.
3. Extend scoped checkpoint/generation and implement invisible idempotent page staging.
4. Implement bounded HTTP download/contract/mutation receipts and exact FromVersion arbitration.
5. Implement one-transaction ordered projection/base/overlay/completion/checkpoint update and
   cancellation/logout guards; no per-entity/chunk visible commits.
6. Implement shadow full snapshot + preserve/rebase + atomic switch, failure/recovery and audit.
7. Add composition-root registration and diagnostics without changing UI service seam.
8. Test port purity, wrong-key/plain-reader, migration ownership, crash/partial/duplicate/late page,
   same-version/tenant/scope, unknown contract, hash/TTL, pending overlay and shadow failure.
9. Real PostgreSQL server integration green (absence blocks DB CI); build 0/0, test green, no-server
   offline smoke. Record counts from catalog, no magic replicated-entity number.

## Acceptance criteria
- No EF/HTTP/Npgsql/Datasync type in public ports; Custom has no server reference; SQLite stays local.
- Replica/local migrations only change their owned tables/history, shared cipher remains enforced
  through every connection/upgrade/cancellation; PRAGMA cipher_version + wrong-key/plain reader.
- Incomplete/missing/corrupt/expired/out-of-order batch leaves base/projection/checkpoint unchanged;
  duplicate pages/completed batches harmless. Crash before/after atomic commit is deterministically
  replay/no-op; 200-row flush failure cannot expose previously flushed partial rows.
- Complete multi-table batch preserves foreign keys and pending intent; authoritative base advances
  independently of overlay conflict, no projection echo. Financial read projections agree on two
  devices after complete sync, catalog/role read restrictions honored.
- Full shadow resync with pending commands survives crash and never clears intent/active base;
  invalid relationships/rebase failure block switch with attention; epoch catch-up can't fake continuity.
- Preflight/header/credentials/out-of-window/no-server responses yield controlled outcomes; local
  identity switch rejects late batch. Decimal-string sequence and UUID Version round-trip losslessly.
- Record real-server evidence + build/tests + offline smoke. No claimed acceptance for missing DB.

## Out of scope
Loop/UI scheduling → E04; conflict resolution → E05; command transport → E06; online auth → E07;
full PostgreSQL chaos/load/restore gates → E08/E09. Optional Datasync is deferred, not an E03 dependency.

## PROGRESS.md update
Only after implemented acceptance mark E03 done and advance E04. Record custom adapter, manifest,
shared-cipher contexts/history, scope checkpoint, atomic projection/base+overlay, staging CAS,
shadow resync, compatible wire decisions and tests. No Datasync pin/table/token claim.

## Version seam gate
Read ARCHITECTURE §PG.7: existing Domain Version is string?, existing V1 command expected revision
is long?. D05 versions ExpectedServerVersion as V2; canonical UUID codec/converter, captured
server-base evidence and V1 pending-journal compatibility must pass before shipping this phase.
