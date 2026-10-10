# Plan — Track S: Resolve Test & Build Issues

> **Source phase:** `backend-roadmap/S-TRACK-TEST-BUILD-ISSUES.md` (run 2026-10-10).
> **Goal:** Take Track S (server + DB) from **652/759 passing + 26+12 warnings**
> to **clean build (0 warnings) + all server tests green**, then delete the phase file.
> **This plan is self-contained:** every root cause below was already traced in the
> repo. An implementing model should NOT re-investigate — read the task, apply the
> fix, run only the named test(s) to confirm, then push.

---

## 0. Ground rules (READ FIRST — locked)

1. **Push after EVERY task.** Run `scripts/git/push.sh "type(scope): message (TASK N)"`
   immediately after each task's code change is complete. *Not pushing = project death.*
   - If push exits `3` → call `setup_github_environment` tool once, re-run the same push.
   - If push exits `2` (rebase conflict) → resolve preferring remote, re-run.
   - Work on branch **`genspark_ai_developer`** (use `scripts/git/push.sh -b genspark_ai_developer "..."` on the first push if not already on it).
2. **No full-suite runs / no full solution build.** Build/test ONLY the project or the
   specific test(s) the current task touches. Heavy fixes that were unavoidable still get
   pushed with a clear message.
3. **Targeted reading only.** The investigation is done; file+line pointers are given.
4. **Environment (one-time, if you must run a test):**
   - `scripts/sandbox/install-prereqs.sh --no-xvfb` (installs .NET SDK, restores)
   - `scripts/sandbox/install-postgres.sh` (local PG on `127.0.0.1:5433`, writes `.sandbox/pg/connection.env`)
   - `scripts/sandbox/server-test.sh --require-pg` (full server test on real PG — use the
     `--filter` passthrough for single tests, e.g. append
     `-- --filter FullyQualifiedName~HostSmokeTests`). Prefer single-test filters.
   - PG-gated tests read `ANIMORA_PG_TEST_CONNECTION` (sourced by `server-test.sh`).

**Status legend:** `[ ]` todo · `[~]` in progress · `[x]` done.

---

## 1. Phase understanding (the whole picture)

`S-TRACK-TEST-BUILD-ISSUES.md` is a **record-only** report. Nothing in it is fixed yet.
It records a `server-test.sh --all --require-pg` run against **real PostgreSQL 17**:

- **Build:** 0 errors, **26 warnings** (src) + **12 warnings** (test project). Not clean.
- **`Animora.Server.Tests`:** 652 pass / **107 fail** / 759 total.
- **`Animora.Persistence.Tests`** (SQLite client, pulled in by `--all`, *not* strictly Track S): 114/120, **6 fail**.
- **`Animora.Sync.Tests`:** 1/1 pass.

The 107 server failures are grouped A–G by symptom. An earlier report
(`SERVER-TEST-REPORT.md`, 251 failures) had three mega-causes (pending-model-changes,
null service provider, pool budget >12). Those were **already fixed** (git log:
TASK 1 pool cap, TASK 2 null RequestServices guard, TASK 4 pending-model tolerance,
TASK 3/5 source-scan + monitor). The **107 remaining** are the next layer and are what
THIS plan addresses. The 6 `Animora.Persistence.Tests` failures (groups F + part of G)
are client-side SQLite and are addressed last / optionally (see Task 9).

**Scope of "done":** all `Animora.Server.Tests` green + 0 build warnings on the server
build. `Animora.Persistence.Tests` (SQLite) fixes are included as Task 9 (lower priority,
still pushed) because the report lists them, but they are client-track and may be
split off if they prove large.

---

## 2. Task list

> Order is chosen so the biggest/root-shared fixes land first. Each task names the
> failing tests it clears (from the report), the root cause (already traced), the exact
> file(s), the fix, and the single verification command.

---

### [x] TASK 1 — Group A: `ArgumentNullException (Parameter 'Host')` from `WebApplicationFactory<Program>` (34 tests)

**Failing tests (all boot the real host via `WebApplicationFactory<Program>`):**
`Authorization/*` (AuthorizationTests, CapabilitySnapshotTests, DeviceEndpointsTests,
KeysEndpointTests, PlaneOrderTests, TenantContextMiddlewareTests),
`HostSmokeTests.*` (4), `Identity/AuthEndpointsTests.*` (6).

**Shared entry points:**
- `tests/Animora.Server.Tests/Authorization/AuthorizationTestKit.cs` → `AuthzHost` builds
  `new WebApplicationFactory<Program>().WithWebHostBuilder(...)` (lines 165–200).
