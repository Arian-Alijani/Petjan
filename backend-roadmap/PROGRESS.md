# Progress & Project State — Animora Backend & Server

> **Read this file first, every session — but only the part you need.** It is the single source
> of truth for "what already exists" and "what to do next", and it grows by one entry per completed
> phase (currently >1000 lines). **Every session reads the section up to and including "Phase status"
> below in full** (principles + current state + Next phase + status table — short, ~115 lines). **Never
> read "Repo state log" or "Decision & deviation log" end-to-end.** Those are per-phase archives: jump
> straight to the one `### <PhaseID>` entry a phase file's "Read before starting" cites, by `grep -n
> "^### <PhaseID>"` or an editor search — never scroll past it. Update this file at the end of every
> phase (append one new entry; never rewrite/shorten an old one). Never skip the update step.

## Standing principles (in force in EVERY phase, never traded off)

1. **Offline-first correctness.** The desktop must keep full functionality with the server unreachable,
   on a real local database. Local state may be provisional; it is never lost, never silently
   overwritten, and never invented. The server is the single global authority for settlement.
2. **Backend-ready clean layering.** Domain ← Application ← Infrastructure/Presentation, dependencies
   inward only. No EF Core / Datasync / SQL / HTTP type ever appears in a Desktop View or ViewModel. No
   client code in a server project or vice versa. (`CONVENTIONS.md` §1–§2.)
3. **Runnability.** Through the end of Track C, `scripts/sandbox/build.sh full` = 0 warnings / 0 errors
   and `scripts/sandbox/run.sh` = SMOKE RUN OK on a machine with no server, no PostgreSQL and no
   network. From Track D on, the desktop must still start and operate offline. (`CONVENTIONS.md` §11.)
4. **No mock left behind.** Every `Animora.Contracts` interface ends this roadmap with a real
   implementation; every `Mock*` file is deleted in the phase that replaces it. No bounded context ever
   keeps both. (`CONVENTIONS.md` §10; audited in C11.)

> These restate — never replace — `CONVENTIONS.md` and `ROADMAP.md`. If wording differs, those govern.

## Current state

> **This table reflects the state as of the last completed phase (see "Next phase" below) — it is
> updated every phase, never left stale.** For per-phase detail, jump to that phase's `### <ID>` entry
> in "Repo state log" (do not read the whole log).

| Area | State |
|---|---|
| `src/Animora.Desktop/` | **Complete; Track C mock purge and offline acceptance finished (C01–C11 done; reviewer UI/performance follow-up documented below).** 17 screens, ~300 `.cs` files, shell/nav/dialogs/toasts/theme/localization. `App.axaml.cs` is the composition root (DI). Now on the real backend: Identity (B03c), **Patients (C01)**, **Reception + Visits triage (C02)**, **Visit encounter + Vaccination (C03)**, **Appointments (C04)**, **Boarding (C05)**, **Inventory + Purchasing (C06)**, **Sales / POS (C07)**, **Cashier + Cheques (C08)**, **Finance (C09)**, **History + Dashboard + Global search + Profile (C10)** — the matching `IPatientsQueryService`/`IReceptionQueueService`/`IVisitsTriageService`/`IVisitEncounterService`/`IVaccinationService`/`IAppointmentsQueryService`/`IAppointmentBookingService`/`IBoardingQueryService`/`IInventoryQueryService`/`IPurchasingQueryService`/`ISalesQueryService`/`ICashierService`/`IChequesQueryService`/`IFinanceQueryService`/`IHistoryQueryService`/`IDashboardQueryService`/`IGlobalSearchService`/`IUserProfileService` are all bound to their real implementations, `IStoreReadinessGate` registered, and `FinanceProjectionWorker` started after store init (C09). Every Track C screen is off its mock. |
| `src/Animora.Contracts/` | 24 public interfaces audited in C11 + DTOs, one folder per bounded context. The frontend↔backend seam. B03c added the auth seam (`Identity/IAuthenticationService`, `AuthOutcome`, `FirstRunRequest`) and appended `UserRole.Receptionist`; still references no `Animora.*`. |
| `src/Animora.Desktop/Mocks/` | **Removed entirely by C11.** `MockSyncStatusService.cs` deleted and replaced by real `LocalSyncStatusService`; Track C mock purge complete (22 of 22). The historical per-phase deletion list is: `MockCurrentUserContext` deleted by B03c; **`MockPatientsQueryService` deleted by C01**; **`MockReceptionQueueService` + `MockVisitsTriageService` deleted by C02**; **`MockVisitEncounterService` + `VisitEncounterStore` + `MockVaccinationService` deleted by C03**; **`MockAppointmentsQueryService` + `MockAppointmentBookingService` + `AppointmentStore` deleted by C04**; **`MockBoardingQueryService` deleted by C05**; **`MockInventoryQueryService` + `MockPurchasingQueryService` deleted by C06**; **`MockSalesQueryService` deleted by C07**; **`MockCashierService` + `MockChequesQueryService` deleted by C08**; **`MockFinanceQueryService` deleted by C09**; **`MockHistoryQueryService` + `MockDashboardQueryService` + `MockSearchService` + `MockUserProfileService` deleted by C10**. |
| `src/Animora.Domain/`, `src/Animora.Application/` | **Real code**, not placeholders — built through Track A (A01–A12: kernel, identity, patients/clinical, scheduling/boarding, inventory/purchasing, sales/cashier, finance) and B01–B02 (application dispatch pipeline, command journal/outbox). `Animora.Domain.csproj` still has **zero** project/package references by design. |
| `src/Animora.Persistence.Sqlite/`, `src/Animora.Infrastructure.Local/` | **Real code.** Local encrypted SQLite store: DbContext, **31 migrations (`001`–`031`; physical head `031_TreatmentPlans` — `020`–`031` are post-C11, see "Post-C11 drift" below)**; **the migration-number enumeration lives ONLY in "Post-C11 drift" below (single source of truth — audit 2026-10-08 · RM-H-01; never pick a migration number from this row)**; store contents: C06 `InventorySnapshot`, C07 `SalesInsurers`, C08 `CashSessionLink`, C09 `FinanceProjections` (`DailyFinanceFact` + `ProductSalesFact` + `OwnerFinancialSnapshot`), C10 `ActivityEvent` + `UserPreference`, C11 `SyncCursor` (tenant/scope last-synced marker), C03/C05 schema, UiDraftState, PatientsSearchKeys, StaffWorkingHours; repositories (+ `MedicalHistoryRepository`), seed (clinical/reception/appointment/boarding/inventory/sales + `FinanceSampleData`, C08; banks; `RebuildFinanceProjectionsAsync` + `RebuildActivityFeedAsync` run at end of seeding, C09/C10), read queries (patients/reception/triage/rx/vaccination/appointments/boarding/inventory/purchasing/sales + `CashierQueries`/`ChequeQueries`, C08 + `FinanceQueries`, C09 + `HistoryQueries`/`DashboardQueries`/`GlobalSearchQueries`/profile queries, C10), the finance allocators (`InvoiceNumberAllocator`, `JournalNumberAllocator`, `ExpenseNumberAllocator`) + `FinanceProjectionStore`/`FinanceProjectionRebuilder`, `FinanceProjectionWorker` (Infrastructure.Local, drains the B02 outbox off the OLTP path, C09), maintenance/backup/quarantine (+ `RebuildStockSnapshotsAsync`, `RebuildFinanceProjectionsAsync`), OS keystore, clocks, append-only local audit sink, buffered/redacted rolling file logging, and PII-free diagnostics bundle export. Wired into Desktop by B03c (`AddAnimoraLocalInfrastructure()`; store startup runs async after the login window opens). |
| `src/Animora.Client.Services/` | B03c identity (`Identity/AuthenticationService`, `Identity/CurrentUserContextService`, `Mapping/RoleMapping`). **C01 patients:** `Patients/PatientsService` + `Patients/PatientsMapping`; `Diagnostics/StoreReadinessGate`. **C02:** `Reception/ReceptionQueueService`, `Visits/VisitsTriageService` (+ mappings, `VisitTypeCatalog`). **C03:** `Visits/VisitEncounterService` + `Visits/VisitEncounterMapping` + `Visits/DebounceBuffer` (trailing 600 ms note debounce, flushed on complete/navigate), `Vaccination/VaccinationService` + `Vaccination/VaccinationMapping` + `Vaccination/SmsTemplateRenderer` (token substitution, **no delivery**), `Caching/TenantScopedCache` (bounded LRU keyed by tenant+user+resource+version). **C04:** `Appointments/AppointmentsQueryService`, `AppointmentBookingService` + mapping. **C05:** `Boarding/BoardingQueryService` + mapping. **C06:** `Inventory/InventoryQueryService`, `Purchasing/PurchasingQueryService` + `InventoryMapping` + `PersianFormat`. **C07:** `Sales/SalesQueryService` + `SalesMapping` (22 members; posting is a journaled command). **C08:** `Cashier/CashierService` + `CashierMapping` (reuses C07's commands verbatim, no second invoice path), `Cheques/ChequesQueryService` + `ChequesMapping`. **C09:** `Finance/FinanceQueryService` + `FinanceMapping` (reads the C09 fact tables only); `Patients/PatientsMapping` now reads `OwnerFinancialSnapshot.TotalSpendMinor` (**C01's deferred `TotalSpend` closed**). **C10:** `History/HistoryQueryService` + `CsvWriter` + `HistoryText`, `Dashboard/DashboardQueryService` + `DashboardMapping` (role-gated widgets), `Search/SearchService`, `Profile/UserProfileService` (settings survive restart; save is non-journaled) + mappings. All are `sealed`, scope-per-call over the dispatcher, no EF/SQL/rule. **C11:** `Sync/LocalSyncStatusService` replaces the final mock using Application cursor/journal ports, scope-per-call reads, serialized processor passes and cancellation handling. `DraftStateStore` now delegates to `IUiDraftStore` (no EF reference). **Every Track C surface is now on the real backend; all 24 Contracts registrations machine-audited.** |
| `src/Animora.Sync.Abstractions/` | Still an **empty scaffold** (csproj + README only) — Track E deliverable. |
| `src/Animora.Server/` | **S11–S13 authored: sync manifest/batches/pages/snapshots, transactional outbox command adapter, five dedicated-pool workers, non-fatal heartbeat readiness and tenant SignalR hints; no execution acceptance claim.** **S07–S10 implementation authored (S09 sales/cashier 21 wire types, S10 inventory/purchasing/finance 23 wire types, in-transaction ledger posting); S07/S08: 39 wire types / 40 payloads, real PG ports/versions, optimistic scheduling fences, generated-id mapping; build/test/smoke unexecuted.** **S01 + S02 + S04 + S05 + S06 done (2026-10-09; S06 adds `Commands/` — command intake/idempotency service, payload catalog, recovery, `POST /api/commands`, `/batch`, `GET /{operationId}/ids`; S05 adds `Authorization/` (tenant binding, stored-RBAC policies, entitlement slot) + `Signing/` (ECDSA-P256 snapshot signer) + `DeviceEndpoints` (`/auth/device/{id}/approve|revoke`, `/auth/capabilities`, anonymous `/auth/keys`); S04 adds `Auth/` — `AddAnimoraIdentity`, `AuthEndpoints` (`POST /auth/login|refresh|logout|device/register` under the `auth` rate limit), internal `LoginService`, opaque Data-Protection bearer tokens (`AddBearerToken`, no JWT/OIDC), `UseAuthentication()` in the authN slot; authorization/tenant-context middleware stay S05); S02 wires DataProtection → PostgreSQL key ring, `AddAnimoraPostgreSql`, and `database`/`keyring`/`schema` readiness probes; host still boots without PostgreSQL, readiness = 503).** S01: ASP.NET Core 10 host scaffold — numbered startup block, validated options, `SecretGuard`, `GlobalExceptionMiddleware`, anonymous `/health/live` + `/health/ready`, five rate-limit policies, Development-only OpenAPI. No DB / identity / business route yet (S02+). |
| `src/Animora.Persistence.PostgreSql/` | **S11–S13 authored: Replication/ and Outbox/, head `111_ServerOutbox` (109–111 authored/unapplied), 6/3/1/2 worker-inclusive pools.** **S07–S10 implementation authored: clinical, scheduling/boarding, sales/cashier, money/stock mappings/ports; head `108_MoneyStock` (105–108 unexecuted).** **S02 + S03 + S04 + S06 done (2026-10-09; S06 adds `CommandIntake`/`CommandIdMap` + `Commands/` store/compactor (migration `104_CommandIntake`); S04 maps the Domain `User/StaffMembership/RoleDefinition/Device` aggregates + `RefreshTokens` + append-only `SecurityAudit` (migration `102_Identity`, `Tenants.Code`), `Identity/` stores/services (`ServerUserStore`, `TenantDirectory`, `RefreshTokenService`, `SecurityAuditWriter`, `TenantProvisioner`); S03 adds ServerUnitOfWork, TenantRepository<T>, PageRules, read-context factory):** the only project that references Npgsql/EF-Npgsql. `AnimoraServerDbContext` (`Tenants`), `AnimoraKeyRingDbContext` (`DataProtectionKeys`, history table `__EFMigrationsHistory_KeyRing`), tenancy (`ITenantContext`, stamping + ignore-filter-ban interceptors), `ServerModelConventions`, `VersionTrigger`, `DatabaseOptions` + validator, `AddAnimoraPostgreSql`. Migrations `101_ServerFoundation`, `101_KeyRing`. |
| Licensing / admin web | S14 operator identity/API and subscription implementation authored; S15 signed onboarding intake authored; licensing S16 next. Admin UI remains S19. Execution gates unverified. |
| Login | **Real local authentication (B03c).** `LoginViewModel` → `IAuthenticationService`; disabled until the local store is ready; first run shows `FirstRunView` (owner setup) and opens the shell. No hardcoded credentials, no demo user seeded. (`LoginWindow.axaml` still shows the old demo hint text — untouched by design.) |
| `Directory.Packages.props` | Avalonia 12.1.2 (+Desktop/Fluent/Inter/DataGrid), CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 10.0.12, Serilog 4.2.0 + Extensions.Logging 9.0.0 + Sinks.File 6.0.0, plus the Track A/B backend pins (EF Core Sqlite 10.0.12, xunit, etc. — see A01 entry). `ManagePackageVersionsCentrally=true`. |
| `Directory.Build.props` | `net10.0`, `LangVersion 14.0`, `Nullable enable`, `ImplicitUsings enable`. |
| Solution | `Animora.sln` — 9 `src/` projects + 5 `tests/` projects (see `AGENTS.md` for the full `src/` map). |
| Scripts | `scripts/sandbox/build.sh [fast\|full\|release\|restore]`, `run.sh` (Xvfb smoke, prints `SMOKE RUN OK`), `test.sh` (added by A01), `install-prereqs.sh`, `doctor.sh`, `bench.sh offline` (six-metric report + >2x failure threshold; evidence/limitations in `measurements/C11-offline-budget.md`). |
| Frontend history | `windows-frontend-roadmap/PROGRESS.md` (phases 01–29 done). Its `phases/**` must never be read. |

## Post-C11 drift (baseline correction — read before picking any migration number)

> Added 2026-10-07 from `docs/Phase-Problem.md` §3. The status table above is accurate **as of C11**;
> the repository kept moving through audit/perf/feature passes that were not tracked as roadmap
> phases. Forward phases (D/E) MUST read this section as the true baseline.

**Migration numbering rule (binding for every remaining D/E phase).** Never hard-code a client
migration ordinal from this file's older text. At execution time, **list the physical files in
`src/Animora.Persistence.Sqlite/Migrations/` and take the next free ordinal on the owning
`DbContext`** (the physical head is the highest `NNN_` file, not a count). A migration generated from
a *different* context (e.g. E03's `AnimoraOfflineDbContext`) is numbered on **that** context's own
sequence. All ordinals `001`–`031` exist physically (including `006_CapabilitySnapshot` and
`013_InventorySnapshot`); `020`, `022` and `024`–`031` have a **double** underscore before the ordinal in the file
name (e.g. `20261007120000__031_TreatmentPlans.cs`) — match on the `NNN` token, never on a fixed
prefix pattern.

| # | Migration | What it did | Source |
|---|---|---|---|
| 020 | `OutboxActorProvenance` | `OutboxMessage.ActorUserId` column | `Plan/code-audit-2026-10-07-fixes.md` |
| 021 | `DashboardGeneration` | `DashboardGenerations` table (cache-generation counter) | dashboard audit |
| 022 | `DashboardWidgetIndexes` | 4 perf indexes (visits/vaccination/reception/history) | dashboard audit |
| 023 | `PatientsPerfIndexes` | weight/birth-date/registered-at indexes | `Plan/patients-directory-perf.md` |
| 024 | `BoardingPaymentMode` | `PaymentMode` column on boarding | boarding/sales/cashier audit |
| 025 | `ActivityResolvedDetail` | `DetailText` + `SearchText` on activity feed | history audit |
| 026 | `VariantReorderScaled` | `ReorderLevelScaled` fixed-scale column | `Plan/inventory-product-history-audit.md` |
| 027 | `PersianCategoryNames` | data-only: Persian category names | localization |
| 028 | `PersianBoardingServiceTitles` | data-only: Persian boarding titles | localization |
| 029 | `RecurringExpenses` | **new aggregate** `RecurringExpense` (+ `RecurringExpensePayment`) | finance |
| 030 | `BoardingPrepaidRoom` | `Kind` + `PrepaidInvoiceId` on boarding handoff | boarding/sales/cashier audit |
| 031 | `TreatmentPlans` | **new aggregates** `TreatmentPlan`, `TreatmentOccurrence`, `VaccineCatalogItem` | `Plan/visits-petfile-audit.md` |

**Other post-C11 facts a forward phase must not miss**
- **New aggregates (not in D08's original replicated set):** `Animora.Domain.Clinical.TreatmentPlan`
  (+ occurrences, vaccine catalog items) and `Animora.Domain.Finance.RecurringExpense` — D08 records the
  decision (see its replicated-surface note).
- **New command families:** `vaccination.treatment.*` (4) and `finance.recurring.*` (4);
  `CommandTypes` is now **86** types (D06 = 39 non-financial, D07 = 47 money/stock). The live
  `CommandTypes.cs` is the single source of truth — never a roadmap table.
- **New desktop module:** *Statistics* (`src/Animora.Contracts/Statistics/*`, `Pages/Statistics`) — not
  covered by `AGENTS.md`; no server/replication surface.
- **`ErrorCodes`:** 161 declared codes (incl. `TREATMENT_PLAN_NOT_FOUND`, etc.).
- **Audit/perf work shipped without a phase:** `Plan/code-audit-2026-10-07-fixes.md` (F-01..F-08),
  `Plan/inventory-product-history-audit.md`, `Plan/patients-directory-perf.md`,
  `Plan/visits-petfile-audit.md`, `docs/*_AUDIT_*.md`. They changed behaviour but no roadmap phase file.
- **Roadmap-consistency fixes applied 2026-10-07** (from `docs/Phase-Problem.md`): E03/E05/E07
  migration collisions resolved (E03 split across two contexts); D06/D07 handler inventories rebuilt
  from the live `CommandTypes`; D13/E02/E01 path & rate-limit-policy drift corrected.

## Maintenance drift ledger (consult before any D/E phase)

> Added 2026-10-08 (audit · IMP-02). Out-of-roadmap sources that record real drift against the C11
> baseline. A forward phase MUST read these before authoring/executing, so it starts from the true
> state rather than the C11 snapshot. This is the single index that replaces re-discovering drift.

| Source | Drift it records | Status |
|---|---|---|
| `backend-roadmap/PROGRESS.md` → "Post-C11 drift" (above) | migrations `020`–`031`, 2 new aggregates, 86 `CommandTypes`, Statistics module, next-free-ordinal rule | authoritative baseline |
| `docs/Phase-Problem.md` | migration-id collisions, handler-inventory rebuild, path/rate-limit drift | ✅ resolved 2026-10-07 (do not re-flag) |
| `docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md` | second-order sync-design + roadmap gaps (SYNC-C/H/M, SRV-M, PH, RM-H, CODE, OPT, IMP) | carried into D/E phase files 2026-10-08 (tagged `audit 2026-10-08`) |
| `Plan/*.md` audits, `CODE_AUDIT_REPORT_2026-10-07.md`, `docs/*_AUDIT_*.md` | perf indexes, new columns/aggregates, code debts (ct/clock/`.Result`/leaks) | historical; code debts gated into owning D/E phases |

**Verified-no-action items (recorded so they are not re-flagged):**
- **PH-07** — `PHASE-C03` phase file is a reconstructed historical stub only; acceptable (C03 is Done),
  the stub + `ROADMAP.md` note exist. No change.
- **PH-08** — `identity`→`auth` rate-limit-policy name already corrected in text. At D13/E01 execution,
  verify only `auth, commands, tables, files, operator` policies exist. No change now.

## PostgreSQL architecture revision — 2026-10-08

User-authorized replacement of the future server SQL Server baseline with PostgreSQL 18 / Npgsql 10 for a 2–4 GiB, 1–3-core host. ARCHITECTURE §PG / §16-CT–§18-CT govern the commit-safe tenant journal, UUID Version, REPEATABLE READ immutable exports, resource/period/outbox locking, mappings, pool budgets and WAL recovery. D/E recipes and STACK are aligned. SQLite and completed local migration history are unchanged. **No server/runtime/data-transfer test is claimed; no D/E phase is marked done.** Mandatory PostgreSQL CI and performance/restore gates remain implementation work. Historical completed-phase entries below retain their original technology wording.

This revision aligned `ARCHITECTURE.md`/`STACK.md`/`CONVENTIONS.md`/`ROADMAP.md` on PostgreSQL. It made NO real PostgreSQL integration/load/restore acceptance claim, and changed no production C# code, package pins, solution or local migration. Source importer/history remain intact. Mandatory PostgreSQL CI and performance/restore gates remain implementation work.

## Server-track reset — 2026-10-08 (supersedes the D/E phase files)

User-authorized reset of the **remaining (unbuilt) backend work**. Tracks A–C are complete and
untouched. The old `D01`–`D13` and `E01`–`E09` phase files were authored across mixed/pre-PostgreSQL
baselines and had accumulated drift against the current architecture; **all 22 were deleted** together
with the now-obsolete `scripts/validate-postgresql-architecture.py` (it only asserted text inside those
deleted files). **No server/sync/admin code ever existed** — only planning files were removed, so the
solution, migrations and Tracks A–C are byte-identical. The remaining server+sync+admin work is now
regenerated from the current PostgreSQL / .NET 10 baseline by a single **meta-phase `M00`** (see
`phases/PHASE-M00-server-track-regeneration.md`): it holds a resumable checkbox TODO list whose tasks
each author a small batch of fresh, baseline-accurate server/sync/admin phase files. Phase IDs for the
regenerated work use the **`S##`** (server) / `G##` (sync/integration/release) families to avoid any
confusion with the deleted `D`/`E` files. The regenerated phases — not `M00` — carry the production
server implementation; `M00` only produces them.

## Next phase

**Next phase: `S16`** — licensing and anti-piracy. S15 signed onboarding intake sources authored (head `113_ProvisioningIntents`); execution gates deferred. S14 implementation/regression sources authored (head `112_Subscription`); builds, tests, smoke and PostgreSQL application remain unexecuted by explicit user directive.
`S06` (command intake & idempotency) is **complete** (2026-10-09): every command is deduplicated by `(TenantId, OperationId)` in `CommandIntake`, identity comes only from the authenticated tenant context, the server recomputes fingerprints, settlement is atomic with the effect, and with no handler registered every command settles `Rejected(HANDLER_NOT_REGISTERED)`. Do its successor's work and advance down the `S##`/`G##` chain in dependency order.
The desktop stays offline-runnable throughout (every phase carries "Runnable after: desktop offline yes").

**Track C complete** — real server-free local baseline, all 24 Contracts registrations audited,
32 final focused regressions passed, and networking-disabled GUI smoke recorded. Full build/full suite
were intentionally replaced by targeted verification per user directive. Manual authenticated 17-screen
review, rendered-dashboard/authenticated-shell measurements, and cold-start budget remediation remain
explicit follow-ups, not completed acceptance claims. C06's historical T12 test-class debt remains a
separate outstanding item; C11 stock/UoW regressions do not close that debt.

## Phase status

| ID | Status | Date | Notes |
|---|---|---|---|
| A01 | ✅ Done | 2026-09-23 | Backend track scaffold: 4 src + 5 test projects, 10 pins, WireContract v1, test.sh, LayeringRulesTests. Build 0/0, TESTS OK, SMOKE RUN OK. |
| A02 | ✅ Done | 2026-09-23 | Domain kernel: 15 files (primitives, Money/Quantity, ULID IdGenerator, clock ports, Result/ErrorCodes seed 11). 4 test classes, 24 domain tests. Build 0/0, TESTS OK, SMOKE RUN OK. |
| A03 | ✅ Done | 2026-09-23 | Identity spine: 11 files in `Domain/Identity` (Tenant, User, StaffRole, Permission, PermissionCodes 42, RoleDefinition, DefaultRolePermissions, StaffMembership, Device, CapabilitySnapshot, OfflineCapabilityPolicy). ErrorCodes += 6. 3 test classes (+21 facts → 45). Build 0/0, TESTS OK, SMOKE RUN OK. |
| A04 | ✅ Done | 2026-09-23 | Owners/patients/clinical core: 12 files (`Domain/Patients` 4, `Domain/Clinical` 7, `Events/ClinicalEvents.cs`). Visit state machine + invoice-handoff lines, derived vaccination due dates, append-only medical history. ErrorCodes += 7 (→ 24). 4 test classes (+45 facts → 90). Build 0/0, TESTS OK, SMOKE RUN OK. |
| A05 | ✅ Done | 2026-09-23 | Scheduling & boarding domain: 12 files (`Domain/Scheduling` 5, `Domain/Boarding` 5, `Events/SchedulingEvents.cs`, `Events/BoardingEvents.cs`). Appointment state machine + `SlotConflictPolicy` (pure, `for`-loop), reception derived-queue, boarding stay state machine + `BillableDays` + invoice-required checkout. ErrorCodes += 11 (→ 35). 8 events. **No tests (owed to A12).** Build: Domain 0/0 (solution-wide OOM only in Avalonia Desktop XAML AOT — sandbox RAM cap, not code). |
| A06 | ✅ Done | 2026-09-23 | Inventory & purchasing domain: 13 files (`Domain/Inventory` 6, `Domain/Purchasing` 5, `Events/InventoryEvents.cs`, ErrorCodes). **Movement-based stock only** — `Product` has no on-hand property; `StockCalculator` is the single derivation (signed sum + moving weighted average); `StockMovement` is append-only with a factory-enforced sign rule; `StockLevel` is a rebuildable projection. PO state machine + `RecalculateTotals`/`ApplyReceipt`; posted `GoodsReceipt` immutable (A-RULE 002). ErrorCodes += 11 (→ 46). 4 events. **No tests (owed to A12).** Build full 0/0, SMOKE RUN OK. |
| A07 | ✅ Done | 2026-09-23 | Sales & cashier domain: 9 new files (`Domain/Sales` 6, `Domain/Cashier` 2, `Events/SalesEvents.cs`) + ErrorCodes. `Invoice` aggregate with the 6-state machine and **immutability after posting** (all 5 mutators → `INVOICE_IMMUTABLE`, A-RULE 002); `InvoiceTotals` is the single totals algorithm (invoice discount pro-rated by weighted largest-remainder before tax, `Money.ApplyPercent` the only rounding); `Payment` append-only with factory-validated method references and `OperationId` idempotency (`PAYMENT_DUPLICATE_OPERATION`, A-RULE 006); `InvoiceAdjustment` the only post-posting economic change path; `SettlementStatus` (8 states, §8.3) defined once; `CashSession` open/close invariants + signed `CashMovement` factory. ErrorCodes += 14 (→ 60). 6 events. **No tests (owed to A12).** Build full 0/0, SMOKE RUN OK. |
| A08 | ✅ Done | 2026-09-23 | Finance domain: 11 new files (`Domain/Finance` 10, `Events/FinanceEvents.cs`) + ErrorCodes. Append-only balanced `JournalEntry` (**no mutator at all** — every property `private init`; corrections only via `CreateReversal`, A-RULE 002/003 · R-B22); `ChartOfAccounts` seeds the 20 authoritative accounts behind `AccountCodes` constants (no code literal anywhere else); `PostingRules` is the single event → journal translation (10 `For<Event>` methods covering all 14 tabled rows), every one delegating to `JournalEntry.Create` so balance is structural; `AccountingPeriod` forward-only `Open→Closing→Closed→Locked` with 3 close blockers + `EnsurePostable`; `Cheque` 8-state machine with append-only `ChequeEvent` history and the counterparty invariant; `Expense` one-way `Post` + 9 seeded categories → 6000/6100. ErrorCodes += 12 (→ 72). 6 events. **No tests (owed to A12).** Build full 0/0, SMOKE RUN OK. |
| A09 | ✅ Done | 2026-09-23 | Local SQLite context, conventions, converters, 18 mappings + migration `001_LocalCoreSchema`; named filters; no tests (A12). |
| A10 | ✅ Done | 2026-09-24 | A10a–A10d complete; see Repo state log "A10". |
| A10a | ✅ Done | 2026-09-24 | 8 child rows → relational tables (PK OwnerId+childKey, cascade FK, field nav, AutoInclude) via reflection-free `OwnedRows.Map/Row` + `ApplyOwnedScalarConventions`; Money→`<N>Minor`/`<N>Currency`, Quantity→`<N>Unit` + `<N>Scaled` INTEGER×1000 (`FixedScaleQuantityConverter`; StockMovement, PO/GR/Invoice lines) or `<N>Value` TEXT (ReorderLevel). Indexes: JournalEntries EntryNumber unique, Products Sku unique filtered, +IX_JournalLines_AccountCode; no category-name lookup → no index. StockLevel ignored. Deviation: children are EF dependent entities (EF10 owned types can't hold complex props); domain: private parameterless materialization ctor on 7 child records. dotnet-ef 10.0.12 at `.sandbox/tools`. No migration (A10d). |
| A10b | ✅ Done | 2026-09-24 | Guard: `AppendOnlyGuard.Check` runs first in `PrepareSave()` shared by `SaveChanges(bool)`/`SaveChangesAsync(bool,ct)` overrides (covers all overloads), before tenant check/stamping; blocks Modified/Deleted of the 6 types incl. child rows, diagnostics = type + PK original values; Added children allowed. UoW: `SaveAsync` = own transaction when none active (inside `ExecuteAsync` only flushes); `ExecuteAsync` = one tx, nesting throws; events snapshotted before save, ordered by `OccurredAtUtc` (stable), dispatched in-tx, handler writes saved, commit, then `ClearDomainEvents`; any failure → rollback (tx dispose), events kept, `ChangeTracker.Clear()`. `DbUpdateConcurrencyException`→ConcurrencyConflict, Sqlite/DbUpdate(Sqlite)→StoreUnavailable on begin/save/commit; OCE/other exceptions propagate. Dispatcher: no `dynamic`; `services.AddDomainEventHandler<TEvent,THandler>()` → singleton `DomainEventHandlerRegistration` → singleton `DomainEventHandlerMap` (Type→invoker[]), handlers resolved from scope in order, short-circuit. DI: 18 repos + UoW + dispatcher scoped, idempotent; `AddDbContextFactory(..., ServiceLifetime.Scoped)` is the single config source (also registers the scoped context) — scoped factory so the scoped interceptor/tenant are never captured by a singleton. No repo lookups added. |
| A10c | ✅ Done | 2026-09-24 | `Seed/`: `[LocalOnly] SeedMarker` (SeedKey unique `IX_SeedMarkers_SeedKey`, SchemaVersion=1) + `LocalSeeder.SeedAsync` — per block: indexed marker check → rows + marker in one `IUnitOfWork.ExecuteAsync` tx; concurrent loser rolls back and succeeds if the winner's marker exists; failed factory `Result`s propagate (all-or-nothing per block). Marker keys/blocks: `identity.tenant` (T-LOCAL-DEFAULT), `identity.permissions` (full `PermissionCodes.CreateCatalog()`), `identity.roles` (5 `RoleDefinition.CreateSystem` from `DefaultRolePermissions`), `finance.chart` (20 `ChartOfAccounts.Default`), `finance.period.initial` (1 open period), `inventory.units` (PCS BOX ML MG G KG DOSE), `inventory.categories` (root MED VAC FOOD ACC SRV), `boarding.rooms` (R-01…R-08, cap 1; R-07 Isolation, R-08 Vip). Tenant bootstrap: new narrow `Tenant.CreateLocalDefault(displayName)` + `Tenant.LocalDefaultId` (reserved exception: Id=TenantId=sentinel); seeder refuses unless tenant context = sentinel. Catalog decision: `ExpenseCategories`/`AppointmentTypeCatalog`/`VaccineSchedule` stay **static** (domain is sole authority, referenced by opaque code, no FK) — no DB rows. Period code: `ISeedPeriodCodeProvider` → `PersianMonthPeriodCodeProvider` (singleton; `PersianCalendar` month in Asia/Tehran, code `yyyy-MM` Jalali, real UTC bounds from injected `IClock`; fallback fixed +03:30 zone). `LocalSeeder` scoped in `AddAnimoraLocalStore`; not called from desktop. Gate: build 0/0, `dotnet ef dbcontext info` OK (SeedMarkers in model). No migration (A10d), no tests. |
| A10d | ✅ Done | 2026-09-24 | `Queries/`: `QueryPaging.ToPagedAsync` (page≥1, size clamped `[1,min(MaxPageSize,200)]`, requires OrderBy, COUNT + bounded select; skips select past end), `StockOnHandQuery` (GROUP BY ProductId,VariantId SUM `QuantityScaled`), `InvoiceBalanceQuery` (one SELECT: invoice + correlated SUM payments/adjustments + currency-mismatch counts → throws), `LedgerBalanceQuery` (Entries⨝Lines GROUP BY PeriodId,AccountCode,Currency; debit/credit by `Side`). Bounded id inputs (500/500/60), AsNoTracking, tenant filter kept; 3 queries scoped in DI. Migration `20260924054447_002_LocalBusinessSchema` (24 CreateTable, 39 CreateIndex, no Alter/Drop in Up); 001 untouched. |
| A11 | ✅ Done | 2026-09-24 | A11a–A11d complete; see Repo state log "A11". |
| A11a | ✅ Done | 2026-09-24 | `Infrastructure.Local/Security`: `ISecretStore` + DPAPI (win) / Keychain via `/usr/bin/security` (mac) / dev `FileFallbackSecretStore` (opt-in, warns every read); no OS store + no fallback → fail-closed store. Pin `System.Security.Cryptography.ProtectedData` 10.0.12. `LocalDbKeyService` (fail-closed: non-NotFound read failure → `STORE_KEY_UNAVAILABLE`; `GetKey()` throws) replaces `NullStoreKeyProvider`. `KeySource` in `Persistence.Sqlite/Maintenance`. `InstallationIdentity`. 6 store error codes; `LocalStoreOptions` += fallback/K2/WAL/backup + `Validate()`/`DataDirectory`. DI: `AddAnimoraLocalInfrastructure`. build full 0/0; no tests (A12). |
| A11b | ✅ Done | 2026-09-24 | Cipher seam `IStoreEncryptionProvider`: `NoEncryptionProvider` (default) / `MultipleCiphersEncryptionProvider` (`PRAGMA key` escaped, `cipher_page_size=4096`, `sqlite_master` verify → `STORE_KEY_UNAVAILABLE`), selected only when `EncryptionEnabled && K2Approved`; no cipher package pin (E08). `SqliteConnectionInterceptor` rewritten sync+async: (a) key via `IStoreKeyProvider` → `Apply` when encryption on, missing key → throw (fail closed); (b) WAL/NORMAL/clamped busy_timeout/foreign_keys. Interceptor + deps now singletons. `AddAnimoraLocalStore`: `options.Validate()` at context-options build → throw; `IStoreKeyProvider` Null via TryAdd. Entities (`Entities/`, `[LocalOnly]`, not tenant-filtered — installation-level, commented): `LocalInstallation` (PK InstallationId ULID, fingerprint hash TEXT 64), `ClockHighWater` (fixed Id=1), `StoreMaintenanceLog` (Kind int enum, Detail ≤400; added to `AppendOnlyGuard`). `LocalInstallationBootstrap.EnsureAsync` creates row (TenantId `T-LOCAL-DEFAULT`) → singleton `LocalInstallationState`; `SingleTenantContext` reads cache, sentinel fallback, no DB call. Migration `003_LocalHardening` (3 tables + IX_StoreMaintenanceLogs_OccurredAtUtc); 001/002 byte-identical. Gate: ef list 001/002/003, no pending changes, build full 0/0. |
| A11c | ✅ Done | 2026-09-24 | `Infrastructure.Local/Startup/NativeSqliteAssertion`: `Assert(open)` runs `SELECT sqlite_version()`, `Parse` (strict `major.minor.patch`) — < 3.50.2 or unparsable → `NATIVE_SQLITE_TOO_OLD` (detail = actual version); T-RULE 004. `LocalStoreDiagnostics` record + `ILocalStoreInitializer` live in `Persistence.Sqlite/Maintenance/` (avoid Persistence↔Infrastructure cycle; initializer implemented in A11d, resolved lazily via `IServiceProvider`). `ILocalStoreMaintenance`/`LocalStoreMaintenance` (scoped, `AddAnimoraLocalStore`): fresh unpooled connections through new `SqliteConnectionInterceptor.Prepare` (same key + pragma path). Backup → `<BackupDirectory>/animora-<yyyyMMdd-HHmmss>[-n].db` via `BackupDatabase` (destination keyed ⇒ stays encrypted), `quick_check` (bad copy deleted → `STORE_BACKUP_FAILED`), prune to `BackupRetentionCount`; Restore validates exists+quick_check, quarantines current, copies, `InitializeAsync` → `STORE_RESTORE_FAILED` with original kept; QuarantineAndRebuild: `ClearAllPools`, move db/-wal/-shm to `<data>/quarantine/<stamp>/` (**never deletes**), re-entry flag (second corrupt → `STORE_CORRUPT`, no loop), `Quarantined=true`; Vacuum: `VACUUM` + `wal_checkpoint(TRUNCATE)`, returns page_count delta. Log kinds written: Backup/Restore/Quarantine/Vacuum. SqliteException(11/26)→`STORE_CORRUPT`, key failure→`STORE_KEY_UNAVAILABLE`, else op code; cancellation propagates. `Application/Ports/ILocalFileStore` + `Infrastructure.Local/Storage/LocalFileStore` (singleton): `<data>/files/<sha[0..2]>/<sha256>`, stream-to-temp (`files/.tmp`) while hashing, atomic move, dedupe; hash `^[0-9a-f]{64}$` else `VALIDATION_FAILED`; missing → `NOT_FOUND`. Gate: build full 0/0; network grep clean in sources (only implicit `System.Net.Http` global using in `obj/`). |
| A11d | ✅ Done | 2026-09-24 | `Infrastructure.Local/Time/SystemClock` (over `TimeProvider`, default System) + `MonotonicClock` (singleton; `Stopwatch.GetTimestamp`; lazy high-water from `ClockHighWaters` via own scope + `IDbContextFactory`; `Observe` under lock, memory + single-statement upsert `ON CONFLICT … WHERE HighWaterUtc < excluded` — never lowers; store-not-ready tolerated, retried). `Startup/LocalStoreStartup : ILocalStoreInitializer` (scoped) — 8 steps with internal `Steps` log (`InternalsVisibleTo Animora.Persistence.Tests`): dir 0700 / Windows current-user protected ACL + crash flag; `Validate` + passphrase before any file; open + `NativeSqliteAssertion`; `quick_check`/SqliteException 11/26 → `QuarantineAndRebuildAsync` (AsyncLocal re-entry guard → `STORE_CORRUPT`); `MigrateAsync`; WAL TRUNCATE when pages > `WalCheckpointPages` (log `Checkpoint`) + `CrashRecovery` log; bootstrap + seed; diagnostics. DI: `IClock`→`SystemClock`, `IMonotonicClock`, `LocalStoreStartup` + `ILocalStoreInitializer` (all TryAdd). Gate: build full 0/0, SMOKE RUN OK, network grep clean, ef list 001/002/003 + no pending changes; throwaway ValidateScopes/ValidateOnBuild provider + real init OK (native 3.50.4, dir mode 700, high-water not lowered). |
| A12 | ✅ Done | 2026-09-24 | Track A test consolidation: 18 Domain + 16 Persistence test files (incl. `FakeClock`, `LocalStoreTestFixture`, `TestSupport`) covering A05–A11. One A02 defect fixed (IdGenerator shared high-water, see Repo state log "A12"). Expected 321 tests green (259+2 Domain, 57 Persistence, 3 others); final build/test.sh/run.sh re-verification not run in the finishing session (user directive: no long builds/tests) — run once before B01. |
| B01 | ✅ Done | 2026-09-24 | Application layer skeleton: abstractions, frozen reflection-free dispatcher, fixed 6-behavior chain, validation primitives, 3 security-plane ports + temporary defaults, correlation + `OperationIdFactory`, `AddAnimoraApplication()`, `ErrorCodes` += 2. `build.sh full` 0/0; `test.sh` TESTS OK (353: 261 Domain, 33 Application, 57 Persistence, 1 Sync, 1 Server). `run.sh` not re-run (Xvfb not installed; desktop untouched). |
| B02 | ✅ Done | 2026-09-24 | Local command pipeline + transactional outbox. Wire contracts (`CommandEnvelope`, `SettlementResult`, `CommandTypes` 16, `WireApiError`/`WireFieldError`), Domain `SettlementState` + `SettlementTransitions` allow-table, Application ports (`ICommandJournal`, `IOutbox`, `ICommandTransport`, `ILocalSequenceAllocator`, `IIntegrityAlertSink`, `RetryPolicy`, `CommandFingerprint` SHA-256), `CommandJournalBehavior` in the fixed chain, `LocalCommandProcessor` pass loop, Persistence (`PendingCommand`, `OutboxMessage`, `LocalSequenceCounter`, `PipelineSql` atomic claim, `CommandJournal`, `LocalOutbox`, `OutboxClaim`, `OutboxRelayHandler`), migration `004_CommandPipeline`. Build 0/0; Application 49/49 + Persistence 68/68 green. Not re-run `test.sh`/`run.sh` (user directive: no heavy tests). |
| B03 | ✅ Done | 2026-09-26 | Split into B03a–B03c (index: `PHASE-B03-*.md`); completed with B03c. |
| B03a | ✅ Done | 2026-09-24 | Identity foundation: package pin, `User.MustChangePassword`, 4 error codes, `LocalSession`/`LoginAttemptLog` + migration `005_LocalIdentity`, hashing/policy/session infra + DI. |
| B03b | ✅ Done | 2026-09-24 | Identity application layer: 7 use cases + validators, `SessionCurrentUserProvider` (deleted `SingleUserProvider`), `FindByUsernameAsync` + 3 repos (+ `ILoginAttemptLog`), application DI. Application + Infrastructure.Local builds 0/0; Dispatcher/Behavior tests 20/20. |
| B03c | ✅ Done | 2026-09-26 | Auth seam + `Client.Services` identity, Desktop DI/async store startup, login + first-run rewrite, `MockCurrentUserContext` deleted, no demo user; B03 targeted tests 22/22 + layering 4/4; `build.sh full` 0/0; `SMOKE RUN OK`. Full test suite not run (user directive). |
| B04 | ✅ Done | 2026-09-26 | Split into B04a–B04c (index: `PHASE-B04-*.md`); completed with B04c. |
| B04a | ✅ Done | 2026-09-26 | Authorization plane: `PermissionEvaluator` (stored RBAC → 5 policies → 2 typed resource rules), `RequiresPermissionAttribute` read once at registration into `PipelineDescriptor`/`HandlerEntry`, scoped `PermissionAuthorizationGate` (allow-all deleted), `ErrorCodes` += `PERMISSION_UNKNOWN`. `build.sh full` 0/0. Tests not run (user directive; B04 tests are B04c). |
| B04b | ✅ Done | 2026-09-26 | Offline capability: `DeviceCapabilitySnapshots` (`[LocalOnly]`, unique tenant/user/device) + migration `006_CapabilitySnapshot`, HMAC-SHA256 signer (keystore key), `CapabilityEvaluator`/`CapabilitySnapshotIssuer`/`CapabilityStore`, issuance in sign-in AND first-run UoW, `ErrorCodes` += 2. High window = 72h (policy authoritative). `build.sh full` 0/0. Tests not run (B04c). |
| B04c | ✅ Done | 2026-09-26 | Gate seam: `ICapabilityGate` (Contracts) + singleton `CapabilityGateService` (bounded 256-entry cache, fail-closed, B04b reuse), `NavPermissions` (13 real keys), shell ViewModel filtering/status, DI (one singleton), AuthenticationService warm/clear. 31 targeted tests green; `build.sh full` 0/0. |
| B05 | ✅ Done | 2026-09-27 | Split into B05a–B05c (index: `PHASE-B05-*.md`); completed with B05c (crash safety + notification seam + maintenance + B05 test battery). |
| B05a | ✅ Done | 2026-09-27 | Audit plane: `AuditEntries` + migration `007_AuditDiagnostics` (AuditEntries only), `IAuditWriter`/`AuditWriter`, `AuditableAttribute`/`AuditBehavior`, `IntegrityAlertSink`, `ErrorCodes` += `AUDIT_WRITE_FAILED`. |
| B05b | ✅ Done | 2026-09-27 | Observability: Serilog file logging + redaction, `AmbientCorrelation`/`UiCorrelationScope`, diagnostics bundle, `ErrorCodes` += `DIAGNOSTICS_BUNDLE_FAILED`. |
| B05c | ✅ Done | 2026-09-27 | Crash safety + wiring: `IUserNotificationPort` (Contracts) + `NotificationModels`, `ToastNotificationAdapter`, `NotificationText` (Desktop), `GlobalExceptionHandler`/`CrashRecoveryService`, `UiDraftState` + `DraftStateStore` + migration `008_UiDraftState`, `MaintenanceScheduler`, `App.axaml.cs` wiring (3 hooks first, notification port, scheduler), `LocalCommandProcessor.IsPassRunning`, `ErrorCodes` += `CRASH_RECOVERY_REQUIRED`, 5 B05 test classes. `build.sh full` 0/0; targeted test filters OK; `SMOKE RUN OK`. |
| C01 | ✅ Done | 2026-09-27 | Patients & Owners on the real local DB — **establishes the Track C pattern** (use case → Client.Services impl of the Contracts interface → mapping → one DI line → mock deleted). 9 use cases (4 queries + 5 journaled commands) + 4 read models, `PatientDirectoryQueries` (single-statement projections, `Like` over indexed `SearchKey`), `SearchKey` columns + migration `009_PatientsSearchKeys`, `PatientsSampleData` (12 owners / 18 pets / 36 weights / 36 documents) behind `SeedSampleData`, Contracts addition `IStoreReadinessGate` + `StoreReadinessGate`, `PatientsService`/`PatientsMapping`, DI swap, **mock deleted: `MockPatientsQueryService.cs`**, `CommandTypes` += 5, `ErrorCodes` += 4, 3 test classes. `TotalSpend` deferred to C09. Verified: `build.sh full` = **0/0**, `run.sh` = **SMOKE RUN OK**; all C01 tests green (Patients 20/20, PatientDirectory 7/7, Seeding 4/4). Closeout fixed 4 latent C01 defects that only a real build surfaces — see C01 log. |
| C02 | ✅ Done | 2026-09-27 | Reception queue + visits triage on the real local DB (the C01 pattern). **Derived queue** (`ReceptionQueueEntry` only for walk-ins / state an appointment cannot carry; appointment rows created lazily on first state change), `TicketRef` (`apt:`/`wlk:`), 5 queries + 8 journaled commands, `OperationIdFactory.Deterministic` (idempotent start-visit), `ReceptionQueueQueries` (one LEFT JOIN + UNION ALL statement, UTC range) + `TriageQueries`, `ReceptionSampleData`, `ReceptionQueueService`/`VisitsTriageService` + 2 mappings + `VisitTypeCatalog`, 2 DI swaps, 2 VM readiness lines, **mocks deleted: `MockReceptionQueueService.cs`, `MockVisitsTriageService.cs`**, `CommandTypes` += 7, `ErrorCodes` += 4, 3 test classes. Verified: `build.sh full` = **0/0**; C02 filtered tests green (Application 25/25, ReceptionQueueQuery 3/3). |
| C03 | ✅ Done | 2026-09-28 | Visit encounter & vaccination on the real local DB (the C01 pattern). Per-field encounter commands (`SaveVisitVitals`, `SaveVisitNote`, `AddPrescriptionLine`, `RemovePrescriptionLine`, `CompleteVisit` — the 6-step C07-seam handoff transaction: complete → one `MedicalHistoryEntry` → one `VisitBillingHandoff` → best-effort reception `Done` → `MarkReadyForInvoice`) + `EncounterDraftQuery`/`RxCatalogQuery`; vaccination `MarkVaccinationDone`/`RecordVaccinationReminder` + due/catalog/history/sms-template queries. New `[LocalOnly]` `VisitBillingHandoff` + `SmsTemplateRow` + migration **`010_C03Schema`** (numbered 010 not 009, D1). `VaccinationThresholds` 5-bucket read-model projection (collapsed → 4-value Contracts `VaccinationStatus`, D6). `Client.Services`: `TenantScopedCache`, `DebounceBuffer`, `VisitEncounterService`/`VaccinationService` + mappings + `SmsTemplateRenderer` (no delivery). Contracts additive `IVisitEncounterService.FlushAsync`. New `IMedicalHistoryRepository`. 2 DI swaps + removed `VisitEncounterStore`. **Mocks deleted: `MockVisitEncounterService.cs`, `VisitEncounterStore.cs`, `MockVaccinationService.cs`.** `CommandTypes` += 7, `ErrorCodes` += 7, reused permissions (D2). 3 test classes (12 tests). Verified: `build.sh full` = **0/0**, C03 tests green (VisitEncounter 3/3, Vaccination 7/7, VisitBillingHandoff 2/2), `run.sh` = **SMOKE RUN OK**. |
| C04 | ✅ Done (unverified per user request) | 2026-09-28 | Appointments: local clinic calendar/time zone, working hours + migration 011, SQL projection, 7 calendar queries + 2 roster/picker queries, 5 journaled commands, client services + desktop swap and 3 test classes. Tests/build intentionally not run. |
| C05 | ✅ Done (build/tests unverified — no SDK in session) | 2026-09-28 | Boarding on the real local DB (the C01 pattern). Derived daily accrual via pure `StayChargeCalculator` (never stored); `BoardingBillingHandoff` + migration **012** (C07 reads both handoff tables); stale-index remove rule (C07/C08 reuse); 6 seeded `BoardingServiceDef` reference rows + `BoardingSampleData`; 7 queries + 5 journaled commands; `BoardingQueryService`/`BoardingMapping`; DI swap; **mock deleted: `MockBoardingQueryService.cs`**; `CommandTypes` += 4 (checkout existed); `ErrorCodes` += 6; 3 test classes. Checkout stops at `ReadyForInvoice` + writes a Ready handoff (real invoice deferred to C07). Deviations D1–D11 recorded below. Post-merge fix: boarding list/detail/history queries now filter & sort on the `BoardingStay` entity before projecting into the read model (the embedded entity broke EF translation of `r.Stay.State`). |
| C06 | 🟡 Code complete — T12 tests outstanding | 2026-09-28 | Inventory & Purchasing on the real local DB (the C01 pattern). **New: maintained on-hand cache with a proof** — `[LocalOnly]` `StockOnHandSnapshot` (one row per tenant/product/variant, `OnHandScaled` INTEGER ×1000) + `StockSnapshotUpdater.Maintain(db, tenantId, applied)` called from `AnimoraLocalDbContext.PrepareSave()` right after `AppendOnlyGuard.Check`, so EVERY added `StockMovement` from ANY path (receipts, adjustments, future C07 sales, D07 sync) updates its snapshot inside the same transaction; `ILocalStoreMaintenance.RebuildStockSnapshotsAsync` recomputes the cache from the ledger and is called from the restore + `QuarantineAndRebuild` paths. Migration **`013_InventorySnapshot`** (012 taken by C05, D1). `AdjustmentReasons` (Count/Damage/Expiry/Theft/Correction); `ErrorCodes` += 8; `AuditCategory.Inventory` added. `IInventoryQueries`/`IPurchasingQueries` ports + SQLite impls (`InventoryQueries` joins the snapshot for O(1) catalog on-hand; `PurchasingQueries` snapshot in ≤3 SELECTs). Application: 4 inventory queries + 5 commands, 1 purchasing query + 3 commands (receipt writes one `MovementKind.Receipt` per line, over-receipt → `RECEIPT_EXCEEDS_ORDER` unless `AllowOverReceipt`) + registrations. Seed: 12 categories + 8 units (`TAB` added) + `InventorySampleData` (45 products / 90 variants / opening receipts / 6 suppliers / 8 POs across states). `Client.Services`: `InventoryMapping` + `InventoryQueryService` + `PurchasingQueryService` (+ shared `PersianFormat`). Desktop: 2 DI swaps, 2 VM readiness-gate lines, **mocks deleted: `MockInventoryQueryService.cs`, `MockPurchasingQueryService.cs`**, `CommandTypes` += 7. **T12 (3 test classes) not yet written — the only open item.** Deviations D1–D5 below. Verified: `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Client.Services` each build 0 warnings / 0 errors. |
| C07 | ✅ Done | 2026-09-28 | Sales / POS on the real local DB (the C01 pattern) — **the POS creates and posts real invoices** from a visit handoff, a boarding handoff, or blank, with lines from the catalog, discounts, insurance and payments. **T01:** `Insurer` reference aggregate (`src/Animora.Domain/Sales/Insurer.cs`: `Code, Name, CoveragePercent, CeilingMinor`) + 5 net-new Sales error codes (`INVOICE_NOT_FOUND`, `INVOICE_LINE_INDEX_STALE`, `INVOICE_OWNER_REQUIRED`, `HANDOFF_ALREADY_INVOICED`, `INSURER_NOT_FOUND`) + `LocalStoreOptions.AllowNegativeStockOnSale` (default `true`). The domain `Invoice` aggregate (A07) is reused untouched; reversals go through `InvoiceAdjustment`, and the phase's proposed `PAYMENT_EXCEEDS_BALANCE`/`REFUND_EXCEEDS_PAID`/`INVOICE_ALREADY_POSTED` are satisfied by reusing existing `PaymentExceedsDue`/`AdjustmentExceedsPaid`/`InvoiceImmutable`. **T02:** `InsurerConfiguration` (unique `Code` index) + migration **`014_SalesInsurers`** (numbered 014 not the phase's 013 — 013 is C06's, deviation **D1**; `.Designer.cs` + `AnimoraLocalDbContextModelSnapshot` updated) + **6 seeded insurers** in `LocalSeeder` (تأمین اجتماعی / ایران / آسیا / دانا / البرز / پارسیان). **T03:** `IInvoiceNumberAllocator` port + `InvoiceNumberAllocator` over B02's `LocalSequenceCounters` (`PipelineSql.NextSequenceAsync`, counter `invoice:<tenant>:<jalaliYear>`, format `{yy}-{seq:000000}`) + concurrency test. **T04:** `ISalesQueries` port + `SalesQueries` — ready-for-billing `UNION ALL` over both handoff tables (`HandoffStatus.Ready`, newest first, max 100), paged invoice list, invoice detail (lines + payments + adjustments + the settlement join to `PendingCommand`), `SalesCatalog`, `Insurers`, one-statement `SaleKpi`, `SalesHistory(CashierHistoryFilter)` (page + total + inflow/outflow sums), `OwnerLookup`. **T05:** 6 sales read models (`InvoiceSummaryReadModel`, `InvoiceDetailReadModel` with `SettlementStatus`/`LastSettlementErrorCode`/`ResponsibleRole`, `ReadyForBillingReadModel`, `SalesCatalogReadModel`, `SaleKpiReadModel`, `CashierHistoryReadModel`). **T06:** draft-editing commands (create ×3 from visit/stay/blank, line add + add-custom, line edit/remove with the **C05 stale-index rule** `INVOICE_LINE_INDEX_STALE`, discount, insurance via `Insurer.CoveragePercent`+`CeilingMinor`, owner, delete-draft) — each a journaled command + handler + validator. **T07:** `PostInvoiceCommand` — the 7-step journaled posting transaction (recompute totals → allocate number → `Invoice.Post` → one `MovementKind.Sale` per stockable line → mark the handoff `Invoiced` → `InvoicePostedEvent` → `CommandJournalBehavior`), stable `OperationId` so replay returns the stored result. **T08:** `RegisterPaymentCommand` + `RefundInvoiceCommand`/`ReturnInvoiceLineCommand`/`VoidInvoiceCommand` (all via `InvoiceAdjustment`, original invoice byte-identical; return writes one `MovementKind.ReturnIn`). **T09:** `SalesRegistration` (Application DI) + persistence-side `ISalesQueries`/`IInvoiceNumberAllocator` wiring. **T10:** `SalesMapping` + `SalesQueryService` implementing all **22** `ISalesQueryService` members (post-on-payment, void-as-reversal, draft-only-delete decisions XML-documented). **T11:** `CommandTypes` additions + `SalesSampleData` (30 invoices across states with payments + matching stock movements). **T12:** Desktop DI swap (`ISalesQueryService → SalesQueryService`), `SalePageViewModel` readiness-gate line, **mock deleted: `MockSalesQueryService.cs`** (no remaining reference). **T13:** 4 test classes (`InvoiceImmutabilityTests`, `PostInvoiceCommandTests`, `SalesCompensationTests`, `InvoiceNumberAllocatorTests`). **Verified — all three gates:** `build.sh full` = **0 warnings / 0 errors**; `test.sh` = **TESTS OK** (551 tests: Domain 267, Application 181, Persistence 101, Sync 1, Server 1); `run.sh` = **SMOKE RUN OK**. Deviations D1–D4 below. |
| C08 | ✅ Done | 2026-09-28 | Cashier & Cheques on the real local DB (the C01 pattern) — **cashier reuses C07 verbatim, no second invoice path**. `ICashSessionContext` + `Payment.CashSessionId` + index + migration **`015_CashSessionLink`** (numbered 015 not the phase's 014 — 014 is C07's `SalesInsurers`, D1); `RegisterPaymentCommand` stamps the open session and enforces `CASH_SESSION_REQUIRED` for cash only (the sole additive C07 change). Cash session: open/close/movement commands with the recorded-never-corrected variance rule (`CashVarianceThresholds`, placed under `Domain/Cashier/` not `Domain/Sales/`, D2). Cheque lifecycle: create/update/delete/status + `BulkChangeChequeStatusCommand` (one UoW, per-id result list), each appending a `ChequeEvent`; clearing/bouncing raise `ChequeClearedEvent`/`ChequeBouncedEvent`; closing a session raises `CashSessionClosedEvent` — **the three events C09's ledger consumes**. `BankDirectory` (18 banks, seeded), `ChequeAlertRules`, cheque cashflow/breakdown/alerts read models. `CashierQueries`/`ChequeQueries`, `FinanceSampleData`. `CashierService`/`ChequesQueryService` + 2 mappings; 2 DI swaps; **mocks deleted: `MockCashierService.cs`, `MockChequesQueryService.cs`**; `CommandTypes` += 8 (3 cashier + 5 cheque, D3); `ErrorCodes` += cash/cheque codes (names differ from the phase text, D4); 3 test classes. Verified: Application/Persistence/Client.Services build clean. |
| C09 | ✅ Done | 2026-09-29 | Finance & reporting read models on the real local DB — **a single, balanced, append-only ledger writer** and **projected fact read models off the OLTP path**. `LedgerPostingHandler` is the only `JournalEntry` constructor (`IDomainEventHandler<>` for 10 events: `InvoicePosted/Refunded/Voided`, `PaymentRegistered`, `StockMovementRecorded`, `GoodsReceiptPosted`, `ChequeCleared/Bounced`, `CashSessionClosed`, `ExpensePosted`), asserted by `LedgerSingleWriterTests`; balance enforced by `JournalEntry.EnsureBalanced()` + `AppendOnlyGuard` (`LEDGER_UNBALANCED`). `JournalNumberAllocator` + `ExpenseNumberAllocator` over the B02 counter. Three `[LocalOnly]` fact tables (`DailyFinanceFact`, `ProductSalesFact`, `OwnerFinancialSnapshot`) + migration **`016_FinanceProjections`** (numbered 016 not the phase's 015 — 015 is C08's, D1), maintained outside the OLTP section by `FinanceProjectionWorker` + `FinanceProjectionStore`/`FinanceProjectionRebuilder`; `RebuildFinanceProjectionsAsync` recomputes from the ledger (proven by `FinanceProjectionIntegrityTests` + run at end of seeding). **FIFO-by-latest-receipt COGS.** 7 fact-only reporting queries (`FinanceQueries`, tenant/user/version cache keys) + `RecordExpenseCommand`/`CloseAccountingPeriodCommand`; late-event-into-next-open-period rule; **`ReopenAccountingPeriodCommand` not implemented (D2).** `FinanceQueryService`/`FinanceMapping`; **`PatientsMapping` now reads `OwnerFinancialSnapshot` — C01's deferred `TotalSpend` closed**. DI swap + worker registered; **mock deleted: `MockFinanceQueryService.cs`**; `CommandTypes` += 2 (`finance.period.close`, `finance.expense.post`, D3); `ErrorCodes` += finance/period codes (D4); 4 test classes. Verified: Application/Persistence/Client.Services build clean. |
| C10 | ✅ Done | 2026-09-29 | History, Dashboard, Global search & Profile on the real local DB — **reads the C09 fact tables, adds no new ledger writer**. Materialized `[LocalOnly]` `ActivityEvent` feed + `UserPreference` (migrations **`017_ActivityFeed`** + **`018_UserPreferences`** — 016 taken by C09, D-01); `ActivityFeedMapper` maps **14** domain event types to one `ActivityEvent` each with documented key/args/amount/sign; `IActivityFeedWriter` appends on the OLTP path and `RebuildActivityFeedAsync` replays the outbox to reproduce the feed exactly (proven by tests). History feed query + `CsvWriter` (UTF-8-BOM, Persian-correct) + export path (50 001 rows → `EXPORT_TOO_LARGE`). `DashboardComposer` + role mapping (manager sees revenue/doctor-loads/species; doctor gets none) with a **≤8-statement budget** per snapshot. Five-source **`UNION ALL`** global search (owners/pets/invoices/products/appointments), one statement, Persian-digit + Arabic-form matching via the normalized `SearchKey`, <2-char no-op, per-source limits. Profile/settings queries + **non-journaled** save commands (`UserProfileService`) that survive restart. 4 client services + `CsvWriter` + `HistoryText`; 4 DI swaps; **mocks deleted: `MockHistoryQueryService`, `MockDashboardQueryService`, `MockSearchService`, `MockUserProfileService`**; `ErrorCodes` += 3; 3 test classes (`ActivityFeedTests` 14-type mapping + rebuild-exact + ASCII, `DashboardBudgetTests`, `GlobalSearchQueryTests` — **21 tests green**). Verified: Application/Persistence.Sqlite/Client.Services build 0/0; targeted test run green. Deviation D-01 + the ExportCsv contract-vs-path resolution recorded below. |
| C11 | Done (scoped verification) | 2026-09-30 | Final mock removed; migration 019; DI/layering guards, 14-step clinic/reopen/boarding scenario, startup guard; 32/32 focused tests, targeted Desktop build 0/0, isolated SMOKE RUN OK, six offline metrics recorded. No full suite/build by user directive; manual UI and proxy/cold-start limitations recorded below. |
| ~~D01–D13, E01–E09~~ | 🗑️ Removed 2026-10-08 | | **Deleted, not executed.** Planning-only files with baseline drift; superseded by the `M00` regeneration (see "Server-track reset — 2026-10-08"). No code existed. |
| M00 | ✅ Done | 2026-10-09 | Server-track regeneration meta-phase. Authors fresh `S##`/`G##` phase files, one small batch per TODO task; resumable across sessions. See `phases/PHASE-M00-server-track-regeneration.md`. T1 done: 29 phases mapped (S01–S20, G01–G09; S03/S15/S20/G01 band S, rest M); baseline: client migration head 031, CommandTypes 88 live (PROGRESS prose '86' is stale), ErrorCodes 161; full coverage matrix in phases/M00-source-brief.md. T2 done: Reference pack (R1–R6: wire seam, CommandTypes rule, settlement vocabulary, application ports, Result/ErrorCodes, sync scaffold) copied verbatim into phases/M00-source-brief.md; live counts CommandTypes 88 / ErrorCodes 161. T3 done: Authored PHASE-S01-server-host-scaffold, S02-server-data-layer-tenancy-i (migration 101), S03-server-data-layer-tenancy-ii, S04-identity-server, S05-tenant-context-authorization (+ naming lock Animora.Web.Admin / Persistence.PostgreSql / Sync.Custom in source brief); passes authoring checklist, no forward deps. T4 done: Authored PHASE-S06-command-intake-idempotency (wire v2, CommandIntake/CommandIdMap, +5 ErrorCodes, server chain profile), S07-server-handlers-clinical (incl. TreatmentPlan), S08-server-handlers-scheduling-boarding, S09-server-handlers-sales-cashier, S10-server-handlers-money-stock (incl. RecurringExpense); inventory derived from live CommandTypes.All (never frozen), coverage tests mandatory from S10. T5 done: Authored PHASE-S11-journal-change-capture (commit-ordered ChangeSequence via deferred constraint trigger, SyncContractCatalog), S12-immutable-batch-export (RR frozen 256-KiB batches, retention 90d+30, crash-atomic GC), S13-outbox-workers-resilience (SKIP LOCKED fenced claim, breaker, bulkhead, SignalR hint), S17-files-valet-key, S18-observability-weak-server-performance (loopback-only OTLP, MinPoolSize=0, pool sum 12/20); PG-gated invariants are named tests mandatory in G07. T6 done: Authored PHASE-S14-subscription-operator-api (subscription domain + operator API + single ProvisioningEngine), S15-onboarding-intake (HMAC-signed idempotent provisioning intent), S16-licensing-anti-piracy (Animora.Server.Licensing verification/issuance, licence grants, ServerEntitlementGate; client wiring -> G06). T7 done: Authored PHASE-S19-admin-web-operator-core (Animora.Web.Admin RCL, Static SSR, cookie auth, ports/adapters) and PHASE-S20-admin-web-insight-seams (ops pages, one live island, disabled-by-default ITenantInsightQuery seam; analytics deferred). T8a done: Authored PHASE-G01-sync-engine-abstractions (IStateSyncEngine, SyncScope/SyncResult, NullSyncEngine offline fallback). T8b done: Authored PHASE-G02-immutable-batch-http-adapter (new Animora.Sync.Custom project, 4 ProjectRefs only — no Npgsql/server ref; HttpStateSyncEngine consuming the S12 /sync/* feed with page+content-hash verification and resumable 304; ISyncApplyStore seam + BufferingSyncApplyStore default, real atomic apply -> G04; conditional DI, Null engine stays when unconfigured; +4 Sync-transport ErrorCodes). T8c done: Authored PHASE-G03-sync-orchestration-status (SyncOrchestrator advances SyncCursor only on Applied/UpToDate; SyncSchedulerHostedService dormant-when-unconfigured with jitter+capped-backoff via IDelayProvider/TimeProvider; ISyncResyncHandler seam + NullSyncResyncHandler -> G04; LocalSyncStatusService extended to combine state-pull + command backlog into one SyncState without merging the two pipelines). T8d done: Authored PHASE-G04-conflict-resolution-non-destructive-pull (three-layer replica store repl_base/repl_staging/repl_checkpoint + one client migration; SqliteSyncApplyStore atomic base+checkpoint on one connection/tx, pending overlay never touched; ConflictResolver encoding ARCH §8.5 — posted invoice/payment reversal-only never in-place, UI-prefs LWW only, inventory additive, slot rejected, demographics field-merge; ShadowResyncHandler builds encrypted shadow generation preserving overlay then atomic switch; +2/3 ErrorCodes; replaces G02/G03 no-op defaults conditionally). T8e done: Authored PHASE-G05-command-transport-settlement (HttpCommandTransport : ICommandTransport consuming S06 /api/commands[/batch] + /ids; HTTP-200 settlement mapped onto journal with §8.7 four-question UX; 426 stays Deferred not dropped; idempotency (TenantId,OperationId) never loses/doubles; CommandIdRemapService rewrites dependent payloads with server-minted ids + recomputes fingerprint; IReplicaVersionReader sets ExpectedServerVersion from G04 base; SettlementReconciler via SettlementTransitions.EnsureAllowed; replaces NullCommandTransport only when configured; command vs state pipelines kept separate). T8f done: Authored PHASE-G06-online-auth-entitlement-device (OnlineAuthClient over S04 /auth/*; BearerAuthHandler single-flight 401-refresh on the shared HttpClient so G05/G02 never touch tokens; real OnlineEntitlementGate replacing AllowAllEntitlementGate only-when-configured and deciding from the S16 sealed local cache + LicensePolicy/CapabilityGate offline-only — never a live server call; HighWaterClock forward-only via SealedEntitlementMerge; DeviceFingerprintSource raw-never-leaves + DeviceLifecycleService wipes offline authority on DEVICE_REVOKED at next contact; credential-mirror fallback = OS-protected capability snapshot + sealed entitlement + one-way password verifier for cold offline start; one [LocalOnly] client migration ClientSessions/SealedEntitlements next-free ordinal; +0/2 ErrorCodes iff absent; only client->server ref is Animora.Server.Licensing.Verification). T8g done: Authored PHASE-G07-validation-gates-ci-rls-restore (.github/workflows/pg-gates.yml with PostgreSQL 18 service + non-owner/non-superuser/no-BYPASSRLS app role + ANIMORA_REQUIRE_PG=1 turning the §1 skip into a failure; three jobs: pg-correctness promotes the S02/S03/S11/S12/S18 PG-gated tests to mandatory 0-skipped, pg-rls-hardening proves RLS-ON leaks nothing while RLS stays §5-deferred, restore-drill does the ARCH §17.3 one-boundary DB+files+key-ring restore to readiness+known-tenant with measured RTO + key-ring-missing-refuses-readiness; scripts/ci/pg-gate.sh + restore-drill.sh, PgGateManifestTests locks the [PgGated] set; no src/ change, no migration, no ErrorCode — every acceptance is a named test / CI exit code). T8h done: Authored PHASE-G08-performance-proof-offline-budget (.github/workflows/perf-gates.yml, five jobs: offline-budget promotes the C11 bench.sh offline + offline-budget.py harness to a mandatory gate (6 metrics, >2x=FAIL, table archived, A-RULE 035); server-capacity = published dotnet load harness on a cgroup-constrained 1-3 core / 2-4 GiB container + PostgreSQL 18 recording throughput/p99/RSS/pool to an artifact and asserting no pool exhaustion / no 5xx / readiness-green (gate T, capacity recorded never guaranteed); sync-behavioral = ReconnectStormTests jitter-spread (gate I) + MixedFleetTests N/N-1/N-2 + RuleSetVersion (gate M); zero-internet = published-tree external-endpoint grep + egress-blocked SMOKE RUN OK (gate Z); layering-audit = LayeringRulesTests + ServerLayeringAuditTests mechanical reference-set assertions (R-B10). No src/ change, no migration, no ErrorCode — every acceptance is a script exit / numeric threshold / named test). T8i done: Authored PHASE-G09-release-engineering (three single-operator runbooks docs/release/ — 01 deployment-topology (ARCH §17.1 migration-discipline-before-traffic first-install), 02 upgrade-rollback (stop-migrate-start | blue/green, additive-migration + versioned-wire one-step-back rollback never strands clients, WireContract.IsSupported N/N-1/N-2), 03 backup-restore-hostmove (three-part single consistency boundary DB+files+key-ring, named RPO cadence, measured RTO from G07 restore-rto.txt, off-host durability, host-move + named scale-out trigger); scripts/release/make-release-manifest.sh = NuGet SBOM (CycloneDX, no cloud, NuGetAuditMode=all + lock-file) + native-binary inventory (e_sqlite3/e_sqlite3mc/SQLCipher + version + RID, STACK §16) + suppression-ledger rejecting native-DB/crypto entries without expiry; publish-portable.ps1 -EmitManifest ships the manifest; launch-gate aggregator scripts/release/launch-gate.sh + .github/workflows/release-gates.yml fail the release unless every ARCH §19 lane (G07 pg-gates + G08 perf-gates + desktop smoke + SBOM/native-inventory + zero-Internet built-output scan as a ship-blocker) is green, emitting artifacts/launch-gate-report.md; LaunchGateManifestTests locks the gate list to the §19 table; no src/ change, no migration, no ErrorCode — every acceptance is a script exit / generated artifact / named test; Next phase stays M00 for T9). T9a done: cross-checked all 29 S##/G## phase files against the authoring checklist (baseline-verified line, "Runnable after" offline line, no forward dep — every Depends-on cites only lower S/G or C11); scrubbed the dead `PHASE-D##`/`PHASE-E##` phase-file tokens out of each file's **Produced by:** provenance line (now cite the ROADMAP §2 scope-inventory row, not a deleted phase ID), so the `grep -rn "PHASE-[DE]0" backend-roadmap/phases` gate is clean except the M00 file's own acceptance-criterion prose. T9b done: rewrote ROADMAP §1 (Tracks) + §2 (phase index) so the live tables list the regenerated S01–S20/G01–G09 set with bands/deps/R-B## read from the live phase headers (total 57 = A12+B5+C11+S20+G9; band tally S=5/M=52/XL=0); refreshed §3/§5/§6 and recorded the post-reset deviation in §6. T9 done (closes M00): verified the regenerated set collectively covers the full ROADMAP §2 D/E scope inventory (D01–D13, E01–E09 all mapped via the brief §2 matrix) + post-C11 drift (TreatmentPlan→S07/S11, RecurringExpense→S10/command-settled, Statistics has no server surface→stated in S11 Out of scope) with no duplicate introducer; all 29 phase files pass the authoring checklist (baseline-verified line, "Runnable after: desktop offline yes", Band line, Acceptance criteria, no forward dep — every Depends-on cites only lower S/G or C11); `grep -rn "PHASE-D0\|PHASE-D1\|PHASE-E0" backend-roadmap/phases` returns nothing (only the M00 file's own acceptance-criterion/matrix prose mentions the historical D/E IDs); added the S01–S20/G01–G09 rows below as ⬜ Not started; set "Next phase" → S01; kept phases/M00-source-brief.md (its §2 regeneration map + Reference pack R1–R6 remain the single cross-check index the audit trail cites — not duplicated verbatim into any single phase file). **M00 complete.** |
| S01 | ✅ Done | 2026-10-09 | Server host scaffold (`Animora.Server`, config, health, rate-limit policies `auth/commands/sync/files/operator`, solution + test wiring). Band M · Depends on C11 · R-B05, R-B27. ASP.NET Core 10/Kestrel host with the numbered startup block, `ServerOptions` + `ValidateOnStart`, `SecretGuard`, `GlobalExceptionMiddleware` → `WireApiError`, anonymous `/health/live` + `/health/ready`, five named rate-limit policies, Development-only OpenAPI; `Animora.Server.Tests` wired to the subject (23 tests green) + client↔server isolation facts. See `phases/PHASE-S01-server-host-scaffold.md`. |
| S02 | ✅ Done | 2026-10-09 | Server data layer & tenancy I — new `Animora.Persistence.PostgreSql` project, `AnimoraServerDbContext` (Tenants), named `Tenant`/`SoftDelete` query filters, `ITenantContext` + stamping/ignore-filter-ban interceptors, trigger-owned `uuid` Version, key-ring context (`DataProtectionKeys`), keyed pooled data sources, readiness probes `database|keyring|schema`, migrations **101_ServerFoundation** + **101_KeyRing**. Band M · Depends on S01 · R-B03, R-B11, R-B19. See `phases/PHASE-S02-server-data-layer-tenancy-i.md`. |
| S03 | ✅ Done | 2026-10-09 | Server data layer & tenancy II — tenant-scoped repositories/UoW, tenancy guard (write-path stamping, cross-tenant tests). Band S · Depends on S02 · R-B11, R-B19. | Delivered: `ServerUnitOfWork`/`ServerTransactionRunner` (ReadCommitted + execution strategy, 40001/40P01 retry x3 with 50–250 ms jitter, cancellation never swallowed), `UniqueViolationMapping` opt-in, `ITenantSessionInitializer`+`NoopTenantSessionInitializer` (RLS off), `TenantRepository<T>`, `PageRules` (MaxPageSize=200), `IServerReadContextFactory`/`ServerReadContextFactory` (oltp-read, NoTracking); cross-tenant isolation suite (PG-gated). No migration, no table, no ErrorCode. |
| S04 | ✅ Done | 2026-10-09 | Identity server — ASP.NET Core Identity over the Domain `User`, opaque Data-Protection bearer + rotating refresh (reuse detection), lockout, device binding, append-only `SecurityAudit`, `/auth/login\|refresh\|logout\|device/register`. Band M · Depends on S03 · R-B04, R-B20. See `phases/PHASE-S04-identity-server.md`. |
| S05 | ✅ Done | 2026-10-09 | Tenant context & authorization policies. Band M · Depends on S04 · R-B04, R-B19, R-B20. Tenant bound **only** from the server-stored active membership (`TenantContextMiddleware` + `IMembershipResolver` over the keyless `IUserLookup`; token `tid`/`role` never authority), stored-RBAC `PermissionRequirement`/`ServerPermissionPolicyProvider` + coarse policies + frozen empty `CommandPermissionTable`, entitlement slot (`IServerEntitlementGate` allow-all until S16), fixed plane order authN→tenant→authZ→entitlement, device approve/revoke (+stamp rotation), signed `/auth/capabilities`, anonymous `/auth/keys`, migration `103_SigningKeys`. See `phases/PHASE-S05-tenant-context-authorization.md`. |
| S06 | ✅ Done | 2026-10-09 | Business API — command intake & idempotency. Band M · Depends on S05 · R-B13, R-B14, R-B15, R-B29. Wire v2 (`ExpectedServerVersion`, `RuleSetVersion`; versions {1,2}), `CommandIntake`/`CommandIdMap` (migration `104_CommandIntake`), atomic `ON CONFLICT DO NOTHING` intake with fenced leases + crash recovery, `POST /api/commands`, `/batch` (≤50), `GET /{operationId}/ids`, server pipeline profile (no `CommandJournal`), +5 error codes. No domain handler shipped (all commands settle `HANDLER_NOT_REGISTERED` until S07–S10). |
| S07 | ✅ Done | 2026-10-09 | Implementation authored; build/test/smoke unexecuted by user directive. 26 wire types / 27 payload shapes, migration `105_ClinicalCore`. |
| S08 | ✅ Done | 2026-10-09 | Implementation authored: 13 wire types, PG ports, optimistic slot/room fences, migration `106_SchedulingBoarding`; tests written but build/test/smoke gates not executed. |
| S09 | ✅ Done | 2026-10-10 | Authored (build/test/smoke NOT executed per user directive) — 21 live wire types (18 sales + 3 cashier), migration `107_SalesCashier`, PG ports, DocumentCounters allocator.  Server handlers II-a — sales & cashier. Band M · Depends on S08 · R-B01, R-B17, R-B22. See `phases/PHASE-S09-server-handlers-sales-cashier.md`. |
| S10 | ✅ Done | 2026-10-10 | Authored (build/test/smoke NOT executed per user directive) — 23 live wire types, migration `108_MoneyStock` + immutability triggers, derived on-hand (never stored), ServerDomainEventDispatcher + LedgerPostingHandler, permission-catalog gate un-Skipped.  Server handlers II-b — inventory, purchasing, finance (incl. `RecurringExpense`, cheques, ledger). Band M · Depends on S09 · R-B01, R-B17, R-B22, R-B23. See `phases/PHASE-S10-server-handlers-money-stock.md`. |
| S11 | ✅ Implementation authored | 2026-10-10 | Commit-safe journal/capture; acceptance execution waived by user (unverified). |
| S12 | ✅ Implementation authored | 2026-10-10 | Immutable batches/pages, sync endpoints, retention; acceptance execution waived (unverified). |
| S13 | ✅ Implementation authored | 2026-10-10 | Transactional outbox, bounded workers/resilience and tenant hints; acceptance execution waived (unverified). |
| S14 | ✅ Done (implementation authored; execution gates deferred) | 2026-10-10 | Subscription domain/lifecycle, operator bearer identity/API, one atomic provisioning engine, bounded sweep and fenced expiry consumer; head `112_Subscription`. Pure/PG regression sources authored, none executed. |
| S15 | ✅ Done (authored; gates unexecuted) | 2026-10-10 | Self-service onboarding intake (async idempotent provisioning intent). Band M · Depends on S14 · R-B30, R-B36, R-B29. See `phases/PHASE-S15-onboarding-intake.md`. |
| S16 | ⬜ Not started | | Licensing & anti-piracy (signed license, device-fingerprint binding, offline grace, clock-rollback, fail-closed). Band M · Depends on S15 · R-B06, R-B29, R-B30, R-B36. See `phases/PHASE-S16-licensing-anti-piracy.md`. |
| S17 | ⬜ Not started | | Files — valet key + resumable transfer (`Staged→…→Available`, reconciliation job). Band M · Depends on S13 · R-B26. See `phases/PHASE-S17-files-valet-key.md`. |
| S18 | ⬜ Not started | | Server observability & weak-server performance (bounded pools, `MinPoolSize=0`, loopback OTLP). Band M · Depends on S13 · R-B07, R-B27, R-B35. See `phases/PHASE-S18-observability-weak-server-performance.md`. |
| S19 | ⬜ Not started | | Admin web I — operator panel core (`Animora.Web.Admin`, Blazor Web App Static-SSR-first, operator auth). Band M · Depends on S16, S18 · R-B05, R-B06, R-B29. See `phases/PHASE-S19-admin-web-operator-core.md`. |
| S20 | ⬜ Not started | | Admin web II — insight seams (`ITenantInsightQuery` read-model port only; cross-tenant analytics deferred). Band M · Depends on S19 · R-B05, R-B11, R-B35. See `phases/PHASE-S20-admin-web-insight-seams.md`. |
| G01 | ⬜ Not started | | `Animora.Sync.Abstractions` — `IStateSyncEngine`, `SyncScope`, `SyncResult`, `NullSyncEngine` fallback + DI. Band S · Depends on S12 · R-B03, R-B13. See `phases/PHASE-G01-sync-engine-abstractions.md`. |
| G02 | ⬜ Not started | | Custom immutable-batch HTTP adapter (`Animora.Sync.Custom`; no PostgreSQL driver, no server project). Band M · Depends on G01 · R-B03, R-B13, R-B31. See `phases/PHASE-G02-immutable-batch-http-adapter.md`. |
| G03 | ⬜ Not started | | Sync orchestration & status (scheduler, `SyncCursor` integration, status surfaced to `ISyncStatusService`). Band M · Depends on G02 · R-B03. See `phases/PHASE-G03-sync-orchestration-status.md`. |
| G04 | ⬜ Not started | | Conflict resolution & non-destructive pull (preserve pending intent; posted-invoice conflicts reversal-only). Band M · Depends on G03 · R-B03, R-B17, R-B18. See `phases/PHASE-G04-conflict-resolution-non-destructive-pull.md`. |
| G05 | ⬜ Not started | | Command transport & settlement reconciliation (`HttpCommandTransport`, replaces `NullCommandTransport`). Band M · Depends on G04, S10 · R-B03, R-B09, R-B16. See `phases/PHASE-G05-command-transport-settlement.md`. |
| G06 | ⬜ Not started | | Online auth, entitlement & device lifecycle (replaces `AllowAllEntitlementGate`; credential-mirror fallback). Band M · Depends on G05, S16 · R-B04, R-B06, R-B20, R-B21, R-B36. See `phases/PHASE-G06-online-auth-entitlement-device.md`. |
| G07 | ⬜ Not started | | Validation gates I — mandatory PostgreSQL CI, tenancy/RLS gate, restore gate. Band M · Depends on G06, S18 · R-B19, R-B32. See `phases/PHASE-G07-validation-gates-ci-rls-restore.md`. |
| G08 | ⬜ Not started | | Validation gates II — performance proof & offline budget (weak-server/weak-client capacity, layering audit). Band M · Depends on G07, S20 · R-B07, R-B08, R-B10, R-B32. See `phases/PHASE-G08-performance-proof-offline-budget.md`. |
| G09 | ⬜ Not started | | Release engineering (topology, upgrade/rollback runbook, backup/restore rehearsal, packaging, SBOM). Band M · Depends on G08 · R-B27, R-B31, R-B33. See `phases/PHASE-G09-release-engineering.md`. |

Status values: `⬜ Not started` · `🟨 In progress` · `✅ Done` · `⛔ Blocked (reason)` · `🗑️ Removed`.

---

> ⛔ **STOP — everything below this line is per-phase archive, not current-session context.**
> Do **not** read further unless a phase file's "Read before starting" names a specific
> `### <PhaseID>` entry below by ID. If it does, jump straight to that heading (grep for it) —
> do not read the entries around it. If nothing below is cited, you are done reading this file.

## Repo state log

### S15 — 2026-10-10

- Signed HMAC intake, bounded replay prevention, encrypted payloads, client isolation and once-read temporary passwords. Fenced background claims, engine replay recovery, operator visibility and bounded retention.
- Regression and PostgreSQL-gated sources authored. No build, test, smoke, EF scaffolding or database application executed, per user instruction. Acceptance is not claimed. Migration head: `113_ProvisioningIntents`.


### S14 — Subscription and operator plane (2026-10-10; authored, not execution-accepted)
- Pure subscription aggregate, table-driven lifecycle, gap-free event/billing sequences and tenant status mapping; eight additive error codes.
- Narrow operator context with read-only membership enforcement, seven mappings, audited status/lifecycle services, configurable standard-plan default amount, seat usage and licence scheduler seam.
- Atomic ProvisioningEngine: canonical business fingerprint, permanent ledger, per-key/code serialization, tenant/owner/feed/subscription/security/operator audit on one connection/transaction. TenantProvisioner participates in external transactions rather than prematurely committing; temporary owner passwords require change and are never returned on replay.
- Separate 30-minute opaque operator bearer scheme without refresh, per-request stored stamp/status validation, lockout, forced password change, environment-only bootstrap and all planned operator routes, bounded paging, If-Match and atomic owner reset.
- Migration `112_Subscription`, immutable event/operator-audit triggers and trial/standard seeds; frozen provider model authored manually. Not applied.
- Bounded subscription sweep under shared admission and worker pool, period-key outbox dedupe, expiry consumer explicitly enlisted in the relay's fenced transaction.
- Domain/operator/provisioning/source-boundary regression sources plus env-gated PG race/rollback/immutability/status/idempotency/auth-parity sources authored. **No test, build, smoke or database application executed.** Desktop/local client code untouched.


### S06 — Command intake & idempotency (2026-10-09)

**What exists now.** (projects touched: `Animora.Contracts.Wire`, `Animora.Domain` (`ErrorCodes` only), `Animora.Application` (server pipeline profile only), `Animora.Persistence.PostgreSql`, `Animora.Server`, `Animora.Server.Tests`, `Animora.Application.Tests`; `Persistence.Sqlite`/`Desktop` untouched)
- **Wire v2 (additive):** `CommandEnvelope` gains trailing `ExpectedServerVersion` + `RuleSetVersion`; `WireContract.Version = 2`, `SupportedContractVersions = {1,2}`, `IsSupported(int)`. Numeric `ExpectedAggregateVersion` stays but the server never uses it as concurrency evidence.
- **Error codes (+5, region "Server command intake"):** `COMMAND_IN_PROGRESS`, `DEPENDENCY_REJECTED`, `BATCH_TOO_LARGE`, `CONTRACT_VERSION_UNSUPPORTED`, `RULESET_UNSUPPORTED`.
- **Application:** `ApplicationPipelineProfile { Client, Server }`, `Dispatcher.ServerChain` (= `Chain` minus `CommandJournalBehavior`), `AddAnimoraApplication(..., profile = Client)`; `Chain` unchanged.
- **Tables / migration `104_CommandIntake`:** tenant-scoped `CommandIntake` (PK `(TenantId, OperationId)`, status/outcome/fingerprint/fenced lease/`PayloadJson`, indexes on lease and dependency) and `CommandIdMap` (PK `(TenantId, OperationId, Slot)`); not syncable. `ServerSchemaVersion.ExpectedHead = "104_CommandIntake"`.
- **Persistence (`Commands/`):** `ICommandIntakeStore`/`CommandIntakeStore` (`INSERT … ON CONFLICT DO NOTHING`, find, same-transaction complete, fenced lease renewal, expired-lease listing ≤50), `CommandIntakeCompactor` (nulls `PayloadJson`, keeps fingerprint + outcome), `IntakeStatus`.
- **Server (`Commands/`):** `CommandIntakeService` (version gate → identity + fingerprint → dedupe/lease → dependency → authz/entitlement → payload catalog → server dispatcher in one transaction → settlement mapping), `IntakeRecoveryService` (fenced new lease, 3 expired leases ⇒ `Failed`), `CommandPayloadCatalog` (shape resolution; empty until S07–S10), `ReservedCommandTypes` (5 names), `SettlementMapper`, `ErrorResponsibility`, `RuleSetPolicy`, `OfflineWindowOptions` (90 d), `CommandIntakeRetentionOptions` (120 d / 3650 d), seams `IAggregateVersionReader`, `IEffectCommittedProbe`, `IServerOutbox` (no-op), `CommandEndpoints` (`POST /api/commands`, `POST /api/commands/batch` ≤50 independent in order, `GET /api/commands/{operationId}/ids`), DI in `CommandPlaneServiceCollectionExtensions`.
- **Tests:** `tests/Animora.Server.Tests/Commands/` — `CommandIntakeServiceTests`, `BatchTests`, `IntakeRecoveryServiceTests`, `RuleSetPolicyTests`, `CommandPayloadCatalogTests`, `ReservedCommandTypesTests`, PG-gated `CommandIntakeStoreTests`; Application `ServerChain` test.
- **Verification:** acceptance checked by grep only (chain unchanged, `ServerChain` without `CommandJournal`, `Version = 2`, no stale wire-version asserts, migration present, no `ExpectedAggregateVersion` in `Animora.Server`). Per user directive build/test/smoke were NOT executed in this phase; run `build.sh full`, `test.sh Server`, `test.sh Application`, `run.sh` first thing in the next session.

### S05 — Tenant context & authorization policies (2026-10-09)

**What exists now.** (projects touched: `Animora.Contracts.Wire` (`PublicKeySet`/`PublicKeyInfo`, `CapabilitySnapshotResponse`, `DeviceRevokeRequest`), `Animora.Persistence.PostgreSql` (`SigningKey`, `UserLookup`/`UserLookupContext`, migration `103_SigningKeys`), `Animora.Server`, `Animora.Server.Tests`; **no error codes added** — existing `ErrorCodes.*` reused; `Animora.Domain`/`Application`/`Persistence.Sqlite`/`Desktop` untouched)
- **Table / migration `103_SigningKeys`:** global (non-tenant, no filter) `SigningKeys` (`KeyId` ULID PK, `Purpose` snapshot|license, `Algorithm`, `PublicKeyDer`, Data-Protection-protected `ProtectedPrivateKey`, `CreatedUtc`/`NotAfterUtc`/`RetiredUtc`); `ServerSchemaVersion.ExpectedHead = "103_SigningKeys"`.
- **Tenant binding (`Authorization/`):** `TenantContextMiddleware` (after `UseAuthentication`; skips `/health/*`, `/auth/login|refresh|keys` and anonymous requests; services resolved lazily from `RequestServices` so skipped routes never touch the DB) → `IMembershipResolver`/`MembershipResolver` (primary-key `IUserLookup` → stamp (401 `SESSION_EXPIRED`) → disabled → **bind tenant from the stored user** → active membership → tenant status → device); no cross-request cache. `RequestTenantContext` replaces `AuthTenantContext`; `IRequestAuthorizationState` caches the single resolve. A differing inbound tenant hint (token `tid` / `X-Tenant-Id` / `?tenantId`) is audited as `security.tenant_hint_ignored` and ignored.
- **Authorization:** server PG `ServerStaffMembershipRepository`/`ServerRoleDefinitionRepository` feeding the Application `PermissionEvaluator` (role ∪ extra − denied, deny wins, unknown → `PERMISSION_UNKNOWN`); `PermissionRequirement` + handler, `ServerPermissionPolicyProvider` (policy name = `PermissionCodes.*`), coarse `TenantMember`/`ManagerOnly`/`OperatorOnly` (denies all until S14)/`DeviceActive`, `ServerAuthorizationResultHandler` (401/403 as `WireApiError`), `ICommandAuthorizer` + frozen **empty** `CommandPermissionTable` (unmapped command ⇒ denied).
- **Entitlement slot:** `IServerEntitlementGate` / `AllowAllServerEntitlementGate` (TryAdd; S16 replaces) + `EntitlementMiddleware` after `UseAuthorization()` (non-success ⇒ 403 `ENTITLEMENT_REQUIRED`).
- **Signing / devices (`Signing/`, `Auth/DeviceEndpoints`):** `ISnapshotSigner`/`EcdsaSnapshotSigner` (ECDSA-P256, IEEE-P1363), `SigningKeyStore` (creates the first key, DP-protected private half), deterministic `CapabilitySnapshotCanonicalizer`; `DeviceAdminService` (approve; revoke **rotates the owner security stamp**; capabilities omit Critical codes, expiry = min window capped at 30 d); `POST /auth/device/{id}/approve|revoke` (`devices.manage`), `POST /auth/capabilities` (`DeviceActive`), anonymous `GET /auth/keys` (public halves only).
- **Pipeline (`Program.cs`):** GlobalException → HSTS/HTTPS → `UseAuthentication` → `TenantContextMiddleware` → `UseAuthorization` → `EntitlementMiddleware` → `UseRateLimiter` → endpoints. `AddAnimoraAuthorization()` after `AddAnimoraIdentity`; `AddAnimoraApplication()` deliberately not called server-side.
- **Tests (`tests/Animora.Server.Tests/Authorization/`):** `TenantContextMiddlewareTests`, `AuthorizationTests`, `PlaneOrderTests`, `CapabilitySnapshotTests`, `DeviceEndpointsTests`, `KeysEndpointTests`, `CommandPermissionCatalogTests` (`EveryCommandType_HasARequiredPermission` skipped until S07/S10), PG-gated `AuthorizationPgTests` (`SigningKeyMigration_Applies`, `MembershipResolver_UsesOnlyPrimaryKey_NoCrossTenantEnumeration`). Real pipeline over in-memory fakes (`AuthzWorld`/`AuthzHost`). `Server.Tests` = 175 passed / 18 skipped (PG-gated + S10 skip).
- **Verification:** `build.sh full` 0 warnings / 0 errors, `test.sh Server` green, `run.sh` → `SMOKE RUN OK`; acceptance greps clean (no `role` claim read, `tid` only compared/audited in the middleware); `git diff --stat` over `Domain`/`Application`/`Persistence.Sqlite`/`Desktop` empty.

### S04 — Identity server (2026-10-09)

**What exists now.** (projects touched: `Animora.Domain` (`Tenant.Code` only), `Animora.Contracts.Wire`, `Animora.Persistence.PostgreSql`, `Animora.Persistence.Sqlite` (one `Ignore`), `Animora.Server`, `Animora.Server.Tests`; no error codes added — existing ones reused)
- **Tables / migration `102_Identity`:** `Users`, `StaffMemberships`, `RoleDefinitions`, `Devices` (all `SyncableEntity`, version trigger each), `RefreshTokens` (hash only, unique `TokenHash`), append-only `SecurityAudit` (row trigger raises on UPDATE/DELETE + statement trigger on TRUNCATE), `Tenants.Code` (+ partial unique `UX_Tenants_Code`). Permission-code sets are stored inline as `text[]` (no `+2 / +1` child tables — see deviation log). `ServerSchemaVersion.ExpectedHead = "102_Identity"`.
- **Persistence (`Identity/`):** `ServerIdentityUser` (adapter, never an EF entity), `ServerUserStore` (Identity store over the aggregate; the aggregate owns lockout), `IIdentityRepository`/`EfIdentityRepository`, `IRefreshTokenStore`/`EfRefreshTokenStore`, `RefreshTokenService` (wire token `{tenantId}.{256-bit base64url}`, SHA-256 stored, atomic conditional-UPDATE rotation, family revoke on reuse), `ITenantDirectory`/`TenantDirectory` (own unfiltered `TenantDirectoryContext`, the only unfiltered `Tenants` read), `ISecurityAuditWriter`/`SecurityAuditWriter`, `SecurityAuditDetail` (rejects secret-looking keys), `AuthTenantContext` (explicitly bound; S05 replaces), `TenantProvisioner`.
- **Server (`Auth/`):** `AddAnimoraIdentity`, `AuthEndpoints` (401 credentials/session, 403 disabled/revoked-device/suspended, **423** locked, 400 validation, 409 conflict; body = `WireApiError` + correlation id, `Cache-Control: no-store`), internal `LoginService`, `BearerAccessTokenIssuer` (framework bearer ticket, 15 min), claims `sub|tid|did|sst|role` (identification only — `role` is not `ClaimTypes.Role`), `AuthenticatedCaller`. `Program.cs`: `AddAnimoraIdentity` after step 4, `UseAuthentication()` in the authN slot, `MapAuthEndpoints()`.
- **Seams:** `ITenantDirectory`, `ISecurityAuditWriter`, `TenantProvisioner`, `RefreshTokenService`, `IIdentityRepository`, `IRefreshTokenStore`, `IAccessTokenIssuer`.
- **Tests (`tests/Animora.Server.Tests/Identity/`):** `ServerUserStoreTests`, `LoginServiceTests`, `RefreshTokenServiceTests`, `AuditRedactionTests`, `TokenClaimsTests`, `AuthEndpointsTests` (real pipeline, in-memory fakes) + PG-gated `IdentityMigrationTests`, `AuthFlowTests`. `Server.Tests` = 143/143 against a local PostgreSQL 17 (PG facts skip without `ANIMORA_PG_TEST_CONNECTION`).
- **Verification:** targeted `test.sh` runs (whole solution compiles via `test.sh`); acceptance greps clean (no `JwtBearer`/`OpenIdConnect`, no business `Guid.NewGuid()`); `Persistence.Sqlite` diff = one `Ignore(Code)` line, `Desktop` untouched.

### S03 — Server data layer & tenancy II (2026-10-09)

**What exists now.** (project `src/Animora.Persistence.PostgreSql`; no schema/migration added, no error codes added)
- `Transactions/`: `ServerUnitOfWork : IUnitOfWork` (scoped; `SaveAsync` maps `DbUpdateConcurrencyException` → `CONCURRENCY_CONFLICT`;
  `ExecuteAsync` delegates to the runner), `ITransactionRunner` + `ServerTransactionRunner` (EF execution strategy → `ReadCommitted`
  transaction → `ITenantSessionInitializer` on every begin/retry → work → commit on success / rollback on failure Result, never throwing
  for a handler failure), `RetryPolicy` (pure: `IsTransient` 40001/40P01, `BackoffFor` 50–250 ms, `MaxAttempts=3`, cancellation rethrown),
  `UniqueViolationMapping` (`None` default / `Enabled` → `23505` ⇒ `CONCURRENCY_CONFLICT`/"unique").
- `Tenancy/`: `ITenantSessionInitializer` + `NoopTenantSessionInitializer` (RLS OFF; future RLS impl must run a parameterized
  `set_config('animora.tenant_id', @t, true)` per transaction begin AND retry).
- `Repositories/`: `TenantRepository<TEntity>` (abstract; tracked `GetAsync`, `AsNoTracking` `ReadAsync`, `Page` via `PageRules`, `Add`,
  `SoftDelete`; no `IgnoreQueryFilters`, no un-paged list) and `PageRules` (`MaxPageSize=200`, `ClampPageSize`, `NormalizePage`).
- `Context/`: `IServerReadContextFactory` + `ServerReadContextFactory` (scoped; `oltp-read` data source, `NoTracking`, same tenant
  context + the two interceptors).
- DI (`AddAnimoraPostgreSql`): `ITenantSessionInitializer`→Noop (singleton), `ITransactionRunner`, `IUnitOfWork`, `IServerReadContextFactory` (scoped).
- Tests (`tests/Animora.Server.Tests/Persistence/`): `PageRulesTests`, `RetryPolicyTests` (no DB), `SessionInitializerContractTests`,
  `TenantIsolationTests` (PG-gated, 6 facts, RLS off, probe entity via `EnsureCreated` in the fixture only).
- **Verification:** `build.sh full` 0 warnings / 0 errors; `test.sh` Server tests green (PG facts skipped without `ANIMORA_PG_TEST_CONNECTION`);
  `run.sh` SMOKE RUN OK; acceptance greps clean.

### S02 — Server data layer & tenancy I (2026-10-09)

**What exists now.**
- New project `src/Animora.Persistence.PostgreSql` (refs Domain + Application + Contracts.Wire; the **only** Npgsql
  consumer; `InternalsVisibleTo Animora.Server.Tests`), added to `Animora.sln` and referenced by `Animora.Server` for DI wiring only.
- **Tables:** `public."Tenants"` (PK `Id`, index `IX_Tenants_TenantId_Status`). **Function** `animora_set_version()`;
  **trigger** `trg_Tenants_version` (BEFORE INSERT OR UPDATE → new `gen_random_uuid()` into `"Version"`, so raw SQL also bumps it).
  **Key ring:** table `DataProtectionKeys`, own history table `__EFMigrationsHistory_KeyRing`.
- **Migration heads:** server `101_ServerFoundation`, key ring `101_KeyRing` (client SQLite head stays `031`, untouched).
  Runtime never calls `Migrate()`; `SchemaProbe` only compares the applied head to `ServerSchemaVersion.ExpectedHead`.
- **Public seams:** `ITenantContext` (+`NullTenantContext` default), `TenantContextMissingException`, `TenantMismatchException`,
  `TenantStampingInterceptor`, `NoUnnamedIgnoreFiltersInterceptor`, `ServerModelConventions.ApplyTenancy/TenantIndex`
  (named filters `Tenant` + `SoftDelete`; `Version` = `uuid NOT NULL` concurrency token via `StringGuidConverter`, never xmin/rowversion;
  `UpdatedAt` = `timestamptz(6)` UTC), `VersionTrigger.Apply/Drop`, `EntityVersionCodec`, `DatabaseOptions`
  (+`DatabaseOptionsValidator`, pool budget 12 @ 2 GiB / 20 @ 4 GiB), `AddAnimoraPostgreSql`, keyed `NpgsqlDataSource`s
  `oltp-write | oltp-read | keyring` (`MinPoolSize=0`, `CommandTimeout=30`, no retry strategy until S03), design-time factories
  (`ANIMORA_PG_DESIGN_CONNECTION`, never connects).
- **Server:** `Program.cs` step 3 `AddDataProtection().SetApplicationName("Animora.Server").PersistKeysToDbContext<AnimoraKeyRingDbContext>()`,
  step 4 `AddAnimoraPostgreSql`, step 5 probes `database|keyring|schema` (internal, 2 s timeout, degrade to Unhealthy — host boots
  without PostgreSQL). `appsettings.json` has a `Database` section with only `ProfileGiB`/pool sizes (connection string is env-only
  `Database__ConnectionString`).
- **No new ErrorCodes** (reuses `TenantMismatch`); no Domain/Sqlite/Desktop change.

**Tests (`tests/Animora.Server.Tests`, 51 pass + 3 PG-gated skipped locally):** `DatabaseOptionsValidatorTests`,
`EntityVersionCodecTests`, `ServerModelTests`, `TenancyTests` (stamping, mismatch, operator, context-missing, ignore-filter ban),
`PgGatedTests` (`Applies101_OnEmptyDatabase`, `Version_ChangesOnEveryUpdate_IncludingRawSql`, `KeysSurviveContextRecreation`;
need `ANIMORA_PG_TEST_CONNECTION`, fail under `ANIMORA_REQUIRE_PG=1` without it), `SourceScanTests` extended
(no unnamed `IgnoreQueryFilters()`, no rowversion/xmin, no `EnableLegacyTimestampBehavior`, Npgsql confined to the PG csproj),
`HostSmokeTests` (DB probes stubbed for the S01 cases + new "PostgreSQL absent ⇒ 503 naming database/keyring/schema"),
`ServerLayeringTests` (allows only `Animora.Persistence.PostgreSql` in addition).

**Acceptance verified:** `scripts/sandbox/build.sh full` 0 warnings / 0 errors; `scripts/sandbox/run.sh` → `SMOKE RUN OK`;
Npgsql references only inside `Animora.Persistence.PostgreSql`; `git diff` on Sqlite/Domain/Desktop empty.

### S01 — Server host scaffold (2026-10-09)

**What exists now.**
- New project `src/Animora.Server` (`Microsoft.NET.Sdk.Web`, net10.0 inherited), refs `Animora.Domain` +
  `Animora.Application` + `Animora.Contracts.Wire` only (no persistence, no Npgsql, no client project).
  `InternalsVisibleTo Animora.Server.Tests`. Added to `Animora.sln`.
- `Program.cs` carries the fixed numbered startup block (1 options+`ValidateOnStart`+`SecretGuard`, 2 logging,
  3 DataProtection → S02, 4 DbContext → S02, 5 readiness probes, 6 OpenAPI Development/opt-in, 7 middleware:
  global exception → HSTS/HTTPS redirect (non-Development) → [tenant/authN/authZ/entitlement slots S04/S05] →
  rate limiter → endpoints) and `public partial class Program;`.
- `Hosting/`: `ServerOptions` (+ `RateLimitOptions`/`RateLimitWindowOptions`), `ServerOptionsValidator`,
  `SecretGuard` (+ `SecretInConfigurationException`; scans JSON file **and** JSON stream providers, env/user-secrets
  exempt), `GlobalExceptionMiddleware` (→ 500 `WireApiError` `UNEXPECTED`, no stack outside Development,
  client-abort = no body), `CorrelationHeader` (`X-Correlation-Id`, ≤64 `[A-Za-z0-9-_]`, else ULID),
  `IReadinessProbe`/`ReadinessResult`, `DataRootWritableProbe`, `RateLimitPolicies` (`auth|commands|sync|files|operator`),
  `ServerRateLimiting` (the `AddRateLimiter` configuration, extracted from `Program.cs` so it is unit-testable),
  `ServerRoutes`, `ServerErrorCodes` (`UNEXPECTED`, `RATE_LIMITED` — transport strings, not Domain codes).
- `appsettings.json` (non-secret: Logging, `Server:RateLimits`) / `appsettings.Development.json`
  (`EnableOpenApi=true`, `DataRootPath=./.data`).
- Pins added to `Directory.Packages.props`: `Microsoft.AspNetCore.OpenApi` 10.0.12, `Microsoft.AspNetCore.Mvc.Testing` 10.0.12.
- No migration, no table, **no ErrorCodes added to Domain**, no wire DTO.

**Tests (`tests/Animora.Server.Tests`, 23 green):** `HostSmokeTests` (live / ready 200 / ready 503 names probe only),
`SecretGuardTests` (literal conn-string throws, non-empty secret key throws, env provider allowed, shipped appsettings clean),
`GlobalExceptionMiddlewareTests` (500 WireApiError no stack in Production, message only in Development, correlation echo/replace),
`RateLimitTests` (429 + `Retry-After` + `RATE_LIMITED`, policy independence, defaults, validator), `ServerLayeringTests`
(no client refs, only Domain/Application/Contracts.Wire, no Npgsql), `SourceScanTests` (no `SslProtocols.`, no `Npgsql`).
`tests/Animora.Domain.Tests/LayeringRulesTests` gained `Client_DoesNotReferenceServerProjects`.

**Acceptance verified:** `scripts/sandbox/build.sh full` 0 warnings / 0 errors; `Animora.Server.Tests` 23/23;
`scripts/sandbox/run.sh` → `SMOKE RUN OK`; `grep SslProtocols\.`/`Npgsql` under `src/Animora.Server` empty;
`AddRateLimiter` present; client projects untouched except the build fix in the deviation log.

### C11 — Mock purge and scoped offline acceptance (2026-09-30)
- **Repo state.** Real `LocalSyncStatusService` is the sole `ISyncStatusService` registration in
  `Animora.Client.Services`. It reads durable tenant/scope `SyncCursor` through Application ports,
  recomputes pending state, serializes one `LocalCommandProcessor` pass per request, and handles
  cancellation/errors without simulated delays. Null transport reports Offline; login refresh loads
  the cursor without a processor pass. **E04 replaces the internals, not the registration**; E03 can
  extend the cursor. `[LocalOnly] SyncCursor`, unique `(TenantId, Scope)` configuration, migration
  **`019_SyncCursor`** (`20260929195619`), Designer and model snapshot are committed. Ordinal 018 was
  already occupied by `018_UserPreferences`, hence the deliberate 018→019 deviation.
- **mock deleted: `MockSyncStatusService.cs` — Track C mock purge complete (22 of 22)**.
  `src/Animora.Desktop/Mocks/` no longer exists; tracked Desktop C#/XAML contains no executable Mock
  identifiers. Historical lowercase source comments are not executable registrations.
- **Machine guards.** `LayeringRulesTests` adds mock namespace/type purge, exactly-one registration
  for every one of 24 public Contracts interfaces (four explicit pure-UI exceptions remain Desktop),
  and forbidden EF/DbConnection/HTTP/Datasync references in Desktop/Client.Services. Registration-only
  `App.ConfigureServices(IServiceCollection)` is shared by production and audit; Domain.Tests adds a
  Desktop project reference. The audit exposed EF in `DraftStateStore`: moved it behind `IUiDraftStore`
  with the persistence adapter using explicit tracking. Desktop's missing `AddCashierUseCases` fixed.
  No guard weakened and no Contracts interface omitted.
- **Continuous migrated-file scenario.** `OfflineClinicDayScenarioTests` uses controlled `IClock`
  and a real file DB: (1) first-run admin; (2) owner/pet; (3) next-day appointment/check-in;
  (4) reception/admission/visit; (5) vitals/diagnosis/two Rx lines/completion; (6) handoff/invoice/product;
  (7) cash session/two partial payments; (8) Paid + two payments + exact unsettled journal operation IDs;
  (9) exact stock delta, snapshot equals movement recomputation; (10) balanced ledger and revenue;
  (11) nine ordered activity events; (12) zero-variance cash close; (13) same-file reopen and numeric
  reassertions; (14) boarding checkout/invoice/payment with stock/ledger/feed and room-history/reopen
  consequences. Assertions use codes/IDs/numbers only. `StartupBudgetTests` verifies readiness-gated,
  off-thread store work after window opening, not pre-frame migration/seeding/rebuild.
- **Scenario defects repaired.** Anonymous expired-session renewal matches AuthenticationService;
  ready-to-bill scalar UNION avoids SQLite translation failures; catalog lookup avoids unsupported APPLY;
  stock snapshots and billing handoffs explicitly track persisted updates; UoW buffers movement events
  across intermediate audit flushes, dispatches once transactionally and clears on completion/rollback;
  boarding invoice posting atomically attaches the invoice and completes checkout. Checkout feed follows
  invoice issuance, as required by the domain's no-unbilled-checkout invariant.
- **Final short verification.** Persistence `StockSnapshotIntegrityTests` + `UnitOfWorkTests`: **9/9**,
  7 s; Application scenario/startup/sync/compensation/single-ledger-writer/post-invoice: **16/16**, 9 s;
  `LayeringRulesTests`: **7/7**, 167 ms. Final invocations exit 0, **32/32 total**, no skipped tests.
  Sequential targeted Desktop build **0 warnings / 0 errors**; Application/Persistence/Domain.Tests
  dependencies compiled. Logs: `.sandbox/logs/c11-final-{persistence,application,layering,desktop-build}.log`.
  **No full build, full suite or `test.sh` run/green claim**, by explicit user instruction.
- **Offline smoke evidence (T8).** `unshare -Urnm .sandbox/offline-exec.sh scripts/sandbox/run.sh
  --no-build --seconds 6` exited 0, **SMOKE RUN OK**, only `lo` DOWN, no addresses/routes. Xvfb/GUI used
  Unix sockets with no server/interface required. Advanced-branch task evidence captured this run;
  log path `.sandbox/logs/c11-offline-smoke.log` refers to that execution, not a checked-in log.
- **Measurements.** `bench.sh offline` records six metrics and exits non-zero on missing/runtime failures
  or >2x measured budgets (not CI timing assertions). Final recorded invocation exit 0, `wall_ms=60053`,
  `peak_rss_kb=292512`, process alive. CPU Intel Xeon @ 2.50 GHz, host RAM 1008784 kB, Debug .NET 10.0.401,
  Xvfb, workstation GC 576 MiB. No external interface/routes; namespace-local loopback enabled solely
  for VSTest IPC. Report: `backend-roadmap/measurements/C11-offline-budget.md`.

  | Metric | Measured | Budget | Result / scope |
  |---|---:|---:|---|
  | First visible frame | 1564.504 ms | 1200 ms | OVER BUDGET, <2x; Main→LoginWindow.Opened proxy |
  | Dashboard data | 3.442 ms | 2500 ms | PASS; warm query, not first-frame→render |
  | 60 s idle working set | 285.656 MiB | 300 MiB | PASS; login, not authenticated shell |
  | Patients page (200 rows) | 2.163 ms | 120 ms | PASS; warm real query |
  | Global search keystroke | 0.624 ms | 80 ms | PASS; production normalized query, eight pet hits |
  | Post invoice (10 lines) | 26.857 ms | 250 ms | PASS; production command/journal/UoW/ledger |

- **Limits retained.** Initial cold-cache startup **6215.490 ms** correctly failed >2x; initial RSS
  **327.293 MiB** had a too-short 59683 ms interval, not a valid 60 s sample. Millisecond deadline and
  Xvfb error evidence fixed; warm rerun is not a cold-start certification. Manual authenticated 17-screen
  data check and rendered-dashboard/authenticated-shell measurements are **left to reviewer**. No claim
  that the full UI budgets passed; future work must address the cold-start observation. These scoped
  verification deviations do not alter the roadmap phase file or the independent historical C06 debt.

### C10 — History, Dashboard, Global search & Profile: read surfaces off the C09 facts (2026-09-29)
- **What shipped.** C10 is the Application/Persistence/Client-services layer for the last four Track C
  surfaces — the activity **history** feed + CSV export, the role-aware **dashboard**, five-source
  **global search**, and the user **profile/settings** — all reading the real local DB (and the C09
  fact tables) and **adding no new ledger writer**. It is the final swap: every Track C screen is now
  off its mock.
- **Materialized activity feed (T01–T03).** New `[LocalOnly]` entities `ActivityEvent` (the
  materialized feed row) and `UserPreference` (per-user settings), with EF configurations and
  migrations **`017_ActivityFeed`** + **`018_UserPreferences`** — numbered 017/018 not the phase's
  015/016 because 016 was already taken by C09 (**deviation D-01**; `.Designer.cs` +
  `AnimoraLocalDbContextModelSnapshot` updated). `ActivityEventKeys` holds the stable,
  language-independent title keys; `ActivityFeedMapper` maps **14** domain event types
  (`PetRegistered`, `VisitCompleted`, `VaccinationAdministered`, `AppointmentBooked`,
  `AppointmentCancelled`, `StayActivated`, `StayCheckedOut`, `GoodsReceiptPosted`,
  `StockMovementRecorded`, `InvoicePosted`, `PaymentRegistered`, `InvoiceRefunded`,
  `ChequeStateChanged`, `ExpensePosted`) to exactly one `ActivityEvent` each, with a documented
  key / JSON args / absolute amount / sign / status / entity ref. `IActivityFeedWriter` appends on the
  OLTP path; `ILocalStoreMaintenance.RebuildActivityFeedAsync` replays the B02 outbox through the same
  mapper so a rebuild reproduces the live feed **byte-for-byte** (proven by `ActivityFeedTests`), and
  is run at the end of seeding.
- **History query + export (T04).** History feed query (filter + paging) + `CsvWriter` producing
  **UTF-8-BOM** CSV that renders Persian correctly in Excel; the export path enforces a row cap
  (50 001 rows → `EXPORT_TOO_LARGE`). Client-side `HistoryText` localizes the stored keys.
  **Resolution:** the phase text's `ExportCsv` contract vs. the file-path export were reconciled — the
  service returns the CSV payload and the path is the caller's concern (recorded in the deviation log).
- **Dashboard (T05).** `DashboardQueries.GetSliceAsync` = 7 reads + `VersionAsync` = 1 → a firm
  **≤8-statement budget** per snapshot; `DashboardComposer` builds the slice; `DashboardMapping.Map`
  is **role-gated** — a manager gets the revenue pulse, doctor loads and species donut; a doctor gets
  none of the manager widgets. Manager "today revenue" reads the same figure as the C09 finance fact
  for that day (asserted by `DashboardBudgetTests`).
- **Global search (T06).** `GlobalSearchQueries` issues **one** raw-ADO `UNION ALL` over five sources
  (owners, pets, invoices, products, appointments), ordered by a static source rank. Matching is on the
  normalized `SearchKey` (stamped on `SaveChanges`), so Persian-digit phone strings and Arabic-form
  names both match ASCII/normalized terms; terms shorter than 2 chars short-circuit to no query; each
  source is capped (per-source limits). Appointments are discovered through the joined pet's key.
- **Profile / settings (T07).** Profile + settings queries and **non-journaled** save commands
  (`UserProfileService`) — settings are user-scoped preferences, not audited business events, so they
  intentionally bypass the command journal; they survive restart, and signing out another session
  invalidates it.
- **DI + cleanup (T08).** 4 DI swaps (`IHistoryQueryService`, `IDashboardQueryService`,
  `IGlobalSearchService`/`ISearchService`, `IUserProfileService` → real), 4 ViewModel readiness-gate
  lines, and **4 mocks deleted: `MockHistoryQueryService.cs`, `MockDashboardQueryService.cs`,
  `MockSearchService.cs`, `MockUserProfileService.cs`** (no remaining references). `ErrorCodes` += 3.
- **Tests (T09).** 3 classes, **21 tests, all green** (targeted run only, no full suite):
  `ActivityFeedTests` (Application.Tests) — the 14 event types each produce exactly one `ActivityEvent`
  with the documented key/amount/sign, rebuild-equals-live, and ASCII/normalized titles;
  `DashboardBudgetTests` (Application.Tests) — ≤8 statements/role, manager revenue == the finance fact,
  doctor snapshot carries no manager widget; `GlobalSearchQueryTests` (Persistence.Tests) — single
  statement covering all five sources, Persian-digit + Arabic-form matching, 1-char no-op, per-source
  limits, and owner-before-pet source ranking.
- **Verified.** `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Client.Services` each
  build **0 warnings / 0 errors**; the 3 targeted test classes run **green (21/21)**. (Full build/
  smoke intentionally skipped this session per the targeted-only directive.)
- **Deviations.** **D-01:** migrations numbered `017`/`018` instead of the phase's `015`/`016` because
  `016` was consumed by C09. The `ExportCsv` contract-vs-path shape was resolved in favour of returning
  the CSV payload (see above).

### C09 — Finance & reporting read models: one ledger writer + projected facts (2026-09-29)
- **What shipped.** C09 is the Application/Persistence/Infrastructure/Client-services layer that turns
  the A08 finance domain into a **live, balanced, append-only double-entry ledger written by exactly
  one component** and read through **projected fact tables that never ride the OLTP path**. The finance
  screen now shows real overview/income/expense/profit numbers derived from those facts; expenses are
  real; and C01's owner-total placeholder is filled from a real snapshot.
- **Single ledger writer.** `Animora.Application/Finance/LedgerPostingHandler.cs` is the **only** type
  in the solution that constructs a `JournalEntry`. It implements `IDomainEventHandler<T>` for **10**
  events — `InvoicePostedEvent`, `InvoiceRefundedEvent`, `InvoiceVoidedEvent`, `PaymentRegisteredEvent`,
  `StockMovementRecordedEvent`, `GoodsReceiptPostedEvent`, `ChequeClearedEvent`, `ChequeBouncedEvent`,
  `CashSessionClosedEvent`, `ExpensePostedEvent` — each building the entry through `PostingRules` (A08)
  and appending it **inside the raising transaction** (A10's dispatcher runs handlers there). Balance is
  enforced twice: `JournalEntry.EnsureBalanced()` (domain invariant) and `AppendOnlyGuard` (A10); an
  unbalanced entry cannot be saved (`LEDGER_UNBALANCED`). `LedgerSingleWriterTests` scans
  `Animora.Application` + `Animora.Client.Services` and asserts no other type constructs `JournalEntry`.
- **Entry numbering.** `JournalNumberAllocator` (per-period `journal:<tenant>:<periodCode>` over B02's
  `LocalSequenceCounters`, mirroring C07's invoice allocator) and `ExpenseNumberAllocator` for expenses.
- **Fact read models (A-RULE 034).** Three `[LocalOnly]` projection tables — `DailyFinanceFact`
  (per `TenantId`+`LocalDay`: revenue/COGS/expense/discount/insurance/refund + invoice counts, unique
  `IX_DailyFinanceFacts_TenantId_LocalDay`), `ProductSalesFact` (per product/variant/day) and
  `OwnerFinancialSnapshot` (per owner: total spend, open balance, last invoice, count) — plus migration
  **`016_FinanceProjections`** (`.Designer.cs` + snapshot updated). They are maintained **outside** the
  OLTP critical section by `FinanceProjectionWorker` (Infrastructure.Local — 1 worker, batched, idle
  backoff, drains the B02 outbox) via `FinanceProjectionStore` + `FinanceProjectionRebuilder`.
  `ILocalStoreMaintenance.RebuildFinanceProjectionsAsync` recomputes all three from the ledger; it runs
  at the end of seeding (so a fresh install proves the rebuild path from C05–C08 sample data) and from
  the restore / quarantine-rebuild paths. `FinanceProjectionIntegrityTests` asserts maintained ==
  recomputed and that a rebuild repairs a corrupted fact — projections are a cache with a proof; the
  ledger is the truth.
- **COGS basis.** Each `MovementKind.Sale` movement carries `UnitCostMinor` (written by C07 from the
  latest receipt), so COGS is a plain sum over movements — documented as **FIFO-by-latest-receipt-cost**,
  no valuation-method choice left open.
- **Queries & expenses.** 7 fact-only reporting queries (`FinanceReportingQueries` + persistence-side
  `FinanceQueries`, single grouped statements, deltas vs. the preceding equal window, cached in
  `TenantScopedCache` with a tenant|user|resource|`v<maxLocalDayInRange>` key so a new fact invalidates
  naturally). `RecordExpenseCommand` (journaled, permission-gated, auditable, raises `ExpensePostedEvent`
  → ledger + facts) and `CloseAccountingPeriodCommand` (forward-only close; a late event posts into the
  **next open period** with `BackdatedFromUtc`, never into the closed one → `PERIOD_CLOSED`).
- **Client seam & Desktop.** `FinanceQueryService` + `FinanceMapping` implement `IFinanceQueryService`
  by reading the facts only — no `Invoices`/`JournalLines` in a query. `PatientsMapping` now reads
  `OwnerFinancialSnapshot.TotalSpendMinor`, **closing C01's deferred `TotalSpend`** (the only C01 file
  touched). DI line swapped (`IFinanceQueryService → FinanceQueryService`), `FinanceProjectionWorker`
  registered and `.Start()`-ed after store init in `App.axaml.cs`, `FinancePageViewModel` gained the
  readiness-gate line, and **`MockFinanceQueryService.cs` deleted**.
- **Tests.** `PostingRulesTests` (Domain), `LedgerSingleWriterTests` + `PeriodCloseTests` (Application),
  `FinanceProjectionIntegrityTests` (Persistence).
- **Deviations.** (D1) migration is **`016_FinanceProjections`**, not the phase text's `015` (015 is
  C08's `CashSessionLink`; the migration numbers have run one ahead since C06's `013`). (D2)
  **`ReopenAccountingPeriodCommand` and its `finance.period.reopen` command type / `PERIOD_REOPEN_REASON_REQUIRED`
  code are not implemented** — only close + the late-event-into-next-open-period rule ship; reopen is
  deferred (revisit before Track D's server-side ledger). (D3) `CommandTypes` gained **2** entries
  (`finance.period.close`, `finance.expense.post`) — the expense type is `finance.expense.post`, not the
  phase text's `finance.expense.record`, and reopen is absent. (D4) the error-code set differs from the
  phase text: `PERIOD_CLOSE_BLOCKED` / `PERIOD_INVALID_TRANSITION` / `PERIOD_NOT_FOUND` +
  `EXPENSE_CATEGORY_INVALID` + `FINANCE_RANGE_INVALID` + `LEDGER_UNBALANCED` ship, and there is no
  `PERIOD_REOPEN_REASON_REQUIRED`. (D5) the `LedgerPostingHandler` event list resolves to the events the
  domain actually raises — `InvoiceRefundedEvent`/`InvoiceVoidedEvent` (not `InvoiceReversedEvent`),
  `StockMovementRecordedEvent`/`GoodsReceiptPostedEvent` (not `GoodsReceivedEvent`) — 10 handlers total.
  (D6) projections are maintained through `FinanceProjectionStore`/`FinanceProjectionRebuilder` behind
  the outbox worker rather than a single `FinanceProjectionHandler` type, same off-OLTP guarantee.
- **Verified.** `Animora.Application`, `Animora.Persistence.Sqlite` and `Animora.Client.Services` each
  build **0 warnings / 0 errors**; the four C09 test classes are in the tree. Full solution
  `build.sh full` / `test.sh` / `run.sh` re-run deferred to the C10 closeout (no SDK/heavy-build in this
  documentation session).
- **Out of scope (deferred):** dashboard/history/search/profile surfaces that read these same facts →
  C10; cross-tenant/operator reporting → E02; server-side ledger posting (re-derived from the same
  `PostingRules`) → D07; period **reopen** → deferred (D2).

### C08 — Cashier & Cheques: cash sessions, cheque lifecycle, ledger seam (2026-09-28)
- **What shipped.** C08 is the Application/Persistence/Client-services layer for the cashier and cheque
  screens. The cashier manages a real **cash session** (open/close with a counted drawer and a recorded
  variance) and settles invoices **through C07's commands verbatim**; the cheques screen runs the full
  cheque lifecycle with bank breakdown, cashflow projection and alerts, and raises the events C09's
  ledger consumes.
- **No second invoice path.** `CashierService` composes C07's existing commands
  (`RegisterPaymentCommand`, `PostInvoiceCommand`, the void/refund/line/discount/insurance commands);
  `CashierMapping` maps C07's read models onto the `Cashier*` DTO family. `CashierReusesSalesTests`
  asserts no type under `Animora.Client.Services/Cashier` references `IUnitOfWork`, a repository port or
  the DbContext.
- **Cash session.** `UseCases/Cashier/` adds `OpenCashSessionCommand` (refuses a second open session per
  `(TenantId, UserId)` → `CASH_SESSION_ALREADY_OPEN`), `CloseCashSessionCommand` (computes expected cash
  via `CashSessionQuery`, records `VarianceMinor = Counted − Expected` **recorded, never corrected**,
  raises `CashSessionClosedEvent`; a large variance without a note is refused) and a movement command for
  pay-in/pay-out. The variance thresholds live in `CashVarianceThresholds`. Every cash payment taken
  while a session is open is stamped with `CashSessionId` — the **only, additive** C07 change: a new
  `ICashSessionContext` port (+ `CashSessionContext` in Persistence) resolves the current open session,
  and `RegisterPaymentCommand` enforces `CASH_SESSION_REQUIRED` for cash payments only (non-cash methods
  unaffected). Schema: `Payment.CashSessionId` + `IX_Payments_TenantId_CashSessionId` + migration
  **`015_CashSessionLink`**.
- **Cheque lifecycle.** `UseCases/Finance/Cheques/`: `CreateChequeCommand`, `UpdateChequeCommand`
  (metadata only, refused once the cheque left `Registered` → `CHEQUE_NOT_EDITABLE`),
  `DeleteChequeCommand` (soft, `Registered` only), `ChangeChequeStatusCommand` (validated against the A08
  transition table) and `BulkChangeChequeStatusCommand` (executes the per-cheque command in **one** unit
  of work and returns a **per-id result list** — partial success reported, never swallowed). Each
  transition appends a `ChequeEvent` (append-only); **clearing raises `ChequeClearedEvent`, bouncing
  raises `ChequeBouncedEvent`** — together with `CashSessionClosedEvent`, these are **the events C09's
  ledger consumes**. This phase writes **no** journal entry itself (single ledger writer = C09).
- **Read models & reference data.** `ChequeCashflowQuery` (weeks bucketed through `ClinicCalendar`, not a
  SQL date function), `ChequeBankBreakdownQuery` (one `GROUP BY BankKey`), `ChequeAlertsQuery` (three
  rules from `ChequeAlertRules`: due-within-3-days / overdue-uncleared / bounced-unresolved).
  `BankDirectory` (18 Iranian banks) is reference data, seeded by `LocalSeeder`. `CashierQueries`
  (single grouped stats + tab-count statements) and `ChequeQueries` on the persistence side.
  `FinanceSampleData` (cheques across states + weeks, plus closed and open cash sessions) behind
  `SeedSampleData`.
- **Client seam & Desktop.** `CashierService`/`CashierMapping` and `ChequesQueryService`/`ChequesMapping`
  implement `ICashierService` / `IChequesQueryService`; 2 DI lines swapped, the 2 page ViewModels gained
  the readiness-gate line, and **`MockCashierService.cs` + `MockChequesQueryService.cs` deleted**.
- **Tests.** `CashSessionUseCaseTests`, `ChequeLifecycleTests`, `CashierReusesSalesTests` (Application).
- **Deviations.** (D1) migration is **`015_CashSessionLink`**, not the phase text's `014` (014 is C07's
  `SalesInsurers`). (D2) `CashVarianceThresholds.cs` lives under `src/Animora.Domain/Cashier/`, not the
  phase text's `src/Animora.Domain/Sales/`. (D3) `CommandTypes` gained **8** entries — 3 cashier
  (`cashier.session.open/close`, `cashier.movement.record`) + 5 cheque (`finance.cheque.create/update/
  delete/status` **plus** a `finance.cheque.transition` type); the phase table anticipated 7. (D4) the
  cheque error-code names differ from the phase text: `CHEQUE_INVALID_TRANSITION` (not
  `CHEQUE_TRANSITION_INVALID`), `CHEQUE_COUNTERPARTY_REQUIRED`, `CHEQUE_NOT_EDITABLE`,
  `CHEQUE_NOT_FOUND`, plus the cash codes `CASH_SESSION_ALREADY_OPEN` / `CASH_SESSION_REQUIRED` /
  `CASH_SESSION_CLOSED` / `CASH_SESSION_UNSETTLED_COMMANDS`.
- **Verified.** `Animora.Application`, `Animora.Persistence.Sqlite` and `Animora.Client.Services` each
  build **0 warnings / 0 errors**; the three C08 test classes are in the tree. Full solution gates
  re-run deferred to a later closeout (no heavy build in this documentation session).
- **Out of scope (deferred):** journal entries from cheque/cash-session events → C09 (single ledger
  writer); invoice creation/editing semantics → C07 (reused, not redefined); server-side settlement of
  these commands → D07.

### C07 closeout — Sales / POS posted through the journaled pipeline (2026-09-28)
- **What shipped.** C07 is the Application/Persistence/Client-services layer that drives the already-finished
  A07 `Invoice` aggregate. The POS now creates and posts **real invoices** from three sources — a completed
  visit's `VisitBillingHandoff`, a checked-out stay's `BoardingBillingHandoff`, or blank (`InvoiceKind.Visit` /
  `Boarding` / `DirectSale`) — with catalog lines, custom lines, discounts, insurance and payments. **Posting is
  a journaled command through the B02 pipeline, never CRUD**, and is the single place a sale's stock
  consumption movement is written.
- **Schema.** Migration `014_SalesInsurers` (numbered 014 because 013 is C06's `InventorySnapshot`, D1) adds the
  `Insurers` table with a unique `Code` index; `.Designer.cs` + `AnimoraLocalDbContextModelSnapshot` updated.
  `LocalSeeder` seeds 6 insurers. `SalesSampleData` (marker behind `SeedSampleData`) adds 30 invoices across
  Draft/Posted/PartiallyPaid/Paid/Void with payments and matching `Sale`/`ReturnIn` movements.
- **Persistence read side.** `ISalesQueries`/`SalesQueries`: ready-for-billing `UNION ALL` over both handoff
  tables (`HandoffStatus.Ready`, newest first, max 100 — filter on the entity before projecting, the C05 lesson),
  a paged invoice list, invoice detail (lines + payments + adjustments + the settlement LEFT JOIN to the
  `PendingCommand` row for `SettlementStatus`/`LastSettlementErrorCode`/`ResponsibleRole`), `SalesCatalog`,
  `Insurers`, a one-statement `SaleKpi`, and `SalesHistory(CashierHistoryFilter)` (page + total + inflow/outflow
  sums over the whole filtered set, PERFORMANCE §6). `IInvoiceNumberAllocator`/`InvoiceNumberAllocator` reuse
  B02's `LocalSequenceCounters` (`PipelineSql.NextSequenceAsync`, counter `invoice:<tenant>:<jalaliYear>`, format
  `{yy}-{seq:000000}`, joins the ambient transaction).
- **Application use cases.** 6 read models; draft-editing commands (create ×3, line add + add-custom, line
  edit/remove with the **C05 stale-index rule** → `INVOICE_LINE_INDEX_STALE`, discount, insurance resolved from
  `Insurer.CoveragePercent` capped by `CeilingMinor`, owner, delete-draft); `PostInvoiceCommand` — the 7-step
  journaled posting (recompute totals → allocate number → `Invoice.Post(clock)` → one `MovementKind.Sale` per
  stockable line with `UnitCostMinor` copied from the latest receipt → mark the handoff `Invoiced` →
  `InvoicePostedEvent` → `CommandJournalBehavior`); `RegisterPaymentCommand`; and the compensating
  `RefundInvoiceCommand`/`ReturnInvoiceLineCommand`/`VoidInvoiceCommand`. `SalesRegistration` wires handlers,
  validators and the persistence-side ports.
- **Client seam & Desktop.** `SalesMapping` + `SalesQueryService` implement all **22** `ISalesQueryService`
  members by dispatching commands/queries and mapping — **no EF/SQL/business rule**. The documented
  posting-moment behavior is encoded: `TakePaymentAsync` posts a still-`Draft` invoice first (one transaction)
  then registers the payment; `VoidAsync` on a posted invoice issues a reversal; `DeleteInvoiceAsync` is
  draft-only (a posted invoice surfaces `INVOICE_IMMUTABLE`). DI line swapped
  (`ISalesQueryService → SalesQueryService`, `App.axaml.cs:302`), `SalePageViewModel` gained the
  `IStoreReadinessGate` line, and **`MockSalesQueryService.cs` deleted** (no remaining reference).
- **Tests.** `InvoiceImmutabilityTests` (Domain), `PostInvoiceCommandTests` + `SalesCompensationTests`
  (Application), `InvoiceNumberAllocatorTests` (Persistence). Latent B04a/B05a/C06 test-lag ripples were also
  repaired (see deviations D5).
- **Deviations.** (D1) migration is `014_SalesInsurers`, not the phase text's `013` (013 is C06's). (D2)
  reversals/refunds/voids use the domain's `InvoiceAdjustment` / `Invoice.Void` — the original invoice stays
  byte-identical; no negative-line "CreditNote" invoice is minted. (D3) the phase's new codes
  `PAYMENT_EXCEEDS_BALANCE` / `REFUND_EXCEEDS_PAID` / `INVOICE_ALREADY_POSTED` are **reused** as the existing
  `PaymentExceedsDue` / `AdjustmentExceedsPaid` / `InvoiceImmutable`; a sale is `MovementKind.Sale` and a return
  is `MovementKind.ReturnIn` (there is no `MovementKind.Return`). (D4) `LocalStoreOptions.AllowNegativeStockOnSale`
  (default `true`) gates the negative-stock refusal (`STOCK_WOULD_GO_NEGATIVE`); when true, insufficient stock
  never blocks posting. (D5) the test-lag repair — rolling the stale `Persistence.Tests` reference counts to the
  C06/B05a reality (units 8, categories 12, seed markers 18, protected types 9, migrations 14, schema `014`) and
  giving the fixture a `NoOpUserNotificationPort`; in `Application.Tests`, supplying the B04a/B04b security seams
  in `TestHost.Build`, appending `"Audit"` to the two chain-order asserts, aligning the retry-budget assert to
  `RetryPolicy.MaxAttempts`, giving the identity host a clean store + dev key fallback, and wiring the security
  seams + a session for the defaults-only `PingQuery` test. (D6) `CommandTypes` void/refund/register/adjustment
  already existed, so only the still-missing strings were added.
- **Verified — all three gates ran:** `build.sh full` = **0 warnings / 0 errors** (`BUILD OK (Debug)`);
  `test.sh` = **TESTS OK** (551 tests: Domain 267, Application 181, Persistence 101, Sync 1, Server 1);
  `run.sh` = **SMOKE RUN OK**.
- **Out of scope (deferred):** cash session open/close + cashier surface → C08; ledger entries from
  `InvoicePostedEvent` → C09; cheque payments → C08; server-side posting → D07; pending-command transport → E06.

### C06 — Inventory catalog, stock adjustments and purchasing (2026-09-28)
- **The core new mechanism — a maintained cache with a proof.** On-hand had no single home: the only
  authority is the append-only `StockMovements` ledger (R-B23), but the grid must not run a SUM per
  render. C06 adds `StockOnHandSnapshot` (`[LocalOnly]`, one row per `(TenantId, ProductId, VariantId)`,
  `OnHandScaled` as the exact ×1000 INTEGER that matches `FixedScaleQuantityConverter`). It is never
  written by business code: `StockSnapshotUpdater.Maintain(...)` walks `ChangeTracker.Entries<StockMovement>()`
  in state `Added` from `AnimoraLocalDbContext.PrepareSave()`, upserts the row and folds in the signed
  scaled quantity — inside the SAME transaction, for EVERY write path (C06 receipts/adjustments, and
  later C07 sales / D07 sync) with no call site to remember. A `_snapshotApplied` set (owned by the
  context, cleared after a successful save) makes a retried failed save idempotent. Because a cache can
  drift, the proof is shipped too: `ILocalStoreMaintenance.RebuildStockSnapshotsAsync` deletes and
  recomputes every row from the ledger, and the restore + `QuarantineAndRebuild` paths call it so a
  rebuilt store's cache equals its ledger. **C07/C09/D07 may rely on `StockOnHandSnapshot` being exact
  after any committed write, and on `RebuildStockSnapshotsAsync` as the repair.**
- **Schema.** Migration `013_InventorySnapshot` (numbered 013 because 012 is C05's `BoardingHandoff`),
  unique index `IX_StockOnHandSnapshots_TenantId_ProductId_VariantId`.
- **Domain/contracts.** `AdjustmentReasons` (Count, Damage, Expiry, Theft, Correction) with `IsValid`;
  8 error codes (`PRODUCT_NOT_FOUND`, `PRODUCT_HAS_STOCK`, `VARIANT_NOT_FOUND`,
  `ADJUSTMENT_REASON_INVALID`, `STOCK_WOULD_GO_NEGATIVE`, `PURCHASE_ORDER_NOT_FOUND`,
  `PURCHASE_ORDER_STATE_INVALID`, `RECEIPT_EXCEEDS_ORDER`); `AuditCategory.Inventory`; `CommandTypes` += 7
  (`inventory.product.create|update`, `inventory.variant.add`, `inventory.product.delete`,
  `purchasing.order.create`, `purchasing.receipt.record`, `purchasing.order.cancel`) — `inventory.stock.adjust`
  already existed and is reused.
- **Read projections.** `InventoryQueries` takes catalog/detail on-hand from the snapshot (never a live
  SUM) and computes `LowStockOnly` against the variant's reorder level; `PurchasingQueries.GetSnapshotAsync`
  answers the whole screen in ≤3 SELECTs (bounded PO list + supplier names + month-to-date posted-receipt
  spend). Both registered `TryAddScoped`.
- **Use cases.** 4 inventory queries + 5 commands (`CreateProduct`, `UpdateProductInfo`,
  `AddProductVariant`, `DeleteProduct`, `AdjustStock`) and 1 purchasing query + 3 commands
  (`CreatePurchaseOrder`, `ReceiveGoods`, `CancelPurchaseOrder`), each `[RequiresPermission]` +
  `[Auditable(AuditCategory.Inventory, …)]` + `IJournaledCommand`. Business rules implemented: delete is a
  soft delete refused with `PRODUCT_HAS_STOCK` while on-hand ≠ 0 or a movement landed in the last 90 days;
  `AdjustStock` validates the reason, projects the new on-hand and refuses a negative outcome with
  `STOCK_WOULD_GO_NEGATIVE`, writing ONE movement whose kind follows the delta sign
  (`AdjustmentIn`/`AdjustmentOut` — there is no single `Adjustment` kind); `ReceiveGoods` posts a receipt,
  appends one `MovementKind.Receipt` per line carrying its unit cost, applies the receipt to the order
  (→ PartiallyReceived/Received) and refuses over-receipt with `RECEIPT_EXCEEDS_ORDER` unless
  `AllowOverReceipt`.
- **Seed.** Units 7 → 8 (`TAB`) and categories 5 → 12 (`SUPP`, `GROOM`, `TOY`, `HYG`, `LAB`, `SURG`,
  `EQUIP` added to MED/VAC/FOOD/ACC/SRV); new `InventorySampleData` (marker `inventory.sample`, behind
  `SeedSampleData`, after the reference blocks because it reads the seeded categories): 45 products × 2
  variants, one opening `Receipt` per variant, 6 suppliers, 8 purchase orders spread over
  Draft/Submitted/PartiallyReceived/Received/Cancelled — the received ones also append their real ledger
  movements so order state, receipts and on-hand agree.
- **Client seam & Desktop.** `InventoryQueryService` + `PurchasingQueryService` (+ `InventoryMapping` and
  a shared `PersianFormat` for Persian digits / ٬ grouping / Jalali dates) replace the two mocks; both DI
  lines swapped and `.AddInventoryUseCases().AddPurchasingUseCases()` chained into
  `AddAnimoraApplication`; `InventoryPageViewModel`/`PurchasingPageViewModel` gained the
  `IStoreReadinessGate` line; `MockInventoryQueryService.cs` and `MockPurchasingQueryService.cs` deleted.
- **Deviations.** (D1) migration is `013_InventorySnapshot`, not the phase text's `012`. (D2) the phase's
  `MovementKind.Adjustment` maps to `AdjustmentIn`/`AdjustmentOut` chosen by the delta's sign. (D3)
  `StockAdjustRequest` carries an absolute target while `AdjustStockCommand` takes a signed delta, so the
  service computes `delta = target − currentOnHand` and records the neutral `Correction` reason (Contracts
  has no reason field). (D4) `AuditCategory` had no `Inventory` member — it was added (additive) as the
  phase's attribute requires. (D5) existing permission codes are reused (`inventory.read|adjust|receive`,
  `purchasing.read|manage`) rather than inventing new ones; no new permission was added, so
  `DefaultRolePermissions` needed no change. Also: the phase's `inventory.product.edit` / `purchasing.order.manage`
  names do not exist in the repo. (D6) `PurchaseOrderStatus` mapping for the read-model `State` int:
  Received→Delivered, Closed→Finalized, Draft/Submitted/PartiallyReceived→InProgress, Cancelled→omitted
  from the table (no Contracts equivalent).
- **Outstanding.** T12 — the three test classes (`InventoryUseCaseTests`, `StockSnapshotIntegrityTests`,
  `PurchasingQueryTests`) were not written in this session; the plan file is retained for that.


### C05 — Boarding stays, accrual and checkout handoff (2026-09-28)
- **Domain & database:** pure static `StayChargeCalculator.Accrued(stay, services, asOf, tz)` — the single
  daily-accrual algorithm, derived and **never stored** (movement-based, like inventory): `PerDay` services
  bill `UnitPrice × NightsBetween` (local-day count in the clinic time zone, clamped `Max(1, …)`, same-day
  → 1 night), `PerStay`/`PerUse` and catalog/product items bill `UnitPrice × Quantity`. `NightsBetween` is
  inlined in the calculator (Domain never depends on Application). `BoardingStay` gained
  `long? FrozenTotalMinor` + `FreezeTotalForCheckout(Money)` (audit snapshot; the calculator reproduces the
  same value at `CheckOutUtc`). New `[LocalOnly] BoardingBillingHandoff` (same column set as C03's
  `VisitBillingHandoff`, `StayId` unique, reusing `HandoffStatus`) so **C07 can invoice a stay exactly like a
  visit**, plus additive migration **`012_BoardingHandoff`** (`FrozenTotalMinor` on the stay + handoff table;
  001–011 unchanged). `ErrorCodes` += 6 (`STAY_NOT_FOUND`, `STAY_NOT_EDITABLE`, `STAY_ITEM_INDEX_STALE`,
  `ROOM_AT_CAPACITY`, `ROOM_NONE_AVAILABLE`, `STAY_ALREADY_CHECKED_OUT`).
- **Wire:** `CommandTypes` += 4 (`boarding.stay.create`, `boarding.item.add`, `boarding.item.remove`,
  `boarding.stay.transfer`); `boarding.stay.checkout` already existed.
- **Application:** `Application/Ports/IBoardingQueries` + `IBoardingBillingHandoffWriter` (the
  `IVisitBillingHandoffWriter` pattern), 5 read models, 7 queries (stays list, detail, rooms occupancy,
  services, selectable items, known pets, paged history ≤200) and 5 journaled commands with permission checks
  (`BoardingManage` for create/item/transfer, `BoardingCheckout` for checkout) and transaction-scoped room
  capacity re-check. Stale-index remove: the service resolves the ItemId from the last-read detail and passes
  both `Index` and `ExpectedItemId`; a mismatch inside the transaction → `STAY_ITEM_INDEX_STALE` and nothing
  is removed. Transfer with a null target picks the next free room of the same type. **Checkout deviation:**
  because `CompleteCheckout` needs an InvoiceId (a C07 concern), C05 stops at `RequestCheckout` →
  `ReadyForInvoice`, freezes the accrued total and writes a `Ready` `BoardingBillingHandoff`; occupancy counts
  only `State==Active`, so a ready-for-invoice stay already frees its room; a second checkout on a non-active
  stay → `STAY_ALREADY_CHECKED_OUT`.
- **Persistence:** `BoardingQueries` — occupancy counted in SQL (correlated `COUNT`, only `State==Active`
  occupied, never per-room), single-statement stay/detail projections, paged history honoring
  `BoardingHistorySort` and the 200 cap; `BoardingBillingHandoffWriter` shares the command's scoped
  `DbContext`. `LocalSeeder` gained a marker-guarded `boarding.services` block (6 `BoardingServiceDef`,
  rooms untouched); `BoardingSampleData` (5 active + 9 completed stays) sits behind `SeedSampleData`.
  **Post-merge translation fix:** the list/detail/history read model embeds the whole `BoardingStay`, so
  filtering/sorting on `r.Stay.State` / `r.Stay.CheckInUtc` after the projection could not be translated by
  EF; all filtering, ordering and paging now run on the `BoardingStay` source query before the join/project
  (name search still runs on the projection's own joined labels).
- **Client/Desktop:** scope-per-call `BoardingQueryService` implementing all `IBoardingQueryService` members
  + `BoardingMapping` (enum mapping by name, stable item order by `ConsumedUtc` then `ItemId`); DI swapped to
  the real service, `BoardingPageViewModel` gained an `IStoreReadinessGate` gate, and `IBoardingQueries` /
  `IBoardingBillingHandoffWriter` registered. **Mock deleted: `MockBoardingQueryService.cs`.**
- **Coverage:** `StayChargeCalculatorTests` (same-day→1 night, per-day×3, one-time×1, frozen = recompute at
  `CheckOutUtc`), `BoardingUseCaseTests` (`ROOM_AT_CAPACITY`, `STAY_ITEM_INDEX_STALE` without removal, checkout
  writes one handoff + frees the room, double checkout → `STAY_ALREADY_CHECKED_OUT`), `BoardingQueryTests`
  (rooms single `SELECT` via `ToQueryString()`, history sort + 200 cap). No .NET SDK was available in the
  closeout session, so `build.sh`/`test.sh`/`run.sh` were **not run**; compile/runtime verification remains
  outstanding (the translation fix above was made by inspection).
- **Deviations:** D1 migration numbered 012 (011 was C04). D2 billing enum is `PerStay/PerDay/PerUse`
  (mapped to one-time/per-night/per-unit). D3 stay dates are `DateTimeOffset`, not `long`. D4 `StayItem`
  uses `ConsumedUtc`/`ItemId` for stable order (no separate `AddedUtc`). D5 items & room history are JSON
  columns (no new relational table); transfer uses `BoardingStay.TransferRoom`. D6 `FrozenTotalMinor` added
  to the domain. D7 existing boarding permissions reused (no new permission). D8 `boarding.stay.checkout`
  already existed (+4 new command types only). D9 new `IBoardingQueries` + handoff writer ports. D10 6 service
  defs seeded, rooms untouched. D11 domain enums mapped to Contracts enums by name (`Isolation/Cage/Suite`
  → nearest `Standard`/`Vip`; status `Active→Active`, else `Done`). Plus the checkout→`ReadyForInvoice`
  deviation (real invoice deferred to C07).

### C04 — Appointments calendar and booking (2026-09-28)
- **Domain & database:** three `StaffMembership` work-window fields with validated mutator; indexed
  `(TenantId,DoctorUserId,StartUtc)` appointment reads; additive `011_StaffWorkingHours` migration
  (010 belonged to C03; migrations 001–010 unchanged). Four new scheduling error codes; existing
  slot-conflict code and book/move command types retained.
- **Application:** `ClinicCalendar` + `IClinicTimeZoneProvider` for UTC/local-day conversion,
  `SlotGrid`, seven calendar/slot queries plus doctor-roster and patient-picker queries; five journaled
  commands (book/move/cancel/check-in/create booking patient) with permission checks, transaction-scoped
  conflict re-check, working-hour validation and single-row check-in. Shared `QueueEntryResolver` with C02.
- **Persistence:** `SchedulingProjections` shared with the reception queue; indexed SQLite appointment
  reads, clinic-day bucketing outside SQL, bounded terminal history; marker-guarded 40-row
  `SchedulingSampleData` after reception/clinical seeding (only when sample seeding is enabled).
- **Client/Desktop:** two scope-per-call appointment services + DTO mapping, DI replacement, readiness
  gates for calendar and booking, and deletion of `MockAppointmentsQueryService.cs`,
  `MockAppointmentBookingService.cs`, `AppointmentStore.cs`. `CommandTypes` gained three names (cancel,
  check-in, create booking patient); four genuinely new scheduling `ErrorCodes` (slot conflict existed).
- **Coverage:** three new test classes for booking state/conflicts, grid slots and SQLite date queries.
  Per explicit user instruction, **no tests or builds were run**; runtime/compile verification remains
  outstanding. Deviation: migration 011 not 010, AuditCategory.Clinical (Scheduling absent), existing
  permissions reused, Tenant.Timezone instead of TimeZoneId. The desktop's 81-day calendar prefetch was
  split into two <=62-day queries to honor the bounded range invariant.

### C03 — Visit encounter & vaccination on the real local DB (2026-09-28)
- **Domain:** `Clinical/VisitNoteField` (`History, Exam, Diagnosis, Plan`); `Clinical/VaccinationThresholds`
  (const bucket bounds `OverdueMaxDays=-1`, `DueSoonMaxDays=14`, `UpcomingMaxDays=60` + `BucketFor(days,
  administered)` → 5-bucket read-model enum `VaccinationDueBucket { Overdue, DueSoon, Upcoming, Scheduled,
  Done }`). Existing `VaccinationRecord.StateAt` (30-day window, A04) left untouched — C03's bucket is a
  **separate read projection** computed only from `DaysUntilDue` (D7). `ErrorCodes` += 7 (`VISIT_NOT_FOUND`,
  `VISIT_NOT_EDITABLE`, `VISIT_INCOMPLETE`, `RX_LINE_NOT_FOUND`, `VACCINATION_NOT_FOUND`,
  `VACCINATION_ALREADY_DONE`, `SMS_TEMPLATE_TOKEN_UNKNOWN`).
- **Wire:** `CommandTypes` += 7 (`visits.visit.vitals`, `visits.visit.note`, `visits.rx.add`,
  `visits.rx.remove`, `visits.visit.complete`, `vaccination.record.administer`,
  `vaccination.record.reminder`).
- **Application:** `UseCases/Visits/` — per-field encounter commands (`SaveVisitVitalsCommand`,
  `SaveVisitNoteCommand`, `AddPrescriptionLineCommand`, `RemovePrescriptionLineCommand`) + `EncounterDraftQuery`
  / `RxCatalogQuery`; `CompleteVisitCommand` is the **C07 handoff seam** (one transaction: guard →
  `Visit.Complete` → one `MedicalHistoryEntry(kind=Visit)` → one `VisitBillingHandoff.Create(Status=Ready)`
  → best-effort reception ticket → `Done` → `Visit.MarkReadyForInvoice`; returns an `EncounterSummary`
  read model, tax 9%). Commands 1–4 `[RequiresPermission(VisitsWrite)]`, complete `[RequiresPermission(
  VisitsComplete)]` (D2 — reused existing permission codes). `UseCases/Vaccination/` — `VaccinationDueQuery`,
  `VaccinationCatalogQuery`, `VaccinationHistoryQuery`, SMS-template query; journaled
  `MarkVaccinationDoneCommand` (+ one next `VaccinationRecord` when the protocol has a follow-up dose, D9;
  second call → `VACCINATION_ALREADY_DONE`) and `RecordVaccinationReminderCommand` (history entry only,
  **no network**). New port `IMedicalHistoryRepository` (`Add`). Registered by `AddVisitsUseCases()` (extended)
  + new `AddVaccinationUseCases()`.
- **Persistence:** `[LocalOnly]` `Entities/VisitBillingHandoff` (+ `HandoffStatus { Ready, Invoiced,
  Cancelled }`, unique on `VisitId`, `MarkInvoiced`/`Cancel` for C07) + `Entities/SmsTemplateRow`; configs in
  `Configurations/Clinical/`. Migration **`010_C03Schema`** — both tables + indexes
  (`IX_VisitBillingHandoffs_TenantId_Status_CompletedUtc`, unique `IX_VisitBillingHandoffs_VisitId`); 001–009
  byte-identical (D1: numbered 010 not the phase's 009 — 009 taken by C01). `Queries/RxCatalogQueries`
  (Medicine/Consumable products UNION service defs → `RxCatalogItemReadModel`), `Queries/VaccinationQueries`
  (due projection joins Pet/Owner, buckets via `VaccinationThresholds`; history over `MedicalHistoryEntry`
  where `Kind==Vaccination`, paged ≤200). `Repositories/MedicalHistoryRepository`. `Seed/ClinicalSampleData`
  (`clinical.sample`, behind `SeedSampleData`, guarded on pets existing: 10 completed visits + 14 vaccination
  records across all 5 buckets, `FixedClock`) + reference SMS-template block (`vaccination.sms.templates`,
  **always seeded**, 4 rows, tokens `{pet}{owner}{vaccine}{due}`). Ports/queries registered in
  `LocalStoreServiceCollectionExtensions`.
- **Contracts:** additive `IVisitEncounterService.FlushAsync(CancellationToken=default)` — flushes pending
  debounced note edits before navigating away. Nothing else reshaped (D3 — the 12-member seam is frozen; the
  per-field commands are an Application/Client.Services concern).
- **Client services:** `Caching/TenantScopedCache<TKey,TValue>` (bounded 16, LRU, key = tenant+user+resource
  +version, A-RULE 033); `Visits/DebounceBuffer` (trailing 600 ms per `(encounterId, VisitNoteField)`, bounded
  32, non-blocking, flush on dispose); `Visits/VisitEncounterService` + `Visits/VisitEncounterMapping`
  (singleton, scope-per-call, maps the 12 existing members onto the T4 commands, `GetCatalogAsync` cached);
  `Vaccination/VaccinationService` + `Vaccination/VaccinationMapping` (5→4 status collapse, D6:
  `DueSoon→Due`, `Upcoming→Scheduled`; `SendSmsAsync` = dispatch reminder + `SmsState.Sent`, no network) +
  `Vaccination/SmsTemplateRenderer` (`{pet}{owner}{vaccine}{due}`, unknown token →
  `SMS_TEMPLATE_TOKEN_UNKNOWN`, 70-char segments).
- **Desktop:** `App.axaml.cs` — `.AddVaccinationUseCases()` added; 2 DI swaps
  (`MockVaccinationService → VaccinationService`, `MockVisitEncounterService → VisitEncounterService`) and
  removed `VisitEncounterStore` registration; both new services `Singleton`. `VisitEncounterViewModel`:
  `IStoreReadinessGate.WaitAsync()` first line of `LoadAsync`, `FlushAsync()` in `BackToGate()`/`NextVisit()`;
  `VaccinationPageViewModel`: readiness gate on initial load. **Mocks deleted:
  `MockVisitEncounterService.cs`, `VisitEncounterStore.cs`, `MockVaccinationService.cs`.**
- **Tests:** `Application.Tests/VisitEncounterUseCaseTests` (note-on-completed → `VISIT_NOT_EDITABLE`,
  empty complete → `VISIT_INCOMPLETE`, success writes exactly one history + one handoff, double-complete
  rejected — 3), `Application.Tests/VaccinationUseCaseTests` (bucket boundaries −1/0/14/15/60/61, one next
  record on follow-up, double administer → `VACCINATION_ALREADY_DONE` — 7),
  `Persistence.Tests/VisitBillingHandoffTests` (010 applies on a 009 DB, unique-index rejects 2nd handoff for
  same visit, append-only `MedicalHistoryEntry` update throws — 2). Verified: `build.sh full` **0/0**, the 3
  classes green (12 total), `run.sh` **SMOKE RUN OK**.

### C02 — Reception queue & visits triage on the real local DB (2026-09-27)
- **Derived-queue rule:** a `ReceptionQueueEntry` is persisted only for walk-ins and for state an `Appointment`
  cannot carry (called/in-visit/left/urgent). An appointment-sourced ticket gets its row **lazily on the first
  state change** (`Source=Appointment`, `AppointmentId` set); until then it is projected from the appointment as
  `Waiting`. Ticket id = `TicketRef`: `"apt:" + AppointmentId` / `"wlk:" + QueueEntryId`; unparsable/unknown →
  `TICKET_NOT_FOUND`.
- **Application:** `UseCases/Reception/` — `ReceptionQueueQuery`, `ReceptionDoctorsQuery`; journaled
  `AdmitTicketCommand`, `EnterVisitCommand`, `LeaveQueueCommand`, `MarkNoShowCommand`, `ToggleTicketUrgentCommand`,
  `CreateWalkInCommand` (owner de-dup by normalized phone, pet reuse by owner+name). `UseCases/Visits/` —
  `TriageDoctorRailQuery`, `BookedForDoctorQuery`, `TriagePatientsQuery`; journaled `StartVisitFromTicketCommand`
  (idempotent: existing in-progress visit returned, no second `Visit`) + `StartNewVisitCommand`. Read models in
  `ReadModels/`; ports `IReceptionQueueQueries`, `ITriageQueries`; `IReceptionQueueRepository` +=
  `FindByAppointmentAsync`/`CountForDayAsync`. `Correlation/OperationIdFactory.Deterministic(seed)` (SHA-256 →
  26-char Crockford id; client uses `"visit-start:" + ticketId`). Registered by `AddReceptionUseCases()` /
  `AddVisitsUseCases()`.
- **Persistence:** `Queries/ReceptionQueueQueries` — ONE statement (today's appointments LEFT JOIN queue rows
  UNION ALL walk-ins), half-open UTC range `[start,end)`, no SQL date function. `Queries/TriageQueries` — doctor
  rail counts in one projection, booked-for-doctor, top-50 recent patients. `Seed/ReceptionSampleData`
  (`reception.sample`) behind `SeedSampleData`.
- **Client services:** `Reception/ReceptionQueueService` + `ReceptionMapping`, `Visits/VisitsTriageService` +
  `VisitsMapping` + `VisitTypeCatalog` (static, from `AppointmentTypeCatalog`; `GetVisitTypes()` stays synchronous).
  Enum mapping by name, never numeric cast.
- **Wire/Domain:** `CommandTypes` += 7 (`reception.ticket.admit/enter/leave/noshow/urgent`,
  `reception.walkin.create`, `visits.visit.start`). `ErrorCodes` += 4 (`TICKET_NOT_FOUND`, `TICKET_STATE_INVALID`,
  `WALKIN_PHONE_REQUIRED`, `DOCTOR_NOT_AVAILABLE`).
- **Desktop:** 2 DI swaps + 2 registration calls in `App.axaml.cs`; `IStoreReadinessGate.WaitAsync()` as the first
  load step of `ReceptionPageViewModel` and `VisitsTriagePageViewModel`. **Mocks deleted:
  `MockReceptionQueueService.cs`, `MockVisitsTriageService.cs`.** No `.axaml` change.
- **Tests:** `Application.Tests/ReceptionQueueUseCaseTests` (lazy single row, repeated admit → state invalid,
  walk-in owner reuse, no-show on appointment, permissions), `Application.Tests/VisitsTriageUseCaseTests`
  (start-from-ticket twice → one visit, deterministic OperationId, doctor-rail booked counts over a real store),
  `Persistence.Tests/ReceptionQueueQueryTests` (no duplicate ticket, one UNION ALL SELECT, no date function);
  shared doubles in `Application.Tests/ReceptionTestDoubles.cs`.

### C01 — Patients & Owners on the real local DB; the Track C pattern (2026-09-27)
- **THE C01 PATTERN (cited by C02–C10 as "the C01 pattern"):** (1) use cases in
  `Application/UseCases/<Context>/`, one file per command/query + handler + validator; business-fact commands are
  `IJournaledCommand` with `[RequiresPermission]` + `[Auditable]`, pure reads are queries; (2) the Contracts
  interface is implemented by a `sealed` `Client.Services/<Context>/<Context>Service` taking `IDispatcher` +
  `ICurrentUserProvider` + `IOperationIdFactory`, with **no EF / SQL / business rule** — build context, dispatch,
  map; (3) DTO ↔ read-model mapping in `Client.Services/<Context>/<Context>Mapping.cs`, hand-written static
  extensions (no AutoMapper); (4) a list read is a **projection query handler** returning a purpose-built read
  model (never a loaded aggregate); paging via `QueryPaging.ToPagedAsync`; (5) no Persian text in
  Application/Client.Services — a failure is an `ErrorCode`, the VM/`NotificationText` maps it; (6) reference seed
  in `LocalSeeder`, realistic sample rows in `Seed/<Context>SampleData.cs` behind `LocalStoreOptions.SeedSampleData`;
  (7) DI swap is one line per interface + the mock deleted in the same phase.
- **Application (`UseCases/Patients/`):** `Text/PersianText.cs` (`NormalizeDigits`/`NormalizeArabicLetters`/
  `Normalize` — digits/letters only, no localization). **4 queries** (`PetDirectoryQuery`, `OwnerDirectoryQuery`,
  `OwnerDetailQuery`, `PetDetailQuery`) + **5 journaled commands** (`CreateOwnerCommand`, `UpdateOwnerCommand`,
  `CreatePetCommand`, `UpdatePetInfoCommand`, `AddWeightEntryCommand`) each with handler + validator,
  `[RequiresPermission("patients.owner.edit" | "patients.pet.edit")]` + `[Auditable(AuditCategory.Clinical, …)]`.
  **4 read models** in `ReadModels/PatientReadModels.cs`. Registered by `PatientsRegistration`.
- **Persistence:** `Queries/PatientDirectoryQueries` (port `IPatientDirectoryQueries`) — single-statement
  projections; owner name / last weight / pet count folded in as correlated scalar sub-SELECTs (no N+1), search via
  `EF.Functions.Like(SearchKey, "%term%")` over the indexed column. `Search/ISearchKeyProvider` + Owner/Pet
  providers; `SearchKey` written in `SaveChangesAsync`; indexes `IX_Owners_TenantId_SearchKey`,
  `IX_Pets_TenantId_SearchKey`. Migration **`009_PatientsSearchKeys`** (+ backfill; named 009 — deviation below).
  `Seed/PatientsSampleData` — 12 owners, 18 pets (varied species/status), 2 weight entries each (**36**) and
  **2 `DocumentReference` metadata rows each (36)**, deterministic ULIDs/hashes; runs behind
  `LocalStoreOptions.SeedSampleData` (default true).
- **Contracts addition:** `Diagnostics/IStoreReadinessGate` (the standard startup-gate remedy later C phases reuse) +
  `Client.Services/Diagnostics/StoreReadinessGate` over `LocalStoreStartup`.
- **Client services:** `Patients/PatientsService` implements all 9 `IPatientsQueryService` members (unpaged
  `GetPetsAsync`/`GetOwnersAsync` request page 1 at the max page size, documented; no Contracts member reshaped) +
  `Patients/PatientsMapping`.
- **Wire/Domain:** `CommandTypes` += 5 (`patients.owner.create/update`, `patients.pet.create/update`,
  `patients.pet.weight.add`). `ErrorCodes` += 4 (`OWNER_NOT_FOUND`, `PET_NOT_FOUND`, `OWNER_HAS_ACTIVE_PETS`,
  `WEIGHT_ENTRY_INVALID`).
- **DI (`App.axaml.cs`):** `IPatientsQueryService → PatientsService`; `IStoreReadinessGate` registered.
  **Mock deleted: `src/Animora.Desktop/Mocks/MockPatientsQueryService.cs`** (confirmed absent). No `.axaml`
  change; no ViewModel change required.
- **Deferred (out of scope, documented):** `OwnerSummary.TotalSpend` is supplied as `0` until C09's
  `OwnerFinancialSnapshot`; document payload storage is D12 (C01 seeds only the metadata row); visit collections
  in the pet file arrive with C03.
- **Tests (3 classes):** Application `PatientsUseCaseTests` (create/update/weight rules — unknown owner →
  `OWNER_NOT_FOUND`, unknown pet → `PET_NOT_FOUND`, non-positive weight → `WEIGHT_ENTRY_INVALID`, required-field
  validators; **pet mutations carry `[RequiresPermission("patients.pet.edit")]`** and owner mutations
  `patients.owner.edit` — the end-to-end deny path is already proven in `AuthorizationGateTests`) and
  `PatientsMappingTests` (every Contracts DTO field populated from the read model). Persistence
  `PatientDirectoryQueryTests` (SearchKey stamped + Persian-normalized, `Like` matching, stable paging,
  **page size clamped to 200**, **directory projection is a single non-batched SELECT statement** via
  `ToQueryString()`, no N+1). `SeedingTests` gains a C01 sample-data assertion (12/18/36/36 counts, documents point
  at a pet+owner with a 64-char lowercase hash).
- **Verification (closeout session, SDK 10.0.401 installed on disk):** `scripts/sandbox/build.sh full` =
  **0 warnings / 0 errors** (14 projects); `scripts/sandbox/run.sh` = **SMOKE RUN OK** (app boots under Xvfb with the
  real `PatientsService` + `IStoreReadinessGate`, mock deleted, no startup crash). All **C01 tests pass**:
  `PatientsUseCaseTests`/`PatientsMappingTests` 20/20, `PatientDirectoryQueryTests` 7/7, `SeedingTests` 4/4.
  Building for the first time surfaced **4 latent C01 defects** the earlier (SDK-less) sessions could not catch, all
  fixed here: (a) `PatientsUseCaseTests` used `CommandContext` without `using Animora.Application.Abstractions;`
  (CS0246); (b) `PatientDirectoryQueryTests` referenced a non-existent nested `LocalStoreFixture.FakeClock` — the
  clock is a top-level class (CS0426); (c) `PatientsSampleData` carried an unused `IClock clock` primary-ctor param
  (CS9113, and `build.sh` treats warnings as errors) — dropped it (it uses its own `FixedClock`); (d) `PatientsMapping`
  split clinical notes only on `,`/`\n`, so a Persian-comma-separated note wasn't split — now also splits on `،`
  (U+060C), matching the mapping test. `test.sh` (whole suite) still reports **34 failures in B04-Security
  pipeline/DI tests** (`BehaviorPipelineTests`, `DispatcherTests`, `NoReflectionTests`, `SessionLifecycleTests`,
  `CommandJournalTests`, `LocalMigrationTests`, `AppendOnlyGuardTests`, `StoreStartupTests`) — **all pre-existing and
  unrelated to C01**: reproduced identically at the pre-C01 commit `1d1e940` (B05c) via a throwaway worktree; root
  cause is those tests' service provider not registering `IStaffMembershipRepository`/`IRoleDefinitionRepository`/
  `ICapabilitySnapshotSigner`. No C01 file touches those types; fixing them belongs to a B04-closeout, not C01.
- **Deviations:** (1) **Migration named `009_PatientsSearchKeys`, not `008`** as the phase text says — `008` was
  already consumed by B05c (`UiDraftState`), so the next free number is 009 (see Decision log "C01"). (2) The
  single-statement acceptance ("`ToQueryString()` contains exactly one `SELECT`") is asserted as **one non-batched
  SQL statement whose top level is a SELECT** — the last-weight / pet-count folds render as correlated scalar
  sub-SELECTs inside that one statement (which is exactly what avoids N+1); a literal single-`SELECT`-token count
  would wrongly flag a correct no-N+1 projection. (3) The permission-denied acceptance is covered by asserting the
  attribute each mutation carries plus the existing generic gate test, rather than re-instantiating the whole
  pipeline in the patients test.

### B04c — Capability gate, synchronous shell consumption, nav filtering & status (2026-09-26)
- **Contracts (pure, no `Animora.*` ref):** `Identity/ICapabilityGate.cs` — `enum CapabilityState { Allowed, Denied,
  ExpiredOffline, RequiresOnline }` + `ICapabilityGate { bool Can(code); bool CanAny(params string[]); CapabilityState
  GetState(code); event EventHandler? CapabilitiesChanged; }`. `CanAny` false for null/empty; unknown code always denied.
- **Gate service:** `Client.Services/Identity/CapabilityGateService.cs` (singleton, `ICapabilityGate`): internal
  `RefreshAsync(ct)` + `Clear()`; public API synchronous O(1). Warm-up opens ONE short async scope, resolves scoped
  `ICapabilityStore`/`CapabilityEvaluator`/`IPermissionEvaluator`/`IIdentityRepository` + singleton install identity, loads
  the session snapshot for tenant/user/device, verifies via B04b `CapabilityEvaluator` (signature, stamp, expiry,
  high-water) and precomputes a result for all 42 `PermissionCodes.All`, applying the B04a RBAC decision. Publishes an
  immutable per-snapshot view atomically + `CapabilitiesChanged`. `GetState` does bounded lookup then time-sensitive
  re-validation (`max(now, highWater)`, session expiry, absolute + per-code windows) so a window cannot stay cached
  Allowed. No `.Result`/`.Wait()`/`GetAwaiter().GetResult()`. Fail closed before warm-up, on sign-out, locked/expired/absent
  session, missing/mismatched/tampered snapshot; a tampered snapshot is dropped whole (`ForTampered` view, read-only) and
  never re-signed. Bounded cache 256 entries, key `(tenant,user,device,code,snapshotId)`, FIFO eviction; new
  snapshot/sign-in/sign-out/revocation clears it. Map: Critical→`RequiresOnline`, expired→`ExpiredOffline`, else `Denied`.
- **Auth seam:** `AuthenticationService` now takes the concrete `CapabilityGateService`; awaits `RefreshAsync` BEFORE
  returning success from `SignInAsync` and `CompleteFirstRunAsync` (shell built immediately after), `Clear()` on any
  failure and on `SignOutAsync` (in `finally`). `IAuthenticationService` unchanged.
- **Nav mapping (Desktop `Shell/NavPermissions.cs`):** ordinal map for the **13 real** keys — dashboard→`patients.read`,
  appointments→`appointments.read`, patients→`patients.read`, visits→`visits.read`, vaccine→`vaccination.read`,
  boarding→`boarding.read`, sale→`sales.read`, inventory→`inventory.read`, expense→`finance.read`,
  reports→`reports.read`, cheques→`cheques.read`, history→`audit.read`, profile→`settings.read`. Catalog has no `*.view`
  permission, so dashboard/history/profile map to real read constants. Substitutions vs the phase text: dashboard→
  `patients.read` (receptionist landing surface), history→`audit.read`, profile→`settings.read`; the invented
  reception/purchasing/cashier entries do not exist and were not added. Receptionist (no `reports.read`/`finance.read`/
  `cheques.read`) never sees `reports`/`expense`/`cheques`; Manager (full catalog) sees all 13.
- **Offline windows (authoritative, unchanged):** Low 30 d, Normal 14 d, **High 72 h**, Critical online-only. Tests assert
  `OfflineCapabilityPolicy.HighWindow`; the phase text's "High = 24 h" is stale and not applied.
- **Shell:** `NavItem` gained an optional, LAST `string? RequiredPermission = null` (no existing construction changed;
  `NavigationMenu.cs` untouched). `SidebarViewModel` now takes `ICapabilityGate`, filters `NavigationMenu.Items` by
  `NavPermissions`+`Can`, drops empty groups, exposes a notifying `Groups` rebuilt on `CapabilitiesChanged` (UI-thread
  marshal), keeps the active selection only while still permitted and refuses navigation to a revoked entry; FinancePage
  `InitialTab/InitialRange` preserved. `TopbarViewModel` takes the gate, adds notifying `IsReadOnlyMode` (true only when
  the gate permits no nav capability at all), and reuses the EXISTING status box for a read-only explanation via
  `SyncStatusText`/`SyncToolTip` while true, restoring normal sync presentation on recovery. No `.axaml` edited; exactly
  the two allowed ViewModels changed.
- **DI (`App.axaml.cs`):** `AddSingleton<CapabilityGateService>()` + `AddSingleton<ICapabilityGate>(sp =>
  sp.GetRequiredService<CapabilityGateService>())` — the same singleton the `AuthenticationService` warms.
- **Tests (4 new classes, 31 facts):** Application — `PermissionEvaluatorTests` (stored RBAC allow/deny, deny-wins
  override, extra grant, unknown code ⇒ `PERMISSION_UNKNOWN`, policy before typed rule via an instrumented rule),
  `CapabilityExpiryTests` (real 30d/14d/**72h**/online-only windows, expiry at equality, clock rollback with later
  high-water does not revive, mutated JSON ⇒ `CAPABILITY_SNAPSHOT_TAMPERED` + discard, revoke denies writes/releases
  reads, missing ⇒ `CAPABILITY_SNAPSHOT_MISSING`, stamp mismatch), `AuthorizationGateTests` (denied attributed command ⇒
  `PERMISSION_DENIED` + handler not called, allow path runs handler, unannotated with no session ⇒ `SESSION_EXPIRED`, only
  the 4 real pre-auth requests pass anonymously). Persistence — `CapabilityStoreTests` over `LocalStoreTestFixture`
  migrated FILE SQLite: replace/get round-trip + signature verifies, missing ⇒ null, replace leaves one row, device/tenant
  isolation, revoke re-signs (still valid), revoke-absent OK.
- **Fast gate:** `build.sh full` 0 warnings / 0 errors. Targeted `dotnet test` (`--no-restore --no-build`): Application
  B04 24/24, Persistence `CapabilityStoreTests` 7/7. Smoke (`run.sh`) NOT run — Xvfb not present and per the user's
  no-desktop-install rule; Receptionist/Manager sidebar is covered by the mapping + gate tests, not manual UI.
- **Deviations:** (1) none in production shape beyond the locked set. (2) `.sandbox/` SDK/obj are git-ignored; the .NET
  10.0.401 SDK was installed to `$HOME/.dotnet` via `install-prereqs.sh --no-xvfb` (no desktop env). (3) The persistence
  test injects a deterministic in-memory `ISecretStore` (the OS keystore does not exist in the sandbox) so the REAL signer
  and `CapabilityStore` still run — no in-memory EF shortcut.

### B04b — Signed, device-bound offline capability snapshots & issuance (2026-09-26)
- **Ports/DTO (Application, `Security/ICapabilityStore.cs`):** immutable `StoredCapabilitySnapshot(Id, TenantId, UserId,
  DeviceId, PermissionsJson, IssuedAtUtc, PerCapabilityExpiryJson, AbsoluteExpiryUtc, SnapshotSignature, SecurityStamp,
  RevokedAtUtc)`; `ICapabilityStore.GetAsync → Result<StoredCapabilitySnapshot?>` (missing = null value),
  `ReplaceAsync → Result`, `RevokeAsync(tenant,user,device,revokedAtUtc) → Result`; `ICapabilitySnapshotSigner.Sign →
  Result<string>`, `Verify → Result`. Same file: `CapabilitySnapshotCodec` (single JSON codec + canonical form) and
  `CapabilityPayload`. B04c must consume exactly these signatures.
- **Table:** `DeviceCapabilitySnapshots` (`Entities/DeviceCapabilitySnapshot`, `[LocalOnly]`, not tenant-filtered; every
  query binds tenant+user+device). Unique `IX_DeviceCapabilitySnapshots_Tenant_User_Device`; JSON bounds 8192/16384,
  signature 64 (explicit in `OnModelCreating`, overriding the short-text convention — one-line model addition in
  `AnimoraLocalDbContext.cs`, indispensable because conventions run after entity configurations); timestamps UTC ticks.
  Migration `20260926055039_006_CapabilitySnapshot` (EF-generated): CreateTable + CreateIndex only; 001–005 untouched;
  `migrations list` shows 001–006.
- **Store:** `Persistence.Sqlite/Security/CapabilityStore` (scoped, scoped `AnimoraLocalDbContext`): inside an active UoW
  transaction it only flushes (caller commits/rolls back); standalone it opens its own short transaction. Replace =
  delete current row(s) for the triple, flush, insert (the unique index never sees two). Revoke verifies first: a tampered
  row is deleted and `CAPABILITY_SNAPSHOT_TAMPERED` returned (never re-signed); otherwise `RevokedAtUtc` is set and the row
  re-signed atomically (earliest revocation wins). Reads never re-sign.
- **Signer:** `Infrastructure.Local/Security/CapabilitySnapshotSigner` (singleton): HMAC-SHA256 over one UTF-8 canonical
  JSON (version, Id, tenant, user, device, ordinal-sorted/distinct permissions, issued ticks, ordinal-sorted expiry pairs,
  absolute ticks, stamp, revoked ticks|null; signature excluded); malformed/duplicate JSON codes or permission≠expiry key
  set rejected; base64; `FixedTimeEquals`. Key `capability-snapshot-key` from `ISecretStore` (32 bytes): generated only on
  `NOT_FOUND`, any other read/write failure or wrong length → `STORE_KEY_UNAVAILABLE` (fail closed). Never in SQLite.
- **Issuer:** `CapabilitySnapshotIssuer` (scoped): tenant's stored role definition (missing → `CAPABILITY_NOT_GRANTED`,
  no default fallback) ∪ extras − denies, declared codes only; `CapabilitySnapshot.BuildExpiry`; absolute = max per-code
  expiry (empty set ⇒ absolute = issued ⇒ grants nothing); `IdGenerator.NewId(clock.UtcNow)`; copies `User.SecurityStamp`;
  sign → `ReplaceAsync`.
- **Evaluator:** `CapabilityEvaluator` (scoped, separate from B04a `IPermissionEvaluator`), `Evaluate(snapshot?, code,
  tenantId, userId, deviceId, currentSecurityStamp, nowUtc, highWaterUtc) → CapabilityEvaluation(State, Result,
  DiscardSnapshot)`; `CapabilityDecisionState { Allowed, Denied, ExpiredOffline, RequiresOnline }`. Order: missing →
  `CAPABILITY_SNAPSHOT_MISSING`; binding/stamp mismatch → `CAPABILITY_NOT_GRANTED`; malformed payload → deny + discard;
  signature mismatch → `CAPABILITY_SNAPSHOT_TAMPERED` + discard (keystore outage denies without discard); unknown/absent
  code → deny; revoked → deny every non-Low (read) code; Critical → `RequiresOnline`; effective = max(now, highWater)
  (high-water never lowered/advanced); issued > effective + 5 min → `ExpiredOffline` (rollback, no lifetime extension);
  per-code window above policy → deny; absolute or per-code expiry `<=` effective → `OFFLINE_CAPABILITY_EXPIRED`.
- **Offline windows (corrected):** `OfflineCapabilityPolicy` is authoritative — Low 30 d, Normal 14 d, **High 72 h**,
  Critical 0 (online-only). The phase text's "High 24 h / Medium 7 d" is wrong and was not applied.
- **Integration:** `SignInCommandHandler` and `CompleteFirstRunSetupCommandHandler` now run success mutations + audit +
  `IssueAsync` inside one `uow.ExecuteAsync` (failure propagates, no session). First-run is included because it opens the
  Manager shell without a later sign-in; without a snapshot the B04c gate would deny the owner. Lockout/failure paths
  unchanged; session start and permissions/result order unchanged; cache set only after session success.
- **Transaction boundary (unavoidable, documented):** `ISessionService.StartAsync` (`LocalSessionService`) opens its own
  DI scope and commits separately, so the session row is a later, separate transaction — NOT the handler's. If it fails
  after the snapshot committed, the handler returns the failure (no session, no cache); the orphan snapshot grants
  nothing without a session and is replaced at the next sign-in. `ISessionService` was not modified.
- **DI (real files):** signer → `AddAnimoraLocalInfrastructure` (`LocalInfrastructureServiceCollectionExtensions`, singleton,
  depends only on singleton `ISecretStore`); store → `AddAnimoraLocalStore` (`LocalStoreServiceCollectionExtensions`,
  scoped); issuer/evaluator → `AddAnimoraApplication` (scoped). No scoped service captured by a singleton.
- **ErrorCodes:** `CAPABILITY_SNAPSHOT_TAMPERED`, `CAPABILITY_SNAPSHOT_MISSING` (both Forbidden).
- **Deviations:** (1) `AnimoraLocalDbContext.cs` (outside the locked set) gained 4 lines of column bounds — indispensable
  for realistic JSON max lengths (conventions overwrite configuration-level bounds). (2) `install-prereqs.sh` ran once but
  does not install dotnet-ef (known, see B03 note); dotnet-ef 10.0.12 was installed to `.sandbox/tools` with the
  documented `dotnet tool install` command. (3) Indispensable compile fix in `tests/Animora.Application.Tests/
  IdentityTestSupport.cs` (first `build.sh full` failed CS7036: `IdentityWorld` constructs both handlers directly): added
  `SeededRoleRepository`, `InMemoryCapabilityStore`, `TestCapabilitySigner`, passed an issuer, and `CountingUnitOfWork.
  ExecuteAsync` now counts one save per successful unit (success paths moved from `SaveAsync` to `ExecuteAsync`). No test
  assertion changed. Second `build.sh full`: 0 warnings / 0 errors. (4) Tests not run (B04 tests are B04c).

### B04a — Authorization evaluator, policies, resource rules & dispatcher gate (2026-09-26)
- **Evaluator:** `Security/IPermissionEvaluator.cs` (`AuthorizationSubject`, `AuthorizationDecision`, interface) +
  `PermissionEvaluator`: unknown code → `PERMISSION_UNKNOWN` first; then RBAC from the store only
  (`FindActiveAsync` + tenant `FindByRoleAsync`; role ∪ extra − denied, ordinal; missing role definition = no grant,
  no `DefaultRolePermissions` fallback; caller `Permissions`/`Role` never trusted) → `RBAC`; then policies → policy
  name; then typed rules → rule type name.
- **Policies:** `AuthorizationPolicies` — frozen ordinal map of exactly 5 predicates, statically bound to
  `(code, concrete type)` pairs (`VisitsWrite/VisitsComplete`+`Visit`, `SalesDraft`+`Invoice`,
  `PurchasingManage`+`PurchaseOrder`, `CashierSessionClose`+`CashSession`, `FinancePost/FinancePeriodClose`+
  `AccountingPeriod`, `UsersManage`+`User`/`UserManagementOperation`, `RolesManage`+`UserManagementOperation`).
  `NotSelfDestructive` fails closed without explicit `UserManagementOperation` metadata (a plain `User` is denied).
- **Rules:** `IResourceAuthorizationRule<T>`; `InvoiceAuthorizationRule` (`SalesDraft` → Draft only; posted is
  immutable) and `CashSessionAuthorizationRule` (`CashierSessionClose` → owner only), scoped, invoked by type switch.
- **Registration binding:** `RequiresPermissionAttribute`; `ApplicationRegistrationBuilder.BuildEntry` reads it once,
  throws on a non-catalog code, stores nullable `PermissionCode` additively in `PipelineDescriptor` and `HandlerEntry`.
  No request is annotated yet (no existing request maps to a catalog code); unannotated = authenticated-only.
- **Gate:** scoped `PermissionAuthorizationGate` (registry lookup, never attributes in the hot path); allow-all deleted;
  DI registers gate, evaluator and both rules scoped. `IEntitlementGate` unchanged.
- **Deviations from phase text (real tree):** (1) `HandlerEntry`/`PipelineDescriptor` live in their own files under
  `Dispatch/`, not in `HandlerRegistry.cs`. (2) The catalog has no self-password code: `ChangePasswordCommand` is
  unannotated and relies on session + `context.UserId` self ownership. (3) A four-request pre-auth allowlist
  (`SignInCommand`, `CompleteFirstRunSetupCommand`, `UnlockSessionCommand`, `CurrentSessionQuery`), anonymous user on the
  installation tenant/device only, is required so B03 sign-in/first-run/unlock cannot deadlock. (4) Rule classes sit in
  `Security/Rules/` but use namespace `Animora.Application.Security`: a `Security.Rules` namespace shadowed
  `Validation.Rules` in `SessionCurrentUserProvider` (CS0234). (5) Compile-required: `IdempotencyTests` referenced the
  deleted `AllowAllAuthorizationGate`; it now registers the existing `RecordingAuthorizationGate` with `Result.Ok()`.
  (6) The .NET SDK was missing; `install-prereqs.sh` ran once. Tests not run (user directive).

### B03c — Identity contracts, desktop wiring, seeding & targeted tests (2026-09-26)
- **Contracts auth seam:** `Animora.Contracts/Identity/IAuthenticationService` (`SignInAsync`, `SignOutAsync`,
  `IsFirstRunAsync`, `CompleteFirstRunAsync`) + `AuthModels` (`AuthOutcome`, `FirstRunRequest`). `UserRole.Receptionist`
  appended (existing numeric values preserved). Contracts still references no `Animora.*`.
- **Client identity services:** `Client.Services/Identity/AuthenticationService` (singleton; a fresh async scope per call
  resolves the scoped `IDispatcher` + `ICurrentUserProvider`; anonymous contexts for sign-in / first-run / session query,
  active-session context for sign-out, which stays idempotent; failure is never treated as first run; no
  `ConfigureAwait(false)`). `CurrentUserContextService` (singleton) reads `SessionContextCache.Current` — no sync-over-async;
  initials = first text element; `RoleLabel` = stable key `role.*`.
- **Role mapping:** `Mapping/RoleMapping` — explicit, exhaustive `StaffRole → UserRole` (Accountant → Manager fallback,
  documented) + `ToRoleKey`.
- **Desktop DI:** csproj references `Animora.Client.Services`; `App.axaml.cs` calls `AddAnimoraLocalInfrastructure()` +
  `AddAnimoraApplication(b => b.AddIdentityUseCases())`; `ICurrentUserContext → CurrentUserContextService`,
  `IAuthenticationService → AuthenticationService`; `FirstRunViewModel` transient + `Func<FirstRunViewModel>` factory.
- **Async local-store startup gate:** `LoginWindow.Opened` (async-void boundary, all exceptions caught) → scoped
  `ILocalStoreInitializer.InitializeAsync` inside an async scope on the thread pool → `LoginViewModel.InitializeAsync(storeReady)`
  on the UI thread. First frame never blocked; sign-in disabled until ready; failure → generic error, sign-in stays disabled.
- **LoginViewModel rewrite:** demo credentials/`TryAuthenticate` removed; `IsBusy` (initially true) + `IsStoreReady`
  drive `SignInCommand` (async); `MapError` is the single code → Persian mapping (shared with first-run); password cleared
  on failure; first-run modal shown at most once and raises `LoginSucceeded` after setup.
- **FirstRunView/ViewModel:** `Pages/Login/FirstRunView.axaml` (+ trivial code-behind) and `FirstRunViewModel`
  (`CompleteSetupCommand`, `SetupSucceeded`); registered in `Shell/ViewFactory.cs`.
- **Pre-shell DialogService fallback:** with no `ModalHost` attached (before `MainWindow`), `ShowModal` opens a lightweight
  owned `Window` (`ShowDialog(owner)` or `Show()`), which `Close()` closes; host-attached behaviour unchanged.
- **Mock deletion:** `src/Animora.Desktop/Mocks/MockCurrentUserContext.cs` deleted.
- **Seeding:** `LocalSeeder` checked — it seeds no user/credential; unchanged. First-run setup is the only user-creation path.
- **Tests (Application.Tests; + refs to Infrastructure.Local & Persistence.Sqlite):** `IdentityTestSupport.cs` (mutable clock,
  counting hasher, in-memory repos, file-backed `LocalIdentityHost`), `SignInTests` (7), `FirstRunSetupTests` (2),
  `SessionLifecycleTests` (6, temp-file SQLite, restart = simulated crash), `PasswordPolicyTests` (7 incl. IdentityV2 legacy
  hash → rehash). **Domain.Tests:** `LayeringRulesTests.IdentityPackage_IsReferencedOnlyByInfrastructureLocal`
  (+ refs to Infrastructure.Local & Client.Services).
- **Commands & results:**
  `scripts/sandbox/build.sh` → BUILD OK;
  `dotnet test tests/Animora.Application.Tests/... --no-restore <MSBUILD_LOWMEM> --filter "FullyQualifiedName~SignInTests|FullyQualifiedName~FirstRunSetupTests|FullyQualifiedName~SessionLifecycleTests|FullyQualifiedName~PasswordPolicyTests" -- xUnit.MaxParallelThreads=1` → **22 passed / 0 failed**;
  `dotnet test tests/Animora.Domain.Tests/... --no-restore <MSBUILD_LOWMEM> --filter "FullyQualifiedName~LayeringRulesTests" -- xUnit.MaxParallelThreads=1` → **4 passed / 0 failed**;
  `scripts/sandbox/build.sh full` → **0 warnings / 0 errors**; `scripts/sandbox/run.sh` → **SMOKE RUN OK**.
  **The full test suite (`scripts/sandbox/test.sh`) was NOT run, per user directive; only the B03c targeted tests ran.**
  No new migration (still `001`–`005`).

### B03b — Identity use cases, provider swap & repositories (2026-09-24)
- **Use cases** (`Application/UseCases/Identity/`, one file each, handler + validator): `SignInCommand` → `SignInResult`
  (lookup by username → lockout/disabled gate *without* hash comparison → verify → failure: `RegisterFailedAttempt` +
  `LoginAttemptLog` + `CREDENTIALS_INVALID` (5th → `USER_LOCKED_OUT`) → success: rehash if needed, `RegisterSuccessfulLogin`,
  `LoginAttemptLog("OK")`, `ISessionService.StartAsync`, permissions); `SignOutCommand`, `LockSessionCommand`,
  `UnlockSessionCommand` → `SessionSnapshot`, `ChangePasswordCommand` (policy → verify current → `SetPasswordHash`),
  `CompleteFirstRunSetupCommand` → `SignInResult` (guard: any user exists → `FIRST_RUN_ALREADY_COMPLETED`; renames the
  seeded `T-LOCAL-DEFAULT` tenant, creates owner `User` + `StaffMembership(Manager)` + approved `Device`, starts session),
  `CurrentSessionQuery` → `SessionSnapshot`. Registered via `builder.AddIdentityUseCases()` (`IdentityRegistration`).
- **Support types:** `Abstractions/Unit`, `Commands/ISelfTransactedCommand` (TransactionBehavior bypass),
  `Security/SessionContextCache` (singleton last-known snapshot), `IdentityAccess.PermissionsAsync` (stored
  `RoleDefinition` or `DefaultRolePermissions`, + extras − denies).
- **Provider:** `SessionCurrentUserProvider` replaces `SingleUserProvider` (deleted). No/expired session →
  `SESSION_EXPIRED`; locked → `PERMISSION_DENIED`. `ICurrentUserProvider` gains `AnonymousUserId` +
  `BuildAnonymousCommandContext` / `BuildAnonymousQueryContext` for pre-auth identity requests.
- **Ports/repos:** `IIdentityRepository.FindByUsernameAsync` / `AnyUserAsync` / `FindTenantAsync`; new
  `IStaffMembershipRepository` (`FindAsync`, `FindActiveAsync`, `Add`), `IDeviceRepository` (`FindAsync`, `Add`),
  `IRoleDefinitionRepository` (`FindByRoleAsync`), `ILoginAttemptLog` → `LoginAttemptLogWriter`; all registered in
  `AddAnimoraLocalStore`.
- **DI:** `AddAnimoraApplication` → `SessionContextCache` (singleton) + `ICurrentUserProvider → SessionCurrentUserProvider`.
- **Tests touched (compile only):** `SingleUserProvider` fact replaced by `SessionCurrentUserProvider_RequiresSession_AndBuildsContexts`;
  `TestHost` registers a `FakeSessionService`. Full B03 test suite is B03c.

### B03a — Identity foundation: tables, hashing & session (2026-09-24)
- **Package:** `Microsoft.Extensions.Identity.Core 10.0.12` (home of `Microsoft.AspNetCore.Identity.PasswordHasher<T>`) pinned
  in `Directory.Packages.props`; referenced **only** by `Animora.Infrastructure.Local.csproj` (see deviation log).
- **Domain:** `User.MustChangePassword` + `RequirePasswordChange()`; `SetPasswordHash` clears it (and still rotates the stamp).
- **ErrorCodes** (Identity region) += 4: `CredentialsInvalid`, `SessionExpired`, `PasswordPolicyViolation`,
  `FirstRunAlreadyCompleted`. Lock/disable cases reuse `UserLockedOut` / `UserDisabled` (see deviation log).
- **Persistence:** `[LocalOnly]` `LocalSession` (Start/Touch/Lock/Unlock/End; one active row per device via unique filtered
  index `IX_LocalSessions_DeviceId_Active` `"EndedAtUtc" IS NULL`) and append-only `LoginAttemptLog`
  (`Create(username, outcome, deviceId, now)`, `SuccessOutcome = "OK"`, index `IX_LoginAttemptLogs_OccurredAtUtc`;
  added to `AppendOnlyGuard.ProtectedTypes`). Configs in `Configurations/Identity/`; column types/lengths from
  `ModelConventions` (Ids TEXT(26), datetimes INTEGER UTC ticks). Migration `005_LocalIdentity`
  (2 tables + `Users.MustChangePassword`) + snapshot.
- **Application/Security:** `IPasswordHasher` + `PasswordVerificationOutcome`, `PasswordPolicy.Validate` (≥ 8, letter + digit),
  `ISessionService` + `SessionSnapshot` (`SignedOut(isFirstRun)` helper).
- **Infrastructure.Local:** `PasswordHashingService` (Identity `PasswordHasher<T>`, IdentityV3, 210 000 iterations; corrupt hash →
  `Failed`), `LocalSessionService` (singleton, scope-per-operation, serialized by a semaphore; restore ends expired sessions
  with `SESSION_EXPIRED`, resumes idle-past-timeout sessions **locked**; touch throttled by `TouchThrottle` = 60 s; unlock
  verifies the hash, counts failures via `RegisterFailedAttempt`, ends the session if the lockout triggers),
  `Options/LocalSessionOptions` (Idle 30 min, Absolute 12 h, TouchThrottle 60 s).
- **DI** (`AddAnimoraLocalInfrastructure`): `LocalSessionOptions`, `IPasswordHasher → PasswordHashingService`,
  `ISessionService → LocalSessionService` (singletons, `TryAdd`). Nothing consumes them yet (B03b).
- **Gate:** `build.sh full` 0/0; `dotnet-ef migrations list` shows `005_LocalIdentity`.

### B01 — Application layer skeleton (2026-09-24)
- **Abstractions** (`Animora.Application.Abstractions`): `ICommand<TResult>`, `IQuery<TResult>` (markers),
  `ICommandHandler<TCommand,TResult>`, `IQueryHandler<TQuery,TResult>`
  (`Task<Result<TResult>> HandleAsync(req, ctx, ct)`), `CommandContext` / `QueryContext` (the only carriers
  of ambient identity), `IDispatcher` (`SendAsync` / `QueryAsync`), `ErrorMapping.FromException` (the single
  exception → `ErrorCode` table).
- **Dispatch** (`Animora.Application.Dispatch`): `HandlerRegistry` (readonly `FrozenDictionary<Type, HandlerEntry>`),
  `HandlerEntry` (typed `PipelineStep<TResult>` + untyped `Invoker` closure, both compiled once),
  `ApplicationRegistrationBuilder` — `AddCommand<TCommand,TResult,THandler>()`, `AddQuery<TQuery,TResult,THandler>()`,
  `AddValidator<T,TValidator>()`, `RequireCommand<,>()` / `RequireQuery<,>()`; handlers are scoped.
  `RegistryValidation.Validate` throws at startup on duplicate handler, abstract handler, required request
  without handler/wrong kind, or validator without handler. `Dispatcher` returns `HANDLER_NOT_REGISTERED` for an
  unknown type and `DISPATCH_FAILED` on kind/result mismatch. Supporting types: `IPipelineBehavior`,
  `PipelineStep<TResult>`, `PipelineDescriptor`, `RequestKind`.
- **Fixed chain** (`Dispatcher.Chain`, composed once per entry at registration, outer → inner):
  `Correlation → ExceptionMapping → Validation → Authorization → Entitlement → Transaction → Handler`.
  Validation link is omitted for entries without validators; Transaction link is omitted for queries; a command
  with no `IUnitOfWork` registered fails closed with `DISPATCH_FAILED`. Failures get a `[corr:<id>] ` Detail prefix.
- **Validation**: `IValidator<T>`, `ValidationFailure(Field, Code)` (+ `FormatDetail`/`ParseDetail`), `Rules`
  (`Required`, `MaxLength`, `Range`, `PositiveMoney`, `NonNegativeQuantity`, `UlidFormat`, `NotFutureUtc`,
  `ValidEnum<TEnum>`; return `null` on success). No FluentValidation.
- **Security planes**: `IAuthorizationGate` (default `AllowAllAuthorizationGate` → **replaced by B04**),
  `IEntitlementGate` (default `AllowAllEntitlementGate` → **replaced by E07**), `ICurrentUserProvider`
  (default `SingleUserProvider`, user `U-LOCAL-OWNER` → **replaced by B03**). All registered with `TryAdd`.
- **Correlation**: `ICorrelationContext` / `CorrelationContext` (`AsyncLocal`), `OperationIdFactory` (the only
  client-side `OperationId` source).
- **Reference pair**: `UseCases/Diagnostics/PingQuery` + `PingQueryHandler` (returns `"PONG"`), always registered.
- `AddAnimoraApplication(configure?)` in `ApplicationServiceCollectionExtensions`.
- `ErrorCodes` += `HANDLER_NOT_REGISTERED`, `DISPATCH_FAILED` (region "Application dispatch (B01)").
- Tests: `DispatcherTests`, `BehaviorPipelineTests`, `ValidationRulesTests`, `NoReflectionTests` (+ `TestDoubles`);
  `PlaceholderTests` removed.
- No desktop DI change (`App.axaml.cs` untouched — C01), no mock deleted, no migration, no Contracts change.
- Deviations: see Decision log "B01".

### B02 — Local command pipeline & transactional outbox (2026-09-24)
- **Wire contracts** (`Animora.Contracts.Wire`, no dependency outward): `Commands/CommandEnvelope`
  (28-field sealed record + `Create` factory that defaults `ContractVersion = WireContract.Version`),
  `SettlementOutcome` (`Accepted`/`Rejected`/`Conflict`/`Deferred`), `FinancialEffect`
  (`None`/`Applied`/`NotApplied`/`Unknown`), `SettlementResult`, `CommandTypes` (16 op-type consts + `All`),
  `Errors/WireFieldError`, `Errors/WireApiError`.
- **Domain**: `Primitives/SettlementState` (8 states) + `Primitives/SettlementTransitions` — the allow-table
  (`EnsureAllowed` → `SETTLEMENT_INVALID_TRANSITION`; `IsAllowed` for callers that want a boolean, 64-pair
  exhaustive test). `Reversed` is terminal; `PendingSettlement → PendingSettlement` allowed (retry).
  `Results/ErrorCodes` += `SETTLEMENT_INVALID_TRANSITION`, `COMMAND_ALREADY_SETTLED`, `COMMAND_CLAIM_LOST`,
  `OUTBOX_DEAD_LETTERED` (region "Command pipeline (B02)").
- **Application** (`Commands/`): `PendingCommandRecord` (28-field projection), `ICommandJournal` port
  (`EnqueueAsync`, `FindByOperationIdAsync`, `ApplySettlementAsync`, `ClaimBatchAsync`, `ReleaseAsync`,
  `GetUnsettledForAggregateAsync`, `GetSummaryAsync`, `GetUnsettledPageAsync`), `IJournaledCommand` marker,
  `CommandFingerprint` (SHA-256 hex over `CommandType + \u001f + canonical sorted-key payload JSON`; A-RULE 019),
  `RetryPolicy` (`MaxAttempts = 10`, `NextDelay` capped 15 min ± 20 % jitter, `IsExhausted => attempts >= MaxAttempts`),
  `ICommandTransport` + `NullCommandTransport` (returns `Deferred`), `IIntegrityAlertSink` + null default,
  `ILocalSequenceAllocator`, `LocalCommandProcessor` (`RunPassAsync`; claim → dependency check → transport →
  settle/release; `NullCommandTransport` keeps rows provisional). `Outbox/IOutbox` port.
  `Behaviors/CommandJournalBehavior` (only fires for `IJournaledCommand`; builds envelope, allocates sequence,
  enqueues) appended to the frozen chain as the innermost link before Handler → chain is now
  `Correlation → ExceptionMapping → Validation → Authorization → Entitlement → Transaction → CommandJournal → Handler`.
  Queries `UseCases/Sync/PendingCommandSummaryQuery` + `UnsettledCommandListQuery`. DI defaults registered with
  `TryAdd`.
- **Persistence.Sqlite**: entities `PendingCommand` (`[LocalOnly]`, 28 props, `Create`/`ApplySettlement`/`Release`),
  `OutboxMessage` (+ `OutboxStatus`), `LocalSequenceCounter`; configs in `Configurations/Pipeline/`
  (unique OperationId & LocalSequence; `(Status, NextAttemptUtc)`, `(AggregateType, AggregateId)`,
  `DependsOnOperationId`, `MessageType`); `Commands/PipelineSql` (`NextSequenceAsync` upsert-increment RETURNING,
  `ClaimPendingCommandsAsync` / `ClaimOutboxAsync` = single conditional UPDATE with UTC-ticks lease — A-RULE 025/026),
  `Commands/LocalSequenceAllocator`, `Commands/CommandJournal` (replay-safe `EnqueueAsync` without SaveChanges,
  `ApplySettlementAsync` gone through `SettlementTransitions.EnsureAllowed`), `Outbox/LocalOutbox`, `Outbox/OutboxClaim`
  (claim/complete/fail, 60 s lease), `Outbox/OutboxRelayHandler` (writes every `DomainEvent` to the outbox).
  Mutating queries use explicit `.AsTracking()` (NoTracking is the default).
  `DomainEventDispatcher` now walks the concrete type up to `DomainEvent` so base-type handlers (the relay) receive
  derived events. `AnimoraLocalDbContext` pins payload max length 65536 / fingerprint 64. `InternalsVisibleTo`
  `Animora.Persistence.Tests` added for the internal entity factories.
- **DI**: `AddAnimoraLocalStore` registers `ICommandJournal`, `ILocalSequenceAllocator`, `IOutbox`, `OutboxClaim`,
  `LocalCommandProcessor`, the relay handler, and `TryAdd` defaults for `ICorrelationContext`, `IIntegrityAlertSink`,
  `ICommandTransport` so the local store is self-sufficient.
- **Migration** `20260924142221_004_CommandPipeline` — additive only (creates `LocalSequenceCounters`,
  `OutboxMessages`, `PendingCommands` + indexes); 001–003 byte-identical.
- **Tests**: Application `IdempotencyTests` (6), `SettlementTransitionTests` (3, all 64 pairs),
  `CommandProcessorTests` (7) + `CommandPipelineTestDoubles`; Persistence `CommandJournalTests` (7),
  `OutboxClaimTests` (4). Updated `BehaviorPipelineTests` (7-link chain ending `CommandJournal`),
  `TestDoubles` (pipeline doubles), `LocalMigrationTests` (count 4), `StoreStartupTests` ("004").
- Gate: focused Application 49/49 + Persistence 68/68 green; `build.sh full` 0/0. `test.sh`/`run.sh` not re-run
  in the finishing session (user directive: no heavy tests).

### A12 — Track A test consolidation (2026-09-24)

- **Test classes added — Domain (`tests/Animora.Domain.Tests`, A05–A08):** `SlotConflictPolicyTests`,
  `AppointmentStateMachineTests`, `BoardingStayTests`, `ReceptionQueueTests`, `StockCalculatorTests`,
  `StockMovementFactoryTests`, `ProductInvariantTests`, `PurchaseOrderTests`, `InvoiceStateMachineTests`,
  `InvoiceImmutabilityTests`, `InvoiceTotalsTests`, `PaymentTests`, `CashSessionTests`, `JournalEntryTests`,
  `PostingRulesTests`, `ChequeLifecycleTests`, `AccountingPeriodTests` (+ helper `FakeClock`).
- **Test classes added — Persistence (`tests/Animora.Persistence.Tests`, A09–A11):** `LocalMigrationTests`,
  `ConverterTests`, `QueryFilterTests`, `BusinessMappingTests`, `AppendOnlyGuardTests`, `UnitOfWorkTests`,
  `DerivedQueryTests`, `SeedingTests`, `NativeSqliteVersionTests`, `StoreStartupTests`, `BackupRestoreTests`,
  `QuarantineRebuildTests`, `MonotonicClockTests`, `LocalFileStoreTests` (+ helper `TestSupport` with the
  `CommandCounter` interceptor).
- **Fixture:** `LocalStoreTestFixture` added — shared temp **file** SQLite db per test (never `:memory:`).
- **Test count:** 321 total (Domain 261, Persistence 57, Application/Sync/Server 1 each). Before the finishing
  fixes the run was Domain 259/261 (2 red, below); both are fixed so the expected result is 321/321 green.
- **Bug-fix (A02 defect, minimal):** `IdGenerator` — `NewId()` (monotonic) and `NewId(DateTimeOffset)`
  (explicit) shared one static high-water (`LastRandom`/`_lastMs`). An explicit call with a past instant reset
  it, so the next parameterless id in the same real millisecond drew fresh randomness and could sort *below*
  the previous one (flaky `IdGeneratorTests.TenThousandIds_AreUniqueAndStrictlyAscending`, ~1/15 in the full
  parallel run). Fix: the explicit path now keeps its own `LastRandomExplicit`/`_lastExplicitMs`, under the
  same lock. Public signatures and the "explicit earlier instant is honoured as given" contract are unchanged.
- **Test-side fixes (no production change):** `PaymentTests` reversal scenario (pay 40k then reverse −40k);
  `CashSessionTests` variance-note amounts (declared 710000 vs expected 700000); reflection assertions (below).
- **No production type/migration/DI/mock change otherwise.** No committed migration edited.
- **Deviations:**
  - `tests/Animora.Persistence.Tests/Animora.Persistence.Tests.csproj` gained a `ProjectReference` to
    `src/Animora.Infrastructure.Local` (A11 store-hardening types are under test).
  - `PaymentTests.PaymentAndAdjustment_ExposeNoPublicSetter` and `JournalEntryTests.JournalEntry_ExposesNoPublicSetter`
    assert **no mutable public setter**: a public setter, if present, must be init-only (`IsExternalInit`
    modreq). `InvoiceAdjustment` is a positional record whose `init` setters are construction-only, which
    satisfies A-RULE 002/003 ("never edited"). Inherited `SyncableEntity` transport properties stay skipped.
- **Gate:** this is the first phase whose entry records test.sh = TESTS OK for the full Track A suite.
  Note: the finishing session (reflection + IdGenerator fixes) did not re-run the full build, `test.sh` or
  `run.sh` (explicit user directive to skip long-running scripts); the first session to run them must confirm
  `Build 0/0`, `TESTS OK` and `SMOKE RUN OK` before starting B01, and amend this line if anything is red.

### A11 — Local hardening: keystore, encryption seam, native assertion, startup, maintenance, clocks (2026-09-24)

- **Keystore (A11a):** `ISecretStore` → `DpapiSecretStore` (win), `KeychainSecretStore` (`/usr/bin/security`, mac), opt-in dev `FileFallbackSecretStore` (warns every read), fail-closed `UnavailableSecretStore` otherwise. `LocalDbKeyService` (32-byte random passphrase; non-NotFound read failure → `STORE_KEY_UNAVAILABLE`; `GetKey()` throws) replaces `NullStoreKeyProvider`. `InstallationIdentity` (SHA-256 fingerprint hash).
- **Encryption seam (A11b):** `IStoreEncryptionProvider` — `NoEncryptionProvider` default; `MultipleCiphersEncryptionProvider` only when `EncryptionEnabled && K2Approved` (else `Validate()` → `STORE_ENCRYPTION_NOT_APPROVED`). Interceptor applies key then WAL/NORMAL/busy_timeout/foreign_keys, sync + async + `Prepare`.
- **Native assertion (A11c):** `NativeSqliteAssertion` (≥ 3.50.2, else `NATIVE_SQLITE_TOO_OLD`, detail = actual).
- **Startup (A11d):** `LocalStoreStartup` 8 steps — (1) private data dir + crash flag, (2) `Validate` + key before any file, (3) open + native assert, (4) `quick_check` → quarantine/rebuild, (5) migrate, (6) WAL checkpoint/crash log, (7) installation bootstrap + seed, (8) `LocalStoreDiagnostics` (path, native version, encryption, `KeySource`, latest migration, recovered, quarantined, bytes, last backup). Never on UI thread; desktop wiring is C01/B05.
- **Maintenance (A11c):** backup (keyed, quick_check, retention), restore (quarantine current, re-init), quarantine-and-rebuild (move, never delete; no loop), vacuum; one `StoreMaintenanceLog` row per action.
- **Clocks (A11d):** `SystemClock`; `MonotonicClock` high-water persisted in `ClockHighWaters`, never decreases (A-RULE 011).
- **Files:** `LocalFileStore` (content-addressed sha256, atomic, dedupe) behind `Application/Ports/ILocalFileStore`.
- **Schema:** 3 local-only entities (`LocalInstallation`, `ClockHighWater`, `StoreMaintenanceLog`), migration `003_LocalHardening`; 001/002 unchanged. ErrorCodes +6 store/native codes.
- **DI:** `AddAnimoraLocalInfrastructure` (wraps `AddAnimoraLocalStore`) is the single entry point. **No Desktop DI change; no mock deleted.**
- **Deviations:** `LocalStoreDiagnostics`/`KeySource`/`ILocalStoreInitializer` live in `Persistence.Sqlite/Maintenance` (avoid Persistence↔Infrastructure cycle; initializer resolved lazily); entry point named `AddAnimoraLocalInfrastructure`; `System.Security.Cryptography.ProtectedData` pinned 10.0.12; `LocalStoreStartup` writes Checkpoint/CrashRecovery log rows via parameterized SQL (entity factory is internal to Persistence); `LocalDbContextFactory.DesignTimeClock` kept (design-time only).
- **A12 test debt:**
  - `NativeSqliteVersionTests`: runtime version ≥ 3.50.2; `Parse`/`Assert` → `NATIVE_SQLITE_TOO_OLD` for a lower version.
  - `StoreStartupTests`: 8 steps in order (step log); after init `journal_mode=wal`, `synchronous=NORMAL(1)`, `busy_timeout=5000`, `foreign_keys=1`; `EncryptionEnabled` + no key + no fallback → `STORE_KEY_UNAVAILABLE` and no `animora.db`; `EncryptionEnabled` + `!K2Approved` → `Validate()` = `STORE_ENCRYPTION_NOT_APPROVED`.
  - `BackupRestoreTests`: backup → quick_check ok → restore → pre-backup rows intact; retention keeps exactly `BackupRetentionCount`; one `StoreMaintenanceLog` row per action.
  - `QuarantineRebuildTests`: header-corrupted DB moved (not deleted) with `-wal`/`-shm` into `quarantine/<timestamp>/`; fresh migrated + seeded DB; `Quarantined = true`.
  - `MonotonicClockTests`: earlier `Observe` never lowers `HighWaterUtc`; value survives a new context.
  - `LocalFileStoreTests`: put/open/delete round trip, dedupe, invalid hash rejected.

### A10 — Local persistence II: business mappings, UoW, seed, read helpers, migration 002 (2026-09-24)

- **Files:** `Persistence.Sqlite/Conventions/OwnedRows.cs`, `Converters/FixedScaleQuantityConverter.cs`, `Context/AppendOnlyGuard.cs`, `Repositories/*` (18 repos, `LocalUnitOfWork`, `DomainEventDispatcher`), `Seed/*` (`LocalSeeder`, `SeedMarker`, `PersianMonthPeriodCodeProvider`), `Queries/{QueryPaging,QueryGuards,StockOnHandQuery,InvoiceBalanceQuery,LedgerBalanceQuery}.cs`, `Migrations/20260924054447_002_LocalBusinessSchema{,.Designer}.cs` + snapshot.
- **Contracts (Track C):** `QueryPaging.ToPagedAsync<T>(IQueryable<T>, page, pageSize, LocalStoreOptions, ct) → PagedResult<T>`; `StockOnHandQuery.GetAsync(ids) → IReadOnlyDictionary<(ProductId, VariantId), decimal>`; `InvoiceBalanceQuery.GetAsync(ids) → IReadOnlyDictionary<string, InvoiceBalance(InvoiceId, Currency, GrandTotalMinor, AdjustmentsMinor, PaidMinor, DueMinor)>` (Due = Grand − Paid − Adjustments); `LedgerBalanceQuery.GetAsync(periodIds) → IReadOnlyList<LedgerBalance(PeriodId, AccountCode, Currency, DebitMinor, CreditMinor, BalanceMinor)>`.
- **DI (`AddAnimoraLocalStore`):** scoped context factory + context, 18 repos, UoW, dispatcher (+ singleton handler map), `LocalSeeder`, singleton period-code provider, scoped `StockOnHandQuery`/`InvoiceBalanceQuery`/`LedgerBalanceQuery`.
- **Migration 002:** additive only — 16 aggregate tables (Accounting/Cash/Cheques/Expenses/GoodsReceipts/Invoices/Journal/Ledger/Payments/Categories/Products/POs/SeedMarkers/StockMovements/Suppliers/Units) + 8 owned-row tables (CashMovements, ChequeEvents, GoodsReceiptLines, InvoiceAdjustments, InvoiceLines, JournalLines, ProductVariants, PurchaseOrderLines), 39 indexes. Money = `<N>Minor INTEGER` + `<N>Currency TEXT`; summable quantities `<N>Scaled INTEGER`; no JSON money. `001_LocalCoreSchema` + Designer byte-identical.
- **Verification:** `dotnet ef migrations list` → 001, 002; `has-pending-model-changes` → none; `build.sh full` 0/0; `run.sh` SMOKE RUN OK (desktop still on mocks). One-off throwaway console (not committed) migrated a fresh DB and executed the 3 aggregates (SQL translation, no client eval).
- **Deviations:** InvoiceBalance is one SELECT with correlated sub-aggregates (payments + adjustments + currency checks) rather than a standalone GROUP BY, to keep one command per invoice batch; ledger groups also by currency (multi-currency safety). Paging skips the item query when the page is past the end (1 command).
- **No tests** (by phase rule). **A12 test debt:** schema/owned-row tables/indexes/round trips + no JSON money; append-only update/delete protection per protected type; transaction + handler-failure rollback, exactly-once in-transaction dispatch, mapped concurrency error; stock negative adjustment, signed partial payments/refunds, ledger debit−credit each from one SQL aggregate; paging bounds/count; idempotent double seed, tenant match, 20 accounts, all permissions/5 roles, 7 units, 5 categories, 8 rooms, 1 open period, catalog values.

### A09 — Local persistence I: DbContext and core mappings (2026-09-23)

- **Files:** `src/Animora.Persistence.Sqlite/{Options,Context,Converters,Conventions,Configurations/{Identity,Patients,Clinical,Scheduling,Boarding},Migrations}`; 18 entity configurations, the initial `001_LocalCoreSchema` migration and snapshot. Options, scoped DbContext, tenant/soft-delete named filters, UTC-ticks and invariant decimal converters, exact minor-unit Money columns, delta indexes, key-provider seam, and SQLite pragma interceptor are in place.
- **No tests written or run** in A09: migration application, 18 tables/indexes, no REAL/NUMERIC, converter round-trips, named-filter isolation, and foreign-tenant SaveChanges guard are owed to A12. No desktop DI changes; no mock deleted.
- **Deviation D-A09-1:** Immutable positional child records (`StayItem`, `VisitServiceLine`, etc.) have constructor parameters of complex value-object types (`Money`/`Quantity`), which EF Core cannot bind as owned JSON entities during model validation. They are instead persisted to the same aggregate JSON columns using field-backed converters and structural value comparers. A12 must verify child-collection materialization/change tracking. Role permission sets use JSON converters with structural comparers for the same reason. The `AppointmentSlot` property is derived and not stored separately; `StartUtc`/`DurationMinutes` are the persisted columns. Due-date index uses the domain's `NextDueUtc` column under the roadmap's requested `DueUtc` index name.


> Append one entry per completed phase, newest first. Required fields:
> **files added**, **contracts added**, **DI changes**, **mocks deleted**, **migrations created**,
> **tests added**, **verification** (build/test/smoke results), **deviations**.

### A08 — Domain: finance ledger, cheques, expenses (2026-09-23)

- **Files added (src, 11):**
  - `Finance/LedgerAccount.cs` — `sealed class LedgerAccount : AggregateRoot` (`Code`, `Name`,
    `AccountType Type`, `ParentAccountId?`, `IsPostable`, `IsActive`, `Description?`) +
    `enum AccountType { Asset, Liability, Equity, Revenue, Expense }`. `NormalBalance` is **derived**
    from `Type` (`Asset`/`Expense` → Debit, else Credit) through `static EntrySide
    NormalBalanceOf(AccountType)` — never a stored column that could drift. `Create` validates that
    the code is a numeric string (indexed `for`, no regex/LINQ).
  - `Finance/ChartOfAccounts.cs` — `static class AccountCodes` with the **20 `const string` codes**
    exactly as tabled (1000/1010/1020/1100/1110/1200/1900 · 2000/2010/2100/2200 · 3000 ·
    4000/4010/4020/4900 · 5000/6000/6100/6200) and `static class ChartOfAccounts` holding the
    authoritative seed as `ChartOfAccountsEntry(Code, Name, Type, IsPostable)` rows with
    `Default` / `Find` / `Contains` / `IsPostable` (ordinal dictionary lookup). Every later
    reference uses an `AccountCodes` constant; a numeric literal appears nowhere else.
  - `Finance/JournalLine.cs` — `sealed record JournalLine(LineId, AccountCode, EntrySide Side,
    Money Amount, Memo?, CostCenter?)` + `enum EntrySide { Debit, Credit }`. Direction lives in
    `Side`, **never** in the sign of `Amount`; `SignedMinor` and `Mirror(lineId)` are the balance
    check and the reversal building block.
  - `Finance/JournalEntry.cs` — `sealed class JournalEntry : AggregateRoot`, **append-only forever**:
    every property is `private init`, there is **no mutator, no delete, no business soft-delete**
    (A-RULE 002 / R-B22, ARCHITECTURE §8.5 — a ledger row therefore cannot produce a sync conflict
    by construction). `Create(...)` is the only constructor path and validates the whole double
    entry in **one `for` pass** (PERFORMANCE §2): `< 2` lines → `JOURNAL_TOO_FEW_LINES`, non-positive
    amount → `JOURNAL_LINE_AMOUNT_INVALID`, second currency → `JOURNAL_CURRENCY_MIXED`, empty
    account → `ACCOUNT_UNKNOWN`, default header account → `ACCOUNT_NOT_POSTABLE`, `Σ D ≠ Σ C` →
    `JOURNAL_UNBALANCED`; on success it raises `JournalEntryPostedEvent` and caches `TotalDebit`.
    `CreateReversal(original, reason, IClock, …)` is the **only** correction path: mirrored lines,
    `Source = Reversal`, `ReversesEntryId` set and the mandatory reason folded into the memo
    (A-RULE 003) — the original row is never touched.
  - `Finance/PostingRules.cs` — the **authoritative** event → journal table as 10 pure
    `For<EventName>` methods covering all 14 tabled rows, plus
    `readonly record struct PostingContext(EntryNumber, PeriodId, PostedByUserId, OperationId?)`.
    Each builds its lines and exits through `JournalEntry.Create`, so balance / positivity / single
    currency / postability are **structurally guaranteed**; a zero-amount component emits no line.
    Rows: `ForInvoicePosted` (D 1100+4900+1110 · C 4000/4010/4020 split by `InvoiceLineKind` + 2200),
    `ForPaymentRegistered` (Cash→1000, Card/Transfer→1010, Cheque→1020, Insurance→1110,
    Wallet→2100 · C 1100; `Credit` moves no money and is refused), `ForInvoiceRefunded`
    (D 1100 · C 1000/1010 by original method), `ForInvoiceVoided` (= `CreateReversal` of the posting
    entry), `ForGoodsReceiptPosted` (D 1200 · C 2000), `ForStockMovementRecorded`
    (Sale/Consumption → D 5000, Loss/AdjustmentOut → D 6200 · C 1200, at the weighted-average cost
    passed in — the rule never computes cost), `ForChequeCleared` (inbound D 1010/C 1020, outbound
    D 2010/C 1010), `ForChequeBounced` (inbound D 1100/C 1020, outbound D 2010/C 2000),
    `ForCashSessionClosed` (shortage D 1900/C 1000, overage swapped; zero variance posts nothing),
    `ForExpensePosted` (D 6000/6100 by category · C 1000/1010/2010/2000 by settlement). Every entry
    carries `SourceEntityType` + `SourceEntityId` + `OperationId`.
  - `Finance/AccountingPeriod.cs` — `sealed class AccountingPeriod : AggregateRoot` with the opaque
    `PeriodCode` (`"1405-07"` — **the Domain never computes or parses Jalali**; the Desktop formats
    it, CONVENTIONS §2), UTC `StartUtc`/`EndUtc`, `PeriodState { Open, Closing, Closed, Locked }`,
    close audit fields. Forward-only machine `Open → Closing → Closed → Locked` behind
    `static bool IsTransitionAllowed` + one private `Transition` gate — **no reopen** (a post-close
    correction is an entry in the next open period). `Close(hasUnsettledCommands,
    hasOpenCashSessions, hasDraftInvoices, IClock, closingEntryId, closedByUserId, note?)` returns
    `PERIOD_CLOSE_BLOCKED` with the blocker named in `Detail`, otherwise stamps the close and raises
    `PeriodClosedEvent`; `static Result EnsurePostable(period)` returns `PERIOD_CLOSED` for
    `Closed`/`Locked` (a `Closing` period still accepts the closing entry itself).
  - `Finance/ChequeEvent.cs` — `sealed record ChequeEvent(EventId, FromState, ToState, OccurredUtc,
    ActorUserId, Reason?)`: the append-only cheque history row.
  - `Finance/Cheque.cs` — `sealed class Cheque : AggregateRoot` + `enum ChequeDirection { Inbound,
    Outbound }`, `enum ChequeState { Registered, InPortfolio, Deposited, Cleared, Bounced, Returned,
    Cancelled, Settled }`. The 10 legal transitions are declared once in
    `static bool IsTransitionAllowed` and every mutator (`MoveToPortfolio`, `Deposit`, `MarkCleared`,
    `MarkBounced`, `Return`, `Cancel`, `SettleInCash`) funnels through a single private `Transition`
    that validates, appends **exactly one** `ChequeEvent` and raises `ChequeStateChangedEvent` — so
    no transition can happen without a trace; illegal ones return `CHEQUE_INVALID_TRANSITION` and
    change nothing. `MarkCleared`/`MarkBounced` additionally raise the ledger-facing
    `ChequeClearedEvent` / `ChequeBouncedEvent` (bounce reason mandatory). `Register` enforces the
    counterparty invariant (`Inbound` ⇒ `OwnerId`, `Outbound` ⇒ `SupplierId`) →
    `CHEQUE_COUNTERPARTY_REQUIRED`, and a strictly positive face amount.
  - `Finance/Expense.cs` — `sealed class Expense : AggregateRoot` + `enum ExpenseState { Draft,
    Posted, Reversed }`. Editing is draft-only (`Edit`, `SetSettlement`, `AttachReceipt` →
    `EXPENSE_IMMUTABLE` once posted); `Post(IClock)` is **one-way** and raises `ExpensePostedEvent`;
    a correction is a new expense carrying `ReversesExpenseId` (+ `MarkReversed` on the original).
    `DebitAccountCode` resolves through `ExpenseCategories`, so the expense never names an account.
  - `Finance/ExpenseCategories.cs` — the 9 seeded codes (`RENT`, `UTILITIES`, `SALARY`, `SUPPLIES`,
    `MAINTENANCE`, `MARKETING`, `TAX`, `TRANSPORT`, `OTHER`) + `AccountCodeFor(code)`:
    `SALARY` → `6100`, everything else (including an unknown code — an uncategorized cost must still
    post) → `6000`.
  - `Events/FinanceEvents.cs` — the 6 events: `JournalEntryPostedEvent(EntryId, PeriodId, Source,
    TotalDebitMinor, Currency)`, `ChequeStateChangedEvenoState)`,
    `ChequeClearedEvent(ChequeId, Direction, AmountMinor, Currency)`, `ChequeBouncedEvent(ChequeId,
    Direction, AmountMinor, Currency, Reason)`, `ExpensePostedEvent(ExpenseId, CategoryCode,
    AmountMinor, Currency, SettledMethod)`, `PeriodClosedEvent(PeriodId, ClosingEntryId)` — all
    `sealed record : DomainEvent(EventId, TenantId, OccurredAtUtc)`.
- **Files modified (1):** `Results/ErrorCodes.cs` — new `#region Finance (Phase A08)` with the 12
  codes: `JOURNAL_UNBALANCED`, `JOURNAL_TOO_FEW_LINES`, `JOURNAL_LINE_AMOUNT_INVALID`,
  `JOURNAL_CURRENCY_MIXED`, `ACCOUNT_NOT_POSTABLE`, `ACCOUNT_UNKNOWN`, `PERIOD_CLOSED`,
  `PERIOD_CLOSE_BLOCKED`, `PERIOD_INVALID_TRANSITION`, `CHEQUE_INVALID_TRANSITION`,
  `CHEQUE_COUNTERPARTY_REQUIRED`, `EXPENSE_IMMUTABLE` (46 + 14 + 12 → **72** total). No existing
  code renamed or removed.
- **Chart-of-accounts codes (authoritative):** 1000 Cash on Hand · 1010 Bank · 1020 Cheques
  Receivable · 1100 Accounts Receivable · 1110 Insurance Receivable · 1200 Inventory · 1900 Cash
  Variance · 2000 Accounts Payable · 2010 Cheques Payable · 2100 Customer Prepayments · 2200 Tax
  Payable · 3000 Owner Equity · 4000 Service Revenue · 4010 Product Revenue · 4020 Boarding Revenue ·
  4900 Sales Discounts · 5000 COGS · 6000 Operating Expense · 6100 Salary Expense · 6200 Inventory
  Loss. All 20 are postable; later phases reference them only as `AccountCodes.*`.
- **State machines added:** period `Open → Closing → Closed → Locked` (no reopen); cheque
  `Registered → InPortfolio|Cancelled`, `InPortfolio → Deposited|Returned|Cancelled`,
  `Deposited → Cleared|Bounced`, `Bounced → InPortfolio|Returned|Settled`, with
  `Cleared`/`Returned`/`Cancelled`/`Settled` terminal.
- **Contracts added:** none. `Animora.Contracts` and `Animora.Contracts.Wire` untouched; the
  Domain's `ChequeDirection`/`ChequeState` are deliberately separate types from the Contracts'
  UI-facing `ChequeDirection`/`ChequeStatus` (the Domain references no Contracts type — CONVENTIONS
  §2). C08 maps between them with an explicit switch, never a cast.
- **DI changes:** none. **Mocks deleted:** none. **Migrations created:** none (A09 maps these
  entities).
- **Tests added:** **none — this is a Track A phase (ROADMAP §3.10 / CONVENTIONS §9).** The full
  coverage is owed to **A12**: `JournalEntryTests` (unbalanced / 1-line / mixed-currency codes;
  `CreateReversal` mirrors and nets to zero per account; **no public setter exists**, asserted by
  reflection over `BindingFlags.DeclaredOnly` — the inherited `SyncableEntity` transport columns
  (`TenantId`/`UpdatedAt`/`Version`/`Deleted`) are deliberately settable and out of that assertion's
  scope), `PostingRulesTests` (all 14 rows balanced, only postable chart accounts, source
  entity + `OperationId` carried), `ChequeLifecycleTests` (all 64 `ChequeState` pairs — only the 10
  tabled ones succeed; exactly one `ChequeEvent` per success; counterparty guard),
  `AccountingPeriodTests` (each of the 3 close blockers → `PERIOD_CLOSE_BLOCKED`; `EnsurePostable`
  rejects `Closed`/`Locked`; no transition back to `Open`).
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors** (14 projects);
  `scripts/sandbox/run.sh` = **SMOKE RUN OK**; `test.sh` deliberately **not** invoked (A12 owns it);
  `src/Animora.Domain/Animora.Domain.csproj` still contains **zero** `PackageReference` /
  `ProjectReference` — the file is literally `<Project Sdk="Microsoft.NET.Sdk"></Project>`.
- **Deviations:** see D-A08-1 below (supporting members the phase table implied but did not name).

### A07 — Domain: sales & cashier (2026-09-23)

- **Files added (src, 9):**
  - `Sales/SettlementStatus.cs` — `enum SettlementStatus { Draft, LocallyPosted, PendingSettlement,
    Settled, Rejected, Conflict, Reversed, NeedsAttention }`, the exact `ARCHITECTURE.md` §8.3 list.
    **Defined here once** and reused by B02 / D05 / E06 — never redefined. `Invoice` and `Payment` both
    carry it so the UI can always show where a money command stands (never hidden).
  - `Sales/InvoiceLine.cs` — `sealed record InvoiceLine(LineId, Kind, CatalogItemId?, ProductId?,
    VariantId?, Title, Quantity, UnitPrice, DiscountPercent, TaxPercent, Source, SourceEntityId?)` +
    `enum InvoiceLineKind { Service, Product, Boarding, Custom }`,
    `enum InvoiceLineSource { Visit, Stay, Catalog, Manual }`; computed `LineGross`, `LineDiscount`,
    `LineTax`, `LineNet`. Quantity extension is `unitPrice.ApplyPercent(q × 100)` — no inline rounding.
    `LineTax` is documented as **line-local**; the authoritative tax is `InvoiceTotals`'.
  - `Sales/InvoiceTotals.cs` — the **single** totals algorithm, pure static, indexed `for` loops, no
    LINQ: `Compute(lines, discountKind, discountValue, insurancePercent, currency)` →
    `InvoiceTotalsSnapshot(Subtotal, LineDiscountTotal, InvoiceDiscount, DiscountTotal, TaxTotal,
    InsuranceCoverage, GrandTotal)` exactly as tabled in the phase file, plus
    `ComputeDue(grandTotal, paidTotal, adjustments)`. The invoice discount is spread over the lines
    **before** tax by weighted largest-remainder allocation (weights = post-line-discount nets;
    degrades to `Money.Allocate` when all weights are zero), so the shares sum back to exactly the
    invoice discount and no minor unit is lost. `Money.ApplyPercent` is the only rounding boundary.
    Also hosts `enum DiscountKind { Percent, Amount }` (the Domain's own; `Animora.Contracts` keeps a
    separate UI-facing copy — the Domain references no Contracts type).
  - `Sales/Invoice.cs` — `sealed class Invoice : AggregateRoot` + `enum InvoiceKind { Visit, Boarding,
    DirectSale, SalesReturn }`, `enum InvoiceState { Draft, Posted, PartiallyPaid, Paid, Void,
    Refunded }`. One `Result Transition(...)` gate over `static bool IsTransitionAllowed(from, to)`
    (exposed for A12) implementing the tabled machine exactly (`INVOICE_INVALID_TRANSITION`).
    **[INVARIANT] Immutability after posting (A-RULE 002):** a single private `EnsureDraft()` guards
    `AddLine`, `RemoveLine`, `EditLine`, `SetDiscount`, `SetInsurance` (plus `AttachVisit`,
    `AttachStay`, `AssignOwner`) → `INVOICE_IMMUTABLE`. `RecalculateTotals()` (delegating to
    `InvoiceTotals`) is the only writer of the totals and runs after every change; `Post(IClock)`
    re-verifies stored vs recomputed totals (`INVOICE_TOTAL_MISMATCH`), refuses an empty invoice
    (`INVOICE_EMPTY`) and a negative grand total (`INVOICE_NEGATIVE_TOTAL`), stamps `PostedUtc`, sets
    `Settlement = LocallyPosted` and raises `InvoicePostedEvent`. `ApplyPayment(Payment)` rejects a
    foreign invoice id, a duplicate `OperationId` (`PAYMENT_DUPLICATE_OPERATION`, `PaidTotal`
    untouched — A-RULE 006) and an over-payment (`PAYMENT_EXCEEDS_DUE`), then advances
    `PartiallyPaid`/`Paid` and raises `PaymentRegisteredEvent`. `ApplyRefund` (→ `Refunded`,
    `ADJUSTMENT_EXCEEDS_PAID` guard, `Settlement = Reversed`, `InvoiceRefundedEvent`),
    `AddCreditNote` (every non-refund adjustment kind), `Void(reason, IClock)` (only while
    `PaidTotal == 0`, else `INVOICE_VOID_AFTER_PAYMENT`; raises `InvoiceVoidedEvent`),
    `MarkSettlement(status, errorCode?)`. `InvoiceNumber` is assigned by the Application layer (B01),
    never by the entity.
  - `Sales/Payment.cs` — `sealed class Payment : SyncableEntity`, **append-only, zero mutators**
    (every property `private init`), `enum PaymentMethod { Cash, Card, Transfer, Cheque, Credit,
    Insurance, Wallet }`. `Create(...) → Result<Payment>` is the only way to obtain one: refuses a
    zero amount and enforces the reference rule — cash needs `CashSessionId`, a cheque needs
    `ChequeId` (`PAYMENT_METHOD_REFERENCE_REQUIRED`). A reversal is a **new** payment with a negative
    `Amount` and `ReversesPaymentId` set (A-RULE 002/003); `OperationId` is the idempotency key.
  - `Sales/InvoiceAdjustment.cs` — `sealed record InvoiceAdjustment(AdjustmentId, Kind, Amount,
    Reason, CreatedUtc, CreatedByUserId, OperationId?, ReversesAdjustmentId?)` +
    `enum AdjustmentKind { CreditNote, Refund, WriteOff, RoundingCorrection, LineReturn }`. The
    **only** mechanism that changes a posted invoice's economics. Sign convention documented:
    positive reduces the debt (credit note / write-off), negative re-opens it (refund, paired with a
    negative `Payment`).
  - `Cashier/CashMovement.cs` — `sealed record CashMovement(MovementId, Kind, Amount, InvoiceId?,
    PaymentId?, Reason, OccurredUtc, RecordedByUserId, OperationId?)` + `enum CashMovementKind
    { SalePayment, RefundOut, PettyCashOut, FloatIn, BankDropOut, CorrectionIn, CorrectionOut }`.
    **Sign rule** (`SalePayment`/`FloatIn`/`CorrectionIn` positive, the other four negative, zero never
    a movement) enforced in the `Create(...) → Result<CashMovement>` factory (`MOVEMENT_SIGN_INVALID`,
    reusing A06's code — the same rule, one code).
  - `Cashier/CashSession.cs` — `sealed class CashSession : AggregateRoot` + `enum CashSessionState
    { Open, Closing, Closed, Reconciled }`, strictly forward machine via one `Transition(...)` gate
    over `static IsTransitionAllowed`. `Open(...)` factory raises `CashSessionOpenedEvent`;
    `static Result EnsureNoOpenSession(bool anyOpenExists)` → `CASH_SESSION_ALREADY_OPEN` (the unique
    filtered index in A09 is the second line of defence); `AddMovement` refused unless `Open`
    (`CASH_SESSION_CLOSED`); private `Recalculate()` is the only writer of
    `ExpectedClosing = OpeningFloat + Σ movements` and `Variance = DeclaredClosing - ExpectedClosing`;
    `BeginClose(declared, note, IClock, hasUnsettled)` refuses while a linked command is unsettled
    (`CASH_SESSION_UNSETTLED_COMMANDS`) and requires a note for a non-zero variance
    (`CASH_VARIANCE_NOTE_REQUIRED` — a variance itself is allowed); `Close(IClock)` raises
    `CashSessionClosedEvent`; `Reconcile(userId, IClock)`.
  - `Events/SalesEvents.cs` — `InvoicePostedEvent(InvoiceId, OwnerId, GrandTotalMinor, Currency,
    VisitId, BoardingStayId)`, `PaymentRegisteredEvent(PaymentId, InvoiceId, AmountMinor, Currency,
    Method)`, `InvoiceRefundedEvent(InvoiceId, AdjustmentId, AmountMinor, Currency)`,
    `InvoiceVoidedEvent(InvoiceId, Reason)`, `CashSessionOpenedEvent(SessionId, CashierUserId)`,
    `CashSessionClosedEvent(SessionId, VarianceMinor, Currency)` — all
    `: DomainEvent(EventId, TenantId, OccurredAtUtc)`.
- **Files modified (1):** `Results/ErrorCodes.cs` — new `#region Sales & Cashier (Phase A07)` with
  **14** codes (→ 60 total): `INVOICE_IMMUTABLE`, `INVOICE_INVALID_TRANSITION`,
  `INVOICE_TOTAL_MISMATCH`, `INVOICE_NEGATIVE_TOTAL`, `INVOICE_EMPTY`, `INVOICE_VOID_AFTER_PAYMENT`,
  `PAYMENT_EXCEEDS_DUE`, `PAYMENT_DUPLICATE_OPERATION`, `PAYMENT_METHOD_REFERENCE_REQUIRED`,
  `CASH_SESSION_ALREADY_OPEN`, `CASH_SESSION_CLOSED`, `CASH_VARIANCE_NOTE_REQUIRED`,
  `CASH_SESSION_UNSETTLED_COMMANDS`, `ADJUSTMENT_EXCEEDS_PAID`. No existing code renamed or removed.
- **Key decisions recorded:** (1) the totals algorithm lives **only** in `InvoiceTotals` — client
  (C07) and server (D07) must both call it so they can never disagree; (2) the invoice discount is
  pro-rated over lines *before* tax, by weight, with largest-remainder so the shares are exact;
  (3) payments and adjustments are **facts**, never edited — a correction is a compensating entry
  with a reason (A-RULE 002/003), which is also why offline conflict resolution here is additive and
  never LWW (CONVENTIONS §6 item 9); (4) a negative payment (a refund's compensating entry) skips the
  `PAYMENT_EXCEEDS_DUE` check because the paired `InvoiceAdjustment` already authorized it.
- **Contracts added:** none (`Animora.Contracts` untouched). **DI changes:** none. **Mocks deleted:**
  none. **Migrations created:** none. `Animora.Domain.csproj` still has zero references.
- **Tests added:** **none — Track A phase (ROADMAP §3.10); all coverage is owed to A12**
  (`InvoiceStateMachineTests`, `InvoiceImmutabilityTests`, `InvoiceTotalsTests`, `PaymentTests`,
  `CashSessionTests` — exactly as listed in the phase file's "Test coverage owed to A12").
- **Verification:** `scripts/sandbox/build.sh full` → **0 warnings / 0 errors** (14 projects);
  `scripts/sandbox/run.sh` → **SMOKE RUN OK**. `test.sh` deliberately not invoked (A12 owns it).
- **Deviations:** see D-A07-1 below (small additions inside the listed files: `DiscountKind` enum,
  `InvoiceTotalsSnapshot`, `InvoiceTotals.ComputeDue`, `CashSession.Open`/`ReconciledByUserId`,
  `Invoice.SettlementErrorCode`).

### A06 — Domain: inventory & purchasing (2026-09-23)

- **Files added (src, 13):**
  - `Inventory/UnitOfMeasure.cs` — `sealed class UnitOfMeasure : AggregateRoot`: `Code`, `DisplayKey`,
    `DecimalPlaces` ∈ [0, 3], `BaseUnitCode?`, `ConversionToBase?`. All-or-nothing conversion invariant
    (both null or both set, factor > 0) enforced in `Create` and `SetConversion` (`VALIDATION_FAILED`).
    Units are **never converted silently** anywhere in the domain — mixing them is `UNIT_MISMATCH`.
  - `Inventory/ProductCategory.cs` — `sealed class ProductCategory : AggregateRoot`: `Code`,
    `DisplayName`, `ParentCategoryId?`, `SortOrder`, `IsActive`. Cycle guard (`CATEGORY_CYCLE`) takes the
    **supplied** ancestor chain of the candidate parent (the Domain has no repository) and walks it with a
    `for` loop; `Create`, `MoveTo`, `Rename`, `Deactivate`, `Activate`.
  - `Inventory/ProductVariant.cs` — `sealed record ProductVariant(VariantId, Sku, AttributeName,
    AttributeValue, CostPrice, SalePrice, ReorderLevel, Barcode?, IsActive)` + `const DefaultAttributeName
    = "DEFAULT"` and `IsDefault`. Every movement references a `VariantId`, never a bare `ProductId`.
  - `Inventory/Product.cs` — `sealed class Product : AggregateRoot` + `enum ProductKind { Medicine,
    Vaccine, Food, PetShop, Consumable, Equipment, Service }`. **No stock property of any kind (R-B23)** —
    XML-doc states on-hand is always derived by `StockCalculator`; `grep -n "OnHand" Product.cs` returns
    nothing. Invariants: `Service ⇒ !TrackStock` (`SERVICE_NO_STOCK`); a stock-tracked product is created
    with, and always keeps, ≥ 1 active variant (`VARIANT_REQUIRED`); variant reorder levels use the
    product unit (`UNIT_MISMATCH`); variant SKUs unique inside the product (`PRODUCT_SKU_DUPLICATE`).
    `AddVariant`, `UpdateVariant`, `DeactivateVariant`, `SetPrices`, `SetReorderLevel`, `Describe`,
    `Deactivate`, `Activate`.
  - `Inventory/StockMovement.cs` — `sealed class StockMovement : SyncableEntity`, **append-only, zero
    mutators** (every property `private init`). `enum MovementKind` with all 10 values. **Sign rule
    (authoritative):** `Receipt/AdjustmentIn/TransferIn/ReturnIn` ⇒ `Quantity.Value > 0`, the other six
    ⇒ `< 0`, zero is never a movement — enforced by the `Create(...) → Result<StockMovement>` factory
    (`MOVEMENT_SIGN_INVALID`), the only way to obtain an instance. `static IsInboundKind`,
    `static OppositeKind`, and `static Reverse(original, …)` building the offsetting movement with
    `ReversesMovementId` set. Corrections are new rows; conflicts converge additively (§8.5).
  - `Inventory/StockLevel.cs` — `sealed record StockLevel(ProductId, VariantId, OnHand, Reserved,
    Available, AverageCost, EarliestExpiryUtc)` + `Empty(...)`, `IsOutOfStock`. Documented as a
    **rebuildable projection, never an authority**; A10 may cache it `[LocalOnly]`, ledger wins on
    disagreement.
  - `Inventory/StockCalculator.cs` — the **single** on-hand algorithm, pure static, indexed `for` loops,
    no LINQ, no temp collections (PERFORMANCE §2). `OnHand(movements)` = signed sum; `WeightedAverageCost`
    = moving weighted average over inbound, outbound consuming at the running average (**the** costing
    method — FIFO/LIFO need an ADR), accumulated in `decimal` and rounded once at the end;
    `EnsureSufficient(onHand, requested, allowNegative)` → `UNIT_MISMATCH` / `INSUFFICIENT_STOCK`;
    `IsBelowReorder`; `EarliestExpiry(inbound)`; plus `Project(...)` returning the full `StockLevel`.
    A mixed-unit/currency ledger is a caller defect and throws (CONVENTIONS §4).
  - `Purchasing/Supplier.cs` — `sealed class Supplier : AggregateRoot`: `Code`, `Name`, `ContactPerson?`,
    `Phone?`, `Email?`, `Address?`, `NationalId?`, `PaymentTermDays` ∈ [0, 365], `IsActive`, `Notes?`.
    Payables/cheques stay in A08.
  - `Purchasing/PurchaseOrderLine.cs` — `sealed record PurchaseOrderLine(LineId, ProductId, VariantId,
    OrderedQuantity, ReceivedQuantity, UnitCost, DiscountPercent, TaxPercent, LotNumber?, ExpiryUtc?)`
    with computed `Gross`, `DiscountAmount`, `NetAmount`, `TaxAmount`, `LineTotal`,
    `OutstandingQuantity`, `IsFullyReceived`. **All money goes through `Money.ApplyPercent`** — including
    the quantity extension (`unitCost.ApplyPercent(q × 100)`); no inline `Math.Round` anywhere
    (CONVENTIONS §4).
  - `Purchasing/PurchaseOrder.cs` — `sealed class PurchaseOrder : AggregateRoot` + `enum
    PurchaseOrderState { Draft, Submitted, PartiallyReceived, Received, Cancelled, Closed }`. One
    `Result Transition(...)` gate over a `static bool IsTransitionAllowed(from, to)` table (exposed for
    A12), matching the phase's machine exactly (`PO_INVALID_TRANSITION`). Lines frozen from `Submitted`
    on (`PO_LOCKED`); `RecalculateTotals()` is the only writer of `Subtotal`/`DiscountTotal`/`TaxTotal`/
    `GrandTotal` and runs after every line change; `Submit(IClock)` re-verifies stored totals against the
    line sum (`PO_TOTAL_MISMATCH`) and raises `PurchaseOrderSubmittedEvent`; `ApplyReceipt(GoodsReceipt)`
    adds the received quantity per matching (ProductId, VariantId) line and sets `PartiallyReceived` or
    `Received` — it writes **no** stock; `AddLine`, `RemoveLine`, `Cancel`, `Close`, `SetExpectedDate`.
  - `Purchasing/GoodsReceiptLine.cs` — `sealed record GoodsReceiptLine(LineId, ProductId, VariantId,
    Quantity, UnitCost, LotNumber?, ExpiryUtc?)` + computed `LineCost`. `Quantity` is the **positive**
    amount received; the movement factory owns the sign.
  - `Purchasing/GoodsReceipt.cs` — `sealed class GoodsReceipt : AggregateRoot` + `enum GoodsReceiptState
    { Draft, Posted, Reversed }`. `ReceiptNumber`, `PurchaseOrderId?` (null = direct receipt),
    `SupplierId`, `ReceivedUtc`, `ReceivedByUserId`, `InvoiceReference?`, `ReversesReceiptId?`, `Lines`.
    `Post(IClock)` → `Posted` + `GoodsReceiptPostedEvent`; posted is **immutable** (A-RULE 002) — every
    line mutator and `SetInvoiceReference` return `RECEIPT_LOCKED`, a correction is a new receipt with
    `ReversesReceiptId` + `ReturnOut` movements. `MarkReversed()` flags the offset original.
  - `Events/InventoryEvents.cs` — `StockMovementRecordedEvent(MovementId, ProductId, VariantId, Kind,
    QuantityValue)`, `StockBelowReorderEvent(ProductId, VariantId, OnHandValue, ReorderLevelValue)`,
    `GoodsReceiptPostedEvent(ReceiptId, SupplierId, LineCount)`, `PurchaseOrderSubmittedEvent(OrderId,
    SupplierId, GrandTotalMinor, Currency)` — all `: DomainEvent(EventId, TenantId, OccurredAtUtc)`.
- **Files modified (1):** `Results/ErrorCodes.cs` — new `#region Inventory & Purchasing (Phase A06)`
  with **11** codes (→ 46 total): `SERVICE_NO_STOCK`, `MOVEMENT_SIGN_INVALID`, `INSUFFICIENT_STOCK`,
  `UNIT_MISMATCH`, `CATEGORY_CYCLE`, `VARIANT_REQUIRED`, `PO_INVALID_TRANSITION`, `PO_LOCKED`,
  `PO_TOTAL_MISMATCH`, `RECEIPT_LOCKED`, `PRODUCT_SKU_DUPLICATE`. No existing code renamed or removed.
- **Key decisions recorded:** (1) the movement **sign rule** above is authoritative and lives only in
  `StockMovement.Create`; (2) costing is **moving weighted average**, product-wide — FIFO/LIFO are not
  implemented and require an ADR; (3) on-hand is **never stored** — `Product` has no stock field and
  `StockLevel` is a rebuildable projection; (4) `ApplyReceipt` never touches the ledger — the movement
  writer (D07/C06) does, from `GoodsReceiptPostedEvent`.
- **Contracts added:** none (`Animora.Contracts` untouched). **DI changes:** none. **Mocks deleted:**
  none. **Migrations created:** none. `Animora.Domain.csproj` still has zero references.
- **Tests added:** **none — this is a Track A phase (ROADMAP §3.10); all coverage is owed to A12**
  (`StockCalculatorTests`, `StockMovementFactoryTests` over all 10 kinds, `ProductInvariantTests`,
  `PurchaseOrderTests` — as listed in the phase file).
- **Verification:** `scripts/sandbox/build.sh full` → **0 warnings / 0 errors** (whole solution,
  12 projects incl. the 5 test projects); `scripts/sandbox/run.sh` → **SMOKE RUN OK**;
  `grep -n "OnHand" src/Animora.Domain/Inventory/Product.cs` → **no match**. `test.sh` deliberately not
  invoked (A12 owns it).
- **Deviations:** (a) added three small conveniences beyond the file table, all inside the listed files
  and consistent with the phase's rules: `StockMovement.Reverse`/`OppositeKind` (the reversal path the
  phase mandates), `StockCalculator.Project` (assembles the documented `StockLevel` from the five
  primitives) and `GoodsReceipt.MarkReversed` (flags the offset original). (b) `PurchaseOrder` also
  allows `PartiallyReceived -> PartiallyReceived` so a second partial receipt is not an invalid
  transition. (c) Scripts under `scripts/sandbox/` regained their executable bit (the sandbox checkout
  had dropped it) — no content change.

### A05 — Domain: scheduling & boarding (2026-09-23)

- **Files added (src, 12):**
  - `Scheduling/AppointmentSlot.cs` — `readonly record struct AppointmentSlot(DateTimeOffset StartUtc,
    int DurationMinutes)`; computed `EndUtc`; `bool Overlaps(other)` as a **half-open** interval
    (`StartUtc < other.EndUtc && other.StartUtc < EndUtc`) so adjacent slots (`end == start`) never overlap.
  - `Scheduling/AppointmentTypeCatalog.cs` — `readonly record struct AppointmentTypeDef(Code,
    DefaultDurationMinutes, RequiresDoctor)` + static seed of **7** types (`CHECKUP 30 true`, `VACCINE 15
    true`, `SURGERY 90 true`, `DENTAL 60 true`, `GROOMING 45 false`, `FOLLOWUP 20 true`, `CONSULT 20 true`)
    exposed as `All` with an ordinal `TryGet`.
  - `Scheduling/Appointment.cs` — `sealed class Appointment : AggregateRoot` + `enum AppointmentState
    { Scheduled, Confirmed, CheckedIn, InVisit, Completed, NoShow, Cancelled }`, `enum AppointmentSource
    { Phone, WalkIn, Online, Followup, Recall }`. `PetId`, `OwnerId`, `DoctorUserId?`,
    `AppointmentTypeCode`, `StartUtc`, `DurationMinutes`, `State`, `Source`, `Note?`, `VisitId?`,
    `CancelReason?`, `CheckedInUtc?`, `CreatedByUserId`, `RescheduleCount`; computed `EndUtc`, `Slot`.
    One `Result Transition(AppointmentState)` gate over a `static bool IsTransitionAllowed(from, to)` table
    (exposed for A12). `Book(...) → Result<Appointment>` (duration guard + `AppointmentBookedEvent`),
    `Confirm`, `CheckIn(IClock)`, `StartVisit(visitId)` (requires `CheckedIn`), `Complete`,
    `MarkNoShow(IClock)`, `Cancel(reason, IClock)` (`AppointmentCancelledEvent`), `Move(newStartUtc,
    newDurationMinutes, IClock)` — forbidden once `State >= InVisit` (`APPOINTMENT_NOT_MOVABLE`),
    increments `RescheduleCount`, raises `AppointmentMovedEvent`. Duration ∈ [5, 480] and a multiple of 5
    else `APPOINTMENT_DURATION_INVALID`. Slot-conflict is **not** enforced here (needs the calendar) —
    that is `SlotConflictPolicy`, called by the caller (ARCHITECTURE §8.5).
  - `Scheduling/SlotConflictPolicy.cs` — static **authoritative** conflict rule + `readonly record struct
    ExistingSlot(AppointmentId, DoctorUserId?, Slot, State)`. `EnsureFree(candidate, doctorUserId,
    existing) → Result` applies four ordered rules: (1) terminal slots (`Cancelled`/`NoShow`/`Completed`)
    never conflict; (2) conflict only on overlap **and** same non-null doctor; (3) a null-doctor candidate
    never conflicts (triaged at reception); (4) on conflict `Fail(APPOINTMENT_SLOT_CONFLICT,
    conflictingAppointmentId)`. Pure, no clock, `for`-loop, no LINQ, no per-candidate allocation
    (PERFORMANCE §2). Candidate list supplied by A-layer from an indexed `(TenantId, DoctorUserId,
    StartUtc)` range query (index owed to A09).
  - `Scheduling/ReceptionQueueEntry.cs` — `sealed class ReceptionQueueEntry : AggregateRoot` + `enum
    QueueState { Waiting, Called, InVisit, Done, Left }`, `enum QueueSource { Appointment, WalkIn }`.
    `PetId`, `OwnerId`, `AppointmentId?`, `DoctorUserId?`, `State`, `Source`, `TicketNumber`,
    `EnqueuedUtc`, `CalledUtc?`, `ClosedUtc?`, `Priority` (0/1/2), `Note?`, `VisitId?`. `Create(...)`
    raises `QueueEntryCreatedEvent`; `Call(doctorUserId, IClock)`, `StartVisit(visitId)`, `Close(IClock)`,
    `MarkLeft(IClock)`, `SetPriority(int)` (clamped 0–2). **Derived-queue decision** documented in XML:
    an entry exists only for a *present* patient (check-in / walk-in); future appointments never create
    queue rows (C02 relies on this). `TicketNumber` is a per-tenant, per-local-day counter set by the
    A-layer (B01+), not by the entity.
  - `Boarding/Room.cs` — `sealed class Room : AggregateRoot` + `enum RoomType { Standard, Vip, Isolation,
    Cage, Suite }`, `enum RoomStatus { Available, Occupied, Maintenance, Retired }`. `Code`,
    `DisplayName`, `Type`, `Capacity`, `Status`, `Notes?`. `Create(...) → Result<Room>` guards
    `Capacity ∈ [1, 10]` (`ROOM_CAPACITY_INVALID`); `SetCapacity`, `SetStatus` (refuses `Retired →
    Available` → `ROOM_UNAVAILABLE`), `Rename`.
  - `Boarding/BoardingServiceDef.cs` — `sealed class BoardingServiceDef : AggregateRoot` + `enum
    BoardingServiceBilling { PerStay, PerDay, PerUse }`. `Code`, `Title`, `Money UnitPrice`, `Billing`,
    `IsActive`. `Create`, `Update`, `Deactivate`/`Activate`.
  - `Boarding/StayItem.cs` — `sealed record StayItem(ItemId, StayItemKind Kind, CatalogItemId?, Title,
    Quantity, Money UnitPrice, ConsumedUtc, RecordedByUserId)` + `enum StayItemKind { BoardingService,
    ClinicService, Product, Custom }`. Part of the invoice-handoff payload Sales reads (A07).
  - `Boarding/StayRoomAssignment.cs` — `sealed record StayRoomAssignment(RoomId, FromUtc, ToUtc?,
    Reason?)`; open (`ToUtc == null`) while current.
  - `Boarding/BoardingStay.cs` — `sealed class BoardingStay : AggregateRoot` + `enum StayState
    { Reserved, Active, ReadyForInvoice, Invoiced, CheckedOut, Cancelled }`. `PetId`, `OwnerId`, `RoomId`,
    `CheckInUtc`, `PlannedCheckOutUtc`, `ActualCheckOutUtc?`, `State`, `Money DailyRate`, `CareNote?`,
    `DietNote?`, `InvoiceId?`, `IReadOnlyList<StayItem> Items`, `IReadOnlyList<StayRoomAssignment>
    RoomHistory`. One `Result Transition(StayState)` gate over `static bool IsTransitionAllowed(from, to)`
    (exposed for A12). `Reserve(...) → Result<BoardingStay>` guards `PlannedCheckOutUtc > CheckInUtc`
    (`STAY_DATES_INVALID`); `Activate(IClock)` (opens room history, `StayActivatedEvent`), `AddItem`/
    `RemoveItem` (frozen once `State >= Invoiced` → `STAY_LOCKED`), `TransferRoom(newRoomId, reason,
    IClock)` (`Active` only, closes+appends `RoomHistory`, `StayRoomTransferredEvent`),
    `RequestCheckout(IClock)` (→ `ReadyForInvoice`, `StayReadyForInvoiceEvent` carrying `BillableDays`),
    `AttachInvoice(invoiceId)`, `CompleteCheckout(IClock)` (→ `CheckedOut`, sets `ActualCheckOutUtc`,
    closes room history; **requires `InvoiceId != null`** → `STAY_INVOICE_REQUIRED`, `StayCheckedOutEvent`),
    `Cancel`, `SetNotes`. **Billable-days rule (authoritative):** `BillableDays(endUtc) = Math.Max(1,
    ceil((endUtc - CheckInUtc).TotalHours / 24))` — partial day = full day, same-day = 1 (1h→1, 24h→1,
    25h→2).
  - `Events/SchedulingEvents.cs` — `AppointmentBookedEvent`, `AppointmentMovedEvent`,
    `AppointmentCancelledEvent`, `QueueEntryCreatedEvent`, all `sealed record : DomainEvent`.
  - `Events/BoardingEvents.cs` — `StayActivatedEvent`, `StayReadyForInvoiceEvent`,
    `StayRoomTransferredEvent`, `StayCheckedOutEvent`, all `sealed record : DomainEvent`.
- **Files modified (src, 1):** `Results/ErrorCodes.cs` — new `#region Scheduling & Boarding (Phase A05)`
  **appended** (nothing reordered) with **11** codes: `APPOINTMENT_SLOT_CONFLICT` (Conflict),
  `APPOINTMENT_INVALID_TRANSITION` (Conflict), `APPOINTMENT_NOT_MOVABLE` (Conflict),
  `APPOINTMENT_DURATION_INVALID` (Validation), `QUEUE_INVALID_TRANSITION` (Conflict), `ROOM_UNAVAILABLE`
  (Conflict), `ROOM_CAPACITY_INVALID` (Validation), `STAY_INVALID_TRANSITION` (Conflict), `STAY_LOCKED`
  (Conflict), `STAY_INVOICE_REQUIRED` (Conflict), `STAY_DATES_INVALID` (Validation). Catalog total 24 → 35.
- **Contracts added:** 5 entities (`Appointment`, `ReceptionQueueEntry`, `Room`, `BoardingServiceDef`,
  `BoardingStay`), 5 value objects (`AppointmentSlot`, `ExistingSlot`, `AppointmentTypeDef`, `StayItem`,
  `StayRoomAssignment`), 9 enums (`AppointmentState`, `AppointmentSource`, `QueueState`, `QueueSource`,
  `RoomType`, `RoomStatus`, `BoardingServiceBilling`, `StayState`, `StayItemKind`), 2 statics
  (`SlotConflictPolicy`, `AppointmentTypeCatalog`), 8 domain events. **No `Animora.Contracts` change** —
  domain enums are duplicated, never imported (CONVENTIONS §2); Track C maps between them.
- **DI changes:** none. **Mocks deleted:** none. **Migrations created:** none (A09 maps these + adds the
  `(TenantId, DoctorUserId, StartUtc)` slot index the conflict policy's range query needs, CONVENTIONS §5).
- **Package/project references added:** **none** — `Animora.Domain.csproj` is still an empty
  `Microsoft.NET.Sdk` project with zero references.
- **Tests added:** **none — owed to A12** per the Track A test-deferral policy (ROADMAP §3.10). A12 must
  cover: `SlotConflictPolicyTests` (exact/partial overlap, adjacent `end==start` no conflict, different
  doctor no conflict, cancelled/no-show existing no conflict, null-doctor candidate no conflict);
  `AppointmentStateMachineTests` (all state pairs — only tabled succeed; move guards; 7-minute duration →
  `APPOINTMENT_DURATION_INVALID`); `BoardingStayTests` (transitions; billable-day boundaries 1h→1, 24h→1,
  25h→2; `CompleteCheckout` without invoice → `STAY_INVOICE_REQUIRED`; transfer-history append);
  `ReceptionQueueTests` (transitions, priority, derived-entry rule).
- **Verification:** `Animora.Domain` builds **0 warnings / 0 errors** (standalone and inside the solution,
  along with Application, Infrastructure.Local, Persistence, Client.Services and all 5 test projects). The
  only solution-wide build failure is `AVLN9999 System.OutOfMemoryException` in the **Avalonia Desktop**
  XAML AOT compiler — a sandbox RAM-cap limitation (workstation GC, ~448 MiB `GCHeapHardLimit` per
  `_env.sh`), unrelated to A05 (Domain-only, UI untouched). `test.sh` not run (A12 owns it).
- **Deviations:** none material — additions inside phase scope needed to make the aggregates usable
  without re-deriving rules later: `Book`/`Reserve`/`Create` factories (private setters + `IClock`);
  `Appointment.Slot`/`EndUtc` and `IsTransitionAllowed`, `ReceptionQueueEntry.VisitId`/`SetPriority`
  clamp, `Room.SetCapacity`/`Rename`, `BoardingServiceDef.Update`/`Activate`/`Deactivate`,
  `BoardingStay.SetNotes`/`IsTransitionAllowed`/`AreItemsLocked`. `AppointmentTypeCatalog.TryGet` added so
  the seed is reachable. Some declared error codes (`ROOM_UNAVAILABLE` for retired-room reuse) are
  enforced structurally and by the A-layer as documented.

### A04 — Domain: owners, patients, clinical (2026-09-23)

- **Files added (src, 12):**
  - `Patients/Owner.cs` — `sealed class Owner : AggregateRoot` + `enum OwnerGender { Unspecified, Male,
    Female }`. `FullName`, `PhonePrimary`, `PhoneSecondary?`, `NationalId?`, `Email?`, `Address?`,
    `City?`, `Gender`, `Notes?`, `RegisteredUtc`, `IsBlocked`, `BlockReason?`. `Register(...)`,
    `UpdateContactDetails(...)` (one funnel so the §8.5 explicit demographic merge has a single write
    path), `Block` (idempotent — keeps the first reason) / `Unblock`. `static string NormalizePhone`
    keeps digits only with the leading `0`, folding Persian (U+06F0–U+06F9) and Arabic-Indic digits to
    ASCII so the same number typed on either keyboard compares equal.
  - `Patients/Pet.cs` — `sealed class Pet : AggregateRoot` + `enum PetSpecies { Dog, Cat, Bird, Rodent,
    Reptile, Other }`, `enum PetGender { Unknown, Male, Female }`, `enum PetStatus { Active, Inactive,
    Deceased }`. `OwnerId`, `Name`, `Species` (**private setter, no mutator, no method anywhere takes a
    `PetSpecies`** — a species change is a new record), `Breed?`, `Gender`, `BirthDateUtc?`,
    `IsNeutered`, `Color?`, `MicrochipCode?`, `MicrochipImplantUtc?`, `Status`, `AllergyNotes?`,
    `ChronicNotes?`, `RegisteredUtc`. `AgeMonthsAt(nowUtc)` computed (never stored), clamped at 0 for a
    future birth date and `null` when unknown. `Register` raises `PetRegisteredEvent`;
    `UpdateProfile`, `SetMicrochip`, `SetClinicalFlags`, `SetStatus`.
  - `Patients/WeightEntry.cs` — `sealed class WeightEntry : SyncableEntity`, append-only (zero public
    methods). `PetId`, `WeightKg`, `MeasuredUtc`, `Note?`, `RecordedByUserId`, `CorrectsEntryId?`.
    `Record(...) → Result<WeightEntry>`; `MinWeightKg = 0` (exclusive) / `MaxWeightKg = 300` →
    `WEIGHT_OUT_OF_RANGE`.
  - `Patients/DocumentReference.cs` — `sealed class DocumentReference : SyncableEntity` + `enum
    DocumentKind { Lab, Imaging, Prescription, Consent, Invoice, Other }`. `OwnerId?`/`PetId?`/`VisitId?`
    (at least one required → `DOCUMENT_TARGET_REQUIRED`), `Title`, `Kind`, `FileId` (D12 metadata id —
    **never a filesystem path**, A-RULE 020), `SizeBytes`, `ContentHashSha256`, `UploadedUtc`,
    `UploadedByUserId`. `Attach(...) → Result<DocumentReference>`.
  - `Clinical/PetVitals.cs` — `sealed record PetVitals(WeightKg?, TemperatureC?, HeartRateBpm?,
    RespiratoryRate?, MucousMembrane?, Hydration?)`; every component optional so a missing measurement
    is never read as zero.
  - `Clinical/PrescriptionLine.cs` — `sealed record PrescriptionLine(LineId, ProductId?, DrugName,
    Dosage, Frequency, DurationDays, Instructions?)`; `ProductId` links to A06 inventory only when the
    drug is dispensed from stock.
  - `Clinical/VisitServiceLine.cs` — `sealed record VisitServiceLine(LineId, CatalogItemId?, Title,
    Quantity, Money UnitPrice, VisitLineSource Source)` + `enum VisitLineSource { Service, Product,
    Custom }`. The **invoice-handoff payload** Sales reads (A07/C07); the visit never computes totals.
  - `Clinical/Visit.cs` — `sealed class Visit : AggregateRoot` + `enum VisitState { Draft, InProgress,
    Completed, ReadyForInvoice, Invoiced, Cancelled }`, `enum VisitReason { Checkup, Illness, Injury,
    Vaccination, Surgery, Dental, Followup, Other }`. Private `List<T>` collections exposed as
    `IReadOnlyList<PrescriptionLine>` / `IReadOnlyList<VisitServiceLine>`. One `Result
    Transition(VisitState)` gate over a `static bool IsTransitionAllowed(from, to)` table (exposed so
    the tests can enumerate all 36 pairs). `Open`, `Start`, `SetVitals`, `SetAssessment`,
    `AddPrescription`/`RemovePrescription`, `AddServiceLine`/`RemoveServiceLine`, `Complete(IClock)`,
    `MarkReadyForInvoice()`, `AttachInvoice(invoiceId)`, `Cancel(reason, IClock)` — all returning
    `Result`. `Complete` requires a non-blank diagnosis (`VISIT_DIAGNOSIS_REQUIRED`) and raises
    `VisitCompletedEvent`; `MarkReadyForInvoice` raises `VisitReadyForInvoiceEvent`; `AttachInvoice` is
    legal only from `ReadyForInvoice`; lines freeze at `State >= Invoiced` (`AreLinesLocked` →
    `VISIT_LOCKED`); clinical fields close after `InProgress`.
  - `Clinical/VaccineSchedule.cs` — static `IReadOnlyDictionary<string, int[]> All` (ordinal-ignore-case):
    `DHPP [21,21,365]`, `RABIES [365]`, `FVRCP [21,21,365]`, `FELV [21,365]`, `LEPTO [21,365]`,
    `BORDETELLA [365]`. `IntervalFor(code, dose)` clamps past the series end and returns
    `AnnualBoosterDays = 365` for an unknown code (never "no due date"); `TotalDosesFor(code)`.
  - `Clinical/VaccinationRecord.cs` — `sealed class VaccinationRecord : AggregateRoot` + `enum
    VaccinationState { Administered, DueSoon, Overdue, Skipped }`. `Administer(...) →
    Result<VaccinationRecord>` derives `NextDueUtc = AdministeredUtc + IntervalFor(code, dose)`,
    uppercases the code, defaults `TotalDoses` from the schedule, validates the dose
    (`VACCINE_DOSE_INVALID`) and raises `VaccinationAdministeredEvent`. `StateAt(nowUtc)` is the derived
    read (`DueSoonWindow = 30 d`; `Overdue` whew = 30 d`; `Overdue` when `NextDueUtc < nowUtc`); the stored `State` only
    separates a live series from a `Skip(...)`-ed one.
  - `Clinical/MedicalHistoryEntry.cs` — `sealed class MedicalHistoryEntry : SyncableEntity` + `enum
    MedicalHistoryKind { Visit, Vaccination, Surgery, Lab, Weight, Prescription, Boarding, Note }`.
    Public constructor only — **zero methods, zero public setters** (ARCHITECTURE §8.5: history is
    preserved in full; a correction is a new row). `PetId`, `Kind`, `Title`, `Detail?`, `OccurredUtc`,
    `SourceEntityId?`, `SourceEntityType?`, `RecordedByUserId`.
  - `Events/ClinicalEvents.cs` — `PetRegisteredEvent`, `VisitCompletedEvent`,
    `VisitReadyForInvoiceEvent`, `VaccinationAdministeredEvent`, all `sealed record : DomainEvent`
    (EventId/TenantId/OccurredAtUtc + payload). Dispatch to `MedicalHistoryEntry` stays in A10.
- **Files modified (src, 1):** `Results/ErrorCodes.cs` — new `#region Patients & Clinical (Phase A04)`
  **appended** (nothing reordered) with 7 codes: `PET_SPECIES_IMMUTABLE` (Integrity),
  `VISIT_INVALID_TRANSITION` (Conflict), `VISIT_DIAGNOSIS_REQUIRED` (Validation), `VISIT_LOCKED`
  (Conflict), `WEIGHT_OUT_OF_RANGE` (Validation), `DOCUMENT_TARGET_REQUIRED` (Validation),
  `VACCINE_DOSE_INVALID` (Validation). Catalog total 17 → 24.
- **Contracts added:** 7 entities (`Owner`, `Pet`, `WeightEntry`, `DocumentReference`, `Visit`,
  `VaccinationRecord`, `MedicalHistoryEntry`), 3 value objects (`PetVitals`, `PrescriptionLine`,
  `VisitServiceLine`), 10 enums (`OwnerGender`, `PetSpecies`, `PetGender`, `PetStatus`, `DocumentKind`,
  `VisitState`, `VisitReason`, `VisitLineSource`, `VaccinationState`, `MedicalHistoryKind`), 1 static
  (`VaccineSchedule`), 4 domain events. **No `Animora.Contracts` change** — domain enums are duplicated,
  never imported (CONVENTIONS §2); Track C maps between them.
- **DI changes:** none. **Mocks deleted:** none. **Migrations created:** none (A09 maps these).
- **Package/project references added:** **none** — `Animora.Domain.csproj` is still an empty
  `Microsoft.NET.Sdk` project with zero references; `LayeringRulesTests` still green.
- **Tests added (4 classes, 45 facts; `Animora.Domain.Tests` 45 → 90):**
  - `VisitStateMachineTests` (7) — all **36** `VisitState` pairs checked against a literal allowed-set
    (only the 6 tabled ones pass); the full Draft→Invoiced happy path with both events in order; cancel
    legal from `Draft` and `InProgress`; both terminal states refuse every further transition;
    `AttachInvoice` before the handoff is refused. Hosts the shared advancing `TestClock : IClock`.
  - `VisitInvariantTests` (4) — `Complete` without (and with a whitespace-only) diagnosis returns
    `VISIT_DIAGNOSIS_REQUIRED` and raises nothing; after `Invoiced`, add/remove of service and
    prescription lines all return `VISIT_LOCKED`; lines stay editable through `ReadyForInvoice` and a
    removal of a missing line is `NOT_FOUND`; vitals/assessment are closed once the encounter ends.
  - `VaccinationDueDateTests` (18 cases) — the per-dose interval and resulting `NextDueUtc` for all six
    seeded codes; clamp past the series end and unknown code → 365 d; the event carries the derived due
    date; `StateAt` boundaries exact to ±1 second (`due+1s` Overdue, `due` and `due−30 d` DueSoon,
    `due−30 d−1 s` Administered); `Skip` clears the due date permanently; invalid dose numbers.
  - `PatientInvariantTests` (16 cases) — species has no public setter and no method accepting a
    `PetSpecies` (reflection) and registration raises `PetRegisteredEvent`; `AgeMonthsAt` boundaries and
    the null case; weight range rejections and the append-only shape; document target requirement plus
    all three single-target forms; `NormalizePhone` over spaced/`+`-prefixed/Persian-digit/parenthesised
    input; owner trim + idempotent block/unblock; `MedicalHistoryEntry` asserted by reflection to have
    **no declared public method and no public setter**.
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**;
  `scripts/sandbox/test.sh` = **TESTS OK** (Domain 90/90, other 4 projects 1/1 each);
  `scripts/sandbox/run.sh` = **SMOKE RUN OK**.
- **Deviations:** none material. Additions inside phase scope, needed to make the aggregates usable
  without re-deriving rules later: factories (`Owner.Register`, `Pet.Register`, `Visit.Open`) because
  every aggregate has private setters and an `IClock`-supplied instant; `Visit.SetAssessment`
  (diagnosis/plan/notes have to be writable for `Complete`'s invariant to be reachable),
  `Visit.IsTransitionAllowed`/`AreLinesLocked`/`CancellationReason`; `Owner.UpdateContactDetails`/
  `Block`/`Unblock`; `Pet.UpdateProfile`/`SetMicrochip`/`SetClinicalFlags`/`SetStatus`;
  `VaccineSchedule.TotalDosesFor`; `VaccinationRecord.Skip` (the `Skipped` enum member is otherwise
  unreachable). `PET_SPECIES_IMMUTABLE` is declared but not thrown by the domain: immutability is
  enforced structurally (no mutator exists), so the code is reserved for the A-layer command that
  rejects a species-change request — asserted by reflection instead of by a runtime guard.

### A03 — Domain: identity, tenant, staff, device (2026-09-23)

- **Files added (src, 11 — all in `src/Animora.Domain/Identity/`; the A02 `.gitkeep` placeholder removed):**
  - `Tenant.cs` — `sealed class Tenant : AggregateRoot` + `enum TenantStatus { Active, Suspended, Closed }`.
    `DisplayName`, `LegalName?`, `Timezone` (IANA, default `Asia/Tehran` via `DefaultTimezone` const),
    `DefaultCurrency` (default `CurrencyScale.Irt` = `"IRT"`), `Status`. `Create(...)` sets
    `TenantId == Id` so the tenant row is covered by the same uniform tenant filter; `SetStatus`, `Rename`.
  - `User.cs` — `sealed class User : AggregateRoot` + `enum UserStatus { Active, Disabled, Locked }`.
    `UserName` (lowercase invariant, unique per tenant), `DisplayName`, `PasswordHash?`, `SecurityStamp`
    (ULID), `Status`, `LastLoginUtc?`, `FailedAttemptCount`, `LockoutEndUtc?`. Policy constants
    `MaxFailedAttempts = 5`, `LockoutDuration = 15 min`, `DefaultFailureWindow = 15 min`. Methods
    `RegisterFailedAttempt(IClock, threshold, window)` (returns whether it triggered the lockout; a streak
    older than the window is discarded), `ClearFailures()` (never re-activates a `Disabled` account),
    `IsLockedOut(nowUtc)` (fail-closed when `LockoutEndUtc` is null), `RegisterSuccessfulLogin(IClock)`,
    `RotateSecurityStamp()`, `SetPasswordHash(string)` (rotates the stamp), `Disable()`, `Enable()`.
  - `StaffRole.cs` — `enum StaffRole { Manager, Doctor, Receptionist, Cashier, Accountant }`.
    Mapping to `Animora.Contracts.Navigation.UserRole` deferred to C10 (Contracts untouched).
  - `Permission.cs` — `sealed class Permission` (`Code` PK, `Category`, `RiskLevel Risk`) +
    `enum RiskLevel { Low, Normal, High, Critical }`; `Create(code, risk, category?)`, `CategoryOf(code)`
    (first dotted segment).
  - `PermissionCodes.cs` — **42 seed codes** as `const string` members in 5 regions (patients/reception,
    clinical, appointments/boarding, inventory/purchasing, sales/payments, finance/reporting,
    administration), exactly the strings listed in the phase file. `All` (ordered `IReadOnlyList<string>`),
    `Exists(code)`, `RiskOf(code)` (Critical set of 7 → High set of 6 → `*.read` = Low → Normal),
    `CreateCatalog()`.
  - `RoleDefinition.cs` — `sealed class RoleDefinition : AggregateRoot`: `Role`, `DisplayKey`
    (`role.<lowercase>`), `IsSystem`, `PermissionCodes` (`IReadOnlySet<string>` over a private ordinal
    `HashSet`). `CreateSystem(tenantId, role, IClock)` seeds from `DefaultRolePermissions`;
    `Grant`/`Revoke` throw `InvalidOperationException` for the system `Manager` definition and
    `ArgumentException` for an undeclared code; `Has(code)`.
  - `DefaultRolePermissions.cs` — the role→permission matrix exactly as tabled, built **only** from
    `PermissionCodes` constants (`using P = ...` alias) so a rename is a compile error. Manager = all 42;
    Doctor 16; Receptionist 14; Cashier 12; Accountant 14. `All`, `For(role)` (empty set = fail closed).
  - `StaffMembership.cs` — `sealed class StaffMembership : AggregateRoot` + `enum MembershipStatus
    { Active, Suspended, Ended }`: `UserId`, `Role`, `Status`, `JoinedUtc`, `EndedUtc?`,
    `ExtraPermissionCodes`, `DeniedPermissionCodes`. `Create`, `IsActive`, `ChangeRole`, `Suspend`,
    `Reinstate` (an ended membership is never reopened), `End(IClock)` (idempotent, keeps the first
    instant), `GrantExtra`/`Deny` (mutually exclusive — deny wins), `ClearOverride`. Documented as the
    **only** `(User, Tenant)` link; `TenantId` is resolved from it server-side (A-RULE 012).
  - `Device.cs` — `sealed class Device : AggregateRoot` + `enum DeviceStatus { Pending, Active, Revoked }`:
    `UserId`, `DeviceName`, `InstallationIdHash` (SHA-256 hex; the identity itself never leaves the device),
    `Platform`, `Status`, `RegisteredUtc`, `LastContactUtc?`, `RevokedUtc?`, `RevocationReason?`.
    `Register(...)` → `Pending`; `Approve()` (throws for a revoked device), `IsUsable`,
    `RecordContact(IClock)`, `Revoke(reason, IClock)` **idempotent** — a second call keeps the first
    instant and reason so the audit trail stays truthful.
  - `CapabilitySnapshot.cs` — `sealed record CapabilitySnapshot(TenantId, UserId, DeviceId, Role,
    CapabilityExpiry, IssuedUtc, SnapshotExpiryUtc, SecurityStamp, Signature)`. `Allows(code, nowUtc)`
    **fails closed**: blank/unknown code → false, snapshot expired → false, capability expiry `<= nowUtc`
    → false (so a zero window never grants). `IsExpired(nowUtc)`. Helper
    `BuildExpiry(codes, issuedUtc)` applies `OfflineCapabilityPolicy` per code — signing/verification
    stays server-side (D03/E07); `Signature` is an opaque base64 string here.
  - `OfflineCapabilityPolicy.cs` — the authoritative windows: `Low = 30 d`, `Normal = 14 d`,
    `High = 72 h`, `Critical = 0`; `WindowFor(RiskLevel)` (unknown level → `TimeSpan.Zero`),
    `WindowForCode(code)`. Documented as ADR-gated values.
- **Files modified (src, 1):** `Results/ErrorCodes.cs` — new `#region Identity (Phase A03)` **appended**
  (no existing entry reordered) with 6 codes, all category `Forbidden`: `TENANT_SUSPENDED`,
  `USER_DISABLED`, `USER_LOCKED_OUT`, `MEMBERSHIP_NOT_ACTIVE`, `DEVICE_REVOKED`,
  `CAPABILITY_NOT_GRANTED`. Catalog total 11 → 17.
- **Contracts added:** 6 entities (`Tenant`, `User`, `Permission`, `RoleDefinition`, `StaffMembership`,
  `Device`), 6 enums (`TenantStatus`, `UserStatus`, `StaffRole`, `RiskLevel`, `MembershipStatus`,
  `DeviceStatus`), 1 value object (`CapabilitySnapshot`), 3 statics (`PermissionCodes`,
  `DefaultRolePermissions`, `OfflineCapabilityPolicy`). **No `Animora.Contracts` change**, no wire DTO,
  no EF configuration, no persistence.
- **DI changes:** none. **Mocks deleted:** none. **Migrations created:** none.
- **Package/project references added:** **none** — `Animora.Domain.csproj` is still an empty
  `Microsoft.NET.Sdk` project with zero `PackageReference`/`ProjectReference` (CONVENTIONS §2);
  `LayeringRulesTests` still green.
- **Tests added (3 classes, 21 facts; `Animora.Domain.Tests` 24 → 45):**
  - `UserLockoutTests` (5) — the 5th failure sets `Locked` + `LockoutEndUtc = now + 15 min`; attempts
    outside the window restart the streak; a successful login clears status/count/end and stamps
    `LastLoginUtc`; `ClearFailures` does not revive a `Disabled` account; `SetPasswordHash` normalizes
    the user name and rotates the stamp. Uses a local advancing `TestClock : IClock` — no wall clock.
  - `RolePermissionMatrixTests` (8 methods / 10 cases) — every string in every role's set resolves to a declared code;
    the catalog has no duplicates and `CreateCatalog` risk-matches `RiskOf`; the risk table is asserted
    literally (7 Critical, 6 High, every `*.read` Low, `patients.write` Normal); Manager == `All`;
    Doctor/Receptionist/Cashier hold **no** Critical code; Accountant's Critical subset is exactly
    `{finance.period.close, payments.reverse}`; the system Manager definition rejects `Grant`/`Revoke`
    and a non-Manager one accepts idempotent grant/revoke but rejects an unknown code; membership carries
    the only `(User, Tenant)` link, deny beats extra, and `End` is idempotent.
  - `CapabilitySnapshotTests` (6) — an expired snapshot denies even a `Low` read; an unknown/ungranted/
    blank code is denied; a `Critical` code is denied at `IssuedUtc` because its window is zero;
    a `High` code is allowed at +71 h and denied at +73 h while the snapshot itself is still valid
    (capability expiry enforced independently); the four offline windows are the published numbers;
    device revocation is idempotent and a revoked device cannot be re-approved.
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**;
  `scripts/sandbox/test.sh` = **TESTS OK** (Domain 45/45, other 4 projects 1/1 each);
  `scripts/sandbox/run.sh` = **SMOKE RUN OK**.
- **Deviations:** none material. Additions beyond the literal file list, all inside phase scope and
  needed to keep the types usable without re-deriving rules later: factory methods (`Create`/`Register`/
  `CreateSystem`) because every aggregate has private setters and an `IClock`-supplied instant;
  `User.IsLockedOut`/`RegisterSuccessfulLogin`/`Enable`/`Disable`; `Permission.CategoryOf`;
  `PermissionCodes.Exists`/`RiskOf`/`CreateCatalog`; `DefaultRolePermissions.For`;
  `StaffMembership` override/lifecycle helpers; `Device.Approve`/`RecordContact`/`IsUsable`;
  `CapabilitySnapshot.BuildExpiry`; `OfflineCapabilityPolicy.WindowForCode`. `RoleDefinition.Grant`
  additionally rejects undeclared codes (fail-closed, CONVENTIONS §7). No `IPermissionEvaluator`,
  no hashing, no signing, no EF mapping — those stay in B03/B04/D03/A09 as scoped.

### A02 — Domain kernel (2026-09-23)

- **Files added (src, 15 — all in `src/Animora.Domain/`, all new folders):**
  - `Primitives/SyncableEntity.cs` — syncable shape base: `Id` (init, ULID), `TenantId`,
    `UpdatedAt`, `Version` (nullable, server-issued), `Deleted`; protected EF ctor; **no EF attribute**.
    XML docs state `UpdatedAt`/`Version` are transport bookkeeping only (A-RULE 009/010).
  - `Primitives/LocalOnlyEntity.cs` — `Id` + `CreatedAtUtc`; never replicated.
  - `Primitives/LocalOnlyAttribute.cs` — `[LocalOnly]` class marker (consumed by A09 model builder, E03 scope).
  - `Primitives/IHasDomainEvents.cs` — `IReadOnlyList<DomainEvent> DomainEvents` + `ClearDomainEvents()`.
  - `Primitives/AggregateRoot.cs` — `SyncableEntity` + private `List<DomainEvent>` + `protected Raise(...)`;
    buffer exposed read-only.
  - `Primitives/IdGenerator.cs` — monotonic 26-char Crockford base32 ULID (48-bit ms + 80-bit
    `RandomNumberGenerator` randomness), lock-guarded same-ms increment; `NewId()` additionally clamps a
    backwards OS-clock jump to the last emitted ms so process-wide ordering holds; `NewId(DateTimeOffset)`
    honours the caller's instant verbatim (tests).
  - `Events/DomainEvent.cs` — `abstract record DomainEvent(string EventId, string TenantId,
    DateTimeOffset OccurredAtUtc)`; no `INotification`, no MediatR.
  - `ValueObjects/Money.cs` — `readonly record struct Money(long Minor, string Currency)`: `ZeroIrt`,
    `Irt(long)`, `Of(long,string)` (3-letter uppercase validation), `Zero(string)`, `Scale`, `IsZero`,
    `IsNegative`, `Abs`; `+ - unary- *` (int/long, both operand orders) with `checked` arithmetic and named
    alternatives (`Add`/`Subtract`/`Negate`/`Multiply`); `IComparable<Money>`/`IComparable` + `< <= > >=`;
    currency mismatch throws `InvalidOperationException` (defect, not control flow);
    `Allocate(int)` largest-remainder (sign-aware, conserves every minor unit);
    `ApplyPercent(decimal)` with `MidpointRounding.AwayFromZero` — the product's single rounding site.
  - `ValueObjects/CurrencyScale.cs` — `Of(currency)`: `IRT` → 0, `USD`/`EUR` → 2, default 2; `Irt` const.
  - `ValueObjects/Quantity.cs` — `readonly record struct Quantity(decimal Value, string UnitCode)`:
    `Zero(unit)`, `IsZero`, `IsNegative`, `Abs`, `+ - unary-` with ordinal unit-code check (throws on
    mismatch). Negative values intentionally legal (movements are signed).
  - `Time/IClock.cs` — `DateTimeOffset UtcNow`. `Time/IMonotonicClock.cs` — `ElapsedTicks`,
    `HighWaterUtc` (never decreases), `Observe(DateTimeOffset serverUtc)`; A-RULE 011 documented.
    Implementations deferred to A11 as specified.
  - `Results/ErrorCode.cs` — `readonly record struct ErrorCode(string Code, string Category)` + the 7
    category consts (`Validation`, `NotFound`, `Conflict`, `Forbidden`, `Integrity`, `Infrastructure`,
    `Entitlement`) + `None`/`IsNone`.
  - `Results/ErrorCodes.cs` — central catalog seeded with the 11 cross-cutting codes:
    `VALIDATION_FAILED`, `NOT_FOUND`, `CONCURRENCY_CONFLICT`, `PERMISSION_DENIED`, `TENANT_MISMATCH`,
    `IDEMPOTENCY_FINGERPRINT_MISMATCH`, `DEPENDENCY_UNSETTLED`, `ENTITLEMENT_REQUIRED`,
    `OFFLINE_CAPABILITY_EXPIRED`, `STORE_UNAVAILABLE`, `UNEXPECTED`. Append-only by contract.
  - `Results/Result.cs` — `Result` and `Result<T>` (`IsSuccess`/`IsFailure`/`Value`/`Error`/`Detail`),
    `Ok`/`Fail` factories, implicit `T → Result<T>` (+ named `FromValue`), `Map`, `Bind` (all arities),
    `ToResult`, `OrElse`. `Detail` documented as a developer string, never localized text.
  - `Identity/` created empty (`.gitkeep`) — filled by A03.
- **Files modified (src, 1):** `src/Animora.Domain/README.md` — folder-layout table + the zero-dependency,
  no-Guid, no-system-clock, no-decimal-money, append-only-ErrorCodes rules.
- **Contracts added:** domain primitives + value objects + clock ports + results listed above.
  No `Animora.Contracts` change, no `Animora.Contracts.Wire` change, no DbContext, no wire DTO.
- **DI changes:** none (Domain has no composition root; clock implementations and registration are A11).
- **Mocks deleted:** none (Track C protocol not yet in effect).
- **Migrations created:** none.
- **Package/project references added:** **none** — `Animora.Domain.csproj` remains an empty
  `Microsoft.NET.Sdk` project with zero `PackageReference` and zero `ProjectReference` (CONVENTIONS §2).
- **Tests added (4 classes, 24 facts total in `Animora.Domain.Tests`):**
  - `MoneyTests` — exact minor-unit arithmetic/comparison; currency mismatch throws on `+`/`-`/`CompareTo`
    and bad-code `Of` throws; `Allocate` conserves the sum and keeps shares within one unit (incl. a
    negative-amount case); `Allocate(3)` of 100 IRT = `34/33/33`; `ApplyPercent` away-from-zero
    (`105 × 50% → 53`, `-105 × 50% → -53`).
  - `QuantityTests` — add/sub keep unit and allow negatives; unit mismatch throws.
  - `IdGeneratorTests` — 26 chars, Crockford alphabet only, cross-ms sortability, same-ms strict
    monotonicity with identical timestamp prefix, and the 10 000-id loop asserting zero duplicates and
    strictly ascending ordinal order.
  - `ResultTests` — Ok/Fail state + code/category/detail, implicit conversion + `FromValue`/`OrElse`,
    `Map` transform/propagate, `Bind` chain/short-circuit, `ToResult`.
  - `LayeringRulesTests` unchanged and still green (Domain still references no `Animora.*` assembly).
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**;
  `scripts/sandbox/test.sh` = `TESTS OK` (Domain 24 passed, other 4 projects 1 placeholder each);
  `scripts/sandbox/run.sh` = `SMOKE RUN OK` (desktop untouched);
  `grep -rn "DateTime.Now\|DateTimeOffset.UtcNow\|Guid.NewGuid" src/Animora.Domain` returns nothing.
- **Deviations:**
  - **D-A02-1** — The phase file placed the category consts in `Results/ErrorCode.cs` without naming a
    holder type. They are declared as consts **on `ErrorCode` itself** (`ErrorCode.Validation`, …) rather
    than in a second public type, to keep one public type per file (CONVENTIONS §3). No file count change.
  - **D-A02-2** — `IdGenerator.NewId()` obtains the instant from `TimeProvider.System`; `IClock` cannot be
    injected into a static generator and lives in the same assembly. This is an id prefix only, never a
    business sequence or licensing authority (A-RULE 011). The forbidden-call grep stays clean.
  - **D-A02-3** — Added conveniences beyond the phase list, all additive and non-breaking: `Money.Of`,
    `Money.Zero`, `Abs`, `IsZero`/`IsNegative`, named operator alternatives (CA2225 compliance),
    `Quantity.Abs`/`Negate`, `Result<T>.FromValue`/`ToResult`/`OrElse`.
  - **D-A02-4** — `src/Animora.Domain/Identity/` is tracked via a `.gitkeep` (git cannot store an empty
    directory); A03 removes it when it adds the first identity entity.

### A01 — Backend track scaffold (2026-09-23)

- **Files added (src, 4 new projects):**
  - `src/Animora.Contracts.Wire/` — `Animora.Contracts.Wire.csproj` (no refs) + `WireContract.cs`.
  - `src/Animora.Persistence.Sqlite/` — csproj (refs Domain + Application; packages EF Core Sqlite/Design,
    SQLitePCLRaw.bundle_e_sqlite3, SourceGear.sqlite3) + `README.md` (filled by A09).
  - `src/Animora.Infrastructure.Local/` — csproj (refs Domain + Application; packages Logging.Abstractions,
    Options) + `README.md` (owning phases A02 clocks, A11 keystore/encryption, B05 logging).
  - `src/Animora.Client.Services/` — csproj (refs Application, Contracts, Persistence.Sqlite,
    Infrastructure.Local) + `README.md` (Track C DI swap target).
  - Anchor types `src/Animora.Domain/AssemblyMarker.cs` and `src/Animora.Application/AssemblyMarker.cs`
    (see deviation D-A01-1) so the layering test can load empty placeholder assemblies deterministically.
- **Files modified (src):**
  - `src/Animora.Application/Animora.Application.csproj` — now refs Domain + Contracts + Contracts.Wire,
    and packages Logging.Abstractions + Options.
  - `src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj` — now refs Domain + Contracts.Wire.
- **Files added (tests, 5 new projects):** `tests/Animora.Domain.Tests` (xunit; refs Domain + Application +
  Contracts so LayeringRulesTests can load them), `tests/Animora.Application.Tests`,
  `tests/Animora.Persistence.Tests`, `tests/Animora.Sync.Tests`, `tests/Animora.Server.Tests` (xunit only,
  no subject ref until D01). Each has a placeholder test; Server.Tests keeps only the placeholder.
- **Build/infra files modified:** `Directory.Packages.props` (10 backend pins added), `Directory.Build.props`
  (`NuGetAuditMode=all`, `EnableNETAnalyzers=true`, `TreatWarningsAsErrors=false`), `Animora.sln`
  (4 src projects under `src` folder + 5 test projects under a new `tests` folder), `.gitignore`
  (`**/TestResults/`), `scripts/sandbox/test.sh` added (env + `dotnet test Animora.sln`, prints `TESTS OK`,
  optional `<project-name-filter>` arg).
- **Package pins added (exact, no ranges — T-RULE 010):** `Microsoft.EntityFrameworkCore 10.0.12`,
  `Microsoft.EntityFrameworkCore.Sqlite 10.0.12`, `Microsoft.EntityFrameworkCore.Design 10.0.12`,
  `Microsoft.Extensions.Logging.Abstractions 10.0.12`, `Microsoft.Extensions.Options 10.0.12`,
  `SQLitePCLRaw.bundle_e_sqlite3 3.0.3`, `SourceGear.sqlite3 3.50.4.5`, `xunit 2.9.2`,
  `xunit.runner.visualstudio 2.8.2`, `Microsoft.NET.Test.Sdk 17.12.0`. No Datasync / SQL Server /
  ASP.NET Core / encryption-bundle pins (owned by later phases).
- **Contracts added:** `Animora.Contracts.Wire.WireContract` (`Version = 1`, `HeaderName =
  "X-Animora-Contract-Version"`). No `Animora.Contracts` change, no entity, no DbContext.
- **DI changes:** none. `Animora.Desktop` still references only its existing projects and runs entirely on
  mocks; it does NOT reference `Animora.Client.Services` (that is C01).
- **Mocks deleted:** none.
- **Migrations created:** none.
- **Tests added:** `LayeringRulesTests` (3 facts: Domain has no forbidden refs; Application has no EF/
  Datasync refs; Contracts references no `Animora.*`) + 4 placeholder tests (Application/Persistence/Sync/
  Server). Domain.Tests runs 3 facts, total 7 tests across the suite.
- **Verification:** `scripts/sandbox/build.sh full --clean-restore` = 0 warnings / 0 errors (14 projects);
  `scripts/sandbox/test.sh` = `TESTS OK`, all 5 test projects discovered and green;
  `scripts/sandbox/run.sh` = `SMOKE RUN OK`; `dotnet sln list` = 9 src + 5 test projects; layering test
  confirmed RED when an EF Core reference was temporarily added to `Animora.Domain`, then reverted;
  `grep -r "CommunityToolkit.Datasync\|EntityFrameworkCore" src/Animora.Domain src/Animora.Application`
  returns nothing in source (`.cs`/`.csproj`).
- **Deviations:** see D-A01-1 below (anchor `AssemblyMarker` types added to Domain/Application).

### B05a — Append-only audit plane and integrity sink (2026-09-27)

- Added `[LocalOnly]` `AuditEntry`, explicit mapping with four required indexes, append-only protection, and migration `007_AuditDiagnostics`; migration 007 contains `AuditEntries` only.
- Added `IAuditWriter`/`AuditWriter`, registration-time `AuditableAttribute` resolution into `AuditBinding`, and `AuditBehavior` immediately after `CommandJournalBehavior` inside `TransactionBehavior`.
- Applied audit metadata to `SignInCommand`, `ChangePasswordCommand`, and `CompleteFirstRunSetupCommand`; added `AUDIT_WRITE_FAILED`.
- Added async `IntegrityAlertSink` and connected the B02 fingerprint-mismatch path to its durable integrity record plus a `NeedsAttention` notification.
- Registered a temporary `TryAdd` no-op `IUserNotificationPort`; B05c replaces it with the real adapter.
- **Verification:** focused Persistence project build completed with 0 warnings / 0 errors through migration generation; EF migration list shows `007_AuditDiagnostics`; acceptance greps passed. Per the user's locked instruction, no full build or long test suite was run.
- **Deliberate split:** `007_AuditDiagnostics` contains only `AuditEntries`; `UiDraftStates` is deferred to migration `008` in B05c.

### B05b — Logging, correlation propagation & diagnostics bundle (2026-09-27)

- Pinned `Serilog` 4.2.0, `Serilog.Extensions.Logging` 9.0.0, and `Serilog.Sinks.File` 6.0.0 centrally; only `Animora.Infrastructure.Local` references them.
- Added `LocalLoggingOptions`, local-only buffered daily `LoggingSetup` (14 files, 25 MiB roll, two-second flush, EF warning floor), and recursive `LogRedaction`; no console or network sink exists.
- Extended correlation additively with `AmbientCorrelation`, `ICorrelationContext.Current`, and a full-context `Begin` overload while retaining `CurrentId`/`Begin(string?)`; added `UiCorrelationScope` for per-gesture tenant/user/device/operation propagation. Log events receive all five correlation fields.
- Added `IDiagnosticsBundleBuilder`/`DiagnosticsBundleBuilder` and DI registration. The archive contains `environment.json`, `schema.json`, `pipeline.json` (summary + newest 50 unsettled, no payloads), `audit-tail.json` (500 newest, bounded detail), `logs/` (newest three), and `redaction.txt`; owner/pet personal fields are never queried.
- Added `DIAGNOSTICS_BUNDLE_FAILED`; no migration, tests, Desktop wiring, crash safety, scheduler, or draft-store work was added.
- **Verification:** acceptance greps and `git diff --check` passed. Per the user's locked instruction, no full build or long test suite was run; a focused build could not start because no `dotnet` executable is installed in this fresh sandbox.

### S07 — 2026-10-09 — clinical command implementation (execution gates not run)

- PostgreSQL mappings: Owners, Pets, WeightEntries, MedicalHistory, ReceptionQueueEntries, DocumentReferences, Visits, VisitRxLines, VisitHandoffLines, VaccinationRecords, TreatmentPlans, TreatmentOccurrences, VaccineCatalogItems. Additive `105_ClinicalCore` + uuid version triggers; frozen snapshot/designer authored without invoking EF's implicit build.
- Application clinical families reused (26 wire types, two Visit-start shapes); server payload/permission rows, tenant-filtered uuid reader, 12 repository/query/writer ports and request-scoped clinic timezone. S05 already supplies staff memberships (not S04).
- Tests authored: ClinicalModelTests, ReplicatedSetTests, HandlerCoverageTests, ClinicalIntakeTests, ServerS07RederiveTests, VisitEncounterUseCaseTests, VaccinationUseCaseTests, S07MigrationTests, S07EndToEndTests. **No test/build/smoke was executed; no passing or migration-application claim.**
- No new wire/error codes. Domain rules referenced: WEIGHT_OUT_OF_RANGE, VISIT_DIAGNOSIS_REQUIRED, VISIT_LOCKED, TREATMENT_SCHEDULE_INVALID/TOO_LARGE, TREATMENT_OCCURRENCE_ALREADY_DONE, VACCINE_CATALOG_CODE_TAKEN. Application retains its existing error translations.
- Cross-phase dependency: reception/start/complete handlers inject IAppointmentRepository; appointment mapping/port arrives in S08 (not only `reception.ticket.noshow`). Desktop/SQLite/client-service trees remain untouched.

### S08 — 2026-10-09 — scheduling / boarding implementation (execution gates not run)

- PostgreSQL tables: Appointments, AppointmentSettings, Rooms, BoardingServiceDefs, BoardingStays, BoardingStayItems, BoardingHandoffLines. Additive `106_SchedulingBoarding` + uuid triggers for all six syncable roots; frozen target model and snapshot authored without EF's implicit build. Owner/Pet are reused from S07.
- Eight scoped Application ports + settings store, 13 command payloads/permission rows, same scheduling/boarding handlers, Appointment/Room/BoardingStay version reader. The five self-transacted settings commands remain outside the wire catalog/permission table.
- Resource contention: authenticated intake optimistically fences doctor StaffMembership/Room by uuid before handler slot/occupancy reads; reservation failures retry whole transactions with bounded backoff and tracker reset. No SELECT FOR UPDATE/FromSql. Occupancy includes still-present prepaid/invoice-pending patients, never just the Active billing state. Patient-owner association is rechecked before booking.
- All generated syncable ids captured before save, with primary slot 0 and stable secondary slots; walk-in UI ids normalized to ULIDs; visit-start returns server identity/version; child/update results use parent version. Vaccine/room code uniqueness races map to existing Domain errors.
- Test sources: SchedulingBoardingModelTests, HandlerCoverageTests, SchedulingBoardingIntakeTests, AppointmentBookingUseCaseTests, BoardingUseCaseTests, ServerS08RederiveTests, SlotConflictServerTests, S08MigrationTests, S08EndToEndTests, S08ConcurrencyTests. PG harness uses actual intake receipts, ports, savepoints/retry runner, versions and resource guard; same-slot and last-room-place races are written with PgFact skip/fail gates.
- **No build, test or smoke command executed, no PostgreSQL application or passing-test claim.** Protected Desktop/SQLite/Client.Services diff is empty; repositories contain no filter bypass. Existing Domain/wire codes unchanged.

### S11 — journal implementation (2026-10-10)

- PostgreSQL write-path replication catalog, feed position/epoch services, deferred commit sequencing, root/child capture and tenant provisioning; migration `109_ChangeJournal` + frozen designer/snapshot. ProjectionOrder.Count records 25 replicated roots.
- Authored CatalogTests, JournalSqlTemplatesTests, JournalCoverageTests, JournalOrderingTests; **no tests, builds, migrations or smoke runs executed**, by user directive. Implementation completion is not a production acceptance claim.
- Decision/deviation: actual child FKs are ProductVariants.OwnerId, VisitRxLines.OwnerId, BoardingStayItems.OwnerId, VisitHandoffLines.VisitId and BoardingHandoffLines.StayId; handoffs captured as aggregate children, excluded as standalone roots. Child-only writes rotate the root token. Tenant feed row is saved first in one explicit provisioning transaction.
- Decision/deviation: migrations hand-authored with frozen metadata because dotnet-ef generation implicitly builds. Runtime TRUNCATE revocation remains G09.

### S12 — immutable feed implementation (2026-10-10)

- Additive v2 wire contracts for every catalog root; server-only sync routes, tenant ownership/audit, active-device policy, v2 header gate, ETags and resume-safe immutable gzip pages. Head `110_FeedBatches`; frozen snapshot/designer.
- Snapshot streams allowlisted row images into a bounded disk spool under a 10-second REPEATABLE READ READ ONLY budget; transaction commits/disposes before final page materialization. Global admission serializes long jobs. GC retires, removes files, deletes metadata, then atomically advances journal floor/deletes a contiguous old prefix.
- Authored ContractsTests, PageWriterTests, RecordProjectorTests, RetentionPolicyTests, OpenBatchDecisionTests, GcOrderTests, SyncEndpointsTests, BatchFreezerTests, GcTests, SyncIsolationTests and SyncFlowTests. **No tests/builds/smoke/database migrations executed.**
- Decision/deviation: persistence mirrors 90/120 numeric retention constants, linked to server option defaults by bridge tests; no forbidden server dependency. ServerRoutes.Sync already existed in S01, contrary to pre-verified plan note; reused rather than introduced.
- Decision/deviation: immutable Ready content permits the required Ready->Retired lifecycle transition; pages reject all updates. A compressed page cannot contain its own SHA-256 (self-reference), so SyncPage.PageHash is empty; authoritative PageHash lives in metadata/ETag and ContentHash. Full page count is fixed via two-pass bounded page staging. Oversize single roots fail rather than silently exceed the byte cap.

### S13 — outbox/workers implementation (2026-10-10)

- Same-command-transaction outbox append, narrow OutboxDbContext, atomic SKIP LOCKED work claims with 60-second token-fenced leases, transactional consumed markers, full-jitter retry/dead-letter audit and Done cleanup. Main-context head `111_ServerOutbox`; frozen migration designer/snapshot. Deferred outbox event fills the commit-assigned lastSequence only after journal sequencing.
- In-house TimeProvider breaker (5 failures/60s, 30s open, single half-open probe), linked 10-second timeout, bounded queue capacity 64/two consumers. Global background admission shared with immutable export/retention. No additional ErrorCodes; fenced outbox loss reuses CommandClaimLost.
- OutboxRelayWorker, IntakeRecoveryWorker, CommandIntakeRetentionWorker, FeedRetentionWorker, OutboxCleanupWorker; PeriodicTimer loops survive faults, class-only rate-limited logs, non-fatal name-only worker degradation. Authorized /sync/hub groups are chosen by stored tenant context, never the client.
- Dedicated worker pool default 2; total write/read/keyring/worker = 6/3/1/2 = 12 for 2 GiB, validator also enforces the 20 budget for 4 GiB. Worker scopes select the worker pool before resolving a business context; feed factory always uses the worker pool.
- Authored OutboxClaimSqlTests, ConsumerIdempotencyTests, OutboxClaimTests, WorkerRecoveryTests, ServerRetryPolicyTests, CircuitBreakerTests, OutboundTimeoutTests, BoundedWorkerQueueTests, WorkerLoopTests, WorkerHeartbeatTests, PoolBudgetTests and HintHubTests. **No builds, tests, smoke, migration application or runtime verification executed.**
- Implementation closeout is not a production acceptance claim. Client sources remain outside this phase's implementation scope; the plan is removed only after its implementation tasks are ticked and pushed.

## Decision & deviation log

### S14 — Execution directives and real-seam corrections (2026-10-10)
- User explicitly prohibited every build/test/smoke execution. Gate task records implementation/progress only; no green execution acceptance is claimed. EF migrations were hand-authored, including frozen provider snapshots, to avoid the implicit build in scaffolding.
- IServerOutbox lives in ServerOutbox.cs, not a separate interface file. Generic enqueue was added there with injected IClock.
- The existing TenantProvisioner owned and committed its transaction. It now preserves that standalone behavior while participating in a caller-owned transaction for atomic provisioning; legal-name/timezone forwarding is additive.
- Outbox relay and operator contexts had independent connections. Expiry processing explicitly enlists the narrow operator context in the fenced relay transaction, preserving atomicity of consumed marker and subscription effects.
- Standard plan migration seeds amount zero; SubscriptionOptions initializes the configured amount atomically only before its first subscription, preserving existing terms.


### S06 — verification deferred, server pipeline profile, wire v2 additive          (date: 2026-10-09)
Context:    The session directive forbade running builds/tests; the plan also required reusing Application handlers on the server without the client-only journal.
Decision:   Server runs a dedicated `ServerChain` (no `CommandJournalBehavior`) selected by `ApplicationPipelineProfile`; wire v2 only adds trailing optional members and v1 envelopes stay valid. Acceptance was verified by grep gates only.
Why:        Keeps the client chain and desktop untouched while sharing handlers; avoids a breaking wire change.
Impact:     First next-session action: `build.sh full`, `test.sh Server`/`Application`, `run.sh`; fix any compile fallout before S07.

### S05 — lazy service resolution in the tenant middleware, device-pending policy, 403 for entitlement          (date: 2026-10-09)
Context:    (1) A conventional middleware that takes scoped services as `InvokeAsync` parameters builds them for **every** request, so `/health/live` needed the database (host smoke tests returned 500 without PostgreSQL). (2) `/auth/capabilities` must refuse a `Pending` device while the tenant stage only rejects `Revoked`. (3) The phase does not fix the entitlement status code.
Decision:   (1) `TenantContextMiddleware` resolves `IMembershipResolver`/subject factory/state/audit/clock from `context.RequestServices` only after the skip rule and the authenticated-caller check pass. (2) `DeviceActive` policy reads `ResolvedMembership.DeviceActive` (set from `Device.IsUsable`). (3) Entitlement failures are always `403` + `ENTITLEMENT_REQUIRED` (any gate failure is coerced to that code).
Why:        Health/anonymous routes must keep working with no database; entitlement stays in the Forbidden family like the other planes.
Impact:     No new error codes, no change to the forbidden projects. S16 only swaps the gate registration; S14 fills `OperatorOnly`.

### S04 — inline permission arrays, EF interceptor provider cache, Tenant.Code in SQLite          (date: 2026-10-09)
Context:    (1) `StaffMembership`/`RoleDefinition` permission sets are `IReadOnlySet<string>` over private backing sets; mapping them
            as child tables would need a second entity model. (2) Attaching the *scoped* tenancy interceptors with `AddInterceptors`
            made EF build a new internal service provider per request and throw `ManyServiceProvidersCreatedWarning` after 20 (found by the
            PG-backed tests). (3) Adding `Tenant.Code` to the shared Domain type would have produced a pending model change for the SQLite
            (offline) context.
Decision:   (1) Stored inline as `text[]` via `PermissionSetConversion`; the aggregates stay the single source (no `+2/+1` child tables).
            `_firstFailureUtc` is a field-backed shadow column so the lockout window survives requests. (2) The two interceptors are registered
            as scoped `IInterceptor` and resolved from the application service provider (`ServerReadContextFactory` uses
            `UseApplicationServiceProvider`); the `TenantIsolationTests` probe context reuses one interceptor pair per tenant.
            (3) `builder.Ignore(e => e.Code)` in the SQLite `TenantConfiguration` — local schema/migrations unchanged.
Consequence: One EF internal service provider per context type; offline path untouched; S05 swaps `AuthTenantContext` for the request-bound context.

### S03 — read factory scope, exception base types, runner seam visibility          (date: 2026-10-09)
Context:    (1) The phase text described the read factory as a singleton; it must capture the request-scoped `ITenantContext`
            and scoped interceptors. (2) `TenantContextMissingException`/`TenantMismatchException` derive from `Exception`
            (not `InvalidOperationException` as the reference pack loosely said). (3) The retry state machine needed to be
            testable without PostgreSQL.
Decision:   (1) `IServerReadContextFactory` is registered SCOPED, built from the keyed `oltp-read` singleton data source.
            (2) Tests/handlers treat both as plain `Exception` subclasses. (3) Extracted `ITransactionRunner` + pure `RetryPolicy`
            (internal-visible to `Animora.Server.Tests`); the runner keeps the EF execution strategy non-retrying so the single
            jittered `RetryPolicy` owns cadence (no double back-off).
Consequence: Isolation holds on both write and read paths with RLS off; retry/cancellation semantics are unit-proven without a database.

### S02 — pool defaults, EF Relational pin, design-time/model details            (date: 2026-10-09)
Context:    (1) The plan's defaults `OltpWriteMax=8/OltpReadMax=4/KeyRingMax=1` sum to 13, exceeding the plan's own
            12-connection budget for the 2 GiB profile, so the host failed `ValidateOnStart`. (2) Npgsql EF 10.0.3 pulls
            `Microsoft.EntityFrameworkCore.Relational` 10.0.4 transitively, conflicting with the 10.0.12 pin (MSB3277).
            (3) `AggregateRoot.DomainEvents` is not EF-mappable and the generic convention must ignore it for every aggregate.
            (4) `OptionsBuilder.Bind` needs the Configuration.Binder package; (5) S01 readiness tests assumed one probe.
Decision:   (1) default `OltpWriteMax=7` (7+4+1=12); validator rule unchanged. (2) pinned `Microsoft.EntityFrameworkCore.Relational`
            10.0.12 centrally and referenced it from the PG project. (3) `ServerModelConventions.ApplyTenancy` ignores
            `DomainEvents` on aggregate roots; the tenant accessor is an `Expression<Func<string>>` so EF re-binds it per context.
            (4) explicit, binder-free option binding in `AddAnimoraPostgreSql`. (5) S01 readiness tests stub the three DB probes;
            a new test covers the PostgreSQL-absent case.
Consequence: Behaviour matches the phase intent; the migration was hand-edited as specified (function first, trigger last,
            `Version uuid NOT NULL DEFAULT gen_random_uuid()` without `rowVersion`).

### S01 — build repair, extracted rate-limiter config, SecretGuard stream provider            (date: 2026-10-09)
Context:    (1) The tree did not compile before S01 could be verified: `LocalUnitOfWork.cs` (Persistence.Sqlite,
            from the F-01 fix) used a property-pattern exception filter inside a `catch` that does not parse on the
            pinned SDK. (2) `AddRateLimiter` body was inline in `Program.cs`, which cannot be exercised without
            a business endpoint. (3) `JsonConfigurationProvider` alone misses `AddJsonStream` providers, so the
            mandated `SecretGuardTests` could not feed the guard a literal JSON secret.
Decision:   (1) rewrote it as `catch (SqliteException ex) when (ex.SqliteErrorCode == 19)` — identical semantics.
            (2) moved the configuration into `Hosting/ServerRateLimiting.Configure` (Program.cs still calls
            `AddRateLimiter`, acceptance #5 holds). (3) `SecretGuard` scans `JsonConfigurationProvider` **and**
            `JsonStreamConfigurationProvider` (both are JSON-sourced; env/user-secrets stay exempt).
            Also: `GlobalExceptionMiddleware` re-echoes `X-Correlation-Id` after `Response.Clear()` (Clear drops it).
Consequence: Acceptance #4 ("client untouched") has one semantic-neutral exception: `LocalUnitOfWork.cs`.
            Pre-existing, unrelated: `LayeringRulesTests.Contracts_HaveExactlyOneRealRegistration` fails on the
            current tree (a Contracts interface has no single Client.Services registration; introduced by post-C11
            purchasing/sales work) — not caused by S01, left for the owning client phase.

### C11 — Migration numbering, DI seams and scoped acceptance            (date: 2026-09-30)
Context:    Ordinal 018 already belonged to UserPreferences; the real DI audit needed a shared
            registration seam and exposed Client.Services EF leakage. User prohibited full builds
            and long tests. Sandbox login-gated startup differs from the phase's authenticated UI metrics.
Decision:   Use migration 019, add Domain.Tests→Desktop reference, extract registration-only
            App.ConfigureServices, move draft persistence to IUiDraftStore, and register cashier use cases.
            Complete sequential targeted builds + 32 short filtered tests instead of full build/test.sh.
            Record login-window first-frame/RSS and warm dashboard-query proxies explicitly; keep initial
            cold-cache failure and manual authenticated 17-screen/render/shell review outstanding.
Rule check: R-B34/CONVENTIONS §10: mock purge + all 24 interfaces machine-checked, no weakened guard.
            R-B10/§2: Desktop/Client EF/HTTP dependencies forbidden and the draft-store violation fixed.
            R-B09/§9: real migrated-file reopen scenario asserts codes/IDs/numbers; stock/UoW regressions pass.
            R-B01: GUI smoke succeeds with lo DOWN and no routes. A-RULE 035/R-B08: six actual measurements,
            described hardware, >2x non-zero failure and honest proxy/over-budget limits; no capacity promise.
Impact:     Track C complete under documented verification scope; next D01, desktop remains server-free.
            E03 extends SyncCursor; E04 changes LocalSyncStatusService internals, not its registration.
            Full-suite/manual UI and cold-start/performance follow-up remain visible to later reviewers.

> Append an entry whenever an executing session deviates from its phase file, or makes a decision the
> phase file did not pre-make. Format:

```
### <PhaseID> — <short title>            (date: YYYY-MM-DD)
Context:    what forced the decision
Decision:   what was done instead
Rule check: which A-RULE / T-RULE / CONVENTIONS § was consulted and why this complies
Impact:     phases affected + what a later session must know
```

### C03 — migration number, reused permissions, note merge, complete/next-dose modeling            (date: 2026-09-28)
Context:    The phase file (a) numbered the migration `009` (already taken by C01); (b) invented new permission
            strings (`visits.encounter.edit/complete`, `vaccination.record.edit`); (c) named a 4-field
            `VisitNoteField` while the `Visit` aggregate has no separate History/Exam fields; (d) named a
            5-bucket `VaccinationStatus` while Contracts freezes a 4-value enum; (e) specified a
            "diagnosis OR one line" complete-guard while `Visit.Complete` hard-requires a Diagnosis; (f)
            required "administering a dose with a follow-up protocol creates exactly one next record" with no
            Domain "scheduled-but-not-administered" record type; (g) placed "field commands" on the frozen
            `IVisitEncounterService` seam.
Decision:   (D1) migration = **`010_C03Schema`**, single migration carrying both new tables. (D2) reuse the
            existing `PermissionCodes.VisitsWrite`/`VisitsComplete`/`VaccinationWrite`; `CompleteVisitCommand`
            gates on `VisitsComplete`. (D3) Contracts `IVisitEncounterService` keeps its 12 members + one
            additive `FlushAsync`; per-field commands live in Application/Client.Services. (D5) note merge:
            `Diagnosis→Diagnosis`, `Plan→TreatmentPlan`, `History`+`Exam`→ labelled sections concatenated into
            `DoctorNotes` (both survive independent edits), via `SetAssessment` only — no new `Visit` field.
            (D6) 5-bucket read-model enum lives in Domain/read layer for the tests; Contracts mapping collapses
            `DueSoon→Due`, `Upcoming→Scheduled`. (D7) C03 bucket is a new read projection from `DaysUntilDue`;
            `VaccinationRecord.StateAt` untouched. (D8) **complete requires a non-empty Diagnosis** — the
            "OR lines" phrase is only the pre-attempt `VISIT_INCOMPLETE` guard; empty diagnosis →
            `VISIT_INCOMPLETE`. (D9) `MarkVaccinationDoneCommand` administers the current record + appends
            history, and creates exactly one next `VaccinationRecord` (dose N+1) when `DoseNumber < TotalDoses`;
            a second invocation (a dose N+1 record already exists for the pet+code) → `VACCINATION_ALREADY_DONE`.
Rule check: CONVENTIONS §111 (append-only sequential migrations) → D1; A-RULE 002/003 append-only history
            (update throws) verified by test; A-RULE 033 (tenant+user+resource+version cache key) → `TenantScopedCache`;
            PERFORMANCE §4 (never block UI) → `DebounceBuffer` non-blocking; Domain-frozen `Visit`/`VaccinationRecord`
            (A04) honoured — no aggregate reshape (D5/D7/D8). Reusing permission codes avoids touching
            `DefaultRolePermissions`/`PermissionCodes.All`/role seed.
Impact:     **C07** consumes `VisitBillingHandoff` (numbered 010, `MarkInvoiced`/`Cancel` already present) and the
            reception `Done` transition; C04+ build on completed visits + the vaccination read projection. Any later
            phase reading vaccination status through Contracts sees the 4-value collapse (D6); a 5-bucket consumer
            must use the read model / `VaccinationThresholds` directly. `SmsTemplateRenderer` has **no delivery** —
            wiring a real channel is a future/out-of-band concern.

### C02 — paths, permissions, no-show modeling, gate scope            (date: 2026-09-27)
Context:    The phase file places `OperationIdFactory` under `Commands/`, cites permissions `reception.queue.manage` /
            `visits.encounter.edit`, and `QueueState` has no `NoShow` member. The user's locked directive forbids
            long/heavy builds and test runs.
Decision:   (1) `Deterministic(seed)` added at the real path `Application/Correlation/OperationIdFactory.cs`.
            (2) Reused existing `PermissionCodes.ReceptionManage` (6 reception commands) and `VisitsWrite` (2 start-
            visit commands) — no new permission strings (would need role seeding). (3) No-show lives on the
            `Appointment` (`MarkNoShow`); an existing Waiting/Called queue row is `MarkLeft`; the projection reports
            `NoShow` from the appointment state; a walk-in "no-show" is just `MarkLeft`. (4) `TriagePatientsQuery`
            orders by last `Visit.StartedUtc` desc (null last) then pet id. (5) Gate: `build.sh full` = 0/0 plus the
            C02-filtered tests only; whole-suite `test.sh` and `run.sh` smoke not run per the user directive.
Rule check: CONVENTIONS §4 (errors are codes), §5/PERFORMANCE §6 (one statement, UTC range, no N+1), §10 (mocks
            retired in-phase). Domain gained no member.
Impact:     C03 continues from the `InProgress` visit C02 starts. A later session may run `test.sh`/`run.sh` to
            re-confirm the smoke; the pre-existing B04-Security test failures (C01 closeout) still apply.

### C01 closeout — first real build/test/smoke; 4 latent defects fixed            (date: 2026-09-27)
Context:    C01 was authored and merged (up to `767659d`) without ever compiling — no .NET SDK was present in the
            earlier sessions. This closeout installed SDK 10.0.401 on disk and ran the real gates. `build.sh full`
            immediately surfaced defects that only a compiler catches, and `test.sh` surfaced one wrong test
            expectation; the whole-suite run also showed 34 unrelated B04-Security failures.
Decision:   Fixed the 4 latent C01 defects (all in C01-authored files): (1) `PatientsUseCaseTests` missing
            `using Animora.Application.Abstractions;` for `CommandContext` (CS0246); (2) `PatientDirectoryQueryTests`
            used non-existent nested `LocalStoreFixture.FakeClock` → the top-level `FakeClock` (CS0426);
            (3) removed the unused `IClock clock` primary-ctor param on `PatientsSampleData` (CS9113, warnings=errors
            under `build.sh`); (4) `PatientsMapping.Split` now also splits on the Persian comma `،` (U+060C) so
            Persian-comma clinical notes split correctly (the mapping test's input uses it). Also relaxed the
            single-statement projection assertion to skip EF Core 10's `.param set` preamble before checking the
            body is one `SELECT`. Left the 34 B04-Security test failures untouched: proven pre-existing by
            reproducing them verbatim at the pre-C01 commit `1d1e940` (B05c) in a throwaway git worktree — root cause
            is those tests not registering `IStaffMembershipRepository`/`IRoleDefinitionRepository`/
            `ICapabilitySnapshotSigner`; no C01 file references them, so the fix is a B04 concern, out of C01 scope.
Rule check: CONVENTIONS §4 (errors are codes; Persian handling stays in mapping/normalization), §8 / PERFORMANCE §6
            (projection stays one no-N+1 statement — only the test's preamble handling changed, not the query).
            `build.sh full` = 0/0 (warnings-as-errors satisfied); `run.sh` = SMOKE RUN OK.
Impact:     C01 is now build/test/smoke-verified for its own surface. A later B04 closeout must register the three
            Security repositories in the pipeline test container to turn the 34 red tests green; until then a
            whole-suite `test.sh` is red for reasons unrelated to any Track C phase.

### C01 — migration number, single-statement assertion & permission-test shape            (date: 2026-09-27)
Context:    The phase text names the migration `008_PatientsSearchKeys`, requires each directory query's
            `ToQueryString()` to "contain exactly one `SELECT`", and requires a test proving "a user lacking
            `patients.pet.edit` is denied". But `008` was already taken by B05c (`UiDraftState`); the no-N+1
            projection legitimately renders correlated scalar sub-SELECTs (last weight / pet count) inside one
            statement; and the end-to-end deny path is already exercised generically by `AuthorizationGateTests`.
Decision:   (1) Numbered the migration `009_PatientsSearchKeys` (next free number; 001–008 byte-identical).
            (2) Asserted the projection is a single, non-batched SQL statement whose top level is a `SELECT`
            (via `ToQueryString()`), not a literal one-`SELECT`-token count — a scalar sub-SELECT is part of the
            same round trip and is exactly what avoids N+1. (3) Proved the pet mutations carry
            `[RequiresPermission("patients.pet.edit")]` (owner mutations `patients.owner.edit`) by reflection and
            rely on `AuthorizationGateTests` for the pipeline deny path, rather than rebuilding the pipeline in the
            patients test.
Rule check: CONVENTIONS §5 (search filter runs in SQL over an indexed column), §8 / PERFORMANCE §6 (one statement
            per list, no N+1), §10 (mock retirement), §4 (errors are codes, no Persian in Application/Client.Services).
            Migration numbering is sequential and additive (A-RULE on append-only migrations).
Impact:     C02+ cite "the C01 pattern" and reuse `IStoreReadinessGate`. Any doc/tooling that expected `008` for
            patients search keys must read `009`. (build/test/smoke now run — see the C01 closeout entry above.)

### B05b — gate limited by the session instruction            (date: 2026-09-27)
Context:    The phase document requested `build.sh full`, while the user's direct locked instruction explicitly prohibited full builds and long tests; this fresh sandbox also has no dotnet executable installed.
Decision:   Ran only acceptance greps and `git diff --check`; did not install an SDK, run a full build, or execute tests.
Rule check: The direct session instruction has priority and avoids unrelated environment setup and heavy verification work.
Impact:     B05c must compile B05b as part of its own required build/test gate before finalizing B05.

### B05a — migration split and notification placeholder            (date: 2026-09-27)
Context:    B05 is intentionally split while the integrity sink already needs the future notification port.
Decision:   Migration 007 contains only AuditEntries; UiDraftStates remains migration 008 for B05c. A TryAdd no-op IUserNotificationPort keeps the audit-backed integrity sink composable until B05c supplies the real adapter.
Rule check: Preserves append-only audit durability, migration ownership, and inward dependency direction.
Impact:     B05c must replace the placeholder notification adapter and create migration 008 without altering 007.

### B01 — pipeline/wiring details the phase file left open            (date: 2026-09-24)
Context:    The phase lists 6 behaviors "in this exact order" with ExceptionMapping last yet calls it "the
            outermost catch"; `ValidationBehavior` must return a `ValidationFailure[]` payload but `Result<T>`
            carries none; `SingleUserProvider` must read `LocalInstallation`/`InstallationIdentity`, which live
            in Persistence.Sqlite / Infrastructure.Local (Application may not reference them); the mapping
            table names EF/Sqlite exception types that Application may not reference.
Decision:   (1) Composition order is Correlation → ExceptionMapping → Validation → Authorization →
            Entitlement → Transaction → Handler: the catch wraps every inner stage and sits inside Correlation
            so mapped failures still get the correlation prefix; the recorded execution order of the other
            stages is exactly the specified one. (2) Validation failures travel in `Detail` as
            `validation: Field=CODE; ...` (`ValidationFailure.FormatDetail`/`ParseDetail`). (3) New port
            `Ports/ILocalInstallationIdentity` (TenantId, DeviceId), implemented by
            `Infrastructure.Local/Security/LocalInstallationIdentity` (tenant from `LocalInstallationState`,
            device = installation id, or the machine-fingerprint hash before bootstrap) and registered in
            `AddAnimoraLocalInfrastructure`. (4) `ErrorMapping` matches `DbUpdateConcurrencyException` /
            `SqliteException` by full type name along the base chain, plus `DbException`/`IOException` by type
            (failure path only). (5) Extra: `RequireCommand/RequireQuery` declare required handlers so a
            "command with no handler" is a startup failure; a command with no `IUnitOfWork` fails closed
            with `DISPATCH_FAILED`. (6) Application now references `Microsoft.Extensions.DependencyInjection`
            (already pinned); the test project references `Microsoft.EntityFrameworkCore.Sqlite` to throw the
            real provider exceptions.
Rule check: CONVENTIONS §2 (Application still has no EF/Datasync reference — `LayeringRulesTests` green),
            PERFORMANCE §3 (frozen map, closures built once; `NoReflectionTests`), A-RULE 005/023.
Impact:     B02 can replay stored commands through `HandlerEntry.Invoker`; B03/B04/E07 replace the defaults by
            registering before (or `Replace`-ing) the `TryAdd` defaults; C01 calls `AddAnimoraApplication()`.

### A01 — anchor AssemblyMarker types in Domain & Application            (date: 2026-09-23)
Context:    `Animora.Domain` and `Animora.Application` are still empty placeholders (no types). The phase
            requires `LayeringRulesTests` to "force assembly loading with a `typeof(...)` touch of one type
            per asserted assembly". With no type to touch, the assemblies could not be loaded/anchored and
            the C# compiler would omit their (unused) project references from the manifest.
Decision:   Added a single empty `public static class AssemblyMarker` to `src/Animora.Domain` and
            `src/Animora.Application` purely as a `typeof(...)` anchor. No behavior, no data, no dependency
            direction change. (`Animora.Contracts` already has real types, so it uses
            `typeof(Contracts.Navigation.UserRole)`.)
Rule check: CONVENTIONS §1 (no TFM/Nullable in the marker's project — inherited; unchanged), §2 (adds no
            reference — Domain still depends on nothing), §4 (sealed/`static` type). The phase file itself
            mandates the `typeof(...)` touch, so this realizes rather than contradicts its intent.
Impact:     A02 onward may keep or replace these markers when real Domain/Application types appear; the
            layering facts key off assembly reference sets, not the marker, so removing them later is safe
            as long as another loadable type exists in each assembly.

### A08 — supporting members the phase table implied but did not name            (date: 2026-09-23)
Context:    The phase file specifies the posting rules as "pure functions taking the event payload +
            an `IClock` + a `periodId`", but a `JournalEntry` also needs an `EntryNumber`, a
            `PostedByUserId` and an `OperationId` — none of which any of the events carries, and all
            of which `JournalEntry.Create` requires. Three rules additionally need data the event
            payload does not carry at all: `InvoicePostedEvent` has only the grand total (not the
            per-line-kind revenue split, the discount or the tax), `GoodsReceiptPostedEvent` has only
            a `LineCount` (not the cost), and `StockMovementRecordedEvent` has only a quantity (the
            weighted-average cost is `StockCalculator`'s, per R-B23). The file also names the cheque
            mutators and `Expense.Post` but no actor parameter, no creator for `Cheque`/`Expense`,
            and no way to record the `JournalEntryId` the two entities declare.
Decision:   Added, all inside the eleven files the phase table already lists and with no new file:
            (1) `readonly record struct PostingContext(EntryNumber, PeriodId, PostedByUserId,
                OperationId?)` in `PostingRules.cs` — the bookkeeping the Application layer owns,
                passed as one argument instead of four, so the rules stay pure;
            (2) `ForInvoicePosted` additionally takes the read-only `Invoice`, `ForGoodsReceiptPosted`
                the `GoodsReceipt` + currency, and `ForStockMovementRecorded` the already-derived
                `Money cost`, and `ForInvoiceRefunded` the original `PaymentMethod` — the rules read
                these, never compute or store them, so they remain pure translations;
            (3) `ForInvoiceVoided` takes the original posting `JournalEntry` because the tabled rule
                *is* "reversal of the posting entry" and a reversal needs the entry it mirrors;
            (4) `Cheque.Register(...)` and `Expense.Create(...)` factories (the file lists mutators
                but no creator), mirroring `GoodsReceipt.Create` from A06;
            (5) an `actorUserId` parameter on every cheque transition — `ChequeEvent` declares an
                `ActorUserId` field the phase file itself specifies, which nothing else could fill;
            (6) `Cheque.LinkJournalEntry` / `Expense.LinkJournalEntry` and `Expense.MarkReversed`,
                the only writers of the `JournalEntryId` / `Reversed` state both entities declare;
            (7) `Expense.Edit` / `SetSettlement` / `AttachReceipt`, draft-only, so a `Draft` expense
                is actually editable before the one-way `Post` (otherwise `ExpenseState.Draft` and
                `EXPENSE_IMMUTABLE` would both be unreachable);
            (8) `LedgerAccount.Create`/`Rename`/`Deactivate`/`Activate` and
                `AccountingPeriod.Create`/`BeginClose`/`Lock`, without which the two entities could
                never be instantiated or reach the states their own enums declare;
            (9) `ForPaymentRegistered` maps the three methods the phase table does not list:
                `Insurance` → 1110, `Wallet` → 2100, and `Credit` → refused (it moves no money, so
                there is nothing to post) — the tabled Cash/Card/Transfer/Cheque rows are unchanged;
            (10) `PostingRules.Add` skips a zero-amount component rather than emitting a zero line,
                which is what keeps `ForInvoicePosted` valid for an invoice with no discount, no tax
                or no insurance (a zero line would otherwise trip `JOURNAL_LINE_AMOUNT_INVALID`).
Rule check: CONVENTIONS §2 (`Animora.Domain.csproj` still has zero references — verified), §3
            (naming), §4 (`sealed`/`record`/`Result` returns/`IClock` injected, never ambient), §11
            (build 0/0, smoke OK). A-RULE 002 is strengthened, not relaxed: none of these additions
            is a mutator on `JournalEntry`, which still has no setter of any kind, and `Expense`'s
            new mutators are all draft-only. A-RULE 003 holds: `MarkBounced`, `Return`, `Cancel` and
            `CreateReversal` each require a reason. PERFORMANCE §2: the `Create` validation and the
            `ForInvoicePosted` revenue split are single indexed `for` passes, no LINQ.
Impact:     A10 constructs a `PostingContext` per dispatched event (entry-number allocator + resolved
            period + actor + the envelope's `OperationId`) and loads the one aggregate the three
            data-hungry rules need; it must call `AccountingPeriod.EnsurePostable` before every
            `JournalEntry.Create`. A09 maps `Cheque.History` and `JournalEntry.Lines` as owned
            collections and gives `JournalEntry` **no** update path. A12 asserts the reflection
            "no public setter" fact against `JournalEntry` exactly as written above.

### A07 — supporting types the phase table implied but did not name            (date: 2026-09-23)
Context:    The phase file specifies `Invoice.DiscountKind DiscountKind`, an `InvoiceTotals` static that
            returns "the totals", `Invoice.MarkSettlement(SettlementStatus, string? errorCode)` and a
            `CashSession` aggregate, but the Domain layer owns no `DiscountKind` type (the existing one
            lives in `Animora.Contracts`, which `Animora.Domain` may never reference — CONVENTIONS §2),
            names no return type for the totals function, no storage for the settlement error code, and
            no constructor for a cash session.
Decision:   Added, all inside the nine files the phase table already lists and with no new file:
            (1) `enum DiscountKind { Percent, Amount }` in `Sales/InvoiceTotals.cs` — the Domain's own
            copy, member-for-member identical to the Contracts one, so no layering edge is created;
            (2) `readonly record struct InvoiceTotalsSnapshot` as `InvoiceTotals.Compute`'s return type;
            (3) `InvoiceTotals.ComputeDue(...)` so the documented `DueTotal` formula also lives in the
            single totals module instead of being re-implemented on the aggregate;
            (4) `Invoice.SettlementErrorCode` to actually hold `MarkSettlement`'s `errorCode` argument;
            (5) `CashSession.Open(...)` factory (the phase lists the four mutators but no creator) and
            `ReconciledByUserId` to hold `Reconcile`'s argument;
            (6) `Invoice.Create(...)` factory, mirroring `PurchaseOrder.Create` from A06.
            `CashMovement.Create` reuses A06's `MOVEMENT_SIGN_INVALID` rather than adding a 15th code —
            it is literally the same rule (kind fixes sign), and §3 forbids duplicate codes for one
            condition.
Rule check: CONVENTIONS §2 (Domain still references nothing — verified: `Animora.Domain.csproj` is
            empty), §3 (naming), §4 (sealed/record/`Result` returns/no ambient clock), A-RULE 002/003/006
            unaffected (all additions are read-only or factory paths). ARCHITECTURE §8.3's state list is
            reproduced exactly.
Impact:     A09 maps `Invoice.SettlementErrorCode` as a plain nullable string column; C07/C08 map the
            Domain `DiscountKind` to the Contracts one explicitly (a one-line switch, not a cast) —
            the two enums are deliberately separate types.

### B03a — error-code reuse, tooling & session details            (date: 2026-09-24)
Context:    Phase file pre-made the `ACCOUNT_LOCKED`/`ACCOUNT_DISABLED` substitution; it also assumed install-prereqs
            installs dotnet-ef to `.sandbox/tools` (it does not) and left some session mechanics open.
Decision:   (0) The package `Microsoft.AspNetCore.Identity` has no 10.x on NuGet (last 2.3.x; in .NET 10 it is shared-framework
            only). Pinned `Microsoft.Extensions.Identity.Core 10.0.12` instead — it contains `PasswordHasher<T>`,
            `PasswordHasherOptions` and `PasswordVerificationResult` in namespace `Microsoft.AspNetCore.Identity`. B03c's
            `LayeringRulesTests` fact must assert on `Microsoft.Extensions.Identity.Core` (and the namespace
            `Microsoft.AspNetCore.Identity`) being used only by Infrastructure.Local.
            (1) Reused `UserLockedOut`/`UserDisabled` instead of adding `ACCOUNT_LOCKED`/`ACCOUNT_DISABLED`.
            (2) Installed dotnet-ef 10.0.12 with `dotnet tool install dotnet-ef --version 10.0.12 --tool-path .sandbox/tools`
            (git-ignored). (3) `LocalSessionService` is a singleton that opens a DI scope per operation (session is
            app-wide; DbContext stays scoped). (4) Throttle interval exposed as `LocalSessionOptions.TouchThrottle`.
            (5) Touch on a locked session returns `PERMISSION_DENIED`; unlock failures on a non-existent session return
            `NOT_FOUND`. (6) `SessionSnapshot.Role` = first active `StaffMembership` role of the user (null if none).
            (7) Unlock does not rehash on `SuccessRehashNeeded` (sign-in handler does, B03b).
Rule check: CONVENTIONS §2 (Identity only in Infrastructure.Local), §4 (Result codes, IClock, ULIDs), §7 (Identity primitives),
            A-RULE 029 (explicit expiry, no silent extension), R-B09 (durable session row).
Impact:     B03b consumes `IPasswordHasher`, `PasswordPolicy`, `ISessionService`; it should write `LoginAttemptLog` rows on
            sign-in. install-prereqs.sh should be taught to install dotnet-ef (not changed here — out of scope).

### B03b — self-transacted auth commands, anonymous contexts & registration            (date: 2026-09-24)
Context:    The phase file assumed identity commands run inside the normal pipeline transaction and that a session
            exists for every dispatch; neither holds for sign-in / first-run / unlock.
Decision:   (1) Added `ISelfTransactedCommand`: `TransactionBehavior` skips such commands and their handlers call
            `IUnitOfWork.SaveAsync` explicitly — a failed sign-in must still persist its failed-attempt counter and
            audit row (the ambient UoW rolls back on `Fail`), and `LocalSessionService` writes through its own scope,
            which an open ambient SQLite write transaction would block. All 6 identity commands use it.
            (2) `ICurrentUserProvider` gained `BuildAnonymousCommandContext`/`BuildAnonymousQueryContext`
            (`UserId = U-ANONYMOUS`) so pre-auth requests can dispatch; every other request requires a live session.
            (3) `SessionContextCache` singleton so the synchronous `BuildQueryContext` never hits the store.
            (4) Use cases are registered through `builder.AddIdentityUseCases()` (opt-in) — registering them
            unconditionally in `AddAnimoraApplication` would break `ValidateOnBuild` for hosts without the local store;
            B03c's Desktop calls it inside its `configure` lambda.
            (5) Added `Unit`, `ILoginAttemptLog` port, `IIdentityRepository.AnyUserAsync`/`FindTenantAsync` (first-run
            guard = "any user exists", matching `LocalSessionService.IsFirstRun`; clinic name renames the seeded tenant).
            (6) Unknown username still performs one dummy hash verification (timing does not leak existence); membership
            missing after a correct password → `USER_DISABLED`.
            (7) Gate ran targeted builds (Application.Tests, Infrastructure.Local = 0/0) instead of `build.sh full` per
            user directive (no full build).
Rule check: CONVENTIONS §2 (Application has no Identity/EF refs), §4 (sealed, Result, IClock, ULIDs, ConfigureAwait),
            §7 (no user-existence leak, stamp rotation via `SetPasswordHash`), A-RULE 023/029.
Impact:     B03c: Desktop must call `AddIdentityUseCases()`, dispatch SignIn/FirstRun/Unlock/CurrentSession with the
            anonymous contexts, and run `build.sh full` + the B03 test suite.

### B03c — tree vs. phase mismatches & sandbox fixes            (date: 2026-09-26)
Context:    The phase text diverged from the tree in places, and wiring the backend into Desktop exposed two build issues.
Decision:   (1) `UserRole` had no `Receptionist` (phase said it did); appended it so the required mapping holds and
            existing numeric values are kept.
            (2) `RoleDefinition` has `DisplayKey` (e.g. `role.manager`), not `DisplayName`; `CurrentUserContextService.RoleLabel`
            returns that stable key (Client.Services produces no Persian text); shell labels were not refactored here.
            (3) `DialogService` had no attached `ModalHost` before `MainWindow`, so `ShowModal` from login was a silent
            no-op; added a minimal owned-`Window` fallback in `DialogService.cs` only (outside the phase file table, but
            required for a runnable first-run). `IDialogService` unchanged.
            (4) Referencing the backend made `Application.Current` ambiguous with namespace `Animora.Application` inside
            `Animora.Desktop`; qualified as `global::Avalonia.Application.Current` in `App.axaml.cs`, `ToastService.cs`,
            `IconKeyConverter.cs` (compile-error-driven, no behaviour change).
            (5) The Avalonia XAML compiler hit `OutOfMemoryException` under the sandbox's 448 MiB GC cap once Desktop
            compiled against the full backend graph; raised `DOTNET_GCHeapHardLimit` in `scripts/sandbox/_env.sh` to
            ~576 MiB (still well under 985 MiB; single-process flags unchanged). A partially OOM-built Desktop `obj/`
            produced a "No precompiled XAML" smoke crash; a clean Desktop rebuild fixed it.
            (6) `install-prereqs.sh` was run once because the .NET SDK was actually missing.
            (7) The Domain.Tests project also references `Animora.Client.Services` so the layering fact can load it.
            (8) Full test suite not run (user directive); only the B03c targeted tests ran.
Rule check: CONVENTIONS §2 (Contracts pure; Identity package only in Infrastructure.Local — proven by test), §4, §7, §9, §10.
Impact:     B04 can build on `IAuthenticationService`/`CurrentUserContextService`; a later localization pass should map
            `role.*` keys to Persian labels in the Desktop.

### S07 implementation deviations — 2026-10-09
- Live Owner delete cascades tombstones; Pet update has no Species field. Preserve shared C01 behavior instead of inventing outdated OWNER_HAS_ACTIVE_PETS/PET_SPECIES_IMMUTABLE handler outcomes.
- Live Visit service lines have no EF materialization constructor, and local handoffs are LocalOnly JSON snapshots: server uses its own syncable VisitHandoffLines snapshot and jsonb service-line field; prescriptions are relational VisitRxLines.
- Server command-plane opts out of UI query registrations while preserving the client default, avoiding unprovided directory/Rx/local-only query dependencies. Query invoice projections remain null until S09; SMS template administration has no S07 server wire surface.
- User expressly forbids all builds/tests/smoke; migration/snapshot authored directly and test sources written only. Execution acceptance gates remain unverified.

### S08 implementation deviations — 2026-10-09
- Live appointment preferences use a client-local file, not an existing SQLite table. Server creates a tenant-scoped jsonb settings row; file-backup import/export return false and never accept arbitrary host paths. No settings wire types were invented.
- Boarding items are relational Money/Quantity rows (private EF-only StayItem constructor); room history remains jsonb. LocalOnly handoff classes are not referenced: server owns syncable immutable snapshot rows for S09.
- Invoice fields/filters await S09; product/variant selectable lookup awaits S10; only currently mapped boarding-service prices resolve. No missing financial or stock data is invented or implicitly mapped by S08.
- Clinical live-handler drift and all skipped execution gates are recorded in the S07 entry. Full execution verification remains required before release; neither done row means tests were run.

### S09-a — 2026-10-10 (T2–T4 authored)
- Sales/cashier PostgreSQL mappings, composite child rows, tenant-scoped DocumentCounters and migration 107 authored.
- No standalone ServiceRate entity exists; rates remain Product/BoardingServiceDef, no ServiceRates table added.
- Model tests authored. No build/test/smoke/EF-tool execution per explicit user instruction; migrations manually authored and execution gates unverified.

### S09 T6 deviations — 2026-10-10
- IFinancialEffectReporter already exists in the server. CommandDispatchResult implements it; explicit catalog metadata reports Applied only after successful monetary dispatch. Domain/Application remain independent of server metadata.
- Existing PostInvoiceCommand already writes movements through IStockMovementRepository. A no-op would silently lose stock, so the narrow IStockMovementWriter uses a real transaction-bound append writer immediately; S10 supplies its mapped tables. No fake/no-op financial write is registered.
- Intake exposes its current operation to the request-scoped cashier query, excluding the close command itself from unsettled-command blockers; transaction locks serialize invoice and cashier mutations.

### S09 T8 baseline corrections — 2026-10-10
- Live S09 catalog has 21 wire types (18 sales + 3 cashier), not 18 total. S10 inventory has 9 live types, so S10 total is 23, not 22. Coverage asserts the live inventories rather than the erroneous plan totals.
- Visit/stay invoice creation share aggregate and exactly the same HandoffId shape. A server-only routed payload looks up both tenant-scoped handoff stores and delegates to the existing Application handlers, rejecting an ambiguous or missing source. Both original handlers remain registered; desktop/wire contracts unchanged.

### S09 — 2026-10-10 (T9 implementation gate)
- Sales/cashier handlers, 21 live wire types, permissions, PostgreSQL ports, UUID version reads, resource locks and authoritative document counters authored.
- Intake coverage, money/cash invariants, migration/tenant-isolation/counter-concurrency regressions authored; none executed.
- Desktop, SQLite and client services untouched. Repository query filters never bypassed.
- Per explicit user directive, full/fast builds, tests and smoke run are NOT executed. This is implementation completion, not production acceptance; runtime/PG gates remain unverified.
- Reporter/stock seam and identical handoff-shape deviations documented in the S09 task entries above. Next implementation phase: S10.

### S10-a — 2026-10-10 (T10–T12 authored)
- Inventory, purchasing and finance PostgreSQL relational mappings, migration 108 and immutability triggers authored. No on-hand/read-model table is persisted.
- Model, composite-child and pure stock-derivation regressions authored; all execution gates remain unexecuted per user directive.
- RecurringExpensePayment is an existing SyncableEntity, not an owned Domain child. Its existing Id key + unique tenant/template/period guard are preserved; it is command-settled only and not exposed to replication.

### S10 T15 catalog gate — 2026-10-10
- Registered all 23 live inventory/purchasing/finance wire types and matching permissions; reserved types remain absent.
- Removed the mandatory permission-catalog Skip; its inventory explicitly excludes reserved types which fail HANDLER_NOT_REGISTERED before authorization. No separate skipped payload-catalog test exists in this baseline.
- No build/test executed. Gate enabled in source only, not claimed green.


### S10 — 2026-10-10 (T16–T17 implementation gate)
- Inventory/purchasing/finance handlers (23 live wire types), permissions, PostgreSQL ports, derived (never persisted) stock on-hand, append-only StockMovement/JournalEntry triggers, in-transaction `ServerDomainEventDispatcher` + `LedgerPostingHandler` (no `FinanceProjectionHandler`) authored; journal/expense numbers via `DocumentCounters`.
- T16 regressions authored (coverage, intake, stock derivation/immutability, ledger immutability, re-derive, domain-event coverage, PG-gated migration/e2e); none executed.
- Static gate checks only: no `IgnoreQueryFilters` in PG repositories, no `OnHand` in PG configurations, no diff in Desktop/Sqlite/Client.Services. `ServerSchemaVersion.ExpectedHead = 108_MoneyStock`.
- Deviations: live S10 catalog is 23 wire types (not 22 as planned); reserved types remain `HANDLER_NOT_REGISTERED`. Per explicit user directive, full/fast builds, tests and smoke run are NOT executed; this is implementation completion, not production acceptance. Next implementation phase: S11.

### S11–S13 — execution decisions (2026-10-10)

- Explicit user no-build/no-test directive overrides the plan's execution commands. Migration sources and frozen metadata were hand-authored to avoid dotnet-ef's implicit compilation. All runtime/PG/acceptance gates remain unverified; no success is fabricated.
- Actual child FKs and existing ServerRoutes.Sync supersede stale pre-verified notes; persistence holds mirrored 90/120 retention constants with authored bridge tests, not an illegal dependency on internal Server types.
- S13's IServerOutbox persistence implementation cannot depend on an internal Server interface. A persistence-owned public seam plus a scoped server facade preserves S06 callers/test doubles without a dependency cycle. The no-op registration is removed.
- Pool rebalance is 6 OLTP write / 3 OLTP read / 1 separate keyring context / 2 dedicated workers (12 total). Global claim index is the explicitly allowed work-distribution exception.
- Relay claims two concurrently (within the required <=20 bound) to keep both in-flight consumer timeouts below lease lifetime. Queue capacity remains 64 with Wait backpressure/two consumers.
- Recovery receipts omit authenticated user/device and aggregate precondition evidence. Worker takes over expired leases through IntakeRecoveryService, then fences safe records to Failed awaiting authenticated replay; it never fabricates authority or replays money effects anonymously. SignalR is a non-authoritative hint; a crash around delivery may duplicate a hint, never lose authoritative feed state.
- Ready->Retired is required for GC and is the only permitted immutable batch lifecycle change. Self-referential compressed PageHash is impossible; authoritative page SHA-256 is metadata/ETag, with an empty embedded hash. Oversize indivisible roots fail rather than violating 256 KiB. Snapshot rows and page metadata are streamed via bounded disk staging, with UTC canonicalization and an absolute hard 10-second snapshot cap.
