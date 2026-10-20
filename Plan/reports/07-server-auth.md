# 07 — Server Host, Auth & Authorization Audit

Scope: `src/Animora.Server/{Hosting,Auth,Authorization,Resilience,Signing}` + `Server.Licensing/**` +
`Persistence.PostgreSql/{Identity,KeyRing}`. Audit targets: startup/readiness ordering, rate-limit
policies, bearer issuance/validation (opaque DP tokens, no JWT), RBAC stored-policy evaluation,
tenant-binding middleware, ECDSA snapshot/license signing & verification, key rotation. Plus the
PROGRESS flag that S04–S06 are "done" but higher phases are authored-but-unexecuted.

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| AUTH-01 | Medium | provisioning / isolation | Device auto-approval: unknown installations are `Approve()`d on first login (manual manager approval was removed) — any valid credential on any device gets an active session; the only cutoff is after-the-fact Revoke |
| AUTH-02 | Medium | latent (unverified code) | S07–S13 + licensing/admin S14–S20 handlers are **authored but build/test/smoke UNEXECUTED** per PROGRESS; `ServerHandlerModuleS07..S10` + licensing endpoints carry no evidence of exercised paths — first-class risk for auth/entitlement regressions |
| AUTH-03 | Low | perf | `AuthzDecisionCache` + membership/role reads per command: the per-operation budget (`AuthzOperationGuard`) protects latency, but RBAC evaluation hits the store on every command when the decision cache misses; hot command tenants pay repeated membership/permission lookups |
| AUTH-04 | Low | latent (time) | Access-token lifetime + bearer validation ride the framework `BearerTokenProtector` ticket; expiry is wall-clock (`IClock`), correct, but there is **no server-side revocation list** for access tokens — only `SecurityStamp` rotation invalidates them, and only refresh-time re-issuance reads the new stamp (an already-issued access token stays valid until it expires even after Revoke, bounded by `BearerTokenExpiration`) |
| AUTH-05 | Info | — | Auth/authz/signing core is strong: constant-time dummy-hash anti-enumeration, refresh-reuse → family revoke + security-stamp rotation committed atomically with its audit row, opaque DP bearer tokens with the role claim deliberately NOT `ClaimTypes.Role`, authorize+entitle run BEFORE any effect inside the UoW, ECDSA-P256 license/snapshot verification over exact signed bytes with duplicate-JSON-key rejection and fixed 64-byte IEEE-P1363 signatures, 5 named fixed-window rate-limit policies with health never limited |

---

## AUTH-05 (baseline verification — security core is sound)

- Severity: Info
- Location: `Auth/LoginService.cs`, `Auth/AccessTokenIssuer.cs`, `Authorization/{CommandAuthorizer,ServerPipelineGates,AuthzOperationGuard}.cs`,
  `Server.Licensing/Verification/LicenseVerifier.cs`, `Hosting/RateLimitPolicies.cs`, `Commands/CommandIntakeService.cs`.
- Evidence:
  - **Anti-enumeration:** `LoginService` burns a dummy password hash (`BurnPasswordHash`) on tenant-miss and
    unknown-user so wrong tenant/user/password are timing-indistinguishable; the dummy hash is computed once
    via `LazyInitializer` (BUG-BURN-01). Disabled accounts only reveal themselves after a correct password and
    never touch lockout counters.
  - **Refresh rotation:** `RefreshAsync` validates the account *before* rotating (a transient refusal never
    burns the token); a replayed/already-rotated token → `RotateSecurityStamp()` (invalidates every
    outstanding access token) + `RevokeFamilyAsync`, and the response is gated on the audit/stamp save
    committing.
  - **Tokens:** `BearerAccessTokenIssuer` uses the framework `BearerTokenProtector` (opaque DP ticket, no
    JWT); the role is stored under `AuthClaimTypes.Role`, deliberately **not** `ClaimTypes.Role`, so
    `IsInRole()` can never be satisfied from a token claim — authorization must go through the stored RBAC.
  - **Authorize before effects:** `CommandIntakeService.SubmitAsync` ordering is validate → caller/tenant
    match (`caller.TenantId != tenantContext.TenantId → SESSION_EXPIRED`) → dependency check → **authorize**
    (`authorizer.AuthorizeAsync`, line 121) → **entitlement** (line 124) → concurrency fence (line 145) →
    effect. `CommandAuthorizer` fails closed on an unmapped command (`PermissionUnknown`).
  - **Signing:** `LicenseVerifier.Verify` imports SPKI, asserts `KeySize==256` and `Signature.Length==64`,
    verifies over the **exact** signed bytes (never re-canonicalized), rejects duplicate JSON property names,
    and enforces key `NotBefore/NotAfter` against `IssuedUtc`. All crypto exceptions map to `LicenseInvalid`.
  - **Resilience:** `AuthzOperationGuard` caps every authz-pipeline DB call at a per-operation budget → typed
    retryable `503 SERVER_BUSY`, so one slow query cannot pin a pooled connection for the full 30 s Npgsql
    timeout.
  - **Rate limits:** exactly five named policies (`auth` 10/min, `commands` 120/min, `sync` 60/min,
    `files` 60/min, `operator` 30/min); health endpoints never limited.
