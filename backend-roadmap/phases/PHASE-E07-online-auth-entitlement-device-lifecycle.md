# Phase E07 — Online auth, entitlement & device lifecycle

**Band:** M · **Track:** E · **Depends on:** E06, D11 · **Satisfies:** R-B04, R-B06, R-B20, R-B21
**Runnable after this phase:** desktop offline yes · server required no (login falls back to the B03
local credential mirror; entitlement falls back to the sealed local cache)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §4, §7 (all), §8, §9, §12
- `backend-roadmap/PROGRESS.md` (A11 entry — `ISecretStore`, `InstallationIdentity`,
  `LocalInstallation`, `MonotonicClock.HighWaterUtc`, `ILocalFileStore`; B01 entry —
  `IEntitlementGate`, `AllowAllEntitlementGate` (**deleted here**), `ENTITLEMENT_REQUIRED`;
  B03 entry — `IAuthenticationService`, `AuthOutcome`, `AuthenticationService`, `IPasswordHasher`,
  `ISessionService`, `SecurityStamp`, `CREDENTIALS_INVALID`; B04 entry — `ICapabilityStore`,
  `DeviceCapabilitySnapshot`, `CapabilityEvaluator`, `CapabilitySnapshotIssuer` (**replaced here**),
  `CapabilitySnapshotSigner`, `OfflineCapabilityPolicy`; B05 entry — `IUserNotificationPort`,
  `AuditWriter`, `LogRedaction`, `NotificationText`; D03 entry — `/api/identity/*`,
  `LoginRequestV1`, `TokenPairV1`, `IdentityMeV1`, `DeviceRegistrationV1`, `DEVICE_REVOKED`,
  `IdentityEndpoints`; D11 entry — `Animora.Server.Licensing/Verification/**`, `LicenseVerifier`,
  `LicenseVerificationContext`, `LicenseVerdict`, `LicenseStatus`, `EntitlementPolicy`,
  `CapabilityGroups`, `LicenseKeyRing`, `LicenseKeySetBundle`, `InstallationBinding`,
  `EntitlementCacheSeal`, `LicenseSigningKey`, `LicenseRequestV1`, `/api/license/*`; E03 entry —
  the named `HttpClient`, `BearerTokenHandler`, `ISyncCredentialProvider`,
  `AnonymousSyncCredentialProvider` (**replaced here**), `SyncOptions`; E04 entry —
  `SyncOrchestrator`, `IConnectivityProbe`; E06 entry — `HttpCommandTransport`, the
  **commands-before-replication** pass order)
- `ARCHITECTURE.md` §10.1, §10.2, §10.4, §13.2 · `STACK.md` §5.3
- `A-RULE 011`, `A-RULE 016`, `A-RULE 023`, `A-RULE 029`, `A-RULE 030`, `T-RULE 012`
- `src/Animora.Client.Services/Identity/AuthenticationService.cs`,
  `src/Animora.Server/Endpoints/IdentityEndpoints.cs`

## Goal
The desktop authenticates against the server, stores its tokens in the OS keystore, refreshes and
rotates them, registers its device and honors revocation, receives a **server-signed** capability
snapshot, and enforces the D11 licence + entitlement matrix client-side — while a machine with no
server still logs in offline against the B03 credential mirror and keeps working until its
capabilities expire. This is the phase where the three security planes become real end to end.

## Design decisions (pre-made — implement, do not re-derive)
- **Remote access is a port in Application, an HTTP adapter in `Animora.Sync.Datasync/Transport/`**
  (the E06 precedent: that folder owns the shared named `HttpClient` and its handlers; it is
  transport, not the sync engine, and touches no Datasync type):
  - `Animora.Application/Ports/IRemoteIdentityApi.cs` — `LoginAsync(LoginRequestV1)`,
    `RefreshAsync(string refreshToken)`, `LogoutAsync()`, `GetMeAsync()`,
    `RegisterDeviceAsync(DeviceRegistrationV1)`, `GetCapabilitiesAsync()`, each
    `Task<Result<T>>` with a `CancellationToken`.
  - `Animora.Application/Ports/IRemoteLicenseApi.cs` — `RequestLicenseAsync(LicenseRequestV1)`,
    `GetKeySetAsync()`.
  - Adapters `HttpIdentityApi`, `HttpLicenseApi`. Offline defaults `OfflineRemoteIdentityApi` /
    `OfflineRemoteLicenseApi` (Application) return `SERVER_UNREACHABLE` and are the registrations
    when `SyncOptions.ServerBaseUrl` is null — so nothing in the client ever *requires* a server.