- `tests/Animora.Server.Tests/HostSmokeTests.cs` → `CreateFactory` (lines 32–63).
- `tests/Animora.Server.Tests/Identity/AuthEndpointsTests.cs` → same factory pattern.
- Host under test: `src/Animora.Server/Program.cs` (top-level statements;
  `public partial class Program;` at line 227 exposes it to the factory).

**Root cause (traced; confirm with ONE run):** the exception `Value cannot be null.
(Parameter 'Host')` is thrown during host startup under `WebApplicationFactory`. It is
NOT thrown by the previously-fixed causes (pool budget / pending model / null RequestServices)
— those are cleared. The remaining trigger is in `Program.cs` startup wiring that the
`TestServer` cannot satisfy. The two highest-probability causes, in order:
  1. **`app.MapStaticAssets()` + Blazor `MapRazorComponents<App>()`** (Program.cs:175,178).
     Under `WebApplicationFactory` the static-web-assets manifest / Blazor host can
     resolve a null `Host`/address. Fix pattern: in the test factory, set the content
     root and/or guard the static-assets map, OR have `Program.cs` skip
     `MapStaticAssets()` when there is no manifest (`app.Environment` test flag).
  2. **`builder.WebHost.ConfigureKestrel(...)`** (Program.cs:65) running while
     `UseTestServer()`/`WebApplicationFactory` replaces the server — Kestrel profile
     application touching a null host feature.

**Fix (do in this order, stop when green):**
  1. Reproduce with exactly one test to capture the real stack frame:
     `server-test.sh --require-pg -- --filter FullyQualifiedName~HostSmokeTests.Live_Returns200_WithoutDependencies`.
     The top non-framework frame names the offending `Program.cs` line.
  2. If the frame is `MapStaticAssets`/Blazor: wrap the static-asset + Razor-component
     mapping so it is skipped when the static-web-assets endpoint manifest is absent
     (test host). Keep production behavior identical. Prefer guarding in `Program.cs`
     over per-test hacks so ALL 34 tests are fixed at once.
  3. If the frame is `ConfigureKestrel`: make `KestrelProfile.Apply` a no-op / tolerant
     when the server is the TestServer (feature/host is null).
  4. Re-run the single test; then run one Authorization test
     (`...AuthorizationTests.RoleClaimInToken_IsNeverTrusted`) and one AuthEndpoints test
     to confirm the shared host now boots.

> **Done:** real root cause was `NpgsqlDataSourceBuilder.Build()` rejecting the empty
> `Database:ConnectionString` (eager data-source resolution by `SnapshotAgeMonitor`). `BuildDataSource`
> now substitutes a reserved never-resolvable host (`*.invalid`) so the host boots and probes report
> not-ready. Follow-ups fixed in the same task: HostSmoke stubs drop optional DB-backed probes and the
> missing-data-root test uses an uncreatable path (log sink re-creates `<root>/logs`); PlaneOrder test
> moved to an entitlement-gated route (`/files/{id}`, S16 exempts `/auth`); 7a schema-head literals →
> `115_Files`; 7b permission-table/fail-closed tests updated to the seeded table. 111/111 green.

**Why this is first:** 34 tests share one root; fixing the host unblocks the largest group.

**Verify:** `server-test.sh --require-pg -- --filter "FullyQualifiedName~HostSmokeTests|FullyQualifiedName~Authorization.AuthorizationTests|FullyQualifiedName~Identity.AuthEndpointsTests"` → all pass.

**Push:** `scripts/git/push.sh "fix(server): boot test host under WebApplicationFactory (TASK 1 / group A)"`

---

### [x] TASK 2 — Group B: `NullReferenceException` in S09/S10 handler-intake tests (28 tests)

**Failing tests:** `HandlersS09/SalesCashierIntakeTests.Settles_*` (16) and
`HandlersS10/MoneyStockIntakeTests.Settles_*` (12). Note: the `Settles_Create*` cases that
build a brand-new aggregate (e.g. `CreateBlankInvoiceCommand`, `CreateInvoiceFromHandoffCommand`)
are **not** in the failing list — only commands that operate on an existing seeded entity fail.

**Entry points:**
- Test driver: `tests/Animora.Server.Tests/HandlersS09/SalesCashierIntakeTests.cs`
  `SettlesAsync` (lines 37–52) → `f.Clinical.Intake.CreateService(f.Catalog, f.Clinical.Dispatcher)`.
- Fixture: `tests/Animora.Server.Tests/HandlersS09/SalesCashierHandlerFixture.cs`
  and `HandlersS10/MoneyStockHandlerFixture.cs`.
- Intake wiring: `tests/Animora.Server.Tests/Commands/CommandTestSupport.cs`
  `IntakeTestFixture.CreateService` (lines 59–64) — note it passes a `DefaultHttpContext`,
  and leaves `financialEvents`, `concurrency`, `effectIds`, `execution` **null**.
