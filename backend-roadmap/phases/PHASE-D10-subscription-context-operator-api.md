# Phase D10 — Subscription context & operator API

**Band:** M · **Track:** D · **Depends on:** D04 · **Satisfies:** R-B05, R-B30
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §4, §5, §7, §9, §12
- `backend-roadmap/PROGRESS.md` (D04 entry — `RequireOperator`, `HttpTenantContext`,
  `ITenantContextSetter`, `AnimoraPolicies`, `X-Animora-Tenant`; D03 entry — `AnimoraUser`,
  `AnimoraRole`, `ServerBootstrapState`, `IServerAuditWriter`, `IdentityRoleSeeder`; D02 entry —
  `AnimoraServerDbContext`, `ServerModel.NonTenantScopedTables`, `TenantProvisioner`; D05 entry —
  `IServerUnitOfWork`; D09 entry — `IServerOutbox`, `OperatorAlert`, `IServerStoreMaintenance`,
  `ProjectionRebuildWorker`)
- `ARCHITECTURE.md` §13.1, §13.3, §6 (context map row for Subscription) · `STACK.md` §5.3
- `A-RULE 012`, `A-RULE 014`, `A-RULE 023`, `A-RULE 025`
- `src/Animora.Domain/Identity/Tenant.cs`, `src/Animora.Server/Security/AnimoraPolicies.cs`,
  `src/Animora.Persistence.SqlServer/Context/ServerModel.cs`

## Goal
The commercial model becomes a real bounded context: plans, subscriptions, seats, billing periods and
grace states with an append-only event log, plus an operator-scoped API that can create / extend /
reduce / suspend / cancel / renew a subscription, manage seats, and run the operator maintenance
actions earlier phases deferred here. Nothing in the runtime entitlement path depends on it — that
separation is the point of this phase (`ARCHITECTURE.md` §13.1, R-B30).

## Design decisions (pre-made — implement, do not re-derive)
- **Subscription aggregates live in `src/Animora.Domain/Subscription/`** (Domain has zero
  dependencies, so both the server and — later — a licence verifier can share the vocabulary).
  Mapping is `Animora.Persistence.SqlServer`; behavior is `Animora.Server`. The desktop never
  references these types.
- **Entities and value objects (exact shapes):**
  | Type | Kind | Members |
  |---|---|---|
  | `Plan` | `AggregateRoot` (syncable: no — `[LocalOnly]`-equivalent server table, not replicated) | `Code` (e.g. `clinic.basic`), `DisplayName`, `IncludedSeats` (int), `FeatureFlags` (`IReadOnlySet<string>` from `PlanFeatures`), `PriceMinor` (`long`), `Currency`, `BillingCycle`, `IsRetired` |
  | `Subscription` | `AggregateRoot` | `TenantId`, `PlanCode`, `SubscriptionStatus Status`, `SeatLimit`, `StartsAtUtc`, `CurrentPeriodId`, `GraceEndsAtUtc?`, `CancelledAtUtc?`, `CancellationReason?` |
  | `BillingPeriod` | entity | `SubscriptionId`, `Sequence` (int, 1-based), `StartsAtUtc`, `EndsAtUtc`, `BillingPeriodStatus Status`, `AmountMinor`, `Currency` |
  | `Seat` | entity | `SubscriptionId`, `TenantId`, `UserId?`, `SeatStatus Status`, `AssignedAtUtc?`, `ReleasedAtUtc?` |
  | `SubscriptionEvent` | append-only entity | `SubscriptionId`, `TenantId`, `Sequence` (`long`, gap-free per subscription), `EventType` (`SubscriptionEventTypes`), `OccurredAtUtc`, `ActorUserId`, `PayloadJson`, `Reason?` |
  | `PlanFeatures` | static catalog | `const string` feature keys: `boarding`, `purchasing`, `cheques`, `multi_device`, `insurance_billing`, `advanced_reports` |
  Enums: `SubscriptionStatus { Trial, Active, PastDue, Grace, Suspended, Cancelled, Expired }`,
  `BillingCycle { Monthly, Quarterly, Yearly }`,
  `BillingPeriodStatus { Open, Settled, Waived, Cancelled }`,
  `SeatStatus { Free, Assigned, Revoked }`.
