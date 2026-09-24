# Phase B04a — Authorization evaluator, policies & pipeline gate (session 1/3 of B04)

**Band:** M · **Track:** B · **Depends on:** B03 ✅ · **Next:** B04b · Split of original B04 (B04a→B04c, one session each).
**Satisfies (with B04b+B04c):** R-B04, R-B20, R-B21 · **Runnable after this phase:** desktop offline yes · server required no

---

## ⚡ Zero-exploration contract (read this first)
> This file is self-contained and pre-verified against the current tree. **Do not re-open the repo to
> "understand" it.** Implement top-to-bottom from §Implementation steps. Names here are the **real**
> ones in the code (see the reality check). If a compile error proves a snippet diverges, fix the
> smallest thing and log it in `PROGRESS.md`; do not pre-emptively audit.

**This is session 1 of 3.** It lands the **authorization plane only**: the permission evaluator + the 5
named policies + the resource-rule seam + `RequiresPermissionAttribute` + `PermissionAuthorizationGate`
(replacing the allow-all), plus storing the permission code on the pipeline entry. **No capability
snapshot, no Contracts/shell/DI-for-gate, no tests** — those are B04b (offline capability) and B04c
(shell seam + tests + gate). Do not build ahead of this scope.

### Environment (already true — do not probe)
- `scripts/sandbox/install-prereqs.sh` once (idempotent); `dotnet-ef 10.0.12` at `.sandbox/tools`
  (do not `dotnet tool install`). **No migration this session** (the snapshot migration `006` is B04b).
- Build is **only** `scripts/sandbox/build.sh full` → `0 warnings / 0 errors`. **No `test.sh`/`run.sh`
  this session** (tests + smoke gate are B04c). Sandbox RAM ≈ 985 MiB; no parallel/server-GC flags.

### Rules (condensed — do not open `CONVENTIONS.md`)
- **§2 layering:** `Animora.Application` → Domain only. **§4 style:** `sealed`/`sealed record`,
  nullable honored, async-all-the-way (`ct = default`), library `ConfigureAwait(false)`, `Result`/
  `Result<T>` not exceptions, `IClock` injected, Ids via `IdGenerator.NewId()`.
- **§7 security:** authorization fails **closed** — an unknown permission code is a deny.
- **PERFORMANCE.md §3:** the permission code is resolved **at registration**, never by attribute
  reflection at dispatch. **§11/§12:** build 0/0; `PROGRESS.md` updated; commit on `genspark_ai_developer`.

## Goal (this session)
Turn the permission catalog into a working RBAC + policy + resource evaluator, bind each
command/query to its permission code at registration, and enforce it in the B01 dispatcher chain via a
real gate that fails closed.

## §Reference pack (verbatim — do not re-read source)

### R1 · `PermissionCodes` (A03) — real naming; use verbatim
Codes are `<context>.<action>` with **no aggregate segment** (e.g. `patients.read`, `sales.post`,
`finance.period.close`, `cashier.session.open`) — **not** `sales.invoice.post`. Full catalog (42 codes)
is `src/Animora.Domain/Identity/PermissionCodes.cs`; use the existing constants verbatim
(`PermissionCodes.SalesPost`, `.PatientsWrite`, …) — invent no literal strings, rename nothing.
`RiskLevel` is computed by `PermissionCodes.RiskOf(code)` from two `HashSet`s (`CriticalCodes`,
`HighCodes`) plus a `.read`-suffix → `Low` rule; **reuse `RiskOf` / `OfflineCapabilityPolicy.WindowForCode`
directly** (do not re-derive a risk table). `RoleDefinition.CreateSystem(tenantId, role, clock)` rows
are per-tenant, seeded once by `LocalSeeder`'s `identity.roles` block; `DefaultRolePermissions` maps a
role → codes. `StaffMembership` carries `ExtraPermissionCodes` / `DeniedPermissionCodes` (A03).

### R2 · The B01 gate contract + dispatcher chain (fixed)
```csharp
public interface IAuthorizationGate {
    ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct);
    ValueTask<Result> EvaluateAsync(object query,   QueryContext   context, CancellationToken ct); }
```
`AuthorizationBehavior` (already wired into `Dispatcher.Chain`, stage 3) already coerces a
non-`Forbidden`-category failure to `ErrorCodes.PermissionDenied` — so `PermissionAuthorizationGate`
only returns the right category; it does not duplicate that coercion. `AllowAllAuthorizationGate`
(`src/Animora.Application/Security/AllowAllAuthorizationGate.cs`) is the current default to **delete**.
The real session/subject source is B03's `SessionCurrentUserProvider` (tenant/user/device from the
active `LocalSession`).

### R3 · Pipeline records (must extend, not reshape)
`HandlerEntry(RequestType, ResultType, HandlerType, Kind, Pipeline, Invoker)` and
`PipelineDescriptor(RequestType, ResultType, Kind, HasValidators)` are immutable records. Storing the
permission code requires **adding a field to both** (e.g. `PermissionCode` on `PipelineDescriptor`,
populated by reading `RequiresPermissionAttribute` in `ApplicationRegistrationBuilder.BuildEntry` via
`typeof(TRequest).GetCustomAttribute<...>()` **once, at registration**). Every behavior receives the
same `PipelineDescriptor`, so a new field is additive and safe. **Do not change
`IPipelineBehavior.Wrap<TRequest,TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)`'s
signature — only extend the record.** (`HandlerRegistry.cs` is the file to modify.)

