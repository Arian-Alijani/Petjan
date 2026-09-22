# Phase D11 — Licensing & anti-piracy

**Band:** M · **Track:** D · **Depends on:** D10 · **Satisfies:** R-B06, R-B29, R-B30
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §4, §5, §7, §9, §12
- `backend-roadmap/PROGRESS.md` (D10 entry — `TenantSubscription`, `SubscriptionViewV1`, `Plan.FeatureFlagsJson`,
  `SubscriptionStatus`; D04 entry — `EntitlementMiddleware` placeholder, `AnimoraPolicies`,
  `HttpTenantContext`; D03 entry — `Device` registry, `InstallationFingerprint`, `IServerAuditWriter`;
  D01 entry — Data Protection key ring, `SecretGuard`; A11 entry — `InstallationIdentity`,
  `MachineFingerprintHash`, `ClockHighWater`, `IMonotonicClock`; A02 entry — `IMonotonicClock`)
- `ARCHITECTURE.md` §13.1, §13.2, §10.1, §10.6, §17.3 · `STACK.md` §8, §11
- `A-RULE 006`, `A-RULE 011`, `A-RULE 016`, `A-RULE 023`, `A-RULE 027`, `A-RULE 030`
- `src/Animora.Server/Security/EntitlementMiddleware.cs`, `src/Animora.Persistence.SqlServer/Subscription/**`

## Goal
A real **Licensing** context: a new `Animora.Server.Licensing` project that issues and verifies
**signed, installation-bound, rotation-aware** license artifacts, a server-side entitlement plane
that replaces D04's no-op middleware, and the anti-piracy controls — tamper detection, clock-rollback
resistance and fail-closed behavior — that make a copied license useless on a second installation.

## Design decisions (pre-made — implement, do not re-derive)
- **New project `src/Animora.Server.Licensing/`** (class library, `net10.0`, added to `Animora.sln`
  in the `server` folder). References `Animora.Domain`, `Animora.Application`,
  `Animora.Contracts.Wire` — and **nothing else** (no EF Core, no ASP.NET Core, no persistence).
  It is pure crypto + policy, so the **same verifier compiles into the desktop in E07** without
  dragging the server in. `LayeringRulesTests` gains a fact asserting exactly that reference set.
  Storage of issued licenses lives in `Animora.Persistence.SqlServer/Licensing/`, not here.
- **Artifact format — plain, versioned, signed JSON** (no JWT library, no custom crypto;
  `CONVENTIONS.md` §7): a `LicenseArtifactV1` is
  `{ "payload": <base64url(canonical-json)>, "keyId": "<ulid>", "alg": "ES256", "signature": "<base64url>" }`.
  The payload (`LicensePayloadV1`, in `Animora.Contracts.Wire/Licensing/`) carries, and only carries:
  | Field | Purpose |
  |---|---|
  | `LicenseId` (ULID) | anti-replay identity; a reissue always gets a new id |
  | `ContractVersion` | `WireContract.Version` at issue time |
  | `Audience` (`"animora.desktop"`) + `Product` + `ProductMajorVersion` | wrong-audience artifacts are rejected outright |
  | `TenantId`, `TenantName` | which clinic |
  | `PlanCode`, `Features` (`IReadOnlyList<string>`) | the entitlement set — copied from `Plan.FeatureFlagsJson` at issue time, **never** read live |
  | `MaxSeats`, `MaxDevices` | enforced limits |
  | `InstallationBindingHash` | SHA-256 of the **sealed installation identity** (see below) — the anti-cloning anchor |
  | `IssuedAtUtc`, `NotBeforeUtc`, `ExpiresAtUtc` | validity window |
  | `GraceDays` | how long a just-expired license still grants read-only continuity |
  | `IssuerInstance` | D01's `ServerOptions.InstanceName`, for support triage |
  Canonical JSON = the **same** canonicalizer B02 defined for `CommandFingerprint` (sorted property
  names, no indentation, invariant culture) — reused, never re-implemented, so signing and
  verification cannot disagree about bytes.
