# Animora Server — Senior Architecture & Code Review

> **Scope**: `src/Animora.Server` (the central multi-tenant **.NET 10** ASP.NET Core
> server) and the server-facing parts of `src/Animora.Persistence.PostgreSql` it
> depends on (unit of work, tenancy, pooling, intake store, resource locks, feed
> replication).
> **Reviewer role**: Senior software architect / server specialist.
> **Review type**: Static, line-by-line source audit. No build, no tests run.
> **Date**: 2026-10-13.
> **Audience**: AI agents and engineers. Findings are written to be
> machine-parseable: every item has a stable ID, a severity, exact file/line
> anchors, an explanation, an impact statement, and a concrete remediation.

---

## 0. How to read this report

Each finding uses this shape:

```
### [ID] Title
- Severity: BLOCKER | CRITICAL | HIGH | MEDIUM | LOW | INFO
- Category: Technical | Performance | Bug | Scalability | Security | Improvement | Consistency
- Location: <relative/path.cs>:<line(s)>
- Status quo: what the code does today
- Impact: what breaks, when, and for whom
- Recommendation: the concrete fix
```

Severity legend (impact × likelihood at production scale):

| Severity | Meaning |
|---|---|
| **BLOCKER** | Must fix before any multi-instance / production-scale deployment. |
| **CRITICAL** | Correctness, security, or data-integrity risk under realistic load. |
| **HIGH** | Will materially degrade throughput, latency, or operability as users grow. |
| **MEDIUM** | Real defect or risk, bounded blast radius. |
| **LOW** | Minor correctness / style / maintainability. |
| **INFO** | Observation, not a defect; context for the score. |

---

## 1. Executive summary

The Animora server is a **well-architected, security-conscious codebase**. The
authentication plane (opaque Data-Protection bearer tokens, refresh-token
rotation with reuse detection and family revocation, security-stamp
invalidation, timing-equalized credential checks), the tenant-isolation model
(stored-membership-only tenant binding, a save-time stamping interceptor that
rejects cross-tenant writes, global query filters), the fenced idempotent
command intake (`INSERT … ON CONFLICT DO NOTHING`, savepoint-joined outer
transaction, PostgreSQL advisory locks for financial resources), and the
request-signing / replay-protection for provisioning are all **above the
industry norm** for a product at this stage.

The dominant risk is **not** correctness — it is **horizontal scalability and
per-request database amplification**. Several core components are explicitly
single-instance (in-memory rate limiter partitions, in-memory replay window,
in-memory admin-status cache, in-memory circuit breaker / log gates) and one
shared global semaphore (`SemaphoreBackgroundAdmission(1)`) serializes **both**
all background workers **and** the user-facing sync snapshot build to a single
concurrent operation per instance. Combined with a deliberately cache-free
authorization pipeline that issues **5–7 database round-trips before any real
work**, and a very small write connection pool (`OltpWriteMax = 6`), the server
will hit a throughput wall well before CPU/RAM are exhausted.

**Top blockers / criticals to resolve for a "100/100" production-ready score:**

1. `SEC-RL-01` Rate limiter is per-instance in-memory and partitioned by **raw
   client IP only**, despite comments claiming tenant partitioning — ineffective
   behind a load balancer / multi-instance, and a memory-growth DoS surface.
2. `SCALE-ADM-01` One global `IBackgroundAdmission(1)` semaphore serializes every
   background worker **and** the request-path sync `BatchFreezer` — a hard
   per-instance concurrency ceiling of 1 for a user-facing operation.
3. `PERF-AUTHZ-01` Cache-free per-request authorization (`MembershipResolver` =
   4 queries) + per-request `EntitlementMiddleware` (2–3 queries) on the
   **write** DbContext pool → connection-pool exhaustion risk at `OltpWriteMax=6`.
4. `SCALE-STATE-01` In-memory single-instance state (`ReplayWindow`,
   `AdminStatusCache`, `CircuitBreaker`, `QueryBudget` semaphore, worker log
   gates) breaks or degrades silently when a second instance is added.
5. `PERF-QB-01` Reporting query budget is a global `SemaphoreSlim(1,1)` — one
   reporting query at a time **across all tenants** on the whole instance.

Once these are addressed (details below), the architecture is sound enough to
reach a top score; most remaining items are HIGH/MEDIUM optimizations.

---

## 2. Component map (what was reviewed)

| Area | Key files |
|---|---|
| Bootstrap / pipeline | `Program.cs`, `Hosting/*` |
| Auth plane | `Auth/LoginService.cs`, `Auth/AccessTokenIssuer.cs`, `Auth/AuthEndpoints.cs`, `Auth/DeviceAdminService.cs`, `Auth/StaffProvisioningService.cs` |
| Authorization / tenancy | `Authorization/TenantContextMiddleware.cs`, `Authorization/MembershipResolver.cs`, `Authorization/EntitlementMiddleware.cs`, `Authorization/ServerEntitlementGate.cs` |
| Command plane | `Commands/CommandIntakeService.cs`, `Commands/CommandEndpoints.cs`, `Commands/CommandPlaneUnitOfWork.cs`, `Commands/ServerFinancialConcurrencyGuard.cs`, `Commands/ServerSchedulingConcurrencyGuard.cs`, `Commands/IntakeRecoveryService.cs` |
| Sync / feed | `Sync/SyncEndpoints.cs`, `Persistence/Replication/BatchFreezer.cs` |
| Files | `Files/FileEndpoints.cs`, `Files/ValetKeyService.cs`, `Files/FileStore.cs` |
| Provisioning / operator / admin | `Provisioning/RequestSignatureVerifier.cs`, `Provisioning/ReplayWindow.cs`, `Operator/OperatorContextMiddleware.cs`, `Operator/OperatorAccountService.cs`, `Admin/OperatorCookie.cs`, `Admin/AdminSecurityHeadersMiddleware.cs`, `Admin/AdminStatusCache.cs` |
| Licensing | `Licensing/ServerEntitlementGate.cs`, `Licensing/LicenseKeyBootstrapper.cs` |
| Workers / resilience | `Workers/*`, `Resilience/CircuitBreaker.cs`, `Resilience/*` |
| Signing | `Signing/SigningKeyStore.cs`, `Signing/EcdsaSnapshotSigner.cs` |
| Persistence (server-facing) | `DependencyInjection/PersistenceServiceCollectionExtensions.cs`, `Transactions/ServerUnitOfWork.cs`, `Transactions/ServerTransactionRunner.cs`, `Tenancy/TenantStampingInterceptor.cs`, `Commands/CommandIntakeStore.cs`, `Repositories/MoneyResourceLock.cs` |

