# Phase B04 — Authorization & offline capability

**Band:** M · **Track:** B · **Depends on:** B03 · **Satisfies:** R-B04, R-B20, R-B21
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §4, §7, §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §10.1 (three planes), §10.3 (authorization architecture), §10.4 (offline session),
  §21 (A-RULE 023, A-RULE 029)
- `src/Animora.Domain/Identity/*` (`Permission`, `RoleDefinition`, `StaffMembership`,
  `CapabilitySnapshot`, `PermissionCodes`, `DefaultRolePermissions`, `OfflineCapabilityPolicy`)
- `src/Animora.Application/Security/*`, `src/Animora.Application/Dispatch/Dispatcher.cs`
- `src/Animora.Desktop/Shell/SidebarViewModel.cs`, `src/Animora.Desktop/Shell/TopbarViewModel.cs`,
  `src/Animora.Contracts/Navigation/NavItem.cs`

## Goal
Turn the permission catalog into a working authorization plane: a real `IPermissionEvaluator` (RBAC +
policy + resource), a real `IAuthorizationGate` wired into the B01 chain, a signed device-bound
capability snapshot with per-capability offline expiry that fails closed, and an `ICapabilityGate` seam
the shell can consume for screen/action gating without touching any `.axaml`.

## Design decisions (pre-made — implement, do not re-derive)
- **Permission model (fixed):** a permission code is `<context>.<action>` (e.g. `sales.invoice.post`,
  `patients.pet.edit`, `finance.period.close`, `inventory.stock.adjust`, `identity.user.manage`).
  `PermissionCodes` (A03) is the catalog; B04 **adds no new code** — it consumes them.
  Three evaluation kinds, checked in order by `PermissionEvaluator`:
  1. **RBAC** — does any active `StaffMembership` role grant the code?
  2. **Policy** — a named rule beyond role membership, from `AuthorizationPolicies` (below).
  3. **Resource** — an `IResourceAuthorizationRule<TResource>` for instance-level checks.
  Result is `AuthorizationDecision(bool Allowed, string? DeniedByRule, string PermissionCode)`.
  **Unknown permission code = deny** (fail closed, CONVENTIONS §7).
- `Application/Security/IPermissionEvaluator.cs`:
  `Task<AuthorizationDecision> EvaluateAsync(string permissionCode, AuthorizationSubject subject,
  object? resource, CancellationToken ct)`;
  `AuthorizationSubject(string TenantId, string UserId, IReadOnlySet<string> Permissions,
  StaffRole Role, string DeviceId, bool IsOffline)`.
- `Application/Security/AuthorizationPolicies.cs` — the fixed named-policy table:
  | Policy | Rule |
  |---|---|
  | `OwnRecordOrManager` | the actor is the record's `CreatedByUserId`, or holds `StaffRole.Manager` |
  | `AssignedDoctorOnly` | the actor is the visit's `DoctorUserId` (managers exempt) |
  | `OpenCashSessionOwner` | the actor opened the `CashSession` being modified |
  | `PeriodOpen` | the target `AccountingPeriod` is `Open` |
  | `NotSelfDestructive` | a user may not disable/delete their own account or last manager role |
  Each is a `static Func<AuthorizationSubject, object?, bool>` registered in a
  `FrozenDictionary<string, …>` — no reflection.
- `Application/Security/IResourceAuthorizationRule.cs` —
  `interface IResourceAuthorizationRule<TResource> { string PermissionCode { get; }
  bool IsSatisfied(AuthorizationSubject subject, TResource resource); }`. Registered explicitly per
  resource type by later phases; B04 ships `InvoiceAuthorizationRule` (posted invoices need
  `sales.invoice.reverse`, not `sales.invoice.edit`) and `CashSessionAuthorizationRule` as the two
  reference implementations.
- `Application/Security/RequiresPermissionAttribute.cs` — `[RequiresPermission("sales.invoice.post")]`
  on a command/query record; **read once at registration** by
  `ApplicationRegistrationBuilder` and stored on the `HandlerEntry` as a plain string (no reflection at
  dispatch — PERFORMANCE.md §3). A command with no attribute is treated as
  `identity.session.any` (authenticated-only) — never as "public".
- `Application/Security/PermissionAuthorizationGate.cs` — replaces `AllowAllAuthorizationGate`
  (deleted): reads the entry's permission code, builds the `AuthorizationSubject` from the session,
  calls `IPermissionEvaluator`, and returns `PERMISSION_DENIED` with `Detail = DeniedByRule`.
