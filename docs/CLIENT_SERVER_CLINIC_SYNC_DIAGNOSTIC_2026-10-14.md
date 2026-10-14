# Animora clinic synchronization: source-level diagnostic report

## 1. Review record and evidence boundaries

| Field | Value |
| --- | --- |
| Review date | 2026-10-14 |
| Source baseline | `d2b1430bc4724d9b76e05c1f68614e3b3e4e4215` (`main`) |
| Incident input | User-supplied Persian diagnostic report describing concurrent `vmone` (Doctor) and `owner1` (Manager) desktop sessions |
| Reported server | `http://188.212.99.234:5000`, Kestrel, PostgreSQL |
| Reported clinic tenant | `01M4WRZDN5R52NS2CHJT4VXZ15` |
| Method | Static inspection of production source, command contracts, persistence mappings, query paths, and UI wiring |
| Execution | **No tests, builds, application runs, database queries, or requests to the incident server were performed.** |
| Changes | Documentation only; no application or schema fixes |

The original two trace files and the incident databases were not supplied as files. The excerpts in the user's report are therefore **reported observations**, not independently inspected raw evidence. The deployed client/server binaries have not been matched to this Git commit. A code-confirmed mechanism is not a claim that the current source revision was deployed during the incident.

This review covers authentication/mirror identity, visit creation and completion, billing handoffs, invoices, posting, payments, cashier/history reads, and the synchronization paths that connect them. It also records directly related defects found while following those paths. It is not a claim of an exhaustive audit of every subsystem in the repository.

### Evidence and severity conventions

- **Observed + source-confirmed:** the supplied excerpt describes the failure, and the reviewed implementation contains the corresponding mechanism.
- **Source-confirmed, incident-unproven:** the defective path is present, but the excerpts do not demonstrate that this additional failure occurred in these sessions.
- **Conditional:** a specific input or session sequence is required; that condition is stated explicitly.
- **Not a defect / unproven allegation:** the observation is normal behavior, incorrectly interpreted, or insufficient to establish the asserted cause.
- **Critical:** breaks the clinic's cross-device billing workflow or leaves authoritative financial/clinical state materially different from the local workflow.
- **High:** creates persistent missing/misleading state, hides failed work, or corrupts attribution/history semantics.
- **Medium:** presentation, error propagation, or operational interpretation problem without demonstrated unauthorized server effects.

All source paths below are repository-relative. `:start-end` denotes the inspected line range **at the baseline commit**, not a generated stack trace. Small code excerpts reproduce the decisive statements; surrounding implementation is identified by file and method.

## 2. Executive conclusion

The reported cashier failure is real, but **it has more than one independent cause**. Correcting only `doctorId` would not make cross-device invoicing work:

1. A local login mirror creates a different `User.Id`; triage selects that local identity and sends it as a business `doctorId`. Transport replaces the envelope actor, not the payload doctor reference. The server correctly rejects the unknown doctor membership.
2. Even a successfully completed and accepted visit does not populate another client's ready-to-bill queue: the server exports `billingHandoffs`, but the client ignores them and does not rebuild the local handoff tables.
3. Even on the originating device, a valid handoff is minted with a different random ID on the client and server. Invoice creation addresses the **handoff ID**, not the stable visit ID; the server cannot find the client's ID.
4. Independently, invoices, payments, cash sessions, stock movements, and other financial aggregates are explicitly excluded from the server state feed. The desktop's cashier/invoice/history services read local SQLite, not a server read API that supplies these missing records.
5. Server settlement updates the command journal, not the locally changed business rows. Rejected visits, posts, payments, stock effects, and activity rows can remain locally visible. Accepted financial results do not refresh authoritative invoice numbers or financial state either.
6. The UI's optimistic post-and-pay sequence has no declared dependency on server acceptance of the post. A rejected post is followed by a payment against an authoritative Draft invoice.
7. Rejections are stored, but the generic sync status excludes terminal failures and can declare the device `Synced`. There is partial invoice-detail warning support; it is incorrect to say the client never reads `responsibleRole` at all.

An `UpToDate` response or equal watermark proves the clients reached the same position in the **configured feed**, not that their cashier databases, invoice histories, or materialized business tables are equal.

## 3. Adjudication of the 15 supplied findings

| Original finding | Verdict | Correct interpretation / related defect |
| --- | --- | --- |
| 1. Shared malformed Owner | **Substantially valid, with limits** | Empty `phonePrimary` is sufficient for the materializer to skip the row. Null national ID/address/email are optional and do not establish corruption. Both clients receiving the row establishes a shared wire input, not which writer originally damaged it. Skipping does not prove an earlier local copy never existed. See D10–D11. |
| 2. Dual `vmone` identity | **Real integration defect; existence alone is not the full cause** | Separate local and server identities are intentionally used in the login bridge, but no corresponding business-reference mapping is provided. The defect is leaking a local `User.Id` into server-resolved fields. Initial `CREDENTIALS_INVALID` does not prove a security-stamp mismatch: an absent user or missing/wrong local password verifier produces it. See D01. |
| 3. `visits.visit.start` rejection | **Observed + source-confirmed** | The submitted local doctor ID is checked against PostgreSQL staff membership and can fail with `DOCTOR_NOT_AVAILABLE`. `responsibleRole=Manager` is generic responsibility metadata, not a diagnosis that a Manager was selected as doctor. See D01, D05, D09. |
| 4. `VISIT_NOT_FOUND` cascade | **Observed + source-confirmed, with a counting/reference correction** | There are seven visit commands rejected with `VISIT_NOT_FOUND` and one invoice-create command rejected with `NOT_FOUND`: eight commands, not eight identical errors. The invoice command takes a handoff ID; it is not literally addressed to the same Visit aggregate. Missing visit creation explains missing server handoff in this incident, while D03 supplies a separate handoff-ID failure even for valid visits. See D03, D05–D06. |
| 5. Doctor posting RBAC | **UI defect; enforcement is working** | The shown `PostInvoiceCommand` rejection is from the **local dispatcher RBAC behavior**, not proof of a server rejection/request. The UI exposes a payment CTA that internally posts; there need not be a separate Post button. Doctor draft/read access does not imply posting rights. See D08. |
| 6. Post rejected, then payment rejected | **Observed + source-confirmed; original UI explanation is imprecise** | The service does check a **local** post failure before payment. It does not await server settlement. Local posting succeeds under a different stock policy, so payment is queued before authoritative rejection is known. The UI need not knowingly allow payment on a server-Draft invoice after receiving the rejection. The server's stock and state-transition guards are correct. See D05–D07. |
| 7. Same guest-invoice fingerprint | **Not an idempotency defect** | Fingerprint hashes command type + payload, not operation ID. Two independent operations may have identical payloads and produce distinct invoices. Page initialization deliberately opens/creates a default guest draft. The excerpts do not establish a double-click/retry bug. See §7. |
| 8. Two checkpoint scopes | **Misclassified** | Scope name is the same (`default`); the **tenant key** differs. An absent checkpoint for a newly bound tenant returns zero, and binding isolates the local clinic store. This is not evidence of resetting a live clinic cursor incorrectly. The old tenant's provenance cannot be established from these excerpts. See §7. |
| 9. Different watermark paths | **Not a defect** | Different start times and batching produce different paths. `12` from the old tenant cannot be combined with the new tenant's `36` as one cursor progression. Equal final feed positions do not imply complete business-state equality. See D04, D11–D12. |
| 10. `responsibleRole` never read | **Incorrect as a blanket claim; UX gap remains** | It is deserialized, persisted in `PendingCommand`, read by invoice-detail queries, included in Sales state labels, and exported in diagnostics. There is no equivalent actionable visit rejection presentation, and terminal failures disappear from the generic sync badge. See D09, D15. |
| 11. Three anonymous login/provision dispatches | **Expected fallback sequence, not inherently a defect** | The explicit online-first → local sign-in → verified mirror provision → retry path runs before a local session exists; anonymous contexts are expected. The problematic identity produced by that sequence is D01, not the number of dispatches. |
| 12. Different capability lifetimes | **Expected risk policy; no demonstrated divergence** | Read=30 days, ordinary write=14 days, financial/stock write=72 hours, critical=zero. Expiry dictionaries are authority bounds, not proof every listed permission is granted to Doctor. Actual permission grants and evaluator outcomes must be compared separately. See §7. |
| 13. Frequent polling | **Not established as a defect** | Default scheduling is 30 seconds with ±20% jitter; on-demand passes are also supported. Most listed intervals are consistent with this policy. Short bursts require raw call-site evidence; they are not enough to prove a tight loop. The stated counting interval is inconsistent with the examples. See §4. |
| 14. Entity ID equals operation ID | **Intentional correctness mechanism** | Invoice creation explicitly uses `id: context.OperationId`; `IdGenerator.ForOperation(..., 0, ...)` also returns operation ID. This is not a malformed response or a duplication cause. |
| 15. Similar names on different Owners | **Not a defect on this evidence** | Different IDs and differently spelled names identify different records. No merge, collision, or relation is established. Name similarity is not an entity identity rule. |