- **Signature algorithm — binding decision: `ECDsa` over NIST P-256 with SHA-256 (`"ES256"`)**, using
  in-box `System.Security.Cryptography` only (`ECDsa.SignData` / `VerifyData`,
  `DSASignatureFormat.Rfc3279DerSequence`). It is first-party, offline, FIPS-available on both
  target OSes, and needs **no package outside `STACK.md` §11** (no BouncyCastle, no NSec, no JWT
  library). Do not substitute Ed25519 or a JWT: the artifact carries `alg` precisely so a future
  algorithm change is a *data* change, not a format change.
- **Key management, rotation-aware** (`ARCHITECTURE.md` §13.2 step 2, §10.6, A-RULE 027):
  - Private signing keys live **only** on the server, in
    `src/Animora.Persistence.SqlServer/Licensing/LicenseSigningKey.cs` (table `LicenseSigningKeys`,
    migration `109_Licensing`): `KeyId` (ULID PK), `PublicKeyPem`, `EncryptedPrivateKey` (protected
    with D01's **Data Protection key ring**, purpose string `"Animora.Licensing.SigningKey.v1"` —
    so a restore that omits the key ring cannot sign, §17.3), `Algorithm`, `CreatedAtUtc`,
    `ActivatedAtUtc`, `RetiredAtUtc`, `Status` (`Pending | Active | Retired | Revoked`).
    Exactly **one** `Active` key at a time (a filtered unique index enforces it).
  - `LicenseKeyRing` (`Animora.Server.Licensing/LicenseKeyRing.cs`) is the **verifier-side** type: an
    immutable `IReadOnlyDictionary<string /*keyId*/, VerifyingKey>` plus `Rotate` semantics. A
    verifier accepts a signature from any **non-revoked** key in its ring, which is exactly what
    makes rotation survivable across a staggered fleet; `Revoked` keys are rejected even inside the
    validity window.
  - Rotation is an **operator action** (`POST /api/operator/licensing/keys/rotate`): generate a new
    key pair, mark it `Active`, move the previous to `Retired` (still verifying, no longer signing),
    and enqueue `license.reissue.required` onto D09's outbox for every active tenant. Retired keys
    are kept for `LicensingOptions.RetiredKeyRetentionDays` (default 400 — longer than the longest
    license) and then `Revoked`.
- **Installation binding — the control that actually prevents cloning** (A-RULE 030, §13.2 step 3):
  - The desktop's **sealed installation identity** is A11's `InstallationIdentity`
    (`MachineFingerprintHash`, sealed at install time, never leaves the device as raw material).
    D03's device registration already stores `InstallationFingerprint` per `Device`.
  - `InstallationBindingHash = SHA-256( tenantId ‖ 0x1f ‖ installationFingerprint ‖ 0x1f ‖ licensePurposeSalt )`
    where `licensePurposeSalt` is a per-deployment random value stored beside the signing key
    (`ServerBootstrapState`). Verification recomputes it from the **local** identity and compares in
    constant time (`CryptographicOperations.FixedTimeEquals`). A byte-for-byte copied license on a
    second machine fails here, which is stated in a code comment citing A-RULE 030 so nobody
    "optimizes" the check away.
  - **A signature proves authenticity, not non-cloning** — the verifier returns a distinct
    `LICENSE_INSTALLATION_MISMATCH` code, never a generic "invalid signature", so support can tell
    the two apart.
- **Clock-rollback resistance** (A-RULE 011, §13.2 step 5): verification uses
  `max(wallClockUtc, IMonotonicClock.HighWaterUtc)` — A11's persisted high-water value, which can
  only advance. Every successful verification **advances** the high-water mark to `now`. A wound-back
  system clock therefore cannot resurrect an expired license, and the verifier returns
  `LICENSE_CLOCK_ROLLBACK` when `wallClockUtc` is more than `LicensingOptions.ClockSkewToleranceMinutes`
  (default 10) **behind** the high-water mark. Client wall-clock time is never the authority.
