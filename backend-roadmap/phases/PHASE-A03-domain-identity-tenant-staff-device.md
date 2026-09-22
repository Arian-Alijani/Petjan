# Phase A03 — Domain: Identity, Tenant, Staff, Device

**Band:** S · **Track:** A · **Depends on:** A02 · **Satisfies:** R-B04, R-B19, R-B21
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §7, §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §9.1 (tenancy model), §10.1 (three planes), §10.3 (authorization), §10.4 (offline
  session), §21 (A-RULE 012, 023, 029)
- `src/Animora.Domain/Primitives/*`, `src/Animora.Domain/Results/ErrorCodes.cs`
- `src/Animora.Contracts/Navigation/UserRole.cs`, `src/Animora.Contracts/Identity/ICurrentUserContext.cs`

## Goal
Model the identity spine: tenant/clinic, user, role, permission, staff membership, device and the
device-bound capability snapshot — plus the default role→permission matrix. Every later phase resolves
authority from these types instead of inventing its own.

## Design decisions (pre-made — implement, do not re-derive)
- **Folder:** `src/Animora.Domain/Identity/`.
- `Tenant.cs` — `sealed class Tenant : AggregateRoot`: `DisplayName`, `LegalName?`, `Timezone`
  (IANA string, default `"Asia/Tehran"`), `DefaultCurrency` (default `"IRT"`), `TenantStatus Status`.
  `enum TenantStatus { Active, Suspended, Closed }`.
  **Note:** `Tenant.Id` *is* the `TenantId` every other entity carries; `Tenant.TenantId` equals its own
  `Id` (set in the factory) so the tenant filter is uniform.
- `User.cs` — `sealed class User : AggregateRoot`: `UserName` (unique per tenant, lowercase invariant),
  `DisplayName`, `PasswordHash?`, `SecurityStamp` (ULID, rotated on credential/role change),
  `UserStatus Status`, `DateTimeOffset? LastLoginUtc`, `int FailedAttemptCount`,
  `DateTimeOffset? LockoutEndUtc`.
  `enum UserStatus { Active, Disabled, Locked }`.
  Methods: `RegisterFailedAttempt(IClock, int threshold, TimeSpan window)`,
  `ClearFailures()`, `RotateSecurityStamp()`, `SetPasswordHash(string)`.
  Lockout policy constants live here: `MaxFailedAttempts = 5`, `LockoutDuration = 15 min`.
- `StaffRole.cs` — `enum StaffRole { Manager, Doctor, Receptionist, Cashier, Accountant }`.
  Maps to the existing `Animora.Contracts.Navigation.UserRole` in C10 — **no change to Contracts here.**
- `Permission.cs` — `sealed class Permission`: `Code` (the PK, e.g. `"invoice.post"`), `Category`,
  `RiskLevel Risk`. `enum RiskLevel { Low, Normal, High, Critical }`.
- `PermissionCodes.cs` — static catalog of permission code constants, grouped by context. Seed set
  (exact strings; later phases add, never rename):
  ```
  patients.read patients.write
  reception.manage
  visits.read visits.write visits.complete
  vaccination.read vaccination.write
  appointments.read appointments.book appointments.move appointments.cancel
  boarding.read boarding.manage boarding.checkout
  inventory.read inventory.adjust inventory.receive
  purchasing.read purchasing.manage
  sales.read sales.draft sales.post sales.void sales.refund sales.discount.approve
  payments.register payments.reverse
  cashier.session.open cashier.session.close
  cheques.read cheques.manage
  finance.read finance.post finance.period.close
  reports.read
  settings.read settings.write
  users.manage roles.manage devices.manage
  audit.read
  ```
  Risk assignment: `sales.void`, `sales.refund`, `payments.reverse`, `finance.period.close`,
  `users.manage`, `roles.manage`, `devices.manage` = `Critical`; `sales.post`, `payments.register`,
  `cashier.session.close`, `inventory.adjust`, `finance.post`, `sales.discount.approve` = `High`;
  all `*.read`/`reports.read`/`audit.read` = `Low`; everything else `Normal`.
- `RoleDefinition.cs` — `sealed class RoleDefinition : AggregateRoot`: `StaffRole Role`,
  `DisplayKey` (a stable non-localized key, e.g. `"role.manager"`), `bool IsSystem`,
  `IReadOnlySet<string> PermissionCodes` (backed by a private `HashSet<string>`),
  `Grant(string code)` / `Revoke(string code)` which throw when `IsSystem` and the role is `Manager`.
