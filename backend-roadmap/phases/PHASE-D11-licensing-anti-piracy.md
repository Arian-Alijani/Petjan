# Phase D11 — Licensing & anti-piracy

> **PostgreSQL baseline (2026-10-08):** Read `ARCHITECTURE.md` §PG and §16-CT–§18-CT / `STACK.md` §6 before implementation. They govern PostgreSQL 18, Npgsql 10.0.3, UUID Version, tenant journal, snapshot/batch atomicity, pool budgets and deployment-only migrations. This phase is a future instruction, not an implementation claim. Mandatory PostgreSQL CI cannot pass by skipping DB tests.

**Band:** M · **Track:** D · **Depends on:** D10 · **Satisfies:** R-B06, R-B29, R-B30, R-B36
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §4, §7, §9, §12
- `backend-roadmap/PROGRESS.md` (D10 entry — `Subscription`, `Plan`, `PlanFeatures`,
  `SubscriptionEventTypes`, `subscription.plan.changed`, `ISubscriptionOperations`,
  `RequireOperator`; D09 entry — `IOutboxConsumer`, `ConsumedMessages`, `OperatorAlert`; D04 entry —
  `EntitlementMiddleware` (no-op placeholder); D03 entry — `IServerAuditWriter`,
  `InstallationFingerprint` on `Device`; D01 entry — `SecurityOptions`, Data Protection key ring;
  A11 entry — `InstallationIdentity`, `MachineFingerprintHash`, `MonotonicClock.HighWaterUtc`,
  `ClockHighWater`, `ISecretStore`; B01 entry — `IEntitlementGate`/`AllowAllEntitlementGate`)
- `ARCHITECTURE.md` §13.1, §13.2, §10.6, §19 gate U · `STACK.md` §8, §8.1
- `A-RULE 011`, `A-RULE 023`, `A-RULE 027`, `A-RULE 030`, `A-RULE 016`
- `src/Animora.Infrastructure.Local/Security/InstallationIdentity.cs`,
  `src/Animora.Application/Security/IEntitlementGate.cs`

## Goal
A real licensing mechanism: a signed licence artifact issued from subscription state, verified
**entirely offline** against a rotation-aware key set, bound to one sealed installation identity,
checked against a monotonic high-water clock, with a fail-closed policy matrix. The verification half
is a standalone library the desktop consumes in E07 without pulling in any server type.

## Design decisions (pre-made — implement, do not re-derive)
- **One new project, split by direction, not by layer:** `src/Animora.Server.Licensing/`
  (class library, `net10.0`, added to `Animora.sln`): `Verification/` references only
  `Animora.Domain` + `Animora.Contracts.Wire` (the half E07 consumes from the client);
  `Issuance/` references `Animora.Application` + `Animora.Domain` and is consumed only by
  `Animora.Server`. `LayeringRulesTests` gains a fact asserting `Verification/` types transitively
  reference neither `Animora.Persistence.PostgreSql` nor ASP.NET Core — that is what makes client
  reuse safe.
- **Licence artifact = a detached-signature envelope, not a JWT.** JWT would drag in a token stack
  and a clock model this product does not want. Format
  (`src/Animora.Server.Licensing/Verification/LicenseArtifact.cs`):
  ```
  LicenseArtifact = Base64Url(payloadJson) + "." + Base64Url(signature) + "." + KeyId
  ```
  `payloadJson` is canonical JSON (the same `CommandFingerprint` canonicalizer settings from B02 —
  sorted properties, no indentation, invariant culture) of
  `sealed record LicensePayload(int Version, string LicenseId, string TenantId, string Product,
  string Audience, string PlanCode, IReadOnlyList<string> Features, int Seats,
  DateTimeOffset NotBefore, DateTimeOffset NotAfter, DateTimeOffset IssuedAtUtc,
  string InstallationBindingHash, string KeyId, string IssuerName)`.
  `Version = LicenseContract.Version` (`= 1`, a constant in `Verification/LicenseContract.cs`,
  deliberately **separate** from `WireContract.Version`: the licence outlives wire shapes).
  `Product = "animora.clinic"`, `Audience = "desktop"`.
- **Signing algorithm: Ed25519 is not in .NET 10's BCL surface for this use; use ECDSA P-256 with
  SHA-256** (`ECDsa.SignData(..., HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)`)
  — first-party crypto only, no new package (CONVENTIONS §7: no custom crypto). Keys are stored as
  PKCS#8 (private) / SubjectPublicKeyInfo (public) DER.