- **The lifecycle is one state machine, one file.**
  `src/Animora.Domain/Subscription/SubscriptionTransitions.cs`, a static allow-table mirroring
  B02's `SettlementTransitions` style — the only authority, consulted by every operation:
  ```
  Trial     -> Active | Cancelled | Expired
  Active    -> PastDue | Suspended | Cancelled | Active (renew)
  PastDue   -> Grace | Active | Suspended | Cancelled
  Grace     -> Active | Suspended | Expired
  Suspended -> Active | Cancelled
  Cancelled -> terminal
  Expired   -> Active (reactivate) | terminal
  ```
  `static Result EnsureAllowed(SubscriptionStatus from, SubscriptionStatus to)` →
  `SUBSCRIPTION_INVALID_TRANSITION`. `GraceState` is **not** a separate entity: it is
  `Status == Grace` + `GraceEndsAtUtc`, so there is exactly one truth.
- **Grace and expiry numbers are policy, declared once** in
  `src/Animora.Domain/Subscription/SubscriptionPolicy.cs`:
  `GraceDays = 14`, `TrialDays = 30`, `PastDueDays = 7` (PastDue → Grace after this),
  `RenewalWindowDays = 30` (how early a renewal may be booked),
  `SeatOverAssignmentAllowed = false`. A test asserts no other file contains these literals.
- **Operator operations are NOT tenant business commands.** They never enter `/api/commands`
  (that pipeline is tenant-scoped and device-bound). They are direct, operator-authorized service
  calls behind one application-layer port:
  `src/Animora.Application/Subscription/ISubscriptionOperations.cs` —
  `Task<Result<SubscriptionView>> CreateAsync(CreateSubscriptionRequest r, OperatorContext ctx, CancellationToken ct)`
  plus `ExtendAsync`, `ReduceSeatsAsync`, `IncreaseSeatsAsync`, `SuspendAsync`, `ResumeAsync`,
  `CancelAsync`, `RenewAsync`, `AssignSeatAsync`, `ReleaseSeatAsync`.
  `sealed record OperatorContext(string OperatorUserId, string CorrelationId, string RequestOperationId)`.
  Implementation: `src/Animora.Server/Subscription/SubscriptionOperationsService.cs`.
- **Every operation is idempotent and audited, without reusing D05's tenant idempotency store.**
  Each request carries a client-generated `RequestOperationId` (header
  `X-Animora-Operation` — documented contract, mandatory). The service writes a
  `SubscriptionEvent` whose `(SubscriptionId, EventType, RequestOperationId)` is **unique**; a
  replay returns the stored `SubscriptionView` instead of applying a second effect (A-RULE 025 shape,
  reused mental model). `IServerAuditWriter` records every operation with actor + before/after status.
- **`SubscriptionEvent.Sequence` is gap-free per subscription**, allocated inside the same
  transaction (`IServerUnitOfWork.ExecuteAsync`) by a `SELECT MAX(Sequence) + 1 ... WITH (UPDLOCK)`
  read. The log is append-only: `ServerAppendOnlyGuard` (D07) gains `SubscriptionEvents` to its
  protected table list, so an `Update`/`Delete` throws.
- **`SubscriptionEventTypes`** (`const string`, `<aggregate>.<verb>` like `CommandTypes`):
  `subscription.created`, `subscription.extended`, `subscription.renewed`,
  `subscription.seats.increased`, `subscription.seats.reduced`, `subscription.suspended`,
  `subscription.resumed`, `subscription.cancelled`, `subscription.expired`,
  `subscription.grace.entered`, `subscription.pastdue.entered`, `seat.assigned`, `seat.released`,
  `plan.changed`.
- **Published integration events go through D09's outbox** (never an in-process call into another
  context): `PlanChanged` (`MessageType` `subscription.plan.changed`) and `SeatLimitReached`
  (`subscription.seat.limit.reached`), enqueued with `IServerOutbox.EnqueueAsync` in the same
  transaction. D11 consumes `subscription.plan.changed` to trigger a licence reissue; `AlertConsumer`
  (D09) already turns `subscription.seat.limit.reached` into an `OperatorAlert`.