### R4 · Real resource field names (for the policies/rules)
`CashSession.CashierUserId` is the session-owner field (not `OpenedByUserId`); `AccountingPeriod` uses
`PeriodState` (`Open | Closing | Closed | Locked`) — `PeriodOpen` checks
`period.State == PeriodState.Open`. `Invoice`/`Expense`/`PurchaseOrder`/`Appointment` expose
`CreatedByUserId`; `Visit`/`Appointment`/`ReceptionQueueEntry` expose `DoctorUserId` (nullable on
`Appointment`/`ReceptionQueueEntry`, required on `Visit`).

### R5 · Repository availability
B03 added `IRoleDefinitionRepository`/`IStaffMembershipRepository`/`IDeviceRepository`. If any is
missing, add the minimal lookup here rather than blocking. `PermissionEvaluator` loads the tenant's
stored `RoleDefinition` for a `StaffRole` via `IRoleDefinitionRepository` (a tenant may have edited it)
— do not re-derive from `DefaultRolePermissions` at evaluation time.

## §Design decisions (implement — do not re-derive)
- **Permission model (fixed):** three evaluation kinds, checked **in order** by `PermissionEvaluator`:
  1. **RBAC** — effective set = `RoleDefinition.PermissionCodes ∪ StaffMembership.ExtraPermissionCodes
     \ StaffMembership.DeniedPermissionCodes` (denies win); does any active membership grant the code?
  2. **Policy** — a named rule from `AuthorizationPolicies` (below).
  3. **Resource** — an `IResourceAuthorizationRule<TResource>` for instance-level checks.
  Result = `AuthorizationDecision(bool Allowed, string? DeniedByRule, string PermissionCode)`. Returns
  at the **first denial**. **Unknown code = deny** (`PERMISSION_UNKNOWN`), fail closed.
- `Application/Security/IPermissionEvaluator.cs`:
  `Task<AuthorizationDecision> EvaluateAsync(string permissionCode, AuthorizationSubject subject,
  object? resource, CancellationToken ct)`;
  `AuthorizationSubject(string TenantId, string UserId, IReadOnlySet<string> Permissions,
  StaffRole Role, string DeviceId, bool IsOffline)`.
- `Application/Security/AuthorizationPolicies.cs` — a `FrozenDictionary<string,
  Func<AuthorizationSubject, object?, bool>>` (no reflection):
  | Policy | Rule |
  |---|---|
  | `OwnRecordOrManager` | actor is the record's `CreatedByUserId`, or holds `StaffRole.Manager` |
  | `AssignedDoctorOnly` | actor is the visit's `DoctorUserId` (managers exempt) |
  | `OpenCashSessionOwner` | actor opened the `CashSession` (`CashierUserId`) being modified |
  | `PeriodOpen` | the target `AccountingPeriod.State == PeriodState.Open` |
  | `NotSelfDestructive` | a user may not disable/delete their own account or last manager role |
- `Application/Security/IResourceAuthorizationRule.cs` —
  `interface IResourceAuthorizationRule<TResource> { string PermissionCode { get; }
  bool IsSatisfied(AuthorizationSubject subject, TResource resource); }`. Ship two reference rules:
  `Rules/InvoiceAuthorizationRule.cs` (posted invoices need `sales.invoice.reverse`, not
  `sales.invoice.edit` — use the real `PermissionCodes` equivalents per R1) and
  `Rules/CashSessionAuthorizationRule.cs` (session-owner).
- `Application/Security/RequiresPermissionAttribute.cs` — `[RequiresPermission("<code>")]` on a
  command/query record; **read once at registration** (R3) and stored on the entry as a plain string.
  A command with **no attribute** is treated as `identity.session.any` (authenticated-only) — never as
  "public".
- `Application/Security/PermissionAuthorizationGate.cs` — replaces `AllowAllAuthorizationGate`
  (deleted): reads the entry's permission code, builds the `AuthorizationSubject` from the session,
  calls `IPermissionEvaluator`, returns `PERMISSION_DENIED` with `Detail = DeniedByRule`.
- **Attribute application (existing commands):** apply `[RequiresPermission]` to the B02/B03 commands
  now visible — `SignInCommand` → none/authenticated; `ChangePasswordCommand` → `identity.self.password`
  (use the real `PermissionCodes` constant if one exists; otherwise the closest existing self-scope
  code, recorded in `PROGRESS.md`). Do **not** add new permission codes — B04 consumes them.
- **New error code (this session):** `PERMISSION_UNKNOWN`. (`PERMISSION_DENIED` already exists.)
  The other two new codes (`CAPABILITY_SNAPSHOT_TAMPERED`, `CAPABILITY_SNAPSHOT_MISSING`) are B04b.