- **Rotation-aware key set, both sides.** Server: `LicenseSigningKey` table (migration
  `109_Licensing`) — `KeyId` (ULID), `PublicKeyDer` (`byte[]`), `PrivateKeyProtected` (`byte[]`,
  wrapped by the D01 Data Protection ring, purpose `"Animora.Licensing.SigningKey.v1"`),
  `Algorithm` (`"ECDSA_P256_SHA256"`), `CreatedAtUtc`, `NotAfterUtc`,
  `KeyStatus { Active, Retiring, Retired }`, `IsPrimary` (exactly one, filtered unique index).
  Losing the ring means no private key can be unwrapped → the server refuses to issue and raises an
  `OperatorAlert` (A-RULE 027, fail closed). Verifier: `Verification/LicenseKeyRing.cs` — an
  immutable set of
  `sealed record LicenseVerificationKey(string KeyId, byte[] PublicKeyDer, DateTimeOffset NotAfterUtc)`
  loaded from a **signed key-set bundle** (`LicenseKeySetBundle` = the key list plus a signature by
  the previous primary key, so a new key chains from the shipped root). The installer ships
  `keyset.animora` in the app content directory; a newer bundle may arrive with a licence. `KeyId`
  selects the verifying key (§13.2 step 2); an unknown one fails closed with `LICENSE_KEY_UNKNOWN`.
- **Installation binding (the actual anti-cloning control, A-RULE 030):**
  `InstallationBindingHash = SHA-256( InstallationId + '\u001f' + MachineFingerprintHash )`, both
  inputs from A11's `LocalInstallation` row and `InstallationIdentity`. The client sends them at
  licence-request time (E07); the server stores the pair on the `LicenseIssuance` row and stamps the
  hash into the payload. Verification recomputes and compares — a byte-for-byte copied licence fails
  on another machine before expiry is even considered. `Verification/InstallationBinding.cs` holds
  the single hash function used by **both** sides, so they can never drift.
- **Verifier, exact order, fail closed at every step**
  (`Verification/LicenseVerifier.cs`, `sealed class`, no I/O, no clock of its own):
  `Result<LicenseVerdict> Verify(LicenseArtifact artifact, LicenseVerificationContext ctx)` where
  `sealed record LicenseVerificationContext(string ExpectedProduct, string ExpectedAudience,
  string ExpectedTenantId, string InstallationBindingHash, DateTimeOffset WallClockUtc,
  DateTimeOffset MonotonicHighWaterUtc, LicenseKeyRing KeyRing)`:
  ```
  1 parse/shape        -> LICENSE_MALFORMED
  2 KeyId -> key       -> LICENSE_KEY_UNKNOWN            (retired key = fail, retiring = warn)
  3 signature          -> LICENSE_SIGNATURE_INVALID
  4 contract version   -> LICENSE_VERSION_UNSUPPORTED    (payload.Version > LicenseContract.Version)
  5 product/audience   -> LICENSE_AUDIENCE_MISMATCH
  6 tenant             -> LICENSE_TENANT_MISMATCH
  7 installation bind  -> LICENSE_INSTALLATION_MISMATCH  (the anti-cloning step)
  8 effective-now      -> LICENSE_NOT_YET_VALID | LICENSE_EXPIRED
       effectiveNow = max(WallClockUtc, MonotonicHighWaterUtc)   <-- clock rollback cannot help
  9 verdict            -> Valid | Expiring(within ExpiringWithinDays) | Invalid(code)
  ```
  `sealed record LicenseVerdict(LicenseStatus Status, LicensePayload? Payload, ErrorCode? Error,
  DateTimeOffset? ExpiresAtUtc, IReadOnlySet<string> Features, int Seats)`;
  `enum LicenseStatus { Valid, Expiring, Expired, Invalid }`.
  Step 8 using `max(wall, highWater)` is the whole clock-rollback defense (A-RULE 011) and is
  asserted by a test that winds the wall clock back a year.