- **`LicenseVerifier`** (`Animora.Server.Licensing/LicenseVerifier.cs`) — one `sealed class`, one
  method, the **exact order of §13.2** (each step its own error code, all returning `Result<EntitlementSnapshot>`,
  never throwing for an expected outcome):
  ```
  1 parse + contract-version window        -> LICENSE_MALFORMED / CONTRACT_VERSION_UNSUPPORTED
  2 keyId resolves in the ring, not Revoked-> LICENSE_KEY_UNKNOWN
  3 signature verifies over canonical bytes-> LICENSE_SIGNATURE_INVALID
  4 audience + product + major version     -> LICENSE_AUDIENCE_MISMATCH
  5 installation binding hash              -> LICENSE_INSTALLATION_MISMATCH
  6 NotBefore <= effectiveNow              -> LICENSE_NOT_YET_VALID
  7 effectiveNow <= Expires (+ grace)      -> LICENSE_EXPIRED  (grace -> Degraded, not Invalid)
  8 monotonic clock check                  -> LICENSE_CLOCK_ROLLBACK
  ```
  Result: `sealed record EntitlementSnapshot(string TenantId, string PlanCode,
  IReadOnlySet<string> Features, int MaxSeats, int MaxDevices, EntitlementState State,
  DateTimeOffset ExpiresAtUtc, DateTimeOffset EvaluatedAtUtc, string LicenseId)` with
  `enum EntitlementState { Valid, Grace, Expired, Invalid }`. The **same** verifier and the **same**
  snapshot type are what E07 runs on the desktop — one implementation, two hosts, zero drift.
- **`LicenseIssuer`** (`Animora.Server.Licensing/LicenseIssuer.cs`) builds a payload from a
  `LicenseIssueRequest` (tenant, plan code + features, seats/devices, period, installation
  fingerprint) and signs it with the `Active` key. It is **called only** by
  `src/Animora.Server/Licensing/LicenseService.cs`, which supplies the subscription data by reading
  D10's `TenantSubscription` + `Plan` — the **only** legitimate coupling between Subscription and
  Licensing, and it is **snapshot-at-issue**, not a live lookup (R-B30: the runtime depends on the
  license, never on the subscription row).
- **Issued-license ledger** (`Animora.Persistence.SqlServer/Licensing/IssuedLicense.cs`, table
  `IssuedLicenses`, migration `109_Licensing`): `LicenseId` PK, `TenantId`, `DeviceId`,
  `InstallationBindingHash`, `KeyId`, `PlanCode`, `IssuedAtUtc`, `NotBeforeUtc`, `ExpiresAtUtc`,
  `SupersededByLicenseId`, `RevokedAtUtc`, `RevokeReason`, `ArtifactJson`. **Append-only**: the
  D07 `ServerAppendOnlyGuard` is extended with this table so a row is never updated except through
  the two allow-listed columns (`SupersededByLicenseId`, `RevokedAtUtc`/`RevokeReason`), which the
  guard permits explicitly. Reissue writes a **new** row and links the old one.
- **No online revocation channel — stated, not faked** (§13.2 invariant): `RevokedAtUtc` only affects
  **future issuance and the next contact**; there is no push. The honest mechanism is a **short
  validity window plus reissuance**, so `LicensingOptions.DefaultValidityDays` defaults to **45**
  with `GraceDays` **14**, and the reissue endpoint is cheap and idempotent. A comment in
  `LicenseService` states this limitation verbatim; `LicensingDocsTests` asserts the XML doc of
  `LicenseVerifier` contains the words "not a revocation channel" so the property survives refactors.
