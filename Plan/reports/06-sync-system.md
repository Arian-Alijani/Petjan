# 06 — Sync System Deep Audit (audit target #7, CRITICAL)

Scope: `src/Animora.Sync.Custom/**` (client engine), `src/Animora.Server/{Sync,Commands,Workers,Handlers}`,
`Application/Outbox`, `Persistence.*/{Replication,Outbox,Commands}`, `Client.Services/Sync/**`.

Method: prior art in `docs/CLIENT_SERVER_CLINIC_SYNC_DIAGNOSTIC_2026-10-14.md` (defect register D01–D18)
re-validated against live code, then extended. The backend-architecture audit doc named in the plan
(`docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md`) is **absent from the tree** — only the 10-14
diagnostic remains; this is noted as a documentation-drift finding (SYN-00).

Key architecture confirmed from live code:
- **Two pipelines, strictly separated.** State sync is **pull-only** (`HttpStateSyncEngine`: manifest →
  open batch → download+verify pages → verify content hash → atomic commit; *no push path, never touches
  the pending-command overlay*). Writes travel the **command outbox** pipeline (envelope → server intake →
  settlement), reconciled by `SettlementReconciler`.
- **Money/stock state is deliberately NOT in the pull feed.** `SyncExclusions.Reasons` excludes
  `Invoice, Payment, CashSession, JournalEntry, Cheque, Expense, RecurringExpense(+Payment), StockMovement,
  PurchaseOrder, GoodsReceipt` as *"command-settled money/ledger/stock state"*. `SyncContractCatalog`
  (the closed replicated surface) carries master/clinical data only (Tenant, Owner, Pet, Visit,
  VaccinationRecord, **TreatmentPlan + TreatmentOccurrence** — now present, resolving the plan's open
  question — etc.) plus a server-authored `StockLedgerEntry` *image* (read-only).

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| SYN-01 | High | sync (design) | `CommandIdRemapService` map is **in-memory only** (`ConcurrentDictionary`, no persistence) — a client restart between settlement and dependent-command submission loses every provisional→server id mapping; dependents then travel with a dead provisional id |
| SYN-02 | High | sync / inconsistency | Money state is pull-excluded by design but there is **no business-state reconciliation** of settled money back into local provisional records (confirms D04/D05/D11) — settlement updates the command record, not the invoice/payment/stock the UI reads |
| SYN-03 | Medium | sync (latent) | Settlement/history can present success while an earlier prerequisite was rejected (confirms D13/D15) — last-command settlement masks an earlier terminal failure; terminal failures also vanish behind a healthy sync badge (D09) |
| SYN-04 | Medium | sync (resilience) | `BearerAuthHandler` buffers the **entire** request body into a `byte[]` on every protected call to enable the single 401-retry — unbounded for large command/page uploads; and a mid-sync refresh that rotates the token still surfaces the original 401 if the retry also 401s, aborting the in-flight batch |
| SYN-05 | Medium | sync (ordering/latent) | `SettlementReconciler` orders by `LocalSequence` **within a page** only (`OrderBy` after `GetUnsettledPageAsync(page,50)`), not globally — across page boundaries a later-sequence command in page 1 can be reconciled before an earlier-sequence one in page 2, so dependency unblocking is not strictly oldest-first |
| SYN-06 | Low | perf | `HttpStateSyncEngine` stages verified pages in-memory (`MaxStagedPages = 64`) to survive a partial pull; a batch with `PageCount > 64` loses staging for the overflow pages and re-downloads them on every retry (no correctness loss, wasted bandwidth) |
| SYN-07 | Info | — | Pull integrity is strong: per-page `PageHash` + batch `ContentHash` verified before commit, atomic `ISyncApplySession` commit, checkpoint advances only on fully verified batch, epoch-change → `ResyncRequired`, bounded exponential backoff with jitter + `Retry-After` honouring, single-flight token refresh |

Plus a full push/pull/conflict/auth failure-scenario matrix (§Matrix).

---

## SYN-00 Missing prior-art document

- Severity: Info (documentation drift)
- Location: `docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md` — not present in the working tree.
- Evidence: plan Task T7 and the Ground-truth prior-art list name this file; only
  `CLIENT_SERVER_CLINIC_SYNC_DIAGNOSTIC_2026-10-14.md` exists. Its gap IDs (SYNC-C/H/M, SRV-M, RM-H…) cannot
  be re-validated against the original wording.