## 4. Corrections to incident chronology and proof claims

1. **Keep enqueue time, transport time, and server settlement time separate.** The narrative places C2 post/payment rejection at approximately `18:41:17`; the supplied transport excerpts place these settlements at `18:41:25.532...Z` and `18:41:25.895...Z`. An earlier local action/enqueue time is not the rejection time.
2. **UTC conversion does not guarantee synchronized machine clocks.** C1 `11:48:30` at −07:00 is `18:48:30Z`; C2 `22:10:25` at +03:30 is `18:40:25Z`. The offsets are useful, but the excerpts also contain request/response timestamps that do not line up perfectly with server `settledAtUtc`. Network time, clock skew, and log precision must not be collapsed into an exact total order.
3. **The polling examples start earlier than their stated interval.** `22:12:35` at +03:30 is `18:42:35Z`, not inside `18:47:27–18:51:14Z`. The claimed “about 22 cycles in that interval, all UpToDate” cannot be verified from this inconsistent list. This review does not recalculate a definitive cycle count without the full traces.
4. **A transport batch record count is not a typed-table success count.** A batch containing 33 records can advance the cursor while individual records are skipped or unsupported entity/child surfaces are not projected. See D11.
5. **The summary says two bad data records, but identifies one Owner.** No second bad record is supplied. No second record is invented here.
6. **HTTP 200 is not business acceptance.** `SettlementResult.Outcome` carries Accepted/Rejected/Conflict/Deferred. A well-formed HTTP 200 with `outcome=Rejected` is the implemented command protocol.
7. **No evidence here proves loss of PostgreSQL invoice rows.** The strongest proven failures are rejection, missing cross-device distribution, stale/provisional local rows, missing history projection, and disconnected UI refresh. Server persistence loss would need database/intake evidence not supplied.

## 5. Defect register

| ID | Severity | Ownership | Defect | Incident evidence |
| --- | --- | --- | --- | --- |
| D01 | Critical | Client identity + roster + wire boundary | Local user IDs leak into doctor references; replicated staff cannot join to local Users | Observed + source-confirmed |
| D02 | Critical | Client sync materialization | Remote visit/stay billing handoffs never populate ready queues | Source-confirmed; separate from rejected incident visit |
| D03 | Critical | SQLite + PostgreSQL handoff writers | Local/server handoff IDs are independently random | Source-confirmed; incident `NOT_FOUND` has multiple possible causes |
| D04 | Critical | Server feed + client read architecture | Financial/stock state has no cross-client delivery path | Source-confirmed; explains broader invoice/history complaint |
| D05 | Critical | Client settlement/persistence | Settlement modifies journal only, leaving provisional business effects and numbering | Observed + source-confirmed; accepted-number divergence source-confirmed |
| D06 | High | Client command construction | Visit/invoice/post/payment dependencies are not declared | Observed + source-confirmed |
| D07 | High | Local/server stock policy | Local posting permits negative stock by default; server never does | Observed rejection + source-confirmed default mechanism |
| D08 | Medium | Desktop Sales/Cashier | Payment/post UI is not capability-gated | Observed + source-confirmed |
| D09 | High | Sync status + Desktop | Rejected/conflicted work can end in a clean `Synced` presentation | Observed lack of warning + source-confirmed |
| D10 | High | Shared Owner input/domain validation | Nonblank phone input can normalize to empty and become unmaterializable | Bad row reported; specific creation input unproven |
| D11 | High | Client replica apply | Skipped typed rows consume the cursor without a replay path | Source-confirmed; poisoned Owner is an observed instance |
| D12 | High | History architecture/materialization | Financial and general activity histories are not clinic-wide authoritative histories | Source-confirmed |
| D13 | High | Local activity projection/query | General History income totals count invoice posting and its payment twice | Source-confirmed, incident-unproven |
| D14 | Medium | Desktop/sync integration | Successful background pulls/settlements do not invalidate visible business pages | Source-confirmed, incident-unproven |
| D15 | High | SQLite invoice-detail query | Latest command verdict can mask an earlier rejected prerequisite | Source-confirmed, conditional |
| D16 | Medium | Client Sales service | Several mutation/query failures are converted into unchanged data or empty success-like results | Source-confirmed, incident-unproven |
| D17 | High | Server feed projector | Projection/schema errors are published as deletions of potentially live roots | Source-confirmed, conditional; not observed in supplied Owner payload |
| D18 | High | Client outbox/identity boundary | Same-clinic user switching can submit another user's queued work as the current actor | Source-confirmed, conditional; not demonstrated across these two devices |

### D01 — Mirror identity is not mapped for business doctor references

**Locations and decisive code**