---

## 3. Scalability — behavior as users / requests grow

### [SCALE-ADM-01] One global admission semaphore serializes workers AND the request-path sync build
- Severity: **BLOCKER**
- Category: Scalability / Performance
- Location: `Animora.Persistence.PostgreSql/DependencyInjection/PersistenceServiceCollectionExtensions.cs:103`; `Animora.Persistence.PostgreSql/Replication/IBackgroundAdmission.cs:7-9`; `Animora.Persistence.PostgreSql/Replication/BatchFreezer.cs:31`; and ~10 workers (`Workers/OutboxRelayWorker.cs:20`, `Workers/SubscriptionSweepWorker.cs:17`, `Workers/IntakeRecoveryWorker.cs:19`, `Workers/OutboxCleanupWorker.cs:13`, `Workers/CommandIntakeRetentionWorker.cs:16`, `Provisioning/ProvisioningIntentWorker.cs:22`, `Provisioning/ProvisioningIntentRetentionWorker.cs:15`, `Files/FileReconciliationWorker.cs:25`, `Observability/SnapshotAgeMonitor.cs:29`, `Licensing/LicenseKeyBootstrapper.cs:21`), plus `FeedRetentionService`.
- Status quo: `IBackgroundAdmission` is registered as `SemaphoreBackgroundAdmission(1)` — **concurrency = 1**, a single process-wide permit. Every background worker acquires it per tick, and critically, **`BatchFreezer.OpenAsync` (the `/sync/batches` and `/sync/snapshots` request handler) also acquires the very same permit**.
- Impact:
  - A user-facing sync snapshot build can hold the single permit for up to ~10s (`BatchFreezer` budget `CancelAfter ≤ 10s`), during which **no background worker runs** and **no other sync batch can start** — concurrent sync requests serialize to one-at-a-time per instance regardless of tenant.
  - As the number of clients pulling sync grows, p95/p99 sync latency grows unbounded; the outbox relay (money-effect replication) and subscription sweep can be starved behind sync builds.
  - This is a hard per-instance concurrency ceiling that no amount of CPU/connections can lift.
- Recommendation:
  - Split admission into **two independent gates**: (a) a background-worker gate (keep small, e.g. 1–2) and (b) a request-path sync gate sized to the write pool / CPU (e.g. `OltpWriteMax - headroom`, tenant-partitioned if possible).
  - Do not let a request path share a semaphore with maintenance workers.
  - Consider per-tenant fairness (one in-flight snapshot per tenant) instead of one global permit.

### [SEC-RL-01] Rate limiter is in-memory, per-instance, IP-only — not tenant-scoped and a memory-DoS surface
- Severity: **BLOCKER** (for multi-instance) / **HIGH** (single instance)
- Category: Scalability / Security
- Location: `Hosting/ServerRateLimiting.cs:54-66`
- Status quo: Every policy partitions by `httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`. The code comment itself says "Tenant-scoped partitioning for commands/sync/files/operator arrives with tenant context (S04/S05)" — but S04/S05 are already wired, yet the limiter still uses IP only. Partitions are kept in process memory with no eviction policy shown.
- Impact:
  - **Multi-instance**: limits are per-process, so N instances multiply the effective limit by N and a client is throttled inconsistently depending on which instance the LB picks. Credential-stuffing / brute-force protection on `/auth` is correspondingly weakened.
  - **Shared NAT / proxy**: one `RemoteIpAddress` may represent an entire clinic; all its users share one bucket (false throttling) — or, if `X-Forwarded-For` is not honored, every client behind a reverse proxy collapses to the proxy IP (one bucket for everyone).
  - **Memory-growth DoS**: an attacker rotating source IPs creates unbounded distinct partitions; fixed-window limiter partitions accumulate.
  - `partitionKey = "unknown"` groups all IP-less connections into one bucket.
- Recommendation:
  - Partition authenticated policies (`commands`, `sync`, `files`, `operator`) by **tenant id + user id** (available after `TenantContextMiddleware`), keep `auth` partitioned by a **normalized client IP derived from a trusted `ForwardedHeaders` configuration** (ensure `UseForwardedHeaders` is configured with a known-proxy allowlist — not visible in `Program.cs`).
  - For multi-instance, move to a **distributed limiter** (e.g. Redis token bucket) or enforce per-tenant quotas in the DB.
  - Bound partition cardinality / add sliding eviction to cap memory.

### [SCALE-STATE-01] Single-instance in-memory state breaks or degrades under horizontal scale
- Severity: **CRITICAL**
- Category: Scalability
- Location:
  - `Provisioning/ReplayWindow.cs:4-19` (comment: "Single-host, fail-closed bounded replay window")
  - `Admin/AdminStatusCache.cs:5` (comment: "Single-instance only (A-RULE 031). No circuit backplane")
  - `Resilience/CircuitBreaker.cs` (per-process state; each instance has its own breaker)
  - `Workers/ResilientWorker.cs:3-16` (`WorkerFailureLogGate`, per-process)
  - `Observability/QueryBudget.cs:19` (`SemaphoreSlim(1,1)`, per-process)