- **Seat enforcement is a read the identity path performs, not a lock.**
  `src/Animora.Application/Subscription/ISeatGuard.cs` →
  `Task<Result> EnsureSeatAvailableAsync(string tenantId, string userId, CancellationToken ct)`,
  implemented by `SeatGuard` in `Animora.Server`. `D03`'s `DeviceRegistrationService` call site is
  **not** edited in this phase; instead `SeatGuard` is invoked from
  `src/Animora.Server/Endpoints/IdentityEndpoints.cs`'s device-register handler via a single added
  line, returning `SEAT_LIMIT_REACHED` and enqueuing the `seat.limit.reached` message. Assignment is
  first-come: a `Seat` row moves `Free → Assigned` with the user id, under the row's `rowversion`.
- **Operator accounts.** D03 created only tenant users. This phase adds
  `src/Animora.Server/Subscription/OperatorAccountService.cs`:
  - the `Operator` role is seeded by extending `IdentityRoleSeeder`'s role list (one added entry);
  - the **first** operator is bootstrapped from the environment variable
    `ANIMORA_OPERATOR_BOOTSTRAP` (`email:password`, read once, never logged, cleared from the
    options snapshot after use) and recorded in `ServerBootstrapState.OperatorBootstrapped` so it
    cannot run twice;
  - an operator has **no** `StaffMembership`, therefore `HttpTenantContext` stays null for it —
    exactly what `RequireOperator` (D04) demands.
  An operator can create further operators through `POST /api/operator/operators`.
- **Endpoints** (`src/Animora.Server/Endpoints/OperatorEndpoints.cs`, prefix `/api/operator`, every
  route `.RequireAuthorization(AnimoraPolicies.RequireOperator)`, all failures `WireApiError`):
  | Route | Body / result |
  |---|---|
  | `GET /api/operator/tenants` | `PagedResult<TenantSummaryView>` (id, name, status, plan, seats used/limit, subscription status) |
  | `POST /api/operator/tenants` | `CreateTenantRequestV1` → provisions via `TenantProvisioner` + creates a `Trial` subscription |
  | `GET /api/operator/subscriptions/{tenantId}` | `SubscriptionView` + its periods + seats |
  | `POST /api/operator/subscriptions/{tenantId}/extend` | `ExtendSubscriptionRequestV1(int Periods)` |
  | `POST /api/operator/subscriptions/{tenantId}/renew` | `RenewSubscriptionRequestV1` |
  | `POST /api/operator/subscriptions/{tenantId}/seats` | `ChangeSeatsRequestV1(int SeatLimit, string Reason)` — increase or reduce |
  | `POST /api/operator/subscriptions/{tenantId}/suspend` · `/resume` · `/cancel` | `SubscriptionActionRequestV1(string Reason)` |
  | `POST /api/operator/subscriptions/{tenantId}/seats/{seatId}/assign` · `/release` | seat moves |
  | `GET /api/operator/subscriptions/{tenantId}/events` | `PagedResult<SubscriptionEventView>` (the append-only log) |
  | `GET /api/operator/plans` · `POST /api/operator/plans` | plan catalog read + create/retire |
  | `GET /api/operator/alerts` | `PagedResult<OperatorAlertView>` over D09's `OperatorAlerts` |
  | `POST /api/operator/tenants/{tenantId}/maintenance/rebuild-projections` | raises the `projection.drift` request D09's `ProjectionRebuildWorker` waits for |
  | `POST /api/operator/tenants/{tenantId}/periods/{periodId}/reopen` | the operator action D07 deferred here: writes an audited `AccountingPeriod` reopen through `IServerStoreMaintenance` |
  **Reducing seats below the number currently assigned is rejected** (`SEAT_REDUCTION_BLOCKED`) —
  the operator must release seats first. Reducing is never silently destructive.