- **Capability snapshot (offline entitlement of *authorization*, not licensing):**
  - `CapabilitySnapshot` (A03 value object) is materialized locally as `[LocalOnly]
    DeviceCapabilitySnapshot` — `Id`, `UserId`, `DeviceId`, `TenantId`, `PermissionsJson`,
    `IssuedAtUtc`, `PerCapabilityExpiryJson` (`{ "<permissionCode>": "<utc>" }`),
    `AbsoluteExpiryUtc`, `SnapshotSignature`, `SecurityStamp`, `RevokedAtUtc`.
    Migration `006_CapabilitySnapshot`.
  - **Signing (local, offline-usable):** HMAC-SHA256 over the canonical snapshot JSON with a key from
    `ISecretStore` (`"capability-snapshot-key"`, created on first run). Purpose: tamper detection of
    the local cache, **not** authenticity of a server grant — E07 replaces the signature with the
    server-issued one and the verifier accepts either while `ServerIssued == false`.
    Implemented in `Infrastructure.Local/Security/CapabilitySnapshotSigner.cs`
    (`Sign`, `Verify`), never in Application.
  - **Expiry policy (from `OfflineCapabilityPolicy`, A03):** each permission carries a
    `RiskLevel`; offline validity is `Low = 30 days`, `Medium = 7 days`, `High = 24 hours`,
    `Critical = online-only (0)`. `AbsoluteExpiryUtc` = the max of the per-capability values.
  - **Fail-closed evaluation:** `CapabilityEvaluator.Evaluate(snapshot, permissionCode, nowUtc,
    highWaterUtc)` uses `max(nowUtc, IMonotonicClock.HighWaterUtc)` as "now" (A-RULE 011), so a
    wound-back OS clock cannot resurrect an expired capability. An expired capability →
    `OFFLINE_CAPABILITY_EXPIRED`; an unverifiable signature → `CAPABILITY_SNAPSHOT_TAMPERED` and the
    snapshot is discarded, dropping the user to read-only until the next successful contact.
    A `RevokedAtUtc` value → immediate denial of every non-read capability.
  - `Application/Security/ICapabilityStore.cs` (`GetAsync`, `ReplaceAsync`, `RevokeAsync`) +
    `Persistence.Sqlite/Security/CapabilityStore.cs`.
  - **Issuance while offline-only (today):** `CapabilitySnapshotIssuer` builds the snapshot locally at
    sign-in from `DefaultRolePermissions` + the membership. E07 replaces the issuer with the
    server-refreshed one; the shape does not change.
- **UI gating seam (no `.axaml` change):**
  `Animora.Contracts/Identity/ICapabilityGate.cs` (**Contracts addition**):
  `bool Can(string permissionCode)`, `bool CanAny(params string[] codes)`,
  `CapabilityState GetState(string permissionCode)` (`enum CapabilityState { Allowed, Denied,
  ExpiredOffline, RequiresOnline }`), `event EventHandler? CapabilitiesChanged`.
  Implemented by `Client.Services/Identity/CapabilityGateService.cs` over `ICapabilityStore` +
  `IPermissionEvaluator`, with a bounded in-memory cache (key = `userId|deviceId|code|snapshotId`,
  capacity 256, cleared on `CapabilitiesChanged` — CONVENTIONS §8, A-RULE 033).
  **Shell consumption:** `NavItem` gains an optional `string? RequiredPermission` (**additive**);
  `SidebarViewModel` filters its item list through `ICapabilityGate.Can(...)` and re-filters on
  `CapabilitiesChanged`; `TopbarViewModel` exposes a `bool IsReadOnlyMode` bound to the existing
  status area. These are the **only** two ViewModels touched, and no `.axaml` file changes.
- **Per-nav permission map** (fixed, placed in `Animora.Desktop/Shell/NavPermissions.cs`):
  Dashboard `dashboard.view` · Reception `reception.queue.view` · Patients `patients.view` ·
  Visits `visits.view` · Vaccination `vaccination.view` · Appointments `appointments.view` ·
  Boarding `boarding.view` · Inventory `inventory.view` · Purchasing `purchasing.view` ·
  Sale `sales.view` · Cashier `cashier.view` · Cheques `cheques.view` · Finance `finance.view` ·
  History `history.view` · Profile `profile.view`. Every code already exists in `PermissionCodes`.
