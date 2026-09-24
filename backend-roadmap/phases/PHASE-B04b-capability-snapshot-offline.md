# Phase B04b — Offline capability snapshot: table, signer, evaluator, issuer (session 2/3 of B04)

**Band:** M · **Track:** B · **Depends on:** B04a ✅ · **Next:** B04c · Split of original B04 (B04a→B04c, one session each).
**Satisfies (with B04a+B04c):** R-B04, R-B20, R-B21 · **Runnable after this phase:** desktop offline yes · server required no

---

## ⚡ Zero-exploration contract (read this first)
> Self-contained and pre-verified. **Do not re-open the repo to "understand" it.** Implement
> top-to-bottom from §Implementation steps. Names here are the **real** ones. If a compile error proves
> a snippet diverges, fix the smallest thing and log it in `PROGRESS.md`; do not pre-emptively audit.

**This is session 2 of 3.** B04a landed the evaluator + policies + resource rules + gate. **This session
lands the offline capability layer only**: the signed, device-bound `DeviceCapabilitySnapshot` table
(migration `006_CapabilitySnapshot`), its signer, its fail-closed evaluator, the store, the issuer, and
the sign-in integration that issues a snapshot on success. **No Contracts `ICapabilityGate`, no shell,
no gate DI in the Desktop, no tests** — those are B04c. Do not build ahead of this scope.

### Environment (already true — do not probe)
- `scripts/sandbox/install-prereqs.sh` once; `dotnet-ef 10.0.12` at `.sandbox/tools` (do not
  `dotnet tool install`). Add the migration with the exact command in step 4.
- Build is **only** `scripts/sandbox/build.sh full` → `0 warnings / 0 errors`. **No `test.sh`/`run.sh`
  this session** (tests + smoke gate are B04c). Sandbox RAM ≈ 985 MiB; no parallel/server-GC flags.

### Rules (condensed — do not open `CONVENTIONS.md`)
- **§2 layering:** Application → Domain only; `Persistence.Sqlite`/`Infrastructure.Local` may reference
  Application+Domain; **signing (HMAC) lives in `Infrastructure.Local`, never in Application.**
- **§4 style:** `sealed`/`sealed record`, nullable honored, async-all-the-way, `Result`/`Result<T>`,
  `IClock`/`IMonotonicClock` injected (never wall-clock), Ids via `IdGenerator.NewId()`.
- **§7 security:** capability evaluation fails **closed**. **A-RULE 011:** expiry uses
  `max(now, HighWaterUtc)` so a wound-back OS clock cannot resurrect an expired capability.
- **§11/§12:** build 0/0; `PROGRESS.md` updated; commit on `genspark_ai_developer`.

## Goal (this session)
Give the desktop a tamper-evident, per-capability-expiring, device-bound authorization cache that works
offline and fails closed, issued locally at sign-in. E07 later swaps the issuer/signature for the
server-issued one; the **shape does not change**.

## §Reference pack (verbatim — do not re-read source)

### R1 · A03 value object + offline policy (reuse)
`CapabilitySnapshot` (A03 value object) is the in-domain shape. `OfflineCapabilityPolicy.WindowForCode(code)`
+ `PermissionCodes.RiskOf(code)` (A03) give each permission its offline window:
`Low = 30 days`, `Medium = 7 days`, `High = 24 hours`, `Critical = online-only (0)`. **Reuse these — do
not re-derive a risk/expiry table.** `AbsoluteExpiryUtc` = the max of the per-capability values.
`IMonotonicClock.HighWaterUtc` (A11d) is the monotonic high-water used for the `max(now, …)` rule.

### R2 · Secret store + EF/migration patterns (reuse)
`ISecretStore` (A11a) provides a key by name (created on first run). Local entities are `[LocalOnly]`,
not tenant-filtered where installation-level; `DateTimeOffset` stored as `long` epoch by convention.
Migrations run `001`–`005` today (`005_LocalIdentity` from B03a); **your migration is
`006_CapabilitySnapshot`**. Config pattern + `db.Set<T>()` repository shape per B03a §R3.
`IIdentityRepository`/`IStaffMembershipRepository`/`IRoleDefinitionRepository` from B03; the sign-in
handler + `DefaultRolePermissions` + the membership give the permission set to snapshot.

