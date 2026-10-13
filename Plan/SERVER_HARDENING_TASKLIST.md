# Plan — Server Hardening (from SERVER_CODE_REVIEW_2026-10-13)

> **Source of truth**: `docs/SERVER_CODE_REVIEW_2026-10-13.md` (a static senior
> architecture audit of `src/Animora.Server` + server-facing
> `src/Animora.Persistence.PostgreSql`). This file distills that review into an
> **executable task list**. All findings were spot-verified against the live
> source on 2026-10-13 (line anchors, status-quo code, and that referenced
> helpers exist). An executing model can start implementing with **minimal
> re-reading** — the needed facts are inlined per task.
>
> **Goal of the phase**: close every finding in the review to reach the review's
> own "100/100" bar across technical quality, performance, bug-freedom,
> scalability, security, and consistency. Work is grouped into the review's three
> phases: **A (BLOCKER/CRITICAL — unblock production scale)**, **B (HIGH/MEDIUM —
> correctness/robustness)**, **C (LOW/INFO + improvements — consistency/polish)**.

---

## HARD RULES FOR THE EXECUTING MODEL (read first)

1. **PUSH AFTER EVERY TASK (locked rule).** The instant a task's status flips to
   `[x] DONE`, run from repo root:
   ```bash
   scripts/git/push.sh "type(scope): message (TASK-ID)"
   ```
   If it exits `3` (no creds) → call the `setup_github_environment` tool once, re-run.
   If it exits `2` (rebase conflict) → resolve preferring remote, re-run. **Not pushing = project death.**
2. **Work on branch `genspark_ai_developer`.** First push of the session:
   `scripts/git/push.sh -b genspark_ai_developer "..."`.
3. **Targeted reading only.** Each task lists the exact files/lines to open. Do
   **not** browse unrelated code. Do **not** run full-solution builds or the long
   test suite; build/test only the narrowly-affected project if quick, else rely
   on focused reasoning. (Long/full test+build is forbidden per phase rules.)
4. **If a task produces a large/heavy fix** (big new file, cross-cutting refactor),
   push it on its own with a clear message — do not batch it with later tasks.
5. **Preserve the positives in review §9** (tenant isolation, auth hardening,
   fenced idempotent intake, secret hygiene, fail-closed defaults). Do not regress them.
6. **Update this file's checkbox + "Done note"** for each task as you complete it,
   then push. The **final task (T-END)** deletes THIS file only — touch nothing
   else (no roadmap files).
7. **Status markers**: `[ ] TODO`, `[~] IN-PROGRESS`, `[x] DONE`, `[!] BLOCKED`.

---

## VERIFIED ENVIRONMENT FACTS (no need to re-discover)

- Server project: `src/Animora.Server`; server-facing persistence:
  `src/Animora.Persistence.PostgreSql`. Target framework: **.NET 10**.
- Pool sizes live in `src/Animora.Server/appsettings.json`:
  `OltpWriteMax: 6`, `OltpReadMax: 3`, `WorkerMax: 2` (keys under the DB/pool section).
- Route constants: `src/Animora.Server/Hosting/ServerRoutes.cs` —
  `Commands="/api/commands"`, `Sync="/sync"`, `Auth="/auth"`, `Operator="/operator"`,
  `Provisioning="/provisioning"`, `Licensing="/licensing"`, `Files="/files"`,
  `Admin="/admin"`, `HealthLive="/health/live"`, `HealthReady="/health/ready"`.
- `TenantScopedMemoryCache` ALREADY EXISTS at
  `src/Animora.Server/Observability/TenantScopedMemoryCache.cs` — reuse it for caching tasks.
- Admission gate is registered at
  `src/Animora.Persistence.PostgreSql/DependencyInjection/PersistenceServiceCollectionExtensions.cs:103`
  as `services.TryAddSingleton<IBackgroundAdmission>(_ => new SemaphoreBackgroundAdmission(1));`.
  Interface + impl: `.../Replication/IBackgroundAdmission.cs` (`SemaphoreBackgroundAdmission(int concurrency=1)`,
  one `SemaphoreSlim`). `BatchFreezer.OpenAsync` (`.../Replication/BatchFreezer.cs:31`)
  does `await using var lease = await admission.EnterAsync(ct)` — it shares the SAME singleton.
- Middleware order in `src/Animora.Server/Program.cs` (verified ~line 144-166):
  `GlobalExceptionMiddleware` → (non-Dev) `UseHsts`+`UseHttpsRedirection` →
  `RequestLoggingMiddleware` → JsonCompression (commands only) →
  `AdminSecurityHeadersMiddleware` → `UseAuthentication` → `OperatorContextMiddleware` →
  `TenantContextMiddleware` → `UseAuthorization` → `EntitlementMiddleware` →
  **`UseRateLimiter()`** → `UseAntiforgery` → `AdminAntiforgeryMiddleware` → endpoints.
  (This confirms BUG-PIPE-01: limiter runs AFTER authN/authZ/entitlement.)
- Rate limiter: `src/Animora.Server/Hosting/ServerRateLimiting.cs:54-66` — every
  policy partitions by `httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`;
  uses `GetFixedWindowLimiter`; policy list in `RateLimitPolicies.All`; windows from
  `ServerOptions.RateLimits.ForPolicy(policyName)`.
- Reporting budget: `src/Animora.Server/Observability/QueryBudget.cs` —
  `private readonly SemaphoreSlim _reporting=new(1,1);` (one reporting query server-wide).
  `QueryClass.Oltp` returns a no-op lease; `Reporting` serializes through `_reporting`.
- There are TWO `ServerEntitlementGate` types: `Authorization/ServerEntitlementGate.cs`
  (interface + `AllowAllServerEntitlementGate`) and `Licensing/ServerEntitlementGate.cs`
  (real impl with `IsExempt`). Registration uses `TryAdd` — confirm which wins.
- Existing tests project references `PlaneOrderTests` (pipeline order) — reuse it to
  validate BUG-PIPE-01.

---

## PHASE A — Unblock production scale (BLOCKER / CRITICAL)

### [x] A1 · SCALE-ADM-01 — Split the single admission semaphore (BLOCKER)
- **Why**: `IBackgroundAdmission` = `SemaphoreBackgroundAdmission(1)` is a single
  process-wide permit shared by ~12 background workers AND the user-facing sync
  path (`BatchFreezer.OpenAsync`). A sync snapshot build can hold the one permit
  up to ~10s, during which no worker runs and no other sync batch can start →
  hard per-instance concurrency ceiling of 1 for a user-facing op.
- **Files**:
  - `src/Animora.Persistence.PostgreSql/Replication/IBackgroundAdmission.cs`
  - `src/Animora.Persistence.PostgreSql/DependencyInjection/PersistenceServiceCollectionExtensions.cs:103`
  - `src/Animora.Persistence.PostgreSql/Replication/BatchFreezer.cs:31` (the `OpenAsync` `EnterAsync`)
  - Workers that call `EnterAsync` (keep them on the WORKER gate):
    `Workers/OutboxRelayWorker.cs:20`, `Workers/SubscriptionSweepWorker.cs:17`,
    `Workers/IntakeRecoveryWorker.cs:19`, `Workers/OutboxCleanupWorker.cs:13`,
    `Workers/CommandIntakeRetentionWorker.cs:16`, `Provisioning/ProvisioningIntentWorker.cs:22`,
    `Provisioning/ProvisioningIntentRetentionWorker.cs:15`, `Files/FileReconciliationWorker.cs:25`,
    `Observability/SnapshotAgeMonitor.cs:29`, `Licensing/LicenseKeyBootstrapper.cs:21`,
    plus `FeedRetentionService`.
