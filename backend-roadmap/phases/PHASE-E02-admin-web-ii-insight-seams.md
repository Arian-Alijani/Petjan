# Phase E02 — Admin web II: insight seams

**Band:** M · **Track:** E · **Depends on:** E01 · **Satisfies:** R-B05, R-B11, R-B35
**Runnable after this phase:** desktop offline yes · server required yes (for the admin site only)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §5, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (E01 entry — the three interactive islands, `OperatorContextFactory`,
  the `/admin` path base, the asset scan; D10 entry — `Subscription`, `Plan`, `Seat`,
  `BillingPeriod`, `SubscriptionEvent`, `SubscriptionQueries`, `OperatorAlert`; D11 entry —
  `LicenseIssuance`, `LicenseStatus`, `LicensePolicy.ExpiringWithinDays = 10`; D13 entry —
  `QueryBudget`, `TenantCache`, `CacheKey`, `AnimoraMetrics`, `DiagnosticsEndpoints`,
  `ServerDiagnosticsBundle`; D02 entry — `ServerModel`, the named query filters, migration numbering;
  D08 entry — the replicated / non-replicated split)
- `ARCHITECTURE.md` §9.4, §11.4, §16.3 · `STACK.md` §6.2, §10 · `PERFORMANCE.md` §12, §15
- `A-RULE 018`, `A-RULE 033`, `A-RULE 034`, `A-RULE 035`
- `src/Animora.Server/Diagnostics/QueryBudget.cs`, `src/Animora.Server/Subscription/SubscriptionQueries.cs`

## Goal
The operator dashboard shows **real** subscription, seat, licence and platform-health numbers, all
computed under D13's `Reporting` query budget so they can never load the OLTP path. The phase also lands
the **seam** that makes future per-tenant analytics a configuration change rather than a rewrite: a
tenant-scoped aggregate snapshot table, an upload contract, an `ITenantInsightQuery` port whose only
implementation today reads subscription/licensing data, and a consent/ADR record. **No cross-tenant
business data is exposed by this phase.**

## Design decisions (pre-made — implement, do not re-derive)
- **`ITenantInsightQuery` — the whole point of the phase**
  (`src/Animora.Application/Insight/ITenantInsightQuery.cs`):
  ```
  Task<Result<PlatformInsight>> GetPlatformAsync(InsightWindow w, OperatorContext ctx, CancellationToken ct);
  Task<Result<TenantInsight>> GetTenantAsync(string tenantId, InsightWindow w, OperatorContext ctx, CancellationToken ct);
  Task<Result<IReadOnlyList<TimeBucket>>> GetSeriesAsync(InsightSeriesKey key, InsightWindow w, OperatorContext ctx, CancellationToken ct);
  ```
  `sealed record InsightWindow(DateOnly FromUtc, DateOnly ToUtc, InsightGranularity Granularity)`;
  `enum InsightGranularity { Day, Week, Month }`;
  `sealed record InsightSeriesKey(string Metric, string? TenantId)`.
  **`InsightMetrics` is a closed allow-list of metric names** — today exactly:
  `platform.tenants.active`, `platform.tenants.by_status`, `platform.subscriptions.by_plan`,
  `platform.seats.utilization`, `platform.licenses.expiring`, `platform.licenses.issued`,
  `platform.alerts.open`, `platform.settlement.backlog`, `tenant.seats.utilization`,
  `tenant.subscription.timeline`, `tenant.license.history`, `tenant.snapshot.freshness`.
  An unknown metric returns `INSIGHT_METRIC_UNSUPPORTED`, never a dynamic query.
  `InsightMetricAllowListTests` asserts the implementation covers exactly this list — **this is what
  keeps a future analytics feature from smuggling business data in through a string.**
- **Today's implementation is deliberately narrow.**
  `src/Animora.Server/Insight/SubscriptionInsightQuery.cs` implements the port **only** over
  `Subscription`, `Plan`, `Seat`, `BillingPeriod`, `SubscriptionEvent`, `LicenseIssuance`,
  `OperatorAlert`, `IdempotencyRecord` (backlog count) and `TenantAggregateSnapshot` freshness.
  It must not touch `Invoice`, `Payment`, `JournalEntry`, `StockMovement`, `Pet`, `Owner`, `Visit` or
  any clinical/financial table. `InsightIsolationTests` asserts, by EF model inspection of the
  compiled queries' root types, that the implementation's entity set is a subset of that allow-list.
  A later analytics phase adds a second implementation; it does not widen this one.