### R3 · Sign-in handler to extend (from B03b)
`SignInCommandHandler` (`src/Animora.Application/UseCases/Identity/`) succeeds by
`RegisterSuccessfulLogin` + `ISessionService.StartAsync` + `LoginAttemptLog("OK")` inside its unit of
work. **This session adds one step inside the same transaction:** call `CapabilitySnapshotIssuer` to
build + persist the snapshot on success. Do not change the sign-in order otherwise.

### R4 · The evaluator from B04a (do not modify here)
`IPermissionEvaluator` (B04a) is the RBAC/policy/resource evaluator. The **capability** evaluator of
this session is a separate concern (offline validity of a cached grant), consumed by B04c's shell gate;
it does not replace `IPermissionEvaluator`.

## §Design decisions (implement — do not re-derive)
- **Local table** (`[LocalOnly]`, migration `006_CapabilitySnapshot`):
  `DeviceCapabilitySnapshot` — `Id`, `UserId`, `DeviceId`, `TenantId`, `PermissionsJson`,
  `IssuedAtUtc`, `PerCapabilityExpiryJson` (`{ "<permissionCode>": "<utc>" }`), `AbsoluteExpiryUtc`,
  `SnapshotSignature`, `SecurityStamp`, `RevokedAtUtc`. One active snapshot per user+device.
  Configuration under `Configurations/Identity/DeviceCapabilitySnapshotConfiguration.cs` (mapping +
  index on `UserId,DeviceId`).
- **Signing (local, offline-usable)** — `Infrastructure.Local/Security/CapabilitySnapshotSigner.cs`
  (`Sign`, `Verify`): HMAC-SHA256 over the canonical snapshot JSON with key
  `"capability-snapshot-key"` from `ISecretStore` (created on first run). Purpose: tamper detection of
  the local cache, **not** authenticity of a server grant — E07 replaces the signature with the
  server-issued one and the verifier accepts either while `ServerIssued == false`. **Never implement
  signing in Application.**
- **Store** — `Application/Security/ICapabilityStore.cs` (`GetAsync`, `ReplaceAsync`, `RevokeAsync`) +
  `Persistence.Sqlite/Security/CapabilityStore.cs` (round-trip; one active snapshot per user+device).
- **Fail-closed evaluator** — `Application/Security/CapabilityEvaluator.cs`:
  `Evaluate(snapshot, permissionCode, nowUtc, highWaterUtc)` uses `max(nowUtc, highWaterUtc)` as "now"
  (A-RULE 011). Outcomes: expired capability → `OFFLINE_CAPABILITY_EXPIRED` (already exists); an
  unverifiable signature → `CAPABILITY_SNAPSHOT_TAMPERED` and the snapshot is discarded, dropping the
  user to read-only until the next successful contact; a `RevokedAtUtc` value → immediate denial of
  every non-read capability; a missing snapshot → `CAPABILITY_SNAPSHOT_MISSING`.
- **Issuer** — `Application/Security/CapabilitySnapshotIssuer.cs` builds the snapshot locally at sign-in
  from `DefaultRolePermissions` + the membership, computes per-capability expiry via
  `OfflineCapabilityPolicy`, signs via the signer, and persists via `ICapabilityStore.ReplaceAsync`.
  Called from `SignInCommandHandler` **inside the same transaction** (R3). E07 later replaces the
  issuer with the server-refreshed one; the shape does not change.
- **New error codes (this session):** `CAPABILITY_SNAPSHOT_TAMPERED`, `CAPABILITY_SNAPSHOT_MISSING`.
  (`PERMISSION_UNKNOWN` was added in B04a; `OFFLINE_CAPABILITY_EXPIRED` already exists.)