- `src/Animora.Client.Services/Identity/AuthenticationService.cs:107-130`, `EnsureLocalMirrorAsync`: validates `ServerUserId`/`ServerTenantId` against the stored online session, but constructs `ProvisionLocalMirrorUserCommand(request.Username, request.Username, request.Password, role)` without either server ID.
- `src/Animora.Application/UseCases/Identity/ProvisionLocalMirrorUserCommand.cs:31-35,66-88`: command contains username/display name/password/role; new-user branch calls `User.Create(...)` and creates a local membership with `user.Id`.
- `src/Animora.Persistence.Sqlite/Queries/TriageQueries.cs:19-35` and `Queries/ReceptionQueueQueries.cs:61-68`: doctor rails use an **inner join** from `StaffMembership.UserId` to local `User.Id`; the exposed doctor ID is `user.Id`.
- `src/Animora.Desktop/Pages/Visits/VisitsTriagePageViewModel.cs:417-431`: `DoctorId: SelectedDoctor.Doctor.Id`.
- `src/Animora.Client.Services/Visits/VisitsTriageService.cs:58-63`: forwards that reference unchanged into `StartNewVisitCommand`.
- `src/Animora.Sync.Custom/HttpCommandTransport.cs:143-179`: `RebindIdentityAsync` replaces only envelope `TenantId`, `UserId`, `DeviceId`.
- `src/Animora.Application/UseCases/Visits/StartVisitCommands.cs:157-161`: server/shared handler requires an active Doctor membership for `c.DoctorId`.
- `src/Animora.Persistence.PostgreSql/Replication/SyncExclusions.cs:20` excludes `User`; `SyncContractCatalog.cs:8-10` includes staff membership, and `ReplicaMaterializer.cs:248-252` preserves its server `UserId`.

```csharp
// UI business reference:
DoctorId: SelectedDoctor.Doctor.Id,
// Handler membership lookup:
var doctor = await memberships.FindActiveAsync(context.TenantId, c.DoctorId, ct);
if (doctor is null || doctor.Role != StaffRole.Doctor)
    return Result<EncounterDraftReadModel>.Fail(ErrorCodes.DoctorNotAvailable);
```

**Failure construction.** Server user `01M4X2FV...` signs in; the verified mirror creates local user `01M4XVX7...`. Its local membership makes the Doctor appear selectable and lets the local start handler succeed. On upload the actor becomes `01M4X2FV...`, but `payload.doctorId` remains `01M4XVX7...`. PostgreSQL has no matching Doctor membership, so the start is rejected before server visit/owner/pet/queue creation.

**Additional roster consequence.** Pulling server `StaffMembership` records cannot by itself build the local doctor directory because server `User` records are excluded and local mirrors use different keys. The online staff-management read in `Client.Services/Profile/UserProfileService.cs:184-201` maps a response to Contracts; it does not populate the SQLite Users used by triage. A peer doctor may be absent from the rail, unnamed on joined reads, or replaced by a locally provisioned but server-unknown reference. Merely seeing staff in the profile page does not establish that triage has a usable authoritative directory.

**Existing-mirror role drift (conditional, not observed here).** `LoginViewModel.SignInLocalAsync` returns immediately when the first local sign-in succeeds (`:268-271`). It only calls EnsureLocalMirror after a local credential failure. A server role change with an unchanged password can therefore leave the existing local membership role unreconciled: the incoming online Role is not passed to the mirror-refresh command on that successful path. Local role-based decisions can disagree with current server membership even though both logins succeeded. This is another identity-bridge synchronization gap; it does not demonstrate a server authorization bypass.

**Qualification.** The first failed local sign-in does not diagnose a password/security-stamp disagreement. `UseCases/Identity/SignInCommand.cs:58-77` returns `CREDENTIALS_INVALID` for an absent local user and for a failed/missing verifier. The verified provisioning fallback itself is explicitly implemented in `Desktop/Pages/Login/LoginViewModel.cs:252-288`.

### D02 — Remote billing handoffs are exported but not materialized or rebuilt

**Locations**

- `Persistence.PostgreSql/Replication/SyncContractCatalog.cs:25-28`: `BoardingStay` and `Visit` export `BoardingHandoffLines`/`VisitHandoffLines` as root children (all paths in this section are under `src/`).
- `Persistence.PostgreSql/Replication/RecordProjector.cs:20-29,183-197`: those children become `BillingHandoffs`; child images include `Id`, source root ID, `LinesJson`, `Status`, and `InvoiceId`.
- `Contracts.Wire/Sync/Contracts/VisitSyncContract.cs:23-24`: carries `ServiceLines` and `BillingHandoffs`.
- `Sync.Custom/Replica/ReplicaMaterializer.cs:24-26,200-214,232-241`: explicitly omits the handoff child rows; its Visit and BoardingStay maps contain no handoff-table child writer.
- `Application/UseCases/Visits/EncounterCommands.cs:220-227`: local completion is what calls the handoff writer.
- `Persistence.Sqlite/Repositories/VisitBillingHandoffWriter.cs:10-13`: inserts the local handoff.
- `Persistence.Sqlite/Queries/SalesQueries.cs:31-66`: ready queue reads only `VisitBillingHandoff`/`BoardingBillingHandoff` with `Status == Ready`; it does not derive rows directly from completed Visits/Stays.

**Failure construction.** Client A completes a server-accepted visit. Client B pulls the updated Visit including `billingHandoffs`. The raw payload is stored in `repl_base`, and the Visit root is upserted into `Visits`. No row is inserted into B's handoff table; raw SQL does not run `CompleteVisitCommandHandler` or raise completion events. Consequently B's ready-to-bill query returns no entry, even with valid owner/pet data, a successful batch, and the same final watermark.

The materializer comment says the device-local queue is “rebuilt from the visit/stay itself”, but this apply path contains no such rebuild. This is a **client projection gap**, not proof the server forgot the handoff. The ready query already uses left joins for owner/pet, so blaming its previously corrected inner-join behavior would be wrong at this baseline.

### D03 — The handoff identifier used to create an invoice differs across stores

**Locations**

- `src/Animora.Persistence.Sqlite/Entities/VisitBillingHandoff.cs:26-40`: `Id = IdGenerator.NewId(completedUtc)`.
- `src/Animora.Persistence.PostgreSql/Repositories/VisitBillingHandoffWriter.cs:10-19`: independently executes the same **random** ID generator.
- `src/Animora.Persistence.Sqlite/Entities/BoardingBillingHandoff.cs:42-56` and `Persistence.PostgreSql/Repositories/BoardingBillingHandoffWriter.cs:9-17`: same problem for boarding.
- `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs:42-44,51-53,65-66`: ready entry ID is `h.Id`, not `h.VisitId`/`h.StayId`.
- `src/Animora.Client.Services/Sales/SalesQueryService.cs:170-181`: forwards that entry ID as `HandoffId`.
- `src/Animora.Persistence.Sqlite/Repositories/SalesDraftData.cs:14-27` and `Persistence.PostgreSql/Repositories/SalesDraftData.cs:15-28`: look up by `h.Id == id`.
- `src/Animora.Server/Handlers/CreateInvoiceFromHandoffCommand.cs:28-36`: checks both tenant-filtered source tables and returns `NOT_FOUND` when neither contains the ID.

