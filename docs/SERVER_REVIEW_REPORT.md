# Animora Server — Professional Code Review Report

> Scope: `src/Animora.Server` (166 C# files, ~9.3K LOC) plus the persistence, sync,
> and wire-contract projects it directly depends on. Review method: full static
> analysis of every subsystem (build/test execution was not possible — the .NET 10
> SDK is absent from this sandbox, see §9). Format: English, structured for AI
> ingestion. Previous report files were NOT consulted (per instruction).

---

## 0. Executive Summary

Animora.Server is a **mature, security-conscious, offline-first clinic platform
backend** built on .NET 10 / ASP.NET Core minimal APIs + PostgreSQL. It implements
a sophisticated command-intake / outbox / feed-replication sync architecture with
idempotency, optimistic concurrency, fenced leases, RBAC, multi-tenancy, device
capability snapshots, and a separate privileged "operator" plane.

The codebase is **well above average** in engineering discipline:
- Fail-closed-by-default route surface classification.
- DB-backed membership + security-stamp validation on every protected request.
- Content-addressable file storage with magic-byte revalidation and CAS refcounting.
- Transaction-scoped PG advisory locks with a consistent lock hierarchy (deadlock-safe).
- Constant-time HMAC comparison, DP-protected valet keys, ECDSA snapshot signing.
- Privacy-first logging (no raw path/query/header/body; redacting enricher).
- Test-to-source ratio ~1.57:1 (14.6K test LOC vs 9.3K source LOC).

**No critical (exploitable) vulnerability or data-loss bug was found.** The
findings below are overwhelmingly **scale-out limitations** (per-process state that
is explicitly documented) and **minor hardening / consistency nuances**. None block
single-instance production; several must be addressed before horizontal scaling.

**Realistic score: 92 / 100** (see §10 for the breakdown and the exact gap to 100).

---

## 1. Severity Legend

| Tag | Meaning |
|-----|---------|
| **S1** | Critical — exploitable security hole or data loss. **(none found)** |
| **S2** | High — correctness/consistency failure under realistic conditions. |
| **S3** | Medium — latent risk, scale-out blocker, or standards gap. |
| **S4** | Low — hardening, polish, defense-in-depth. |

---

## 2. Multi-Instance / Horizontal-Scale Correctness (biggest category)

The server is **engineered and documented as single-instance** for several
components (`SCALE-STATE-01`, 7 in-code markers). Running >1 replica behind a load
balancer silently degrades these guarantees. This is the single largest cluster of
real findings.

### 2.1 — S2 — AuthZ decision cache is per-process; revocation lags on other replicas
- **File:** `Authorization/AuthzDecisionCache.cs`, `MembershipResolver.cs`
- Membership + entitlement verdicts are cached in-process keyed by
  `(user, device, securityStamp)` with `TtlSeconds = 10` (`ServerOptions.cs:154`).
- `InvalidateUser` / `InvalidateTenant` mutate **only the local process dictionary**.
- **Impact (multi-instance):** a device revocation, staff disable, tenant
  suspension, or licence revocation performed on replica A is **not** reflected on
  replica B until B's cache entry expires (≤ TTL, default 10 s). On a single
  instance this is correct and immediate.
- **Fix to reach 100:** back invalidation with a shared signal (PG `LISTEN/NOTIFY`,
  Redis pub/sub, or a short-TTL distributed cache), or set `TtlSeconds = 0` to
  disable caching (documented escape hatch, at a latency cost).

### 2.2 — S2 — Provisioning replay-protection window is per-process
- **File:** `Provisioning/ReplayWindow.cs`
- The nonce/signature replay window is an in-memory bounded structure. With N
  replicas, a captured signed provisioning request can be replayed **once per
  replica** inside the timestamp-skew window before any one of them has seen it.
- **Mitigation already present:** HMAC + timestamp skew bound the window to minutes;
  provisioning is an operator-only, low-volume plane.
- **Fix to reach 100:** persist consumed nonces (unique index on
  `(clientId, nonce)` with TTL cleanup) so replay detection is global.

### 2.3 — S3 — Circuit breaker, file-upload gate, background-admission are per-process
- **Files:** `Resilience/CircuitBreaker.cs`, `Files/FileUploadGate.cs`
- Each replica maintains its own breaker state and upload concurrency slots.
  Aggregate load on a shared downstream (DB, disk) can be N× the intended ceiling.
- **Acceptable** for bounded resources that scale with instances (local disk); worth
  noting for shared downstreams.

### 2.4 — S3 — `SnapshotAgeMonitor` / worker singletons assume one scheduler
- Background workers (`OutboxRelayWorker`, retention, reconciliation, recovery) are
  `IHostedService` instances. On multi-instance they will **all run concurrently**.
  They are **safe** because every mutation is fenced by PG advisory locks / claim
  leasing / `TryAcquireAttempt`, but the *effective* parallelism and poll pressure
  multiply by N. Confirm this matches capacity planning before scaling.

> **Net:** The code is explicit and honest about this (good). To become a "100/100"
> horizontally-scalable server, §2.1 and §2.2 are the two that change *observable
> behavior* and should be the first fixes.

---

## 3. Authentication, Authorization & Session Lifecycle

### 3.1 — S3 — Disabling a staff user does not rotate the security stamp
- **File:** `Auth/StaffProvisioningService.cs` (`SetActiveAsync` disable path)
- Compare: `DeviceAdminService.RevokeAsync` **does** call
  `owner.RotateSecurityStamp()`, invalidating all live access tokens on next request
  across replicas. Disabling a staff member calls only `decisions.InvalidateUser`
  (local cache) and relies on `Status == Disabled` being re-read after cache expiry.
- **Impact:** a disabled user keeps a working session for up to the AuthZ cache TTL
  on each replica (≤ 10 s single-instance; per-replica otherwise). Refresh is
  correctly blocked (`PreRotationChecksAsync` → `user_disabled` + family revoke).
- **Fix to reach 100:** rotate the security stamp on disable, matching device revoke.
  This makes disable *immediate* and consistent with the rest of the auth model.

### 3.2 — S4 — Access-token revocation latency is bounded by token lifetime + cache TTL
- Access tokens are opaque DP-protected bearer tokens (15 min). AuthN decodes them
  with **no DB read** (by design, for throughput). Revocation is enforced at the
  membership-resolution layer via the `sst` (security stamp) claim comparison. This
  is correct; the residual latency is the documented TTL window (§2.1). Not a bug —
  listed for completeness because the task asked for "anything that could bite later."

### 3.3 — Positive findings (verified, no action)
- `CommandEnvelopeValidator`: strict ULID/fingerprint/GUID validation, duplicate
  JSON key rejection, payload-shape enforcement, 64/32-char caps — robust against
  malformed/oversized envelopes.
- `CommandAuthorizer` / `CommandPermissionTable`: **fail-closed** — an unmapped
  command type returns `PermissionUnknown` and is never executable.
- Operator plane validates the `ost` stamp against the DB on **every** request (no
  stale caching) — stronger than the tenant plane, appropriate for privilege level.
- Login uses `FOR UPDATE` row lock to serialize per-username lockout-counter updates
  (no lost-update on brute-force counters). Timing-equalized on missing secret.
- `DeviceAdminService.RevokeAsync` forbids self-revocation (no admin lockout).

---

## 4. Command Plane, Sync & Client/Server Consistency

### 4.1 — Positive — Client/server command parity is enforced by tests
- Wire contract defines **88** command types; the server permission table maps **83**;
  the remaining **5** are explicitly **reserved** (`Commands/ReservedCommandTypes.cs`:
  `sales.adjustment.add`, `inventory.receipt.post`, `purchasing.order.submit`,
  `finance.period.close`, `finance.cheque.status`) — forward-compat placeholders that
  **fail closed**. Parity is guarded by catalog/reserved tests. **No client/server
  drift.** This directly answers task item #6.
- Client `HttpCommandTransport.MaxBatchSize = 50` matches the server cap. Contract
  version negotiation returns `426 Upgrade Required` / `ResyncRequired` cleanly.

### 4.2 — Positive — Idempotency, fencing & concurrency are correct
- `CommandIntakeService` + `CommandPlaneUnitOfWork`: idempotent intake keyed by
  `operationId`, fenced attempt leases, savepoint-scoped inner handler rollback,
  settlement staged after the inner effect (change-tracker clear is safe).
- `ServerFinancialConcurrencyGuard` + `MoneyResourceLock`: transaction-scoped
  `pg_advisory_xact_lock`, two-key (tenant+resource) partitioning, **sorted
  acquisition within each phase** and a **consistent global phase order**
  (ledger/invoice/cashier → stock → scheduling) → lock-order deadlocks are avoided.
- `IntakeRecoveryService`: S06 crash-recovered rows are released as **fenced Failed**
  ("awaiting_authenticated_replay") — it never invents authority to replay money
  effects. Excellent correctness discipline.

### 4.3 — S4 — `IntakeRecoveryWorker` total cap uses non-atomic `Volatile.Read` pre-check
- **File:** `Workers/IntakeRecoveryWorker.cs:33`
- The `Volatile.Read(ref remaining) <= 0` fast-path is advisory; the authoritative
  budget is the atomic `Interlocked.Decrement` passed as `takeBudget`. Under high
  parallelism the worker may process a few items **beyond** `RecoveryTotalCap` before
  the decrement catches up. Bounded by `RecoveryParallelism`; harmless but imprecise.

### 4.4 — Positive — IDOR / tenant-isolation checks
- `FeedPageReader` scopes by `CurrentTenantId` (DbContext filter + tenant-joined
  query + tenant-scoped file path) — no cross-tenant feed download.
- `CommandIntakeStore.GetIdMapAsync` is tenant-scoped (`RequireTenant()`).
- `SyncHintHub` group membership is derived **server-side** from stored tenant
  membership; no client-invocable join/subscribe. No tenant-spoofing on the hub.

---

## 5. File Subsystem

### 5.1 — Positive — Strong design
- `FileStore`: content-addressable, SHA-256 validated via regex, ULID-validated
  tenant/upload ids → **no path traversal**. CAS integrity checks present.
- `FileTransferService.CompleteAsync` (`IMP-08`): re-validates type **and** extension
  **and** magic bytes at completion (not only at declare) → upload-time content
  spoofing is caught. CAS refcount increment is in the same transaction as the state
  change. Cross-process FS mutation guarded by advisory locks.
- `FileReconciliationWorker`: cursor-based bounded FS scan; **never deletes unknown
  filenames by guess**; validates path components through the store before touching
  them; detects `abandoned` / `blob_missing` / `bitrot` / `orphan` with audit rows.
- `ValetKeyService`: DP-protected, time-limited, fully bound (tenant/user/device/
  file/op/max) grants. `Results.File(..., fileName)` sets `Content-Disposition:
  attachment` + `nosniff` → no inline-render XSS vector for user content.

### 5.2 — S4 — Post-commit `File.Delete` crash window (self-healing)
- `FileTransferService.CompleteAsync`/`DeleteAsync` delete the staged file **after**
  `tx.Commit`. A crash in that window leaves a staged orphan — but
  `FileReconciliationWorker` reclaims it. Correct by design; noted for completeness.

---

## 6. Provisioning, Licensing, Operator Plane

### 6.1 — S4 — Provisioning GET handler `Unprotect` is not exception-guarded
- **File:** `Provisioning/ProvisioningEndpoints.cs` (GET `/intents/{key}`)
- `protection.CreateProtector(...).Unprotect(...)` and
  `JsonSerializer.Deserialize<JsonElement>(result.ResultJson)` are **not** wrapped in
  try/catch. If a stored `ProtectedTempPassword` / `ResultJson` is ever corrupted or
  was protected under a rotated/purged key, this throws → `GlobalExceptionMiddleware`
  returns a generic 500 instead of a typed error. Low likelihood (server-written
  data), but a corrupt row makes the intent **un-pollable**. Wrap and map to a typed
  `ProvisioningSignatureInvalid`/`ValidationFailed`. The POST path already guards
  `Protect` with `try/finally` + `ZeroMemory` — mirror that rigor on read.

### 6.2 — Positive
- `RequestSignatureVerifier`: constant-time HMAC compare, dummy-HMAC on null secret
  for timing equalization, replay window + timestamp skew. HTTPS-required filter on
  the whole provisioning group (401 on plain HTTP outside Development).
- `ProvisioningEndpoints.ReadBodyAsync`: hard **8 KiB** body cap with early
  `Content-Length` rejection → no unbounded buffering.
- `WebsiteClientService`: 32-byte CSPRNG secret, DP-protected at rest, `ZeroMemory`
  on the plaintext in `finally`, secret returned to caller exactly once.
- `OperatorBootstrapper`: advisory-lock single-run, `MustChangePassword = true`.
- Operator login/change-password/logout are **antiforgery-validated** (`__Host-`
  cookie) and operator-cookie authorized; change-password uses `FOR UPDATE`.

### 6.3 — S4 — Operator bootstrap password remains in process environment
- The initial operator password is read from an env var and not scrubbed from the
  process environment after bootstrap. Operational, not a code bug, but document
  "unset `ANIMORA_OPERATOR_*` after first boot" in the runbook for defense-in-depth.

---

## 7. Hosting, Config, Transport & Security Headers

### 7.1 — Positive
- Middleware order is fixed, documented, and **test-enforced** (`PlaneOrderTests`):
  exception → forwarded-headers → HSTS/HTTPS → logging → authN → **rate limiter
  (before all DB-backed planes)** → operator ctx → tenant ctx → authZ → entitlement
  → antiforgery → endpoints. Rate-limiting *before* identity DB cost is a deliberate,
  correct DoS defense (`BUG-PIPE-01`).
- `ServerForwardedHeaders`: proxy IP/scheme only honored from an explicit
  KnownProxies/KnownNetworks allowlist (fail-safe no-op otherwise) → no
  `X-Forwarded-For` spoofing of rate-limit/audit keys.
- `KestrelProfile`: 2 MiB body cap, header timeout 15 s, min data-rate (slowloris
  defense), bounded concurrent/upgraded connections.
- `ServerRateLimiting`: five named fixed-window policies, tenant/user/IP partitioning;
  429 + `Retry-After`. Health endpoints never limited.
- `SecretGuard`: scans JSON config for credential-named keys to prevent accidental
  secret-in-appsettings. `SensitiveLogNames` + `RedactingEnricher` strip
  password/token/authorization/valet/connection fragments from logs.
- `GlobalExceptionMiddleware`: typed `WireApiError` + correlation id; no stack leak.
- `ServerSurfacePolicy`: **unknown routes are tenant-bound + entitlement-gated by
  default** (fail-closed) — exemplary.

### 7.2 — S4 — Admin CSP has no `script-src`/`style-src` and no nonce
- **File:** `Admin/AdminSecurityHeadersMiddleware.cs`
- CSP relies on `default-src 'self'`; there is no per-response nonce and no explicit
  `script-src`/`style-src`/`frame-ancestors` tightening. For a Blazor Server admin
  this is generally acceptable, but adding `frame-ancestors 'none'`, explicit
  `script-src 'self'`, and (ideally) a nonce would close the last XSS-hardening gap.

### 7.3 — S4 — `JsonCompressionMiddleware` buffers the whole command response
- **File:** `Observability/JsonCompressionMiddleware.cs`
- It buffers the command response into a `MemoryStream` to decide compression
  eligibility (`>= 1 KiB`). Scope is **only** the bounded `/api/commands` response
  (file/feed streams are excluded), and Kestrel caps the body at 2 MiB, so worst-case
  per-request buffer is bounded. Acceptable, but it is extra managed allocation /
  copy on every command response; consider `Vary`-aware streaming compression via the
  framework's `ResponseCompression` on an allowlisted content-type instead.

### 7.4 — S4 — OpenAPI can be exposed anonymously outside Development
- Guarded by an explicit, loud warning log when `Server:EnableOpenApi=true` in
  non-Development. Ensure deployment keeps it off or network-restricted; the schema
  is anonymous.

---

## 8. Observability, Resilience & Workers

### 8.1 — Positive
- `LoggingSetup`: structured Serilog, **no raw request path/query/headers/body**,
  destructuring policy + redaction. Strong PII posture for a clinic domain.
- `CircuitBreaker`: generation-fenced, single half-open probe, thread-safe (per-process, §2.3).
- `OutboxRelayWorker`: claim-leased delivery, circuit breaker, bounded batch, poison
  handling, `CommandClaimLost` warning on lost claims. Solid at-least-once relay.
- Readiness/liveness/startup probes split correctly; `/health/ready` serves a
  short-TTL cached report (`BUG-HEALTH-01`) so polling cost is bounded; startup probe
  latches "started" forever (orchestrator-correct).
- `AuthzOperationGuard` (`IMP-06`): per-operation DB budget → a slow authz query is
  cancelled and surfaced as retryable `503 SERVER_BUSY` instead of pinning a pooled
  connection for the full 30 s Npgsql timeout. Excellent.

### 8.2 — S4 — Sync-over-async in one `Dispose`
- **File:** `Observability/QueryBudget.cs:16` —
  `DisposeAsync().AsTask().GetAwaiter().GetResult()`. Standard sync-dispose-over-async
  bridge; low risk (no sync-context deadlock in ASP.NET Core). Prefer the object be
  consumed via `await using` everywhere so the sync `Dispose` is never hit.

---

## 9. Build / Test Verification Gap (process finding)

- **S3 — The review could not be build- or test-verified in this sandbox.**
  `global.json` pins **.NET SDK 10.0.401**; the SDK is **not installed**
  (`dotnet: command not found`; the install attempt left no `~/.dotnet`). All
  findings above are from static analysis. Before claiming "100/100", run in a
  real toolchain:
  - `dotnet build -warnaserror` (catch analyzer/nullable warnings),
  - `dotnet test` (157 server test files; confirm green),
  - a container/load smoke test for the multi-instance behaviors in §2.
- `RestorePackagesWithLockFile=true` + committed `packages.lock.json` is good supply-chain hygiene.

---

## 10. Realistic Score & The Exact Gap to 100

### Score: **92 / 100**

| Dimension | Weight | Score | Notes |
|-----------|-------:|------:|-------|
| Security (authn/z, crypto, files, headers) | 25 | 23 | No S1. Gaps: §3.1 disable-stamp, §7.2 CSP, §6.1 unguarded unprotect. |
| Correctness & sync integrity | 25 | 24 | Idempotent, fenced, deadlock-safe; client/server parity test-enforced. |
| Multi-instance / scale-out | 15 | 10 | §2 per-process state is the dominant deduction. |
| Performance | 10 | 9 | Rate-limit-before-DB, bounded buffers, authz budget. §7.3 minor. |
| Standards / API hygiene | 10 | 9 | Fail-closed surface, typed errors, contract versioning. |
| Observability / resilience | 10 | 9.5 | Redacted logs, probes, circuit breaker, outbox relay. |
| Verification (build/test run) | 5 | 2.5 | Could not build/test here (§9). |
| **Total** | **100** | **92** | |

### To reach a realistic 100/100, fix in this order:
1. **§2.1** Distributed/shared AuthZ-cache invalidation (or `TtlSeconds=0`) — removes the main multi-instance revocation lag. **(highest impact)**
2. **§2.2** Persist provisioning replay nonces — global replay protection.
3. **§3.1** Rotate security stamp on staff **disable** (match device revoke) — immediate, consistent revocation.
4. **§6.1** Guard the provisioning GET `Unprotect`/`Deserialize` with typed error mapping.
5. **§7.2** Tighten admin CSP (`frame-ancestors 'none'`, explicit `script-src`, nonce).
6. **§9** Build with `-warnaserror` and run the full test suite + a 2-replica smoke test to *prove* the above.
7. **§4.3 / §6.3 / §8.2** Low-priority polish (exact recovery cap, env scrub runbook, `await using` for QueryBudget).

### Bottom line
This is a **production-grade single-instance server** with unusually strong
correctness and security discipline. There are **no exploitable S1 issues and no
data-loss bugs**. The 8-point gap is almost entirely **honest, documented
scale-out state** (which becomes behavioral only at >1 replica) plus a handful of
minor hardening items and the inability to build/test-verify in this sandbox. Close
§2.1–§2.2, §3.1, §6.1, §7.2 and prove it with a green test run and a multi-replica
smoke test, and 100/100 is attainable.