- Status quo: These components hold authoritative or coordination state purely in process memory.
- Impact:
  - **`ReplayWindow`** (provisioning anti-replay): per-instance ⇒ a replayed signed provisioning request routed to a *different* instance is **not** detected as a replay. The HMAC timestamp window (±5 min, `RequestSignatureVerifier.cs:22`) still bounds the attack, but the "exactly-once within window" guarantee is lost across instances. Also `Capacity = 4096` is global and **fail-closed**: a burst of >4096 distinct provisioning signatures within 5 min makes the window reject *all* further requests (`_entries.Count >= Capacity` ⇒ `return false`) — a self-inflicted DoS on the provisioning surface.
  - **`AdminStatusCache` / `CircuitBreaker` / log gates**: duplicated refresh work and inconsistent breaker verdicts across instances (one instance open, another closed) — operationally confusing but not unsafe.
- Recommendation:
  - Move provisioning replay protection to a **durable, shared** store (DB table keyed by `(keyId, signature)` with a TTL index, or Redis `SET key NX EX 300`). Make capacity-exhaustion degrade to "shed" rather than "deny-all", or remove the hard cap once durable.
  - Document explicitly which components are single-instance and gate multi-instance deployment on replacing them; or make the circuit breaker / status cache tolerate N instances by design (they already roughly do).

### [PERF-AUTHZ-01] Cache-free authorization + entitlement = 5–7 DB round-trips per request on the small write pool
- Severity: **HIGH**
- Category: Performance / Scalability
- Location: `Authorization/MembershipResolver.cs:31-72` (4 reads: user-by-id, membership, tenant, device, each comment "deliberately no cache"); `Authorization/EntitlementMiddleware.cs:27`; `Licensing/ServerEntitlementGate.cs:19-30` (grant + active-license + possibly revoked-license = up to 3 reads); pool sizing `appsettings.json` `OltpWriteMax: 6`, `OltpReadMax: 3`.
- Status quo: Every authenticated, tenant-bound request runs `MembershipResolver` (4 queries) **and** `EntitlementMiddleware` → `ServerEntitlementGate` (2–3 queries) before the endpoint does any work. These run on the **write** context (`AnimoraServerDbContext`, `OltpWriteKey`) because the request scope is not the worker scope. For command requests the entitlement check is then **repeated a second time inside `CommandIntakeService`** (`CommandIntakeService.cs:122`).
- Impact:
  - 6–9 serialized DB round-trips of fixed overhead per request; at even moderate RPS this dominates latency and **saturates the 6-connection write pool** — each in-flight request may hold a connection across these awaits, so ~6 concurrent authenticated requests can exhaust the pool and the 7th blocks until `CommandTimeout`/pool timeout.
  - Redundant double entitlement evaluation for commands wastes one full gate round-trip.
- Recommendation:
  - Introduce a **short-TTL, tenant-scoped cache** for membership resolution and entitlement (e.g. 5–15s), invalidated on security-stamp rotation (the stamp is already compared, so a stale cache is self-correcting within the TTL). `TenantScopedMemoryCache` already exists in `Observability/` — use it.
  - Route these **read-only** lookups through the **read** pool / `IServerReadContextFactory`, not the write context, so they don't consume write connections.
  - Remove the duplicate entitlement check: trust the middleware verdict for plain requests and keep only the command-type-specific check inside intake (or vice-versa).
  - Re-balance pool sizes (`OltpWriteMax` is very low for a command-heavy server) and set an explicit Npgsql connection/pool timeout to fail fast rather than hang.

### [PERF-QB-01] Reporting query budget is a single global semaphore (one report at a time, all tenants)
- Severity: **HIGH**
- Category: Performance / Scalability
- Location: `Observability/QueryBudget.cs:20,24`
- Status quo: `_reporting = new SemaphoreSlim(1,1)`; all `QueryClass.Reporting` acquisitions serialize through one permit per instance.
- Impact: Insight/reporting for the entire server (all tenants) is single-threaded. One slow report blocks every other tenant's report. As tenant count grows this becomes a visible stall.
- Recommendation: Size the reporting budget to the read pool (e.g. `OltpReadMax`), and/or partition by tenant so one tenant's heavy report can't starve others. Guard with a wait timeout so callers get a typed 503 instead of blocking indefinitely.

### [SCALE-N1-01] N+1 query patterns in staff directory and role catalogue
- Severity: MEDIUM
- Category: Performance
- Location: `Auth/StaffProvisioningService.cs:249-257` (loop over every `StaffRole` enum value, one `FindByRoleAsync` each) and `:260-278` (per-member effective-permission assembly).
- Status quo: `ListAsync` issues one role-definition query per role enum value, then processes each member. Role definitions could be fetched in a single query.
- Impact: Grows with role count; bounded but wasteful, and runs on the write context.
- Recommendation: Batch-load all role definitions for the tenant in one query into a dictionary; keep member assembly in memory.

---

## 4. Security

### [SEC-FWD-01] Forwarded headers / trusted-proxy configuration not visible — IP-based decisions may be spoofable
- Severity: HIGH
- Category: Security
- Location: `Program.cs` (no `UseForwardedHeaders` / `ForwardedHeadersOptions`); consumed by `Hosting/ServerRateLimiting.cs:56`, `Provisioning/ReplayWindow`, audit IP.
- Status quo: Rate-limit partitioning and (implicitly) any IP logging rely on `Connection.RemoteIpAddress`. If the server runs behind a reverse proxy / LB (the stated SaaS topology), without a configured `ForwardedHeaders` middleware with a known-proxy allowlist, `RemoteIpAddress` is the proxy's IP (collapsing all clients) — or, if a raw `X-Forwarded-For` is trusted elsewhere, it is attacker-controllable.
- Impact: Rate limits become either global (one bucket) or trivially bypassable by spoofing `X-Forwarded-For`; auth brute-force protection weakened.
- Recommendation: Add `UseForwardedHeaders` early in the pipeline with an explicit `KnownProxies`/`KnownNetworks` allowlist; derive the client IP from it consistently; never trust `X-Forwarded-For` without the allowlist.