- **Token storage is the OS keystore, never the DB, never a config file** (CONVENTIONS §7).
  `src/Animora.Client.Services/Identity/TokenStore.cs` over A11's `ISecretStore`, keys
  `animora.auth.access` / `animora.auth.refresh`; value is canonical JSON of
  `sealed record TokenSet(string AccessToken, DateTimeOffset AccessExpiresAtUtc, string RefreshToken, DateTimeOffset RefreshExpiresAtUtc, string UserId)`.
  Both key names are added to B05's `LogRedaction` deny list.
- **`AuthTokenProvider`** (`src/Animora.Client.Services/Identity/AuthTokenProvider.cs`) implements
  `ISyncCredentialProvider`, **replacing `AnonymousSyncCredentialProvider`**:
  returns the cached access token; refreshes **proactively** when
  `now > AccessExpiresAtUtc - 20%` of its lifetime, and **reactively** exactly once on a `401`.
  Refresh is **single-flight** (`SemaphoreSlim(1,1)`): concurrent sync + command passes share one
  refresh round-trip. The refresh token is **rotated** (the response replaces both values
  atomically in the keystore). A failed refresh returns `AUTH_REQUIRED`, clears the access token but
  **keeps** the local session and capability snapshot — the user keeps working offline until those
  expire (A-RULE 029). `effectiveNow = max(IClock.UtcNow, IMonotonicClock.HighWaterUtc)` for every
  expiry comparison (A-RULE 011).
- **`OnlineFirstAuthenticationService`** (`src/Animora.Client.Services/Identity/`) becomes the
  `IAuthenticationService` registration, **wrapping** B03's `AuthenticationService` (kept, not
  deleted — it is the offline path). Order per sign-in:
  ```
  1 IConnectivityProbe.CheckAsync()
  2 Reachable    -> POST /api/identity/login -> TokenPairV1 -> GET /api/identity/me
                    -> mirror credentials + memberships locally -> register device
                    -> refresh capability snapshot -> start local session
  3 Unreachable  -> B03 local sign-in against the mirrored hash
  4 Server says INVALID_CREDENTIALS -> return that; NEVER silently fall back to a stale
    local hash (a server-rejected password must not open the app)
  ```
- **Credential mirroring (the offline-login enabler):** after a **successful server login only**,
  the password is hashed with B03's `IPasswordHasher` into the local `User.PasswordHash` together
  with `SecurityStamp` from `IdentityMeV1`; the plaintext is never stored, logged or re-sent. A
  server `SecurityStamp` differing from the stored one **discards** the snapshot and mirrored hash
  first (a password change elsewhere invalidates offline access — `ARCHITECTURE.md` §10.4). A user
  who never logged in online on this device gets `OFFLINE_LOGIN_UNAVAILABLE`.