- Impact: the SYNC-*/RM-H findings must be treated as superseded by the 10-14 register (D01–D18); this audit
  re-validates the latter.
- Confidence: High.

## SYN-07 (baseline verification — pull-path integrity is sound)

- Severity: Info
- Location: `Sync.Custom/HttpStateSyncEngine.cs`, `IPageIntegrity.cs`, `Client.Services/Sync/SyncOrchestrator.cs`,
  `Sync.Custom/BearerAuthHandler.cs`.
- Evidence: `StreamPagesAsync` validates each page's `PageCount` against the batch, then
  `PageIntegrity.ComputeContentHash(pageHashes) == batch.ContentHash` before `session.CommitAsync`; any
  mismatch → `SyncIntegrityFailed`, session aborted, **checkpoint not advanced**. Epoch mismatch →
  `ResyncRequired("EPOCH_CHANGED")`. `DelayBeforeRetryAsync` does exponential backoff capped at 30 s with
  0.5–1.0 jitter and honours a numeric `Retry-After`. `SyncOrchestrator` gates on an authenticated session so
  no request hits `/sync/*` anonymously. `BearerAuthHandler` does exactly one single-flight refresh+retry,
  never a refresh loop.
- Why it matters: the pull path's correctness core is solid; the findings below are about the
  **command/settlement** side and reconciliation, not page verification.
- Confidence: High.

## SYN-01 Provisional→server id map is volatile (lost on restart)

- Severity: High
- Class: sync (design / latent)
- Location: `Sync.Custom/CommandIdRemapService.cs`:
  ```csharp
  private readonly ConcurrentDictionary<(string TenantId, string LocalId), string> _map = new();
  ```
  The doc even admits it: *"The map is an in-memory, tenant-scoped seam … persistence can be added behind it
  without touching callers."*