- Why it matters: establishes the authentication/authorization/signing spine is correct; the findings below
  are policy choices (device auto-approve), unexecuted-code risk, and residual caching/revocation latency —
  not a broken security core.
- Confidence: High.

## AUTH-01 Device auto-approval removes the manual admission gate

- Severity: Medium
- Class: provisioning / isolation
- Location: `Auth/LoginService.LoginAsync`:
  ```csharp
  // All devices are approved automatically — the manual manager approval step was removed …
  Device candidate = Device.Register(tenant.Id, user.Id, "Unnamed device", installationHash, "unknown", clock);
  candidate.Approve();
  ```
- Why it is a problem: with manual approval removed, any party holding valid credentials can activate a
  **new** device installation immediately; the only control is *after-the-fact* `Revoke`/`Reinstate`. For a
  commercial multi-tenant product this weakens defence against credential-stuffing / shared-credential abuse
  — a stolen password yields an active session on an attacker device before any human notices. The idempotent
  `(user, installation)` convergence is correct; the admission policy is the finding.
- Scenario / impact: compromised staff credential → attacker signs in on their own machine, gets a full
  session + refresh family, until an admin revokes the device. No pre-admission throttle beyond the `auth`
  rate limit.
- Confidence: High (code-evident; severity depends on product risk appetite — it is a deliberate choice, so
  flagged as policy risk).

## AUTH-02 Higher server/licensing phases are authored but unexecuted

- Severity: Medium
- Class: latent (unverified code)
- Location: `Server/Handlers/ServerHandlerModuleS07..S10.cs`, `Server.Licensing/**`, per PROGRESS
  "S07–S13 + licensing/admin S14–S20 authored … build/test/smoke UNEXECUTED".
- Why it is a problem: the audit can read the code but the project's own status says these paths have never
  been built/tested/smoked. Auth/entitlement/licensing handlers that have never executed are a prime source
  of latent defects (null-ref on an untaken branch, mis-wired DI, an entitlement gate that compiles but
  denies/allows wrongly at runtime). Treating them as "done" is unsafe.
- Scenario / impact: the first real traffic through an S07+ handler or a licensing capability gate is also
  its first execution; failures surface in production rather than CI.
- Confidence: High (status is authoritative per PROGRESS; the risk is intrinsic to unexecuted code).

## AUTH-03 RBAC store lookups per command on decision-cache miss

- Severity: Low
- Class: perf
- Location: `Authorization/{AuthzDecisionCache,MembershipResolver,CommandAuthorizer}.cs`,
  `CommandIntakeService` (authorize + entitle inside the per-command UoW).
- Why it is a problem: every command authorizes via `IPermissionEvaluator` against stored RBAC; on a decision
  cache miss this is DB work (membership + role definition reads) on the command hot path, bounded by the
  authz budget but still per-command latency. For a high-command-rate tenant this is repeated lookups that a
  longer-lived per-(tenant,user,permission) cache would amortize.
- Scenario / impact: command throughput ceiling lower than necessary under bursty intake; not a correctness
  issue.
- Confidence: Medium (needs runtime check of cache hit rate / TTL).

## AUTH-04 No access-token revocation list; only stamp rotation + expiry bound compromise

- Severity: Low
- Class: latent (security / time)
- Location: `Auth/AccessTokenIssuer.cs` (DP ticket with `ExpiresUtc`), `LoginService.RefreshAsync`
  (`RotateSecurityStamp`).
- Why it is a problem: access tokens are self-contained DP tickets; there is no server-side deny-list checked
  per request. Revoking a device or rotating the security stamp invalidates **future** re-issuance (refresh
  reads the new stamp), but an **already-issued** access token remains cryptographically valid until
  `BearerTokenExpiration` elapses, because the per-request validation does not re-check the stamp against the
  live user record. The compromise window equals the access-token lifetime.
- Scenario / impact: after a device Revoke, the current access token keeps working until it expires (seconds
  to minutes depending on `BearerTokenExpiration`); acceptable for most threat models but worth documenting
  as the bound. Shorten the access-token TTL or add a stamp re-check to tighten it.
- Confidence: Medium (needs confirmation that request-time validation does not already re-read the stamp;
  the ticket format suggests it does not).