### [SEC-HSTS-01] HTTPS redirect / HSTS only outside Development — confirm TLS termination contract
- Severity: MEDIUM
- Category: Security
- Location: `Program.cs:147-151`
- Status quo: `UseHsts()` + `UseHttpsRedirection()` run only when `!IsDevelopment()`. If TLS is terminated at the LB and the app receives HTTP, `UseHttpsRedirection` may loop or no-op depending on forwarded-proto handling (tied to `SEC-FWD-01`).
- Impact: Possible redirect loops or missing HSTS if forwarded-proto is not wired. Admin surface mitigates via `AdminOptions.RequireHttps` returning 400 on non-HTTPS (`Admin/AdminSecurityHeadersMiddleware.cs:19`), but the main API does not.
- Recommendation: Wire `ForwardedHeaders` (proto) and confirm the HTTPS contract end-to-end; document whether the app or the LB enforces TLS.

### [SEC-ENUM-01] Login tenant-binding audit before user existence is confirmed is fine, but device auto-create on login is a write on an unauthenticated path
- Severity: MEDIUM
- Category: Security / Bug (race)
- Location: `Auth/LoginService.cs:124-138`
- Status quo: On successful password + membership check, an **unknown installation hash** auto-registers a `Device` row during `/auth/login`. Two concurrent first logins from the same installation can both observe `device is null` and both call `repository.AddDevice(...)`, producing duplicate device rows (unless a unique constraint on `(UserId, InstallationHash)` rejects the second — which would then surface as a 500 via the save path rather than a clean result).
- Impact: Duplicate device rows or an unhandled unique-violation on a hot path; also, device creation is driven by an unauthenticated request body (installation hash) and counts toward device inventory.
- Recommendation: Use an idempotent upsert (`INSERT … ON CONFLICT (UserId, InstallationHash) DO NOTHING` then re-read) for device binding, mirroring the command-intake pattern; ensure the unique index exists and is mapped to a typed `Result` (as `ServerUnitOfWork` does for rooms/vaccines) rather than a 500.

### [SEC-STAMP-01] Security-stamp rotation on refresh-reuse relies on an implicit save; verify it commits
- Severity: MEDIUM (verify)
- Category: Security / Bug
- Location: `Auth/LoginService.cs:211-218` (`victim?.RotateSecurityStamp();` then returns failure)
- Status quo: On refresh-token **reuse** detection, the code rotates the victim's security stamp and family-revokes, then returns `SessionExpired`. `RotateSecurityStamp()` mutates the tracked entity; whether it is persisted depends on `RevokeFamilyAsync` / audit writing `SaveChanges` within the same unit of work. Unlike `DeviceAdminService`/`StaffProvisioningService`, there is no explicit `AuditAsync` success-check gating the stamp persistence here before returning.
- Impact: If the rotated stamp is **not** flushed, stolen access tokens remain valid until natural expiry despite reuse detection — defeating the primary benefit of reuse detection.
- Recommendation: Explicitly persist the stamp rotation in the same transaction as the family revocation + audit, and assert (test) that after a detected reuse, the user's stored `SecurityStamp` changed. Make the audit/save result gate the response as elsewhere.

### [SEC-ADV-01] Advisory-lock key uses `hashtextextended` over a concatenated string — collision risk causes false contention
- Severity: MEDIUM
- Category: Security (availability) / Correctness
- Location: `Animora.Persistence.PostgreSql/Repositories/MoneyResourceLock.cs:15-18`
- Status quo: `pg_advisory_xact_lock(hashtextextended($"{tenantId}:{resource}", 0))` maps an arbitrary string to a 64-bit advisory key. Two distinct `(tenant, resource)` pairs that hash-collide will serialize against each other.
- Impact: Hash collisions (birthday bound over 2^64 is low but non-zero across the full lifetime of a busy multi-tenant system) cause **false mutual exclusion** between unrelated financial resources — correctness-preserving but a latent, hard-to-diagnose contention/latency source. More importantly, cross-tenant collisions could let one tenant's lock delay another's unrelated transaction.
- Recommendation: Prefer `pg_advisory_xact_lock(key1 int4, key2 int4)` with `key1 = hashtext(tenantId)` and `key2 = hashtext(resource)` to drastically reduce cross-domain collisions, or document the accepted collision probability. Ensure deterministic ordering (already sorted) to avoid deadlocks — good.

### [SEC-OBS-01] Good: constant-time signature compare, secret zeroing, redacted logging, no stack traces leaked
- Severity: INFO (positive)
- Category: Security
- Location: `Provisioning/RequestSignatureVerifier.cs:26,44`; `Hosting/GlobalExceptionMiddleware.cs:39,62`; `Hosting/SecretGuard.cs`; `Auth/LoginService.cs:435-440` (timing equalization via dummy hash).
- Note: HMAC compared with `CryptographicOperations.FixedTimeEquals`; the unknown-key path still computes an HMAC against a 32-byte zero secret and returns the same failure (no key-existence oracle); secrets zeroed in `finally`; exception middleware never emits stack traces outside Development and uses an HTML error for `/admin`. Credential checks spend a dummy hash on miss. This is correct and should be preserved.

### [SEC-OPENAPI-01] OpenAPI can be enabled in production via config flag
- Severity: LOW
- Category: Security
- Location: `Program.cs:127-132`
- Status quo: `EnableOpenApi` can turn on the schema document in any environment.
- Impact: Operational foot-gun (surface disclosure) if accidentally enabled in prod. Default is `false`.
- Recommendation: Gate behind the operator auth or a separate network; keep default off (already true) and document the risk.

---

## 5. Technical correctness & bugs

### [BUG-PIPE-01] `UseRateLimiter()` is placed AFTER authentication/authorization/entitlement
- Severity: HIGH
- Category: Technical / Security / Performance
- Location: `Program.cs:159-165`
- Status quo: Middleware order is `UseAuthentication → OperatorContext → TenantContext → UseAuthorization → Entitlement → UseRateLimiter`. The rate limiter therefore runs **after** the expensive authN/authZ/entitlement work (which, per `PERF-AUTHZ-01`, is 5–7 DB queries).
- Impact: An unauthenticated or unauthorized flood still pays the full authN/tenant-resolution/entitlement cost **before** being rate-limited — the limiter cannot shed load it is meant to shed. This inverts the protective intent of the limiter and amplifies DB pressure under attack. (Endpoint-level `RequireRateLimiting` partially mitigates because the limiter metadata is attached per endpoint, but the global `UseRateLimiter` position still gates when the limiter executes relative to other middleware.)
- Recommendation: Move `UseRateLimiter()` to run **before** `UseAuthentication()` (right after routing / exception / logging), so throttling happens before any DB-touching identity work. Keep per-endpoint policies. Validate with the existing `PlaneOrderTests`.