- **New error codes:** `CAPABILITY_SNAPSHOT_TAMPERED`, `CAPABILITY_SNAPSHOT_MISSING`,
  `PERMISSION_UNKNOWN`. (`PERMISSION_DENIED`, `OFFLINE_CAPABILITY_EXPIRED` already exist.)

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Security/IPermissionEvaluator.cs` | create | evaluator port + `AuthorizationDecision`/`AuthorizationSubject` |
| `src/Animora.Application/Security/PermissionEvaluator.cs` | create | RBAC → policy → resource, in order |
| `src/Animora.Application/Security/AuthorizationPolicies.cs` | create | the 5 named policies (frozen map) |
| `src/Animora.Application/Security/IResourceAuthorizationRule.cs` | create | resource rule port |
| `src/Animora.Application/Security/Rules/InvoiceAuthorizationRule.cs` | create | posted-invoice rule |
| `src/Animora.Application/Security/Rules/CashSessionAuthorizationRule.cs` | create | session-owner rule |
| `src/Animora.Application/Security/RequiresPermissionAttribute.cs` | create | registration-time permission binding |
| `src/Animora.Application/Security/PermissionAuthorizationGate.cs` | create | real gate for the B01 chain |
| `src/Animora.Application/Security/AllowAllAuthorizationGate.cs` | delete | replaced |
| `src/Animora.Application/Security/ICapabilityStore.cs` | create | snapshot store port |
| `src/Animora.Application/Security/CapabilityEvaluator.cs` | create | fail-closed, high-water-aware evaluation |
| `src/Animora.Application/Security/CapabilitySnapshotIssuer.cs` | create | local issuance at sign-in |
| `src/Animora.Application/Dispatch/HandlerRegistry.cs` | modify | store the permission code on the entry |
| `src/Animora.Infrastructure.Local/Security/CapabilitySnapshotSigner.cs` | create | HMAC sign/verify |
| `src/Animora.Persistence.Sqlite/Entities/DeviceCapabilitySnapshot.cs` | create | local snapshot row |
| `src/Animora.Persistence.Sqlite/Security/CapabilityStore.cs` | create | store implementation |
| `src/Animora.Persistence.Sqlite/Configurations/Identity/DeviceCapabilitySnapshotConfiguration.cs` | create | mapping + index |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `006_CapabilitySnapshot` + snapshot |
| `src/Animora.Contracts/Identity/ICapabilityGate.cs` | create | Contracts **addition** (UI gating seam) |
| `src/Animora.Contracts/Navigation/NavItem.cs` | modify | add optional `RequiredPermission` |
| `src/Animora.Client.Services/Identity/CapabilityGateService.cs` | create | gate implementation + bounded cache |
| `src/Animora.Desktop/Shell/NavPermissions.cs` | create | nav → permission map |
| `src/Animora.Desktop/Shell/SidebarViewModel.cs` | modify | filter items via `ICapabilityGate` |
| `src/Animora.Desktop/Shell/TopbarViewModel.cs` | modify | expose `IsReadOnlyMode` |
| `src/Animora.Desktop/App.axaml.cs` | modify | register `ICapabilityGate` + the real gate |
| `src/Animora.Application/UseCases/Identity/SignInCommandHandler.cs` | modify | issue the capability snapshot on success |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 3 codes |
| `tests/Animora.Application.Tests/PermissionEvaluatorTests.cs` | create | RBAC/policy/resource order + unknown-code deny |
| `tests/Animora.Application.Tests/CapabilityExpiryTests.cs` | create | per-risk expiry, clock rollback, tamper, revoke |
| `tests/Animora.Application.Tests/AuthorizationGateTests.cs` | create | denied command never reaches the handler |
| `tests/Animora.Persistence.Tests/CapabilityStoreTests.cs` | create | replace/revoke/read round-trip |

## Implementation steps
1. Add the evaluator port, `AuthorizationSubject`, `AuthorizationDecision`, then
   `AuthorizationPolicies` as a frozen map.
2. Implement `PermissionEvaluator` evaluating RBAC → policy → resource **in that order**, returning at
   the first denial and denying unknown codes.
3. Add `IResourceAuthorizationRule<T>` and the two reference rules.
4. Add `RequiresPermissionAttribute`; read it in `ApplicationRegistrationBuilder` and store the string
   on the `HandlerEntry`. Apply the attribute to the existing B02/B03 commands
   (`SignInCommand` → none/authenticated; `ChangePasswordCommand` → `identity.self.password`).
5. Implement `PermissionAuthorizationGate`, delete `AllowAllAuthorizationGate`, and update the DI
   registration in `AddAnimoraApplication`.
6. Add `DeviceCapabilitySnapshot` + configuration + migration `006_CapabilitySnapshot`, then
   `CapabilityStore`.
7. Implement `CapabilitySnapshotSigner` (HMAC over canonical JSON, key from `ISecretStore`) and
   `CapabilityEvaluator` with the `max(now, highWater)` rule and the three failure outcomes.
8. Implement `CapabilitySnapshotIssuer` and call it from `SignInCommandHandler` inside the same
   transaction.
9. Add `ICapabilityGate` to Contracts, `CapabilityGateService` to Client.Services (bounded cache with
   a tenant/user/device/snapshot-scoped key), register it in `App.axaml.cs`.
10. Add `NavPermissions`, extend `NavItem` additively, and filter in `SidebarViewModel`; expose
    `IsReadOnlyMode` in `TopbarViewModel`. Touch no `.axaml`.
11. Append the 3 error codes; write the four test classes.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`; manually confirm that a
    non-manager role sees a reduced sidebar.