**Failure construction.** Even when the same completion command succeeds in SQLite and PostgreSQL, client handoff `H_local` and server handoff `H_server` are independently generated. Creation from the local queue sends `{ "handoffId": "H_local" }`. Server lookup requires `H_server`, so it rejects invoice creation. Matching visit IDs do not fix this child-reference mismatch. Server and client completion timestamps may also differ, but random generation is sufficient even if timestamps match exactly.

`IdGenerator.NewId` is not deterministic; `Domain/Primitives/IdGenerator.cs:101-135` uses process-local random state. The operation-derived helper is a different function (`ForOperation`, lines 54-77).

**Remap qualification.** `Sync.Custom/CommandIdRemapService.cs` does not establish a mapping between these local/server child IDs: `Learn` requires an accepted creator with an `AggregateId`, and `FetchCreatedIdsAsync` only automatically maps an explicitly supplied provisional **primary** ID. Receiving a server child ID alone does not identify which local child it replaces. No local handoff-ID correspondence is provided in these creation paths. Moreover, the reviewed production call sites do not call `Rewrite` or `FetchCreatedIdsAsync`: `HttpCommandTransport.SubmitAsync` only rebinds envelope identity, and `LocalCommandProcessor` does not invoke ID learning. The standalone reconciler has an optional `Learn` call, but that is not an active child-reference rewrite path in the normal submission flow. The mere presence of the remap class must not be treated as evidence that this handoff mismatch is repaired.

**Incident distinction.** For the shown rejected visit, server completion never created any handoff at all. That already explains `NOT_FOUND`. The random child IDs establish a second, source-confirmed failure for otherwise valid visits; the invoice-create excerpt is not enough to distinguish the two causes by itself.

### D04 — Invoices, payments and authoritative stock are absent from cross-client state delivery

**Locations**

- `src/Animora.Persistence.PostgreSql/Replication/SyncExclusions.cs:9-19`: excludes Invoice, Payment, CashSession, JournalEntry, Cheque, Expense, RecurringExpense/payment, StockMovement, PurchaseOrder, GoodsReceipt as “Command-settled money, ledger or stock state.”
- `src/Animora.Persistence.PostgreSql/Replication/SyncContractCatalog.cs:6-33`: closed feed surface contains no financial transaction aggregates.
- `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs:97-254`: typed projection likewise has no Invoice/Payment/CashSession/StockMovement map.
- `src/Animora.Client.Services/Sales/SalesQueryService.cs:63-94,149-165,331-347`: dispatches local queries through fresh scopes; no invoice/history HTTP fetch is used.
- `src/Animora.Persistence.Sqlite/Queries/CashierQueries.cs:45-102`: invoice tabs read local Invoice rows.
- `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs:119-122,199-238`: payment details/history read local Payments/Invoices.
- `src/Animora.Persistence.Sqlite/Repositories/SalesPostingData.cs:17-28`: on-hand/cost reads use local stock snapshot/movements.

**Failure construction.** A cashier on A creates/posts/pays an invoice accepted by PostgreSQL. B's pull cannot receive the invoice, payment, authoritative balance, or cash drawer because these entities are outside the feed. B's local query service therefore cannot display the shared transaction. Retrying pulls cannot request a surface the contract excludes.

The settlement response answers the **submitting operation**; it is not broadcast state for other devices. `Contracts.Wire/Commands/SettlementResult.cs:18-27` supplies outcome/IDs/version/effect/role, not invoice lines, balances, payments, numbering, or history. A journal result on A cannot fill B's local read tables.

This is an **end-to-end missing read/delivery path**. The exclusions may be deliberate separation of command settlement from generic replication, but the actual desktop reads have no compensating authoritative financial path. Existing PostgreSQL sales/cashier repository classes are not evidence that the desktop calls a corresponding read endpoint; the reviewed route wiring exposes command intake and the restricted sync feed, not a financial read adapter consumed by these services.

### D05 — Server settlement changes the command record, not provisional business state

**Locations**

- `src/Animora.Persistence.Sqlite/Commands/CommandJournal.cs:71-83`: finds a PendingCommand, validates settlement transition, calls `row.ApplySettlement`, saves.
- `src/Animora.Persistence.Sqlite/Entities/PendingCommand.cs:81-92`: updates journal status/error/effect/role/time/server references only.
- `src/Animora.Application/Commands/LocalCommandProcessor.cs:94-96`: applies that journal settlement; it does not reconcile business aggregates.
- `src/Animora.Sync.Custom/SettlementReconciler.cs`, `ReconcileOneAsync`: uses the same journal application and optional ID-learning path.
- `src/Animora.Domain/Sales/Invoice.cs:528-547,574-604,772-775`: local Post sets State/PostedUtc/Settlement and emits an event; payment updates PaidTotal/DueTotal/State; `MarkSettlement` exists but has no production caller in the reviewed `src` tree.
- `src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs:32-39`: allocates from local counters; `Persistence.PostgreSql/Repositories/InvoiceNumberAllocator.cs:8-12` allocates from server counters.

**Failure construction.** A local start/post/payment succeeds and commits its business changes together with an outgoing command. A later server rejection becomes `PendingCommand.Status=Rejected`; it does not invalidate the local Visit, remove its ready handoff, restore the Invoice's authoritative Draft state, undo local stock/drawer/finance effects, or reclassify already written success history rows. Follow-up UI queries can continue reading provisional state as real work. A rejected creation cannot be corrected by pulling a server row that does not exist, and full snapshots intentionally do not delete absent local typed records (`ReplicaMaterializer.cs:35-36`).

For financial state D04 prevents authoritative pull repair even where a server Invoice does exist. Reversal/rollback of domain effects is not performed by `FinancialEffect.NotApplied`; that flag is merely stored in the journal.

**Accepted-result divergence.** The local and server invoice counters are independent. Two devices can both display their own first allocated invoice number while the server allocates distinct tenant-wide sequence numbers; processing order or existing counters can also make A's local number differ from its server number. `PostInvoiceCommand.cs:85-89` assigns the number in each store, but the settlement contract does not carry it and the invoice is excluded from pull. Paid/posted timestamps and locally generated financial child IDs likewise are not rehydrated by the settlement.

**Qualification.** Some **read models** combine journal status with invoice details, so “the client does not apply the response anywhere” is false. The defect is absence of business-state reconciliation, not absence of journal persistence.

### D06 — Prerequisite operations are omitted from visit and payment command metadata

**Locations**