- `DefaultRolePermissions.cs` — static `IReadOnlyDictionary<StaffRole, IReadOnlySet<string>>`:
  | Role | Permissions |
  |---|---|
  | Manager | every code in `PermissionCodes` |
  | Doctor | patients.read/write, visits.*, vaccination.*, appointments.read/book/move, boarding.read, inventory.read, sales.read, sales.draft, reports.read, settings.read |
  | Receptionist | patients.read/write, reception.manage, appointments.*, visits.read, vaccination.read, boarding.read/manage, sales.read, sales.draft, settings.read |
  | Cashier | patients.read, sales.read/draft/post, payments.register, cashier.session.open/close, cheques.read/manage, inventory.read, reports.read, settings.read |
  | Accountant | finance.read/post/period.close, sales.read, payments.register/reverse, cheques.read/manage, purchasing.read/manage, inventory.read, reports.read, audit.read, settings.read |
- `StaffMembership.cs` — `sealed class StaffMembership : AggregateRoot`: `UserId`, `StaffRole Role`,
  `MembershipStatus Status`, `DateTimeOffset JoinedUtc`, `DateTimeOffset? EndedUtc`,
  `IReadOnlySet<string> ExtraPermissionCodes`, `IReadOnlySet<string> DeniedPermissionCodes`.
  `enum MembershipStatus { Active, Suspended, Ended }`.
  **This is the only place a `(User, Tenant)` relationship exists** — `TenantId` is resolved from an
  active membership, never from client input (A-RULE 012).
