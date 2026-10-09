# Phase G06 — Online auth, entitlement & device lifecycle (real entitlement gate replaces `AllowAllEntitlementGate`; credential-mirror offline fallback)

**Band:** M · **Track:** G (sync / integration / release) · **Depends on:** G05, S16 · **Next:** G07
**Satisfies:** R-B04, R-B06, R-B20, R-B21, R-B36 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; regenerates scope-inventory row E07 (Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

> Baseline verified 2026-10-09: client migration head `031` + the G04 replica migration (verify by listing — the client ordinal moved when G04 landed `ReplicaStore`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` = live count (161 at A02 + codes added by S04/S06/S16 and G02/G04/G05; S16 added the 8 `LICENSE_*` codes this phase reuses — never freeze, `grep -c`), `WireContract.Version` = 2 after S06 (supported {1,2}); `src/` projects include `Animora.Sync.Abstractions` (G01), `Animora.Sync.Custom` (G02), `Animora.Server.Licensing` with its client-reusable `Verification/` half (S16) (+ server-track `S01–S20`).
> **Next-free-ordinal rule.** This phase adds **one client migration** (the sealed-entitlement + device-fingerprint + credential-mirror local tables, all `[LocalOnly]`). Do **not** hard-code an ordinal: list `src/Animora.Persistence.Sqlite/Migrations/` and take the **next free client ordinal** on `AnimoraDbContext` (expected `032`+ after G04's replica migration — verify). Server sequence (101+) is untouched by this client phase.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. Every existing type, signature, path and command you need is in
> **§Reference pack**; every new name is fixed in **§Design decisions**. Do **not** re-open the repo to "understand" it and
> do not re-derive decisions. If — and only if — a compile error proves a snippet diverges from the tree, fix the smallest
> thing and log it in the `PROGRESS.md` deviation log. Names here are the real ones; there is no alias to resolve.
> Implement exactly this scope: nothing from a later phase, nothing "while we're here".

**Environment (do not probe):**
- Build / test / run only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]` (0 warnings / 0 errors), `scripts/sandbox/test.sh [filter]`,
  `scripts/sandbox/run.sh` (desktop smoke → `SMOKE RUN OK`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent).
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — **never** `dotnet tool install`. Add the one client migration with it
  (`ef migrations add <NNN>_OnlineEntitlement -c AnimoraDbContext`), ordinal from the folder listing.
- **Forbidden tech (restated, binding):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11,
  per-tenant database, hard-coded TLS version, Native AOT with EF Core. **No new package** — the online auth uses the same
  `HttpClient` G02/G05 configured; the sealed-cache protection uses `System.Security.Cryptography` (DPAPI on Windows via the
  existing `Animora.Infrastructure.Local` protection seam) — no third-party crypto.

**Offline invariant (non-negotiable).** The real `OnlineEntitlementGate` replaces `AllowAllEntitlementGate` **only when a server
base address is configured** (same guard G02/G05 use); unconfigured, `AllowAllEntitlementGate` stays and the desktop runs exactly
as today. Even when configured, the gate decides from the **sealed local entitlement cache** (S16 `SealedEntitlement` + the
offline grace window), **never** from a live server call — so the desktop authenticates and operates offline via the
**credential-mirror** (the last successful online login's verifier material + the B04b signed, device-bound capability snapshot),
failing closed only at expiry (R-B21, CONVENTIONS §11, Standing principle 1). A revoked device loses offline capability at its
**next** online contact, never retroactively (ARCH §10.4).

**Condensed CONVENTIONS rules used here:** §2 layering (online client plumbing in `Animora.Client.Services` + `Animora.Sync.Custom`;
the **only** sanctioned client reference to a server project is `Animora.Server.Licensing.Verification` — whitelisted by S16's edit
to `LayeringRulesTests`; **no** Npgsql, **no** `Animora.Server`); §4 C# style (`sealed`, nullable honored, `CancellationToken ct = default`
+ `ConfigureAwait(false)`, no `.Result`/`.Wait()`, expected failures as `Result` not exceptions, time via `IClock`, ULID ids);
§6 three planes never merged (Authentication ≠ Authorization ≠ Entitlement, checked in order — ARCH §10.1, A-RULE 023);
§10.4 fail-closed offline snapshot + monotonic high-water clock (A-RULE 011/029).

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §10.1 (three planes, ordered), §10.2 (authentication: bearer/refresh, device binding), §10.4 (offline
  capability snapshot, fail-closed, high-water clock, revoked-device-at-next-contact), §13.2 (licence trust model the snapshot
  reuses) — only these `§`.
- `backend-roadmap/phases/PHASE-S04-identity-server.md` §Design decisions (the `/auth/login`, `/auth/refresh`, `/auth/logout`,
  `/auth/device/register` surface + `LoginResponse`/token lifetimes + device states + `DEVICE_REVOKED`/`SESSION_EXPIRED`) and
  `PHASE-S16-licensing-anti-piracy.md` §Design decisions (the `Verification/` half this phase consumes: `LicenseEnvelope`,
  `LicenseVerifier`, `LicensePolicy.Evaluate`, `CapabilityGate.IsAllowed`, `HighWaterClock`, `DeviceFingerprint.Compute`,
  `SealedEntitlement`/`ISealedEntitlementStore`/`SealedEntitlementMerge`, the `/licensing/activate|renew|status|keys` surface,
  `LicenseResponse`/`LicenseStatusResponse`, the 8 `LICENSE_*` ErrorCodes).
- `src/Animora.Application/Security/*` (the `IEntitlementGate`, `AllowAllEntitlementGate`, `CapabilityEvaluator`,
  `CapabilitySnapshotIssuer`, `ICapabilityStore`/`StoredCapabilitySnapshot`, `ICapabilitySnapshotSigner` surfaces — reused,
  not changed in signature) and `src/Animora.Sync.Custom/*` (the configured `HttpClient` + `ServerEndpointOptions` guard G02/G05 use).
- (Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Light up the **online** legs of all three identity planes while keeping the desktop fully offline-capable. (1) **Authentication:** a
client `IOnlineAuthClient` that signs in against the S04 `/auth/*` surface, stores the bearer/refresh pair in a protected session
store, and an auth **delegating handler** on the shared `HttpClient` that attaches the bearer and performs the single-flight 401
refresh (reuse-detection honoured) — the command transport (G05) and state engine (G02) both benefit without knowing about tokens.
(2) **Entitlement:** the real `OnlineEntitlementGate : IEntitlementGate` that **replaces `AllowAllEntitlementGate`** and decides
from the **sealed local entitlement cache** (S16 `SealedEntitlement` + `LicensePolicy`/`CapabilityGate`), refreshed opportunistically
on online contact but **evaluated offline**, fail-closed at grace expiry. (3) **Device lifecycle:** collect the stable
`DeviceFingerprint` components once, register/activate the device online, mirror the credential material + capability snapshot
locally (the **credential-mirror** fallback) so a cold offline start still authenticates, and drop offline authority the moment a
revoked device next reaches the server. `AllowAllEntitlementGate` stays as the documented offline-dev fallback (unconfigured).

---

## §Reference pack (verbatim from the tree + S04/S16, 2026-10-09)

```csharp
// Animora.Application.Security (B04) — the gate this phase replaces + the snapshot machinery it drives; reuse, never redefine
public interface IEntitlementGate {
    ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct);
    ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct);
}
public sealed class AllowAllEntitlementGate : IEntitlementGate { /* always Result.Ok() — REPLACED here when configured */ }
public sealed class CapabilityEvaluator(ICapabilitySnapshotSigner signer) {
    public CapabilityEvaluation Evaluate(StoredCapabilitySnapshot? snapshot, string code, string tenantId,
        string userId, string deviceId, string securityStamp, DateTimeOffset nowUtc); // fail-closed; discards a tampered snapshot
}
public sealed record StoredCapabilitySnapshot(/* TenantId, UserId, DeviceId, SecurityStamp, PermissionsJson,
    PerCapabilityExpiryJson, IssuedUtc, Signature, KeyId … */);
public interface ICapabilityStore { Task<Result<StoredCapabilitySnapshot?>> ReadAsync(CancellationToken ct = default);
    Task<Result> ReplaceAsync(StoredCapabilitySnapshot snapshot, CancellationToken ct = default); }

// Animora.Server.Licensing.Verification (S16) — the client-reusable half (Domain + Contracts.Wire only); reuse, never redefine
public static class LicenseEnvelope { public static Result<ParsedLicense> Parse(string token); }
public static class LicenseVerifier { public static Result<LicensePayload> Verify(ParsedLicense p, ILicenseKeySet keys); }
public static class LicensePolicy {
    public static LicenseVerdict Evaluate(LicensePayload p, DateTimeOffset nowUtc, HighWaterClock clock, string localInstallationIdHash);
}
public enum LicenseState { Valid, Grace, ReadOnly, Locked }                       // from LicenseVerdict.State
public static class CapabilityGate { public static bool IsAllowed(LicenseState state, RiskLevel risk); }
public readonly record struct HighWaterClock(DateTimeOffset HighWaterUtc) { public HighWaterClock Advance(DateTimeOffset nowUtc); }
public static class DeviceFingerprint { public static string Compute(IReadOnlyList<string> stableComponents); } // SHA-256 hex (64)
public sealed record SealedEntitlement(string LicenseToken, long HighWaterUnixMs, long HighestSequenceSeen,
    string TenantId, string InstallationIdHash, long WrittenUnixMs);
public interface ISealedEntitlementStore { Task<Result<SealedEntitlement?>> ReadAsync(CancellationToken ct);
    Task<Result> WriteAsync(SealedEntitlement e, CancellationToken ct); }
public static class SealedEntitlementMerge { public static SealedEntitlement Merge(SealedEntitlement? existing, SealedEntitlement incoming); } // forward-only

// ====== S04 auth surface this client consumes (verbatim from PHASE-S04) ======
//   POST /auth/login   body LoginRequest(TenantCode, UserName, Password, DeviceInstallationIdHash)
//                      -> 200 LoginResponse(AccessToken, ExpiresInSeconds, RefreshToken, UserId, TenantId, DeviceId, Role, MustChangePassword)
//                         | 401 CREDENTIALS_INVALID | 423 USER_LOCKED_OUT | 403 DEVICE_REVOKED | 403 TENANT_SUSPENDED
//   POST /auth/refresh body RefreshRequest(RefreshToken) -> same LoginResponse shape | 401 SESSION_EXPIRED (reuse ⇒ family revoked)
//   POST /auth/logout  (authenticated) revokes the refresh family
//   POST /auth/device/register (authenticated) -> device in Pending (Manager approves via S05 devices.manage)
// ====== S16 licensing surface this client consumes (verbatim from PHASE-S16) ======
//   POST /licensing/activate | /renew  body LicenseRequest(installationIdHash, currentSequence?)
//                      -> LicenseResponse(token, expiresUtc, offlineGraceUntilUtc, sequence, serverTimeUtc)
//   GET  /licensing/status -> LicenseStatusResponse(grantStatus, validUntilUtc?, planCode?, seatLimit?, maxDevices?, activeLicenses, serverTimeUtc)
//   GET  /licensing/keys   (anonymous) -> LicensePublicKeySet   ; 403 ENTITLEMENT_REQUIRED / LICENSE_REVOKED on gated routes

// Animora.Domain.Results / Time (A02): Result/ErrorCode/ErrorCodes ; IClock.UtcNow. ULID via IdGenerator. RiskLevel (B04).
```

## §Design decisions

- **Online auth client (`OnlineAuthClient : IOnlineAuthClient`, `src/Animora.Client.Services/Identity/`)** over the configured
  `HttpClient` (`"animora-sync"` base + `WireContract.HeaderName`=`2`). `LoginAsync(tenantCode, userName, password, ct)`: computes
  `DeviceInstallationIdHash` from `DeviceFingerprint.Compute(IDeviceFingerprintSource.Collect())` (see below), `POST /auth/login`;
  on 200 persists the session (`ProtectedSessionStore`, below), triggers the credential-mirror write (below), and performs the first
  `/licensing/activate` → sealed cache; on 401 `CREDENTIALS_INVALID` / 423 `USER_LOCKED_OUT` / 403 `DEVICE_REVOKED` returns the exact
  `Result.Fail(<code>, detail)`. `RefreshAsync(ct)` and `LogoutAsync(ct)` map `/auth/refresh` (401 `SESSION_EXPIRED` ⇒ clear session,
  surface re-login) and `/auth/logout`. Offline (unreachable server) ⇒ `Result.Fail(NetworkUnavailable)` and the shell falls back to
  the **offline path** (the capability snapshot + sealed cache), never a hard block.
- **Protected session store (`ProtectedSessionStore : ISessionStore`, `Animora.Client.Services`)** persists
  `ClientSession(AccessToken, AccessExpiresUtc, RefreshToken, UserId, TenantId, DeviceId, Role, SecurityStamp)` in the new local
  `ClientSessions` table (`[LocalOnly]`), **both tokens OS-protected** via the existing `Animora.Infrastructure.Local` protection
  seam (DPAPI on Windows). Exactly one active session row per `(TenantId, UserId, DeviceId)`.
- **Auth delegating handler (`BearerAuthHandler : DelegatingHandler`, `src/Animora.Sync.Custom/`)** on the shared named `HttpClient`:
  attaches `Authorization: Bearer <access>` from `ISessionStore`; on a **401** response (not the login/refresh/keys routes) does a
  **single-flight** `RefreshAsync` (an `IAsyncLock`/`SemaphoreSlim(1,1)` so concurrent 401s refresh once), then retries the original
  request once with the new token; a second 401 or a `SESSION_EXPIRED` refresh ⇒ surface `Unauthorized` (no infinite loop). This is
  why G05's transport and G02's engine never handle tokens themselves (G05 Out-of-scope → here).
- **Real entitlement gate (`OnlineEntitlementGate : IEntitlementGate`, `src/Animora.Client.Services/Security/`, registered
  `Replace` when configured):** on each `EvaluateAsync(command|query)` it (a) reads the `SealedEntitlement` (S16
  `ISealedEntitlementStore`), parses+verifies its licence token offline against the embedded/cached `LicensePublicKeySet`
  (`LicenseEnvelope.Parse` → `LicenseVerifier.Verify`), (b) evaluates `LicensePolicy.Evaluate(payload, clock.HighWaterUtc,
  localInstallationIdHash)` → `LicenseVerdict(State)`, (c) maps the command/query to its `RiskLevel` (reuse the B04 permission-code
  risk catalog; a query is read-class) and returns `Result.Ok()` iff `CapabilityGate.IsAllowed(State, risk)`, else
  `Result.Fail(EntitlementRequired, "<state>:<reason>")`. **It never calls the server to decide** (A-RULE 016/032: zero-Internet
  runtime path) — the sealed cache is the authority; freshness is a separate opportunistic refresh. Entitlement runs **after**
  authentication and authorization in the pipeline (B04 `Entitlement` behavior position — unchanged; this only swaps the gate impl).
- **High-water clock persistence & monotonicity.** The `HighWaterClock` is stored inside the `SealedEntitlement`
  (`HighWaterUnixMs`); every online `LicenseResponse.serverTimeUtc` **advances** it via `HighWaterClock.Advance` and
  `SealedEntitlementMerge.Merge` (forward-only: a lower server time or a lower `sequence` is ignored — blob-rollback + clock-rollback
  guard, A-RULE 011). The gate reads `max(IClock.UtcNow, HighWaterUtc)` for every expiry decision. Write is atomic (temp file +
  rename, OS-protected) via `ISealedEntitlementStore`'s client impl here.
- **Device fingerprint source (`IDeviceFingerprintSource.Collect() → IReadOnlyList<string>`, impl in `Animora.Infrastructure.Local`):**
  gathers stable machine components (machine GUID / OS install id / primary volume serial — platform-appropriate, **raw values never
  leave the machine**); only `DeviceFingerprint.Compute(...)` (the 64-hex hash) is sent as `DeviceInstallationIdHash`. A
  `DeviceLifecycleService` drives `/auth/device/register` on first online login of a new install and records the returned `DeviceId`
  + state; a `403 DEVICE_REVOKED` on login/refresh clears the session, **wipes the sealed cache + capability snapshot** (offline
  authority gone at next contact, ARCH §10.4), and surfaces a re-activation prompt.
- **Credential-mirror fallback (the offline invariant's teeth).** On every successful online login the client mirrors, locally and
  OS-protected: (i) the signed, device-bound **capability snapshot** fetched/issued at login (reuse B04b `CapabilitySnapshotIssuer`
  output shape via `ICapabilityStore.ReplaceAsync`), (ii) the **sealed entitlement** (licence token + high-water + sequence), (iii) a
  one-way **password verifier** (`PasswordHasher<ClientSession>` over the entered password, stored as `OfflineCredentialVerifier` in
  `ClientSessions`) so a cold offline start can authenticate the same user **without the server**: `LoginAsync` detects
  unreachable-server and, if a matching mirror exists and its capability snapshot + sealed cache are still inside their windows,
  verifies the password locally and grants the offline session. Fail-closed: no mirror, tampered snapshot, expired grace, or
  clock-rollback > policy tolerance ⇒ deny (read-only or locked per `LicensePolicy`), never a silent full grant.
- **New local tables (one client migration, all `[LocalOnly]`, next free ordinal):** `ClientSessions(Id, TenantId, UserId, DeviceId,
    AccessToken(protected), AccessExpiresUtc, RefreshToken(protected), Role, SecurityStamp, OfflineCredentialVerifier, UpdatedUtc)`
  — unique `(TenantId, UserId, DeviceId)`; `SealedEntitlements(TenantId PK, LicenseToken(protected), HighWaterUnixMs,
    HighestSequenceSeen, InstallationIdHash, WrittenUnixMs)` — one row per tenant; the capability snapshot continues to use the
  **existing** B04b `ICapabilityStore` table (no new table for it). No `SyncableEntity`, no server sequence touched.
- **New ErrorCodes (region "Online entitlement", +0/2):** reuse S16's `EntitlementRequired`/`LicenseRevoked`/`LicenseClockRollback`/
  `LicenseExpired` and S04's `CredentialsInvalid`/`SessionExpired`/`UserLockedOut`/`DeviceRevoked` and A02/G02's
  `NetworkUnavailable`/`Unauthorized`. Add `OfflineCredentialMismatch = new("OFFLINE_CREDENTIAL_MISMATCH", ErrorCode.Forbidden)` and
  `OfflineSessionUnavailable = new("OFFLINE_SESSION_UNAVAILABLE", ErrorCode.Forbidden)` **only if absent** (`grep ErrorCodes.cs`
  first; if a sibling already introduced an equivalent, reuse).
- **DI (`AddAnimoraOnlineIdentity`, called from the same `App.axaml.cs` site as G02/G05 — fold into the existing `Animora.Sync.Custom`
  extension or a sibling in `Animora.Client.Services`, document which):** when `ServerEndpointOptions.IsConfigured` ⇒
  `services.Replace(ServiceDescriptor.Singleton<IEntitlementGate, OnlineEntitlementGate>())`, register `IOnlineAuthClient`,
  `ISessionStore`, `ISealedEntitlementStore` (client impl), `IDeviceFingerprintSource`, `DeviceLifecycleService`, and add
  `BearerAuthHandler` to the named `HttpClient` pipeline. Unconfigured ⇒ `AllowAllEntitlementGate` (B04 registration) stays and no
  handler is added.
- **Not in this phase:** the server identity/licensing implementations themselves (**S04/S16**); the command transport (**G05**) and
  state engine (**G01–G04**) — they only *benefit* from the handler here; the validation/RLS/restore gates (**G07**) and the
  perf/offline-budget proof (**G08**); packaging the embedded public key set (**G09**). Cross-tenant analytics, passkeys/external IdP
  — deferred (ROADMAP §5).

## §Implementation plan

1. `src/Animora.Client.Services/Identity/OnlineAuthClient.cs` (+ `IOnlineAuthClient`), `ClientSession` record,
   `ProtectedSessionStore.cs` (+ `ISessionStore`) — `ConfigureAwait(false)` throughout.
2. `src/Animora.Sync.Custom/BearerAuthHandler.cs` — single-flight 401 refresh delegating handler.
3. `src/Animora.Client.Services/Security/OnlineEntitlementGate.cs` — sealed-cache-driven `IEntitlementGate` (offline-only decision,
   reuses S16 `Verification/` + B04 `CapabilityEvaluator`).
4. `src/Animora.Infrastructure.Local/Identity/DeviceFingerprintSource.cs` (+ `IDeviceFingerprintSource`),
   `SealedEntitlementStore.cs` (atomic, OS-protected `ISealedEntitlementStore` impl);
   `src/Animora.Client.Services/Identity/DeviceLifecycleService.cs`.
5. `src/Animora.Persistence.Sqlite/` — EF config for `ClientSessions` + `SealedEntitlements` (`[LocalOnly]`); add migration
   `ef migrations add <next>_OnlineEntitlement -c AnimoraDbContext` (ordinal from folder listing).
6. `src/Animora.Domain/Results/ErrorCodes.cs` — `#region Online entitlement`: add the 2 codes only if absent.
7. DI: conditional `Replace` of `IEntitlementGate` + registrations + `BearerAuthHandler` wiring in the `Animora.Sync.Custom` /
   `Animora.Client.Services` extension.
8. Tests in `tests/Animora.Application.Tests/` (gate + capability interplay), `tests/Animora.Sync.Tests/` (handler + auth client over
   an `HttpMessageHandler` fake), `tests/Animora.Persistence.Tests/` (local tables + sealed-cache forward-only): see Acceptance.
9. `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Application` + `scripts/sandbox/test.sh Sync` +
   `scripts/sandbox/test.sh Persistence` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK` (unconfigured ⇒ `AllowAllEntitlementGate`).
10. `PROGRESS.md` per recipe; commit via `scripts/git/push.sh`.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Client.Services/Identity/OnlineAuthClient.cs` (new) | `IOnlineAuthClient`; login/refresh/logout over `/auth/*`; offline credential-mirror path |
| `src/Animora.Client.Services/Identity/ProtectedSessionStore.cs` (new) | `ISessionStore` + `ClientSession`; OS-protected token persistence |
| `src/Animora.Client.Services/Identity/DeviceLifecycleService.cs` (new) | device register/activate; `DEVICE_REVOKED` ⇒ wipe offline authority |
| `src/Animora.Sync.Custom/BearerAuthHandler.cs` (new) | single-flight 401 refresh delegating handler on the shared `HttpClient` |
| `src/Animora.Client.Services/Security/OnlineEntitlementGate.cs` (new) | real `IEntitlementGate`; decides from sealed cache + `LicensePolicy`/`CapabilityGate`, offline-only |
| `src/Animora.Infrastructure.Local/Identity/DeviceFingerprintSource.cs` (new) | `IDeviceFingerprintSource.Collect()`; raw components never leave the machine |
| `src/Animora.Infrastructure.Local/Identity/SealedEntitlementStore.cs` (new) | atomic OS-protected `ISealedEntitlementStore` impl; forward-only via `SealedEntitlementMerge` |
| `src/Animora.Persistence.Sqlite/**` (edit) | `ClientSessions` + `SealedEntitlements` `[LocalOnly]` mappings + migration `<next>_OnlineEntitlement` |
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit, maybe) | 2 codes only if absent |
| `src/Animora.Sync.Custom/SyncCustomServiceCollectionExtensions.cs` (edit) | conditional `Replace` of `IEntitlementGate` + registrations + handler |
| `tests/Animora.Application.Tests/*.cs`, `tests/Animora.Sync.Tests/*.cs`, `tests/Animora.Persistence.Tests/*.cs` (new) | gate, handler, auth client, device lifecycle, sealed-cache tests |

## Contracts / schema added

No new wire DTO (reuses S04's `Animora.Contracts.Wire.Auth` + S16's `Animora.Contracts.Wire.Licensing`). New client types:
`IOnlineAuthClient`/`OnlineAuthClient`, `ISessionStore`/`ProtectedSessionStore`/`ClientSession`, `BearerAuthHandler`,
`OnlineEntitlementGate`, `IDeviceFingerprintSource`/`DeviceFingerprintSource`, `SealedEntitlementStore`, `DeviceLifecycleService`.
ErrorCodes +0/2 (`OFFLINE_CREDENTIAL_MISMATCH`, `OFFLINE_SESSION_UNAVAILABLE` iff absent). One client migration
(`ClientSessions`, `SealedEntitlements`, both `[LocalOnly]`), next free client ordinal. No server migration, no `WireContract.Version` bump.

## Rules that apply here

- **Three planes, ordered (ARCH §10.1, A-RULE 023, R-B20):** this phase lights the online legs of all three but never merges them —
  authentication (session/token), authorization (B04 re-reads stored role every request — unchanged), entitlement (this gate) stay
  distinct and are evaluated in order; "authorized but not entitled" is a different answer than "not permitted".
- **Offline-first / zero-Internet (ARCH §10.4, A-RULE 016/032, §11, R-B21):** the gate decides from the sealed local cache + capability
  snapshot, never a live server call; fail-closed at grace expiry on `max(now, high-water)`; a revoked device loses authority at its
  next contact, never retroactively; the credential-mirror lets a cold offline start authenticate the same user.
- **Anti-piracy trust model (ARCH §13.2, R-B06):** the licence/snapshot are detached-signature verified against a **public** key set
  only (the client holds no signing secret); a tampered artifact is dropped whole, never re-signed; the honest threat model (client
  gating is not an unbreakable boundary; server-side enforcement backs any online-touching capability) is restated, not re-litigated.
- **Layering / C# style (§2/§4, R-B10):** online plumbing in `Animora.Client.Services` + `Animora.Sync.Custom`; the **only**
  client→server reference is `Animora.Server.Licensing.Verification` (S16 whitelist); **no** Npgsql, **no** `Animora.Server`; `sealed`,
  `Result`, `IClock`, ULID, `ConfigureAwait(false)`.

## Out of scope (owning phase)

- Server identity + licensing implementations → **S04 / S16** (this consumes their surface).
- Command transport + settlement → **G05**; state replication/conflict engine → **G01–G04** (they only benefit from `BearerAuthHandler`).
- PostgreSQL CI / tenancy-RLS / restore gates → **G07**; weak-host/weak-client perf proof + offline budget → **G08**.
- Packaging the embedded `LicensePublicKeySet` + SBOM → **G09**.
- Passkeys / external IdP, cross-tenant analytics → deferred (ROADMAP §5).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK` (unconfigured ⇒
   `AllowAllEntitlementGate` — the desktop starts with no server).
2. `scripts/sandbox/test.sh Application` + `scripts/sandbox/test.sh Sync` + `scripts/sandbox/test.sh Persistence` green with:
   `OnlineEntitlementGateTests.ValidLicense_AllowsByRisk`, `OnlineEntitlementGateTests.Grace_AllowsLowNormalHigh_DeniesReadOnly`,
   `OnlineEntitlementGateTests.ReadOnly_AllowsQuery_DeniesCommand`, `OnlineEntitlementGateTests.Locked_DeniesEverything`,
   `OnlineEntitlementGateTests.DecidesWithoutServerCall` (asserts the fake `HttpMessageHandler` is **never** hit during evaluate),
   `OnlineEntitlementGateTests.BackwardClock_DoesNotReviveExpired` (high-water),
   `BearerAuthHandlerTests.Attaches_Bearer`, `BearerAuthHandlerTests.On401_SingleFlightRefresh_RetriesOnce`,
   `BearerAuthHandlerTests.RefreshSessionExpired_SurfacesUnauthorized_NoLoop`,
   `OnlineAuthClientTests.Login200_PersistsSession_AndSealsEntitlement`,
   `OnlineAuthClientTests.ServerUnreachable_UsesCredentialMirror_WhenWindowsValid`,
   `OnlineAuthClientTests.DeviceRevoked_WipesOfflineAuthority`,
   `SealedEntitlementStoreTests.Write_IsForwardOnly_IgnoresLowerHighWaterOrSequence`,
   `DeviceFingerprintSourceTests.RawComponentsNeverLeave_OnlyHashSent`,
   `OnlineIdentityDiTests.Configured_ReplacesAllowAllGate`, `OnlineIdentityDiTests.Unconfigured_KeepsAllowAllGate`.
3. `grep -rn "Npgsql\|Animora.Server\b\|Animora.Persistence.PostgreSql" src/Animora.Client.Services src/Animora.Sync.Custom` returns
   nothing; the only `Animora.Server.*` reference in the client tree is `Animora.Server.Licensing.Verification`
   (`tests/Animora.Domain.Tests` `LayeringRulesTests` still green with exactly that whitelist).
4. `grep -rn "SealedEntitlementMerge.Merge\|HighWaterClock" src/Animora.Client.Services src/Animora.Infrastructure.Local` ≥ 1
   (forward-only high-water honoured); `grep -rn "ServerEndpointOptions.IsConfigured\|Replace(ServiceDescriptor" src/Animora.Sync.Custom src/Animora.Client.Services` ≥ 1 (conditional replace proven).
5. The new migration is the **next free client ordinal** (`ls src/Animora.Persistence.Sqlite/Migrations | tail` shows it last); both
   new tables are `[LocalOnly]` (a `LocalOnlyTablesTest` asserts neither appears in the sync manifest / has no `Version`/`Deleted`).
6. The B04 capability-snapshot tests and the `AllowAllEntitlementGate` contract test still pass (offline authority + unconfigured
   fallback intact).

## PROGRESS.md update

At the end of this phase: set the `G06` row in "Phase status" to `✅ Done` (add date + one-line summary), advance
**"Next phase" → `G07`**, append one `### G06` entry to "Repo state log" (online auth client + bearer handler + real
`OnlineEntitlementGate` + device lifecycle + sealed cache + credential-mirror, the client migration ordinal, any ErrorCode added,
test classes), log any deviation in the "Decision & deviation log", then commit with
`scripts/git/push.sh -b genspark_ai_developer "feat(sync): G06 online auth, entitlement & device lifecycle"`.