- **Server entitlement plane — D04's placeholder is replaced, not bypassed** (A-RULE 023, §10.1):
  `src/Animora.Server/Security/EntitlementMiddleware.cs` is **rewritten in place** (same type name,
  same pipeline position, immediately after tenant resolution and authorization) to:
  1. resolve the tenant's current `EntitlementSnapshot` from `IEntitlementProvider`
     (`src/Animora.Server/Licensing/ServerEntitlementProvider.cs`) — a **bounded, tenant-safe cache**
     keyed `("entitlement", tenantId, licenseId, WireContract.Version)` with a 5-minute TTL and a
     `MaximumSize` of 512 entries (A-RULE 033, CONVENTIONS §8);
  2. attach it to `HttpContext.Items` for handlers;
  3. **fail closed**: `Invalid` → `403 ENTITLEMENT_INVALID`; `Expired` beyond grace → `403 ENTITLEMENT_EXPIRED`;
     `Grace` → allowed but the response carries an `X-Animora-Entitlement: grace` header and
     write commands with a `RequiresEntitlement` feature are refused.
  4. **Always allowed regardless of entitlement** (an explicit allow-list, because locking a clinic
     out of its own data is not an anti-piracy feature): `/health/*`, `/api/identity/**`,
     `/api/operator/**`, `GET /tables/*` (read replication), `GET /api/commands/{operationId}`, and
     every **read** query endpoint. Writes and settlement are what an invalid entitlement blocks.
  A `RequiresFeatureAttribute`/`AnimoraPolicies.RequireFeature(name)` pair lets a specific endpoint
  demand a named feature; D06/D07 handlers are **not** edited in this phase — the plane is generic.