## Files to create / modify
| Path | Action | Contents |
|---|---|---|
| `src/Animora.Application/Security/ICapabilityStore.cs` | create | snapshot store port |
| `src/Animora.Application/Security/CapabilityEvaluator.cs` | create | fail-closed, high-water-aware evaluation |
| `src/Animora.Application/Security/CapabilitySnapshotIssuer.cs` | create | local issuance at sign-in |
| `src/Animora.Infrastructure.Local/Security/CapabilitySnapshotSigner.cs` | create | HMAC sign/verify (key from `ISecretStore`) |
| `src/Animora.Persistence.Sqlite/Entities/DeviceCapabilitySnapshot.cs` | create | local snapshot row |
| `src/Animora.Persistence.Sqlite/Security/CapabilityStore.cs` | create | store implementation |
| `src/Animora.Persistence.Sqlite/Configurations/Identity/DeviceCapabilitySnapshotConfiguration.cs` | create | mapping + index |
| `src/Animora.Persistence.Sqlite/Migrations/*006_CapabilitySnapshot*` | create | via `dotnet-ef` (step 4) + snapshot |
| `src/Animora.Application/UseCases/Identity/SignInCommandHandler.cs` | modify | issue the capability snapshot on success (same tx) |
| `src/Animora.Application/ApplicationServiceCollectionExtensions.cs` | modify | register evaluator/issuer/store |
| `src/Animora.Infrastructure.Local/LocalInfrastructureServiceCollectionExtensions.cs` | modify | register `CapabilitySnapshotSigner` |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 2 codes |
| `backend-roadmap/PROGRESS.md` | modify | mark B04b done, next → B04c, repo-state entry, deviations |

## §Implementation steps
1. Add `DeviceCapabilitySnapshot` + configuration; then `CapabilityStore` + `ICapabilityStore`.
2. Implement `CapabilitySnapshotSigner` (HMAC over canonical JSON, key from `ISecretStore`).
3. Implement `CapabilityEvaluator` with the `max(now, highWater)` rule and the four outcomes.
4. Generate the migration:
   ```bash
   cd /home/user/webapp && .sandbox/tools/dotnet-ef migrations add 006_CapabilitySnapshot \
     --project src/Animora.Persistence.Sqlite --startup-project src/Animora.Persistence.Sqlite \
     --context AnimoraLocalDbContext
   ```
   Verify with `dotnet-ef migrations list … --context AnimoraLocalDbContext` (shows `006`). If the tool
   is absent, run `scripts/sandbox/install-prereqs.sh` once, then retry.
5. Implement `CapabilitySnapshotIssuer` and call it from `SignInCommandHandler` inside the same
   transaction. Register evaluator/issuer/store + signer in DI.
6. Append the 2 error codes.
7. **Gate:** `scripts/sandbox/build.sh full` = 0/0.
8. `PROGRESS.md` update + commit on `genspark_ai_developer`.

## Contracts / schema added
- Local table `DeviceCapabilitySnapshots` (migration `006_CapabilitySnapshot`).
- Application: `ICapabilityStore`, `CapabilityEvaluator`, `CapabilitySnapshotIssuer`.
- Infrastructure: `CapabilitySnapshotSigner`. Persistence: `CapabilityStore`.
- `SignInCommandHandler` issues a snapshot on success. `ErrorCodes` += 2. No mock deleted this session.

## Rules that apply here
- **A-RULE 029 / R-B21** — the snapshot is device-bound, per-capability expiring and fails closed;
  no silent extension; a revoked snapshot denies immediately.
- **A-RULE 011** — expiry evaluated against `max(now, HighWaterUtc)`, defeating clock rollback.
- **CONVENTIONS §7** — signing in Infrastructure only; fail-closed evaluation.

## Out of scope (sibling sessions)
- Evaluator/policies/gate/attribute → **B04a** (already done).
- `ICapabilityGate` (Contracts), `CapabilityGateService` + bounded cache, `NavPermissions`, shell
  ViewModels, gate DI in the Desktop, all tests + `run.sh` gate → **B04c**.

## Acceptance criteria
- `scripts/sandbox/build.sh full` = 0/0.
- `dotnet-ef migrations list … --context AnimoraLocalDbContext` shows `006_CapabilitySnapshot`.
- `CAPABILITY_SNAPSHOT_TAMPERED` and `CAPABILITY_SNAPSHOT_MISSING` are present in `ErrorCodes.cs`.
- `CapabilitySnapshotSigner` lives under `Animora.Infrastructure.Local` (grep confirms no HMAC/signing
  type under `Animora.Application`).
- `SignInCommandHandler` calls the issuer on success (grep for the issuer type in the handler).

## PROGRESS.md update
Mark B04b done; advance "Next phase" to `B04c`; append a "Repo state" entry listing: the capability
snapshot table + migration `006_CapabilitySnapshot`, the signer (Infrastructure) + evaluator + issuer +
store, the `SignInCommandHandler` integration, `ErrorCodes` += 2, and any deviation with its
justification.