- **Server-signed capability snapshot — one new server endpoint, signed by the D11 key ring.**
  `GET /api/identity/capabilities` (`src/Animora.Server/Endpoints/CapabilityEndpoints.cs`,
  authenticated, tenant-resolved) returns
  `CapabilitySnapshotV1(string UserId, string TenantId, string DeviceId, IReadOnlyList<string> PermissionCodes, IReadOnlyDictionary<string,DateTimeOffset> PerCapabilityExpiryUtc, DateTimeOffset AbsoluteExpiryUtc, string SecurityStamp, DateTimeOffset IssuedAtUtc, string KeyId, string Signature)`
  (`src/Animora.Contracts.Wire/Identity/CapabilitySnapshotV1.cs`). Expiries come from B04's
  `OfflineCapabilityPolicy` risk bands, computed **server-side** so a client cannot lengthen them.
  Signing uses D11's **existing** ECDSA P-256 primary `LicenseSigningKey` through
  `src/Animora.Server/Identity/ServerCapabilitySigner.cs` over the canonical JSON of every field
  except `Signature` — no second crypto scheme, and the client already holds the public key set.
  The client verifies with `CapabilitySnapshotSigner.VerifyServerSignature(snapshot, LicenseKeyRing)`
  (B04's signer gains that one method); a bad signature ⇒ `CAPABILITY_SIGNATURE_INVALID`, snapshot
  discarded, user read-only. `ServerCapabilitySnapshotIssuer` **replaces** B04's local
  `CapabilitySnapshotIssuer` as the `ICapabilityStore` filler when online; the local issuer stays the
  first-run/offline fallback with `ServerIssued = false`.
- **Device lifecycle (`src/Animora.Client.Services/Identity/DeviceLifecycleService.cs`):**
  registration runs on the first successful online login and after every reinstall, sending
  `DeviceRegistrationV1(DeviceId = LocalInstallation.InstallationId, DeviceName, Platform, AppVersion, InstallationFingerprint = InstallationIdentity.MachineFingerprintHash)`.
  **Revocation handling is global, not per-call:** any `DEVICE_REVOKED` response (login, refresh,
  sync, command transport, licence request) invokes `HandleRevokedAsync` exactly once — purge both
  tokens, `ICapabilityStore.RevokeAsync`, local `Device.Status = Revoked`, seal the entitlement
  cache as `Invalid`, `ISessionService.EndAsync("device-revoked")`, audit `audit.device.revoked`,
  and one `NeedsAttention` notification. Local data stays readable; every write and command is
  refused; only an operator action (D10) restores it — the client never self-reactivates
  (A-RULE 029).
- **Licence acquisition & offline verification (`src/Animora.Client.Services/Licensing/`):**
  `LicenseService.EnsureAsync()` loads `license.animora` (artifact) and `entitlement.cache` (sealed
  verdict) from A11's `ILocalFileStore` and verifies with D11's `LicenseVerifier` using
  `LicenseVerificationContext(ExpectedProduct: "animora.clinic", ExpectedAudience: "desktop", ExpectedTenantId: LocalInstallation.TenantId, InstallationBindingHash: InstallationBinding.Compute(installationId, machineFingerprintHash), WallClockUtc, MonotonicHighWaterUtc, KeyRing)`.
  The key ring loads from the shipped `keyset.animora`, replaced when a newer signed
  `LicenseKeySetBundle` arrives with a licence response (chain verified first). Refresh: on a
  successful online contact when the verdict is `Expiring`/`Expired`/`Invalid`, at most **once per
  hour** (`LicenseRefreshInterval`) via `POST /api/license/request`; a failed request never degrades
  a currently `Valid` verdict. The verdict is re-sealed with `EntitlementCacheSeal` (HMAC-SHA256,
  key from `ISecretStore`); a broken seal ⇒ `Invalid`, never "assume valid".
  `Animora.Client.Services` references **only** `Animora.Server.Licensing/Verification/`;
  `LayeringRulesTests` gains a fact that no client project touches `Issuance/`.
- **Real entitlement plane (`src/Animora.Client.Services/Licensing/LicenseEntitlementGate.cs`)**
  implements B01's `IEntitlementGate` and **deletes `AllowAllEntitlementGate`**. It maps the
  dispatched type to a `CapabilityGroups` value through
  `src/Animora.Application/Security/CommandCapabilityGroups.cs` — a `FrozenDictionary<string,string>`
  keyed by `CommandTypes`/query name: `Invoice`/`Payment`/`Cheque`/`Expense`/`JournalEntry`/
  `CashSession`/`Period` ⇒ `Money`; clinical/scheduling/inventory writes ⇒ `ClinicalWrite`; queries
  ⇒ `ClinicalRead`; sync + command transport ⇒ `Sync`; identity/settings ⇒ `Admin`. **An unmapped
  command type ⇒ `Money`** (strictest group) — fail closed. The decision itself is D11's
  `EntitlementPolicy.Evaluate(status, group)`; E07 adds no policy. Denial returns
  `ENTITLEMENT_REQUIRED` with the verdict code in `Detail`.
- **Reconnect recovery order (`ARCHITECTURE.md` §10.4), inserted ahead of E06's order:**
  `IOnlineSessionCoordinator.EnsureSessionAsync()` is **step 0** of every `SyncOrchestrator` pass —
  (1) ensure/refresh token, (2) refresh the capability snapshot when older than
  `CapabilityRefreshInterval = 15 min` or the security stamp changed, (3) ensure the licence — then
  E06's command submission, then replication. `AUTH_REQUIRED`/`SERVER_UNREACHABLE` at step 0 reports
  `Offline` and **nothing else runs**: commands are never sent unauthenticated.
- **Entitlement & expiry UX** (`src/Animora.Client.Services/Licensing/EntitlementNotices.cs`), all
  through B05's `IUserNotificationPort`, always with the action code `contact-admin`, never a bare
  failure: `Expiring` (≤ 10 days) ⇒ `Warning`, once per day · `Expired` ⇒ `NeedsAttention`, on start
  and on the first denied write · `Invalid` (signature/binding/tamper) ⇒ `NeedsAttention`, on start ·
  `DEVICE_REVOKED` ⇒ `NeedsAttention`, immediately. Persian strings stay in the Desktop resource
  layer (`NotificationText`, B05) — this project emits keys only.
- **Options** (`SyncOptions` extension, E03's file): `OnlineLoginEnabled = true`,
  `TokenRefreshSkewPercent = 20`, `CapabilityRefreshIntervalMinutes = 15`,
  `LicenseRefreshIntervalMinutes = 60`, `DeviceName` (defaults to the machine name).
- **Zero-Internet (A-RULE 016):** every call targets `SyncOptions.ServerBaseUrl` only. No external
  host, no revocation service, no time service. `NoExternalHostTests` scans the adapters for any
  absolute URI literal.
- **New error codes:** `SERVER_UNREACHABLE`, `TOKEN_REFRESH_FAILED`, `OFFLINE_LOGIN_UNAVAILABLE`,
  `CAPABILITY_SIGNATURE_INVALID`, `LICENSE_REQUEST_FAILED`, `SECURITY_STAMP_CHANGED`
  (`AUTH_REQUIRED`, `DEVICE_REVOKED`, `ENTITLEMENT_REQUIRED`, `CREDENTIALS_INVALID` exist — reuse).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Ports/IRemoteIdentityApi.cs` | create | identity port + offline default |
| `src/Animora.Application/Ports/IRemoteLicenseApi.cs` | create | licence port + offline default |
| `src/Animora.Application/Security/CommandCapabilityGroups.cs` | create | command → capability group, unmapped ⇒ `Money` |
| `src/Animora.Application/Security/AllowAllEntitlementGate.cs` | delete | replaced by the real gate |
| `src/Animora.Contracts.Wire/Identity/CapabilitySnapshotV1.cs` | create | signed snapshot wire record |
| `src/Animora.Server/Identity/ServerCapabilitySigner.cs` | create | ECDSA signing over the D11 primary key |
| `src/Animora.Server/Endpoints/CapabilityEndpoints.cs` | create | `GET /api/identity/capabilities` |
| `src/Animora.Sync.Datasync/Transport/HttpIdentityApi.cs` | create | `/api/identity/*` adapter |
| `src/Animora.Sync.Datasync/Transport/HttpLicenseApi.cs` | create | `/api/license/*` adapter |
| `src/Animora.Sync.Datasync/SyncOptions.cs` | modify | 5 new options + `Validate()` |
| `src/Animora.Client.Services/Identity/TokenStore.cs` | create | keystore-backed `TokenSet` |
| `src/Animora.Client.Services/Identity/AuthTokenProvider.cs` | create | `ISyncCredentialProvider` + single-flight refresh |
| `src/Animora.Client.Services/Identity/OnlineFirstAuthenticationService.cs` | create | online-then-offline sign-in + mirroring |
| `src/Animora.Client.Services/Identity/DeviceLifecycleService.cs` | create | registration + global revocation handling |
| `src/Animora.Client.Services/Identity/ServerCapabilitySnapshotIssuer.cs` | create | verified server snapshot → `ICapabilityStore` |
| `src/Animora.Client.Services/Identity/OnlineSessionCoordinator.cs` | create | token → capability → licence, pass step 0 |
| `src/Animora.Client.Services/Licensing/LicenseService.cs` | create | load/verify/refresh/seal the licence |
| `src/Animora.Client.Services/Licensing/LicenseEntitlementGate.cs` | create | real `IEntitlementGate` |
| `src/Animora.Client.Services/Licensing/EntitlementNotices.cs` | create | the 4-row UX table |
| `src/Animora.Client.Services/Sync/SyncOrchestrator.cs` | modify | step 0 session ensure, before commands |
| `src/Animora.Infrastructure.Local/Security/CapabilitySnapshotSigner.cs` | modify | `VerifyServerSignature` over `LicenseKeyRing` |
| `src/Animora.Persistence.Sqlite/Migrations/021_OnlineIdentity*` | create | snapshot `ServerIssued`/`SigningKeyId`, `Device.Status` index |
| `src/Animora.Desktop/App.axaml.cs` | modify | 6 DI swaps (auth, credentials, entitlement, issuer, remote APIs, licence) |
| `tests/Animora.Application.Tests/EntitlementGateTests.cs` | create | the D11 matrix + unmapped ⇒ `Money` |
| `tests/Animora.Application.Tests/OnlineFirstAuthTests.cs` | create | 4 sign-in paths + no-fallback-on-rejection |
| `tests/Animora.Sync.Tests/TokenRefreshTests.cs` | create | proactive/reactive/single-flight/rotation |
| `tests/Animora.Sync.Tests/SessionBeforeCommandsTests.cs` | create | pass order + skip on `AUTH_REQUIRED` |
| `tests/Animora.Sync.Tests/DeviceRevocationTests.cs` | create | revocation from any surface, once, fail closed |
| `tests/Animora.Sync.Tests/NoExternalHostTests.cs` | create | no absolute URI literal in the adapters |
| `tests/Animora.Server.Tests/CapabilityEndpointTests.cs` | create | signature, expiries, tenant scoping |

## Implementation steps
1. Add the two Application ports + their offline defaults and `CommandCapabilityGroups`.
2. Add `CapabilitySnapshotV1`, `ServerCapabilitySigner` and `CapabilityEndpoints`; map them in
   `Program.cs` beside `IdentityEndpoints`; expiries from `OfflineCapabilityPolicy`, server-computed.
3. Write `HttpIdentityApi` / `HttpLicenseApi` on E03's named client; extend `SyncOptions`.
4. Write `TokenStore` + `AuthTokenProvider` (skew, single-flight, rotation, `401`-once).
5. Write `DeviceLifecycleService` with the single global `HandleRevokedAsync` path.
6. Write `ServerCapabilitySnapshotIssuer` and add `VerifyServerSignature` to the B04 signer.
7. Write `LicenseService` (load → verify → refresh → re-seal) and `LicenseEntitlementGate`; delete
   `AllowAllEntitlementGate`.
8. Write `OnlineFirstAuthenticationService` with the 4-path order and credential mirroring,
   including the security-stamp discard rule.
9. Write `OnlineSessionCoordinator` and insert it as step 0 of `SyncOrchestrator`, **preserving
   E06's commands-before-replication order**.
10. Add migration `021_OnlineIdentity`; swap the six DI registrations in `App.axaml.cs`.
11. Write `EntitlementNotices` and wire it to `IUserNotificationPort`.
12. Write the seven test classes.
13. Verify against a running server: online login issues tokens + a signed snapshot; pulling the
    network keeps the app working until the snapshot expires, then it goes read-only; an
    operator-revoked device is locked at next contact; a licence copied to a second installation
    fails with `LICENSE_INSTALLATION_MISMATCH`; winding the OS clock back does not restore an
    expired licence or capability.
14. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; with no `ServerBaseUrl`, `run.sh` = `SMOKE RUN OK`
    and offline login works from the mirror.

## Contracts / schema added
- Application: `IRemoteIdentityApi`, `IRemoteLicenseApi`, `OfflineRemoteIdentityApi`,
  `OfflineRemoteLicenseApi`, `CommandCapabilityGroups`; `AllowAllEntitlementGate` **deleted**.
- Wire: `CapabilitySnapshotV1`. `WireContract.Version` stays `1` (new shape, not a changed one).
- Server: `ServerCapabilitySigner`, `CapabilityEndpoints` (1 route).
- Sync.Datasync: `HttpIdentityApi`, `HttpLicenseApi`; `SyncOptions` += 5 options.
- Client services: `TokenStore`, `TokenSet`, `AuthTokenProvider`,
  `OnlineFirstAuthenticationService`, `DeviceLifecycleService`, `ServerCapabilitySnapshotIssuer`,
  `OnlineSessionCoordinator`, `LicenseService`, `LicenseEntitlementGate`, `EntitlementNotices`.
- Infrastructure: `CapabilitySnapshotSigner.VerifyServerSignature`.
- Persistence: migration `021_OnlineIdentity` (`DeviceCapabilitySnapshot.ServerIssued`,
  `.SigningKeyId`, `IX_Devices_Status`).
- DI in `App.axaml.cs`: `IAuthenticationService` → `OnlineFirstAuthenticationService`,
  `ISyncCredentialProvider` → `AuthTokenProvider`, `IEntitlementGate` → `LicenseEntitlementGate`,
  `ICapabilityStore` filler → `ServerCapabilitySnapshotIssuer`, the two remote APIs (HTTP or offline
  default by `ServerBaseUrl`), `LicenseService` hosted-start.
- `ErrorCodes` += 6. No `Animora.Contracts` change, no `CommandTypes` entry, no mock deleted (none
  remain after C11).

## Rules that apply here
- **A-RULE 023 / R-B20** — authentication (token), authorization (capability snapshot) and
  entitlement (licence) are three separate objects evaluated in that order; no code path merges them.
- **A-RULE 029 / R-B21** — offline continuity comes from the signed, device-bound snapshot with
  per-capability expiry, not from the refresh token; a revoked device loses it at next contact.
- **A-RULE 030 / R-B06** — verification always includes the installation binding; a copied licence
  fails on another machine.
- **A-RULE 011** — every expiry comparison uses `max(wall clock, monotonic high water)`.
- **A-RULE 016 / R-B27** — no external host is contacted; the app is fully usable with no server.
- **CONVENTIONS §7** — tokens and licence material live in the OS keystore / sealed files, never in
  the DB, config or logs; unverifiable material fails closed.
- **CONVENTIONS §4** — no `async void`, single-flight refresh via `SemaphoreSlim`, `Result<T>` for
  expected failures.

## Out of scope
- Server-side licence issuance, key rotation and the entitlement middleware → already D11.
- Operator-driven device reactivation and seat assignment → already D10.
- Recording gates R (offline session), S (key survival) and U (anti-cloning) as results → E08.
- Passkeys / external IdP → deferred (ROADMAP §5).
- Any `.axaml` change or new screen: `LoginViewModel` already talks to `IAuthenticationService`
  (B03), so only the DI registration changes.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `OnlineFirstAuthTests` green: online success mirrors the hash + stamp; offline success uses the
  mirror; a server `INVALID_CREDENTIALS` never falls back to the local hash; a changed
  `SecurityStamp` discards the snapshot and the mirror before proceeding; a user with no mirror gets
  `OFFLINE_LOGIN_UNAVAILABLE`.
- `TokenRefreshTests` green: proactive refresh at the 20% skew; exactly one reactive refresh per
  `401`; ten concurrent callers cause one refresh round-trip; the refresh token is rotated; a failed
  refresh yields `AUTH_REQUIRED` and leaves the local session and snapshot intact.
- `SessionBeforeCommandsTests` green: every pass runs session-ensure → commands → push → pull;
  `AUTH_REQUIRED`/`SERVER_UNREACHABLE` at step 0 skips all later steps and reports `Offline`.
- `DeviceRevocationTests` green: `DEVICE_REVOKED` from login, refresh, sync or command transport all
  converge on one `HandleRevokedAsync` execution; tokens purged, snapshot revoked, session ended,
  writes refused, reads still work, audit entry written, no self-reactivation.
- `EntitlementGateTests` green: the D11 matrix holds for all four verdicts × five capability groups;
  an unmapped command type is treated as `Money`; a broken `entitlement.cache` seal ⇒ `Invalid`.
- `CapabilityEndpointTests` green: the snapshot is signed by the primary `LicenseSigningKey`,
  verifies against the published key set, carries per-capability expiries from
  `OfflineCapabilityPolicy`, and never contains another tenant's memberships.
- `NoExternalHostTests` green: no absolute URI literal outside `SyncOptions.ServerBaseUrl` usage.
- Manual: a licence copied to a second installation is rejected; the OS clock wound back one year
  neither extends the licence nor an expired capability.
- Runnability: with no `ServerBaseUrl`, `run.sh` = `SMOKE RUN OK`, offline login works, and the
  entitlement gate uses the sealed local cache.

## PROGRESS.md update
Mark E07 done; advance "Next phase" to `E08`; append a "Repo state" entry listing: the two remote
ports + their HTTP/offline pairs, `TokenStore` + keystore key names (**redacted in logs**),
`AuthTokenProvider` (**replacing `AnonymousSyncCredentialProvider`**) with its skew/single-flight/
rotation rules, `OnlineFirstAuthenticationService` + the credential-mirroring and security-stamp
rules, `CapabilitySnapshotV1` + `ServerCapabilitySigner` + the new endpoint (**signed by D11's key
ring**), `ServerCapabilitySnapshotIssuer`, `DeviceLifecycleService.HandleRevokedAsync` as the single
revocation path, `LicenseService` + `LicenseEntitlementGate` (**`AllowAllEntitlementGate` deleted**),
`CommandCapabilityGroups` + the unmapped ⇒ `Money` rule, the **session-before-commands** pass order
(**E08 must preserve it**), migration `021_OnlineIdentity`, the 5 new `SyncOptions`, the 6 new error
codes, the 6 DI swaps, deviations.
