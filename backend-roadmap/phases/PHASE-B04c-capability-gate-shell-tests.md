# Phase B04c — Capability gate seam, shell gating & tests (session 3/3 of B04)

**Band:** M · **Track:** B · **Depends on:** B04b ✅ · **Next:** B05a · Split of original B04 (B04a→B04c, one session each).
**Satisfies (completes with B04a+B04b):** R-B04, R-B20, R-B21 · **Runnable after this phase:** desktop offline yes · server required no

---

## ⚡ Zero-exploration contract (read this first)
> Self-contained and pre-verified. **Do not re-open the repo to "understand" it.** Implement
> top-to-bottom from §Implementation steps. Names here are the **real** ones. If a compile error proves
> a snippet diverges, fix the smallest thing and log it in `PROGRESS.md`; do not pre-emptively audit.

**This is session 3 of 3 — the B04 close-out.** B04a landed the evaluator/policies/gate; B04b landed the
signed offline capability snapshot + issuer. **This session lands the UI gating seam and proves the whole
thing**: the `Animora.Contracts.ICapabilityGate`, its `Client.Services` implementation with a bounded
cache, the nav→permission map, the two shell ViewModels, the Desktop DI, and **all B04 tests** plus the
runnable gate. B04 is ✅ only when this session's gate is green.

### Environment (already true — do not probe)
- `scripts/sandbox/install-prereqs.sh` once; `dotnet-ef 10.0.12` at `.sandbox/tools` (do not
  `dotnet tool install`). **No new migration this session.**
- Build/test/run are **only** these scripts: `scripts/sandbox/build.sh full` → `0 warnings / 0 errors`;
  `scripts/sandbox/test.sh` → `TESTS OK`; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
- Sandbox RAM ≈ 985 MiB; no parallel/server-GC flags.

### Rules (condensed — do not open `CONVENTIONS.md`)
- **§2 layering:** `Animora.Contracts` references no `Animora.*`; `Animora.Client.Services` references
  Application+Contracts+Persistence+Infrastructure.
- **§4 style:** `sealed`/`sealed record`; nullable honored; **`Client.Services` methods returning to a
  ViewModel do NOT `ConfigureAwait(false)`**.
- **§8 / A-RULE 033:** the gate cache is **bounded** (256 entries), key = `userId|deviceId|code|snapshotId`,
  cleared on `CapabilitiesChanged`.
- **§9 testing:** add tests for what this introduces; no test asserts a Persian UI string; no wall-clock
  (inject `IClock`).
- **§10 item 6:** **zero `.axaml` changes**; exactly two ViewModels touched (`SidebarViewModel`,
  `TopbarViewModel`).
- **§11/§12:** build 0/0, tests green, run OK; `PROGRESS.md` updated (mark **B04** done overall); commit
  on `genspark_ai_developer`.

## Goal (this session)
Expose the offline authorization decision to the UI through a clean Contracts seam, filter the sidebar
and show a read-only banner without touching any `.axaml`, and author every B04 test so the full
acceptance battery is green.

## §Reference pack (verbatim — do not re-read source)

### R1 · B04a/B04b pieces to consume
`IPermissionEvaluator` (B04a) — RBAC/policy/resource. `ICapabilityStore` (B04b, `GetAsync`/
`ReplaceAsync`/`RevokeAsync`), `CapabilityEvaluator` (B04b, fail-closed, `max(now, highWater)`),
`DeviceCapabilitySnapshot` (B04b table). The snapshot id + `UserId`/`DeviceId` form the cache key.
Error codes available: `PERMISSION_DENIED`, `PERMISSION_UNKNOWN`, `OFFLINE_CAPABILITY_EXPIRED`,
`CAPABILITY_SNAPSHOT_TAMPERED`, `CAPABILITY_SNAPSHOT_MISSING`. No new error code this session.

### R2 · Shell reality (the only two ViewModels touched)
`src/Animora.Desktop/Shell/SidebarViewModel.cs` builds a nav item list;
`src/Animora.Desktop/Shell/TopbarViewModel.cs` owns a status area;
`src/Animora.Contracts/Navigation/NavItem.cs` is the nav DTO (extend **additively**). Every nav code in
the map below already exists in `PermissionCodes` (A03); if any specific `*.view` code does not exist,
map that nav item to its closest existing `*Read`/`*Manage` code and **record the mapping in
`PROGRESS.md`** (never rename a shipped code; a genuinely new code is an addition to `PermissionCodes.All`,
not a rename — but prefer reuse). `App.axaml.cs` `ConfigureServices()` is the DI builder (extended by B03).