- `src/Animora.Client.Services/Visits/VisitEncounterService.cs:85-103`: `BuildCommandContextAsync(operationId, null, null, ...)` for visit mutations/completion.
- `src/Animora.Client.Services/Visits/VisitsTriageService.cs:77-86`: same null dependency for start.
- `src/Animora.Client.Services/Sales/SalesQueryService.cs:340-347`: every sales command gets a fresh context with no dependency.
- `src/Animora.Client.Services/Payments/InvoicePaymentCore.cs:69-87`: local Post → reread → RegisterPayment; a **local** failed Post stops the sequence.
- `src/Animora.Application/Behaviors/CommandJournalBehavior.cs:47-61`: copies context `DependsOnOperationId` into the envelope; it does not infer one from aggregate/payload references.
- `src/Animora.Application/Commands/LocalCommandProcessor.cs:103-111`: dependency guard only acts if the field is nonempty.

**Failure construction.** The local visit exists provisionally after start, so Rx/vitals/notes/completion succeed in SQLite even after server start rejection. Their journal envelopes contain no prerequisite operation. Sequential `LocalSequence` submission preserves ordering but does not imply “skip if creation was rejected”; each mutation is still sent and correctly gets `VISIT_NOT_FOUND`.

For payment, the local Post returns success before the server evaluates stock. RegisterPayment is committed with no dependency on that Post operation. The server rejects Post and then receives RegisterPayment against its still-Draft Invoice. This is not a missing `if (posted.IsFailure)` statement: that local check is present. It is a missing **authoritative dependency/settlement relationship** across the local/server boundary.

A dependency field alone would not constitute a complete failure-resolution design either: the current processor defers a rejected prerequisite indefinitely. This report identifies the missing relationship, not a proposed patch.

### D07 — Different stock policies make the post/payment divergence predictable

**Locations**

- `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs:31-35`: `AllowNegativeStockOnSale = true` by default.
- `src/Animora.Persistence.Sqlite/Repositories/SalesPostingData.cs:15-28`: local configured policy and local snapshot/receipt cost.
- `src/Animora.Persistence.PostgreSql/Repositories/SalesPostingData.cs:14-22`: `AllowNegativeStock => false`; on-hand is calculated from server StockMovements under a stock resource lock.
- `src/Animora.Application/UseCases/Sales/PostInvoiceCommand.cs:62-89`: shared preflight returns `STOCK_WOULD_GO_NEGATIVE` before number allocation/posting when negative stock is not permitted.

**Failure construction.** With zero/incomplete local stock, local default permits a stock sale and commits the invoice as Posted. PostgreSQL with zero stock rejects the same Post. Local receipt cost/availability can also be stale because stock movements are excluded from pull (D04). The rejected post/payment pair in the report matches this mechanism, although the actual runtime local option value was not supplied.

Allowing provisional offline posting is a stated product decision; that alone is not necessarily a defect. The real correctness failure is allowing the authoritative policy mismatch to flow into payment/success/history without D05/D06/D09 reconciliation. The **server negative-stock guard is not the defect**.

### D08 — Sales/Cashier payment actions do not consult posting authority

**Locations**

- `src/Animora.Desktop/Pages/Sales/SaleInvoiceViewModel.cs:63-65`: `ShowPayCta = !detail.IsLocked && detail.Lines.Count > 0`, independent of permission.
- `src/Animora.Desktop/Pages/Sales/SalePageView.axaml:978-980`: binds visibility to `SelectedInvoice.ShowPayCta`.
- `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs:544-554`: TakePayment only checks selected invoice and opens the modal; its constructor at lines 71-73 has no capability gate.
- `src/Animora.Desktop/Pages/Sales/PaymentModalViewModel.cs:288-289,325-342`: CanPay checks split amounts, not sales authority; payment service may internally post.
- `src/Animora.Desktop/Pages/Cashier/CashierPageViewModel.cs:514-538`: Pay checks selection/amount and relies on backend failure after action.
- `src/Animora.Application/UseCases/Sales/PostInvoiceCommand.cs:19` and `RegisterPaymentCommand.cs:18`: require `PermissionCodes.SalesPost` in the local pipeline.

**Failure construction.** A Doctor can draft/read invoices and obtain an unlocked draft with lines. The UI shows a payment/post CTA. Invoking it dispatches PostInvoiceCommand and hits local RBAC `PERMISSION_DENIED`. Repeated clicks repeat the same failed local attempt. The shown three RBAC failures do not establish that any forbidden Post reached the server.

This is an affordance/capability mismatch, **not an RBAC bypass**. The supplied report's claim of a separate Post button and a server-side failure is too specific for the shown trace; the actual source has pay-on-post behavior. Settlement expiry is also not equivalent to permission grant.

### D09 — Terminal failures can disappear behind a healthy synchronization status

**Locations**

- `src/Animora.Application/Commands/LocalCommandProcessor.cs:94-96`: increments `settled` after any non-deferred result successfully persisted, including Rejected/Conflict; only a submission failure increments `Failed`.
- `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs:71-90,104-122`: error depends on pass failures; RetriableCount subtracts Rejected/Conflict/NeedsAttention; MapState can return `Synced` when only terminal failed work remains.
- `src/Animora.Desktop/Shell/TopbarViewModel.cs:195-209`: maps `Synced` to “all up to date” or “N changes sent and synchronized”.
- `src/Animora.Persistence.Sqlite/Entities/PendingCommand.cs:84-87`: does preserve detailed error/effect/role.
- `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs:123-142` and `Client.Services/Sales/SalesMapping.cs:335-358`: partial invoice-detail verdict and role presentation exist.

**Failure construction.** All queued commands receive HTTP 200 authoritative rejections, which are persisted successfully. Processor reports no transport failure; the retriable badge becomes zero; a healthy pull returns UpToDate. The combined state is `Synced`, although the clinical/financial workflow did not settle successfully. Users can interpret this as confirmation that the other cashier received their work.

Excluding terminal rows from a “sendable backlog” is reasonable, but there is no separate terminal-failure signal in this status contract/presentation. The invoice chip can show a rejection on a later detail reread, so the report's universal “role is never read or displayed” claim is wrong. Visit start/Rx/complete failures have no comparable actionable presentation in the reviewed workflow, and D14 can keep an already open invoice chip stale.

The returned `Manager` is also a generic fallback: `src/Animora.Server/Commands/ErrorResponsibility.cs:7-15` lists several explicit codes and returns Manager for everything else. It must not be interpreted as a more precise clinical diagnosis.

### D10 — Owner validation permits a required phone to become empty after normalization

**Locations**

- `src/Animora.Application/UseCases/Patients/CreateOwnerCommand.cs:34-40`: requires nonblank **raw** phone only.
- `src/Animora.Application/UseCases/Patients/UpdateOwnerCommand.cs:34`: same raw required check.
- `src/Animora.Domain/Patients/Owner.cs:84-95,116-121,156-178`: checks raw nonblank input, then retains only ASCII/Persian/Arabic-Indic digits; does not require a nonempty normalized result.
- `src/Animora.Persistence.PostgreSql/Configurations/OwnerConfiguration.cs:9-18`: no normalized-phone nonempty check is configured here.
- `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs:99-104,374-380`: PhonePrimary is required; an empty string invalidates the whole typed Owner upsert.