### [BUG-UOW-01] Savepoint is released even on the rollback path — confirm intended semantics
- Severity: MEDIUM (verify)
- Category: Technical / Bug
- Location: `Commands/CommandPlaneUnitOfWork.cs:42-48`
- Status quo: On a nested failure, the code does `RollbackToSavepointAsync(savepoint)` and then **unconditionally** `ReleaseSavepointAsync(savepoint)` before returning. In PostgreSQL, `ROLLBACK TO SAVEPOINT` keeps the savepoint alive, so a subsequent `RELEASE` is legal — but releasing right after rolling back is redundant and, if the provider/driver treats a post-rollback release inconsistently, can throw and convert a clean domain rejection into a transaction-aborting exception.
- Impact: Low-probability but hard-to-reproduce transaction aborts on the rejection path; at minimum it is dead/confusing work.
- Recommendation: On the failure branch, `RollbackToSavepointAsync` then simply return (do not release); release only on the success branch. Add a test that drives a nested rejection and asserts the outer transaction still commits the settlement receipt.

### [BUG-CORR-01] `CorrelationHeader.Resolve` mints a fresh ULID on every call with no per-request memoization
- Severity: MEDIUM
- Category: Bug / Consistency / Observability
- Location: `Hosting/CorrelationHeader.cs:24-40`; consumers e.g. `Commands/CommandIntakeService.cs:51` reads it back from the **response** header rather than calling `Resolve` again.
- Status quo: When no valid inbound `X-Correlation-Id` is present, each `Resolve(context)` call returns a **new** ULID. Correct behavior depends on the first middleware (`GlobalExceptionMiddleware`) calling `Resolve` once and `Echo`-ing it, after which later code must read `Response.Headers[X-Correlation-Id]` (as `CommandIntakeService` does) instead of calling `Resolve` again. Any code path that calls `Resolve` a second time before echo — or after `Response.Clear()` without re-echo — gets a different id.
- Impact: Fragile, implicit contract. `GlobalExceptionMiddleware` itself re-echoes after `Response.Clear()` (good), but the pattern is easy to break; a mismatched correlation id across a request's logs/audit rows undermines traceability.
- Recommendation: Memoize the correlation id in `HttpContext.Items` on first `Resolve`, and have all consumers read the memoized value. This makes "resolve once, same value everywhere" structural rather than conventional.

### [BUG-BATCH-01] Command batch aborts mid-way on the first transport failure, discarding already-computed settlements
- Severity: MEDIUM
- Category: Bug / Consistency
- Location: `Commands/CommandEndpoints.cs:47-55`
- Status quo: `BatchAsync` processes envelopes sequentially; on the first `settled.IsFailure` it returns a single `TransportError` for the whole batch, discarding the `results` already accumulated for prior items (which **were** persisted/settled server-side).
- Impact: The client receives a transport error for the entire batch even though earlier items committed. Idempotency protects re-submission (re-sending returns the stored settlement), so this is not data loss — but it is a confusing partial-success-reported-as-total-failure that forces a full-batch retry and wastes work.
- Recommendation: Return a per-item result array where each item carries its own outcome (including the transport-ish failure as a typed per-item settlement), rather than failing the envelope. Reserve a true HTTP error only for whole-batch transport problems (malformed JSON, too large).

### [BUG-SIGN-01] Cold-start race + per-signature overhead in `SigningKeyStore.GetAsync`
- Severity: MEDIUM
- Category: Bug (race) / Performance
- Location: `Signing/SigningKeyStore.cs:23-70`
- Status quo: `GetAsync` finds an active key or **creates one**. On a cold database (no key yet), two concurrent capability-issuance requests can both miss `FindActiveAsync` and both `CreateAsync`, inserting two "active" snapshot keys. Also, every call re-queries the table and **re-unprotects** the PKCS#8 private key via Data Protection — on the per-capability-issuance hot path.
- Impact: Duplicate active signing keys (ambiguous "current" key, key churn, extra public keys clients must trust); and unnecessary DB + crypto work per signature.
- Recommendation: Create the signing key under a transaction + unique constraint (one active snapshot key) or a PostgreSQL advisory lock during creation; cache the resolved `ISnapshotSigner` in memory (keyed by `KeyId`) with invalidation on rotation, so steady-state signing does no DB read and no repeated unprotect.

### [BUG-FILE-01] Upload buffers the entire chunk in memory (`MemoryStream` + `memory.ToArray()`)
- Severity: MEDIUM
- Category: Performance / Resource
- Location: `Files/FileEndpoints.cs:63-66`
- Status quo: The upload handler reads the request body into a `MemoryStream`, then calls `memory.ToArray()` (a second full copy) before `WriteAsync`. `MaxChunkBytes = 1 MiB`, so each in-flight upload holds ~2 MiB on the managed heap (LOH for `ToArray`).
- Impact: Under concurrent uploads this drives Gen2/LOH pressure and GC pauses; `files` rate limit (60/min) doesn't bound concurrency. Doubles the memory per chunk unnecessarily.
- Recommendation: Stream the request body directly to the staging `FileStream` with offset validation, avoiding the full-buffer + `ToArray` copy. Bound concurrent uploads (semaphore or connection limit) rather than relying only on the rate limiter.

### [BUG-OPLOCK-01] `OperatorContextMiddleware` + cookie `OnValidatePrincipal` both hit the DB for every admin request
- Severity: LOW
- Category: Performance / Consistency
- Location: `Operator/OperatorContextMiddleware.cs:17,43-45`; `Admin/OperatorCookie.cs:28-37`
- Status quo: For `/admin` and `/_blazor`, `OperatorContextMiddleware` calls `AuthenticateAsync(OperatorCookie.Scheme)` which triggers `OnValidatePrincipal` (1 DB read of the operator account), and the operator API path separately re-reads the account (`:43`). Admin requests thus do 1–2 operator-account reads each.
- Impact: Redundant DB work on the admin/Blazor circuit (Blazor Server fires many small requests). Bounded (operator count is tiny) but wasteful.
- Recommendation: Resolve the operator account once per request and cache in `HttpContext.Items`; avoid double authentication.

