# Phase G04 — Conflict resolution & non-destructive pull (base/overlay apply, reversal-only posted conflicts, shadow-generation resync)

**Band:** M · **Track:** G (sync / integration / release) · **Depends on:** G03 · **Next:** G05
**Satisfies:** R-B03, R-B17, R-B18 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; regenerates scope-inventory row E05 (Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

> Baseline verified 2026-10-09: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161 (+ codes added by earlier S/G phases, incl. G02's 4 Sync-transport codes), `WireContract.Version` = 2 after S06; `src/` projects include `Animora.Sync.Abstractions` (G01), `Animora.Sync.Custom` (G02) (+ the server-track `S01–S20` projects).
> **Next-free-ordinal rule.** This phase **adds one client SQLite migration** for the replica/staging store. Do **not** hard-code a number: list `src/Animora.Persistence.Sqlite/Migrations/` physical files and take the next free ordinal on the owning `DbContext` (client head is `031` today → next free is `032`, but **verify by listing**, matching the `NNN` token, not the double-underscore prefix). Server sequence (101+) is untouched by this client phase.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. Every existing type, signature, path and command you need is in
> **§Reference pack**; every new name is fixed in **§Design decisions**. Do **not** re-open the repo to "understand" it and
> do not re-derive decisions. If — and only if — a compile error proves a snippet diverges from the tree, fix the smallest
> thing and log it in the `PROGRESS.md` deviation log. Names here are the real ones; there is no alias to resolve.
> Implement exactly this scope: nothing from a later phase, nothing "while we're here".

**Environment (do not probe):**
- Build / test / run only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]` (0 warnings / 0 errors), `scripts/sandbox/test.sh [filter]`,
  `scripts/sandbox/run.sh` (desktop smoke → `SMOKE RUN OK`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent).
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — use it for the migration; **never** `dotnet tool install`.
- **Forbidden tech (restated, binding):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11,
  per-tenant database, hard-coded TLS version, Native AOT with EF Core. **No new package** — EF Core Sqlite 10.0.12 is already pinned; the replica store reuses the same SQLCipher-encrypted database file / connection factory.

**Offline invariant.** G04 never opens a socket (download is G02; this phase is the **apply** side). With no server the `BufferingSyncApplyStore` default (G02) stays in force and nothing in this phase runs. The three-layer store (base / pending overlay / UI-read = base+overlay) means an inbound pull **never** overwrites unsent local intent, and a full resync preserves the pending overlay before an atomic switch — the desktop keeps full offline functionality and never loses provisional state (Standing principle 1, R-B18, ARCH §8.2).

**Condensed CONVENTIONS rules used here:** §2 layering (apply store lives in `Animora.Persistence.Sqlite` + the orchestration seam in `Animora.Sync.Custom`; **no** `System.Net.Http` in the apply store, **no** server project ref); §4 C# style (`sealed`, nullable honored, `CancellationToken ct = default` + `ConfigureAwait(false)`, no `.Result`/`.Wait()`, expected failures as `Result`, time via `IClock`, ids ULID); §5 EF (EF Core 10; `AsNoTracking` reads; one connection+transaction for atomic base+checkpoint; `UseTransaction` across contexts, never `TransactionScope`; migrations additive); §6 two pipelines (state apply only — financial conflicts route to the command pipeline, never overwritten in place); §11 offline-first.

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §8.2 (non-destructive pull, three layers, atomic batch apply, shadow-generation resync), §8.5 (conflict policy by data class, reversal-only posted records), §16-CT (checkpoint discipline / A-RULE 022), §17-CT (shadow generation / atomic switch), §18-CT (contract versioning) — only these `§`.
- `backend-roadmap/phases/PHASE-G02-immutable-batch-http-adapter.md` §Design decisions (the `ISyncApplyStore`/`ISyncApplySession`/`SyncCheckpoint` seam this phase implements; the `BufferingSyncApplyStore` it replaces) and `PHASE-G03-sync-orchestration-status.md` (the `ISyncResyncHandler` seam this phase implements).
- `src/Animora.Persistence.Sqlite/Entities/SyncCursor.cs` + `Commands/SyncCursorStore.cs` (the C11 cursor this phase coexists with — the checkpoint is a **separate** `(Epoch, Watermark)` record, not the human `LastSyncedAtUtc`).
- (Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Make the pull **non-destructive and conflict-correct** on the client: implement the real `ISyncApplyStore` (G02 seam) backed by a three-layer local store — authoritative **base** (last settled server state), a **pending overlay** (local provisional commands not yet settled), and UI reads = base-with-overlay — so an inbound batch replaces base **without touching the overlay** and advances base + checkpoint **atomically** on one SQLite connection/transaction (never a torn apply). Add the **conflict policy engine** that detects a version mismatch generically and dispatches the decision by data class (UI-prefs LWW only; **posted invoice/payment conflicts are reversal-only**, never an in-place overwrite; ledger append-only; inventory additive; appointment-slot rejected; demographics field-merge). Implement the real `ISyncResyncHandler`: build an encrypted **shadow generation** from a full snapshot (`POST /sync/snapshots`, G02 client), validate it while preserving the pending overlay, then **atomically switch** base/checkpoint to it. Replaces the G02 buffering default and the G03 null resync handler; both stay documented as the offline fallbacks.

---

## §Reference pack (verbatim from the tree + sibling phases, 2026-10-09)

```csharp
// Animora.Sync.Custom (G02) — the seams this phase fills; reuse, never redefine
public interface ISyncApplyStore
{
    Task<Result<SyncCheckpoint>> ReadCheckpointAsync(SyncScope scope, CancellationToken ct = default);
    Task<Result<ISyncApplySession>> BeginAsync(SyncScope scope, BatchReady batch, CancellationToken ct = default);
}
public interface ISyncApplySession : IAsyncDisposable
{
    Task<Result> AcceptPageAsync(SyncPage page, CancellationToken ct = default);
    Task<Result<SyncCheckpoint>> CommitAsync(CancellationToken ct = default);
    Task<Result> AbortAsync(CancellationToken ct = default);
}
public readonly record struct SyncCheckpoint(long Epoch, long Watermark);   // (0,0) = never applied
public sealed class BufferingSyncApplyStore : ISyncApplyStore { /* G02 in-memory default — REPLACED here */ }

// Animora.Sync.Custom — full-resync feed call (G02): POST /sync/snapshots -> OpenBatchOutcome.Ready(BatchReady, IsFullSnapshot=true, FromSequenceExclusive=0)
public sealed class SyncFeedClient { Task<Result<OpenBatchOutcome>> OpenSnapshotAsync(long epoch, CancellationToken ct = default); /* + GetPageAsync */ }

// Animora.Client.Services.Sync (G03) — the seam this phase fills; reuse, never redefine
public interface ISyncResyncHandler { Task<Result> HandleAsync(SyncScope scope, string reason, CancellationToken ct = default); }
public sealed class NullSyncResyncHandler : ISyncResyncHandler { /* G03 no-op default — REPLACED here */ }

// Animora.Contracts.Wire.Sync (S12) — the record shape being applied
public sealed record SyncRecord(string Entity, string EntityId, char Op /* 'U'|'D' */, string? Version, DateTimeOffset? UpdatedAt, bool Deleted, System.Text.Json.JsonElement? Payload);
public sealed record BatchReady(string BatchId, long Epoch, long Generation, long FromSequenceExclusive, long Watermark, int PageCount, string ContentHash);

// Animora.Persistence.Sqlite.Entities.SyncCursor (C11) — coexists; this phase does NOT change it
[LocalOnly] public sealed class SyncCursor { public const string DefaultScope = "default"; public string Scope { get; } public DateTimeOffset? LastSyncedAtUtc { get; } public void MarkSynced(DateTimeOffset whenUtc); }

// Animora.Domain.Results / Time (A02): Result/Result<T>/ErrorCode/ErrorCodes ; IClock.UtcNow. ULID ids via IdGenerator.
```

**ARCH §8.5 conflict matrix (verbatim, the policy this engine encodes):**

| Data class | Resolution |
|---|---|
| UI preferences / display settings | generic Last-Write-Wins (the **only** LWW place) |
| Posted invoice / payment | **immutable — reversal / credit note / compensating entry; never in-place edit** |
| Ledger & audit | append-only forever; a correction is a new balanced reversing entry (cannot conflict) |
| Inventory | movement-based; two concurrent movements both apply (additive), on-hand re-derived |
| Appointment slot | domain validation; a colliding slot is **rejected** (`APPOINTMENT_SLOT_CONFLICT`), not merged |
| Owner/pet demographics | field-level merge through the single update funnel, never whole-row LWW |

## §Design decisions

- **Replica store / three layers (`AnimoraReplicaDbContext`, `Animora.Persistence.Sqlite/Replica/`)** — the staging + base generation live in the **same encrypted SQLite file** (one SQLCipher connection, ARCH §8.2) under a `repl_*` table prefix so they never collide with the business tables. Entities: `ReplicaBase` (the authoritative last-settled server rows, one table generically keyed `(Entity, EntityId)` with the opaque `Version` + JSON payload + `Deleted` flag — a generic projection, **not** 25 physical tables, since the apply is catalog-generic), `ReplicaStaging` (download-in-progress rows for the open `BatchId`), and `ReplicaCheckpoint(Scope, Epoch, Watermark, Generation)`. The **pending overlay** is the already-existing local command journal (`PendingCommandRecord`, B02) — G04 does not duplicate it; "UI reads base-with-overlay" is realized by the existing read services preferring an unsettled local command's effect over base (this phase wires the read-preference only where a replicated entity is also client-writable — the 3 writable replicable shapes; it does not rewrite every query).
- **Migration** — `.sandbox/tools/dotnet-ef migrations add ReplicaStore --project src/Animora.Persistence.Sqlite --context AnimoraDbContext --output-dir Migrations` (the client context), rename to the next free client ordinal (verify by listing; `032` expected), creating `repl_base`, `repl_staging`, `repl_checkpoint`. All `[LocalOnly]`.
- **`SqliteSyncApplyStore : ISyncApplyStore`** (replaces `BufferingSyncApplyStore`): `ReadCheckpointAsync` reads `repl_checkpoint` (returns `(0,0)` if absent). `BeginAsync` opens a session over **one** connection + transaction; `AcceptPageAsync` upserts verified `SyncRecord`s into `repl_staging` (bounded — never holds the whole batch in memory; streams per page). `CommitAsync` is the **atomic swap**: in the same transaction, apply staging onto `repl_base` (upsert `U`, tombstone `D`), advance `repl_checkpoint` to `(batch.Epoch, batch.Watermark, batch.Generation)`, clear staging — then COMMIT. A crash before COMMIT leaves base + checkpoint unchanged (A-RULE 022: a partial page can never advance the checkpoint). `AbortAsync` rolls back staging; checkpoint unchanged.
- **Conflict detection + policy engine (`ConflictResolver`, `Animora.Application/Sync/`)** — detection is generic: when applying a base row whose `(Entity, EntityId)` has an **unsettled pending command** in the overlay AND the inbound `Version` differs from the version the command was based on, it is a **conflict**; the resolver dispatches by `ConflictClass` (an enum mapped from the entity via a `ConflictPolicyCatalog` table built from §8.5 — the single source). Outcomes: `UiPreference` ⇒ LWW (newer `UpdatedAt` wins); `PostedFinancial` (Invoice/Payment — **never in the replicated set**, so this path only guards against a pending local post against a server-changed base) ⇒ **reversal-only**: the local post is marked `NeedsAttention` with `FinancialEffect.Unknown` and the correction must be a new reversing command (A-RULE 002/003) — the engine **never** overwrites or deletes the posted record; `AppendOnlyLedger` ⇒ cannot conflict (base applied, overlay untouched); `InventoryMovement` ⇒ both apply (additive), on-hand re-derived; `AppointmentSlot` ⇒ the colliding local intent is rejected with `APPOINTMENT_SLOT_CONFLICT`; `Demographics` ⇒ field-level merge through the existing update funnel (base fields win for fields the local command didn't touch; the command's fields stay pending). The resolver returns a `ConflictDecision` list the apply session records; it **never** mutates money/stock in place — financial corrections route back through the command pipeline (two-pipeline rule, §8.1).
- **`ShadowResyncHandler : ISyncResyncHandler`** (replaces `NullSyncResyncHandler`): on `ResyncRequired` it (1) calls `SyncFeedClient.OpenSnapshotAsync` (G02) to open a full-snapshot batch; (2) downloads + verifies all pages into a **fresh encrypted shadow generation** (`repl_base` rows under a new `Generation` id, in staging-like isolation) while the current base + the pending overlay stay live and readable; (3) validates the shadow (row counts match the batch manifest, content hash verified, projection order satisfied); (4) **atomically switches** `repl_checkpoint` + the active base generation to the shadow in one transaction — pending overlay preserved, active state never cleared before the replacement succeeds (ARCH §8.2 / §17-CT, CONVENTIONS §6 item 15). A failure at any step discards the shadow and leaves the live base/overlay/checkpoint untouched (returns `Result.Fail`).
- **New ErrorCodes (+3, region "Sync apply/conflict"):** `SyncApplyConflict = new("SYNC_APPLY_CONFLICT", ErrorCode.Conflict)`, `SyncShadowValidationFailed = new("SYNC_SHADOW_VALIDATION_FAILED", ErrorCode.Integrity)`, `AppointmentSlotConflict = new("APPOINTMENT_SLOT_CONFLICT", ErrorCode.Conflict)` — unless `AppointmentSlotConflict` already exists (`grep ErrorCodes.cs`; it is referenced in ARCH §8.5): if present, **reuse** it (one-introducer) and add only the first two.
- **DI** — in `Animora.Sync.Custom`'s `AddAnimoraHttpSyncEngine` extension, `services.Replace(ServiceDescriptor.Singleton<ISyncApplyStore, SqliteSyncApplyStore>())` and register `ConflictResolver`; in the G03 `AddAnimoraSyncOrchestration`, `services.Replace(ServiceDescriptor.Singleton<ISyncResyncHandler, ShadowResyncHandler>())`. These replacements are **conditional on a configured endpoint** (same guard G02 uses) — unconfigured desktops keep the no-op defaults and never touch the replica store. Register `AnimoraReplicaDbContext` on the same connection factory as `AnimoraDbContext`.
- **Not in this phase:** command transport / settlement reconciliation that resolves the pending overlay against the server (**G05** — it reads the overlay this phase preserves but does not change G04's apply); online auth/entitlement on the HTTP client (**G06**); the server batch export / snapshot build (**S12**). No new wire DTO (reuses S12).

## §Implementation plan

1. `src/Animora.Persistence.Sqlite/Replica/`: `AnimoraReplicaDbContext.cs`, `ReplicaBase.cs`, `ReplicaStaging.cs`, `ReplicaCheckpoint.cs` (+ `Configurations/Replica/*`), all `[LocalOnly]`. Register on the shared connection factory.
2. Migration `ReplicaStore` via `dotnet-ef` (next free client ordinal; verify by listing). Update any `ExpectedHead`/schema-version assertion the client uses.
3. `src/Animora.Persistence.Sqlite/Replica/SqliteSyncApplyStore.cs` — the real `ISyncApplyStore` (one connection+transaction atomic apply, streamed pages, A-RULE 022).
4. `src/Animora.Application/Sync/`: `ConflictClass.cs` (enum), `ConflictPolicyCatalog.cs` (entity→class from §8.5, single source), `ConflictResolver.cs` (+ `ConflictDecision`).
5. `src/Animora.Sync.Custom/ShadowResyncHandler.cs` — full-snapshot shadow build + validate + atomic switch.
6. `src/Animora.Domain/Results/ErrorCodes.cs`: +2/3 codes, new `#region Sync apply/conflict` (reuse `AppointmentSlotConflict` if already present).
7. DI: `Replace` the G02/G03 defaults conditionally in the existing extension methods; no new call site in `App.axaml.cs` (the replacements ride the already-registered `AddAnimoraHttpSyncEngine`/`AddAnimoraSyncOrchestration`).
8. Tests in `tests/Animora.Sync.Tests/` + `tests/Animora.Persistence.Tests/`: see Acceptance. PG not required (client SQLite); the replica store tests run on an in-memory/encrypted SQLite fixture.
9. `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Sync` + `scripts/sandbox/test.sh Persistence` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
10. `PROGRESS.md` per recipe; commit via `scripts/git/push.sh`.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Persistence.Sqlite/Replica/AnimoraReplicaDbContext.cs` + entities + `Configurations/Replica/*` (new) | `repl_base`/`repl_staging`/`repl_checkpoint`, `[LocalOnly]` |
| `src/Animora.Persistence.Sqlite/Migrations/*ReplicaStore*` (new) | next free client ordinal |
| `src/Animora.Persistence.Sqlite/Replica/SqliteSyncApplyStore.cs` (new) | atomic base+checkpoint apply (replaces `BufferingSyncApplyStore`) |
| `src/Animora.Application/Sync/{ConflictClass,ConflictPolicyCatalog,ConflictResolver}.cs` (new) | §8.5 policy engine; reversal-only posted |
| `src/Animora.Sync.Custom/ShadowResyncHandler.cs` (new) | shadow-generation full resync + atomic switch (replaces `NullSyncResyncHandler`) |
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit) | +2/3 codes, new region |
| `src/Animora.Sync.Custom/SyncCustomServiceCollectionExtensions.cs`, `src/Animora.Client.Services/Sync/SyncOrchestrationServiceCollectionExtensions.cs` (edit) | conditional `Replace` of the two defaults |
| `tests/Animora.Sync.Tests/*.cs`, `tests/Animora.Persistence.Tests/*.cs` (new) | apply + conflict + resync + layering tests |

## Contracts / schema added

Client tables `repl_base`, `repl_staging`, `repl_checkpoint` (all `[LocalOnly]`, one new migration). New types: `AnimoraReplicaDbContext`, `SqliteSyncApplyStore`, `ConflictClass`, `ConflictPolicyCatalog`, `ConflictResolver`, `ConflictDecision`, `ShadowResyncHandler`. ErrorCodes +2/3. No wire DTO (reuses S12). `SyncCursor` unchanged.

## Rules that apply here

- **Non-destructive pull (R-B18, ARCH §8.2, A-RULE 017):** a pull replaces **base** only; the pending overlay (local command journal) is never touched; UI reads base-with-overlay. A resync builds a shadow generation preserving the overlay and switches atomically; active state is never cleared before the replacement succeeds.
- **Atomic apply (A-RULE 022, ARCH §16-CT):** base + checkpoint advance together on **one** SQLite connection + transaction (`UseTransaction` across the business + replica contexts if both participate), never two independent commits, never `TransactionScope`; a partial page can never advance the checkpoint.
- **Conflict policy (R-B17, ARCH §8.5):** detect generically on version mismatch; decide by data class; **posted invoice/payment conflicts are reversal-only** — never an in-place overwrite or delete; money/stock corrections route through the command pipeline (two pipelines, §8.1); LWW only for UI preferences.
- **Layering / C# style / EF (§2/§4/§5):** apply store in `Animora.Persistence.Sqlite` (no HTTP, no server ref); resolver in `Animora.Application`; `sealed`, `Result`, `IClock`, ULID; EF Core 10, `AsNoTracking` reads, additive migration.
- **Offline-first (§11):** unconfigured ⇒ G02/G03 no-op defaults stay; replica store untouched; desktop fully offline.

## Out of scope (owning phase)

- Command transport + settlement reconciliation of the preserved overlay against the server → **G05** (reads the overlay; does not change G04's apply/conflict).
- Online auth / entitlement / device + bearer token on the HTTP client → **G06**.
- Server-side batch/snapshot build, epoch/generation advance → **S11/S12** (reused).
- Validation gates (PG CI / RLS / restore) and perf proof → **G07/G08**. Release → **G09**.
- Optional Datasync adapter + its whole-row LWW resolvers (usable only for UI-prefs class) → deferred (ROADMAP §5, STACK G1 gate).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; desktop starts unconfigured with the G02/G03 no-op defaults untouched.
2. `scripts/sandbox/test.sh Sync` + `scripts/sandbox/test.sh Persistence` green with: `SqliteSyncApplyStoreTests.ApplyBatch_AdvancesBaseAndCheckpoint_Atomically`, `SqliteSyncApplyStoreTests.CrashBeforeCommit_LeavesBaseAndCheckpointUnchanged`, `SqliteSyncApplyStoreTests.PartialPage_NeverAdvancesCheckpoint`, `SqliteSyncApplyStoreTests.Pull_DoesNotTouchPendingOverlay`, `ConflictResolverTests.UiPreference_LastWriteWins`, `ConflictResolverTests.PostedFinancial_IsReversalOnly_NeverInPlace`, `ConflictResolverTests.InventoryMovements_BothApply_Additive`, `ConflictResolverTests.AppointmentSlotCollision_Rejected`, `ConflictResolverTests.Demographics_FieldLevelMerge_NotWholeRowLww`, `ConflictResolverTests.LedgerAppendOnly_CannotConflict`, `ShadowResyncHandlerTests.BuildsShadow_PreservesOverlay_SwitchesAtomically`, `ShadowResyncHandlerTests.ShadowValidationFailure_LeavesLiveStateUntouched`, `SyncApplyDiTests.Configured_ReplacesBufferingAndNullResync`, `SyncApplyDiTests.Unconfigured_KeepsNoOpDefaults`.
3. `grep -rn "System.Net.Http\|HttpClient\|Npgsql" src/Animora.Persistence.Sqlite/Replica` returns nothing; `grep -rn "DELETE\|Remove\|Overwrite" src/Animora.Application/Sync/ConflictResolver.cs` shows no path that deletes/overwrites a `PostedFinancial` record (reversal-only — enforced by a test, criterion 2).
4. `ls src/Animora.Persistence.Sqlite/Migrations | grep -c ReplicaStore` ≥ 1 (next free ordinal); `grep -c "SYNC_APPLY_CONFLICT\|SYNC_SHADOW_VALIDATION_FAILED" src/Animora.Domain/Results/ErrorCodes.cs` = 2.
5. `tests/Animora.Domain.Tests` `LayeringRulesTests.Desktop_HasNoStoreOrNetworkReferences` still passes; the existing C11 `SyncCursor` tests remain green (G04 does not change `SyncCursor`).

## PROGRESS.md update

At the end of this phase: set the `G04` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `G05`**, append one `### G04` entry to "Repo state log" (replica store + migration ordinal, `SqliteSyncApplyStore`, `ConflictResolver` + policy catalog, `ShadowResyncHandler`, ErrorCodes added, test classes), log any deviation in the "Decision & deviation log", then commit with `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G04 conflict resolution & non-destructive pull"`.