- `Device.cs` — `sealed class Device : AggregateRoot`: `UserId`, `DeviceName`,
  `InstallationIdHash` (SHA-256 hex of the sealed per-installation identity — the value itself never
  leaves the device; used by D11's license binding), `Platform`, `DeviceStatus Status`,
  `DateTimeOffset RegisteredUtc`, `DateTimeOffset? LastContactUtc`, `DateTimeOffset? RevokedUtc`,
  `string? RevocationReason`. `enum DeviceStatus { Pending, Active, Revoked }`.
  Method `Revoke(string reason, IClock)` — idempotent.
- `CapabilitySnapshot.cs` — `sealed record CapabilitySnapshot`:
  `TenantId`, `UserId`, `DeviceId`, `StaffRole Role`,
  `IReadOnlyDictionary<string, DateTimeOffset> CapabilityExpiry` (per-capability absolute UTC expiry),
  `DateTimeOffset IssuedUtc`, `DateTimeOffset SnapshotExpiryUtc`, `string SecurityStamp`,
  `string Signature` (base64; produced/verified server-side — D03/E07).
  Methods: `bool Allows(string permissionCode, DateTimeOffset nowUtc)` — returns false when the
  snapshot is expired **or** the capability's own expiry passed (**fail closed**);
  `bool IsExpired(DateTimeOffset nowUtc)`.
- `OfflineCapabilityPolicy.cs` — static per-risk offline windows, the authoritative numbers:
  `Low = 30 days`, `Normal = 14 days`, `High = 72 hours`, `Critical = 0` (never allowed offline).
  Method `TimeSpan WindowFor(RiskLevel)`. B04 consumes this; no phase changes the values without an ADR.
- **New error codes appended to `ErrorCodes.cs`:** `TENANT_SUSPENDED`, `USER_DISABLED`,
  `USER_LOCKED_OUT`, `MEMBERSHIP_NOT_ACTIVE`, `DEVICE_REVOKED`, `CAPABILITY_NOT_GRANTED`.
- No EF configuration, no persistence in this phase.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Identity/Tenant.cs` | create | tenant aggregate + `TenantStatus` |
| `src/Animora.Domain/Identity/User.cs` | create | user aggregate + lockout/security-stamp behavior |
| `src/Animora.Domain/Identity/StaffRole.cs` | create | the five roles |
| `src/Animora.Domain/Identity/Permission.cs` | create | permission entity + `RiskLevel` |
| `src/Animora.Domain/Identity/PermissionCodes.cs` | create | the code catalog above |
| `src/Animora.Domain/Identity/RoleDefinition.cs` | create | role aggregate + grant/revoke |
| `src/Animora.Domain/Identity/DefaultRolePermissions.cs` | create | the role→permission matrix |
| `src/Animora.Domain/Identity/StaffMembership.cs` | create | user↔tenant membership + overrides |
| `src/Animora.Domain/Identity/Device.cs` | create | device registration/revocation |
| `src/Animora.Domain/Identity/CapabilitySnapshot.cs` | create | signed, device-bound capability snapshot |
| `src/Animora.Domain/Identity/OfflineCapabilityPolicy.cs` | create | per-risk offline windows |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append the 6 identity codes |
| `tests/Animora.Domain.Tests/UserLockoutTests.cs` | create | 5 failures → locked; window expiry; clear on success |
| `tests/Animora.Domain.Tests/RolePermissionMatrixTests.cs` | create | every code exists; Manager = all; Critical codes absent from Doctor/Receptionist |
| `tests/Animora.Domain.Tests/CapabilitySnapshotTests.cs` | create | fail-closed on snapshot expiry, on capability expiry, and on an unknown code |

## Implementation steps
1. Create `Identity/` and add `Tenant`, `TenantStatus`.
2. Add `User` + `UserStatus` with the lockout/security-stamp methods and the two policy constants.
3. Add `StaffRole`, `Permission` + `RiskLevel`, and `PermissionCodes` with the exact strings listed.
4. Add `RoleDefinition` with `Grant`/`Revoke` guarding `IsSystem` Manager.
5. Add `DefaultRolePermissions` exactly as tabled; build it from `PermissionCodes` constants (never
   re-typed literals) so a renamed constant is a compile error.
6. Add `StaffMembership` + `MembershipStatus`, and `Device` + `DeviceStatus` with idempotent `Revoke`.
7. Add `CapabilitySnapshot` with fail-closed `Allows`, and `OfflineCapabilityPolicy`.
8. Append the 6 error codes to `ErrorCodes.cs` (do not reorder existing entries).
9. Write the three test classes. `RolePermissionMatrixTests` must assert that every string in every
   role's set is a member of `PermissionCodes.All` and that `Critical` codes appear only for Manager and
   (for the finance subset) Accountant.
10. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Entities: `Tenant`, `User`, `Permission`, `RoleDefinition`, `StaffMembership`, `Device`.
- Enums: `TenantStatus`, `UserStatus`, `StaffRole`, `RiskLevel`, `MembershipStatus`, `DeviceStatus`.
- Value object: `CapabilitySnapshot`. Statics: `PermissionCodes`, `DefaultRolePermissions`,
  `OfflineCapabilityPolicy`.
- `ErrorCodes` += 6 codes. No migration, no `Animora.Contracts` change.

## Rules that apply here
- **A-RULE 012** — `StaffMembership` is the only source of a `(User, Tenant)` link; document that
  `TenantId` is resolved from it server-side and never from client input.
- **A-RULE 023** — permissions (authorization) are modeled separately from credentials
  (authentication) and from license features (entitlement, D11). No type mixes two planes.
- **A-RULE 029** — `CapabilitySnapshot` carries explicit per-capability expiry and fails closed;
  it is not a token.
- **CONVENTIONS §7** — fail-closed default: `Allows` returns false for an unknown permission code.
- **CONVENTIONS §2** — still zero references in `Animora.Domain`.
- **CONVENTIONS §4** — no `DateTimeOffset.UtcNow`; `IClock` is passed into the behavior methods.

## Out of scope
- Password hashing, login flow, first-run setup → B03.
- `IPermissionEvaluator`, `ICurrentUserContext` real implementation, UI gating → B04.
- Snapshot signing/verification and server-side issuance → D03, E07.
- EF mappings and the identity migration → A09.
- ASP.NET Core Identity storage → D03.
- Mapping `StaffRole` → `Animora.Contracts.Navigation.UserRole` → C10.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `Animora.Domain.csproj` still has zero package/project references.
- `RolePermissionMatrixTests` green: every role's permission strings resolve to `PermissionCodes`
  constants; Manager's set equals `PermissionCodes.All`.
- `CapabilitySnapshotTests` green: an expired snapshot denies a `Low`-risk read; an unknown code is
  denied; a `Critical` code is denied when its expiry equals `IssuedUtc` (zero offline window).
- `UserLockoutTests` green: the 5th failed attempt sets `Locked` + `LockoutEndUtc`; a success clears it.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark A03 done; advance "Next phase" to `A04`; append a "Repo state" entry listing: the 11 identity files,
`PermissionCodes` seed count, the role matrix, the offline capability windows, `ErrorCodes` += 6, the 3
test classes, no DI change, no mock deleted, no migration created, deviations.
