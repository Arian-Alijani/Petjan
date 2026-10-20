# 08 — Multi-Tenant Provisioning + Data Isolation Audit (audit targets #10 + #12, CRITICAL)

Scope: `Persistence.PostgreSql/{Tenancy,Provisioning,Context,Conventions}` + `Server/Provisioning` +
tenant stamping / ignore-filter-ban interceptors + `TenantProvisioner`, `TenantDirectory`, read-context
factory. Covers clinic/account provisioning (each customer buys an account+clinic) and standards-compliant
tenant data separation.

Verification outcome: the isolation model is **defence-in-depth and largely correct**. Every
`SyncableEntity` root gets a named `Tenant` query filter + soft-delete filter; writes are stamped from the
*context* (never the entity) with a hard `TenantMismatchException`; the unnamed `IgnoreQueryFilters()`
overload is banned by a query-expression interceptor and the named `"Tenant"` bypass is operator-only;
tenant-first indexes are the convention; provisioning is single-transaction with fresh ULIDs. Findings
below are a small number of seams and residual risks, not a broken isolation core.

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| ISO-01 | Medium | isolation (seam) | `UserLookupContext` is a **keyless, tenant-filter-free** read over `public."Users"` (`FindByIdAsync(userId)` with no tenant predicate) — a deliberate pre-binding seam, but the one cross-tenant read path; its safety rests entirely on ULID unguessability + correct downstream `tenantContext.Bind` |
| ISO-02 | Medium | isolation (latent) | Tenant isolation depends on **every** tenant table deriving from `SyncableEntity`; any future server-side table holding tenant data that does NOT inherit it (e.g. a bespoke Files/Insight projection) silently gets no `Tenant` filter and no stamping — the model has no compile-time assertion that "tenant data ⇒ SyncableEntity" |
| ISO-03 | Low | isolation (operator) | The named-filter bypass `IgnoreQueryFilters(["Tenant"])` is allowed whenever `tenantContext.IsOperator` — correct by design, but it is a broad grant: any operator-context query reads across all tenants with no per-row authorization beyond the IsOperator flag (admin/operator reads, backups) |
| ISO-04 | Low | provisioning (latent) | `TenantProvisioner` binds the new tenant on the scope's `AuthTenantContext` and warns callers to use a dedicated scope — a shared/re-used scope would cross-contaminate the bound tenant; the invariant is documented, not enforced |
| ISO-05 | Info | — | Isolation core verified sound: stamp-from-context-never-entity with `TenantMismatchException`; `TenantContextMissingException` fails closed when no tenant bound; named Tenant + SoftDelete filters on every `SyncableEntity` root; unnamed `IgnoreQueryFilters()` banned via `IQueryExpressionInterceptor`; tenant-first indexes (`IX_<Table>_TenantId_<cols>`); provisioning is one transaction with fresh ULID tenant/user/roles + unique-code (23505) handling; read contexts are no-tracking, tenant-bound, and attach both tenancy interceptors |

---

## ISO-05 (baseline verification — isolation is defence-in-depth)

- Severity: Info
- Location: `Tenancy/TenantStampingInterceptor.cs`, `Tenancy/NoUnnamedIgnoreFiltersInterceptor.cs`,
  `Conventions/ServerModelConventions.cs`, `Context/ServerReadContextFactory.cs`,
  `Identity/TenantProvisioner.cs`.
- Evidence:
  - **Write stamping (never trust the entity):** `TenantStampingInterceptor.Stamp` sets `TenantId` from
    `ITenantContext` on Added/Modified rows; a pre-set value differing from the context throws
    `TenantMismatchException`; an empty context throws `TenantContextMissingException`. Operators may write
    only `Tenant` rows (anything else → mismatch).
  - **Query filters:** `ServerModelConventions.ConfigureFilters` adds a named `Tenant` filter
    (`e.TenantId == <context accessor>`) and a `SoftDelete` filter to every `SyncableEntity` root; the accessor
    stays rooted at the context instance so one cached model serves many tenants.
  - **Filter-bypass ban:** `NoUnnamedIgnoreFiltersInterceptor` throws on the unnamed `IgnoreQueryFilters()`
    overload and on `IgnoreQueryFilters(["Tenant"])` unless `IsOperator`.
  - **Indexes:** `TenantIndex` forces `TenantId` as the leading index column.
  - **Provisioning:** `TenantProvisioner.CreateTenantWithOwnerAsync` creates tenant + five system role
    definitions + Manager owner + membership in one transaction with fresh ULIDs, binds the new tenant on a
    dedicated scope, validates the code charset/length and password policy, and maps a unique-violation
    (`23505`) to a clean validation error — no shared mutable defaults, no id reuse.
  - **Read contexts:** `ServerReadContextFactory.Create` builds no-tracking contexts bound to the request
    tenant with both tenancy interceptors resolved from the scope.
