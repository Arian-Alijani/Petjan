# Animora — Commercial SaaS Alignment Record (2026-10-07)

**Scope:** Documentation / roadmap reconciliation only. **No source, migration, build or test was
touched, and no build/test was run** — per the task instruction (all phases build 0/0 and are
test-clean; the ask was to find and fix *doc & phase* inconsistencies, not code).

---

## 1. The goal this record aligns to

The product is a **commercial, vendor-hosted (SaaS) platform**. The customer journey is:

> **register & buy a subscription on the website → download the app → log in & use it**

Consequences that the specs must reflect without contradiction:

- The **vendor (Animora) runs one central, multi-tenant server**. Customers never buy, host, operate
  or own a server — that is explicitly *not* the product.
- Each clinic is **one tenant** of a shared database; many users per tenant.
- Data of different paying customers is managed on the vendor server with: proper security, **no
  tenant can read another tenant's data**, good performance, and a flawless sync architecture.
- All remaining backend / architecture / stack phases must be coherent and aligned to this model.

## 2. Diagnosis — what was wrong

The design was already *substantially* this SaaS model (shared-DB multi-tenancy in `ARCHITECTURE.md`
§9.1 / R6; a cross-tenant **Platform Operator**; per-tenant Subscription & Licensing contexts). Two
problems blocked full alignment:

### Problem 1 — a hosting-model contradiction
Several passages described the product as a **"self-hosted, single clinic-server node, no shared
multi-tenant"** deployment — i.e. *each clinic hosts its own box*. That directly contradicts the
shared-DB multi-tenant design and the user's goal (one vendor server for all customers). The word
**"self-hosted" was ambiguous**: it was meant as *"the vendor runs the server on its own
infrastructure, not on a cloud PaaS"*, but read in §17 as *"each clinic self-hosts"*.

Locations: `ARCHITECTURE.md` header, §0 (deployment row), R7, §3 (system-context diagram + Future
Public Website bullet), §16.2, §17.1/§17.2; mirrored in `STACK.md` header and `AGENTS.md`.

### Problem 2 — a missing capability (the user's first step)
The flow begins with **"register & buy a subscription on the website"** (self-service). The plan only
supported an **operator manually** creating a tenant (`D10` `POST /api/operator/tenants`) and deferred
the public website as an undefined "future separate system." So the roadmap did not actually deliver
the self-service onboarding step — there was no specified server seam the website could hand a paid
signup to.

## 3. Decisions applied

1. **"Self-hosted" is defined once, unambiguously** = *vendor self-operates the server on its own
   infrastructure (not a cloud PaaS)*; it **never** means per-clinic hosting. R7 (zero external-Internet
   dependency at runtime) is a property of **the vendor's server environment**, not a per-clinic box.
2. **Deployment is a single, central, vendor-operated, multi-tenant node** at launch; tenant isolation
   (§9) is the boundary between clinics. Per-tenant DB/stamp stays the documented **§9.1 escape hatch**,
   not the launch shape.
3. **Self-service onboarding = a new §13.4 contract.** The public website collects registration +
   payment (payment lives **only** on the website; this solution has **no payment gateway/SDK**), then
   hands an **async, idempotent, versioned provisioning intent** to the Subscription context.
4. **One provisioning operation, two authorized callers.** The website's automated intake and the
   operator's manual action call the **same** `ISubscriptionOperations.CreateAsync` — never a second
   provisioning engine. This keeps R6 and R7 both true: a customer *buys* online, but a provisioned
   clinic *runs* with the website gone entirely.
5. **New requirement `R-B36`** locks the commercial-SaaS shape; it is owned by `D10` (intake) with
   `D11` (license issued on provisioning) and `E07` (customer login/activation) carrying its
   downstream legs.

## 4. Changes made (by file)

| File | Change |
|---|---|
| `ARCHITECTURE.md` | Header product def, §0 deployment row, R7, §3 system-context diagram + website bullet, §13.1 context map, §13.1 invariant, §16.2, §17.1/§17.2 reconciled to vendor-hosted central multi-tenant SaaS. **New §13.4 "Self-service onboarding contract."** |
| `STACK.md` | Header product def + hard-runtime-rule reworded; **new §17 non-goal**: no payment gateway/SDK (payment is the separate vendor-side website). |
| `AGENTS.md` | "What this repo is" paragraph reframed to vendor-hosted SaaS; current-state note clarified (offline-first client, **no server yet**, Tracks D–E deliver the central multi-tenant server + subscription/licensing + Datasync). |
| `backend-roadmap/phases/PHASE-D10-*.md` | Added the **onboarding intake seam**: `POST /api/onboarding/provision`, `OnboardingEndpoints`, `OnboardingAccountService`, `ANIMORA_ONBOARDING_BOOTSTRAP`, `RequireProvisioner`, `OnboardTenantRequestV1` / `OnboardingResultV1`, `PROVISIONER_SCOPE_REQUIRED`, `OnboardingIntakeTests`; Goal / Files / steps / contracts / out-of-scope / acceptance / PROGRESS-update all updated. Reuses `CreateAsync`. `Satisfies` += R-B36. |
| `backend-roadmap/00-REQUIREMENTS-LOCK.md` | Added **R-B36**; coverage guarantee updated to 36/36. |
| `backend-roadmap/ROADMAP.md` | D10/D11/E07 `Satisfies` columns += R-B36; D10 title notes the onboarding intake. |
| `PHASE-D11-*.md`, `PHASE-E07-*.md` | `Satisfies` headers += R-B36 (downstream legs). |

## 5. What was intentionally NOT changed

- **No source, migration, test or build artefact** — this is a specs/roadmap pass only.
- The **public website itself** (pages, plan picker, payment UX) remains an independent vendor-side
  system outside this solution; this work only specifies the **server-side intake endpoint** it posts to.
- Prior consistency findings (migration-number collisions, command-vocabulary drift, encryption-at-rest
  enablement) were already resolved in `docs/Phase-Problem.md` (2026-10-07) and are unaffected here.
- Phases remain authored against the C11 baseline; the binding "next-free-physical-ordinal" migration
  rule in `PROGRESS.md` still governs execution-time numbering.
