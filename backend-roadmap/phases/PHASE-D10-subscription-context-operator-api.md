# Phase D10 — Subscription context & operator API

**Band:** M · **Track:** D · **Depends on:** D04 · **Satisfies:** R-B05, R-B30
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §4, §5, §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D02 entry — `AnimoraServerDbContext`, `ServerModel`
  non-tenant-scoped / non-replicated lists, `TenantProvisioner`, `ITenantContext`; D03 entry —
  `AnimoraUser`, `IServerAuditWriter`, `ServerBootstrapState`; D04 entry — `AnimoraPolicies`,
  `RequireOperator`, `TenantResolutionMiddleware`; D09 entry — `OperatorAlert`,
  `IServerStoreMaintenance`, `ProjectionRebuildWorker`)
- `ARCHITECTURE.md` §13.1, §13.3, §3 (Platform Operator actor), §6 (Subscription context row)
- `A-RULE 012`, `A-RULE 023`, `A-RULE 016`, `A-RULE 029`
- `src/Animora.Persistence.SqlServer/Context/**`, `src/Animora.Server/Security/**`

## Goal
A real **Subscription** bounded context on the server — plans, per-tenant subscriptions, seat
counts, billing periods, grace and suspension — plus the **operator API** the admin website (E01)
and the licensing issuer (D11) consume. Subscription is the *commercial* model only: nothing in
this phase gates a running deployment, because that is Licensing's job (R-B30).