- **Tenant-scoped aggregate snapshot table (the future-analytics seam, filled by the client later):**
  `TenantAggregateSnapshot` in `Animora.Persistence.SqlServer`, migration **`111_TenantInsight`**:
  | Column | Type | Note |
  |---|---|---|
  | `Id` | `nvarchar(26)` PK | ULID |
  | `TenantId` | `nvarchar(26)` | tenant-scoped ⇒ the D02 `Tenant` named filter applies |
  | `SnapshotKind` | `nvarchar(64)` | from `SnapshotKinds` (below) |
  | `PeriodStartUtc` / `PeriodEndUtc` | `datetime2(3)` | closed, immutable window |
  | `MetricsJson` | `nvarchar(max)` | a **flat numeric map only** — validated on ingest |
  | `RowCountCovered` | `int` | how many source rows the client aggregated |
  | `ProducedAtUtc` | `datetime2(3)` | client's informational stamp |
  | `ReceivedAtUtc` | `datetime2(3)` | server stamp (authoritative) |
  | `ConsentVersion` | `int` | the tenant consent generation this upload was made under |
  | `RowVersion` | `rowversion` | concurrency |
  Unique index `UX_TenantAggregateSnapshots_Tenant_Kind_Period` on
  `(TenantId, SnapshotKind, PeriodStartUtc)` — **the idempotency key**: re-uploading the same window
  replaces nothing and returns the stored row. Index
  `IX_TenantAggregateSnapshots_TenantId_ReceivedAtUtc`. The table is **append-only** (registered in
  `ServerAppendOnlyGuard`) and **not replicated** (added to `ServerModel.NonReplicatedProjectionTables`,
  asserted by D08's `SyncSurfaceTests` set assertion).
  `SnapshotKinds` (closed set, 4 values): `inventory.onhand.daily`, `sales.totals.daily`,
  `visits.counts.daily`, `appointments.counts.daily`. **No phase in this roadmap produces them** —
  the desktop-side producer is explicitly deferred; the contract exists so it can be added without a
  schema change.
- **Upload contract (server half only, structure now):** `POST /api/insight/snapshots`, in
  `src/Animora.Server/Endpoints/InsightEndpoints.cs`, authorized by
  `AnimoraPolicies.For(PermissionCodes.SettingsManage)` (a tenant manager's device uploads its own
  tenant's aggregates; the operator never uploads). Body
  `TenantSnapshotUploadV1(string SnapshotKind, DateTimeOffset PeriodStartUtc,
  DateTimeOffset PeriodEndUtc, int RowCountCovered, int ConsentVersion,
  IReadOnlyDictionary<string, long> Metrics)` in `Animora.Contracts.Wire/Insight/`.
  Validation, all fail-closed: `SnapshotKind` ∈ `SnapshotKinds`; window is closed and ≤ 31 days;
  `Metrics` ≤ 40 entries, keys `^[a-z][a-z0-9_.]{2,48}$`, values `long` only — **a string or nested
  object is rejected** (`INSIGHT_PAYLOAD_INVALID`), which is the mechanism that stops raw business
  rows from ever arriving; `ConsentVersion` must equal the tenant's current
  `TenantSettings.InsightConsentVersion` or the upload is rejected with `INSIGHT_CONSENT_REQUIRED`.
  `TenantId` is taken from `ITenantContext`, never from the body. Re-upload of an existing
  `(TenantId, Kind, PeriodStart)` returns `200` with the stored row (idempotent, not a duplicate).
- **Consent is explicit and off by default.** `TenantSettings` (D07) gains
  `InsightConsentVersion int NOT NULL DEFAULT 0` and `InsightConsentGrantedAtUtc datetime2(3) NULL`
  in the same migration. `0` = no consent ⇒ every upload is rejected and
  `TenantInsight.SnapshotFreshness` reads "not consented". An operator **cannot** grant it: it is set
  by the tenant's own manager via `POST /api/insight/consent` (`SettingsManage`), audited. This is
  the privacy boundary the deferred analytics feature depends on.
- **ADR record:** `backend-roadmap/decisions/ADR-E02-tenant-insight.md` (new folder, docs only) —
  context, the decision (seam now, feature later), the consent model, what is explicitly **not**
  exposed, the trigger that reopens it (ROADMAP §5), and the A-RULE 034 reporting-isolation
  commitment. Referenced from `PROGRESS.md`'s decision log.
- **Dashboard rendering (Static SSR, reusing E01's islands — no fourth island):**
  `/admin` is replaced with a real dashboard: KPI tiles (active/trial/suspended/grace tenants, seat
  utilization %, licences expiring within `LicensePolicy.ExpiringWithinDays`, open alerts,
  settlement backlog age) + three charts. `/admin/tenants/{tenantId}` gains a tenant insight strip
  (seat utilization, subscription timeline, licence history, snapshot freshness).
  New page `/admin/health` renders D13's `/diagnostics/metrics` + `/diagnostics/outbox` snapshots and
  a "download bundle" form posting to `POST /diagnostics/bundle`.
- **Charts are server-rendered inline SVG — no JS chart library, no CDN.**
  `src/Animora.Web.Admin/Components/Charts/BarChart.razor`, `LineChart.razor`,
  `StackedBarChart.razor`: each takes `IReadOnlyList<TimeBucket>`
  (`sealed record TimeBucket(DateOnly Bucket, string Series, long Value)`) and emits `<svg>` with
  computed paths, an accessible `<title>`/`<desc>`, and a `<table>` fallback in a `<details>` element.
  Max **90 buckets** per chart (enforced, not truncated silently: a wider window is coerced to a
  coarser `InsightGranularity` and the chart labels the granularity). Zero JavaScript files are added
  — `AssetIsolationTests` (E01) keeps passing unchanged.
- **Reporting isolation (A-RULE 034, R-B35) — the hard requirement of this phase.**
  Every method of `SubscriptionInsightQuery` opens `QueryBudget.Reporting` (D13) and runs on the
  `DatabaseOptions.ReadConnectionStringName` connection with `AsNoTracking()`, explicit projections,
  `CommandTimeout = 15 s`, and `QuerySplittingBehavior.SplitQuery` where a collection is included.
  Results are cached in D13's `TenantCache` with
  `CacheKey.For(scope: "insight", tenant: tenantId ?? "platform", user: operatorUserId,
  resource: metric, version: windowHash)` and a **120 s** TTL — bounded, tenant-safe, and
  operator-scoped (A-RULE 033). `ReportingIsolationTests` asserts: every insight query records a
  `Reporting` budget scope; none runs inside an OLTP transaction; a cache key missing any component
  fails to compile/throws.
- **Cross-tenant reads are explicit and narrow.** Platform aggregates need the tenant filter
  bypassed; they use **only** D10's existing single tenant-filter-bypass helper
  (`SubscriptionQueries`' documented method), never a new bypass. `InsightIsolationTests` asserts
  exactly one bypass call site exists in `Animora.Server/Insight/**` and that every
  `GetTenantAsync`/`GetSeriesAsync(tenantId)` path applies the tenant predicate explicitly.
- **Metrics about the panel itself:** `AnimoraMetrics` gains `animora.admin.insight.duration`
  (histogram, tag `metric`) so a slow dashboard is measurable, not guessed (A-RULE 035).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Insight/ITenantInsightQuery.cs` | create | port + `InsightWindow`, `InsightSeriesKey`, `InsightGranularity` |
| `src/Animora.Application/Insight/InsightModels.cs` | create | `PlatformInsight`, `TenantInsight`, `TimeBucket` |
| `src/Animora.Application/Insight/InsightMetrics.cs` | create | the closed metric allow-list |
| `src/Animora.Contracts.Wire/Insight/TenantSnapshotUploadV1.cs` | create | upload contract + `SnapshotKinds` |
| `src/Animora.Contracts.Wire/Insight/InsightConsentRequestV1.cs` | create | consent request |
| `src/Animora.Persistence.SqlServer/Entities/TenantAggregateSnapshot.cs` | create | the snapshot row |
| `src/Animora.Persistence.SqlServer/Configurations/Insight/TenantAggregateSnapshotConfiguration.cs` | create | mapping + unique key + indexes |
| `src/Animora.Persistence.SqlServer/Migrations/*` | create | `111_TenantInsight` (+ `TenantSettings` consent columns) |
| `src/Animora.Persistence.SqlServer/Context/ServerModel.cs` | modify | append-only + non-replicated registration (path per D02) |
| `src/Animora.Server/Insight/SubscriptionInsightQuery.cs` | create | the only implementation today |
| `src/Animora.Server/Insight/SnapshotIngestService.cs` | create | validation + idempotent ingest |
| `src/Animora.Server/Endpoints/InsightEndpoints.cs` | create | snapshot upload + consent routes |
| `src/Animora.Web.Admin/Components/Pages/Dashboard.razor` | modify | real KPI tiles + 3 charts |
| `src/Animora.Web.Admin/Components/Pages/Health.razor` | create | D13 diagnostics view + bundle download |
| `src/Animora.Web.Admin/Components/Pages/Tenants/Detail.razor` | modify | tenant insight strip |
| `src/Animora.Web.Admin/Components/Charts/*.razor` | create | 3 inline-SVG chart components |
| `src/Animora.Server/Diagnostics/AnimoraMetrics.cs` | modify | `animora.admin.insight.duration` |
| `src/Animora.Server/Program.cs` | modify | register the port, ingest service, endpoints |
| `backend-roadmap/decisions/ADR-E02-tenant-insight.md` | create | the consent/scope ADR |
| `tests/Animora.Server.Tests/InsightIsolationTests.cs` | create | entity allow-list + single bypass site |
| `tests/Animora.Server.Tests/InsightMetricAllowListTests.cs` | create | exact metric coverage |
| `tests/Animora.Server.Tests/SnapshotIngestTests.cs` | create | validation, consent, idempotency |
| `tests/Animora.Server.Tests/ReportingIsolationTests.cs` | create | `Reporting` budget + cache keys |

## Implementation steps
1. Write the Application-layer port, models and the closed `InsightMetrics` allow-list.
2. Add `TenantAggregateSnapshot` + configuration + the `TenantSettings` consent columns; generate
   migration `111_TenantInsight`; register the table as append-only and non-replicated.
3. Write the wire contracts and `SnapshotIngestService` (validation → consent check → idempotent
   upsert-or-return); map `InsightEndpoints` with the tenant-manager policy.
4. Implement `SubscriptionInsightQuery` over the allow-listed entities only, each method inside a
   `QueryBudget.Reporting` scope on the read connection, results cached via `TenantCache`.
5. Add the `animora.admin.insight.duration` instrument and wrap every port call with it.
6. Write the three inline-SVG chart components with the 90-bucket cap and granularity coercion.
7. Rebuild `/admin` as the real dashboard; add the tenant insight strip; add `/admin/health`.
8. Write the ADR; link it from `PROGRESS.md`'s decision log.
9. Write the four test classes.
10. Verify manually: dashboard numbers match direct SQL counts; an upload without consent is
    rejected; the same window uploaded twice yields one row; a payload with a string value is
    rejected; `/admin/health` renders and the bundle downloads.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Application: `ITenantInsightQuery`, `InsightWindow`, `InsightGranularity`, `InsightSeriesKey`,
  `PlatformInsight`, `TenantInsight`, `TimeBucket`, `InsightMetrics`.
- Wire: `TenantSnapshotUploadV1`, `SnapshotKinds`, `InsightConsentRequestV1`.
  `WireContract.Version` stays `1` (additive, new types only).
- Persistence.SqlServer: `TenantAggregateSnapshot` + configuration, `TenantSettings`
  `InsightConsentVersion`/`InsightConsentGrantedAtUtc`, migration `111_TenantInsight`,
  `ServerModel` append-only + non-replicated registrations.
- Server: `SubscriptionInsightQuery`, `SnapshotIngestService`, `InsightEndpoints` (3 routes:
  `POST /api/insight/snapshots`, `POST /api/insight/consent`, `GET /api/insight/consent`),
  the `animora.admin.insight.duration` instrument.
- Web.Admin: real dashboard, `/admin/health`, tenant insight strip, 3 chart components.
  **No fourth interactive island.**
- Docs: `backend-roadmap/decisions/ADR-E02-tenant-insight.md`.
- `ErrorCodes` += `INSIGHT_METRIC_UNSUPPORTED`, `INSIGHT_PAYLOAD_INVALID`,
  `INSIGHT_CONSENT_REQUIRED`, `INSIGHT_WINDOW_INVALID`.
- No `Animora.Contracts` change, no `CommandTypes` entry, no DI change in `App.axaml.cs`.

## Rules that apply here
- **A-RULE 034 / R-B35** — every insight query runs in a `Reporting` budget scope on the read
  connection; none shares the OLTP transaction path; asserted by `ReportingIsolationTests`.
- **A-RULE 033** — every cached insight result's key carries scope + tenant + operator + resource +
  window version; a missing component is a test failure.
- **A-RULE 018** — the snapshot table is a denormalized read model; it never becomes a financial
  write model, and nothing settles from it.
- **A-RULE 035** — the dashboard's own latency is instrumented, so a capacity claim about it is
  measured rather than asserted.
- **R-B11** — the seam (port + allow-list + snapshot table + upload contract + consent) is complete;
  the analytics *feature* is not built, and the ADR records the trigger.
- **R-B19 / CONVENTIONS §7** — `TenantId` on ingest comes from `ITenantContext`; platform aggregates
  use the single documented bypass helper and nothing else.
- **CONVENTIONS §5** — `AsNoTracking`, explicit projections, split queries, every new index named
  and justified; migration `111` never edits an earlier migration.
- **`STACK.md` §16 / R-B27** — charts are inline SVG; no chart library, no external asset.

## Out of scope
- The desktop-side producer that actually writes `TenantAggregateSnapshot` rows → deferred
  (ROADMAP §5); the contract and consent gate exist here so it needs no schema change.
- Any cross-tenant business metric (stock, revenue, patients) → deferred (ROADMAP §5); the metric
  allow-list mechanically prevents it.
- A separate reporting database / OLAP store → deferred; reopened only if E08's measurement shows
  the OLTP budget breached.
- Client-side sync of insight data → never (the table is non-replicated by design, D08's split).
- The G1/A–V gate runs that use these numbers → E08.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `InsightMetricAllowListTests` green: `SubscriptionInsightQuery` serves exactly the 12 allow-listed
  metrics; an unlisted metric returns `INSIGHT_METRIC_UNSUPPORTED`.
- `InsightIsolationTests` green: the implementation's query root types are a subset of the
  subscription/licensing/alert/snapshot allow-list — **no clinical or financial entity appears**;
  exactly one tenant-filter-bypass call site exists under `Animora.Server/Insight/**`.
- `SnapshotIngestTests` green: upload with `ConsentVersion = 0` → `INSIGHT_CONSENT_REQUIRED`;
  non-numeric metric value → `INSIGHT_PAYLOAD_INVALID`; > 40 metrics → rejected; 40-day window →
  `INSIGHT_WINDOW_INVALID`; the same `(Kind, PeriodStart)` uploaded twice → one row, second call
  returns the stored row; a body-supplied `TenantId` is ignored.
- `ReportingIsolationTests` green: each of the three port methods records a `Reporting` budget
  scope; cache hits are observed on a repeat dashboard load within the 120 s TTL; an operator's
  cached entry is never returned for a different operator or tenant.
- Manual (real SQL Server): `/admin` KPI tiles match direct SQL counts; charts render as inline SVG
  with a `<table>` fallback and never exceed 90 buckets; `/admin/health` shows D13's metrics and
  downloads a bundle; `AssetIsolationTests` (E01) still green.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`; no file under
  `src/Animora.Desktop/` changed.

## PROGRESS.md update
Mark E02 done; advance "Next phase" to `E03`; append a "Repo state" entry listing: `ITenantInsightQuery`
+ the closed `InsightMetrics` allow-list (**the guard a future analytics phase must extend, not bypass**),
`SubscriptionInsightQuery`'s entity allow-list and its single bypass site, `TenantAggregateSnapshot` +
`SnapshotKinds` + migration `111_TenantInsight` + the append-only/non-replicated registrations,
`TenantSettings.InsightConsentVersion` (**default 0 = no consent**), `TenantSnapshotUploadV1` +
the 3 `/api/insight/*` routes + the idempotency key, the `Reporting`-budget + `TenantCache` policy for
every insight read, the real dashboard / `/admin/health` / tenant insight strip, the 3 inline-SVG chart
components (no JS, no island added), `animora.admin.insight.duration`, the 4 new error codes, the ADR
path, deviations. Add a decision-log entry pointing at `ADR-E02-tenant-insight.md`.