- **Views are wire DTOs, never entities** (A-RULE 014), in
  `src/Animora.Contracts.Wire/Operator/`: `SubscriptionView`, `TenantSummaryView`, `SeatView`,
  `BillingPeriodView`, `SubscriptionEventView`, `OperatorAlertView`, `PlanView`, plus the five
  `*RequestV1` records above. `WireContract.Version` stays `1` (additive-only).
- **Cross-tenant reads are legal here and nowhere else.** `SubscriptionOperationsService` and the
  operator queries run with **no** tenant context and must therefore disable D02's `Tenant` named
  filter explicitly per query (`IgnoreQueryFilters(nameof(ServerQueryFilters.Tenant))`-style call,
  `SoftDelete` stays on). A test asserts the operator queries are the **only** call sites that
  disable the tenant filter, and that none of them touch a clinical/financial table
  (`ARCHITECTURE.md` §13.3: Subscription references a tenant, it does not reach into its data).
- **Migration `108_Subscription`** — `Plans`, `Subscriptions`, `BillingPeriods`, `Seats`,
  `SubscriptionEvents`, and one row added to `ServerBootstrapState`. Indexes:
  `UX_Subscriptions_TenantId` (unique — one subscription per tenant),
  `UX_SubscriptionEvents_Subscription_Type_Request` (unique, the idempotency key),
  `IX_SubscriptionEvents_SubscriptionId_Sequence` (unique),
  `IX_Seats_SubscriptionId_Status`, `IX_BillingPeriods_SubscriptionId_Sequence` (unique),
  `IX_Subscriptions_Status_GraceEndsAtUtc` (the sweep). All five tables go into
  `ServerModel.NonTenantScopedTables` (they are operator-owned, not tenant-filtered) **and** into the
  non-replicated list — no subscription row is ever replicated to a client.
- **`SubscriptionLifecycleWorker`** (`src/Animora.Server/Workers/`, daily, uses D09's
  `WorkerBulkhead`): advances `Active → PastDue` after a period's `EndsAtUtc`,
  `PastDue → Grace` after `PastDueDays`, `Grace → Suspended` after `GraceEndsAtUtc`, and writes a
  `SubscriptionEvent` + outbox message for each transition. It **never** deletes anything and never
  reactivates — reactivation is always an operator action.