**Failure construction.** A phone such as `"---"` or `"abc"` passes raw required checks. `Owner.NormalizePhone` returns `""`, and register/update stores it. A server row can therefore be created by normal shared command/domain paths that the client materializer subsequently refuses. This is a concrete producer/consumer invariant mismatch, not just hypothetical direct SQL damage.

**Incident qualification.** The supplied row has `phonePrimary=""`, so this check alone is sufficient to skip it. Its payload is truncated; another required timestamp could also be invalid/missing. The exact original input/writer for Owner `01M4X0KD...` is not known. It could be the normalization path or another import/legacy/manual write; this review does not assert which happened. Null nationalId/address/email are not required-field failures. Skip leaves a previously existing typed row unchanged rather than proving total absence.

### D11 — Skipped materialization consumes the feed position without replaying typed state

**Locations**

- `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs:265-282`: skips roots covered by pending local commands.
- Same file `:296-319`: skips missing-required-field/constraint failures.
- Same file `:362-369`: overlay check covers only LocallyPosted/PendingSettlement commands with **nonnull AggregateId**.
- `src/Animora.Sync.Custom/Replica/SqliteSyncApplyStore.cs:147-175`: upserts raw base, advances checkpoint, materializes staging, deletes staging, commits; skipped typed writes do not fail the batch.
- `src/Animora.Sync.Custom/IReplicaVersionReader.cs`, `SqliteReplicaVersionReader`: reads the active base version, not a recovery/materialization pass.

**Failure construction.** A server root reaches raw replica storage while an unsettled local mutation overlays it. The typed write is skipped, but the cursor advances beyond that change and the staged row is deleted. If the pending mutation later rejects, journal settlement does not reproject the already stored base row (D05). A subsequent unchanged-server UpToDate response contains no row to apply. The typed UI data can remain stale indefinitely until another server change or a full snapshot happens to revisit it.

The same mechanism consumes a poisoned Owner/constraint-clashing row without a durable typed-projection retry path. Server correction would generate a new change and may recover it; absent that, “sync succeeded” is not convergence. Overlay avoidance protects local intent while pending, but it needs an eventual settlement/materialization connection that this implementation lacks.

**Creation edge.** Creation commands declare null AggregateId (`StartVisitCommands.cs:134-136`, `CreateInvoiceFromVisitCommand.cs:23-25`), so they cannot protect their created entity via this nonnull-key overlay lookup. This is another reason journal-key checks are not a complete business overlay model. No specific creation overwrite is claimed from the excerpts.

### D12 — Cashier History and general History are different local projections, neither a complete shared event history

**Locations**

- `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs:199-238`: cashier ledger unions local Payment rows and non-Draft current Invoice rows.
- `src/Animora.Client.Services/Sales/SalesQueryService.cs:149-165`: consumes that local ledger.
- `src/Animora.Persistence.Sqlite/Queries/ActivityFeedQueries.cs:24-50`: general History reads device-local ActivityEvent.
- `src/Animora.Application/Activity/ActivityFeedHandler.cs:9-15` and `Persistence.Sqlite/Activity/ActivityFeedWriter.cs:14-25`: local domain events create activity rows alongside local commands.
- `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs:323-351`: the only incoming activity synthesis is AppointmentBooked.
- `src/Animora.Persistence.Sqlite/Activity/SyncedActivityFeed.cs:18-40`: incoming appointment history deduplicates by entity/type/title, not by an authoritative server event stream.

**Failures and distinctions.**

1. **Cross-device invoice/payment history is absent:** financial source rows are excluded (D04), and ActivityEvent is not in the feed catalog. A peer cannot reconstruct financial events from the incoming Visit root.
2. **General incoming activity coverage is incomplete:** applying a remote completed Visit/medical-history record does not emit a VisitCompleted ActivityEvent. Apart from the appointment-booked special case, raw SQL upserts bypass the local domain-event pipeline. A medical-history entry being present is not the same as a complete general activity feed.
3. **Rejected local success can remain in history:** local Post/Payment/Complete raises success events before server settlement; D05 does not mark those ActivityEvents rejected or reverse the local financial projection. The user can see “done/paid” entries for work the server did not apply.
4. **Cashier ledger is not an immutable event timeline:** its invoice leg computes Kind from **current** `i.State`, with timestamp `i.PostedUtc ?? i.IssuedUtc`. After Void, the same formerly Accepted row becomes Void at the original posting/issue timestamp; the original acceptance is not retained as a separate event, nor is a distinct void-time event emitted by this query. Both `Persistence.Sqlite/Queries/SalesQueries.cs:223-236` and `Persistence.PostgreSql/Repositories/SalesQueries.cs`, `GetHistoryAsync`, use this current-state construction.
5. **Remote appointment “booked time” is synthetic:** `TryBuildBookedDraft` uses `row.UpdatedAtUtc ?? start`. Pulling an appointment after subsequent edits/rescheduling can timestamp the reconstructed booking at the latest root update, not the original booking. Title-based dedup then preserves that synthesized entry. This does not prove corruption in these incident sessions, but it limits how that history can be interpreted.

The corrected UNION query in SalesQueries should not be re-reported as a known EF translation defect merely because an older comment describes one. The structural absence/misclassification above is visible without running EF.

### D13 — General History financial summary double-counts a sale and its receipt

**Locations**

- `src/Animora.Application/Activity/ActivityFeedMapper.cs:59-65`: InvoicePosted creates a positive amount of GrandTotal; PaymentRegistered creates another positive amount of the receipt.
- `src/Animora.Persistence.Sqlite/Queries/ActivityFeedQueries.cs:55-59,69-75`: sums every positive AmountSign as `Income`/day income, without distinguishing accrual sale from collection.
- `src/Animora.Domain/Sales/Invoice.cs:538-547` and `ApplyPayment`: raises both events on the normal local post-and-pay path.

**Failure construction.** Post invoice for 100 units, then collect its full 100-unit payment. Two Sale activity rows have positive financial signs. A general History filter including both reports Income=200 and day net=200, even though the clinic sold/received only 100 units. Partial payment of 40 yields 140. This is a numerical semantic defect of the mixed event summary, not merely cross-device missing rows.

**Scope.** This finding applies to **general ActivityFeed History totals**, not automatically to the cashier ledger totals: the latter intentionally assigns AmountMinor=0 to its Invoice leg (`SalesQueries.cs:233-234`) and sums Payment amounts. It is not evidence that the server accounting JournalEntry itself is double-posted. No such ledger-entry claim is made.

### D14 — Background sync does not publish business-page invalidation

**Locations**