## §Design decisions (implement — do not re-derive)
- **UI gating seam (no `.axaml` change):** `Animora.Contracts/Identity/ICapabilityGate.cs`
  (**Contracts addition**):
  ```csharp
  public enum CapabilityState { Allowed, Denied, ExpiredOffline, RequiresOnline }
  public interface ICapabilityGate {
      bool Can(string permissionCode);
      bool CanAny(params string[] codes);
      CapabilityState GetState(string permissionCode);
      event EventHandler? CapabilitiesChanged; }
  ```
  Implemented by `Client.Services/Identity/CapabilityGateService.cs` over `ICapabilityStore` +
  `IPermissionEvaluator`, with a **bounded in-memory cache** (key = `userId|deviceId|code|snapshotId`,
  capacity 256, cleared on `CapabilitiesChanged` — CONVENTIONS §8, A-RULE 033).
- **Shell consumption:** `NavItem` gains an optional `string? RequiredPermission` (**additive**);
  `SidebarViewModel` filters its item list through `ICapabilityGate.Can(...)` and re-filters on
  `CapabilitiesChanged`; `TopbarViewModel` exposes a `bool IsReadOnlyMode` bound to the existing status
  area. These are the **only** two ViewModels touched, and **no `.axaml` file changes.**
- **Per-nav permission map** (fixed, `Animora.Desktop/Shell/NavPermissions.cs`):
  Dashboard `dashboard.view` · Reception `reception.queue.view` · Patients `patients.view` ·
  Visits `visits.view` · Vaccination `vaccination.view` · Appointments `appointments.view` ·
  Boarding `boarding.view` · Inventory `inventory.view` · Purchasing `purchasing.view` ·
  Sale `sales.view` · Cashier `cashier.view` · Cheques `cheques.view` · Finance `finance.view` ·
  History `history.view` · Profile `profile.view`. (Use the real `PermissionCodes` constants; where a
  `*.view` name does not exist, map to the closest existing `*Read`/`*Manage` and record it.)
- **DI:** register `ICapabilityGate → CapabilityGateService` in `App.axaml.cs`.
- **Tests:**
  - `tests/Animora.Application.Tests/PermissionEvaluatorTests.cs` — RBAC allows; a failing policy denies
    even when RBAC allows; a failing resource rule denies even when both pass; an unknown code →
    `PERMISSION_UNKNOWN` and is denied; the evaluation order is recorded and asserted.
  - `tests/Animora.Application.Tests/CapabilityExpiryTests.cs` — per-risk expiry (`High` 24 h,
    `Critical` online-only), clock rollback does not restore an expired capability, tamper →
    `CAPABILITY_SNAPSHOT_TAMPERED` and discard, revoke denies every write capability.
  - `tests/Animora.Application.Tests/AuthorizationGateTests.cs` — a command whose `[RequiresPermission]`
    is not held returns `PERMISSION_DENIED` and the handler is never invoked; a no-attribute command
    still requires an active session.
  - `tests/Animora.Persistence.Tests/CapabilityStoreTests.cs` — replace/revoke/read round-trip; one
    active snapshot per user+device.

## Files to create / modify
| Path | Action | Contents |
|---|---|---|
| `src/Animora.Contracts/Identity/ICapabilityGate.cs` | create | Contracts **addition** (UI gating seam) |
| `src/Animora.Contracts/Navigation/NavItem.cs` | modify | add optional `RequiredPermission` |
| `src/Animora.Client.Services/Identity/CapabilityGateService.cs` | create | gate implementation + bounded cache |
| `src/Animora.Desktop/Shell/NavPermissions.cs` | create | nav → permission map |
| `src/Animora.Desktop/Shell/SidebarViewModel.cs` | modify | filter items via `ICapabilityGate` |
| `src/Animora.Desktop/Shell/TopbarViewModel.cs` | modify | expose `IsReadOnlyMode` |
| `src/Animora.Desktop/App.axaml.cs` | modify | register `ICapabilityGate` |
| `tests/Animora.Application.Tests/PermissionEvaluatorTests.cs` | create | RBAC/policy/resource order + unknown-code deny |
| `tests/Animora.Application.Tests/CapabilityExpiryTests.cs` | create | per-risk expiry, clock rollback, tamper, revoke |
| `tests/Animora.Application.Tests/AuthorizationGateTests.cs` | create | denied command never reaches the handler |
| `tests/Animora.Persistence.Tests/CapabilityStoreTests.cs` | create | replace/revoke/read round-trip |
| `backend-roadmap/PROGRESS.md` | modify | mark B04c **and B04** done, next → B05 (B05a), repo-state entry, deviations |