### [BUG-RECOVERY-01] Intake-recovery worker is tenant-serial and capped at 50 rows per 30s tick
- Severity: MEDIUM
- Category: Scalability / Bug (throughput)
- Location: `Workers/IntakeRecoveryWorker.cs:17-39`
- Status quo: Under the single global admission permit, the worker iterates tenants sequentially, processing at most `remaining = 50` expired leases **total** per 30s tick across all tenants.
- Impact: With many tenants and a burst of orphaned/expired command leases (e.g. after an instance crash), recovery drains at ≤100 rows/min globally. Stuck commands (and the money effects awaiting authenticated replay) can backlog for a long time.
- Recommendation: Make the cap per-tenant or adaptive; parallelize bounded recovery; decouple from the shared admission gate (see `SCALE-ADM-01`).

### [BUG-OUTBOX-01] Outbox relay concurrency is fixed at 2 and shares the global admission gate
- Severity: MEDIUM
- Category: Scalability
- Location: `Workers/OutboxRelayWorker.cs:20,26,32` (`BoundedWorkerQueue.MaxConcurrentConsumers`)
- Status quo: The relay claims at most 2 messages and processes with 2 consumers per tick, and holds the global admission permit for the whole tick.
- Impact: Outbound replication throughput is capped at ~2 concurrent deliveries per instance and competes with sync builds for the one admission permit. As tenant write volume grows, outbox lag (and thus cross-replica/consumer latency) rises.
- Recommendation: Make claim size / consumer count configurable and independent of the sync admission gate; scale with `WorkerMax` pool.

### [BUG-BURN-01] `BurnPasswordHash` caches a single static dummy hash lazily without synchronization
- Severity: LOW
- Category: Technical (benign race)
- Location: `Auth/LoginService.cs:36,435-440`
- Status quo: `s_dummyHash ??= hasher.HashPassword(...)` is a non-synchronized lazy init of a static field. Concurrent first-misses may each compute the dummy hash; last writer wins.
- Impact: Benign (idempotent value), minor redundant hashing on first concurrent misses. No correctness issue.
- Recommendation: Use `LazyInitializer.EnsureInitialized` or a `static readonly Lazy<string>` for clarity.

### [BUG-HEALTH-01] `/health/ready` runs all probes sequentially and writes shared snapshot state on each hit
- Severity: LOW
- Category: Performance / Technical
- Location: `Program.cs:204-225`
- Status quo: The readiness endpoint awaits each probe in series and calls `snapshot.Set(...)` per probe per request. It is anonymous and un-rate-limited (by design).
- Impact: An external monitor polling `/health/ready` frequently causes repeated DB/key-ring/schema probing (each probe may hit the DB). Un-rate-limited + anonymous = a cheap way to generate DB load.
- Recommendation: Cache readiness results for a few seconds (the `ReadinessSnapshot` already exists); serve the cached snapshot to frequent callers and refresh on a timer. Keep `/health/live` trivial (already is).

---

## 6. Integration & consistency

### [CONS-GATE-01] Two `ServerEntitlementGate` types with different exempt-path lists
- Severity: MEDIUM
- Category: Consistency
- Location: `Authorization/ServerEntitlementGate.cs` (interface + `AllowAllServerEntitlementGate`) vs `Licensing/ServerEntitlementGate.cs` (real impl, `IsExempt` at `:10`); and `Authorization/TenantContextMiddleware.IsSkipped` (`:31-40`).
- Status quo: Three different "which paths bypass this plane" lists exist:
  - `TenantContextMiddleware.IsSkipped`: `/health`, `/admin`, `/_blazor`, `/operator`, `/provisioning`, `/auth/login`, `/auth/refresh`, `/auth/keys`, `/licensing/keys`.
  - `Licensing.ServerEntitlementGate.IsExempt`: `/health`, `/auth`, `/licensing`, `/operator`, `/provisioning`, `/admin`.
  - `EntitlementMiddleware` additionally checks `ServerEntitlementGate.IsExempt` **and** `/admin`.
- Impact: Divergent lists are a classic source of "a path is tenant-bound but not entitlement-gated" (or vice-versa) drift. For example `/auth/logout` and `/auth/device/register` are tenant-skipped at some granularities but `/auth` (whole prefix) is entitlement-exempt — intentional, but the inconsistency between "exact path" (`TenantContext`) and "prefix" (`Entitlement`) matching is fragile.
- Recommendation: Centralize the plane-exemption policy in **one** place (a single `ServerSurfacePolicy` with named route classes) consumed by both middlewares; add a test that asserts every mapped endpoint has a defined (gated or exempt) classification. Remove the `Authorization/ServerEntitlementGate.cs` allow-all once S16's real gate is the registered one (confirm `TryAdd` ordering wins the right impl).

### [CONS-ENT-01] Entitlement evaluated twice for commands (middleware + intake)
- Severity: LOW
- Category: Consistency / Performance
- Location: `Authorization/EntitlementMiddleware.cs:27` and `Commands/CommandIntakeService.cs:122`
- Status quo: A command request passes the per-request entitlement gate in the middleware, then `CommandIntakeService` evaluates entitlement again (this time with `CommandType`).
- Impact: Redundant DB round-trip; two places that can disagree if one is updated and the other not.
- Recommendation: Keep exactly one authoritative evaluation for the command path (the command-type-aware one in intake) and let the middleware skip `/api/commands` for the coarse check, or vice-versa.

### [CONS-RL-01] Comment/implementation mismatch on rate-limit partitioning
- Severity: LOW (documentation) — see `SEC-RL-01` for the substantive issue
- Category: Consistency
- Location: `Hosting/ServerRateLimiting.cs:54-56`
- Status quo: Comment promises tenant partitioning "arrives with S04/S05"; code still partitions by IP although S04/S05 are wired.
- Recommendation: Either implement the promised tenant partitioning or correct the comment; stale comments mislead future maintainers.