- **`IEntitlementGate` (B01's port) is now implemented server-side** by
  `LicensingEntitlementGate`, so the application pipeline's entitlement behavior stops being
  allow-all **on the server** while staying allow-all on the desktop until E07 swaps it there. The
  two hosts therefore never diverge in *order* of checks, only in *implementation*.
- **Tamper resistance, honestly scoped** (R-B06): the controls shipped are (a) signature over
  canonical bytes, (b) installation binding, (c) monotonic clock, (d) append-only issuance ledger
  with audit, (e) fail-closed defaults, (f) the verifier refusing an artifact whose `payload`
  re-serializes to different bytes than it was signed over (`LICENSE_MALFORMED`). Binary/assembly
  tamper-proofing, obfuscation and anti-debugging are **explicitly not** claimed — a
  `docs`-level note in the phase's PROGRESS entry records this so no later session pretends otherwise.
- **Operator + tenant endpoints** (`src/Animora.Server/Endpoints/LicensingEndpoints.cs`):
  | Route | Verb | Auth | Purpose |
  |---|---|---|---|
  | `/api/operator/licensing/keys` | GET | `RequireOperator` | key ring status (public material only) |
  | `/api/operator/licensing/keys/rotate` | POST | `RequireOperator` | rotate signing key + enqueue reissue |
  | `/api/operator/licensing/issue` | POST | `RequireOperator` | issue for a tenant+installation |
  | `/api/operator/licensing/{licenseId}/revoke` | POST | `RequireOperator` | mark revoked (+ reason, audited) |
  | `/api/licensing/current` | GET | authenticated tenant user | the caller's `EntitlementSnapshotV1` |
  | `/api/licensing/renew` | POST | authenticated + device | reissue for **this** installation; idempotent per `(tenant, installationFingerprint, currentPeriod)` |
  `/api/licensing/renew` is the desktop's only licensing write, and E07 calls exactly it.
- **Every licensing action is audited** (R-B29): issue, reissue, rotate, revoke, and **every failed
  verification** write a `ServerAuditEntry` with `TenantId`, `LicenseId`, `KeyId`, `DeviceId`,
  `RequestId` and the failing step's error code. A verification failure is a security event, not a
  debug log line.
- **New error codes:** `LICENSE_MALFORMED`, `LICENSE_KEY_UNKNOWN`, `LICENSE_SIGNATURE_INVALID`,
  `LICENSE_AUDIENCE_MISMATCH`, `LICENSE_INSTALLATION_MISMATCH`, `LICENSE_NOT_YET_VALID`,
  `LICENSE_EXPIRED`, `LICENSE_CLOCK_ROLLBACK`, `LICENSE_REVOKED`, `ENTITLEMENT_INVALID`,
  `ENTITLEMENT_EXPIRED`, `FEATURE_NOT_LICENSED`
  (`ENTITLEMENT_REQUIRED` exists from B01 — reuse; `CONTRACT_VERSION_UNSUPPORTED` from D05 — reuse).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Server.Licensing/Animora.Server.Licensing.csproj` | create | new project, 3 project refs only |
| `src/Animora.Server.Licensing/LicensePayloadCanonicalizer.cs` | create | reuse of B02's canonical JSON for signing bytes |
| `src/Animora.Server.Licensing/LicenseSigner.cs` | create | ES256 sign over canonical payload |
| `src/Animora.Server.Licensing/LicenseVerifier.cs` | create | the 8 ordered checks → `EntitlementSnapshot` |
| `src/Animora.Server.Licensing/LicenseKeyRing.cs` | create | rotation-aware verifying key set |
| `src/Animora.Server.Licensing/LicenseIssuer.cs` | create | payload build + sign |
| `src/Animora.Server.Licensing/InstallationBinding.cs` | create | binding-hash computation + fixed-time compare |
| `src/Animora.Server.Licensing/EntitlementSnapshot.cs` | create | snapshot record + `EntitlementState` |
| `src/Animora.Contracts.Wire/Licensing/*.cs` | create | `LicenseArtifactV1`, `LicensePayloadV1`, `EntitlementSnapshotV1`, `LicenseIssueRequestV1`, `KeyRingStatusV1` |
| `src/Animora.Persistence.SqlServer/Licensing/LicenseSigningKey.cs` | create | signing key row (DP-protected private key) |
| `src/Animora.Persistence.SqlServer/Licensing/IssuedLicense.cs` | create | append-only issuance ledger |
| `src/Animora.Persistence.SqlServer/Licensing/LicensingConfigurations.cs` | create | 2 configurations + filtered unique index |
| `src/Animora.Persistence.SqlServer/Migrations/109_Licensing*` | create | 2 tables + indexes |
| `src/Animora.Persistence.SqlServer/Context/ServerAppendOnlyGuard.cs` | modify | add `IssuedLicenses` + its 3 allowed columns |
| `src/Animora.Server/Configuration/LicensingOptions.cs` | create | validity/grace/skew/retention options |
| `src/Animora.Server/Licensing/LicenseService.cs` | create | issue/reissue/rotate/revoke, transactional + audited |
| `src/Animora.Server/Licensing/ServerEntitlementProvider.cs` | create | bounded tenant-safe entitlement cache |
| `src/Animora.Server/Licensing/LicensingEntitlementGate.cs` | create | real `IEntitlementGate` (server side) |
| `src/Animora.Server/Security/EntitlementMiddleware.cs` | modify | replace the no-op with the real plane + allow-list |
| `src/Animora.Server/Security/AnimoraPolicies.cs` | modify | `RequireFeature(name)` + `RequiresFeatureAttribute` |
| `src/Animora.Server/Endpoints/LicensingEndpoints.cs` | create | 6 routes |
| `src/Animora.Server/Program.cs` | modify | options, key ring load, gate, endpoints, `licensing` readiness check |
| `src/Animora.Application/Results/ErrorCodes.cs` | modify | += 12 codes |
| `tests/Animora.Server.Tests/LicenseVerificationTests.cs` | create | the 8 checks, each failing independently |
| `tests/Animora.Server.Tests/LicenseAntiCloningTests.cs` | create | copied artifact fails on a 2nd installation |
| `tests/Animora.Server.Tests/LicenseClockRollbackTests.cs` | create | wound-back clock cannot extend expiry |
| `tests/Animora.Server.Tests/KeyRotationTests.cs` | create | retired key still verifies, revoked does not |
| `tests/Animora.Server.Tests/EntitlementPlaneTests.cs` | create | fail-closed, grace header, allow-list, cache key |

## Implementation steps
1. Create `Animora.Server.Licensing` with its three project references; add the layering fact.
2. Add the wire licensing DTOs; write the canonicalizer wrapper, `LicenseSigner`, `LicenseKeyRing`,
   `InstallationBinding`, `EntitlementSnapshot`.
3. Write `LicenseVerifier` with the eight ordered checks and one error code each.
4. Add `LicenseSigningKey` + `IssuedLicense` + configurations + migration `109_Licensing`; extend
   `ServerAppendOnlyGuard`.
5. Write `LicenseService` (issue / reissue / rotate / revoke), reading D10's subscription **once**
   at issue time; every path audited and transactional.
6. Write `ServerEntitlementProvider` (bounded, tenant-safe cache) and `LicensingEntitlementGate`.
7. **Replace** the body of `EntitlementMiddleware`; add the allow-list and the grace header;
   add `RequireFeature`.
8. Write `LicensingEndpoints`; register options, the key-ring loader and a `licensing` readiness
   check (key ring present and one `Active` key, or explicitly "unlicensed-dev" when
   `LicensingOptions.DevelopmentUnlicensed == true`, which `ValidateOnStart` forbids outside
   Development).
9. Write the five test classes; the anti-cloning test issues for installation A and verifies against
   installation B's identity; the rollback test drives `IMonotonicClock` and a wound-back wall clock.
10. Verify: a valid license passes all eight checks; each check fails alone with its own code; a
    rotated key ring still verifies yesterday's license; a revoked key does not; the middleware
    blocks a write and allows `/api/identity` and `GET /tables/*`; two tenants never share a cached
    snapshot.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- New project `Animora.Server.Licensing` with `LicenseSigner`, `LicenseVerifier`, `LicenseKeyRing`,
  `LicenseIssuer`, `InstallationBinding`, `LicensePayloadCanonicalizer`, `EntitlementSnapshot`,
  `EntitlementState`.
- Wire: `LicenseArtifactV1`, `LicensePayloadV1`, `EntitlementSnapshotV1`, `LicenseIssueRequestV1`,
  `KeyRingStatusV1`. `WireContract.Version` stays `1`.
- Persistence.SqlServer: `LicenseSigningKey`, `IssuedLicense`, `LicensingConfigurations`,
  migration `109_Licensing`, `ServerAppendOnlyGuard` += `IssuedLicenses`.
- Server: `LicensingOptions`, `LicenseService`, `ServerEntitlementProvider`,
  `LicensingEntitlementGate`, the **real** `EntitlementMiddleware`, `RequireFeature`,
  `LicensingEndpoints` (6 routes), the `licensing` readiness check.
- `ErrorCodes` += the 12 codes listed above.
- DI: `IEntitlementGate` → `LicensingEntitlementGate` (**server container only**; `App.axaml.cs`
  unchanged — E07 owns the desktop swap).
- No `Animora.Contracts` change, no `CommandTypes` entry.

## Rules that apply here
- **A-RULE 030 / §13.2** — the signature proves authenticity; the **installation binding** is what
  prevents cloning, and it has its own distinct error code and its own test.
- **A-RULE 011 / §13.2 step 5** — verification uses the monotonic high-water clock; a wound-back
  wall clock cannot extend a license and is reported as `LICENSE_CLOCK_ROLLBACK`.
- **R-B30 / §13.1** — Licensing reads Subscription **once, at issue time**, and the runtime path
  depends only on the signed artifact. No request-time subscription lookup exists.
- **A-RULE 023 / R-B20** — entitlement is the **third** plane, evaluated after authentication and
  authorization, never merged with either; the middleware order is asserted by a test.
- **A-RULE 016** — no online license server, no revocation service, no outbound call anywhere in
  the licensing path; the limitation is documented instead of faked.
- **A-RULE 027 / §10.6** — private key material is Data-Protection-protected and part of the backup
  set; without the key ring the server can verify but **refuses to sign** (fail closed).
- **A-RULE 033** — the entitlement cache key is `(tenant, licenseId, contract version)` and bounded.
- **R-B29** — every issuance, rotation, revocation and **failed verification** is audited.

## Out of scope
- The desktop-side verifier wiring, offline entitlement UX, and license renewal on reconnect → E07
  (it consumes `Animora.Server.Licensing` and `/api/licensing/renew` unchanged).
- Operator **UI** for keys/licenses → E01.
- Binary obfuscation / anti-debugging / packer-level protection → **never** claimed by this product.
- Feature gating of specific business endpoints → the plane exists here; individual
  `RequireFeature` attributes are applied when a plan actually differentiates a feature.
- Rate limiting on licensing endpoints → D13.
- Gate U (`ARCHITECTURE.md` §19) as a *recorded* validation gate → E08; this phase ships the
  behavior and its unit-level proof.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors, and `Animora.Server.Licensing`
  references only `Animora.Domain`, `Animora.Application`, `Animora.Contracts.Wire` (layering fact).
- `LicenseVerificationTests` green: a correctly issued license returns `EntitlementState.Valid`; each
  of the eight checks can be made to fail **in isolation** and returns its own error code, in the
  documented order (a malformed artifact never reaches the binding check, etc.).
- `LicenseAntiCloningTests` green: an artifact issued for installation A, verified byte-for-byte on
  installation B, returns `LICENSE_INSTALLATION_MISMATCH` — never `Valid`, never a generic signature
  error.
- `LicenseClockRollbackTests` green: with the wall clock wound back one year past an expiry, the
  verifier returns `LICENSE_CLOCK_ROLLBACK`/`LICENSE_EXPIRED` using the high-water mark, and a
  successful verification advances the high-water mark.
- `KeyRotationTests` green: after a rotation, a license signed by the retired key still verifies;
  after the retired key is revoked it does not; only one key is ever `Active`; rotation enqueues one
  `license.reissue.required` message per active tenant.
- `EntitlementPlaneTests` green: an invalid/expired entitlement blocks `POST /api/commands` with
  `403` and the correct code; `Grace` allows it and emits `X-Animora-Entitlement: grace`; every
  allow-listed route still works with **no** license at all; the cache never serves tenant A's
  snapshot to tenant B (gate L shape).
- `/health/ready` reports `licensing: healthy | unlicensed | key-ring-missing`, and the server
  **refuses to start** in non-Development with `DevelopmentUnlicensed == true`.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D11 done; advance "Next phase" to `D12`; append a "Repo state" entry listing: the
`Animora.Server.Licensing` project and its **deliberately EF/ASP-free** reference set (**E07
compiles the same verifier into the desktop**), the `LicenseArtifactV1` format + `ES256` decision +
canonical-JSON reuse, `LicensePayloadV1`'s field set, the `LicenseSigningKeys` ring + one-`Active`-key
rule + rotation semantics + `RetiredKeyRetentionDays`, `InstallationBinding`'s hash formula (**the
anti-cloning anchor, A-RULE 030**), the monotonic-clock rule, `LicenseVerifier`'s **eight ordered
checks and their error codes** (**E07 depends on this exact order**), `EntitlementSnapshot` +
`EntitlementState` (**E07's client cache shape**), the `IssuedLicenses` append-only ledger + the 3
allow-listed mutable columns, the **replaced** `EntitlementMiddleware` + its allow-list + grace
header, `LicensingEntitlementGate` (server-only DI swap), the 6 licensing routes (**E07 calls
`/api/licensing/renew`; E01 renders the key ring**), `LicensingOptions` defaults (validity 45 /
grace 14 / skew 10 / retention 400), the explicit "no online revocation channel" limitation, the
explicit "no binary tamper-proofing claimed" limitation, the 12 new error codes, deviations.
