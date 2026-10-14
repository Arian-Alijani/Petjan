# Plan — Sync reliability + per-clinic (tenant) isolation + logout

> **Read me first.** This file is the single source of truth for this work. Every fact below was
> verified against the repo at plan-time so a fresh model can implement **without re-investigating**.
> Follow the tasks top-to-bottom. **After completing each task you MUST run `scripts/git/push.sh "<msg>"`**
> (locked rule — no push = failure). The final task deletes this file.

---

## 0. Context the model needs (verified facts — do not re-derive)

**Project**: Animora — veterinary clinic suite. .NET solution (`Animora.sln`). Offline-first desktop
(Avalonia, `src/Animora.Desktop`) backed by a local **SQLite** store, syncing to a central
**multi-tenant PostgreSQL server** (`src/Animora.Server`, `src/Animora.Persistence.PostgreSql`).

**Sync model** (how a record travels):
- **Push**: client builds a command → stored in local SQLite → `POST /api/commands` → server applies →
  returns `outcome:0` + `serverVersion`; server writes a `TenantJournal` row (`Op=U`).
- **Pull**: client `GET /sync/manifest` → `POST /sync/batches` → `GET /sync/batches/{id}/pages/N` →
  `ReplicaMaterializer.Apply` projects staged generic rows onto typed local tables → UI reads typed tables.

**Key files (paths are exact):**
- Server save-time stamping: `src/Animora.Persistence.PostgreSql/Tenancy/TenantStampingInterceptor.cs`
- Server pull projector: `src/Animora.Persistence.PostgreSql/Replication/RecordProjector.cs`
- Server replication surface (authoritative entity list): `src/Animora.Persistence.PostgreSql/Replication/SyncContractCatalog.cs`
- **Client pull materializer (CORE of Task 1)**: `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs`
- Local DbContext + tenant query filter: `src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs`
- Tenant query filter definition: `src/Animora.Persistence.Sqlite/Conventions/ModelConventions.cs` (lines ~75-76)
- Local tenant source (CORE of Task 2): `src/Animora.Persistence.Sqlite/Context/LocalInstallationState.cs`,
  `SingleTenantContext.cs`, `Entities/LocalInstallation.cs`, `Context/LocalInstallationBootstrap.cs`
- Online login: `src/Animora.Client.Services/Identity/OnlineAuthClient.cs` (returns `login.TenantId`)
- App login→shell transition (CORE of Task 3): `src/Animora.Desktop/App.axaml.cs` (`LoginSucceeded`)
- Auth façade: `src/Animora.Client.Services/Identity/AuthenticationService.cs` (`SignOutAsync` exists)
- Local maintenance (wipe primitives): `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` + `ILocalStoreMaintenance`
- Settings/Profile UI: `src/Animora.Desktop/Pages/Profile/` (ProfilePageView.axaml[.cs], ProfilePageViewModel.cs)

**Build/test**: GitHub Actions CI already runs build+tests on push (`.github/`, `animora-ci.yml`).
**Do NOT run full local builds / long test suites** (explicit user constraint). Rely on CI. Only do
targeted `dotnet build` of a single project if a change is large/risky (then push per the heavy-change rule).

---

## 1. Root-cause analysis (DONE — conclusions are final, the technical report was pre-fix for 2 bugs)

The attached technical report was captured from logs **before** two fixes that are **already on `main`**.
Verified via `git log`:
- `8879f80c fix(server): stamp UpdatedAt on save so synced rows are not tombstoned`
- `52aef7c2 diag(trace): ... owners/patients load path`

### Already FIXED on main (do NOT touch, only confirm):
- **Report Bug #1 — `UpdatedAt = -infinity` → everything tombstoned.**
  Fixed write-side by `TenantStampingInterceptor.StampUpdatedAt` (stamps `UtcNow` when `UpdatedAt==default`),
  wired in DI (`PersistenceServiceCollectionExtensions.cs` lines ~62-65) and in the two ad-hoc contexts
  (`ProvisioningEngine`, `OwnerCredentialAdministration`). Also defended read-side in `RecordProjector`
  (an `-infinity`/`"-infinity"` cursor is parsed leniently to `null`, NOT treated as a tombstone).
  → **Owners/Pets now replicate as live UPSERTs.** No action needed beyond a sanity read.

### STILL BROKEN — these are the real work (match the user's 3 explicit tasks):