- Service under test: `src/Animora.Server/Commands/CommandIntakeService.cs`
  (`SubmitAsync`, `financialEvents` is nullable and guarded at line 160; `concurrency`
  guarded at 143; metrics guarded at 42).

**Root cause (traced; confirm with ONE run):** S07/S08 intake tests use the SAME `CreateService`
plumbing and PASS, so the NRE is NOT in `CommandIntakeService` null-guards. It is in the
**S09/S10 handler execution path over the fixture ports** — a sales/money fixture port
returns `null` for an existing-entity lookup that a non-reject command then dereferences.
Most likely candidates (check first):
  - `SalesInvoiceRepository`/`FixtureInsurerRepository`/`FixtureProductRepository` in
    `SalesCashierHandlerFixture.cs` / `SalesFixturePorts.cs` / `CashFixturePorts.cs`.
  - `MoneyStockFixturePorts.cs` for S10 (cheque / ledger / period / recurring-expense ports).
  - A handler resolving `IServiceRateQueries`/`ISalesPostingData`/`ICashSessionContext`
    whose fake returns null for the seeded id.

**Fix:**
  1. Reproduce one: `... --filter FullyQualifiedName~SalesCashierIntakeTests.Settles_PostInvoiceCommand`.
     The stack frame names the handler + the null port method.
  2. Make the corresponding fixture port return the seeded aggregate (the fixture already
     seeds `Invoice`, `Insurer`, `Product`, `CashSession` — the port's `Find/Require` must
     return them for the non-reject id). This is a **test-fixture** fix, not product, unless
     the frame points into `src/` (then fix the handler's null handling).
  3. Confirm the two Settles cases that currently pass still pass (no regression on
     `Settles_CreateBlankInvoiceCommand`).

> **Done:** the NRE was `failure.Value!` on a *failed* Result — the reject-case commands used the
> literal id `"missing"`, which is not a ULID, so `CommandEnvelopeValidator` refused the envelope
> ("malformed envelope") before the handler ran. Fixtures now use a well-formed unknown ULID
> (S07 pattern) and the test asserts the submit succeeded with a readable message. 28/28 cleared;
> the 4 cheque cases are tracked under 7b.

**Verify:** `... --filter "FullyQualifiedName~SalesCashierIntakeTests|FullyQualifiedName~MoneyStockIntakeTests"` → all pass.

**Push:** `scripts/git/push.sh "fix(server-tests): S09/S10 intake fixture ports return seeded aggregates (TASK 2 / group B)"`

---

### [ ] TASK 3 — Group C: EF `ManyServiceProvidersCreatedWarning` promoted to error (14 tests)

**Failing tests:** `Admin/AdminEndToEndTests` (2), `Persistence/TenantIsolationTests` (6),
`Subscriptions/SubscriptionPersistenceTests` (6).

**Entry points:**
- Product DI already avoids per-request internal providers on purpose — see
  `src/Animora.Persistence.PostgreSql/DependencyInjection/PersistenceServiceCollectionExtensions.cs:55–63`
  (interceptors registered as `IInterceptor` from the app provider to avoid the unbounded cache).
- So the warning is triggered by the **tests**: each `WebApplicationFactory` /
  per-test `DbContextOptionsBuilder` builds a fresh EF internal service provider; running
  >20 within one class crosses the threshold and EF, in test config, promotes the warning
  to an exception.

**Root cause:** the server tests do not pin/share EF's internal service provider nor
suppress `ManyServiceProvidersCreatedWarning`, and the affected classes spin up >20
contexts/hosts.

**Fix (prefer the least invasive, test-only change):**
  1. For the raw-context classes (`TenantIsolationTests`, `SubscriptionPersistenceTests`)
     locate where they build `DbContextOptionsBuilder<AnimoraServerDbContext>` (search:
     `grep -rn "DbContextOptionsBuilder\|UseNpgsql" tests/Animora.Server.Tests/Persistence tests/Animora.Server.Tests/Subscriptions`).
     Add `.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))`
     **OR** share a single cached `IServiceProvider` via `.UseInternalServiceProvider(...)`
     / `.EnableServiceProviderCaching(true)` across the class's contexts. Ignoring the
     warning is the simplest and matches the fact that the many-providers situation is a
     test artifact, not a product defect.
  2. For `AdminEndToEndTests` (host-based) apply the same warning-ignore inside the test
     factory's `ConfigureServices` where it configures the DbContext, or reuse the shared
     factory instead of a new one per test.
  3. Keep a shared test helper if the pattern repeats (e.g. a `TestDbWarnings.Ignore` extension)
     so future PG tests inherit it.

**Verify:** `... --filter "FullyQualifiedName~TenantIsolationTests|FullyQualifiedName~SubscriptionPersistenceTests|FullyQualifiedName~AdminEndToEndTests"` → pass (note: some AdminEndToEndTests also depend on TASK 5/6 — re-check after those).

**Push:** `scripts/git/push.sh "test(server): suppress EF ManyServiceProviders warning in PG tests (TASK 3 / group C)"`

---

### [ ] TASK 4 — Group D: `PostgresException P0001: Missing tenant feed state` (6 tests)

**Failing tests:** `HandlersS07/S07EndToEndTests.CommandsRoundTrip_ThroughIntake_WithTenantIsolation`,
`HandlersS08/S08ConcurrencyTests.{TwoDevices_LastRoomPlace_OneWins,TwoDevices_SameSlot_OneWins}`,
`HandlersS08/S08EndToEndTests.*`, `HandlersS09/S09EndToEndTests.*`, `HandlersS10/S10EndToEndTests.*`.

**Entry points:**
- Source of the error: `src/Animora.Persistence.PostgreSql/Replication/JournalSqlTemplates.cs:31,59`
  (`RAISE EXCEPTION 'Missing tenant feed state'`) and
  `src/Animora.Persistence.PostgreSql/Replication/FeedEpochService.cs:18`
  (`throw ... "Missing tenant feed state."`). The change-journal trigger that fires on
  every tenant-scoped write requires a pre-seeded **tenant feed-state row**.
- Harnesses that must seed it: `tests/Animora.Server.Tests/HandlersS07/S07PostgreSqlTests.cs`
  (migrates to `20261009160000_104_CommandIntake`), and the per-handler
  `PgCommandHarness.cs` in `HandlersS08/S09/S10` + the E2E test bodies.

**Root cause:** the `*EndToEndTests.CommandsRoundTrip` and `S08ConcurrencyTests` create a
tenant and run commands through the real intake against real PG, but never insert the
tenant's feed-state row that migration `109_ChangeJournal` / the journal trigger expects.
The first write then trips the trigger.

**Fix:**
  1. Find the existing seeding helper (search:
     `grep -rn "feed state\|FeedEpoch\|feed_state\|InitializeFeed\|SeedFeed\|change journal" tests/Animora.Server.Tests src/Animora.Persistence.PostgreSql/Replication`).
     There is almost certainly an initializer (`FeedEpochService.EnsureAsync` or a migration
     seed) that the passing PG tests call and these E2E/concurrency tests omit.
  2. In the shared E2E harness (`PgCommandHarness` for each of S08/S09/S10 and the S07 E2E
     setup), after creating the tenant and before submitting commands, seed the feed-state
     row for the tenant (call the same initializer the working path uses, or `INSERT` the
     row the trigger reads). Put it in one shared place so all 6 tests inherit it.
  3. Keep tenant isolation: seed per-tenant for both tenants the isolation tests create.

**Verify:** `... --filter "FullyQualifiedName~S07EndToEndTests|FullyQualifiedName~S08EndToEndTests|FullyQualifiedName~S08ConcurrencyTests|FullyQualifiedName~S09EndToEndTests|FullyQualifiedName~S10EndToEndTests"` → pass.

**Push:** `scripts/git/push.sh "test(server): seed tenant feed-state before E2E command round-trips (TASK 4 / group D)"`

---

### [ ] TASK 5 — Group E: `CryptographicException: payload invalid` in Licensing persistence (4 tests)

**Failing tests:** `Licensing/LicensePersistenceTests.{ConcurrentRenew_NoDuplicateLive,
CrossTenant_CannotReadOrRevoke, DeviceLimit_Mismatch_AndRevokedGrant_AreRejected,
RevokedLicence_BlocksCommandsAt_ServerGate}`.

**Entry points:**
- `tests/Animora.Server.Tests/Licensing/LicensePersistenceTests.cs:30`
  `private readonly EphemeralDataProtectionProvider _protection = new();`
  Used by `LicenseKeyManager(store, _protection, _clock, _options)` in `CreateTenantWithKey`
  (~line 46) and again in `Issue(...)` (~line 72), and the license private key is
  protected/unprotected with Data Protection.
- Product: `src/Animora.Server.Licensing/*` (LicenseKeyManager / LicenseIssuer) and
  `LicenseStore`.

**Root cause:** "The payload was invalid" = Protect and Unprotect used **different** Data
Protection keys. With `EphemeralDataProtectionProvider`, each `new()` generates a brand-new
in-memory key ring. If any failing path creates a *second* provider (or a second `LicenseKeyManager`
over a differently-seeded provider, or the license key row was protected in a prior test
run / different provider instance and then read back), unprotect fails. The passing license
tests use the single `_protection` field consistently; the 4 failing ones likely protect in
one manager instance and unprotect in another that doesn't share the same ephemeral ring, OR
they persist the protected key to PG and a later instance with a fresh ephemeral ring cannot
read it.

**Fix:**
  1. Reproduce one: `... --filter FullyQualifiedName~LicensePersistenceTests.CrossTenant_CannotReadOrRevoke`.
     Identify the Protect vs Unprotect call sites (the stack shows which manager/method).
  2. Ensure a **single, shared** Data Protection provider (one ephemeral ring, or a
     deterministic test key) is used for BOTH the key-bootstrap (`EnsureAsync`) and every
     subsequent `LicenseKeyManager`/`LicenseIssuer` in these 4 tests. The field `_protection`
     already exists — verify all 4 failing paths route through it and don't `new()` another
     provider internally.
  3. If `LicenseKeyManager.EnsureAsync` persists the protected private key to PG and later
     reads it with a new ephemeral ring, make the test reuse the SAME manager/provider for the
     read, or use a fixed (non-ephemeral) test protector so the payload survives re-instantiation.

**Verify:** `... --filter FullyQualifiedName~LicensePersistenceTests` → pass.

**Push:** `scripts/git/push.sh "test(server): share DataProtection provider across licensing persistence tests (TASK 5 / group E)"`

---

### [ ] TASK 6 — Group G (product bug): `JournalEntrys` → `JournalEntries` table-name mismatch

**Failing test:** `HandlersS10/MoneyStockModelTests.MoneyStockTable_HasTenantIndexesFiltersAndUuid`
(Expected `"JournalEntries"`, Actual `"JournalEntrys"` at pos 11).

**Root cause (confirmed by grep — real product defect):**
- `src/Animora.Persistence.PostgreSql/Configurations/JournalEntryConfiguration.cs:12`
  maps `builder.ToTable("JournalEntrys", "public")` — **misspelled** (naive pluralization).
- But migration `src/Animora.Persistence.PostgreSql/Migrations/Server/20261010100000_108_MoneyStock.cs:178`
  creates the table as `"JournalEntries"` (and its indexes use `IX_JournalEntries_*`), and the
  Designer snapshot (`...108_MoneyStock.Designer.cs:1602`) says `"JournalEntries"`.
- So the EF model (config) says `JournalEntrys` while the DB/migration says `JournalEntries`
  → model↔DB mismatch; the test asserts the correct `JournalEntries`.

**Fix:** change `JournalEntryConfiguration.cs:12` from `"JournalEntrys"` to `"JournalEntries"`.
This aligns the model with the already-correct migration + snapshot. **Do NOT** regenerate the
migration (the migration is already correct). After the edit, the model matches the snapshot, so
the pending-model-changes check stays satisfied (and TASK 4's tolerance from a prior commit
remains).

**Verify:** `... --filter FullyQualifiedName~MoneyStockModelTests.MoneyStockTable_HasTenantIndexesFiltersAndUuid` → pass.
Also re-run one migration-apply test (`... --filter FullyQualifiedName~IdentityMigrationTests`) to
ensure no new pending-model drift.

**Push:** `scripts/git/push.sh "fix(persistence): map JournalEntry to JournalEntries table (TASK 6 / group G)"`

---

### [ ] TASK 7 — Group G (stale test expectations): schema-head + misc assertion mismatches

These are independent assertion fixes. Group by which side is wrong (traced below). Fix each,
then push once at the end of the task (they're small and related: "test expectation corrections").

**7a. Schema head `104_CommandIntake` → `115_Files` (2 tests, TEST side is stale).**
- `src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs:10` already says
  `ExpectedHead = "115_Files"` (correct — `115_Files` is the newest migration).
- Stale assertions: `tests/Animora.Server.Tests/Authorization/AuthorizationPgTests.cs:68`
  and `tests/Animora.Server.Tests/Identity/IdentityMigrationTests.cs:83` hardcode
  `Assert.Equal("104_CommandIntake", ServerSchemaVersion.ExpectedHead)`.
- Fix: change both literals to `"115_Files"`. (Prefer asserting against the constant, but at
  minimum update the literal to match.)
- Note `tests/Animora.Server.Tests/HandlersS07/S07PostgreSqlTests.cs:35` migrates *up to*
  `20261009160000_104_CommandIntake` on purpose (partial migration for S07) — **leave it**.

**7b. The remaining individual asserts in group G** — triage each with ONE run, then fix the
wrong side (expectation vs behavior). For each, the report's Expected/Actual tells you the gap:
  - `Authorization.AuthorizationTests.CommandAuthorizer_UnmappedCommand_IsDenied_FailClosed`
    (Expected True/Actual False) and
    `...CommandPermissionTable_StartsEmpty_AndUnmappedLookupIsNull` (collection not empty:
    contains real mappings like `sales.invoice.insurance.set`): the permission table is now
    **seeded** with command→permission rows, so the "starts empty" invariant is outdated.
    Decide: either the table should start empty (product regression) or the test is stale given
    S09/S10 seeding. Given S07–S10 added real permission rows, update the tests to assert the
    *expected seeded* shape (fail-closed still holds for genuinely unmapped commands). Confirm
    `fail-closed` by checking `src/Animora.Server/Authorization/` command-permission source.
  - `HandlersS07.ClinicalIntakeTests.Settles_CreateWalkInCommand` (Expected `WALKIN_PHONE_REQUIRED`
    / Actual `VALIDATION_FAILED`): the reject case builds a walk-in with empty phone
    (`ClinicalHandlerFixture.Command`, line 102). The handler/validator now returns the generic
    `VALIDATION_FAILED` before the specific `WALKIN_PHONE_REQUIRED`. Fix the validation ordering
    in the walk-in handler/validator (`src/` — search `WALKIN_PHONE_REQUIRED`) so the specific
    code wins, OR update the test if the generic code is the intended contract. Prefer restoring
    the specific code (more precise error = better UX).
  - `HandlersS07.ServerS07RederiveTests.Occurrence_IsIdempotent_AndCatalogCodeIsUnique`
    (Expected `TREATMENT_OCCURRENCE_ALREADY_DONE` / Actual `TREATMENT_PLAN_NOT_ACTIVE`): the
    rederive path marks the plan inactive before the idempotent re-mark, so the plan-state check
    short-circuits the occurrence-state check. Fix precondition ordering in the treatment
    occurrence handler (search `TREATMENT_OCCURRENCE_ALREADY_DONE`).
  - `HandlersS10.HandlerCoverageTests.EveryS10CommandType_HasHandler_CatalogEntry_AndPermissionRow`
    (Expected True/Actual False): an S10 command type is missing a handler, catalog entry, or
    permission row. Enumerate the gap (the test can be made to print the missing type) and add
    the missing registration in `src/Animora.Server/Handlers/ServerHandlerModuleS10.*` and/or the
    command-permission table. This is a **product completeness** fix.
  - Cheque settles (`HandlersS10.MoneyStockIntakeTests.Settles_{RegisterCheque, TransitionCheque,
    UpdateChequeMetadata, DeleteChequeDraft}Command`, Expected Accepted / Actual Rejected): cheque
    commands are rejected — likely a missing cheque fixture port seed or a missing handler/permission
    (overlaps with the S10 coverage gap above). Fix via `MoneyStockHandlerFixture` /
    `MoneyStockFixturePorts.cs` seeding + the S10 registration. May be cleared by TASK 2 + the
    coverage fix; re-check.
  - `Outbox.OutboxClaimTests.Redelivery_ConsumerEffectRunsOnce` (Assert.Single, empty): the
    redelivery consumer did not record its single effect — inspect
    `tests/Animora.Server.Tests/Outbox/` + `src/Animora.Persistence.PostgreSql/.../Outbox*`.
    Likely downstream of a migration/seed; re-check after TASK 4.
  - `Subscriptions.SubscriptionPersistenceTests.CreateTenantWithSubscription_OneTransaction_RollsBackTogether`
    (Expected 1 / Actual 0): re-check after TASK 3 (was failing under the ManyServiceProviders
    error). If still failing, the rollback assertion needs the seeded row — trace then.

> **Working method for 7b:** run each named test once, read the single stack/assert, fix the
> wrong side, re-run that test. Do NOT run the whole class repeatedly. Several of these
> auto-clear once TASK 3/4 land — verify before editing.

**Verify:** each named test green individually; then one combined filter run across the
Authorization + HandlersS07 + HandlersS10 + Outbox + Subscriptions names touched.

**Push:** `scripts/git/push.sh "fix(server): correct schema-head + command permission/validation expectations (TASK 7 / group G)"`
> If 7b produces a large/heavy fix (e.g. adding S10 handler + permission wiring), push that
> piece separately with its own clear message before continuing.

---

### [ ] TASK 8 — Build warnings → clean build (0 warnings)

**8a. Source — 26 × `CS9107` (product, `src/Animora.Persistence.PostgreSql/Repositories/*Repository.cs`).**
- Pattern (e.g. `WeightEntryRepository.cs:8`):
  `public sealed class WeightEntryRepository(AnimoraServerDbContext db) : TenantRepository<WeightEntry>(db), IWeightEntryRepository
   { public new void Add(WeightEntry entry) => db.Set<WeightEntry>().Add(entry); }`
  The primary-ctor param `db` is passed to the base **and** captured by the `Add` method →
  CS9107.
- Base `TenantRepository<T>` (`Repositories/TenantRepository.cs`) already exposes a protected
  `DbSet<T> Set` (tenant+soft-delete filtered). **Fix:** in each repo's method bodies, replace
  `db.Set<T>()` / `db....` usages with the inherited `Set` (and any other inherited protected
  member) so the primary-ctor param is no longer captured — then CS9107 disappears without
  suppression. For `Add`, `Set.Add(entry)` is equivalent.
- Affected files (all `*Repository.cs` under that folder): Appointment, Boarding,
  BoardingService, CashSession, Cheque, Expense, Insurer, Invoice, Ledger, MedicalHistory,
  Owner, Payment, Period, Pet, ProductCategory, Product, Purchasing, ReceptionQueue,
  RecurringExpense, Room, StockMovementWriter, Supplier, TreatmentPlan, Vaccination, Visit,
  WeightEntry (26 total).
- Caveat: a few repos may legitimately need the raw context for something `TenantRepository`
  doesn't expose (e.g. `StockMovementWriter`, `Ledger`). If so, either (a) add a minimal
  protected accessor on the base, or (b) store the ctor param into a `private readonly` field
  of the derived class (assigning in a constructor body) — note a primary-ctor param captured
  into a field does NOT raise CS9107 only when it's not also both passed-to-base and captured;
  the clean route is prefer the base `Set`. Verify per-file that the warning is gone.

**8b. Test project — 12 warnings (`tests/Animora.Server.Tests`).**
- `CS0618` `Outbox/OutboxClaimSqlTests.cs:28` → replace `IReadOnlyEntityType.GetQueryFilter()`
  with `GetDeclaredQueryFilters()`.
- `xUnit2013` `Admin/AdminSourceTests.cs:48` → use `Assert.Single(...)` instead of `.Count` check.
- `xUnit2031` (Where→Single): `Admin/FileMismatchServiceTests.cs:51,60,60`,
  `Commands/CommandIntakeStoreTests.cs:30`, `Provisioning/ProvisioningIntentStoreTests.cs:54,57`,
  `Sync/BatchFreezerTests.cs:15`, `Sync/ContractsTests.cs:19` → rewrite
  `.Where(pred).Single()` as `Assert.Single(collection, pred)`.
- `xUnit2000` `Sync/PureFeedTests.cs:15,17` → put the constant
  `RetentionPolicy.JournalRetentionDays` as the **expected** (first) arg of `Assert.Equal`.

**Verify:** `dotnet build src/Animora.Persistence.PostgreSql --no-incremental` → 0 CS9107;
`dotnet build tests/Animora.Server.Tests --no-incremental` → 0 warnings. (Building these two
projects only — not the full solution.)

**Push:** `scripts/git/push.sh "refactor(persistence,tests): clear CS9107 + xUnit build warnings (TASK 8)"`
> If 8a touches many files in one sweep (likely 26 files), that's expected — push once with
> this message after the build is warning-free.

---

### [ ] TASK 9 (optional / client-track) — `Animora.Persistence.Tests` SQLite failures (6 tests)

> The report lists these under groups F and G but flags them as "not strictly Track S"
> (SQLite client persistence). Include only if the directive wants the whole report cleared;
> otherwise note them and skip. Still push if touched.

- **F (2):** `InvoiceNumberAllocatorTests.{RolledBackAllocation_IsNotCommitted,
  TwoHundredConcurrentAllocations_AreDistinct}` — `Debug.Fail`: "InvoiceNumberAllocator must
  run inside an ambient UnitOfWork transaction". The tests invoke the allocator outside an
  ambient UoW. Fix: wrap the test calls in the ambient UoW the allocator requires (search
  `InvoiceNumberAllocator` + `UnitOfWork` in `tests/Animora.Persistence.Tests`), or relax the
  `Debug.Fail` guard if the allocator should tolerate the rolled-back test path.
- **G (4, SQLite):** `FinanceProjectionIntegrityTests.SeededProjections_MatchIndependentRebuild_AndRepairCorruption`
  (NotEmpty, empty), `SeedingTests.{SampleData_SeedsTheC01...Expected 12 Actual 0,
  Seeding_IsIdempotentAndComplete Expected 18 Actual 12}`,
  `UnitOfWorkTests.StoreFailure_IsMappedToAResult` (Expected `Infrastructure/STORE_UNAVAILABLE`
  Actual `Conflict/CONCURRENCY_CONFLICT`). These are client seeding / error-mapping drifts —
  trace each with one run, fix the wrong side.

**Verify:** `scripts/sandbox/test.sh` scoped to `Animora.Persistence.Tests` (or
`dotnet test tests/Animora.Persistence.Tests --filter ...`) per test.

**Push:** `scripts/git/push.sh "fix(persistence-client): invoice allocator UoW + seeding/error-map expectations (TASK 9)"`

---

### [ ] TASK 10 — Final verification pass (server track)

- Run the Track S server test the report used (single run, no `--all` to avoid the client
  SQLite tree unless TASK 9 was done):
  `scripts/sandbox/server-test.sh --require-pg`
- Confirm: **0 build warnings** (src + server tests) and **0 failed** in
  `Animora.Server.Tests`.
- If anything still red, fix narrowly and push with a precise message (heavy fixes get their
  own commit).

**Push:** `scripts/git/push.sh "test(server): Track S green — 0 warnings, all server tests pass (TASK 10)"`

---

### [ ] TASK 11 — Delete this phase file (closing task)

- Delete ONLY `backend-roadmap/S-TRACK-TEST-BUILD-ISSUES.md`.
  (Do **not** touch `SERVER-TEST-REPORT.md`, `ROADMAP.md`, `PROGRESS.md`, or any other
  roadmap file.)
- `git rm backend-roadmap/S-TRACK-TEST-BUILD-ISSUES.md`
- **Push:** `scripts/git/push.sh "chore(roadmap): remove resolved Track S test/build issues report (TASK 11)"`
- Then delete this plan file too is NOT required by the directive — the directive says only
  the file inside `Plan/` gets removed at the very end *as the closing task of the plan*. Per
  the user's instruction: the final step is deleting the file inside `Plan/` after completion.
  So after TASK 11, as the actual last action, `git rm Plan/<this-file>.md` and push:
  `scripts/git/push.sh "chore(plan): remove completed Track S fix plan"`.

---

## 3. Quick root-cause index (for fast recall)

| Group | Count | Root cause (traced) | Side to fix |
|---|---|---|---|
| A | 34 | `WebApplicationFactory<Program>` host fails to boot → `ArgumentNullException (Host)`; likely `MapStaticAssets`/Blazor or `ConfigureKestrel` under TestServer (`Program.cs:65,175,178`) | product/host wiring |
| B | 28 | S09/S10 intake NRE: a sales/money **fixture port** returns null for a seeded existing-entity id | test fixtures (`SalesCashier*`/`MoneyStock*FixturePorts`) |
| C | 14 | EF `ManyServiceProvidersCreatedWarning` promoted to error (tests build >20 internal providers) | tests (ignore warning / share provider) |
| D | 6 | Journal trigger `P0001 Missing tenant feed state` — E2E harness never seeds the tenant feed-state row (`JournalSqlTemplates.cs:31,59`, `FeedEpochService.cs:18`) | test E2E harness seeding |
| E | 4 | DataProtection payload invalid: Protect/Unprotect across different ephemeral rings in licensing tests (`LicensePersistenceTests.cs:30`) | tests (share one provider) |
| F | 2 | SQLite `InvoiceNumberAllocator` run outside ambient UoW (client track) | tests (wrap in UoW) |
| G | ~22 | Mixed: product bug `JournalEntrys`→`JournalEntries` (`JournalEntryConfiguration.cs:12`); stale schema-head `104`→`115` in 2 tests; permission-table-now-seeded invariants; validation ordering (walk-in phone, treatment occurrence); S10 handler/permission coverage gap; downstream-of-C/D | mixed product+test |

## 4. Notes carried for the implementer
- `ServerSchemaVersion.ExpectedHead` is **already** `115_Files` and correct — do not change it;
  only the two stale test literals are wrong.
- `JournalEntryConfiguration.cs` is the single product typo (`JournalEntrys`); the migration and
  snapshot already say `JournalEntries`, so fixing the config aligns the model with the DB and
  will NOT create pending-model drift.
- Pending-model-changes, DB-pool-budget (cap 12), null-RequestServices, operator-context
  source-scan, and snapshot-age monitor were fixed in prior commits (git log TASK 1–5) — do not
  redo them.
- Prefer fixing the **test/fixture** side for A-adjacent, B, C, D, E (they are test-infrastructure
  gaps), and the **product** side only for the genuine defects in G (JournalEntries, S10 coverage,
  validation ordering) and whatever A's single run pinpoints.
- Push cadence is non-negotiable: one `scripts/git/push.sh "...(TASK N)"` per task.
