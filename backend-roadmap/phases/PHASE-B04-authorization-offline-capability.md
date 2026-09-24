# Phase B04 — Authorization & offline capability (index only — split into 3 sessions)

**Band:** M · **Track:** B · **Depends on:** B03 · **Satisfies:** R-B04, R-B20, R-B21
**Runnable after this phase:** desktop offline yes · server required no

Do not implement from this file. Execute one sub-phase per session, in order; each file is self-contained:

| Session | File | Scope |
|---|---|---|
| B04a | `PHASE-B04a-authorization-evaluator-gate.md` | `IPermissionEvaluator` (RBAC → policy → resource), the 5 named `AuthorizationPolicies`, `IResourceAuthorizationRule<T>` + 2 rules, `RequiresPermissionAttribute` (resolved at registration), `PermissionAuthorizationGate` (replaces the allow-all), `ErrorCodes` += `PERMISSION_UNKNOWN` |
| B04b | `PHASE-B04b-capability-snapshot-offline.md` | signed device-bound `DeviceCapabilitySnapshot` + migration `006_CapabilitySnapshot`, `CapabilitySnapshotSigner` (HMAC, Infrastructure), fail-closed `CapabilityEvaluator` (`max(now, HighWater)`), `ICapabilityStore`/`CapabilityStore`, `CapabilitySnapshotIssuer` + sign-in integration, `ErrorCodes` += 2 |
| B04c | `PHASE-B04c-capability-gate-shell-tests.md` | `Animora.Contracts.ICapabilityGate` + `NavItem.RequiredPermission`, `CapabilityGateService` (bounded cache), `NavPermissions`, `SidebarViewModel`/`TopbarViewModel` gating (no `.axaml`), gate DI, all B04 tests + build/test/run gate |

B04 is ✅ only when B04c is done. The split is equal-weight by design (evaluator/gate → offline snapshot
→ shell seam/tests); quality and acceptance rigor are unchanged — every original B04 acceptance criterion
is preserved and assigned to a session (all tests + the runnable gate land in B04c).

## Goal (whole phase)
Turn the permission catalog into a working authorization plane: a real `IPermissionEvaluator` (RBAC +
policy + resource), a real `IAuthorizationGate` wired into the B01 chain, a signed device-bound
capability snapshot with per-capability offline expiry that fails closed, and an `ICapabilityGate` seam
the shell can consume for screen/action gating without touching any `.axaml`.

## Out of scope (whole phase — deferred to the named phases)
- Licensing/entitlement (`IEntitlementGate` stays allow-all) → E07, D11.
- Server-side authorization, operator scope, tenant middleware → D04.
- Role/permission **management UI** → C10 (Profile) using this catalog.
- Server-issued snapshot refresh and device revocation propagation → E07.
- Per-screen action-level gating inside feature ViewModels → each Track C phase applies
  `ICapabilityGate` to its own actions.