## §Implementation steps
1. Add `ICapabilityGate`/`CapabilityState` to Contracts; extend `NavItem` additively.
2. Implement `CapabilityGateService` over `ICapabilityStore` + `IPermissionEvaluator` with the bounded
   cache (256, tenant/user/device/code/snapshot key, cleared on `CapabilitiesChanged`).
3. Add `NavPermissions`; filter in `SidebarViewModel`; expose `IsReadOnlyMode` in `TopbarViewModel`.
   Touch **no** `.axaml`.
4. Register `ICapabilityGate → CapabilityGateService` in `App.axaml.cs`.
5. Write the four test classes.
6. **Gate:** `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`; manually confirm
   a non-manager role sees a reduced sidebar.
7. `PROGRESS.md` update (mark B04 done overall) + commit on `genspark_ai_developer`.

## Contracts / schema added
- `Animora.Contracts` **additions**: `ICapabilityGate`, `CapabilityState`, `NavItem.RequiredPermission`
  (optional, additive).
- Client services: `CapabilityGateService`. Desktop: `NavPermissions`.
- DI: `ICapabilityGate` registered. No migration, no new error code, no mock deleted this session.

## Rules that apply here
- **A-RULE 033 / CONVENTIONS §8** — the gate cache key includes tenant + user + device + code +
  snapshot id and is bounded to 256 entries.
- **CONVENTIONS §10 item 6** — zero `.axaml` changes; exactly two ViewModels touched.

## Out of scope (sibling sessions / later phases)
- Evaluator/policies/gate/attribute → **B04a**. Capability snapshot table/signer/evaluator/issuer +
  migration `006` + sign-in integration → **B04b**.
- Licensing/entitlement (`IEntitlementGate` stays allow-all) → E07, D11.
- Server-side authorization, operator scope, tenant middleware → D04.
- Role/permission **management UI** → C10. Server-issued snapshot refresh + device revocation → E07.
- Per-screen action-level gating inside feature ViewModels → each Track C phase.

## Acceptance criteria
- `scripts/sandbox/build.sh full` = 0/0.
- `PermissionEvaluatorTests` green: RBAC allows; a failing policy denies even when RBAC allows; a
  failing resource rule denies even when both pass; an unknown code → `PERMISSION_UNKNOWN`; the order is
  recorded and asserted.
- `AuthorizationGateTests` green: a command whose `[RequiresPermission]` is not held returns
  `PERMISSION_DENIED` and the handler is never invoked; a no-attribute command still requires a session.
- `CapabilityExpiryTests` green: a `High` capability expires after 24 h; a `Critical` capability is
  denied offline with `RequiresOnline`; rewinding the OS clock 30 days does not restore an expired
  capability; a mutated `PermissionsJson` → `CAPABILITY_SNAPSHOT_TAMPERED` and discard; a revoked
  snapshot denies every write capability.
- `CapabilityStoreTests` green: replace/revoke/read round-trip; one active snapshot per user+device.
- Signing in as `Receptionist` yields a sidebar without Finance/Cheques/Purchasing; `Manager` yields all
  items. No `.axaml` file is modified (`git diff --name-only` contains no `.axaml`).
- Runnability (§11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark B04c done **and mark B04 done overall**; advance "Next phase" to `B05` (`B05a`); append a "Repo
state" entry listing: the `Animora.Contracts` additions (`ICapabilityGate`, `NavItem.RequiredPermission`),
`CapabilityGateService` + bounded cache, `NavPermissions`, the two ViewModels touched, the DI change,
the 4 test classes, the nav→permission mapping decision (any `*.view`→`*Read`/`*Manage` reuse), no mock
deleted, and any deviation with its justification.