- Why it matters: establishes that stamping, filtering, the bypass ban, provisioning isolation and
  tenant-first indexing are all present and correct; the findings are residual seams, not systemic leakage.
- Confidence: High.

## ISO-01 Keyless tenant-filter-free user lookup is the one cross-tenant read

- Severity: Medium
- Class: isolation (seam)
- Location: `Identity/UserLookupContext.cs`:
  ```csharp
  // Keyless read context over public."Users" with no tenant query filter.
  public DbSet<UserLookupEntry> Users => Set<UserLookupEntry>();
  …
  return await context.Users.AsNoTracking().Where(u => u.Id == userId).FirstOrDefaultAsync(ct);
  ```
- Why it is a problem: this context deliberately has **no** tenant filter so it can resolve a user before a
  tenant is bound (token validation / pre-binding). `FindByIdAsync(userId)` therefore returns *any* tenant's
  user row (Id, TenantId, SecurityStamp, Status). It is a correct necessity, but it is the single place the
  tenant wall is absent; its safety depends on (a) `userId` being an unguessable ULID and (b) every caller
  using the returned `TenantId` only to *bind* context, never trusting a caller-supplied tenant. A future
  call that exposes `UserLookupEntry` fields to a caller, or filters by a weaker key than the ULID, would
  leak cross-tenant identity metadata.
- Scenario / impact: as written, no leak — but it is the highest-value seam to guard in review; any widening
  (lookup by name/email without tenant, or returning the entry to an untrusted caller) becomes a cross-tenant
  disclosure.
- Confidence: Medium (safe at this baseline; flagged because it is the lone unfiltered path and its safety is
  convention-bound).

## ISO-02 "Tenant data ⇒ SyncableEntity" is not enforced

- Severity: Medium
- Class: isolation (latent)
- Location: `Conventions/ServerModelConventions.ApplyTenancy` iterates
  `modelBuilder.Model.GetEntityTypes()` and applies filters/stamping **only** to types assignable to
  `SyncableEntity`.
- Why it is a problem: the whole isolation guarantee is keyed on inheritance. A new server table that holds
  tenant-scoped data but is modelled as a plain entity (not deriving `SyncableEntity`) silently receives no
  `Tenant` filter and is not stamped — queries against it would return all tenants' rows. The Files/Insight
  subsystems and any future projection are the risk area (the plan explicitly calls out admin/operator/Files/
  Insight cross-tenant paths). There is no model-build assertion that fails if a tenant-bearing table escapes
  the convention.
- Scenario / impact: a developer adds a `tenantId`-bearing analytics/Files table without the base type; it
  compiles, migrates, and leaks across tenants until someone notices. Latent, high-blast-radius if it happens.
- Confidence: Medium (no current offender found in the audited scope; the gap is the absence of a guard).

## ISO-03 Operator filter-bypass is a broad, flag-gated grant

- Severity: Low
- Class: isolation (operator)
- Location: `Tenancy/NoUnnamedIgnoreFiltersInterceptor.QueryCompilationStarting` —
  `IgnoreQueryFilters(["Tenant"])` permitted when `tenantContext.IsOperator`; `TenantStampingInterceptor`
  lets operators write only `Tenant` rows.
- Why it is a problem: once a context is in operator mode, any query may drop the tenant filter and read
  across all tenants. That is the intended operator capability, but it is coarse: a single boolean gates
  cross-tenant read for the whole context, so an operator-code bug (or an over-broad operator query in
  Admin/Insight/backup) reads every tenant with no finer authorization. The write side is tighter
  (Tenant-only), so the exposure is read-oriented.
- Scenario / impact: an over-scoped operator report or a backup/export path reads data it should not; bounded
  to operator-context code, but that code must be audited as if it is the tenant wall (because it is).
- Confidence: High (design-evident); impact depends on operator-path code reviewed in T11 (backup).

## ISO-04 Provisioning scope-isolation is documented, not enforced

- Severity: Low
- Class: provisioning (latent)
- Location: `Identity/TenantProvisioner` — *"Binds the new tenant on the scope's `AuthTenantContext`, so use a
  dedicated scope per provisioning call."*
- Why it is a problem: `CreateTenantWithOwnerAsync` calls `tenantContext.Bind(tenant.Id)`. If invoked on a
  scope already bound to (or later reused for) another tenant, the binding is clobbered — a provisioning call
  sharing a request scope with other tenant work could stamp/read under the wrong tenant. The requirement is a
  comment; nothing asserts the scope was fresh/unbound.
- Scenario / impact: a future caller wiring `TenantProvisioner` into a shared scope introduces cross-tenant
  binding; today's callers (S15/S19 + tests) use a dedicated scope, so no live defect.
- Confidence: Medium (no current misuse; the enforcement gap is the finding).