### [CONS-INT-01] `TryBeginAsync` idempotency insert runs outside the outer command transaction
- Severity: INFO (by design, documented here for clarity)
- Category: Consistency
- Location: `Commands/CommandIntakeService.cs:66` → `CommandIntakeStore.TryBeginAsync` (`INSERT … ON CONFLICT DO NOTHING`, autocommit) **before** `unitOfWork.ExecuteAsync` (`:93`).
- Note: This is the intended lease-then-execute pattern (the Begin row + lease must be visible to a concurrent takeover before the effect transaction runs). Correct, but worth a one-line doc comment at the call site so a future maintainer does not "fix" it by moving the insert inside the transaction (which would break crash-recovery visibility).

---

## 7. Improvements & optimizations (beyond the defects above)

| ID | Area | Improvement |
|---|---|---|
| IMP-01 | Pool sizing | Make `OltpWriteMax/OltpReadMax/WorkerMax` scale with `ProfileGiB` / CPU; current `6/3/2` is low for a command server. Add Npgsql `Timeout`/`Connection Pruning` tuning and an explicit pool-exhaustion timeout → typed 503. |
| IMP-02 | Caching | Add a short-TTL tenant-scoped cache (already have `TenantScopedMemoryCache`) for membership + entitlement + role definitions; invalidate on security-stamp change. |
| IMP-03 | Read/write split | Route all read-only pre-flight lookups (membership, entitlement, file metadata `FindAsync(false)`) through the read pool; reserve the write pool for actual mutations. |
| IMP-04 | Observability | Emit metrics for pool saturation, admission-gate wait time, authZ query count/latency, outbox lag, intake-recovery backlog, reporting-budget wait. These are the exact signals the scaling findings predict will turn red first. |
| IMP-05 | Backpressure | Replace fire-and-wait global semaphores with bounded queues that return 503 + `Retry-After` when full, so overload sheds cleanly instead of blocking request threads. |
| IMP-06 | Timeouts | Add per-operation timeouts around each DB call in the authZ pipeline so a single slow query can't pin a request (and its connection) for the full 30s `CommandTimeout`. |
| IMP-07 | Signing | Cache the active `ISnapshotSigner`; rotate via a background job with advisory-lock-guarded creation. |
| IMP-08 | Files | Stream uploads; add content-type/extension re-validation on `Complete`; verify CAS blobs are fsync'd (already `Flush(true)` — good) and add periodic integrity scrubbing (reconciliation worker exists). |
| IMP-09 | Config | Externalize magic numbers (batch size 50, chunk 1 MiB, lease 60s, admission concurrency, outbox claim 2, recovery cap 50, snapshot lifetime 10s) into validated options for tuning without a redeploy. |
| IMP-10 | Health | Cache readiness; expose a `/health/startup` distinct from live/ready for orchestrators. |
| IMP-11 | Tests | Add explicit tests for: refresh-reuse stamp persistence (`SEC-STAMP-01`), concurrent first-login device race (`SEC-ENUM-01`), savepoint release-on-rollback (`BUG-UOW-01`), pipeline order with limiter-first (`BUG-PIPE-01`), and pool-exhaustion behavior. |

---

## 8. Scenario readiness — "is the server ready for X?"

For each operational scenario, the current verdict and the gating findings.

| Scenario | Ready? | Gating findings |
|---|---|---|
| Single instance, low load (pilot clinics) | **Yes** | Architecture is correct; in-memory state is acceptable at 1 instance. |
| Single instance, high concurrency | **Partial** | `SCALE-ADM-01` (sync serialized to 1), `PERF-AUTHZ-01` + `IMP-01` (write pool exhaustion at ~6 concurrent authed requests), `PERF-QB-01` (reporting serialized). |
| Horizontal scale-out (N instances behind LB) | **No** | `SEC-RL-01` (per-instance limits), `SCALE-STATE-01` (replay window / breaker / status cache per-process), `SEC-FWD-01` (client IP). Must move shared state to a backplane first. |
| Burst / thundering herd (mass client resync) | **No** | `SCALE-ADM-01` + `BUG-RECOVERY-01` + `BUG-OUTBOX-01`: sync, recovery, and outbox all throttled by the single admission permit and small caps. |
| Crash recovery (instance dies mid-command) | **Mostly** | Fenced intake + leases + `IntakeRecoveryService` are well-designed; but `BUG-RECOVERY-01` throughput cap means a large backlog drains slowly. |
| Hostile traffic (brute force / DoS) | **Partial** | `BUG-PIPE-01` (limiter after authN work), `SEC-RL-01`/`SEC-FWD-01` (bypassable/ineffective limits), `SCALE-STATE-01` (replay-window deny-all at capacity), un-rate-limited `/health/ready`. |
| Provisioning at scale (many signed website calls) | **Partial** | `SCALE-STATE-01`: in-memory replay window is per-instance and fail-closed at 4096 entries/5 min. |
| Reporting-heavy tenants | **No (at scale)** | `PERF-QB-01`: one report at a time server-wide. |
| Large file uploads at concurrency | **Partial** | `BUG-FILE-01` memory pressure; concurrency unbounded beyond rate limit. |

---

## 9. Positives worth preserving (do not regress these)