- Why it is a problem: the G05 contract is that a creating command authored with a client-generated
  provisional aggregate id, once settled, must let **dependent** commands still queued in the journal travel
  with the *server* id (`Rewrite` recomputes the fingerprint). If the app restarts (crash, update, reboot)
  after a creator settles but before its dependents are submitted, the map is empty: `Resolve` returns null,
  and the dependent command either ships the stale provisional id (server can't resolve the reference) or is
  stuck. The journal persists the pending commands; the *mapping that rewrites them* does not.
- Scenario / impact: offline device creates Owner (provisional id) + Pet (depends on Owner); Owner settles,
  app is killed, device reconnects → Pet submission cannot remap its OwnerId. Dependency resolution fails
  silently until a full resync re-seeds ids. Higher likelihood on flaky mobile/desktop restarts.
- Confidence: High (code-evident; depends on restart timing).

## SYN-02 Settled money state is never reconciled into local provisional records

- Severity: High
- Class: sync / inconsistency
- Location: `Sync.Custom/SettlementReconciler.ReconcileOneAsync` → `journal.ApplySettlementAsync(settlement)`;
  `Persistence.PostgreSql/Replication/SyncExclusions.cs` (Invoice/Payment/… excluded from the pull feed).
- Evidence: settlement application writes the **command record** state (`Settled/Rejected/Conflict`) via the
  journal. The pull feed (`SyncContractCatalog`) carries no Invoice/Payment/CashSession/Cheque/StockMovement
  (all in `SyncExclusions`). So the authoritative server money/stock image never flows back as business
  state; only the command's settlement verdict does. This is exactly diagnostic D04 ("invoices, payments,
  authoritative stock absent from cross-client state delivery") and D05 ("settlement changes the command
  record, not provisional business state"), confirmed against live code.
- Why it is a problem: local provisional invoice/payment/stock rows remain provisional-forever on the local
  store; a second device in the same tenant never sees the first device's settled money state through state
  sync, and the originating device has no reconciliation pass to replace its provisional figures with the
  server-authoritative ones. Stock policy divergence (D07) then flows into payment/history unreconciled.
- Scenario / impact: two clinic devices show different cash/stock/invoice totals for the same tenant until a
  manual full resync; provisional-vs-settled money can disagree with no automatic convergence. This is the
  structural core of the reported incident.
- Confidence: High (code-evident; corroborated by D04/D05/D11).

## SYN-03 Settlement/history can mask an earlier rejected prerequisite

- Severity: Medium
- Class: sync (latent / UX-correctness)
- Location: settlement application + Sales/History read models (confirms diagnostic D13/D15, D09).
- Evidence: the reconciler applies each command's own settlement independently; there is no cross-command
  "a dependent is only successful if its prerequisite settled Accepted" gate at the presentation layer. A
  later command's `Accepted` settlement, or a healthy generic sync badge, can be shown while an earlier
  prerequisite in the same business operation was `Rejected`.
- Why it is a problem: the operator sees "synced / paid / done" while a prerequisite (e.g. the invoice post
  behind a payment) was refused server-side. Terminal per-command failures disappear from the aggregate sync
  status.
- Scenario / impact: misleading acceptance presentation on the manager post/payment path (diagnostic §6.2);
  an operator believes a sale completed when the server refused the posting prerequisite.
- Confidence: Medium (presentation-layer behaviour; needs a UI runtime trace to fully confirm current state).

## SYN-04 Full request-body buffering + batch abort on double-401

- Severity: Medium
- Class: sync (resilience / perf)
- Location: `Sync.Custom/BearerAuthHandler.SendAsync`:
  ```csharp
  byte[]? body = null;
  if (request.Content is not null) body = await request.Content.ReadAsByteArrayAsync(ct)…
  ```
- Why it is a problem: every protected request buffers its **entire** content into a managed `byte[]` so the
  single retry can replay it. For large command payloads or page uploads this is an avoidable LOH/GC cost on
  the hot sync path. Separately, if the refresh succeeds but the retried request still 401s (clock skew on
  token validation, entitlement revoked mid-sync — migration 033 OnlineEntitlement/SealedEntitlement), the
  original 401 is surfaced and the in-flight pull batch aborts; the checkpoint is safe (not advanced) but the
  session is wasted.
- Scenario / impact: bearer expiry exactly at a batch boundary + a revoked entitlement → the whole pull
  session is discarded and retried from the last checkpoint; large uploads pay a double-allocation each.
- Confidence: High (buffering is code-evident); Medium on the revoke-race impact (needs runtime check).

## SYN-05 Reconciler oldest-first ordering is per-page, not global

- Severity: Medium
- Class: sync (ordering / latent)
- Location: `Sync.Custom/SettlementReconciler.ReconcileAsync`:
  ```csharp
  foreach (var record in paged.Items.OrderBy(r => r.LocalSequence)) …   // ordered WITHIN the 50-item page
  ```
- Why it is a problem: `GetUnsettledPageAsync(page, PageSize=50)` returns pages whose ordering across page
  boundaries is not guaranteed to be global `LocalSequence`. The in-memory `OrderBy` only sorts the current
  page. With >50 stuck commands, a higher-sequence command in page 1 can be reconciled before a
  lower-sequence command that landed in page 2, so the "earliest stuck command unblocks dependents first"
  intent does not strictly hold.
- Scenario / impact: on a device with a large backlog (long offline period), dependency unblocking can
  proceed out of order, producing extra `StillPending` passes (eventually consistent, but more round-trips
  and later convergence). Not a data-corruption bug because each re-submit is idempotent.
- Confidence: Medium (depends on `GetUnsettledPageAsync`'s SQL ordering; if it already orders by
  `LocalSequence` globally the effect is benign — verify the query).

## SYN-06 Page staging cap below max batch pages

- Severity: Low
- Class: perf
- Location: `Sync.Custom/HttpStateSyncEngine.cs`: `MaxStagedPages = 64`, `Stage()` only keeps a page if
  `_staged.Count < MaxStagedPages`. `DownloadAndApplyAsync` allows `batch.PageCount` up to
  `_options.MaxBatchPages`.
- Why it is a problem: if `MaxBatchPages > 64`, the pages beyond index 64 are never staged, so a partial-pull
  retry re-downloads them in full (the `If-None-Match`/304 fast-path can't fire for un-staged pages). No
  correctness impact — just wasted bandwidth on retries of very large batches.
- Scenario / impact: large resync batch + flaky link → repeated full re-download of the tail pages each
  retry.
- Confidence: High (code-evident; impact only when `MaxBatchPages > 64`).

---

## Matrix — push/pull/conflict/auth failure scenarios

### Push (command outbox → server intake/settlement)

| # | Trigger | Current behavior | Defect | Impact | Confidence |
|---|---|---|---|---|---|
| P1 | Mid-batch crash after send, before apply | Command stays `PendingSettlement`; `SettlementReconciler` re-submits the **same** envelope (same OperationId+fingerprint) after the 2-min grace; server returns stored result | None (idempotent replay works) | Delayed convergence up to grace window | High |
| P2 | Duplicate command replay | Server dedupes on `(TenantId, OperationId)`; fingerprint unchanged → stored result returned | None | — | High |
| P3 | Command-id remap collision / loss | In-memory map; **lost on restart** | SYN-01 | Dependent command ships dead provisional id | High |
| P4 | Clock skew on command timestamp | `CreatedAtUtc` from the client clock carried in envelope; server authoritative for settlement | Latent: client-authored timestamps trusted for ordering (see Domain audit DOM time findings) | Mis-ordered local sequence if client clock jumps | Medium |
| P5 | Offline queue overflow | Journal is the durable queue (SQLite, unbounded table — see SQL-02 retention gap) | Latent: no cap/compaction | Unbounded journal growth on a long outage | Medium |
| P6 | Partial outbox drain | `FinanceProjectionWorker` claims 100, acks per-message in its own save; unknown types dropped | INF-04 | Lost projection on mis-registered type | High |
| P7 | Settlement rejection reconciliation | `SettlementReconciler` maps `Rejected→Rejected`, validates via `SettlementTransitions.EnsureAllowed` | SYN-03 (masking at presentation) | Rejected prerequisite hidden behind a later success | Medium |

### Pull (state feed → local replica)

| # | Trigger | Current behavior | Defect | Impact | Confidence |
|---|---|---|---|---|---|
| L1 | Page integrity failure | `PageHash`/`ContentHash` verified; mismatch → `SyncIntegrityFailed`, abort, checkpoint not advanced | None | Session retried from last checkpoint | High |
| L2 | Snapshot/signature mismatch | Signing verified server-side; client aborts on protocol violation | None observed at client layer (see T8 for server signing) | — | Medium |
| L3 | Stale cursor / cursor regression | Checkpoint only advances on fully committed batch; server can answer `Current`/`Resync` | None | Safe | High |
| L4 | Replica materialization mid-failure | `ISyncApplySession` atomic commit or abort; staged pages kept for retry (≤64) | SYN-06 (staging cap) | Re-download tail pages of huge batches | High |
| L5 | Shadow resync trigger | `ShadowResyncHandler` / `ResyncRequired` path | Needs runtime check that shadow apply is atomic vs live store | Possible double-apply if not isolated | Medium |
| L6 | Generation/epoch switch during apply | `epoch0 != serverEpoch` → `ResyncRequired("EPOCH_CHANGED")` before download | None | Full resync forced | High |
| L7 | Skipped materialization consumes feed position | Confirms D11: feed position can advance without replaying typed state | SYN-02 family | Local projection gap for excluded/skipped entities | Medium |

### Conflict / ordering

| # | Trigger | Current behavior | Defect | Impact | Confidence |
|---|---|---|---|---|---|
| C1 | Two devices, same tenant, money state | Money excluded from pull feed; settlement verdict only | SYN-02 | Devices diverge on cash/stock/invoice until manual resync | High |
| C2 | Server-authoritative settlement vs local provisional | Only command record updated | SYN-02 | Provisional money never replaced by authoritative | High |
| C3 | Out-of-order page apply | Pages applied in ordinal order within a verified batch | None | — | High |
| C4 | Tombstone / delete propagation | Feed-fault isolation can turn malformed live rows into deletion records (D17) | Conditional server data-correctness defect | Spurious client-side delete | Medium |
| C5 | New aggregates (TreatmentPlan, RecurringExpense) in replicated set? | TreatmentPlan+TreatmentOccurrence **are** in `SyncContractCatalog` (synced); RecurringExpense(+Payment) **excluded** as command-settled money | Resolves the plan's open question; RecurringExpense correctly rides the command pipeline, not state sync | TreatmentPlan now replicates; recurring-expense convergence depends on SYN-02 reconciliation | High |

### Auth / entitlement during sync

| # | Trigger | Current behavior | Defect | Impact | Confidence |
|---|---|---|---|---|---|
| A1 | Bearer expiry mid-sync | `BearerAuthHandler` single-flight refresh + one retry | SYN-04 (double-401 aborts batch; full-body buffer) | Wasted session; checkpoint safe | High |
| A2 | Entitlement revoked mid-sync (mig 033) | Server 401/403 on protected route; refresh can't restore a revoked entitlement | SYN-04 | Pull aborts; retried until entitlement restored or user signs out | Medium |
| A3 | Device only `Pending` approval | `SyncOrchestrator` session gate → `Deferred`, no anonymous `/sync/*` hit | None | Correct fail-safe | High |