- **Fail-closed policy matrix — declared data, not scattered `if`s**
  (`Verification/EntitlementPolicy.cs`):
  | Verdict | Read clinical data | Create/modify clinical | Money commands (invoice/payment/period) | Sync | Admin/settings |
  |---|---|---|---|---|---|
  | `Valid` | allow | allow | allow | allow | allow |
  | `Expiring` | allow | allow | allow | allow | allow (+ warning surfaced) |
  | `Expired` | allow | **deny** | **deny** | allow (pull only) | allow |
  | `Invalid` | allow | **deny** | **deny** | **deny** | allow (so the licence can be replaced) |
  Encoded as `static EntitlementDecision Evaluate(LicenseStatus status, string capabilityGroup)` over
  `CapabilityGroups { ClinicalRead, ClinicalWrite, Money, Sync, Admin }`. **Never** "deny everything"
  (a clinic must always read its own records) and **never** "allow money without a licence".
  A missing/unreadable licence is `Invalid`, not `Valid` — the default is deny.
  `Features` gate feature flags; `Seats` is informational only (enforcement stays server-side in
  D10's `SeatGuard`) so the desktop can explain a limit it never enforces.
- **Issuance (server half), driven by subscription state, never by a request body:**
  `Issuance/ILicenseIssuer.cs` →
  `Task<Result<IssuedLicense>> IssueAsync(LicenseIssueRequest request, CancellationToken ct)`,
  `Task<Result<IssuedLicense>> ReissueAsync(string licenseId, string reason, CancellationToken ct)`,
  `Task<Result<LicenseKeySetBundle>> GetKeySetAsync(CancellationToken ct)`,
  `Task<Result> RotateSigningKeyAsync(TimeSpan retireAfter, CancellationToken ct)`.
  `LicenseIssuer` reads `Subscription` + `Plan` (D10) for `PlanCode`, `Features`, `Seats`, and
  computes `NotAfter = min(period end, now + LicensePolicy.MaxValidityDays)` with
  `LicensePolicy.MaxValidityDays = 45`, `ExpiringWithinDays = 10`. **Short validity is the
  revocation mechanism** — there is no online revocation channel (`ARCHITECTURE.md` §13.2
  invariant); the policy comment states this explicitly.
- **`LicenseIssuance` table** (migration `109_Licensing`): `Id`, `LicenseId` (ULID, unique),
  `TenantId`, `InstallationId`, `MachineFingerprintHash`, `InstallationBindingHash`, `KeyId`,
  `PlanCode`, `SeatsAtIssue`, `FeaturesJson`, `NotBefore`, `NotAfter`, `IssuedAtUtc`,
  `SupersededByLicenseId?`, `ReissueReason?`, `ArtifactHash`. Append-only via
  `ServerAppendOnlyGuard`; a reissue writes a **new** row and sets the old row's
  `SupersededByLicenseId` through the one column whitelisted by name in the guard. Indexes:
  `UX_LicenseIssuances_LicenseId`, `IX_LicenseIssuances_TenantId_NotAfter`,
  `IX_LicenseIssuances_InstallationBindingHash`. Both tables are non-tenant-scoped and
  non-replicated (`ServerModel` updated).
- **Endpoints** (`src/Animora.Server/Endpoints/LicenseEndpoints.cs`):
  | Route | Auth | Purpose |
  |---|---|---|
  | `POST /api/license/request` | authenticated device (tenant token) | body `LicenseRequestV1(InstallationId, MachineFingerprintHash)` → `LicenseResponseV1(Artifact, KeySetBundle, ExpiresAtUtc)`; idempotent per `(TenantId, InstallationBindingHash)` while the current licence is still valid (returns the same artifact) |
  | `GET /api/license/keyset` | anonymous | the signed key-set bundle (public keys only) |
  | `POST /api/operator/licenses/{tenantId}/reissue` | `RequireOperator` | operator-forced reissue with a reason |
  | `POST /api/operator/licenses/rotate-key` | `RequireOperator` | creates a new primary key, marks the old `Retiring` for `LicensePolicy.KeyRetireDays` (30), returns the new bundle |
  Wire records go in `src/Animora.Contracts.Wire/Licensing/`. `WireContract.Version` retains D05 **v2** (§PG.7; compatibility DTO suffix V1 does not reset the envelope version).
- **`subscription.plan.changed` triggers a reissue, through the outbox** — not a direct call:
  `src/Animora.Server/Consumers/LicenseReissueConsumer.cs` implements D09's `IOutboxConsumer`
  (`MessageType = "subscription.plan.changed"`), reissues the tenant's current licence with reason
  `plan-changed`, and relies on D09's `ConsumedMessages` guard for idempotency. This is the only
  coupling between Subscription and Licensing, and it is **asynchronous and one-way**
  (`ARCHITECTURE.md` §13.1).
- **`EntitlementMiddleware` becomes real (server side).** D04's no-op is replaced by an
  implementation that, for `/api/commands*` and `/sync/*`, resolves the tenant's current
  `LicenseIssuance` (cached per tenant for 60 s with a tenant-scoped key per A-RULE 033),
  evaluates `EntitlementPolicy` for the request's capability group, and returns `403`
  `ENTITLEMENT_REQUIRED` (existing code from B01) with the verdict code in `WireApiError.Detail`.
  Authentication and authorization still run first and separately (A-RULE 023).
- **Every licence action is audited** (R-B29): issue, reissue, rotate, and every **failed**
  verification reported by a client (E07 posts failures to `POST /api/license/request` implicitly —
  no new endpoint) writes an `IServerAuditWriter` entry with the licence id, installation binding
  hash and error code. Three failed verifications for one installation within 24 h raise an
  `OperatorAlert` of type `license.verification.failing` (tamper signal, never an auto-block).
- **Anti-tamper of the local entitlement cache is specified here, implemented client-side in E07:**
  the cached verdict file is `entitlement.cache` = canonical JSON + HMAC-SHA256 using a key from
  A11's `ISecretStore`; a broken HMAC = `Invalid` (deny), never "assume valid". The HMAC helper is
  `Verification/EntitlementCacheSeal.cs` so both sides share one implementation.
- **Zero-Internet:** nothing in `Verification/` performs I/O or network access — it is pure
  functions over inputs (A-RULE 016); a test asserts the assembly references no networking type.
- **New error codes:** `LICENSE_MALFORMED`, `LICENSE_KEY_UNKNOWN`, `LICENSE_SIGNATURE_INVALID`,
  `LICENSE_VERSION_UNSUPPORTED`, `LICENSE_AUDIENCE_MISMATCH`, `LICENSE_TENANT_MISMATCH`,
  `LICENSE_INSTALLATION_MISMATCH`, `LICENSE_NOT_YET_VALID`, `LICENSE_EXPIRED`,
  `LICENSE_ISSUANCE_UNAVAILABLE`, `LICENSE_KEY_ROTATION_FAILED`
  (`ENTITLEMENT_REQUIRED` exists from B01 — reuse).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Server.Licensing/Animora.Server.Licensing.csproj` | create | new project, added to `Animora.sln` |
| `src/Animora.Server.Licensing/Verification/LicenseContract.cs` | create | `Version = 1` + constants |
| `src/Animora.Server.Licensing/Verification/LicensePayload.cs` | create | the signed payload record |
| `src/Animora.Server.Licensing/Verification/LicenseArtifact.cs` | create | parse/format the 3-part envelope |
| `src/Animora.Server.Licensing/Verification/LicenseKeyRing.cs` | create | rotation-aware key set + bundle |
| `src/Animora.Server.Licensing/Verification/InstallationBinding.cs` | create | the one binding-hash function |
| `src/Animora.Server.Licensing/Verification/LicenseVerifier.cs` | create | the 9-step offline verifier |
| `src/Animora.Server.Licensing/Verification/EntitlementPolicy.cs` | create | fail-closed matrix |
| `src/Animora.Server.Licensing/Verification/EntitlementCacheSeal.cs` | create | HMAC seal (E07 uses it) |
| `src/Animora.Server.Licensing/Issuance/ILicenseIssuer.cs` | create | issuance port + requests |
| `src/Animora.Server.Licensing/Issuance/LicenseIssuer.cs` | create | issue/reissue/rotate over subscription state |
| `src/Animora.Server.Licensing/Issuance/LicensePolicy.cs` | create | validity/expiring/retire numbers |
| `src/Animora.Contracts.Wire/Licensing/*.cs` | create | `LicenseRequestV1`, `LicenseResponseV1`, `LicenseKeySetBundleV1` |
| `src/Animora.Persistence.PostgreSql/Licensing/LicenseSigningKey.cs` | create | key table entity |
| `src/Animora.Persistence.PostgreSql/Licensing/LicenseIssuance.cs` | create | issuance ledger entity |
| `src/Animora.Persistence.PostgreSql/Migrations/109_Licensing*` | create | 2 tables + indexes |
| `src/Animora.Persistence.PostgreSql/Context/ServerModel.cs` | modify | non-tenant-scoped + non-replicated entries |
| `src/Animora.Persistence.PostgreSql/Context/ServerAppendOnlyGuard.cs` | modify | protect `LicenseIssuances` (+ the one whitelisted column) |
| `src/Animora.Server/Endpoints/LicenseEndpoints.cs` | create | 4 routes |
| `src/Animora.Server/Consumers/LicenseReissueConsumer.cs` | create | `subscription.plan.changed` → reissue |
| `src/Animora.Server/Security/EntitlementMiddleware.cs` | modify | real evaluation, replaces the no-op |
| `src/Animora.Server/Program.cs` | modify | register issuer, consumer, endpoints, middleware order |
| `tests/Animora.Server.Tests/LicenseVerifierTests.cs` | create | the 9 failure paths + valid path |
| `tests/Animora.Server.Tests/LicenseAntiCloningTests.cs` | create | copied licence + clock rollback (gate U) |
| `tests/Animora.Server.Tests/LicenseIssuanceTests.cs` | create | issue/reissue/rotate, append-only ledger |
| `tests/Animora.Server.Tests/EntitlementPolicyTests.cs` | create | the matrix, incl. money denied when expired |

## Implementation steps
1. Create `Animora.Server.Licensing` with the two folders and the layering fact in
   `LayeringRulesTests`.
2. Write `Verification/` in this order: `LicenseContract`, `LicensePayload`, `LicenseArtifact`,
   `InstallationBinding`, `LicenseKeyRing`, `LicenseVerifier`, `EntitlementPolicy`,
   `EntitlementCacheSeal`. No I/O anywhere in this folder.
3. Add the wire records; add the two tables, `ServerModel`/guard updates and migration
   `109_Licensing`.
4. Write `LicensePolicy` and `LicenseIssuer` (wrap the private key with Data Protection; refuse to
   issue when unwrapping fails and raise the alert).
5. Implement key rotation: create a new `Active`+`IsPrimary` key, demote the old to `Retiring` with
   `NotAfterUtc = now + KeyRetireDays`, and build a new bundle signed by the **previous** primary.
6. Map `LicenseEndpoints`; make `/api/license/request` idempotent per binding hash.
7. Write `LicenseReissueConsumer` against D09's `IOutboxConsumer`.
8. Replace `EntitlementMiddleware`'s body; keep it after authentication/authorization in the
   pipeline; add the 60 s tenant-scoped cache.
9. Add the audit writes and the failing-verification alert rule.
10. Write the four test classes; `LicenseAntiCloningTests` **is** `ARCHITECTURE.md` §19 gate U.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Project `Animora.Server.Licensing` (`Verification/` + `Issuance/`).
- Verification: `LicenseContract`, `LicensePayload`, `LicenseArtifact`, `LicenseKeyRing`,
  `LicenseVerificationKey`, `LicenseKeySetBundle`, `InstallationBinding`, `LicenseVerifier`,
  `LicenseVerificationContext`, `LicenseVerdict`, `LicenseStatus`, `EntitlementPolicy`,
  `EntitlementDecision`, `CapabilityGroups`, `EntitlementCacheSeal`.
- Issuance: `ILicenseIssuer`, `LicenseIssuer`, `LicenseIssueRequest`, `IssuedLicense`,
  `LicensePolicy` (`MaxValidityDays 45`, `ExpiringWithinDays 10`, `KeyRetireDays 30`).
- Wire: `LicenseRequestV1`, `LicenseResponseV1`, `LicenseKeySetBundleV1`.
  `WireContract.Version` retains D05 **v2** (§PG.7; compatibility DTO suffix V1 does not reset the envelope version); `LicenseContract.Version = 1` is introduced here.
- Persistence.PostgreSql: `LicenseSigningKey`, `KeyStatus`, `LicenseIssuance`, migration
  `109_Licensing`, `ServerModel` + `ServerAppendOnlyGuard` extended.
- Server: `LicenseEndpoints` (4 routes), `LicenseReissueConsumer`, the real
  `EntitlementMiddleware`.
- `ErrorCodes` += 11. No `Animora.Contracts` change, no `CommandTypes` entry, no DI change in
  `App.axaml.cs`.

## Rules that apply here
- **A-RULE 030 / R-B06** — the signature is authenticity only; `InstallationBindingHash`
  verification (step 7) is the mandatory anti-cloning control and runs before expiry.
- **A-RULE 011** — `effectiveNow = max(wallClock, monotonicHighWater)`; a wound-back clock can never
  extend validity.
- **A-RULE 027 / §10.6** — the private key is wrapped by the persisted Data Protection ring; without
  the ring the server refuses to issue (never falls back to an unprotected key).
- **R-B30 / §13.1** — Licensing reads Subscription **only** at issuance, asynchronously via the
  outbox consumer; the runtime entitlement path touches no subscription table.
- **A-RULE 023** — authentication → authorization → entitlement, three separate middleware stages,
  in that order.
- **R-B29** — issue/reissue/rotate and every reported verification failure are audited; repeated
  failures raise an operator alert, never an automatic block.
- **A-RULE 016 / 032** — `Verification/` performs no I/O and no network call; revocation is
  approximated by short validity, stated in the code comment, never claimed as instant.
- **CONVENTIONS §7** — first-party crypto primitives only (`ECDsa`, `HMACSHA256`); no custom
  algorithm, no third-party crypto package.

## Out of scope
- Client-side verification, local entitlement caching, expiry/grace UX and feature gating in the
  desktop → E07 (which references only `Verification/`).
- The operator UI for licence issue/reissue/rotate → E01.
- Gate U's formal recording alongside the other gates → E08 (the test lands here; the recorded
  verdict table is E08's).
- Any online revocation channel — architecturally excluded (`ARCHITECTURE.md` §13.2).
- Billing/payment for a plan → out of product scope (`STACK.md` §17).

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `LicenseVerifierTests` green: each of the nine steps fails with its own error code, in order (a
  malformed artifact never reaches the signature check; a wrong tenant never reaches expiry); a
  well-formed licence verifies to `Valid`, and to `Expiring` inside `ExpiringWithinDays`.
- `LicenseAntiCloningTests` green — **gate U**: (a) a licence issued for installation A fails with
  `LICENSE_INSTALLATION_MISMATCH` when verified with installation B's binding hash, even though the
  signature is valid; (b) setting `WallClockUtc` one year back while `MonotonicHighWaterUtc` stays
  current still yields `LICENSE_EXPIRED` for an expired licence; (c) a licence signed by a `Retired`
  key fails `LICENSE_KEY_UNKNOWN`, one signed by a `Retiring` key still verifies.
- `LicenseIssuanceTests` green (or skipped with reason): issuance derives `Features`/`Seats` from the
  subscription's plan; a repeat request for the same binding hash returns the identical artifact; a
  reissue writes a new row and supersedes the old one; any `Update`/`Delete` on
  `LicenseIssuances` other than the whitelisted column throws; rotation keeps exactly one
  `IsPrimary` row and the new bundle verifies against the previous key.
- `EntitlementPolicyTests` green: the matrix matches the table cell-for-cell; `Expired` denies
  clinical writes **and** money but still allows clinical reads; `Invalid` denies sync; a missing
  licence evaluates as `Invalid`, never `Valid`.
- `EntitlementMiddleware` returns `403` `ENTITLEMENT_REQUIRED` for a money command under an expired
  licence, and lets a read through; authentication failures still return `401` (planes not merged).
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D11 done; advance "Next phase" to `D12`; append a "Repo state" entry listing: the
`Animora.Server.Licensing` project and the **`Verification/` has no server dependency** rule
(**E07 references only that folder**), `LicenseArtifact` format + `LicenseContract.Version = 1`,
the ECDSA P-256/SHA-256 choice, `LicenseSigningKey` + `LicenseIssuance` (migration `109_Licensing`,
append-only + the one whitelisted column), `InstallationBinding`'s shared hash function,
`LicenseVerifier`'s 9-step order and the `max(wall, highWater)` rule, `EntitlementPolicy`'s
fail-closed matrix (**E07 enforces it client-side**), `EntitlementCacheSeal` (**E07 uses it**),
`LicensePolicy` numbers and the "short validity = revocation" decision, the 4 routes,
`LicenseReissueConsumer` as the only Subscription→Licensing coupling, the real
`EntitlementMiddleware`, the gate U test name, the 11 new error codes, deviations.