- **ROOT CAUSE A (→ user Task 1: offline items must sync to other clients).**
  `ReplicaMaterializer` in `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs` only projects **4**
  entities: `Owner`, `Pet`, `Appointment`, `ReceptionQueueEntry` (see its `Maps` array + `SupportedEntities`).
  But the server's authoritative replication surface (`SyncContractCatalog.ProjectionOrder`) ships **~27**
  entities incl. `Product`, `ProductVariants`, `ProductCategory`, `UnitOfMeasure`, `Supplier`, `Insurer`,
  `LedgerAccount`, `AccountingPeriod`, `Visit`, `VaccinationRecord`, `BoardingStay`, `WeightEntry`, etc.
  Any entity **not** in `Maps` is filtered out at `.Where(... Entities.Contains(x.Entity))` and **never
  written to the typed local table** — exactly the log line `0 staged row(s) match the projected entity set`
  for `Product`. So a product/invoice created on client A reaches `repl_base` on client B but **never lands
  in B's typed `Products` table → invisible on other clients.** This is report Bug #3, and it is the direct
  cause of "موارد ثبت‌شده توسط کلاینت‌های دیگر قابل دریافت نیستند".
  **Fix = extend `ReplicaMaterializer.Maps` with the missing entities (standard, same pattern already used).**

- **ROOT CAUSE B (→ user Task 2: clinic A data shows when logging in as clinic B).**
  The desktop is **single-tenant-per-install by design** (ARCHITECTURE §13: licence is *installation-bound*).
  The local SQLite file is a single shared file (`%LocalAppData%/Animora/animora.db`,
  `LocalStoreOptions.cs`). Tenant isolation relies **entirely** on the EF global query filter
  `HasQueryFilter("Tenant", e => e.TenantId == context.CurrentTenantId)` where `CurrentTenantId` =
  `LocalInstallationState.TenantId` = the single `LocalInstallation` row's `TenantId`.
  That row is created **once** at boot (`LocalInstallationBootstrap.EnsureAsync`) as the constant
  `T-LOCAL-DEFAULT` (`LocalInstallationState.DefaultTenantId`, also `Tenant.LocalDefaultId` used by
  `CompleteFirstRunSetupCommand`). `LocalInstallation.TenantId` has a **private setter and NO rebind method**;
  `LocalInstallationState.Set` is called **only once** from `LocalStoreStartup`. **Nothing reconciles the
  local tenant with the server tenant returned by online login (`login.TenantId`).**
  → Result: every clinic you log into on that machine writes/reads under the **same** `T-LOCAL-DEFAULT`
  tenant. Switching from clinic A to clinic B does **not** change the query-filter tenant, so B sees A's data.
  This is a genuine isolation/security gap, NOT yet fixed.
  **Standard fix (matches single-tenant-per-install design): on online login, if the authenticated server
  `TenantId` differs from the local installation's bound tenant, treat it as a tenant switch — rebind the
  installation to the new tenant AND clear the previous tenant's local business/replica data (so no cross-
  clinic residue), then let a fresh pull re-hydrate. A first-ever bind (still on `T-LOCAL-DEFAULT`) just
  adopts the server tenant.** This keeps isolation correct AND secure (old clinic's rows are physically gone,
  not merely filtered).

- **ROOT CAUSE C (→ user Task 3): no "Sign out" button for the user in settings.**
  `AuthenticationService.SignOutAsync(reason)` already exists and works. The app shows LoginWindow→MainWindow
  on `LoginViewModel.LoginSucceeded` (`App.axaml.cs`). There is **no UI affordance to sign out** and no
  reverse transition (shell → login). **Fix = add a "خروج از حساب" button in the Profile/Settings page that
  calls `SignOutAsync`, then returns the app to the login window.**