## Contracts / schema added
- `Animora.Contracts` **additions**: `ICapabilityGate`, `CapabilityState`,
  `NavItem.RequiredPermission` (optional member, additive).
- Application: `IPermissionEvaluator`/`PermissionEvaluator`, `AuthorizationSubject`,
  `AuthorizationDecision`, `AuthorizationPolicies`, `IResourceAuthorizationRule<T>` + 2 rules,
  `RequiresPermissionAttribute`, `PermissionAuthorizationGate`, `ICapabilityStore`,
  `CapabilityEvaluator`, `CapabilitySnapshotIssuer`.
- Infrastructure: `CapabilitySnapshotSigner`. Persistence: `DeviceCapabilitySnapshot` +
  `CapabilityStore` + migration `006_CapabilitySnapshot`.
- Client services: `CapabilityGateService`. Desktop: `NavPermissions`.
- DI: `ICapabilityGate` registered; `IAuthorizationGate` now resolves to
  `PermissionAuthorizationGate`. `ErrorCodes` += 3. No mock deleted in this phase.

## Rules that apply here
- **A-RULE 023 / R-B20** — authorization stays its own plane: this phase changes only the
  authorization behavior; authentication (B03) and entitlement (E07) are untouched.
- **A-RULE 029 / R-B21** — the capability snapshot is device-bound, per-capability expiring and
  fails closed; there is no silent extension and a revoked snapshot denies immediately.
- **A-RULE 011** — expiry is evaluated against `max(now, HighWaterUtc)`, defeating clock rollback.
- **A-RULE 033 / CONVENTIONS §8** — the gate cache key includes tenant + user + device + code +
  snapshot id and is bounded to 256 entries.
- **PERFORMANCE.md §3** — the permission code is resolved at registration; dispatch performs a
  dictionary lookup, never attribute reflection.
- **CONVENTIONS §10 item 6** — zero `.axaml` changes; exactly two ViewModels touched, both listed.

## Out of scope
- Licensing/entitlement (`IEntitlementGate` stays allow-all) → E07, D11.
- Server-side authorization, operator scope, tenant middleware → D04.
- Role/permission **management UI** → C10 (Profile) using this catalog.
- Server-issued snapshot refresh and device revocation propagation → E07.
- Per-screen action-level gating inside feature ViewModels → each Track C phase applies
  `ICapabilityGate` to its own actions.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `PermissionEvaluatorTests` green: RBAC allows, a failing policy denies even when RBAC allows, a
  failing resource rule denies even when both pass, an unknown code returns `PERMISSION_UNKNOWN` and
  is denied, and the evaluation order is recorded and asserted.
- `AuthorizationGateTests` green: a command whose `[RequiresPermission]` is not held returns
  `PERMISSION_DENIED` and the handler is never invoked; a command with no attribute still requires an
  active session.
- `CapabilityExpiryTests` green: a `High`-risk capability expires after 24 h; a `Critical` capability
  is denied offline with `RequiresOnline`; rewinding the OS clock by 30 days does not restore an
  expired capability; a mutated `PermissionsJson` yields `CAPABILITY_SNAPSHOT_TAMPERED` and the
  snapshot is discarded; a revoked snapshot denies every write capability.
- `CapabilityStoreTests` green: replace/revoke/read round-trip, one active snapshot per user+device.
- Signing in as a `Receptionist` yields a sidebar without Finance/Cheques/Purchasing; signing in as
  `Manager` yields all items. No `.axaml` file is modified (`git diff --name-only` contains no
  `.axaml`).
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark B04 done; advance "Next phase" to `B05`; append a "Repo state" entry listing: the evaluator +
the 5 named policies + resource-rule seam, `RequiresPermissionAttribute` and its registration-time
resolution, `PermissionAuthorizationGate` (replacing the allow-all), the capability snapshot table +
signer + evaluator + issuer + migration `006_CapabilitySnapshot`, the `Animora.Contracts` additions
(`ICapabilityGate`, `NavItem.RequiredPermission`), `CapabilityGateService`, the two ViewModels touched,
the DI changes, `ErrorCodes` += 3, the 4 test classes, no mock deleted, deviations.