- `src/Animora.Client.Services/Sync/SyncOrchestrator.cs:55-66`: after apply/up-to-date, records cursor time and returns; no business-change publication.
- `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs:125-128`: publishes SyncState only.
- `src/Animora.Desktop/Shell/TopbarViewModel.cs:212-227`: sync event refreshes topbar presentation, not business pages.
- `src/Animora.Desktop/Services/DataChangeNotifier.cs:14-42`: distinct Desktop notification channel.
- `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs:94-103,737-755`: refresh listens to local DataDomain notifications or navigation.
- `src/Animora.Desktop/Pages/Visits/VisitEncounterViewModel.cs:424-427`: local completion explicitly notifies affected domains, showing the separate path used for local work.

**Failure construction.** A page loads and stays open while a peer's successfully replicated data changes its local database. Background apply/cursor/status events do not invoke the DataChangeNotifier subscriptions driving the page's collections. The database can be updated while the visible page remains unchanged until navigation or a page-specific reload. Likewise, a persisted invoice rejection need not update an already rendered chip.

This is additional to D02/D04. Reloading the page cannot reveal an invoice/handoff that was never materialized; where a row is correctly materialized, missing invalidation explains a different stale-UI symptom.

### D15 — Last-command settlement can mask an earlier rejected prerequisite

**Locations**

- `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs:123-142`: chooses the highest LocalSequence command whose AggregateId is invoice ID or whose OperationId is invoice creation operation, then derives the whole invoice-detail settlement verdict from that one row.
- `src/Animora.Client.Services/Sales/SalesMapping.cs:335-358`: renders the derived verdict/error/responsibility in the state label.

**Failure construction.** Invoice A has an earlier rejected Post and then a later pending or locally successful command. While the later command remains pending, the detail can display “waiting” instead of the already actionable Post rejection; a later accepted command on a server-Draft invoice can display “server settled” while the earlier attempted posting remains rejected. The query filters neither command type nor unresolved prerequisite failures, despite a comment describing the “post journal entry” as the authoritative source.

The actual C2 sequence ends with a rejected payment, so this specific accepted-later mask is not established in the incident. However, even there the latest payment error `INVOICE_INVALID_TRANSITION` can displace the earlier root-cause `STOCK_WOULD_GO_NEGATIVE` in the displayed invoice error. This is a loss of causal diagnosis, not loss of the durable journal entries themselves.

### D16 — Sales service suppresses several real failures

**Locations: `src/Animora.Client.Services/Sales/SalesQueryService.cs`**

- `:46-55,63-68,88-92,149-159`: failed reads yield empty lists/null/empty history page.
- `:194-206`: AddLine/AddCustomLine discard the mutation result, then return the reread invoice.
- `:227-253`: SetDiscount/SetInsurance/PickOwner discard command failures and return current detail.
- `:257-264`: DeleteInvoice discards delete failure and returns null as though the row was removed.
- `:284-291`: Void discards failure and returns current detail.
- Compare `:211-224`: EditLine/RemoveLine explicitly propagate failures via ServiceException.

**Failure construction.** A permission, validation, immutable-state, or storage failure occurs in a local command. Several methods return unchanged detail with no explicit error, so a caller can refresh/notify as though mutation succeeded. Delete can return the success-shaped “gone” outcome while the row still exists. On reads, authorization/store failure can look indistinguishable from “no invoices/history”.

These are independent of asynchronous server rejection: they suppress the **immediate local Result** itself. `CreateFromEncounterAsync` also tries the boarding branch after any unsuccessful visit-create result, not only NotFound; its final generic `INVOICE_NOT_FOUND` exception loses the original error (`:170-181`). No specific incident command is claimed to have hit this additional local failure path.

### D17 — Server feed fault isolation turns malformed live rows into deletion records

**Locations**

- `src/Animora.Persistence.PostgreSql/Replication/RecordProjector.cs:63-67`: missing/unparseable Version or Deleted emits `new(..., 'D', null, null, true, null)`.
- Same file `:77-86`: any exception during payload projection also emits a deletion record.
- `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs:287-292`: null payload soft-deletes the typed local root.

**Failure construction.** A PostgreSQL row exists and has not been business-deleted, but its schema/serialized data does not satisfy the projector, or `Project` throws. The server translates this uncertainty into an authoritative-looking tombstone. The client applies it by setting Deleted=1, hiding the live record, and advancing the checkpoint. No actual deletion instruction was demonstrated; a projection failure has been conflated with one.

This is a **conditional server-side data correctness defect**, not the same as D10. The supplied Owner payload is an upsert containing an empty phone and was skipped client-side, so it is not evidence that this tombstone fallback ran during the incident. Also, the current projector tolerates an unparseable UpdatedAt by sending a null cursor (`:69-71`); do not re-report a fixed `-infinity` cursor exception as this finding's trigger.

### D18 — Outgoing command actor is rebound to the current same-clinic session

**Locations**

- `src/Animora.Application/Security/SessionCurrentUserProvider.cs`, `NewContext`: records the local session user as command actor.
- `src/Animora.Persistence.Sqlite/Commands/PipelineSql.cs:48-64`: claim SQL has no originating-user predicate; it claims eligible persisted commands in local sequence order.
- `src/Animora.Sync.Custom/HttpCommandTransport.cs:148-179`: replaces outgoing actor/device with the **currently stored online session**, regardless of the original journal actor.
- `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs:68-71`: same-tenant login preserves the existing store/journal.
- `src/Animora.Client.Services/Identity/AuthenticationService.cs:45-64`: sign-out clears session authority; it does not delete queued business commands.
- `src/Animora.Server/Commands/CommandIntakeService.cs:47-55`: server compares envelope identity to the current bearer membership and uses that membership as authority.

**Conditional failure construction.** A makes local same-clinic changes while uploads cannot complete, then signs out. B signs into the same device/clinic, leaving A's queued journal intact. The next pass claims A's commands and sends them with B's server UserId/DeviceId. Server auditing/CreatedByUserId and RBAC evaluate B, not the original author. If B has the necessary rights, the server can accept A's intent attributed to B; if B lacks rights, previously legitimate work can reject for the new actor.

This is not proof of cross-tenant data leakage: different-tenant binding has explicit pending-work checks and table cleanup, and the failure here requires a **same-tenant user switch**. Nor is it shown by two users on two separate devices in the provided incident. It is a directly related source-confirmed attribution risk of the current identity rebinding boundary.

## 6. Reconstructed causal paths

### 6.1 The reported Doctor session

```text
verified server login (server User S)
  -> local user absent/invalid verifier
  -> verified local mirror with a new local User L
  -> triage rail returns L as doctor ID
  -> local StartNewVisit succeeds for local membership L
  -> upload replaces envelope actor with S, leaves payload.doctorId=L
  -> server rejects start (DOCTOR_NOT_AVAILABLE)
  -> journal marked Rejected, local Visit/queue/owner/pet remain provisional
  -> UI continues local Rx/vitals/notes/complete
  -> no declared prerequisite => mutations uploaded anyway
  -> server rejects seven visit mutations (VISIT_NOT_FOUND)
  -> local completion creates a ready handoff; local invoice creation succeeds
  -> server has no corresponding handoff => invoice creation NOT_FOUND
  -> Doctor payment/post attempt hits local RBAC (PERMISSION_DENIED)
  -> terminal journal results can coexist with a zero backlog / Synced badge
```