### Report bugs intentionally OUT OF SCOPE (server-side, not part of the user's 3 tasks):
- Bug #2 (`AccountingPeriods = 0` → `PERIOD_NOT_FOUND` on `sales.invoice.post`) and Bug #4 (server missing the
  desktop seed products → `line.add` `NOT_FOUND`) are **server provisioning/seed** concerns. The user's tasks
  are specifically about (1) offline→online sync visibility across clients, (2) tenant isolation, (3) logout.
  **Do NOT implement server seeding/period provisioning in this plan.** If, while fixing Root Cause A, the
  materializer naturally makes `AccountingPeriod`/`Product` visible once they exist, that is sufficient.
  (Rationale recorded so a later model doesn't chase them here.)

---

## 2. Tasks (do in order; push after EACH; ✅ when done)

### [x] T1 — Confirm Bug #1 fix is intact (read-only, no code change)  — STATUS: ✅ done (verified: StampUpdatedAt + lenient cursor + DI wiring present)
- Open `TenantStampingInterceptor.cs` and `RecordProjector.cs`; verify the `UpdatedAt` stamping +
  lenient-cursor logic described above is present. Verify DI wiring lines in
  `PersistenceServiceCollectionExtensions.cs` (~62-65).
- **Expected**: all present. If somehow missing/regressed, re-apply per §1 "Already FIXED" description.
- No push if nothing changed; if you had to re-apply: `scripts/git/push.sh "fix(server): re-assert UpdatedAt stamping"`.
- Purpose: guarantees Owner/Pet already replicate before we extend the materializer.

### [x] T2 — Extend `ReplicaMaterializer` to project the inventory/sales/clinical entities (Root Cause A) — STATUS: ✅ done (22 new entity maps incl. Product+variants, Visit, BoardingStay items; Tenant/DocumentReference/billing-handoffs deliberately deferred, noted in class doc)
- File: `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs`.
- Pattern to copy: each entity is one `new EntityMap(wireName, localTableName, [Col(...), ...], optional searchKey)`
  in the static `Maps` array. `Col(Name, Kind, Required)` where `Kind ∈ {Text, Int, Bool, Time}`. The upsert
  SQL + tombstone SQL are auto-built; server bookkeeping (`UpdatedAt/Version/Deleted`) is copied verbatim.
  Column **JSON** name is auto-camelCased from `Name`. `Required` columns missing in payload ⇒ row skipped.
- **Add maps for (priority order, these unblock the drug+invoice flow + general cross-client sync):**
  `UnitOfMeasure`, `ProductCategory`, `Product`, `Supplier`, `Insurer`, `LedgerAccount`, `AccountingPeriod`,
  `WeightEntry`, `MedicalHistoryEntry`, `VaccinationRecord`, `VaccineCatalogItem`, `Visit`, `BoardingStay`,
  `Room`, `BoardingServiceDef`, `TreatmentPlan`, `TreatmentOccurrence`, `StaffMembership`, `RoleDefinition`.
  (Full server list = `SyncContractCatalog.ProjectionOrder`. `Tenant` is special — see note below.)
- **How to get exact columns for each entity (do NOT guess):** read the entity's SQLite configuration under
  `src/Animora.Persistence.Sqlite/Configurations/**` and the domain class under `src/Animora.Domain/**`.
  The local **table name** = the one used in that `*Configuration.cs` (`ToTable`/`builder.Metadata`), e.g.
  Product→`Products`, UnitOfMeasure→`UnitsOfMeasure`, AccountingPeriod→`AccountingPeriods`. Map every
  persisted scalar column (skip nav collections / child tables — those are separate wire entities or child
  descriptors). Mark genuinely non-null domain fields as `Required: true`.
  - Known gotcha: `Product` maps to table `Products`; its child `ProductVariants` is a **separate** concern.
    The server descriptor lists `ProductVariants` as a child with root column `OwnerId`. For a first correct
    pass, project the **root `Product`** (fields incl. `Sku`, `Name`, `Brand?`, `CategoryId`, `Kind`(int),
    `UnitCode`, `IsPrescriptionOnly`(bool), `IsActive`(bool), `TrackStock`(bool) — confirm against the config).
  - `SearchKey`: only Owner/Pet currently compute one. For new entities, only add a `searchKey` lambda if the
    local table actually has a `SearchKey` column (check the config); otherwise omit (null) — the builder then
    skips it. Do **not** invent a SearchKey column.
- **Child entities** (wire entities that are children of a root, e.g. `VisitRxLines`, `BoardingStayItems`):
  the server contract lists them as `SyncChildDescriptor`s, not top-level `ProjectionOrder` names. The client
  materializer is keyed by top-level wire entity name. For this pass, materialize the **top-level** entities
  listed above; if a typed local table for a child is required for the drug/invoice flow and arrives under its
  own wire entity name, add it too. Keep scope to what's needed; note any deferral inline in the code comment.
- Update the XML doc summary of the class to reflect the broadened surface (it currently says
  "reception cluster: Owner, Pet, Appointment, ReceptionQueueEntry").
- **Verify**: the `Entities`/`SupportedEntities` array (derived from `Maps`) now includes the new names; the
  `.Where(... Entities.Contains(x.Entity))` filter will now admit them.
- This is a sizeable change → **push with a clear message**:
  `scripts/git/push.sh "fix(sync): materialize inventory/sales/clinical entities on pull so records sync across clients"`.

### [ ] T3 — Tenant switch on login: rebind + wipe (Root Cause B, the clinic A/B isolation bug) — STATUS: pending
- **Goal**: logging in as a server tenant different from the locally bound tenant must NOT show the old
  clinic's data; each clinic's data is isolated and preserved-for-its-own-user only, securely.
- **Design (standard, matches single-tenant-per-install):**
  1. Add a rebind capability to the local installation:
     - `LocalInstallation` (`Entities/LocalInstallation.cs`): add an `internal void BindTenant(string tenantId)`
       that sets `TenantId` (private setter is in this class, so add the method here). Keep it internal.
     - `LocalInstallationState`: add method to update the cached row's tenant and the singleton
       (e.g. `void Rebind(LocalInstallation updated)` or reuse `Set`). Keep thread-safe (`volatile` field already).
  2. Add a reconcile step invoked **after a successful online login** (where `login.TenantId` is known).
     Best seam: `OnlineAuthClient.LoginAsync` success path (after session is written), OR a small new service
     called from `AuthenticationService`/the login VM right after `SignInAsync`/online login succeeds.
     Prefer a dedicated, testable service, e.g. `LocalTenantBinder` in `Animora.Client.Services/Identity/`,
     with method `Task EnsureBoundToAsync(string serverTenantId, CancellationToken ct)`:
       - Read current `LocalInstallationState.TenantId`.
       - If it already equals `serverTenantId` → no-op.
       - If current is `T-LOCAL-DEFAULT` (never bound to a real tenant) → **adopt**: persist the new tenant on
         the `LocalInstallation` row (`BindTenant` + `SaveChanges`), update the singleton. (No wipe needed: a
         first-run install on `T-LOCAL-DEFAULT` only ever held this user's own setup data; but if seed/sample
         data under `T-LOCAL-DEFAULT` would leak, also clear business tables — see next bullet — to be safe.)
       - If current is a **different real tenant** (clinic A) and `serverTenantId` is clinic B → **switch**:
         (a) clear all tenant-scoped business + replica data on disk for the old tenant, (b) rebind the
         installation row to `serverTenantId`, (c) reset the sync cursor so a full re-pull hydrates clinic B.
  3. **Clearing data — reuse existing primitives, do not hand-roll fragile SQL:**
     - `ILocalStoreMaintenance` already has quarantine/rebuild/vacuum and per-tenant delete helpers; review it.
       The cleanest, safest "tenant switch" is to **rebuild the local store** (quarantine-and-rebuild drops the
       data file and recreates an empty schema) OR to run a targeted wipe of every tenant-scoped table +
       `repl_*` staging + sync cursor. Prefer whichever already exists and is transactional. If a full rebuild
       is too broad (it also drops `LocalInstallation`), implement a scoped wipe:
       delete rows where `TenantId = oldTenant` across business tables, clear `repl_base`/`repl_*` staging,
       clear the sync cursor (`SyncCursorStore`), then rebind. Keep it in ONE transaction where possible.
     - Security note to honor: the old clinic's rows must be **physically removed**, not merely filtered, so a
       later query-filter bug can't expose them. This is why we wipe on switch rather than keep multi-tenant data.
  4. **Also handle offline login** (`OfflineSessionAuthorizer` path returns a `TenantId`): the same
     `EnsureBoundToAsync` should run so an offline re-login as a previously-bound tenant is consistent. An
     offline login for a *different* tenant with no local mirror should already be rejected upstream; verify.
- **Edge cases to respect (verified facts):** `CompleteFirstRunSetupCommand` uses `Tenant.LocalDefaultId`
  (`T-LOCAL-DEFAULT`) — a fresh install’s first-run owner setup lives under the default tenant until first
  online login adopts the real tenant. Ensure adopt-path migrates/clears correctly so first-run local setup
  doesn’t masquerade as clinic data under the wrong tenant.
- **Verify**: trace once in your head: install → login clinic A (adopt A, pull A) → sign out → login clinic B
  (switch: wipe A, rebind B, re-pull B). UI must show only B. Re-login A later → wipe B, rehydrate A from server.
- This is a heavy change → **push with a clear message**:
  `scripts/git/push.sh "fix(identity): rebind local store to the authenticated tenant on login and wipe the previous clinic's data (per-clinic isolation)"`.

### [ ] T4 — Add "Sign out" button in the user's Settings/Profile page (Root Cause C) — STATUS: pending
- UI lives in `src/Animora.Desktop/Pages/Profile/` (`ProfilePageView.axaml`, `ProfilePageViewModel.cs`).
  Confirm Profile is the user-facing "تنظیمات"/account area; if the app routes "Settings" elsewhere, place the
  button where the user manages their account. (Profile is the correct, verified location.)
- **ViewModel**: inject `IAuthenticationService` (already DI-registered via `AuthenticationService`). Add an
  async command `SignOutCommand` that calls `await _auth.SignOutAsync("user_requested", ct)` then raises an
  event / invokes a callback that the app shell handles to return to the login window.
- **Shell transition back to login** (reverse of `App.axaml.cs` `LoginSucceeded`): there is currently only
  login→shell. Implement shell→login:
  - Simplest robust approach consistent with the existing lifetime model: after `SignOutAsync` completes,
    construct/show a fresh `LoginWindow` as `desktop.MainWindow`, wire its `LoginSucceeded` again (reuse the
    same handler used at startup — factor it into a reusable method in `App.axaml.cs`), and close the shell
    window. Ensure the capability gate is cleared (SignOutAsync already calls `gate.Clear()`).
  - Keep DI scopes clean: resolve `LoginWindow` from `Services` (same as startup). Avoid leaking the old shell.
- **XAML**: add a clearly-labeled button "خروج از حساب" (Persian UI) in the Profile page, bound to the command,
  styled consistently with existing buttons on that page. Confirm the AV binding compiles (x:DataType VM).
- **Verify**: button visible in Profile; clicking signs out, returns to login; logging back in builds a fresh
  shell. No orphaned windows.
- Push: `scripts/git/push.sh "feat(desktop): add sign-out button in profile/settings returning to login"`.

### [ ] T5 — Integration sanity for Task 1 ("offline adds sync when back online") — STATUS: pending
- No full test runs (user constraint). Instead do a **targeted reasoning + light verification** pass:
  - Confirm the offline command pipeline already queues commands in SQLite and the scheduler flushes on
    reconnect (`SyncSchedulerHostedService`, `HttpCommandTransport`). These are pre-existing and working per the
    report (push `outcome:0` observed). The *gap* was pull-side materialization (fixed in T2).
  - Confirm that for each entity you added in T2, the push path (command → server) already exists (it does per
    `SyncContractCatalog` + the report's successful pushes). The user's complaint is purely the pull/visibility
    side, which T2 fixes.
  - If CI (GitHub Actions) is green after T2–T4 pushes, that is the acceptance signal. If a unit test for
    `ReplicaMaterializer.SupportedEntities` exists, make sure it’s updated to match the new list (search tests).
- If you touched tests/added a small test, push:
  `scripts/git/push.sh "test(sync): cover extended materializer entity set"`. Otherwise no-op (no push).

### [ ] T6 — Open/refresh the PR to `genspark_ai_developer` and share the link — STATUS: pending
- The push script auto-pushes the current branch. Ensure work is on `genspark_ai_developer`
  (use `scripts/git/push.sh -b genspark_ai_developer "..."` on the first push if needed), synced with
  `origin/main`. Then confirm/open the PR to `main` and give the user the PR URL.
- (CI runs build+tests automatically on the PR — do not run them locally.)

### [ ] T7 — FINAL: delete this plan file — STATUS: pending
- `git rm Plan/sync-and-tenant-isolation-fix.md` (keep `Plan/.gitkeep`), then
  `scripts/git/push.sh "chore(plan): remove completed sync+isolation plan"`.
- Do NOT touch any other file or roadmap in this final step.

---

## 3. Guardrails (apply throughout)
- Targeted file reads only — the exact files are listed in §0; don't trawl the repo.
- Standard, idiomatic fixes (follow the patterns already in each file). No hacks, no bypasses.
- **Push after every task** via `scripts/git/push.sh` (locked). Heavy changes (T2, T3) must be pushed with a
  clear, descriptive message. If push exits code 3 → call `setup_github_environment` once, then re-run.
- No full local build / long test runs — CI is the gate.
- Persian UI strings where user-facing (e.g. logout button).