- **Do**:
  1. Introduce TWO gate roles. Simplest non-breaking design: add a second marker
     interface `ISyncAdmission : IBackgroundAdmission` (request-path gate) and keep
     `IBackgroundAdmission` for workers. Register:
     - `IBackgroundAdmission` → `SemaphoreBackgroundAdmission(workerConcurrency)` (keep small, 1–2).
     - `ISyncAdmission` → `SemaphoreBackgroundAdmission(syncConcurrency)` sized to
       the write pool minus headroom (default e.g. `max(2, OltpWriteMax - 2)`), read
       from options (see IMP-09/A-config) — do NOT hardcode if an option is trivial to add.
  2. Point `BatchFreezer` at `ISyncAdmission`; leave all workers on `IBackgroundAdmission`.
  3. (Stretch, optional) per-tenant fairness: one in-flight snapshot per tenant.
     Only do if cheap; otherwise leave a TODO note — the gate split is the blocker fix.
- **Accept**: request sync no longer blocks workers and vice-versa; sync concurrency > 1.
- **Push**: `scripts/git/push.sh "perf(server): split admission into worker+sync gates (A1 SCALE-ADM-01)"`
- **Done note**: DONE. Added `ISyncAdmission : IBackgroundAdmission` + `SemaphoreSyncAdmission`
  in `Replication/IBackgroundAdmission.cs`. `BatchFreezer` now depends on `ISyncAdmission`
  (request path); ALL ~12 workers + `FeedRetentionService` stay on `IBackgroundAdmission`
  (worker gate). DI (`PersistenceServiceCollectionExtensions.cs`): worker gate =
  `Math.Clamp(WorkerMax,1,2)`, sync gate = `Math.Max(2, OltpWriteMax-2)` (headroom for
  mutations). `ServerWorkersServiceCollectionExtensions.cs` TryAdd fallbacks updated to add
  `ISyncAdmission`. Per-tenant fairness deferred (stretch, not the blocker). NOT compiled —
  no .NET SDK in sandbox (build/test forbidden by phase rules); verified by targeted review.

### [x] A2 · SEC-RL-01 + SEC-FWD-01 + CONS-RL-01 — Tenant/user-scoped limits + trusted forwarded headers (BLOCKER)
- **Why**: Every rate-limit policy partitions by raw `RemoteIpAddress` only
  (`ServerRateLimiting.cs:54-66`), despite the comment claiming tenant scoping
  arrives with S04/S05 (which ARE wired). Behind an LB this is per-process (limits
  × N instances), collapses all clients to the proxy IP (or is X-Forwarded-For
  spoofable), and grows memory unboundedly (no eviction). `Program.cs` has NO
  `UseForwardedHeaders`.
- **Files**: `src/Animora.Server/Hosting/ServerRateLimiting.cs:54-66`; `src/Animora.Server/Program.cs`
  (add forwarded headers early, before authN); tenant/user come from
  `TenantContextMiddleware` (`ITenantContext`/`ICurrentUser`-equivalent — confirm the
  accessor the authZ pipeline already uses).
- **Do**:
  1. Add `UseForwardedHeaders` EARLY in the pipeline (right after
     `GlobalExceptionMiddleware`, before HSTS/logging/authN) with an explicit
     `ForwardedHeadersOptions` allowlist: `ForwardedHeaders = XForwardedFor | XForwardedProto`,
     and a configured `KnownProxies`/`KnownNetworks` (from options; default empty =
     trust none, so document that the operator MUST set proxy IPs in prod). Never
     trust `X-Forwarded-For` without the allowlist.
  2. In `ServerRateLimiting`, partition the AUTHENTICATED policies
     (`commands`, `sync`, `files`, `operator`) by **tenantId + userId** when available
     (resolved post-`TenantContextMiddleware`); keep `auth` partitioned by the
     **normalized client IP derived from the forwarded-headers result**.
  3. Bound partition cardinality / add sliding eviction so IP rotation can't grow memory.
  4. Fix the stale comment (CONS-RL-01) to describe actual behavior.
  5. (Multi-instance, note-only unless scoped in): a distributed limiter (Redis token
     bucket) or DB per-tenant quota is the real multi-instance fix — if out of scope
     for this phase, leave a clear `// TODO(multi-instance): distributed limiter` and
     document in the Done note. The tenant/user partition + forwarded headers is the committed deliverable.
- **Accept**: authenticated policies keyed by tenant+user; auth keyed by trusted IP;
  comment matches code; forwarded headers wired with allowlist.
- **Push**: `scripts/git/push.sh "sec(server): tenant/user-scoped rate limits + trusted forwarded headers (A2 SEC-RL-01/SEC-FWD-01)"`
- **Done note**: DONE. (1) Added `ForwardedHeadersSettings` to `ServerOptions` (Enabled/KnownProxies/
  KnownNetworks/ForwardLimit; default Enabled=false = trust nothing). (2) New
  `Hosting/ServerForwardedHeaders.cs` applies `UseForwardedHeaders` with an explicit allowlist;
  wired early in `Program.cs` right after `GlobalExceptionMiddleware`, before HSTS/redirect.
  (3) `ServerRateLimiting.ResolvePartitionKey`: auth plane keys on trusted client IP; all other
  (authenticated) planes key on `t:<tenant>:u:<user>` via `AuthenticatedCaller.TryRead(User)`,
  falling back to IP pre-principal. (4) Comment fixed (CONS-RL-01). (5) Validator fails boot if
  ForwardedHeaders enabled without allowlist. `IPNetwork` fully-qualified as `System.Net.IPNetwork`
  to avoid ambiguity with the deprecated HttpOverrides type. MULTI-INSTANCE: distributed limiter
  is still a documented follow-up (in-memory partitions remain per-process); the tenant/user
  partition + trusted IP + allowlist is the committed single-instance-correct deliverable.

### [x] A3 · SCALE-STATE-01 — Durable shared provisioning replay window + document single-instance state (CRITICAL)
- **Why**: `Provisioning/ReplayWindow.cs:4-19` is in-memory single-host,
  `Capacity=4096`, **fail-closed**: `_entries.Count >= Capacity ⇒ return false` →
  a burst of >4096 distinct signatures in 5 min denies ALL further provisioning
  (self-DoS). Also per-instance ⇒ replay routed to a different instance is not
  detected (HMAC ±5-min window in `RequestSignatureVerifier.cs:22` still bounds it,
  but exactly-once across instances is lost). `AdminStatusCache.cs:5`,
  `Resilience/CircuitBreaker.cs`, `Workers/ResilientWorker.cs:3-16`
  (`WorkerFailureLogGate`), `Observability/QueryBudget.cs:19` are also per-process.