- **Tenant isolation is defense-in-depth**: stored-membership-only tenant binding (`MembershipResolver`), save-time stamping interceptor that rejects cross-tenant writes (`TenantStampingInterceptor`), global query filters per entity, and a fail-closed `CurrentTenantId` that throws if unbound. Token `tid`/`role` claims are explicitly never trusted for authority.
- **Auth hardening**: opaque Data-Protection bearer tokens (no JWT), refresh rotation with reuse detection + family revocation, security-stamp invalidation on revoke/role-change, timing-equalized credential verification, indistinguishable failure codes (no user/tenant enumeration), lockout that never flips on disabled accounts.
- **Idempotent, fenced command intake**: `INSERT … ON CONFLICT DO NOTHING`, lease + attempt fencing, savepoint-joined outer transaction, dependency gating, fingerprint verification, PostgreSQL **advisory locks** (multi-instance-safe) for financial resources and **optimistic version fences** for scheduling.
- **Correct transaction/retry layering**: `ServerTransactionRunner` retries only transient serialization/deadlock faults, non-retrying execution strategy to avoid double jitter, cancellation never swallowed, mapped store faults → typed results.
- **Secret hygiene**: `SecretGuard` refuses to boot with literal secrets in JSON; key ring + signing keys + valet keys all via Data Protection; secrets zeroed after use.
- **Operational discipline**: `ValidateOnStart` options, readiness probes that degrade without killing the host, redacted logging, correlation ids, resilient workers with heartbeats and log gating, circuit breaker + bounded queues + outbound timeouts.
- **Fail-closed defaults** throughout (unknown command types, unbound tenant, missing device, unknown valet key, etc.).

---

## 10. Prioritized remediation roadmap (to reach 100/100)

**Phase A — unblock production scale (BLOCKER/CRITICAL):**
1. `SCALE-ADM-01` Split admission gates (request sync vs workers); size to pool/CPU.
2. `SEC-RL-01` + `SEC-FWD-01` Tenant/user-partitioned limits, trusted forwarded headers, bounded partitions; distributed limiter for multi-instance.
3. `SCALE-STATE-01` Durable shared replay window; document/replace single-instance state before scale-out.
4. `PERF-AUTHZ-01` + `PERF-QB-01` Cache authZ/entitlement, read-pool the lookups, drop duplicate entitlement check, size reporting budget.

**Phase B — correctness & robustness (HIGH/MEDIUM):**
5. `BUG-PIPE-01` Move rate limiter before authN.
6. `SEC-STAMP-01`, `SEC-ENUM-01`, `BUG-UOW-01`, `BUG-SIGN-01`, `BUG-CORR-01`, `BUG-BATCH-01`.
7. `BUG-RECOVERY-01`, `BUG-OUTBOX-01`, `BUG-FILE-01` throughput/memory.

**Phase C — consistency & polish (LOW/INFO + improvements):**
8. `CONS-GATE-01`/`CONS-ENT-01`/`CONS-RL-01` centralize plane policy; `IMP-01…11`.

Completing Phase A makes the server genuinely multi-instance scalable; Phase B
closes every identified correctness/security gap; Phase C removes drift risk and
tunes the hot paths. With all three done and covered by the tests in `IMP-11`,
the server earns a top score on technical quality, performance, bug-freedom,
scalability, security, and consistency.

---

## 11. Finding index (quick reference)

| ID | Sev | Category | File |
|---|---|---|---|
| SCALE-ADM-01 | BLOCKER | Scalability | PersistenceServiceCollectionExtensions.cs:103 / BatchFreezer.cs:31 |
| SEC-RL-01 | BLOCKER/HIGH | Scalability/Security | Hosting/ServerRateLimiting.cs:54 |
| SCALE-STATE-01 | CRITICAL | Scalability | ReplayWindow.cs / AdminStatusCache.cs / CircuitBreaker.cs |
| PERF-AUTHZ-01 | HIGH | Performance | MembershipResolver.cs:31 / EntitlementMiddleware.cs |
| PERF-QB-01 | HIGH | Performance | Observability/QueryBudget.cs:20 |
| SCALE-N1-01 | MEDIUM | Performance | StaffProvisioningService.cs:249 |
| SEC-FWD-01 | HIGH | Security | Program.cs (missing UseForwardedHeaders) |
| SEC-HSTS-01 | MEDIUM | Security | Program.cs:147 |
| SEC-ENUM-01 | MEDIUM | Security/Bug | Auth/LoginService.cs:124 |
| SEC-STAMP-01 | MEDIUM | Security/Bug | Auth/LoginService.cs:211 |
| SEC-ADV-01 | MEDIUM | Security/Correctness | MoneyResourceLock.cs:15 |
| SEC-OBS-01 | INFO(+) | Security | RequestSignatureVerifier.cs / GlobalExceptionMiddleware.cs |
| SEC-OPENAPI-01 | LOW | Security | Program.cs:127 |
| BUG-PIPE-01 | HIGH | Technical/Security | Program.cs:159 |
| BUG-UOW-01 | MEDIUM | Bug | CommandPlaneUnitOfWork.cs:42 |
| BUG-CORR-01 | MEDIUM | Bug/Observability | Hosting/CorrelationHeader.cs:24 |
| BUG-BATCH-01 | MEDIUM | Bug | Commands/CommandEndpoints.cs:47 |
| BUG-SIGN-01 | MEDIUM | Bug/Performance | Signing/SigningKeyStore.cs:23 |
| BUG-FILE-01 | MEDIUM | Performance | Files/FileEndpoints.cs:63 |
| BUG-OPLOCK-01 | LOW | Performance | Operator/OperatorContextMiddleware.cs:17 |
| BUG-RECOVERY-01 | MEDIUM | Scalability | Workers/IntakeRecoveryWorker.cs:17 |
| BUG-OUTBOX-01 | MEDIUM | Scalability | Workers/OutboxRelayWorker.cs:20 |
| BUG-BURN-01 | LOW | Technical | Auth/LoginService.cs:435 |
| BUG-HEALTH-01 | LOW | Performance | Program.cs:204 |
| CONS-GATE-01 | MEDIUM | Consistency | ServerEntitlementGate.cs (x2) / TenantContextMiddleware.cs |
| CONS-ENT-01 | LOW | Consistency | EntitlementMiddleware.cs / CommandIntakeService.cs |
| CONS-RL-01 | LOW | Consistency | Hosting/ServerRateLimiting.cs:54 |
| CONS-INT-01 | INFO | Consistency | CommandIntakeService.cs:66 |

---

*End of report. Generated from a static source audit of `src/Animora.Server`
and its server-facing persistence dependencies on 2026-10-13. No build or tests
were executed; all line anchors reference the reviewed revision.*