The stable Visit ID equaling the start operation ID is not a defect. The invoice-create failure involves its handoff reference, not a direct Visit mutation. Nothing in this chain proves a server stock/payment effect occurred for the Doctor.

### 6.2 The reported Manager post/payment session

```text
local draft invoice/line successfully created
  -> local post allowed (default permits negative stock)
  -> local invoice becomes Posted, local stock/activity effects committed
  -> payment core sees local post success and queues RegisterPayment
  -> server post preflight finds insufficient stock => rejected
  -> authoritative invoice remains Draft
  -> payment is still sent (no post dependency)
  -> server Draft-payment guard rejects INVOICE_INVALID_TRANSITION
  -> local business/payment/history rows are not reconciled by journal settlement
```

The server protected stock and refused payment on an unposted invoice. The defects are provisional-state lifecycle, policy/read divergence, dependency construction, and misleading acceptance presentation.

### 6.3 A valid visit with none of the incident's doctor/RBAC failures

```text
valid authoritative doctor ID; Start/Complete accepted
  -> originating device has handoff H_local
  -> server has independently generated handoff H_server
  -> peer receives Visit.billingHandoffs but ignores the child rows
  -> peer's ready queue is empty (D02)
  -> originating device invoices H_local; server looks up H_local, not H_server
  -> creation rejects NOT_FOUND (D03)
```

This isolates why the cashier problem would survive repairing the incident's doctor reference. If an invoice is created through a path using an actual server-known handoff or as a blank invoice, its financial state still does not reach a peer's local cashier/history tables (D04).

## 7. Behaviors verified as normal or insufficiently proven

### Tenant binding and checkpoint zero

`Client.Services/Identity/OnlineAuthClient.cs:68-79` establishes local tenant binding before publishing the bearer session. `Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs:57-122` handles real tenant transitions, refuses to discard unresolved work, clears old tenant data/checkpoints, initializes tenant reference identity, and updates the cached tenant only after commit. `Sync.Custom/Replica/SqliteSyncApplyStore.cs:67-74` reads by `(TenantId, Scope)` and returns `(0,0,0)` if absent.

Thus the provided old-tenant `12` and clinic-tenant `0` are not a single cursor moving backward. It is appropriate to fetch a clinic snapshot when its checkpoint is absent. The arbitrary old tenant `01M4XNRZ...` must not be assumed to be the built-in default merely because it appears before login on two machines; the excerpts do not explain how those stores acquired it.

### Independent guest drafts and fingerprints

`Application/Commands/CommandFingerprint.cs:62-73` hashes only command type and payload. `Server/Commands/CommandIntakeService.cs:62-79` begins/replays work by the operation identity within tenant, validating fingerprint for a repeated operation. `Application/UseCases/Sales/CreateBlankInvoiceCommand.cs` and `CreateInvoiceFromStayCommand.cs:49-53` explicitly use the operation ID as Invoice.Id. `Desktop/Pages/Sales/SalePageViewModel.cs:758-771` deliberately opens a default guest invoice on initial page load.

Different operation IDs with identical guest draft payloads are legitimate independent requests. The report does not demonstrate a repeated operation causing a second effect, an idempotency-key collision, or a requirement that all guest drafts with identical names be merged.

### Capability TTL and polling

`Domain/Identity/OfflineCapabilityPolicy.cs:14-33` implements 30-day/14-day/72-hour/zero risk windows. `Server/Auth/DeviceAdminService.cs:184-213` builds offline expiry metadata from granted permission codes. A snapshot's expiry entries, the actual grants, stored roles, and the capability evaluator must not be treated as interchangeable evidence.

`Client.Services/Sync/SyncScheduleOptions.cs` sets 30-second interval/minimum and ±20% jitter; `SyncSchedulerHostedService.cs`, `ExecuteAsync`, combines periodic push/pull with backoff. Topbar/manual sync is an additional caller. UpToDate polling is not itself a failure; very short repeated calls need their trigger paths identified in raw logs before a scheduling defect can be alleged.

### Correct guards and already-addressed mechanisms

- `DOCTOR_NOT_AVAILABLE` is the shared membership guard, not an HTTP/Kestrel failure.
- `STOCK_WOULD_GO_NEGATIVE` protects authoritative stock before posting.
- `INVOICE_INVALID_TRANSITION` correctly prevents Draft payment.
- Local `PERMISSION_DENIED` is an RBAC guard, not evidence the permission check is missing.
- `responsibleRole` is present in persistence and invoice-detail presentation; the blanket “never read” finding is contradicted by code.
- Server visit/stay invoice payload ambiguity is already handled by `Server/Handlers/CreateInvoiceFromHandoffCommand.cs:13-36`; the server does not blindly register two indistinguishable payload candidates.
- ReadyBillingProjection uses owner/pet **left joins** at this baseline. Missing remote handoff rows, not that earlier join bug, are the decisive source-level issue here.

## 8. Consequence map and confidence boundaries

| User-visible symptom | Established source causes | What is not proven |
| --- | --- | --- |
| Visit appears completed on Doctor device but not usable by cashier | D01, D05–D06, D09 | That PostgreSQL ever accepted that Visit |
| Successfully completed remote visit absent from ready queue | D02; D14 can additionally leave UI stale | That polling frequency or timezone alone caused it |
| Origin device's visit-derived invoice rejected with NotFound | Missing server handoff after rejection; independently D03 for valid completions | Exact handoff payload of the incident create without full trace |
| Another client cannot see accepted invoice/payment/history | D04, D12; D14 where incoming supported data exists | Deletion/loss of authoritative financial PostgreSQL rows |
| Local paid/posted invoice disagrees with server Draft | D05–D07 | Actual runtime local stock option, beyond the reviewed default |
| “Synced” despite rejected work | D09; D15 can blur root-cause details | User saw no possible invoice warning anywhere |
| Owner skipped on both clients | D10 consumer rule, D11 persistent skip | Original corrupting write or universal absence of prior typed copies |
| History totals/status/times are misleading | D05, D12–D13, D15 | Server accounting journals are themselves double-posted |
| Server and desktop invoice numbers disagree | D04–D05 independent allocators and missing authoritative rehydration | Actual incident numbers, which were not supplied |

The main roots are **missing financial read-state delivery**, **unmaterialized and nondeterministic billing handoffs**, **unmapped user references**, and **journal-only settlement of optimistic business effects**. These are code-level integration defects, not problems explained away by timezone differences or different batch counts.

This document deliberately does not prescribe fixes. It records the responsible source locations, invariants that disagree, necessary trigger conditions, and observable failure construction. Runtime reproduction and deployed-revision verification remain outside this static-only review, as requested.