- **Files**: `src/Animora.Server/Provisioning/ReplayWindow.cs`;
  `src/Animora.Server/Provisioning/RequestSignatureVerifier.cs:22`; the others for documentation.
- **Do**:
  1. Replace the hard fail-closed cap behavior: on capacity, **shed** (evict oldest
     / degrade) instead of deny-all. Minimum committed fix = remove the self-DoS.
  2. Make replay protection **durable + shared**: a DB table keyed by
     `(keyId, signature)` with a TTL/expiry index (prune older than the ±5-min
     window), checked with an idempotent insert (`INSERT ... ON CONFLICT DO NOTHING`
     → if conflict, it's a replay). This mirrors the existing intake pattern. If a
     Redis backplane is preferred and available, `SET key NX EX 300` is equivalent.
     If durable store is genuinely out of scope here, the self-DoS removal (step 1)
     is mandatory and the shared store is a documented TODO.
  3. Add/extend a short "single-instance components" doc note (in code XML doc or a
     short section the plan's Done note references) listing `AdminStatusCache`,
     `CircuitBreaker`, `WorkerFailureLogGate`, `QueryBudget` as per-process-by-design
     and gating multi-instance on replacing/accepting them. (QueryBudget handled in A5.)
- **Accept**: no deny-all at capacity; replay detection survives instance change
  (if durable store done); single-instance state documented.
- **Push**: `scripts/git/push.sh "sec(server): durable shared provisioning replay window, drop fail-closed self-DoS (A3 SCALE-STATE-01)"`
- **Done note**: DONE. Rewrote `Provisioning/ReplayWindow.cs`: at capacity it now SHEDS the oldest live entry (FIFO queue, O(1) expiry) instead of denying all traffic, removing the provisioning self-DoS; replay exposure stays bounded by the +/-5-min HMAC window. XML doc lists the per-process single-instance components (ReplayWindow, AdminStatusCache, CircuitBreaker, WorkerFailureLogGate, QueryBudget, rate-limit partitions) and carries `TODO(multi-instance)` for the shared DB/Redis store (out of scope: would need a schema migration). Regression test updated (shed instead of deny). Not compiled locally (CI only).

### [x] A4 · PERF-AUTHZ-01 + CONS-ENT-01 — Cache authZ/entitlement, read-pool lookups, drop duplicate entitlement check (HIGH)
- **Why**: Every authenticated tenant-bound request runs `MembershipResolver`
  (`Authorization/MembershipResolver.cs:31-72`, 4 reads: user-by-id, membership,
  tenant, device — each "deliberately no cache") PLUS `EntitlementMiddleware`
  (`:27`) → `Licensing/ServerEntitlementGate.cs:19-30` (up to 3 reads) = 5–7
  serialized DB round-trips BEFORE any work, all on the WRITE context
  (`AnimoraServerDbContext`, `OltpWriteKey`). With `OltpWriteMax=6`, ~6 concurrent
  authed requests can exhaust the write pool. For commands, entitlement is evaluated
  AGAIN inside `CommandIntakeService.cs:122` (CONS-ENT-01).
- **Files**: `Authorization/MembershipResolver.cs:31-72`,
  `Authorization/EntitlementMiddleware.cs:27`, `Licensing/ServerEntitlementGate.cs:19-30`,
  `Commands/CommandIntakeService.cs:122`, read path `IServerReadContextFactory`
  (registered in `PersistenceServiceCollectionExtensions.cs` via `OltpReadKey`),
  cache helper `Observability/TenantScopedMemoryCache.cs`, pool config `appsettings.json`.
- **Do**:
  1. Add a short-TTL (5–15s) tenant-scoped cache (reuse `TenantScopedMemoryCache`)
     for membership resolution + entitlement verdict. Key by tenant+user. Because the
     security-stamp is already compared, a stale entry self-corrects within TTL —
     but still invalidate on security-stamp rotation where cheap.
  2. Route these READ-ONLY lookups through the READ pool
     (`IServerReadContextFactory` / `OltpReadKey`), not the write context, so they
     stop consuming write connections.
  3. Remove the DUPLICATE entitlement evaluation: keep exactly one authoritative
     check. Recommended: trust the middleware verdict for plain requests and keep the
     command-type-aware check inside `CommandIntakeService`; have the middleware skip
     the coarse check for `/api/commands` (or vice-versa) — document which is authoritative.
  4. Set an explicit Npgsql pool/connection timeout so exhaustion fails fast (typed
     503) instead of hanging to `CommandTimeout`. (Pool resizing is IMP-01 / task C-IMP1.)
- **Accept**: authZ/entitlement cached per short TTL; lookups on read pool; single
  entitlement evaluation per command path.
- **Push**: `scripts/git/push.sh "perf(server): cache+read-pool authZ/entitlement, drop duplicate entitlement check (A4 PERF-AUTHZ-01/CONS-ENT-01)"`
- **Done note**: DONE. New `Authorization/AuthzDecisionCache.cs` (absolute-TTL, bounded, per-process; `Server:AuthCache:TtlSeconds` default 10, 0 disables) caches ONLY successful membership resolutions (key = user+device+token stamp, so a rotated stamp is always a miss) and positive entitlement verdicts (tenant+device). Explicit invalidation on device approve/revoke/reinstate, staff status/permission changes, operator subscription/licence mutations (admin + operator API) so suspension/revocation is immediate on this instance and converges within the TTL elsewhere. `MembershipResolver` now uses new `IIdentityReadStore` (membership + device lookups on the OLTP READ pool, no tracking); entitlement gate already reads via `AnimoraEntitlementReadContext` (read pool). CONS-ENT-01: `EntitlementMiddleware` skips POST `/api/commands*`; the command-type-aware check in `CommandIntakeService` is the single authoritative one there. Npgsql pool wait is bounded (`Database:PoolWaitSeconds`, default 5, validated 1..60) and pool exhaustion maps to typed 503 `SERVER_BUSY` + Retry-After in `GlobalExceptionMiddleware` via `DatabaseFailure.IsPoolExhausted`. Tests: `AuthzDecisionCacheTests`; test hosts set TtlSeconds=0 and register a fake read store. Not compiled locally (CI only).

### [x] A5 · PERF-QB-01 — Size/partition the reporting query budget (HIGH)
- **Why**: `Observability/QueryBudget.cs:19-20` uses `SemaphoreSlim(1,1)` for
  `QueryClass.Reporting` → ONE report at a time across ALL tenants per instance. One
  slow report blocks every tenant's report.
- **Files**: `src/Animora.Server/Observability/QueryBudget.cs` (the `_reporting` field + `Acquire`).
- **Do**:
  1. Size the reporting budget to the read pool (e.g. `OltpReadMax`) via options
     instead of a hardcoded 1 — `new SemaphoreSlim(reportingMax, reportingMax)`.
  2. (Optional) partition by tenant so one tenant's heavy report can't starve others.
  3. Guard acquisition with a **wait timeout** → return/raise a typed 503 instead of
     blocking indefinitely (tie into the WireApiError pattern used elsewhere).
- **Accept**: reporting concurrency > 1 and configurable; bounded wait with 503 on timeout.
- **Push**: `scripts/git/push.sh "perf(server): size reporting query budget to read pool with wait-timeout 503 (A5 PERF-QB-01)"`
- **Done note**: DONE. `Observability/QueryBudget.cs`: reporting permits now come from `Server:QueryBudget:ReportingMax` (0 = auto = `Database:WorkerMax`, always clamped to the worker pool the sessions actually use), wired in `Program.cs`. Acquisition has a bounded wait (`Server:QueryBudget:WaitSeconds`, default 10); on timeout it throws `QueryBudgetExceededException`, mapped by `GlobalExceptionMiddleware` to typed 503 `SERVER_BUSY` + Retry-After. Added in-use/wait/timeout counters (consumed by C8). Per-tenant partitioning not done: tenant is unknown at Acquire and the shared worker pool is the real limit (C7 scales WorkerMax). Existing 1-permit tests still valid (defaults reportingMax=1); new concurrency/timeout unit test. Not compiled locally (CI only).

---

## PHASE B — Correctness & robustness (HIGH / MEDIUM)

### [x] B1 · BUG-PIPE-01 — Move `UseRateLimiter()` before authentication (HIGH)
- **Why**: Pipeline runs `...authN → tenant → authZ → entitlement → UseRateLimiter()`
  (`Program.cs` ~line 159-165). The limiter runs AFTER the 5–7 DB-query authN/authZ/
  entitlement work, so a flood pays full identity cost before being throttled —
  inverting the limiter's protective intent and amplifying DB pressure under attack.
- **Files**: `src/Animora.Server/Program.cs` (middleware order); validate with `PlaneOrderTests`.
- **Do**: Move `app.UseRateLimiter()` to run BEFORE `app.UseAuthentication()` (right
  after routing/exception/logging and the forwarded-headers from A2). Keep per-endpoint
  `RequireRateLimiting` policies. NOTE ordering dependency: A2 partitions authenticated
  policies by tenant+user which is only known AFTER `TenantContextMiddleware`. Resolve
  by: `auth` policy (IP-based) can throttle pre-authN; the tenant/user-partitioned
  policies attach per-endpoint and evaluate when the endpoint is reached — so the
  GLOBAL `UseRateLimiter()` placement before authN still lets endpoint policies use
  tenant/user keys computed later. Confirm `PlaneOrderTests` still passes.
- **Accept**: limiter positioned before authN; `PlaneOrderTests` green.
- **Push**: `scripts/git/push.sh "fix(server): move rate limiter before authentication (B1 BUG-PIPE-01)"`
- **Done note**: DONE. `Program.cs`: `UseRateLimiter()` moved from after entitlement to directly after `UseAuthentication()` and BEFORE `OperatorContextMiddleware`/`TenantContextMiddleware`/`UseAuthorization`/`EntitlementMiddleware`, so a flood is throttled before any DB-backed work (cookie validation, membership resolve, entitlement). Authentication itself stays first deliberately: bearer tokens are Data-Protection-decrypted in memory (no DB read) and the A2 tenant+user partition keys need the principal; endpoint `RequireRateLimiting` policies still apply (routing is auto-added at pipeline start). Pipeline comment updated. New source-order test `RateLimiterOrderTests` (PlaneOrderTests file); the runtime ordering tests in `PlaneOrderTests` are unaffected (401→403→entitlement ordering unchanged). Not compiled locally (CI only).

### [x] B2 · SEC-STAMP-01 — Persist security-stamp rotation on refresh-reuse (MEDIUM/verify)
- **Why**: On refresh-token REUSE detection (`Auth/LoginService.cs:211-218`), code does
  `victim?.RotateSecurityStamp();` then returns `SessionExpired`. Whether the rotated
  stamp is flushed depends on `RevokeFamilyAsync`/audit calling `SaveChanges` in the
  same UoW. If NOT flushed, stolen access tokens stay valid until natural expiry —
  defeating reuse detection.
- **Files**: `src/Animora.Server/Auth/LoginService.cs:211-218` (+ `RevokeFamilyAsync`, the audit/save path).
- **Do**: Explicitly persist the stamp rotation in the SAME transaction as the
  family revocation + audit; gate the response on the save result (mirror
  `DeviceAdminService`/`StaffProvisioningService` which success-check `AuditAsync`).
  Add a focused test: after a detected reuse the user's stored `SecurityStamp` changed.
- **Accept**: stamp rotation committed; test asserts stored stamp differs post-reuse.
- **Push**: `scripts/git/push.sh "sec(server): persist security-stamp rotation on refresh-reuse (B2 SEC-STAMP-01)"`
- **Done note**: DONE. LoginService reuse branch now captures the audit/save Result of the RefreshReuseDetected write (the audit writer commits the staged RotateSecurityStamp together with the family revocation + audit row in one SaveChanges) and gates the response on it: a failed commit surfaces the typed failure instead of a clean SESSION_EXPIRED. New test Reuse_StagesRotatedStamp_BeforeCommit_AndGatesResponseOnSave asserts the stored stamp differs at commit time and the failure is surfaced (FakeAuditWriter gained OnWrite hook). Not compiled locally (CI only).

### [x] B3 · SEC-ENUM-01 — Idempotent device upsert on login (MEDIUM)
- **Why**: `Auth/LoginService.cs:124-138` auto-registers a `Device` for an unknown
  installation hash during `/auth/login`. Two concurrent first logins can both see
  `device is null` and both `AddDevice(...)` → duplicate rows, or an unhandled
  unique-violation surfacing as 500.
- **Files**: `src/Animora.Server/Auth/LoginService.cs:124-138`; device repo/add path; the
  unique index on `(UserId, InstallationHash)` (confirm it exists in the EF model/migration).
- **Do**: Use an idempotent upsert — `INSERT ... ON CONFLICT (UserId, InstallationHash)
  DO NOTHING` then re-read — mirroring the command-intake pattern. Ensure the unique
  index exists; map the conflict to a typed `Result` (as `ServerUnitOfWork` does for
  rooms/vaccines), NOT a 500. Add a test for the concurrent first-login race.
- **Accept**: no duplicate device rows; unique violation mapped to typed result; test added.
- **Push**: `scripts/git/push.sh "fix(server): idempotent device upsert on login to fix first-login race (B3 SEC-ENUM-01)"`
- **Done note**: DONE. Added IIdentityRepository.AddDeviceIdempotentAsync (EfIdentityRepository: insert+SaveChanges; on unique violation 23505 on the existing unique (tenant,user,installation) index it detaches the candidate and converges on the winner's row; otherwise typed ConcurrencyConflict, never a 500). LoginService device auto-register now uses it (loser adopts winner, deviceIsNew only for the inserting call, revoked winner is refused). Index confirmed in DeviceConfiguration (TenantIndex(InstallationIdHash, UserId).IsUnique()). Fake repo gained the method + OnBeforeAddDevice hook; new test ConcurrentFirstLogin_ConvergesOnOneDevice_NoDuplicateRows. Not compiled locally (CI only).

### [x] B4 · SEC-ADV-01 — Split advisory-lock key into two int4 keys (MEDIUM)
- **Why**: `Persistence/Repositories/MoneyResourceLock.cs:15-18` uses
  `pg_advisory_xact_lock(hashtextextended($"{tenantId}:{resource}", 0))` — one 64-bit
  key from a concatenated string. Hash collisions cause FALSE mutual exclusion between
  unrelated financial resources (incl. cross-tenant), a latent contention source.
- **Files**: `src/Animora.Persistence.PostgreSql/Repositories/MoneyResourceLock.cs:15-18`.
- **Do**: Switch to the two-key overload `pg_advisory_xact_lock(key1 int4, key2 int4)`
  with `key1 = hashtext(tenantId)`, `key2 = hashtext(resource)` to make cross-domain
  collisions far less likely. Keep the existing deterministic ordering (already sorted)
  to avoid deadlocks. If keeping the single key, document the accepted collision prob.
- **Accept**: lock uses two-key form keyed by tenant + resource; ordering preserved.
- **Push**: `scripts/git/push.sh "fix(server): two-key advisory lock to avoid cross-domain collisions (B4 SEC-ADV-01)"`
- **Done note**: DONE. MoneyResourceLock now takes pg_advisory_xact_lock(hashtext(tenantId), hashtext(resource)) (two int4 keys) per resource, still acquired in distinct ordinal-sorted order (deadlock-free); collision now needs both 32-bit hashes to match. Not compiled locally (CI only).

### [x] B5 · BUG-UOW-01 — Do not release savepoint on the rollback path (MEDIUM/verify)
- **Why**: `Commands/CommandPlaneUnitOfWork.cs:42-48` on nested failure does
  `RollbackToSavepointAsync(savepoint)` then UNCONDITIONALLY `ReleaseSavepointAsync`.
  Post-rollback release is redundant and, if the driver treats it inconsistently, can
  throw and convert a clean domain rejection into a transaction-aborting exception.
- **Files**: `src/Animora.Persistence.PostgreSql/Commands/CommandPlaneUnitOfWork.cs:42-48`.
- **Do**: On the failure branch, `RollbackToSavepointAsync` then simply return (NO
  release). Release only on the success branch. Add a test driving a nested rejection
  asserting the outer transaction still commits the settlement receipt.
- **Accept**: release only on success; nested-rejection test proves outer commit survives.
- **Push**: `scripts/git/push.sh "fix(server): don't release savepoint on rollback path (B5 BUG-UOW-01)"`
- **Done note**: DONE. CommandPlaneUnitOfWork nested path: on a failed nested Result it now RollbackToSavepoint + ChangeTracker.Clear and returns immediately; ReleaseSavepointAsync runs only on the success branch (the exception path already rolled back without release). New PG test S08NestedRejectionTests proves a nested rejection discards only its own effect while the outer transaction still commits the later write (settlement receipt). Not compiled locally (CI only).

### [x] B6 · BUG-SIGN-01 — Guard signing-key creation + cache the active signer (MEDIUM)
- **Why**: `Signing/SigningKeyStore.cs:23-70` `GetAsync` finds-or-CREATES a key; on a
  cold DB two concurrent issuances can both miss and both `CreateAsync` → two "active"
  keys. Also every call re-queries + re-unprotects the PKCS#8 private key via Data
  Protection on the per-capability-issuance hot path.
- **Files**: `src/Animora.Server/Signing/SigningKeyStore.cs:23-70`;
  `src/Animora.Server/Signing/EcdsaSnapshotSigner.cs`.
- **Do**: Create the signing key under a transaction + unique constraint (one active
  snapshot key) OR a PostgreSQL advisory lock during creation. Cache the resolved
  `ISnapshotSigner` in memory keyed by `KeyId`, invalidated on rotation — so steady-state
  signing does no DB read and no repeated unprotect.
- **Accept**: concurrent cold-start yields one active key; steady-state signing hits no DB/unprotect.
- **Push**: `scripts/git/push.sh "fix(server): guard signing-key creation and cache active signer (B6 BUG-SIGN-01)"`
- **Done note**: DONE. SigningKeyStore: first-key creation now runs in a transaction under pg_advisory_xact_lock(hashtext('animora.signing-keys'), hashtext(purpose)) and re-reads the active key under the lock, so a cold-start race (any number of instances) yields ONE active snapshot key (losers adopt the winner). New singleton ActiveSignerCache keeps the resolved signer (private key already unprotected/imported) for 5 min (capped by key NotAfterUtc; Invalidate() for rotation) so steady-state signing does no DB read and no Data-Protection unprotect; documented as per-process state converging within the window. DI registers the cache. New PG test SigningKey_ConcurrentColdStart_YieldsOneActiveKey_AndSteadyStateIsCached. Not compiled locally (CI only).

### [x] B7 · BUG-CORR-01 — Memoize correlation id in `HttpContext.Items` (MEDIUM)
- **Why**: `Hosting/CorrelationHeader.cs:24-40` `Resolve` mints a FRESH ULID each call
  when no valid inbound `X-Correlation-Id`. Correctness relies on the implicit contract
  "resolve once in `GlobalExceptionMiddleware`, echo, then read from the response header"
  (as `CommandIntakeService.cs:51` does). Any second `Resolve` before echo gets a different id.
- **Files**: `src/Animora.Server/Hosting/CorrelationHeader.cs:24-40`; consumers.
- **Do**: Memoize the id in `HttpContext.Items` on first `Resolve`; all consumers read
  the memoized value. Makes "same value everywhere" structural, not conventional.
- **Accept**: repeated `Resolve` returns the same id within a request.
- **Push**: `scripts/git/push.sh "fix(server): memoize correlation id in HttpContext.Items (B7 BUG-CORR-01)"`
- **Done note**: DONE. CorrelationHeader.Resolve now memoizes the id in HttpContext.Items on first call (valid inbound header reused, else ONE minted ULID); Echo also stores the echoed value, so every Resolve/Echo within a request agrees structurally. All ~50 existing call sites benefit unchanged. New CorrelationHeaderTests. Not compiled locally (CI only).

### [x] B8 · BUG-BATCH-01 — Per-item results instead of whole-batch abort (MEDIUM)
- **Why**: `Commands/CommandEndpoints.cs:47-55` `BatchAsync` processes envelopes
  sequentially; on the first `settled.IsFailure` it returns ONE `TransportError` for
  the whole batch, discarding `results` already settled server-side. Idempotency makes
  this not data-loss, but it's partial-success reported as total-failure forcing full retry.
- **Files**: `src/Animora.Server/Commands/CommandEndpoints.cs:47-55`.
- **Do**: Return a PER-ITEM result array where each item carries its own outcome
  (including a transport-ish failure as a typed per-item settlement). Reserve a true
  HTTP error only for whole-batch transport problems (malformed JSON, too large).
- **Accept**: earlier committed items reported as success; only whole-batch problems error.
- **Push**: `scripts/git/push.sh "fix(server): return per-item batch command results (B8 BUG-BATCH-01)"`
- **Done note**: DONE. BatchAsync no longer collapses a mid-batch failure into one TransportError: items that already settled keep their real SettlementResult; the failing item and the not-yet-attempted remainder (order is a contract) get a typed per-item Deferred result (ErrorCode + ResponsibleRole, FinancialEffect Unknown, retry unchanged). A true HTTP error is kept only for whole-request problems (malformed/too large/invalid before any item runs) and for request-level failures (contract version/auth/entitlement) on the FIRST item before anything committed. New test MidBatchStoreFailure_KeepsEarlierSettlements_AndDefersTheRest_PerItem (FakeAudit.FailAfterWrites). Not compiled locally (CI only).

### [x] B9 · BUG-FILE-01 — Stream uploads instead of buffering + ToArray (MEDIUM)
- **Why**: `Files/FileEndpoints.cs:63-66` reads the body into a `MemoryStream` then
  `memory.ToArray()` (second full copy) before `WriteAsync`. With `MaxChunkBytes=1 MiB`
  each in-flight upload holds ~2 MiB (LOH), driving Gen2/LOH GC pressure; `files`
  rate limit (60/min) doesn't bound concurrency.
- **Files**: `src/Animora.Server/Files/FileEndpoints.cs:63-66`; `Files/FileStore.cs`.
- **Do**: Stream the request body directly to the staging `FileStream` with offset
  validation, avoiding the full buffer + `ToArray`. Bound concurrent uploads with a
  semaphore (not only the rate limiter). Keep the existing `Flush(true)` fsync.
- **Accept**: no full-buffer copy; concurrent uploads bounded.
- **Push**: `scripts/git/push.sh "perf(server): stream file-chunk uploads and bound concurrency (B9 BUG-FILE-01)"`
- **Done note**: DONE. Upload chunk path no longer buffers through MemoryStream + ToArray: it reads the body into ONE right-sized byte[] (ContentLength-sized, bounded growth to MaxChunkBytes, oversize detected by a 1-byte probe). New FileUploadGate (singleton semaphore, Server:Files:MaxConcurrentUploads=16, UploadWaitSeconds=5, validated, in appsettings) bounds concurrent uploads; a full gate returns 503 SERVER_BUSY + Retry-After. FileStore.WriteChunkAsync keeps Flush(true). Not compiled locally (CI only).

### [x] B10 · BUG-RECOVERY-01 + BUG-OUTBOX-01 — Decouple recovery/outbox throughput from admission gate (MEDIUM)
- **Why**: `Workers/IntakeRecoveryWorker.cs:17-39` is tenant-serial, cap `remaining=50`
  expired leases TOTAL per 30s tick (≤100 rows/min globally). `Workers/OutboxRelayWorker.cs:20,26,32`
  claims at most 2 messages with 2 consumers and holds the global admission permit the
  whole tick. Both throttle behind the single admission gate (now split in A1).
- **Files**: `src/Animora.Server/Workers/IntakeRecoveryWorker.cs:17-39`;
  `src/Animora.Server/Workers/OutboxRelayWorker.cs:20,26,32`.
- **Do**: Make recovery cap per-tenant or adaptive; parallelize bounded recovery.
  Make outbox claim size / consumer count configurable (options) and independent of
  the sync admission gate (use the WORKER gate from A1, scaled with `WorkerMax`).
- **Accept**: recovery drains faster under backlog; outbox concurrency configurable and sync-independent.
- **Push**: `scripts/git/push.sh "perf(server): decouple intake-recovery/outbox throughput, make configurable (B10 BUG-RECOVERY-01/BUG-OUTBOX-01)"`
- **Done note**: DONE. New `Server:Workers` options (`WorkerSettings`, validated at boot; defaults in appsettings.json): `OutboxClaimBatch` (10, <=20), `OutboxConsumers` (4), `RecoveryPerTenantCap` (10), `RecoveryTotalCap` (200 per tick, was 50 total), `RecoveryParallelism` (4). `IntakeRecoveryWorker` now caps recovery PER TENANT (no starvation by one deep backlog), processes tenants with bounded `Parallel.ForEachAsync`, takes the WORKER gate only per tenant unit, and isolates per-tenant failures. `OutboxRelayWorker` claim size/consumer count come from options, and the worker-gate permit is released right after the claim (delivery no longer holds it); validator enforces `ceil(batch/consumers) * 10 s` stays inside the 60 s claim lease. Both stay on `IBackgroundAdmission` (A1 worker gate), independent of `ISyncAdmission`. Not compiled locally (CI only).

---

## PHASE C — Consistency, polish & improvements (LOW / INFO + IMP-*)

### [x] C1 · CONS-GATE-01 — Centralize plane-exemption policy (MEDIUM)
- **Why**: THREE divergent "which paths bypass this plane" lists exist, a classic drift source:
  - `Authorization/TenantContextMiddleware.IsSkipped` (`:31-40`): `/health`, `/admin`,
    `/_blazor`, `/operator`, `/provisioning`, `/auth/login`, `/auth/refresh`, `/auth/keys`, `/licensing/keys`.
  - `Licensing/ServerEntitlementGate.IsExempt` (`:10`): `/health`, `/auth`, `/licensing`,
    `/operator`, `/provisioning`, `/admin`.
  - `Authorization/EntitlementMiddleware` additionally checks `IsExempt` AND `/admin`.
  Note exact-path (TenantContext) vs prefix (Entitlement) matching mismatch is fragile.
- **Files**: `Authorization/TenantContextMiddleware.cs:31-40`,
  `Licensing/ServerEntitlementGate.cs:10`, `Authorization/EntitlementMiddleware.cs`,
  `Authorization/ServerEntitlementGate.cs` (interface + `AllowAllServerEntitlementGate`).
- **Do**: Introduce ONE `ServerSurfacePolicy` (named route classes) consumed by both
  middlewares. Add a test asserting every mapped endpoint has a defined (gated or
  exempt) classification. Remove the `Authorization/ServerEntitlementGate.cs` allow-all
  once the real `Licensing` gate is the registered impl — confirm `TryAdd` ordering picks the real one.
- **Accept**: single policy source; test covers all endpoints; allow-all gate removed/confirmed unused.
- **Push**: `scripts/git/push.sh "refactor(server): centralize plane-exemption policy (C1 CONS-GATE-01)"`
- **Done note**: DONE. New `Hosting/ServerSurfacePolicy.cs`: named `SurfaceClass` route classes (Health, OperatorConsole, Operator, Provisioning, AuthAnonymous [exact /auth/login|refresh|keys], LicensingKeys, AuthSession, Licensing, TenantData, Unclassified) with `SkipsTenantBinding` / `IsEntitlementExempt` as the ONE source. `TenantContextMiddleware.IsSkipped`, `ServerEntitlementGate.IsExempt` and `EntitlementMiddleware` now delegate to it (the extra `/admin` special-case is gone). Unknown paths classify `Unclassified` = tenant-bound + entitlement-gated (fail closed). `AllowAllServerEntitlementGate` + its `TryAdd` registration were REMOVED (the real `Licensing.ServerEntitlementGate` is registered via `Replace` in `AddAnimoraLicensing`; no silent allow-all fallback). Tests: `ServerSurfacePolicyTests` (table incl. case-insensitivity, no-contradiction invariant, and an endpoint-data-source test asserting every authorized mapped endpoint has a defined class; `AuthzHost.Services` exposed). Existing IsSkipped/IsExempt tests unchanged and still valid. Not compiled locally (CI only).

### [x] C2 · BUG-OPLOCK-01 — Resolve operator account once per admin request (LOW)
- **Why**: For `/admin` + `/_blazor`, `Operator/OperatorContextMiddleware.cs:17` calls
  `AuthenticateAsync(OperatorCookie.Scheme)` → `OnValidatePrincipal` (`Admin/OperatorCookie.cs:28-37`,
  1 DB read), and the operator API path re-reads (`:43-45`) = 1–2 operator reads per
  admin request (Blazor Server fires many small requests).
- **Files**: `src/Animora.Server/Operator/OperatorContextMiddleware.cs:17,43-45`;
  `src/Animora.Server/Admin/OperatorCookie.cs:28-37`.
- **Do**: Resolve the operator account once per request, cache in `HttpContext.Items`,
  avoid double authentication.
- **Accept**: at most one operator-account read per admin request.
- **Push**: `scripts/git/push.sh "perf(server): resolve operator account once per admin request (C2 BUG-OPLOCK-01)"`
- **Done note**: DONE. New `Operator/OperatorRequestAccount.cs` memoizes the operator-account lookup (security stamp/status/must-change flag, `AsNoTracking` projection) in `HttpContext.Items` keyed by account id. Both `OperatorCookie.OnValidatePrincipal` (cookie plane: `/admin`, `/_blazor`, re-run by the `AdminOnly` policy authentication) and `OperatorContextMiddleware`'s operator-API branch now call it, so at most ONE operator read happens per request regardless of how many authentication passes run. Semantics unchanged (inactive/stamp-mismatch still rejects). Not compiled locally (CI only).

### [x] C3 · BUG-BURN-01 — Thread-safe dummy-hash init (LOW)
- **Why**: `Auth/LoginService.cs:36,435-440` `s_dummyHash ??= hasher.HashPassword(...)`
  is a non-synchronized lazy init of a static field; concurrent first-misses recompute.
  Benign (idempotent), but untidy.
- **Files**: `src/Animora.Server/Auth/LoginService.cs:36,435-440`.
- **Do**: Use `static readonly Lazy<string>` or `LazyInitializer.EnsureInitialized`.
  Preserve the timing-equalization intent (SEC-OBS-01 positive — still spend a hash on miss).
- **Accept**: single-init, timing behavior unchanged.
- **Push**: `scripts/git/push.sh "refactor(server): thread-safe dummy-hash init (C3 BUG-BURN-01)"`
- **Done note**: DONE. `LoginService` dummy-hash lazy init now uses `LazyInitializer.EnsureInitialized(ref value, ref ready, ref lock, factory)` (the factory runs under the lock exactly once). The per-miss `VerifyHashedPassword` against the dummy hash is untouched, so the SEC-OBS-01 timing-equalization is preserved. Not compiled locally (CI only).

### [ ] C4 · BUG-HEALTH-01 + IMP-10 — Cache readiness probes + add /health/startup (LOW)
- **Why**: `Program.cs:204-225` `/health/ready` awaits each probe in series and
  `snapshot.Set(...)` per probe per request; anonymous + un-rate-limited → frequent
  polling generates DB/key-ring/schema load.
- **Files**: `src/Animora.Server/Program.cs:204-225`; `ReadinessSnapshot` (already exists).
- **Do**: Cache readiness results for a few seconds (serve cached snapshot; refresh on
  a timer). Keep `/health/live` trivial. Add a distinct `/health/startup` for orchestrators.
- **Accept**: frequent `/health/ready` polling served from cache; `/health/startup` exists.
- **Push**: `scripts/git/push.sh "perf(server): cache readiness probes, add /health/startup (C4 BUG-HEALTH-01/IMP-10)"`
- **Done note**: _______

### [ ] C5 · SEC-HSTS-01 + SEC-OPENAPI-01 — Confirm TLS/forwarded-proto contract; document OpenAPI gating (MEDIUM/LOW)
- **Why**: `Program.cs:147-151` runs `UseHsts()`+`UseHttpsRedirection()` only outside
  Development; if TLS terminates at the LB and the app gets HTTP without forwarded-proto
  wired (depends on A2), redirect may loop/no-op. `Program.cs:127-132` `EnableOpenApi`
  can turn on the schema in any env (default false) — a disclosure foot-gun.
- **Files**: `src/Animora.Server/Program.cs:127-132,147-151`.
- **Do**: With forwarded-proto from A2 wired, confirm the HTTPS contract end-to-end
  (document whether app or LB enforces TLS; main API relies on redirect, admin already
  returns 400 on non-HTTPS via `AdminSecurityHeadersMiddleware.cs:19`). Keep OpenAPI
  default off; document the risk and (optional) gate behind operator auth / separate network.
- **Accept**: no redirect loop with forwarded-proto; OpenAPI gating documented, default off.
- **Push**: `scripts/git/push.sh "sec(server): confirm TLS/forwarded-proto contract, document OpenAPI gating (C5 SEC-HSTS-01/SEC-OPENAPI-01)"`
- **Done note**: _______

### [ ] C6 · SCALE-N1-01 — Batch-load role definitions in staff directory (MEDIUM)
- **Why**: `Auth/StaffProvisioningService.cs:249-257` loops every `StaffRole` enum value
  calling `FindByRoleAsync` each (one query per role), then per-member effective-permission
  assembly (`:260-278`). Runs on the write context.
- **Files**: `src/Animora.Server/Auth/StaffProvisioningService.cs:249-278`.
- **Do**: Batch-load all role definitions for the tenant in ONE query into a dictionary;
  keep member assembly in memory. (Pair with A4's read-pool routing where applicable.)
- **Accept**: role definitions loaded in one query; N+1 removed.
- **Push**: `scripts/git/push.sh "perf(server): batch-load role definitions in staff directory (C6 SCALE-N1-01)"`
- **Done note**: _______

### [ ] C7 · IMP-01 + IMP-09 — Scale pool sizes with profile + externalize magic numbers (improvement)
- **Why**: `appsettings.json` `OltpWriteMax=6 / OltpReadMax=3 / WorkerMax=2` is low for a
  command server. Magic numbers scattered: batch size 50, chunk 1 MiB, lease 60s,
  admission concurrency, outbox claim 2, recovery cap 50, snapshot lifetime 10s.
- **Files**: `src/Animora.Server/appsettings.json`; pool config in
  `PersistenceServiceCollectionExtensions.cs`; the options classes under `*/Options`.
- **Do**: Make `OltpWriteMax/OltpReadMax/WorkerMax` scale with `ProfileGiB`/CPU (or raise
  sensible defaults) and add Npgsql `Timeout`/pruning + explicit pool-exhaustion timeout
  → typed 503 (ties to A4 step 4). Externalize the magic numbers into validated options
  (consumed by A1/A5/B9/B10). Several of these may already be partly done by earlier tasks —
  finish and consolidate here.
- **Accept**: pool sizes profile-scaled; the listed constants are options-driven + validated.
- **Push**: `scripts/git/push.sh "chore(server): scale pools with profile, externalize tuning constants (C7 IMP-01/IMP-09)"`
- **Done note**: _______

### [ ] C8 · IMP-04 — Emit scaling-signal metrics (improvement)
- **Why**: The scaling findings predict exactly which signals turn red first; emit them.
- **Files**: observability/metrics registration (search for the existing meter/metrics
  setup in `src/Animora.Server/Observability`).
- **Do**: Emit metrics for: pool saturation, admission-gate wait time (both gates from A1),
  authZ query count/latency, outbox lag, intake-recovery backlog, reporting-budget wait (A5).
- **Accept**: the six signals are observable.
- **Push**: `scripts/git/push.sh "feat(server): emit scaling-signal metrics (C8 IMP-04)"`
- **Done note**: _______

### [ ] C9 · IMP-05 + IMP-06 — Backpressure (bounded queues → 503) + per-op authZ timeouts (improvement)
- **Why**: Fire-and-wait global semaphores block request threads under overload; a single
  slow authZ query can pin a request+connection for the full 30s `CommandTimeout`.
- **Files**: the admission/budget gates (A1/A5), authZ pipeline DB calls (A4 files).
- **Do**: Where a gate is on a request path, prefer bounded queues that return
  `503 + Retry-After` when full (clean shedding) over blocking. Add per-operation
  timeouts around each DB call in the authZ pipeline so one slow query can't pin a request.
- **Accept**: overload sheds with 503+Retry-After; authZ DB calls have bounded per-op timeouts.
- **Push**: `scripts/git/push.sh "perf(server): backpressure 503 + per-op authZ timeouts (C9 IMP-05/IMP-06)"`
- **Done note**: _______

### [ ] C10 · IMP-08 + CONS-INT-01 — File-complete re-validation + document intake lease ordering (improvement/INFO)
- **Why**: IMP-08: on `Complete`, re-validate content-type/extension; periodic integrity
  scrubbing (reconciliation worker exists). CONS-INT-01: `CommandIntakeService.cs:66` →
  `CommandIntakeStore.TryBeginAsync` runs the idempotency insert OUTSIDE the outer
  transaction BY DESIGN (lease must be visible to a concurrent takeover before the
  effect txn) — needs a one-line doc comment so nobody "fixes" it.
- **Files**: `src/Animora.Server/Files/FileEndpoints.cs` (Complete), `Files/FileReconciliationWorker.cs`;
  `src/Animora.Server/Commands/CommandIntakeService.cs:66`.
- **Do**: Add content-type/extension re-validation on `Complete`; ensure reconciliation
  does periodic integrity scrub. Add the one-line doc comment at the `TryBeginAsync` call
  site explaining the intentional outside-transaction placement.
- **Accept**: Complete re-validates; doc comment present at intake call site.
- **Push**: `scripts/git/push.sh "chore(server): file-complete re-validation + document intake lease ordering (C10 IMP-08/CONS-INT-01)"`
- **Done note**: _______

### [ ] C11 · IMP-11 — Consolidate the explicit regression tests (improvement)
- **Why**: The review asks for explicit tests; several were added inline in B-tasks.
  This task ensures the full set exists and passes (quick, targeted test project only — NOT a full suite run).
- **Tests required** (add any still missing from B-tasks):
  - refresh-reuse stamp persistence (B2 / SEC-STAMP-01)
  - concurrent first-login device race (B3 / SEC-ENUM-01)
  - savepoint release-on-rollback → outer commit survives (B5 / BUG-UOW-01)
  - pipeline order with limiter-first (B1 / BUG-PIPE-01, via `PlaneOrderTests`)
  - pool-exhaustion behavior → typed 503 (A4/C7)
- **Files**: `tests/*` (find the server test project).
- **Do**: Confirm/add the five tests. Run ONLY that focused test project if fast; otherwise reason + leave a note.
- **Accept**: the five regression tests exist (and pass if run was quick).
- **Push**: `scripts/git/push.sh "test(server): consolidate hardening regression tests (C11 IMP-11)"`
- **Done note**: _______

---

## FINAL TASK

### [ ] T-END · Delete this plan file (ONLY this file)
- **Precondition**: ALL tasks A1–A5, B1–B10, C1–C11 are `[x] DONE` and pushed.
- **Do**: `git rm Plan/SERVER_HARDENING_TASKLIST.md` (do NOT touch any other file,
  roadmap, or `docs/` — the review doc stays unless a later explicit instruction says otherwise).
  Keep `Plan/.gitkeep` so the directory survives.
- **Push**: `scripts/git/push.sh "chore(plan): remove completed server-hardening task list (T-END)"`
- **Done note**: _______

---

## PROGRESS LEDGER (update as you go)

| Task | Finding(s) | Sev | Status |
|---|---|---|---|
| A1 | SCALE-ADM-01 | BLOCKER | [x] |
| A2 | SEC-RL-01 / SEC-FWD-01 / CONS-RL-01 | BLOCKER/HIGH | [x] |
| A3 | SCALE-STATE-01 | CRITICAL | [x] |
| A4 | PERF-AUTHZ-01 / CONS-ENT-01 | HIGH/LOW | [x] |
| A5 | PERF-QB-01 | HIGH | [x] |
| B1 | BUG-PIPE-01 | HIGH | [x] |
| B2 | SEC-STAMP-01 | MEDIUM | [x] |
| B3 | SEC-ENUM-01 | MEDIUM | [x] |
| B4 | SEC-ADV-01 | MEDIUM | [x] |
| B5 | BUG-UOW-01 | MEDIUM | [x] |
| B6 | BUG-SIGN-01 | MEDIUM | [x] |
| B7 | BUG-CORR-01 | MEDIUM | [x] |
| B8 | BUG-BATCH-01 | MEDIUM | [x] |
| B9 | BUG-FILE-01 | MEDIUM | [x] |
| B10 | BUG-RECOVERY-01 / BUG-OUTBOX-01 | MEDIUM | [x] |
| C1 | CONS-GATE-01 | MEDIUM | [x] |
| C2 | BUG-OPLOCK-01 | LOW | [x] |
| C3 | BUG-BURN-01 | LOW | [x] |
| C4 | BUG-HEALTH-01 / IMP-10 | LOW | [ ] |
| C5 | SEC-HSTS-01 / SEC-OPENAPI-01 | MEDIUM/LOW | [ ] |
| C6 | SCALE-N1-01 | MEDIUM | [ ] |
| C7 | IMP-01 / IMP-09 | IMP | [ ] |
| C8 | IMP-04 | IMP | [ ] |
| C9 | IMP-05 / IMP-06 | IMP | [ ] |
| C10 | IMP-08 / CONS-INT-01 | IMP/INFO | [ ] |
| C11 | IMP-11 | IMP | [ ] |
| T-END | (cleanup) | — | [ ] |

> SEC-OBS-01 and CONS-INT-01's "by design" note are INFO positives — nothing to change
> except the one-line doc comment folded into C10. All 27 review findings are covered.
