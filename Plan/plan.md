# PLAN — Clinic Client↔Server Sync Defects (D01–D18)

> **Source phase:** `docs/CLIENT_SERVER_CLINIC_SYNC_DIAGNOSTIC_2026-10-14.md` (static diagnostic, baseline `d2b1430b`).>   
> That doc **diagnoses** 18 defects but **prescribes no fixes** ("deliberately does not prescribe fixes"). This plan is the **execution/fix** of that phase.>   
> **Branch:** `genspark_ai_developer` · **Push rule (LOCKED):** after EVERY task below, run `scripts/git/push.sh "msg"`. Exit 3 ⇒ call `setup_github_environment` tool, re-run. Exit 2 ⇒ resolve preferring remote, re-run. **No push = failure.**>   
> **dotnet:** not on PATH in a fresh sandbox. `export PATH="$HOME/.dotnet:$PATH"`; if still missing run `bash scripts/sandbox/install-prereqs.sh` once (~5 min). **NO full-solution build/test.** Build/test ONLY the projects you touched. No long full-test runs.>   
> **App:** offline-first vet-clinic desktop (Avalonia MVVM, C#/.NET 10). Client = local **SQLite** replica (`Animora.Persistence.Sqlite`); Server = **PostgreSQL** (`Animora.Persistence.PostgreSql` + `Animora.Server`). Two pipelines: **(A) command outbox PUSH** (`Animora.Sync.Custom/HttpCommandTransport`), **(B) state feed PULL** (`Animora.Sync.Custom/Replica/ReplicaMaterializer`). One clinic = one tenant = one local DB.

---

## HOW TO READ THIS PLAN (token budget)

Every task below carries the **verified** root cause + exact file:line + the **decided** fix approach. The diagnostic was re-verified against live source while writing this plan (see "VERIFIED" stamps). **Do not re-read the whole diagnostic** — open only the specific files a task names, and only the line ranges given. The diagnostic's own §5 defect register is the authority for "where"; this plan is the authority for "what to change".

All paths are repo-relative. `:a-b` = line range at baseline `d2b1430b` (re-verify with `grep -n` before editing — the repo moves between sessions; match on the **symbol/statement**, not the fixed line number).

---

## ARCHITECTURE FACTS (VERIFIED — do NOT re-investigate)

### Identity bridge (root of D01)

- Online login succeeds against server → server identity = `ServerUserId` (e.g. `01M4X2FV…`).
- `src/Animora.Client.Services/Identity/AuthenticationService.cs → EnsureLocalMirrorAsync` (`:107-130`): verifies the stored online session matches `ServerUserId`/`ServerTenantId`, then dispatches `ProvisionLocalMirrorUserCommand(request.Username, request.Username, request.Password, role)` — **it passes NO server id**. VERIFIED.
- `src/Animora.Application/UseCases/Identity/ProvisionLocalMirrorUserCommand.cs` (handler `:66-88`): new-user branch calls `User.Create(tenantId, username, display, clock)` + `StaffMembership.Create(tenantId, user.Id, role, clock)`. VERIFIED.
- `src/Animora.Domain/Identity/User.cs:89`: `Id = IdGenerator.NewId(now)` → **random local id**, never the server id. VERIFIED.
- Triage doctor rail = **inner join** `StaffMembership.UserId == User.Id`, exposes `user.Id` as the doctor id: `src/Animora.Persistence.Sqlite/Queries/TriageQueries.cs:19-23`. VERIFIED. Same join in `Queries/ReceptionQueueQueries.cs:61-68`.
- UI forwards that id as `DoctorId`: `src/Animora.Desktop/Pages/Visits/VisitsTriagePageViewModel.cs:417-431` → `src/Animora.Client.Services/Visits/VisitsTriageService.cs:58-63` → `StartNewVisitCommand`.
- PUSH only rebinds the **envelope** actor, never the payload doctor: `src/Animora.Sync.Custom/HttpCommandTransport.cs → RebindIdentityAsync:143-179` (replaces `TenantId`/`UserId`/`DeviceId` only). VERIFIED (confirmed by D18 too).
- Server start handler requires an **active Doctor membership for `c.DoctorId`**: `src/Animora.Application/UseCases/Visits/StartVisitCommands.cs:157-161` → `memberships.FindActiveAsync(context.TenantId, c.DoctorId, ct)` → `DOCTOR_NOT_AVAILABLE`. The local `user.Id` has no PG membership → reject.
- Server `User` is **excluded from sync** (`SyncExclusions.cs` has `"User"`), but server `StaffMembership` IS replicated and `ReplicaMaterializer.cs:248-252` preserves its **server** `UserId`. So replicated memberships point at server user ids the local Users table never contains → the local doctor directory can never be built by pulling.

### Billing handoffs (D02, D03)

- Completion mints a handoff locally: `src/Animora.Application/UseCases/Visits/EncounterCommands.cs:220-227` → `src/Animora.Persistence.Sqlite/Repositories/VisitBillingHandoffWriter.cs`.
- **SQLite** handoff id: `src/Animora.Persistence.Sqlite/Entities/VisitBillingHandoff.cs:38` → `Id = IdGenerator.NewId(completedUtc)`. VERIFIED.
- **PostgreSQL** handoff id: `src/Animora.Persistence.PostgreSql/Repositories/VisitBillingHandoffWriter.cs:17` → `Id = IdGenerator.NewId(completedUtc)`. **Independent random id.** VERIFIED. Same mirror-pair for boarding (`BoardingBillingHandoff.cs` + PG `BoardingBillingHandoffWriter.cs`).
- `IdGenerator.NewId` = process-local random ULID (`src/Animora.Domain/Primitives/IdGenerator.cs:101-135`). The deterministic sibling is `IdGenerator.ForOperation(operationId, slot, at)` (`:66-77`): slot 0 = the operation id itself; slot n = fixed `SHA-256(operationId‖':'‖n)` over the 48-bit timestamp prefix. **Both stores run the same handler for the same operation**, so `ForOperation` makes them derive the SAME id. This is already how invoice ids stay equal across stores. VERIFIED. **This is the fix mechanism for D03.**
- Ready queue reads only handoff rows with `Status==Ready` and returns **`h.Id`** (not `h.VisitId`): `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs:31-66`. Invoice-create addresses that id: `SalesQueryService.cs:170-181` sends it as `HandoffId`; server `CreateInvoiceFromHandoffCommand.cs:28-36` looks up `h.Id == id` in both source tables (VERIFIED) and returns `NOT_FOUND` if neither matches.
- **Materializer does NOT rebuild the handoff table** from pulled Visits/Stays: `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs` Visit map (`:200-214`) has `Prescriptions`, `ServiceLines` but **no BillingHandoffs child writer**. VERIFIED. So a peer who pulls a completed Visit gets an empty ready queue (D02).

### Financial state has no cross-client delivery (D04)

- `src/Animora.Persistence.PostgreSql/Replication/SyncExclusions.cs:9-19`: Invoice, Payment, CashSession, JournalEntry, Cheque, Expense, Recurring*, StockMovement, PurchaseOrder, GoodsReceipt all excluded as "Command-settled money, ledger or stock state." VERIFIED.
- `ReplicaMaterializer` typed maps contain **no** Invoice/Payment/CashSession/StockMovement entry. Desktop cashier/sales/history read **local** SQLite only (`CashierQueries.cs`, `SalesQueries.cs`, `SalesPostingData.cs`). SettlementResult (`Contracts.Wire/Commands/SettlementResult.cs:18-27`) answers only the submitting operation — not broadcast state.

### Settlement is journal-only (D05)

- `src/Animora.Persistence.Sqlite/Commands/CommandJournal.cs:71-83` → `PendingCommand.ApplySettlement` (`Entities/PendingCommand.cs:81-92`) updates journal status/error/effect/role/server-refs **only**.
- `src/Animora.Application/Commands/LocalCommandProcessor.cs:94-96` applies that journal settlement; does **not** reconcile business aggregates (no undo of local Visit/Invoice/stock/drawer; no refresh of authoritative invoice number).
- Local vs server invoice number allocators are independent: `Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs:32-39` vs `Persistence.PostgreSql/Repositories/InvoiceNumberAllocator.cs:8-12`. `Invoice.MarkSettlement` exists (`Domain/Sales/Invoice.cs`) but **has no production caller**.

### Stock policy divergence (D07)

- `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs:31-35`: `AllowNegativeStockOnSale = true` (default). Server `Persistence.PostgreSql/Repositories/SalesPostingData.cs:14-22`: `AllowNegativeStock => false`. Shared preflight `Application/UseCases/Sales/PostInvoiceCommand.cs:62-89` returns `STOCK_WOULD_GO_NEGATIVE` only when negative stock is disallowed.

### Sync status hides terminal failures (D09)

- `LocalCommandProcessor.cs:94-96`: increments `settled` for Rejected/Conflict too; only transport failure increments `Failed`.
- `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs:71-90,104-122`: `RetriableCount` subtracts Rejected/Conflict/NeedsAttention; `MapState` can return `Synced` while only terminal-failed work remains. Topbar maps `Synced` → "all up to date" (`TopbarViewModel.cs:195-209`). No separate terminal-failure signal.

### Other verified mechanisms (for D06/D08/D10–D18) — trust the diagnostic §5 for file:line; re-verify by `grep -n` before edit.

---

## SCOPE DECISION (read before starting)

The diagnostic groups 18 defects. Fixing all 18 fully is a large multi-session effort. **Execute in severity order (Critical → High → Medium), one defect (or tight cluster) per task, pushing after each.** A later session can resume at the first `☐ not started` task. Each task is independently shippable and leaves the tree runnable offline.

**Clusters that MUST be fixed together** (shared mechanism — splitting leaves a half-fix):

- **D01** is standalone but unlocks the whole "valid visit" path.
- **D02 + D03** (handoff materialize + stable id) — a peer queue is useless if the id still mismatches; fix both before claiming cross-device invoicing.
- **D05 + D06 + D07 + D09** (optimistic-state lifecycle) — settlement reconciliation, dependency wiring, policy alignment, and honest status are one coherent correctness story.
- **D04 + D12** (financial read delivery + history) — history needs the read path.

**Do NOT** re-litigate the "not a defect" items (findings 7, 8, 9, 11, 12, 13, 14, 15 in diagnostic §3, and §7 "normal" behaviors). They are confirmed correct — no work.

---

## TASK D01 — Map the mirrored user to its server identity *(Critical)*

**Defect:** local login mirror creates a random local `User.Id`; triage sends that as `doctorId`; server has no matching membership → `DOCTOR_NOT_AVAILABLE`, and the local doctor directory can never be rebuilt from pulled server memberships (which carry server user ids).

**Root cause (VERIFIED above).** The mirror discards `ServerUserId`.

**Fix approach (decided):**

1. Thread the server identity into the mirror so the local `User.Id` **equals** `ServerUserId`:
   - `AuthenticationService.EnsureLocalMirrorAsync` already has `request.ServerUserId`/`ServerTenantId`. Extend `ProvisionLocalMirrorUserCommand` with a `string? ServerUserId` (keep `null` for ad-hoc callers), pass `request.ServerUserId`.
   - In the handler new-user branch, when `ServerUserId` is non-null use it as the created user id instead of `User.Create`'s random id. Add a factory/overload on `User` (e.g. `User.CreateWithId(serverUserId, tenantId, username, display, clock)`) — keep `User.Create` intact for other callers. `StaffMembership.Create(tenantId, user.Id, …)` then naturally points at the server id.
   - **Existing-user branch:** if an older local user with a random id already exists for this username, reconcile. Minimal safe option: if `ServerUserId` is non-null and differs from the found user's id, create/repoint the membership so the server-id user is the selectable doctor. (A full id-migration of historical local rows is out of scope; document the limitation in the commit.)
2. Verify triage/reception rails now surface `user.Id == ServerUserId` so the pushed `doctorId` joins on the server.
3. **Roster directory gap (D01 additional consequence):** pulling server `StaffMembership` still cannot show peer names because server `User` is excluded. **Decision: defer peer-directory completeness** to its own follow-up; scope this task to the signed-in-doctor id correctness (the incident's actual failure). Note the deferral in the commit. (`UserProfileService.cs:184-201` maps staff to Contracts only, NOT SQLite Users — that is the follow-up surface.)
4. **Role-drift edge (conditional):** `LoginViewModel.SignInLocalAsync:268-271` returns on first local success without calling EnsureLocalMirror, so a server role change with unchanged password never reconciles the local role. **Include only if cheap** (pass the incoming online Role to a mirror refresh on the successful local-sign-in path); otherwise defer and document.


**Tests:** targeted unit over real SQLite in `tests/Animora.Persistence.Tests/`: mirror-provision a user with a known `ServerUserId` → assert created `User.Id == ServerUserId` and the triage doctor rail returns that id.

**Build:** only `Animora.Domain`, `Animora.Application`, `Animora.Client.Services`, `Animora.Persistence.Sqlite`, and the touched test project.

**Status:** ☑ done — `User.CreateWithId` + `ProvisionLocalMirrorUserCommand.ServerUserId` (legacy random-id row superseded: membership ended, login name moved aside); first-run owner also keeps the server id; login now re-aligns the mirror after every verified online sign-in (role drift fixed). Deferred: peer-directory completeness, historical-row id migration.

---

## TASK D02+D03 — Rebuild peer handoff queue & make handoff ids store-stable *(Critical)*

**Defect D02:** pulled Visits/Stays carry `BillingHandoffs`, but the materializer never writes the local handoff table, so a peer's ready-to-bill queue is empty. **Defect D03:** even on the origin device, SQLite and PG mint **independent random** handoff ids, so invoice-create by `h.Id` fails `NOT_FOUND` on the server for otherwise-valid visits.

**Root cause (VERIFIED above).**

**Fix approach (decided):**

1. **D03 first (stable id):** replace `IdGenerator.NewId(completedUtc)` with the **deterministic** `IdGenerator.ForOperation(operationId, slot, completedUtc)` in BOTH writers, using the **completion command's operation id** and a fixed slot identical in SQLite + PG.
   - `src/Animora.Persistence.Sqlite/Entities/VisitBillingHandoff.cs:38` and `src/Animora.Persistence.PostgreSql/Repositories/VisitBillingHandoffWriter.cs:17` (and the Boarding pair).
   - Thread the operation id to the writer: `EncounterCommands.cs:220-227` (SQLite) and the server completion handler both have `CommandContext.OperationId`. Add an `operationId` param to `IVisitBillingHandoffWriter.Add(...)` / `VisitBillingHandoff.Create(...)` (and boarding equivalents).
   - Result: both stores compute the **same** handoff id for the same completion → origin-device invoice-create resolves on the server.
2. **D02 (materialize on pull):** in `ReplicaMaterializer`, when a Visit/BoardingStay root is applied, project its `BillingHandoffs` child image (id, source-root id, LinesJson, Status, InvoiceId — shapes in `RecordProjector.cs:183-197`) into the local `VisitBillingHandoff`/`BoardingBillingHandoff` table (upsert by id; with D03 the id matches the origin's). Add the child writer to the Visit and BoardingStay typed maps (mirror existing children). The ready query (`SalesQueries.cs:31-66`) then returns peer handoffs unchanged.
   - Keep the overlay/skip rules (`:265-319`) consistent — a locally pending handoff mutation must still win over the pulled copy.
3. Confirm boarding parity throughout (BoardingStay has the same handoff child + writer).

**Tests:** (a) same completion operation id → equal handoff id in both entity factories (pure unit, no DB). (b) applying a wire Visit record with a `BillingHandoffs` child inserts a `Ready` local handoff the ready query returns.

**Build:** `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Persistence.PostgreSql`, `Animora.Sync.Custom`, touched tests.

**Status:** ☑ done — handoff writers take the id from the handler: `ForOperation(op, 1)` for visit completion / checkout, `ForOperation(op, 3)` for the prepaid-room handoff at admission (slots 0–2 already used). `ReplicaMaterializer` upserts `billingHandoffs` child images into the local handoff tables (status never downgraded from Invoiced/Cancelled; a legacy local Ready row under a random id is replaced by the server's id; jsonb lines re-keyed to PascalCase).

**Follow-up found in session 4 (test-only defect, fixed):** `ReplicaMaterializerTests.PulledCompletedVisit_RebuildsTheReadyHandoff_UnderTheServerId_AndNeverDowngradesAnInvoicedOne` was **failing at the session baseline** (`555200bf`, verified in a throwaway worktree), which made it look like the never-downgrade rule was broken. It was not: the test read the handoff with a plain `SingleAsync`, but the local store sets `QueryTrackingBehavior.NoTracking` globally (`src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs:75`), so the entity came back **detached** and `MarkInvoiced` + `SaveChanges` silently persisted nothing — the test then blamed the pull. Production marks the handoff through `SalesPostingData.MarkHandoffInvoicedAsync`, which reads with `.AsTracking()` for exactly this reason. Fixed by using `.AsTracking()` (with the reason in the comment) and by adding a **durability assertion** between the local invoicing and the second pull, so the two possible causes can never be confused again. `Animora.Sync.Tests` is now **148/148 green** (was 147/148).

---

## TASK D05+D06+D07+D09 — Optimistic-state lifecycle correctness *(Critical / High)*

Fix the four-part "local looks done but server rejected" story together. (Done before D04 because it's the incident's core and needs no new architecture.)

**D05 — reconcile business state on settlement.** When settlement is `Rejected`/`Conflict`, reconcile the provisional business aggregate, not only the journal.

- `LocalCommandProcessor.cs:94-96` / `SettlementReconciler.ReconcileOneAsync`: after applying the journal settlement, for a rejected creation/mutation mark the local aggregate unsettled/needs-attention (do NOT silently delete user work — surface it). For accepted results, rehydrate the authoritative invoice number where the contract can carry it; wire the existing `Invoice.MarkSettlement` (`Domain/Sales/Invoice.cs`) from the reconciler. **Decision:** reversal of posted money/stock is non-destructive (mark + flag for operator), per ARCH §8.5 "posted invoice/payment reversal-only". Invent no rows.

**D06 — declare prerequisite dependencies.** Follow-ups carry no `DependsOnOperationId`, so after a rejected Start/Post they still upload and reject.

- `VisitEncounterService.cs:85-103`, `VisitsTriageService.cs:77-86`, `SalesQueryService.cs:340-347`, `Payments/InvoicePaymentCore.cs:69-87`: set `DependsOnOperationId` to the creating/posting operation id so `LocalCommandProcessor.cs:103-111`'s dependency guard holds the follow-up until the prerequisite is accepted. `CommandJournalBehavior.cs:47-61` already forwards the field — just populate it.
- **Terminal behavior decision:** when the prerequisite is **rejected** (not just pending), the dependent must also become terminal-rejected (not deferred forever). Implement that transition.

**D07 — align stock-policy honesty.** Local default `AllowNegativeStockOnSale=true` lets a post succeed locally that the server always rejects.

- `LocalStoreOptions.cs:31-35`. **Decision:** keep offline posting allowed (product decision), but mark the permissive-policy post clearly provisional so D05 reconciliation + D09 status surface the eventual server `STOCK_WOULD_GO_NEGATIVE`. Do NOT make the server permissive. Record the chosen default in the commit.

**D09 — stop hiding terminal failures.** A device with only Rejected/Conflict work can show `Synced`.

- `LocalSyncStatusService.cs:71-90,104-122`: add a **terminal-failure count** to the status contract; `MapState` returns a distinct state (e.g. `NeedsAttention`/`CompletedWithErrors`) when terminal-failed work exists with zero retriable backlog. `TopbarViewModel.cs:195-209`: present it honestly (not "all up to date"). Keep the retriable-backlog subtraction for the sendable count.

**Tests:** (a) rejected Start → dependent Rx not uploaded / marked terminal; (b) rejected settlement flags the local aggregate; (c) status with only terminal-failed work ≠ `Synced`.

**Build:** `Animora.Application`, `Animora.Client.Services`, `Animora.Domain`, `Animora.Persistence.Sqlite`, `Animora.Desktop` (topbar), touched tests. May be 2–4 pushes; each sub-fix is shippable.

**Status:** ☑ done — ☑ **D09 done** (`SyncState.NeedsAttention` + `ISyncStatusService.TerminalFailureCount`; `LocalSyncStatusService.MapState` returns it when only terminal-failed work remains; topbar shows it red, never "all up to date"). ☑ **D05 done** (`CommandJournal.ApplySettlementAsync` reconciles the invoice in the same save via `Invoice.MarkSettlement`: Rejected/Conflict flag it with the server code (non-destructive), Accepted → Settled only when no other invoice command is unsettled and no refusal is outstanding; server invoice-number rehydration deferred — the settlement contract does not carry it) · ☑ **D06 done** (`CommandJournalBehavior` auto-declares `DependsOnOperationId` = newest unsettled invoice post on the aggregate, else the aggregate's unsettled creating operation; `LocalCommandProcessor` settles a dependent of a Rejected/Conflict/Reversed prerequisite as `DEPENDENCY_REJECTED` without uploading) · ☑ **D07 done** (default `AllowNegativeStockOnSale=true` kept for offline posting; an oversold post is flagged provisional via `Invoice.MarkSettlement(LocallyPosted, STOCK_WOULD_GO_NEGATIVE)` and the chip shows the warning).

---

## TASK D04+D12 — Deliver financial read-state to peers & make history authoritative *(Critical / High)*

**Defect D04:** invoices/payments/cash-sessions/authoritative-stock are excluded from the feed with no compensating read path, so a peer can never display a cross-device transaction. **Defect D12:** cashier history and general history are device-local projections; incoming activity coverage is incomplete and current-state-derived rows misclassify after Void.

**Root cause (VERIFIED above).**

**Fix approach (decided — architecture addition; keep minimal & offline-safe):**  
Do NOT un-exclude the aggregates from the generic feed (they are intentionally command-settled — un-excluding risks double-writing settled money). Add a **dedicated authoritative financial read path**:

1. **Server read endpoint** returning a tenant's invoices/payments/cash-session summaries since a cursor (reuse existing PG queries — `Persistence.PostgreSql/Repositories/SalesQueries.cs` has `GetHistoryAsync`). Separate from command intake and the state feed.
2. **Client consumer** in `Animora.Client.Services/Sales` (or a new `Finance` reader) that fetches and upserts a **read-only, server-authored** local cache table for display — never fed back into the command pipeline. **No-op when the server is unconfigured** (offline safety).
3. Point cashier/sales/history read models at local-authored rows (own device, provisional) reconciled with server-authored rows (peers, authoritative); dedup by stable id (invoice id = operation id, so origin + server rows coincide).
4. **D12 specifics:** (a) emit a `VisitCompleted` ActivityEvent when a remote completed Visit is applied (materializer currently only synthesizes AppointmentBooked, `:323-351`); (b) stop deriving cashier-ledger Kind purely from **current** `i.State` so a Void does not rewrite history at the original timestamp — retain/emit a distinct void event (`SalesQueries.cs:223-236`).


**Scope guard (prefer split, one push each):** **D04a** server read endpoint · **D04b** client consumer + cache table + ONE migration · **D04c** read-model union · **D12** activity completeness + ledger timeline. Each sub-push must stay runnable offline.

**Migration:** client cache table needs ONE new SQLite migration — **list `src/Animora.Persistence.Sqlite/Migrations/` and take the next free ordinal on the owning DbContext** (per PROGRESS "Migration numbering rule"; physical head was `033`, some files use a double-underscore `__NNN_` prefix — match on the `NNN` token). Never hard-code an ordinal.

**Tests:** server endpoint returns rows for a seeded tenant; client consumer upserts them; read-model shows a peer invoice; offline → consumer no-ops, local reads unchanged.

**Build:** `Animora.Server`, `Animora.Persistence.PostgreSql`, `Animora.Client.Services`, `Animora.Persistence.Sqlite`, `Animora.Contracts`/`Contracts.Wire` if a DTO is added, touched tests.

**Status:** ☑ **D04a done** (server read endpoint). New wire DTO `FinanceReadPage`/`FinanceInvoiceRow`/`FinancePaymentRow` (`Contracts.Wire/Sync/FinanceReadPage.cs`); server-only port `IFinanceLedgerReader` (`Application/Ports`) implemented by `FinanceLedgerReader` (`Persistence.PostgreSql/Repositories`), returning a tenant's non-Draft invoices (by GREATEST(Issued,Posted,Settled)) + payments ≥ a UTC-tick cursor, bounded (≤500), with `NextCursorTicks`/`HasMore`. `GET /finance/ledger?since=&take=` (`FinanceEndpoints`, route `ServerRoutes.Finance`) gated by `TenantMember`+`DeviceActive`+`sales.read`, tenant-bound filter + correlation-logged 500 (never a silent empty page). Separate from command intake and the feed; nothing crosses back into the command pipeline. ☑ **D04b done** (client consumer + cache + migration 034). `SyncFeedClient.GetFinanceLedgerAsync` → `IPeerFinanceSync` (`Application/Ports`) implemented by `HttpPeerFinanceSync` (`Sync.Custom/Finance`), registered ONLY inside the configured-server branch of `AddAnimoraHttpSyncEngine` (offline/unconfigured ⇒ no consumer, `SyncOrchestrator` skips it). `SyncOrchestrator.PullAllAsync` runs it after a healthy state pull, behind the same authenticated-session gate and `TransitionGate`; a failure is logged and never fails the state pull (cursor stays, resumes next pass). Pages upsert by stable id into `[LocalOnly]` `PeerInvoices`/`PeerPayments` (an older re-delivery never rolls a newer row back; payments are idempotent) and advance `PeerFinanceCursors` in the SAME save; up to 25 pages drained per pass. Migration `034_PeerFinanceCache` (next free ordinal after 033; snapshot + designer updated). Nothing written here is read by the command pipeline or touches `Invoices`/`Payments`. Server fix found while consuming it: `FinanceLedgerReader` shipped the cursor past rows of a truncated window and sent a payment without its invoice (a partial payment changes no lifecycle tick) — now two-phase (window of ids, then rows), payment-touched invoices ride along, and a truncated collection caps `NextCursorTicks` at its last returned row. Tests: `tests/Animora.Sync.Tests/PeerFinanceSyncTests.cs` (5). ☐ D04c/D12 pending.

---

## TASK D10+D11 — Owner phone invariant & non-replaying skip *(High)*

**D10:** a raw non-blank phone (`"---"`, `"abc"`) normalizes to `""` and is stored, then the client materializer refuses the whole Owner (`PhonePrimary` required). **D11:** a skipped typed row advances the cursor with no replay path, so the row stays stale indefinitely.

**Fix D10 (single-point producer fix):** require a **non-empty normalized** phone so a server row the consumer will refuse can never be created.

- `src/Animora.Domain/Patients/Owner.cs:84-95,116-121,156-178` (`NormalizePhone`) and validators `CreateOwnerCommand.cs:34-40`, `UpdateOwnerCommand.cs:34`: add a check that the **normalized** result is non-empty (return `ValidationFailed` otherwise).

**Fix D11:** give skipped typed rows a durable replay path.

- `ReplicaMaterializer.cs:296-319` (constraint/missing-field skip) + `SqliteSyncApplyStore.cs:147-175`: when a typed write is skipped for a recoverable reason, record a "pending typed projection" marker keyed by root id+version (raw base is already persisted) instead of silently consuming the cursor; retry flagged roots when the overlaying pending command settles or on a later pass. Do NOT block the batch.

**Tests:** owner with phone `"---"` → create/update rejected; a row skipped due to pending overlay is re-materialized after the overlay settles.

**Build:** `Animora.Domain`, `Animora.Application`, `Animora.Sync.Custom`, `Animora.Persistence.Sqlite`, touched tests. **Status:** ☑ **D10 done** (D11 not started). **D10 was NOT implemented exactly as written — the written form would have broken a real feature.** Tracing every producer of a phone found a sixth, deliberate one: `Persistence.Sqlite/Seed/ClinicBakPreviewData.cs` passes the label `"ثبت نشده"` when the source contact has no phone, *relying* on normalization to produce the empty string ("never invent a routable telephone number for missing source contacts"). A hard "normalized phone must be non-empty" throw in `Owner` would therefore have broken the legacy-import preview (a local-only, isolated, test-covered feature — `LocalTestProfileTests`). Resolved by modelling the state honestly instead of forcing it: `Owner.NormalizePrimaryPhone` now distinguishes **(a) an ABSENT phone** (`null`/blank) → stored as `""`, the product's "not recorded" (only a source import produces it; the import now passes `null` instead of a digitless label) from **(b) a PROVIDED but digitless phone** (`"---"`, `"abc"`, `"+"`) → `ArgumentException`. Operator-facing commands can never reach (b): new `Rules.Phone` (code `PHONE_INVALID`) replaces `Rules.Required` for the phone in **all five** producer validators — `CreateOwner`, `UpdateOwner`, `CreateWalkIn`, `StartNewVisit`, `CreateBookingPatient`, `CreateBoardingStay`(walk-in) — and the two walk-in handlers (`CreateWalkIn`, `StartNewVisit`) additionally refuse a digitless phone with their typed `WALKIN_PHONE_REQUIRED` failure instead of letting the domain throw. Tests: `Owner_RefusesADigitlessPhone` (4 cases) + `Owner_AcceptsAnAbsentPhoneAsNotRecorded` (Domain.Tests, **green**), `CreateOwnerValidator_RefusesADigitlessPhone` (3 cases) + `UpdateOwnerValidator_RefusesADigitlessPhone` (Application.Tests, **green**); two stale `"REQUIRED"`-code assertions updated to `"PHONE_INVALID"`. Pre-existing row repair (owners already stored with `""` on the server) is out of scope, as the plan states.

---

## TASK D15 — Invoice-detail verdict must not mask an earlier rejected prerequisite *(High)*

**Defect:** the detail verdict derives from the single highest-`LocalSequence` command for the invoice, so an earlier rejected Post hides behind a later pending/accepted command; a later `INVOICE_INVALID_TRANSITION` displaces the root-cause `STOCK_WOULD_GO_NEGATIVE`.

**Fix:** `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs:123-142`: when selecting the authoritative verdict, prefer an **unresolved rejected prerequisite** (e.g. a rejected Post) over a later non-terminal/accepted command; surface the earliest root-cause error, not just the latest. Filter by command type where the comment already claims "post journal entry is authoritative". `SalesMapping.cs:335-358` renders it.

**Tests:** rejected Post + later pending → detail shows the Post rejection; rejected Post + later accepted on still-Draft → shows the Post rejection, not "server settled".

**Build:** `Animora.Persistence.Sqlite`, `Animora.Client.Services`, touched tests. **Status:** ☑ done — the verdict is now picked from the invoice's **money/stock-committing** journal commands only (create/post/void/refund/payment-register/line-return/adjustment/delete) instead of the single newest command of any type. A terminal refusal (Rejected/Conflict/NeedsAttention) is superseded only by a **later `Settled` command of the same command type** (the same operation retried and accepted); among the surviving refusals the **earliest** wins, so the root cause (`STOCK_WOULD_GO_NEGATIVE`) is surfaced instead of a downstream symptom (`INVOICE_INVALID_TRANSITION`) or a later pending command. `SalesQueries.SelectVerdict` logic + 4 new tests in `tests/Animora.Persistence.Tests/InvoiceSettlementVerdictTests.cs`. NOTE: `Animora.Persistence.Tests` cannot execute in the authoring sandbox — a host Application Control policy blocks `Animora.Infrastructure.Local.dll` (0x800711C7), pre-existing and unrelated; the project compiles and CI runs it.

---

## TASK D17 — Projector must not turn projection faults into tombstones *(High)*

**Defect:** when a live PG row fails to project (unparseable Version/Deleted, or `Project` throws), `RecordProjector` emits a `'D'` deletion; the client soft-deletes a live root and advances the checkpoint.

**Fix:** `src/Animora.Persistence.PostgreSql/Replication/RecordProjector.cs:63-67,77-86`: on a projection/parse fault, do **not** emit a deletion — skip the record this batch (log/quarantine server-side) so the live row is retried on correction, never represented as an authoritative delete. Only a genuine business-delete (actual `Deleted=true`) may emit `'D'`. Keep the tolerant null-cursor behavior for unparseable `UpdatedAt` (`:69-71`) — already correct.

**Tests:** a row whose payload throws during projection is skipped, not tombstoned; a genuinely deleted row still emits `'D'`.

**Build:** `Animora.Persistence.PostgreSql`, touched tests. **Status:** ☑ done — a live row that merely fails to project (missing/unparseable `Version`/`Deleted`, or a throw from `Project`) is now **skipped** with a `TraceLog.Warn` instead of being emitted as a `'D'` tombstone; the only remaining `'D'` construction is the `reader.IsDBNull(1)` branch, where the LEFT JOIN found no live row (a genuine delete). The row replicates again as soon as the server emits a correctable version of it. Guard test `RecordProjectorTests.FaultyRow_IsSkipped_NeverTombstoned` (asserts exactly one `'D'` construction remains).

---

## TASK D18 — Don't send a prior user's queued work as the current actor *(High)*

**Defect:** after a same-tenant user switch (A queues work, signs out, B signs in on the same device), the next pass claims A's commands and sends them rebound to B's identity; server attributes/authorizes as B.

**Fix:** carry the **originating** user on the pending command and refuse to rebind a queued command's actor to a different user.

- `src/Animora.Persistence.Sqlite/Commands/PipelineSql.cs:48-64` (claim SQL) and `HttpCommandTransport.cs:148-179` (rebind): only rebind envelope identity to the current session when the queued command's originating user matches the current session user; otherwise hold the command (needs-attention) rather than submit under the wrong actor. `SessionCurrentUserProvider.NewContext` already records the author — persist and check it. Sign-out already preserves the queue (`AuthenticationService.cs:45-64`).
- **Decision:** block cross-user submission (safe default). A "resume as original author only when re-authenticated" flow is out of scope — document.

**Tests:** queue a command as A, switch to B (same tenant), run a pass → A's command is NOT submitted as B (held/needs-attention).

**Build:** `Animora.Persistence.Sqlite`, `Animora.Sync.Custom`, `Animora.Application`, touched tests. **Status:** ☑ done — `HttpCommandTransport` now refuses to rebind a queued command whose **originating user** differs from the active session user (same tenant). The originating author was already persisted (`PendingCommand.UserId` → `CommandEnvelope.UserId`), so no new column was needed; `SessionCurrentUserProvider.NewContext` already records it. The guard is `CanSubmitAs`: rebind is allowed only when (a) the envelope's author IS the session user (a normal retry), or (b) the envelope is still **pre-binding** — carrying the local installation's placeholder tenant `Tenant.LocalDefaultId` (exactly what rebinding exists to upgrade). Otherwise `SubmitAsync` returns `Result.Fail(ErrorCodes.IdentityMismatch)` **without any HTTP request**, which `LocalCommandProcessor` releases as a failure → retries → terminal `NeedsAttention` (so D09's topbar surfaces it, never a silent loss). `SubmitBatchAsync` refuses a **mixed** batch as a whole (positional response; nothing is sent) — the batch path has no production caller today, only DI + tests. New `ErrorCodes.IdentityMismatch` (`IDENTITY_MISMATCH`, Forbidden) + a Persian operator message in `Desktop/Localization/ErrorMessages.cs`. New `tests/Animora.Sync.Tests/CommandAuthorGuardTests.cs` (5 tests, **green**): foreign author held and never sent, own command still submitted, pre-binding envelope still rebound to the session (asserted on the wire body), mixed batch refused, and no-session → unchanged. Documented limitation (per the plan): the original author must sign in again for their held work to go out; there is no re-authenticate-and-resume flow.


## TASK D16 — Sales service must not swallow local failures *(Medium)*

**Defect:** several `SalesQueryService` methods discard the command `Result` and return unchanged/empty data as if it succeeded.

**Fix:** `src/Animora.Client.Services/Sales/SalesQueryService.cs` — propagate failures like `EditLine`/`RemoveLine` already do (`:211-224`, via `ServiceException`): `AddLine`/`AddCustomLine` (`:194-206`), `SetDiscount`/`SetInsurance`/`PickOwner` (`:227-253`), `DeleteInvoice` (`:257-264`), `Void` (`:284-291`), and failed reads (`:46-55,63-68,88-92,149-159`) must distinguish "store/authorization failure" from "no rows". `CreateFromEncounterAsync` (`:170-181`): only fall through to the boarding branch on `NotFound`, and preserve the original error instead of generic `INVOICE_NOT_FOUND`.

**Tests:** a permission/validation failure in each method surfaces an error (not a success-shaped unchanged detail).

**Build:** `Animora.Client.Services`, touched tests. **Status:** ☑ done — every mutation now propagates a failed `Result` as a typed `ServiceException` (exactly as `EditLine`/`RemoveLine` always did): `AddLine`, `AddCustomLine`, `SetDiscount`, `SetInsurance`, `PickOwner` (both branches), `DeleteInvoice`, `Void`. Every read now **throws** on failure instead of returning an empty page — `GetReadyEncounters`, `GetInvoices`, `GetInvoicesForOwner`, `GetInvoice` (a failure is no longer indistinguishable from "unknown id"), `GetInsurers`, `GetOwners`, `GetCatalog`, `GetHistory`, `GetKpis` — via one documented `ThrowOnFailedRead` helper. `CreateFromEncounterAsync` falls through to the boarding branch **only** on `NotFound` and rethrows the real error otherwise (no more generic `INVOICE_NOT_FOUND` masking a validation/permission/store failure). **Evidence the throwing contract is the intended one (not a new invention):** the sibling `PatientsService` (C01 — the pattern this class documents itself as following) already throws on failed reads; `SaleHistoryViewModel` already catches and comments "Distinguish 'backend failed' from 'genuinely no rows'"; `PaymentModalViewModel.GetReadyCountAsync` already catches the typed failure (X40) with a comment that it keeps the last known count "instead of masquerading as an empty queue" — dead code until now. Three call sites that were *not* prepared for it were wrapped so nothing escapes: `SalePageViewModel.RefreshAfterMutationAsync` (fire-and-forget → toast + keep current view), `InvoiceViewerViewModel.LoadAsync` (toast + close), `OwnerSheetViewModel.LoadInvoicesAsync` (trace + leave the section). The sales page's own load path needs no change — the shell's load-state block already owns it. New `tests/Animora.Application.Tests/SalesServiceFailureTests.cs` (3 tests, **green**). **Not changed (out of the plan's named scope, documented):** `ReadModelAsync` still returns `null` on a failed read, so `TakePayment`/`Refund`/`ReturnLine`/`DeleteInvoice`/`Void` keep their pre-existing lenient "not found" path when the store itself is broken — making it throw would need a matching catch in the payment modal, which the plan does not scope.

---

## TASK D08 — Capability-gate Sales/Cashier payment & post actions *(Medium)*

**Defect:** pay/post CTA shows regardless of `SalesPost` permission; a Doctor clicking it hits local RBAC `PERMISSION_DENIED` repeatedly (affordance mismatch, not a bypass).

**Fix:** gate CTA visibility/enablement on the current user's `PermissionCodes.SalesPost`.

- `SaleInvoiceViewModel.cs:63-65` (`ShowPayCta`), `SalePageViewModel.cs:544-554` + ctor `:71-73`, `PaymentModalViewModel.CanPay:288-289,325-342`, `CashierPageViewModel.cs:514-538`. Inject the capability the same way other gated UI does; add the `SalesPost` check. No new backend — enforcement already works (`PostInvoiceCommand.cs:19`, `RegisterPaymentCommand.cs:18`).

**Build:** `Animora.Desktop` + touched tests. **Status:** ☑ done — all four pay/post affordances are now gated on the current user's `sales.post` capability, using the app's existing idiomatic seam (`ICapabilityGate.Can(PermissionCodes.SalesPost)`, exactly as `StatisticsExpensesViewModel` gates `finance.post`): (1) `SaleInvoiceViewModel` takes a `canPost` flag and `ShowPayCta` requires it (the Sales-page CTA, `SalePageView.axaml` `IsVisible={Binding SelectedInvoice.ShowPayCta}`); (2) `PaymentModalViewModel.CanPay()` requires it (disables the modal's Pay for both entry points); (3) `InvoiceViewerViewModel.CanPay` requires it (`InvoiceViewer.axaml` `IsVisible={Binding CanPay}`); (4) `CashierDetailViewModel.CanPay` requires it (`CashierPageView.axaml` `IsVisible={Binding Detail.CanPay}`). `ICapabilityGate` is injected where the page owns the capability (`SalePageViewModel`, `CashierPageViewModel`, `OwnerSheetViewModel`, `HistoryRecordOpener` — all DI-resolved) and passed down as a `bool` to the pure display shapes, so no display VM grows a service dependency. `SalePageViewModel.CanPostPayments` is re-read per render, so a sign-in/sign-out is reflected. **No backend change**: `PostInvoiceCommand`/`RegisterPaymentCommand` keep their `[RequiresPermission(PermissionCodes.SalesPost)]` and remain the authority — this only stops the dead affordance (the repeated `PERMISSION_DENIED`). Note: `Animora.Desktop` has no test project, so this is verified by build (plus the unchanged backend gate); the plan's "touched tests" list is empty for this task. `Animora.Domain.Tests` 312/314 — the 2 failures (`LayeringRulesTests.Desktop_HasNoStoreOrNetworkReferences`, `LocalTestProfileTests.ClinicPreview_…`) are both on the session-3 pre-existing list; the latter fails on its fixture precondition ("Clinic preview requires an empty isolated store"), not on the D10 phone change.

---

## TASK D14 — Background sync must invalidate open business pages *(Medium)*

**Defect:** after background apply/settlement, open pages stay stale; local work uses `DataChangeNotifier` but sync does not.

**Fix:** after `SyncOrchestrator` applies changes (`SyncOrchestrator.cs:55-66`) or a settlement reconciles a business aggregate, publish to the same `DataChangeNotifier` channel the local path uses (`src/Animora.Desktop/Services/DataChangeNotifier.cs`, consumed by `SalePageViewModel.cs:94-103,737-755`). Publish a coarse "domain X changed" signal (debounced) so open pages reload. Don't couple sync internals to specific pages.

**Note:** depends on D02/D04 actually materializing the row; sequence after those.

**Build:** `Animora.Client.Services`, `Animora.Desktop`, touched tests. **Status:** ☑ done — added `src/Animora.Desktop/Services/SyncChangeBridge.cs`, started at boot next to `MaintenanceScheduler`/`SyncSchedulerHostedService`. It observes the sync status the shell already publishes (`ISyncStatusService.StateChanged`) and re-announces a completed pass on the **same** `IDataChangeNotifier` channel the local write path uses, so `PageViewModelBase.RefreshOnDataChange` reloads the open page with the rows the pass brought in. **Layering kept clean:** the bridge lives in Desktop (sync lives in `Client.Services` and must not know about pages or this notifier), and it publishes a coarse **all-business-domains** signal — the per-page domain filter in `RefreshOnDataChange` (`if (!IsCurrentPage || Array.IndexOf(domains, domain) < 0) return;`) is what keeps it cheap, so no page is reloaded in the background. **Trigger = the cursor, not the state:** `LastSyncedAt` advances only after a pass whose command batch and state pull were both healthy, whereas the reported state can legitimately be `Pending` (queued work remains) or `NeedsAttention` while rows were just applied — so using the cursor also coalesces a pass's burst of state changes into **one** announcement (no timer needed) and guarantees a failed/deferred/offline pass announces nothing. No dispatcher dependency: `IDataChangeNotifier` already marshals to the UI thread by contract. Tested by linking the bridge into `Animora.Application.Tests` (the established pattern for Desktop logic in that project) with `SyncChangeBridgeTests` — **6 tests, green**: completed pass announces; one pass announces once; a `Pending` pass that still applied a pull announces; a failed/offline pass that never advances the cursor announces nothing; a second pass announces again; dispose stops it. `Animora.Desktop` builds clean.

---

## EXECUTION ORDER (severity-first; each ends with a push)

1. **D01** → push.
2. **D02+D03** → push (split into two pushes if id-threading and the materializer land separately).
3. **D05+D06+D07+D09** → push (2–4 pushes; each sub-fix shippable).
4. **D04+D12** → push (prefer D04a/b/c + D12 split, one push each; big).
5. **D10+D11** → push.
6. **D15** → push.
7. **D17** → push.
8. **D18** → push.
9. **D13** → push.
10. **D16** → push.
11. **D08** → push.
12. **D14** → push.
13. **FINAL TASK — delete this plan file only** (`Plan/plan.md`). Do NOT touch `docs/CLIENT_SERVER_CLINIC_SYNC_DIAGNOSTIC_2026-10-14.md`, roadmap files, or any other doc. → push.

> **Any heavy/large fix or sizeable new code written mid-task** (e.g. the D04 read endpoint, the D11 replay path) gets its **own** push with a clear `type(scope): message`, even before the task's test step — never batch a big change behind a later unfinished step.

## PUSH CHEATSHEET

```
scripts/git/push.sh "fix(identity): map local mirror user id to server id (D01)"
```

Exit 3 → call `setup_github_environment` tool → re-run. Exit 2 → resolve preferring remote → re-run. Verify the single `✓` line.

## PROGRESS LOG

- 2026-10-15: Plan authored. Diagnostic re-verified against live source for D01/D02/D03/D04/D05/D07/D09 (file:line confirmed): `User.Create` random id, dual random handoff ids, `IdGenerator.ForOperation` deterministic sibling, `SyncExclusions` financial list, journal-only settlement. dotnet not on PATH (needs `install-prereqs.sh` to build). No code fixes started yet.
- 2026-10-15 (session 2): D06, D07, D05 done (task 3 closed). CI run 38686583201 (core, ci/d06, commit 1244d405 = D06 only) reported 14 failing tests, incl. `OfflineClinicDayScenarioTests` (missing `history.invoice.posted`), `DispatcherTests.PingQuery_WorksWithDefaultsOnly`, `LayeringRulesTests.Desktop_HasNoStoreOrNetworkReferences`, `StaffManagementTests.*`. Not yet determined which are pre-existing vs caused by the D06 `CommandJournalBehavior` change — **first step next session: triage these before D04**. Resume at TASK D04+D12.
- 2026-10-15 (session 3): CI triage done — core run on HEAD vs baseline `82b74a3b`: 15 of the 16 failures are **pre-existing** at baseline (StaffManagement×6, Dispatcher, Layering, PatientsMapping×2, Dashboard×2, StartupBudget, LocalTestProfile, PatientsUseCase) — not caused by D01–D09, out of this plan's scope. The only new one, `OfflineClinicDayScenarioTests`, was a same-instant feed-order tie (stock row vs invoice post share `OccurredUtc`; id tie-break is process-global) — assertion made order-insensitive within one instant.
- 2026-10-15 (session 4): all 9 remaining defects (D04+D12, D10+D11, D15, D17, D18, D13, D16, D08, D14) re-verified against live source before any edit; every claimed mechanism confirmed real (D11's apply-store path corrected: it is `src/Animora.Sync.Custom/Replica/SqliteSyncApplyStore.cs`, not under `Persistence.Sqlite`). **D17 and D15 fixed and pushed.** Environment note: `dotnet` is present at `C:\Program Files\dotnet` (10.0.401) — the plan's "dotnet not on PATH" note is stale for this machine. `Animora.Persistence.Tests` **cannot run** in the authoring sandbox: a host Application Control policy blocks `Animora.Infrastructure.Local.dll` (0x800711C7); the project builds and CI executes it. `Animora.Server.Tests` runs fine. Push script works (no exit 2/3 encountered).