## Design decisions (pre-made — implement, do not re-derive)
- **Subscription ≠ Licensing, enforced structurally** (`ARCHITECTURE.md` §13.1, R-B30). This phase
  adds **no** runtime check on any tenant-facing endpoint, does **not** touch
  `EntitlementMiddleware` (D04's no-op stays untouched — D11 replaces it), and the desktop is
  unchanged. A `SubscriptionSeparationTests` fact asserts that no type under
  `src/Animora.Server/Subscription/` is referenced from `src/Animora.Server/Endpoints/CommandEndpoints.cs`,
  `src/Animora.Server/Tables/**` or `src/Animora.Server/Security/EntitlementMiddleware.cs`. The only
  legitimate consumer of subscription state in the runtime path is D11's **license issuance**, which
  is an off-line artifact, not a request-time gate.
- **Entities** (`src/Animora.Persistence.SqlServer/Subscription/`, all `nvarchar(26)` ULID keys,
  all in `ServerModel.NonTenantScopedTables` **except** `TenantSubscription`, which is tenant-keyed
  but **operator-owned** — see the filter rule below):
  | Entity | Key columns | Purpose |
  |---|---|---|
  | `Plan` | `Id`, `Code` (unique, e.g. `clinic.basic`), `DisplayName`, `MaxSeats`, `MaxDevices`, `FeatureFlagsJson`, `IsActive`, `SortOrder` | the sellable catalog; `FeatureFlagsJson` is the **only** place a feature name lives on the server |
  | `TenantSubscription` | `Id`, `TenantId` (unique — one active subscription per tenant), `PlanId`, `Status` (`SubscriptionStatus`), `SeatsPurchased`, `DevicesPurchased`, `CurrentPeriodStartUtc`, `CurrentPeriodEndUtc`, `GraceEndsUtc`, `CancelAtPeriodEnd`, `Notes` | the commercial state of one clinic |
  | `SubscriptionEvent` | `Id`, `TenantSubscriptionId`, `Kind` (`SubscriptionEventKind`), `EffectiveAtUtc`, `ActorUserId`, `Reason`, `BeforeJson`, `AfterJson`, `CreatedAtUtc` | **append-only** history; every mutation writes exactly one row (never an update) |
  | `SeatAssignment` | `Id`, `TenantSubscriptionId`, `UserId`, `AssignedAtUtc`, `RevokedAtUtc` | which users consume a seat; `RevokedAtUtc IS NULL` = occupied |
  `enum SubscriptionStatus { Trial, Active, PastDue, Grace, Suspended, Cancelled, Expired }`;
  `enum SubscriptionEventKind { Created, PlanChanged, SeatsChanged, Renewed, Extended, Reduced, Suspended, Resumed, Cancelled, Expired, GraceEntered, Reactivated }`
  (both in `Animora.Domain/Subscription/`, so the vocabulary is domain-owned and the table stores
  `int`). Migration `108_Subscription`; indexes
  `IX_TenantSubscriptions_TenantId` (unique), `IX_TenantSubscriptions_Status_CurrentPeriodEndUtc`,
  `IX_SubscriptionEvents_TenantSubscriptionId_CreatedAtUtc`,
  `IX_SeatAssignments_TenantSubscriptionId_RevokedAtUtc`.
- **Query-filter rule (the one exception, stated explicitly):** the four subscription tables are
  **operator-scoped, not tenant-scoped**, so D02's `"Tenant"` named filter is **not** applied to
  them; they are registered in a **new** `ServerModel.OperatorOwnedTables` list beside D02/D03's
  lists. Access is instead gated by the `RequireOperator` policy at the endpoint — one place, not
  two. `SubscriptionIsolationTests` asserts (a) no subscription table carries a `Tenant` filter,
  (b) every subscription endpoint carries `AnimoraPolicies.RequireOperator`, and (c) a
  tenant-scoped (non-operator) principal receives `403 OPERATOR_SCOPE_REQUIRED` on every one of
  them. This is the *only* table group allowed to opt out of the tenant filter, and the test is
  what keeps it the only one.
- **State machine, pre-decided** (`Animora.Domain/Subscription/SubscriptionTransitions.cs`, the
  same static allow-table shape as B02's `SettlementTransitions`):
  ```
  Trial     -> Active | Expired | Cancelled
  Active    -> PastDue | Suspended | Cancelled | Active (renewal)
  PastDue   -> Grace | Active | Suspended
  Grace     -> Active | Suspended | Expired
  Suspended -> Active | Cancelled | Expired
  Cancelled -> terminal (a new subscription row is created to resume selling)
  Expired   -> Active (reactivation creates a new period)
  ```
  `static Result EnsureAllowed(SubscriptionStatus from, SubscriptionStatus to)` →
  `SUBSCRIPTION_INVALID_TRANSITION`. No endpoint sets `Status` directly; every mutation goes through
  `SubscriptionService`, which consults this table first and writes the `SubscriptionEvent` second.
- **Renewal / grace / expiry semantics (binding numbers in `SubscriptionOptions`,
  section `Animora:Subscription`, validated on start):**
  | Option | Default | Meaning |
  |---|---|---|
  | `GraceDays` | 14 | `PastDue → Grace` window before suspension |
  | `TrialDays` | 30 | default trial length when a tenant is provisioned without a paid plan |
  | `ExpiryWarningDays` | 30, 7, 1 (array) | when `SubscriptionExpiringAlert` rows are raised |
  | `AutoSuspendOnExpiry` | true | `Grace` end → `Suspended` automatically |
  | `SeatOverageAllowed` | false | assigning a seat beyond `SeatsPurchased` → `SEAT_LIMIT_REACHED` |
  Extension/reduction are **period arithmetic, never history rewrites**: extend moves
  `CurrentPeriodEndUtc` forward and appends an `Extended` event; reduce moves it back but **never**
  before `max(now, CurrentPeriodStartUtc)` (`SUBSCRIPTION_PERIOD_INVALID`).
- **`SubscriptionService`** (`src/Animora.Server/Subscription/SubscriptionService.cs`) — one
  `sealed class`, one method per operator action, each returning `Result<SubscriptionViewV1>`, each
  (1) validating the transition, (2) mutating, (3) appending the `SubscriptionEvent`, (4) writing a
  `ServerAuditEntry` through D03's `IServerAuditWriter`, (5) enqueuing the domain notification onto
  **D09's `IServerOutbox`** — all inside **one** `IServerUnitOfWork.ExecuteAsync` transaction, so
  the commercial change and its history/audit/outbox message are atomic:
  `CreateAsync`, `ChangePlanAsync`, `ChangeSeatsAsync`, `RenewAsync`, `ExtendAsync`, `ReduceAsync`,
  `SuspendAsync`, `ResumeAsync`, `CancelAsync`, `ReactivateAsync`, `AssignSeatAsync`,
  `RevokeSeatAsync`.
  Outbox message types published here: `subscription.changed`, `subscription.seat.limit.reached`,
  `subscription.expiring`, `subscription.expired` — consumed by D09's existing `AlertConsumer`
  (operator alerts) and, from D11 on, by the license-reissue consumer. **No new consumer is written
  here**; `AlertConsumer` already handles unknown alert kinds by writing an `OperatorAlert` row.
- **Tenant provisioning joins here:** `CreateAsync` calls D02's `TenantProvisioner` when the tenant
  does not exist yet, so "sell a subscription" and "create the clinic" are one operator action with
  one audit trail. A tenant that already exists is attached, never re-provisioned
  (`TENANT_ALREADY_PROVISIONED`).
- **Operator API** (`src/Animora.Server/Endpoints/OperatorEndpoints.cs`, minimal APIs under
  `/api/operator`, **every** route `.RequireAuthorization(AnimoraPolicies.RequireOperator)`,
  documented in D01's `business` OpenAPI document):
  | Route | Verb | Body / query | Returns |
  |---|---|---|---|
  | `/api/operator/plans` | GET | — | `IReadOnlyList<PlanViewV1>` |
  | `/api/operator/plans` | POST | `PlanUpsertV1` | `PlanViewV1` |
  | `/api/operator/plans/{code}` | PUT | `PlanUpsertV1` | `PlanViewV1` |
  | `/api/operator/tenants` | GET | `page`, `pageSize` (≤200), `status`, `q` | `PagedResultV1<TenantSummaryV1>` |
  | `/api/operator/tenants/{tenantId}/subscription` | GET | — | `SubscriptionViewV1` |
  | `/api/operator/subscriptions` | POST | `CreateSubscriptionV1` | `SubscriptionViewV1` |
  | `/api/operator/subscriptions/{id}/plan` | POST | `ChangePlanV1` | `SubscriptionViewV1` |
  | `/api/operator/subscriptions/{id}/seats` | POST | `ChangeSeatsV1` | `SubscriptionViewV1` |
  | `/api/operator/subscriptions/{id}/renew` | POST | `RenewSubscriptionV1` | `SubscriptionViewV1` |
  | `/api/operator/subscriptions/{id}/extend` | POST | `AdjustPeriodV1` | `SubscriptionViewV1` |
  | `/api/operator/subscriptions/{id}/reduce` | POST | `AdjustPeriodV1` | `SubscriptionViewV1` |
  | `/api/operator/subscriptions/{id}/suspend` | POST | `SuspendSubscriptionV1` | `SubscriptionViewV1` |
  | `/api/operator/subscriptions/{id}/resume` | POST | `ReasonV1` | `SubscriptionViewV1` |
  | `/api/operator/subscriptions/{id}/cancel` | POST | `CancelSubscriptionV1` | `SubscriptionViewV1` |
  | `/api/operator/subscriptions/{id}/events` | GET | paged | `PagedResultV1<SubscriptionEventV1>` |
  | `/api/operator/alerts` | GET | `page`, `acknowledged` | `PagedResultV1<OperatorAlertV1>` (D09's table) |
  | `/api/operator/alerts/{id}/acknowledge` | POST | `ReasonV1` | `OperatorAlertV1` |
  | `/api/operator/maintenance/rebuild` | POST | `RebuildRequestV1(TenantId, Target)` | `202` + request id |
  The last three are the **operator-action seam D09 deferred**: acknowledging an alert and
  requesting a projection rebuild (which writes an `OperatorAlert` of type `projection.rebuild.requested`
  that D09's `ProjectionRebuildWorker` already watches for). Reopening a closed accounting period is
  **also** an operator action and is exposed as `POST /api/operator/tenants/{tenantId}/periods/{periodId}/reopen`,
  delegating to D07's `PeriodGuard` reopen path with a mandatory `Reason` and a `ServerAuditEntry`.
- **Operator accounts** (D03 deferred them here): an `AnimoraUser` with the `operator` role and
  `DefaultTenantId == null`. `IdentityRoleSeeder` (D03) is **extended by one line** to seed the
  `operator` role; the **first** operator account is created by `OperatorBootstrap`
  (`src/Animora.Server/Subscription/OperatorBootstrap.cs`) on first run only, from the
  `ANIMORA_OPERATOR_EMAIL` / `ANIMORA_OPERATOR_PASSWORD` environment variables (never from
  `appsettings*.json` — D01's `SecretGuard` already refuses that), latched by a
  `ServerBootstrapState` row exactly like D03's first-run latch. With the variables absent, no
  operator exists and every `/api/operator/**` route is simply unreachable — fail closed
  (CONVENTIONS §7).
- **Wire DTOs** in `src/Animora.Contracts.Wire/Operator/` (the admin site in E01 is a server-side
  Blazor app, but it consumes the same shapes, and D11 reads `SubscriptionViewV1` to issue a
  license): `PlanViewV1`, `PlanUpsertV1`, `SubscriptionViewV1`, `TenantSummaryV1`,
  `SubscriptionEventV1`, `SeatAssignmentV1`, `OperatorAlertV1`, `PagedResultV1<T>`,
  `CreateSubscriptionV1`, `ChangePlanV1`, `ChangeSeatsV1`, `RenewSubscriptionV1`, `AdjustPeriodV1`,
  `SuspendSubscriptionV1`, `CancelSubscriptionV1`, `ReasonV1`, `RebuildRequestV1`.
  `WireContract.Version` stays `1` (a new DTO family is additive; no existing shape changed).
- **`SubscriptionLifecycleWorker`** (`src/Animora.Server/Workers/SubscriptionLifecycleWorker.cs`,
  a `BackgroundService` acquiring **D09's `WorkerBulkhead`** like every other worker, daily at a
  configurable hour): moves `Active → PastDue` at period end, `PastDue → Grace`, `Grace → Suspended`
  (when `AutoSuspendOnExpiry`), raises the `ExpiryWarningDays` notifications, and expires trials.
  Every transition it performs goes through `SubscriptionService`, so the event/audit/outbox trail
  is identical to an operator-initiated one. The worker is **resumable and idempotent**: it keys
  each day's work on `(TenantSubscriptionId, targetStatus, effectiveDate)` and skips rows whose
  `SubscriptionEvent` for that key already exists (A-RULE 025/026).
- **No external billing integration, ever** (A-RULE 016): payment capture, invoicing the clinic and
  the public website's purchase flow are **outside** this deployment. The public site's contract
  with Animora is the **async, out-of-band license artifact** of §13.1 — represented here by nothing
  more than an operator creating/renewing a subscription. A test asserts no HTTP client is
  constructed anywhere under `src/Animora.Server/Subscription/`.
- **New error codes:** `SUBSCRIPTION_NOT_FOUND`, `SUBSCRIPTION_INVALID_TRANSITION`,
  `SUBSCRIPTION_PERIOD_INVALID`, `PLAN_NOT_FOUND`, `PLAN_INACTIVE`, `SEAT_LIMIT_REACHED`,
  `SEAT_ALREADY_ASSIGNED`, `TENANT_ALREADY_PROVISIONED`
  (`OPERATOR_SCOPE_REQUIRED` exists from D04 — reuse).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Subscription/SubscriptionStatus.cs` | create | status enum |
| `src/Animora.Domain/Subscription/SubscriptionEventKind.cs` | create | event-kind enum |
| `src/Animora.Domain/Subscription/SubscriptionTransitions.cs` | create | allow-table + `EnsureAllowed` |
| `src/Animora.Persistence.SqlServer/Subscription/Plan.cs` | create | plan catalog entity |
| `src/Animora.Persistence.SqlServer/Subscription/TenantSubscription.cs` | create | per-tenant commercial state |
| `src/Animora.Persistence.SqlServer/Subscription/SubscriptionEvent.cs` | create | append-only history row |
| `src/Animora.Persistence.SqlServer/Subscription/SeatAssignment.cs` | create | seat occupancy row |
| `src/Animora.Persistence.SqlServer/Subscription/SubscriptionConfigurations.cs` | create | 4 EF configurations + indexes |
| `src/Animora.Persistence.SqlServer/Migrations/108_Subscription*` | create | 4 tables + indexes |
| `src/Animora.Persistence.SqlServer/Context/ServerModel.cs` | modify | add `OperatorOwnedTables` |
| `src/Animora.Contracts.Wire/Operator/*.cs` | create | 17 operator wire DTOs |
| `src/Animora.Server/Configuration/SubscriptionOptions.cs` | create | grace/trial/warning/seat options |
| `src/Animora.Server/Subscription/SubscriptionService.cs` | create | the 12 operator actions, transactional |
| `src/Animora.Server/Subscription/SubscriptionQueries.cs` | create | paged operator read projections |
| `src/Animora.Server/Subscription/OperatorBootstrap.cs` | create | first operator account, env-only, latched |
| `src/Animora.Server/Endpoints/OperatorEndpoints.cs` | create | `MapAnimoraOperator`, all `RequireOperator` |
| `src/Animora.Server/Workers/SubscriptionLifecycleWorker.cs` | create | daily lifecycle transitions, bulkheaded |
| `src/Animora.Server/Identity/IdentityRoleSeeder.cs` | modify | seed the `operator` role (one line) |
| `src/Animora.Server/Program.cs` | modify | options, service, worker, `MapAnimoraOperator`, bootstrap |
| `src/Animora.Application/Results/ErrorCodes.cs` | modify | += 8 codes |
| `tests/Animora.Server.Tests/SubscriptionLifecycleTests.cs` | create | transitions, grace, expiry, idempotent worker |
| `tests/Animora.Server.Tests/SubscriptionIsolationTests.cs` | create | operator-only, no tenant filter, 403 path |
| `tests/Animora.Server.Tests/SubscriptionSeparationTests.cs` | create | no runtime path references subscription |
| `tests/Animora.Server.Tests/OperatorApiTests.cs` | create | endpoint contracts, paging cap, audit written |

## Implementation steps
1. Add the two enums + `SubscriptionTransitions` to `Animora.Domain`.
2. Add the four entities, their configurations, `OperatorOwnedTables`, and migration `108_Subscription`.
3. Add the wire DTO family under `Animora.Contracts.Wire/Operator/`.
4. Write `SubscriptionOptions`, `SubscriptionService` (transition → mutate → event → audit → outbox,
   all in one `IServerUnitOfWork.ExecuteAsync`), and `SubscriptionQueries` (paged, `AsNoTracking`,
   explicit `Select`).
5. Write `OperatorBootstrap` + the `operator` role seed line.
6. Write `OperatorEndpoints` including the alert-acknowledge, rebuild-request and period-reopen
   routes D09/D07 deferred here.
7. Write `SubscriptionLifecycleWorker` with the bulkhead and the idempotency key.
8. Register everything in `Program.cs`; add the 8 error codes.
9. Write the four test classes. `SubscriptionSeparationTests` is a source/assembly scan, so it runs
   without SQL Server; the rest skip-with-reason when no SQL Server is reachable (CONVENTIONS §1).
10. Verify against a real SQL Server: create tenant+subscription in one call; renew; extend; reduce
    below `now` is refused; seat 11 on a 10-seat plan returns `SEAT_LIMIT_REACHED`; suspend/resume
    produce exactly one event each; the lifecycle worker run twice on the same day produces exactly
    one transition.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Domain: `SubscriptionStatus`, `SubscriptionEventKind`, `SubscriptionTransitions`.
- Persistence.SqlServer: `Plan`, `TenantSubscription`, `SubscriptionEvent`, `SeatAssignment`,
  `SubscriptionConfigurations`, `ServerModel.OperatorOwnedTables`, migration `108_Subscription`.
- Wire: the `Animora.Contracts.Wire/Operator/` DTO family (17 records) incl. `PagedResultV1<T>`.
  `WireContract.Version` stays `1`.
- Server: `SubscriptionOptions`, `SubscriptionService`, `SubscriptionQueries`, `OperatorBootstrap`,
  `OperatorEndpoints` (18 routes), `SubscriptionLifecycleWorker`, the `operator` role seed.
- `ErrorCodes` += `SUBSCRIPTION_NOT_FOUND`, `SUBSCRIPTION_INVALID_TRANSITION`,
  `SUBSCRIPTION_PERIOD_INVALID`, `PLAN_NOT_FOUND`, `PLAN_INACTIVE`, `SEAT_LIMIT_REACHED`,
  `SEAT_ALREADY_ASSIGNED`, `TENANT_ALREADY_PROVISIONED`.
- No `Animora.Contracts` change, no `CommandTypes` entry, no DI change in `App.axaml.cs`,
  no change to `EntitlementMiddleware`.

## Rules that apply here
- **R-B30 / §13.1** — Subscription is commercial state only; it gates nothing at runtime. The
  separation test is the machine-checkable form of this rule.
- **§13.3** — the operator surface is an operator-scoped client of *this one* bounded context; it
  never reaches into clinical, sales or finance data. `OperatorEndpoints` exposes no business row
  beyond tenant identity, alerts and the period-reopen action.
- **A-RULE 012 / §9** — operator routes are the only ones without a tenant filter, and they are
  gated by `RequireOperator` instead; a tenant principal can never reach them.
- **A-RULE 016** — no external billing/payment service, no outbound HTTP, at any point.
- **A-RULE 025 / 026** — the lifecycle worker is idempotent per `(subscription, status, date)` and
  resumable; running it twice changes nothing twice.
- **R-B29** — every subscription mutation writes an append-only `SubscriptionEvent` **and** a
  `ServerAuditEntry` with the full correlation set.
- **CONVENTIONS §8** — every operator list endpoint is paged with the 200 cap.

## Out of scope
- License issuance, signing, installation binding and the real `EntitlementMiddleware` → D11.
- The operator **UI** (Blazor pages, navigation, tables) → E01; this phase ships only the API it calls.
- Cross-tenant business analytics / per-store charts → E02 (deferred feature; only the alert and
  tenant-summary reads exist here).
- Client-side entitlement display and the "subscription expiring" UX on the desktop → E07.
- Any payment, invoicing or public-website integration → permanently out of this product
  (`ARCHITECTURE.md` §3, §13.1).
- Rate limiting on `/api/operator/**` → D13 (one global policy).

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `SubscriptionLifecycleTests` green (or skipped with reason): every allowed transition succeeds and
  every disallowed one returns `SUBSCRIPTION_INVALID_TRANSITION`; `Active → PastDue → Grace →
  Suspended` happens on the configured day boundaries; a trial expires after `TrialDays`; running
  `SubscriptionLifecycleWorker` twice on the same simulated day produces exactly one event per
  subscription; `reduce` below `now` returns `SUBSCRIPTION_PERIOD_INVALID`.
- `SubscriptionIsolationTests` green: no subscription entity has a `"Tenant"` query filter; every
  `/api/operator/**` route carries `RequireOperator`; a clinic-scoped principal gets
  `403 OPERATOR_SCOPE_REQUIRED` on all of them; an unauthenticated caller gets `401`.
- `SubscriptionSeparationTests` green (runs without SQL Server): no type under `Server/Subscription/`
  is referenced by the command, tables or entitlement paths; no `HttpClient` is constructed in the
  subscription namespace.
- `OperatorApiTests` green: creating a subscription for a new tenant provisions the tenant exactly
  once and writes one `Created` event + one audit entry; the 11th seat on a 10-seat plan returns
  `SEAT_LIMIT_REACHED`; list endpoints reject `pageSize > 200`; acknowledging an alert and
  requesting a rebuild both write audit entries and the rebuild request is visible to D09's worker.
- With no `ANIMORA_OPERATOR_*` environment variables set, the server starts normally and every
  `/api/operator/**` route is unauthenticated-inaccessible (no operator exists).
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D10 done; advance "Next phase" to `D11`; append a "Repo state" entry listing: the four
subscription tables + migration `108_Subscription`, `SubscriptionStatus`/`SubscriptionEventKind`/
`SubscriptionTransitions` (**D11 reads the status, E01 renders the history**),
`ServerModel.OperatorOwnedTables` and the deliberate no-tenant-filter exception,
`SubscriptionService`'s transition→event→audit→outbox contract, `SubscriptionOptions` defaults
(grace 14 / trial 30 / warnings 30-7-1), the 18 `/api/operator` routes (**E01 is their only UI
client; D11 reads `SubscriptionViewV1`**), `OperatorBootstrap`'s env-only latched first operator,
`SubscriptionLifecycleWorker` + its idempotency key, the `operator` role seed, the 8 new error
codes, the separation-test guarantee (subscription gates nothing at runtime), deviations.