## Files to create / modify
| Path | Action | Contents |
|---|---|---|
| `src/Animora.Application/Security/IPermissionEvaluator.cs` | create | evaluator port + `AuthorizationDecision`/`AuthorizationSubject` |
| `src/Animora.Application/Security/PermissionEvaluator.cs` | create | RBAC → policy → resource, in order, deny unknown |
| `src/Animora.Application/Security/AuthorizationPolicies.cs` | create | the 5 named policies (frozen map) |
| `src/Animora.Application/Security/IResourceAuthorizationRule.cs` | create | resource rule port |
| `src/Animora.Application/Security/Rules/InvoiceAuthorizationRule.cs` | create | posted-invoice rule |
| `src/Animora.Application/Security/Rules/CashSessionAuthorizationRule.cs` | create | session-owner rule |
| `src/Animora.Application/Security/RequiresPermissionAttribute.cs` | create | registration-time permission binding |
| `src/Animora.Application/Security/PermissionAuthorizationGate.cs` | create | real gate for the B01 chain |
| `src/Animora.Application/Security/AllowAllAuthorizationGate.cs` | delete | replaced |
| `src/Animora.Application/Dispatch/HandlerRegistry.cs` | modify | store the permission code on the entry (R3) |
| `src/Animora.Application/ApplicationServiceCollectionExtensions.cs` | modify | gate → `PermissionAuthorizationGate`; register evaluator + rules; apply `[RequiresPermission]` |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append `PERMISSION_UNKNOWN` |
| `backend-roadmap/PROGRESS.md` | modify | mark B04a done, next → B04b, repo-state entry, deviations |

## §Implementation steps
1. Add the evaluator port, `AuthorizationSubject`, `AuthorizationDecision`, then `AuthorizationPolicies`
   as a frozen map.
2. Implement `PermissionEvaluator` (RBAC → policy → resource, first-denial return, unknown → deny).
3. Add `IResourceAuthorizationRule<T>` + the two reference rules.
4. Add `RequiresPermissionAttribute`; read it in `ApplicationRegistrationBuilder` and store the string
   on the entry (extend the two records — R3). Apply the attribute to `SignInCommand`
   (none/authenticated) and `ChangePasswordCommand` (`identity.self.password`).
5. Implement `PermissionAuthorizationGate`, delete `AllowAllAuthorizationGate`, update the DI in
   `AddAnimoraApplication`.
6. Append `PERMISSION_UNKNOWN`.
7. **Gate:** `scripts/sandbox/build.sh full` = 0/0.
8. `PROGRESS.md` update + commit on `genspark_ai_developer`.

## Contracts / schema added
- Application: `IPermissionEvaluator`/`PermissionEvaluator`, `AuthorizationSubject`,
  `AuthorizationDecision`, `AuthorizationPolicies` (5 policies), `IResourceAuthorizationRule<T>` + 2
  rules, `RequiresPermissionAttribute`, `PermissionAuthorizationGate` (replaces `AllowAllAuthorizationGate`),
  the permission-code field on `HandlerEntry`/`PipelineDescriptor`.
- DI: `IAuthorizationGate` → `PermissionAuthorizationGate`; evaluator + rules registered.
  `ErrorCodes` += 1 (`PERMISSION_UNKNOWN`). No migration, no mock deleted this session.

## Rules that apply here
- **A-RULE 023 / R-B20** — authorization is its own plane; authentication (B03) and entitlement (E07)
  untouched.
- **PERFORMANCE.md §3** — permission code resolved at registration; dispatch is a dictionary lookup.
- **CONVENTIONS §7** — unknown code fails closed (`PERMISSION_UNKNOWN`).

## Out of scope (sibling sessions)
- Capability snapshot table/signer/evaluator/issuer + migration `006` + sign-in integration → **B04b**.
- `ICapabilityGate` (Contracts), `CapabilityGateService`, `NavPermissions`, shell ViewModels, gate DI,
  all tests + `run.sh` gate → **B04c**.

## Acceptance criteria
- `scripts/sandbox/build.sh full` = 0/0.
- `src/Animora.Application/Security/AllowAllAuthorizationGate.cs` does not exist;
  `PermissionAuthorizationGate.cs` does, and `AddAnimoraApplication` registers it.
- `grep -n "PermissionCode" src/Animora.Application/Dispatch/HandlerRegistry.cs` shows the new field on
  the descriptor/entry.
- `PERMISSION_UNKNOWN` is present in `ErrorCodes.cs`; `AuthorizationPolicies` contains the 5 named
  policies.

## PROGRESS.md update
Mark B04a done; advance "Next phase" to `B04b`; append a "Repo state" entry listing: the evaluator +
the 5 named policies + resource-rule seam + 2 rules, `RequiresPermissionAttribute` and its
registration-time resolution (the added `PipelineDescriptor`/`HandlerEntry` field),
`PermissionAuthorizationGate` (replacing the allow-all), the attribute application to `SignInCommand`/
`ChangePasswordCommand`, `ErrorCodes` += 1, and any deviation with its justification.