- **New error codes:** `SUBSCRIPTION_NOT_FOUND`, `SUBSCRIPTION_INVALID_TRANSITION`,
  `SEAT_LIMIT_REACHED`, `SEAT_REDUCTION_BLOCKED`, `PLAN_NOT_FOUND`, `PLAN_RETIRED`,
  `OPERATOR_BOOTSTRAP_ALREADY_DONE`, `PERIOD_REOPEN_DENIED`
  (`OPERATOR_SCOPE_REQUIRED` exists from D04 — reuse).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Subscription/Plan.cs` | create | plan aggregate + `PlanFeatures` |
| `src/Animora.Domain/Subscription/Subscription.cs` | create | subscription aggregate |
| `src/Animora.Domain/Subscription/BillingPeriod.cs` | create | period entity + status enum |
| `src/Animora.Domain/Subscription/Seat.cs` | create | seat entity + status enum |
| `src/Animora.Domain/Subscription/SubscriptionEvent.cs` | create | append-only event + `SubscriptionEventTypes` |
| `src/Animora.Domain/Subscription/SubscriptionTransitions.cs` | create | the allow-table |
| `src/Animora.Domain/Subscription/SubscriptionPolicy.cs` | create | grace/trial/renewal numbers |
| `src/Animora.Application/Subscription/ISubscriptionOperations.cs` | create | operations port + requests + `OperatorContext` |
| `src/Animora.Application/Subscription/ISeatGuard.cs` | create | seat availability port |
| `src/Animora.Contracts.Wire/Operator/*.cs` | create | 7 views + 5 `*RequestV1` records |
| `src/Animora.Persistence.SqlServer/Configurations/Subscription/*.cs` | create | 5 EF configurations |
| `src/Animora.Persistence.SqlServer/Migrations/108_Subscription*` | create | 5 tables + indexes |
| `src/Animora.Persistence.SqlServer/Context/ServerModel.cs` | modify | add the 5 tables to non-tenant-scoped + non-replicated |
| `src/Animora.Persistence.SqlServer/Context/ServerAppendOnlyGuard.cs` | modify | protect `SubscriptionEvents` |
| `src/Animora.Server/Subscription/SubscriptionOperationsService.cs` | create | the 10 operations, idempotent + audited |
| `src/Animora.Server/Subscription/SubscriptionQueries.cs` | create | operator read projections |
| `src/Animora.Server/Subscription/SeatGuard.cs` | create | seat enforcement |
| `src/Animora.Server/Subscription/OperatorAccountService.cs` | create | operator role + bootstrap |
| `src/Animora.Server/Workers/SubscriptionLifecycleWorker.cs` | create | past-due / grace / suspend sweep |
| `src/Animora.Server/Endpoints/OperatorEndpoints.cs` | create | the operator API surface |
| `src/Animora.Server/Endpoints/IdentityEndpoints.cs` | modify | one `SeatGuard` call in device registration |
| `src/Animora.Server/Identity/IdentityRoleSeeder.cs` | modify | seed the `Operator` role |
| `src/Animora.Server/Program.cs` | modify | register operations, seat guard, worker, endpoints |
| `tests/Animora.Domain.Tests/SubscriptionTransitionTests.cs` | create | allow-table + policy numbers |
| `tests/Animora.Server.Tests/SubscriptionOperationsTests.cs` | create | each operation, replay idempotency, event log |
| `tests/Animora.Server.Tests/SeatEnforcementTests.cs` | create | limit reached, reduction blocked |
| `tests/Animora.Server.Tests/OperatorScopeTests.cs` | create | tenant token denied, operator-only filter bypass |

## Implementation steps
1. Add the seven Domain files; `SubscriptionTransitions` and `SubscriptionPolicy` first, so the
   aggregates can enforce them from the start.
2. Add the wire views/requests under `Animora.Contracts.Wire/Operator/`.
3. Add the five EF configurations, extend `ServerModel` and `ServerAppendOnlyGuard`, create migration
   `108_Subscription`.
4. Implement `SubscriptionOperationsService`: one private `ApplyAsync` helper doing
   transition check → mutate → allocate `Sequence` → write `SubscriptionEvent` → audit → outbox,
   all inside `IServerUnitOfWork.ExecuteAsync`; the ten public methods are thin over it.
5. Implement `SubscriptionQueries` with the explicit tenant-filter bypass helper (one method, so the
   bypass has exactly one call path).
6. Implement `SeatGuard` and add the single call in `IdentityEndpoints`' device registration.
7. Implement `OperatorAccountService` (role seed + one-time bootstrap + create-operator).
8. Write `SubscriptionLifecycleWorker` against `WorkerBulkhead`.
9. Map `OperatorEndpoints`; register everything in `Program.cs`.
10. Write the four test classes; `OperatorScopeTests` must prove a tenant-scoped token gets
    `OPERATOR_SCOPE_REQUIRED` on every operator route.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK` (desktop untouched).

## Contracts / schema added
- Domain: `Plan`, `Subscription`, `BillingPeriod`, `Seat`, `SubscriptionEvent`,
  `SubscriptionTransitions`, `SubscriptionPolicy`, `PlanFeatures`, `SubscriptionEventTypes`, 4 enums.
- Application: `ISubscriptionOperations` + its request records + `OperatorContext`, `ISeatGuard`.
- Wire: `SubscriptionView`, `TenantSummaryView`, `SeatView`, `BillingPeriodView`,
  `SubscriptionEventView`, `OperatorAlertView`, `PlanView`, `CreateTenantRequestV1`,
  `ExtendSubscriptionRequestV1`, `RenewSubscriptionRequestV1`, `ChangeSeatsRequestV1`,
  `SubscriptionActionRequestV1`. `WireContract.Version` stays `1`.
- Persistence.SqlServer: 5 configurations, migration `108_Subscription`, `ServerModel` +
  `ServerAppendOnlyGuard` extended.
- Server: `SubscriptionOperationsService`, `SubscriptionQueries`, `SeatGuard`,
  `OperatorAccountService`, `SubscriptionLifecycleWorker`, `OperatorEndpoints`; the `Operator` role.
- Outbox message types: `subscription.plan.changed`, `subscription.seat.limit.reached`.
- `ErrorCodes` += 8. No `Animora.Contracts` change, no `CommandTypes` entry, no DI change in
  `App.axaml.cs`.

## Rules that apply here
- **R-B30 / §13.1** — Subscription is commercial only; no runtime code path reads it to decide what
  the product may do. A test asserts no `Animora.Server` handler outside this folder (and D11's
  issuance) references a subscription type.
- **A-RULE 014** — operators receive wire views, never `Subscription`/`Seat` entities.
- **A-RULE 012 / §13.3** — cross-tenant reads happen only through the one filter-bypass helper, only
  over subscription tables; the operator never reads clinical or financial rows.
- **A-RULE 023** — authentication (D03) → authorization (`RequireOperator`, D04) → entitlement
  (D11, not consulted here) stay three separate checks.
- **A-RULE 025** — the `(SubscriptionId, EventType, RequestOperationId)` unique key makes every
  operation a safe retry.
- **A-RULE 002 shape** — `SubscriptionEvents` is append-only, guarded by the interceptor; a
  correction is a new event, never an edit.
- **CONVENTIONS §7** — the bootstrap secret is read from the environment, never logged, never
  written to a config file; `SecretGuard` (D01) covers it.

## Out of scope
- Licence issuance from subscription state, signing and key rotation → D11 (this phase only
  publishes `subscription.plan.changed`).
- Any operator **UI** → E01 (which consumes exactly these endpoints).
- Usage/insight metrics and per-tenant analytics → E02.
- Payment collection, invoicing the clinic, tax — no billing gateway exists in this product
  (`STACK.md` §17 non-goals); `BillingPeriod.AmountMinor` is recorded, never charged.
- Rate limiting of the operator API → D13.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `SubscriptionTransitionTests` green: every allowed transition passes, every other pair returns
  `SUBSCRIPTION_INVALID_TRANSITION`; the four policy numbers exist in exactly one file.
- `SubscriptionOperationsTests` green (or skipped with reason when no SQL Server): create → extend →
  increase seats → suspend → resume → renew → cancel produces exactly 7 `SubscriptionEvent` rows with
  gap-free `Sequence`; **replaying any request with the same `X-Animora-Operation` returns the stored
  view and adds no event**; every operation wrote an audit row; `subscription.plan.changed` is in the
  outbox in the same transaction.
- `SeatEnforcementTests` green: registering a device beyond `SeatLimit` returns `SEAT_LIMIT_REACHED`
  and enqueues `subscription.seat.limit.reached`; reducing `SeatLimit` below assigned seats returns
  `SEAT_REDUCTION_BLOCKED` and mutates nothing.
- `OperatorScopeTests` green: every `/api/operator/*` route returns `OPERATOR_SCOPE_REQUIRED` for a
  tenant-scoped token and for an anonymous caller; the tenant-filter bypass helper is the only place
  `Tenant` is disabled (asserted by source-level or reflection check over the queries type).
- `SubscriptionLifecycleWorker` advances a period-expired subscription to `PastDue`, then `Grace`,
  then `Suspended` at the policy boundaries, writing one event each and never skipping a state.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D10 done; advance "Next phase" to `D11`; append a "Repo state" entry listing: the Domain
`Subscription` folder + `SubscriptionTransitions` + `SubscriptionPolicy` numbers, the five tables and
migration `108_Subscription`, `ISubscriptionOperations` + the ten operations and their
`X-Animora-Operation` idempotency key, `SubscriptionEventTypes` (**append-only, D11/E01/E02 read
it**), the two outbox message types (**D11 consumes `subscription.plan.changed`**), `ISeatGuard` +
the single `IdentityEndpoints` call site, `OperatorAccountService` + the `ANIMORA_OPERATOR_BOOTSTRAP`
one-time path, the `/api/operator/*` route table (**E01 is its only UI client**), the tenant-filter
bypass helper as the sole cross-tenant read path, `SubscriptionLifecycleWorker`, the 8 new error
codes, deviations.
