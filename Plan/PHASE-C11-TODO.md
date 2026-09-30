# Phase C11 — Mock purge & offline acceptance · TODO

> Self-contained execution plan. A fresh session can implement C11 from this file
> **without re-reading the repo**. All discovered facts, exact signatures, paths,
> and gotchas are inlined below. Source of truth for scope:
> `backend-roadmap/phases/PHASE-C11-mock-purge-offline-acceptance.md`.

---

## 0. Locked rules for this session (READ FIRST)

1. **PUSH AFTER EVERY TASK.** After each task below is complete, run:
   `scripts/git/push.sh "type(scope): message"` (branch is `main`). Not pushing = failure.
2. **No full test / no long build unless a task says so.** Prefer targeted builds
   (`scripts/sandbox/build.sh <project>` style is NOT available — build.sh only takes
   `fast|full|release|restore`). Use `dotnet build <csproj>` for targeted builds via
   the env loader (see §1.3). Only run `build.sh full` / `test.sh` at the final
   verification task (T12), or when a task explicitly requires it.
3. **If a heavy/large fix is written mid-task, push it immediately** with a clear message.
4. **Do NOT touch the roadmap phase file or PROGRESS.md until their dedicated tasks.**
   The very last task (T14) deletes only `Plan/PHASE-C11-TODO.md`.
5. Assert on **codes/ids/numbers only — never on Persian strings** (CONVENTIONS §9).

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done.

---

## 1. Environment & repo facts (discovered — trust these)

### 1.1 Branch / remote
- Current branch: **`main`**. Remote: `origin` → `github.com/Arian-Alijani/Petjan.git`.
- Push tool: `scripts/git/push.sh "msg"` → does `add -A → commit → fetch → rebase → push`.
  Exit 3 = no creds → call `setup_github_environment` tool once, re-run.

### 1.2 CRITICAL numbering gotcha — migration is **019**, not 018
- The phase text says "migration `018_SyncCursor`", **but `018` is already taken** by
  `20260929083425_018_UserPreferences`. The next free ordinal is **019**.
- **Create the SyncCursor migration as `019_SyncCursor`** (class `_019_SyncCursor`,
  file `..._019_SyncCursor.cs`). Everywhere the phase says "018_SyncCursor", read **019**.
  Record this deviation in PROGRESS.md (T13).

### 1.3 dotnet is not on bare PATH
- `which dotnet` fails in a raw shell. The sandbox scripts load it via
  `scripts/sandbox/_env.sh` (`source` it first). For a targeted build/migration:
  ```bash
  cd /home/user/webapp && source scripts/sandbox/_env.sh && dotnet build src/Animora.Persistence.Sqlite/Animora.Persistence.Sqlite.csproj -c Debug
  ```
  `require_dotnet` is defined in `_env.sh`. `_env.sh` also raises `DOTNET_GCHeapHardLimit`
  (~576 MiB) — needed so the Avalonia XAML compiler doesn't OOM.

### 1.4 Migration generation
- Migrations here are EF Core migrations committed as source (`Migrations/*.cs` +
  `*.Designer.cs` + `AnimoraLocalDbContextModelSnapshot.cs`). No custom script wraps
  `dotnet ef`. To generate: `dotnet ef migrations add 019_SyncCursor` against
  `src/Animora.Persistence.Sqlite` (design-time factory `LocalDbContextFactory` exists
  in `Context/`). **If `dotnet ef` tool is unavailable in the sandbox, hand-write the
  migration + snapshot delta** using `20260929083425_018_UserPreferences.cs` as the
  exact format template (shown in §3.1). The snapshot file
  `AnimoraLocalDbContextModelSnapshot.cs` must gain a `SyncCursor` entity block.

### 1.5 Sandbox script behaviour
- `build.sh full` → Debug build whole solution; success line, non-zero on error;
  prints `: error|: warning` grep on failure. Acceptance = 0 warnings/0 errors.
- `run.sh` → boots real app under Xvfb; prints `SMOKE RUN OK`. 143 exit is OK
  (SIGTERM). Networking-disabled variant proves R-B01 (offline).
- `test.sh` → `dotnet test Animora.sln`; prints `TESTS OK` only if all pass.
  Test projects (5): `Animora.Domain.Tests`, `Animora.Application.Tests`,
  `Animora.Persistence.Tests`, `Animora.Server.Tests`, `Animora.Sync.Tests`.
- `bench.sh` → current args: `[--no-build] [--seconds N] [--release] [--label L]`.
  Emits one `BENCH label=... ttfui_ms=... peak_rss_kb=... wall_ms=... alive=...` line.
  **No `offline` positional mode yet** — T9 adds it.

---

## 2. Key type signatures (inlined so you don't re-read source)

### `ISyncStatusService` — `src/Animora.Contracts/Sync/ISyncStatusService.cs`
```csharp
public enum SyncState { Idle, Syncing, Synced, Pending, Error, Offline }
public interface ISyncStatusService {
    SyncState State { get; }
    DateTimeOffset? LastSyncedAt { get; }
    event EventHandler? StateChanged;
    Task SyncNowAsync(CancellationToken ct = default);
}
```

### `PendingCommandSummary` / query — `src/Animora.Application/UseCases/Sync/PendingCommandSummaryQuery.cs`
```csharp
public sealed record PendingCommandSummary(
    int Total, int PendingSettlement, int Rejected,
    int Conflict, int NeedsAttention, DateTimeOffset? OldestCreatedUtc);
public sealed record PendingCommandSummaryQuery : IQuery<PendingCommandSummary>;
// Handler: PendingCommandSummaryQueryHandler(ICommandJournal journal) → journal.GetSummaryAsync(ct)
```
- `Total` > 0 ⇒ there are unsettled commands ⇒ `SyncState.Pending`.

### `LocalCommandProcessor` — `src/Animora.Application/Commands/LocalCommandProcessor.cs`
```csharp
public sealed record CommandProcessorPass(int Claimed,int Settled,int Deferred,int Failed,int SkippedUnsettledDependency);
public sealed class LocalCommandProcessor(ICommandJournal journal, ICommandTransport transport) {
    public async Task<Result<CommandProcessorPass>> RunPassAsync(CancellationToken ct = default);
}
```

### `ICommandTransport` / `NullCommandTransport` — `src/Animora.Application/Commands/`
- `NullCommandTransport.SubmitAsync` returns `SettlementOutcome.Deferred` → a real
  no-op pass; commands stay `PendingSettlement`. "No transport configured (offline)".

### `IClock` — `src/Animora.Domain/Time/IClock.cs`
```csharp
public interface IClock { DateTimeOffset UtcNow { get; } }
```

### `[LocalOnly]` — `src/Animora.Domain/Primitives/LocalOnlyAttribute.cs`
- Applied to an entity class; `ModelConventions.Apply(...)` (called in
  `AnimoraLocalDbContext.OnModelCreating`) collects `[LocalOnly]` types. Just tag the
  entity; no manual DbSet needed.

### DbContext registration — `src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs`
- `OnModelCreating` calls `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnimoraLocalDbContext).Assembly)`
  → **a new `SyncCursorConfiguration : IEntityTypeConfiguration<SyncCursor>` is auto-picked-up.**
  No `DbSet<SyncCursor>` property is required (but you MAY add one for query ergonomics).

### DI line to swap — `src/Animora.Desktop/App.axaml.cs`
- Line ~53: `using Animora.Desktop.Mocks;` → **delete this using**.
- Line ~346-348:
  ```csharp
  services.AddSingleton<ISyncStatusService, MockSyncStatusService>();
  ```
  → replace with `LocalSyncStatusService` (namespace `Animora.Client.Services.Sync`;
  add `using Animora.Client.Services.Sync;`).

### Test harness (reuse for the scenario) — `tests/Animora.Persistence.Tests/LocalStoreTestFixture.cs`
- `public sealed class LocalStoreTestFixture : IDisposable`
  - ctor builds a **temp-file SQLite** DB (`DatabasePath`), a `FakeClock`, a DI provider.
  - `FakeClock(DateTimeOffset now) : IClock { public DateTimeOffset Now {get;set;}; UtcNow => Now; }`
  - `await fixture.MigrateAsync()` → applies **all** migrations to the temp file.
  - `fixture.CreateScope()` → `IServiceScope`; `fixture.Services` → root provider.
  - `fixture.CreateContext(tenantId)` → a fresh `AnimoraLocalDbContext` on the same file
    (**use this for the kill-and-reopen step**: dispose one context, create another).
  - `fixture.TenantId`, `DefaultTenant = "T-LOCAL-DEFAULT"`.
  - `Animora.Application.Tests` already references `Animora.Persistence.Tests`
    (see `DashboardBudgetTests` using `Animora.Persistence.Tests.LocalStoreTestFixture`).
- **Reuse this fixture** for `OfflineClinicDayScenarioTests`: it is the sanctioned
  temp-file-DB-with-migrations harness. Drive time via `fixture.Clock.Now = ...`.

### `LayeringRulesTests` — `tests/Animora.Domain.Tests/LayeringRulesTests.cs`
- Existing facts assert referenced-assembly sets via `GetReferencedAssemblies()`.
- Helpers already present: `ReferencedAssembliesOf(asm)`, `ReferencesNameStartingWith(asm,prefix)`.
- Contracts assembly is loaded via `typeof(Animora.Contracts.Navigation.UserRole).Assembly`.
- Domain.Tests **already references** `Animora.Client.Services` and `Animora.Desktop`?
  → It references `Animora.Client.Services` (proven by the identity fact touching
  `AuthenticationService`). **Verify Domain.Tests references `Animora.Desktop`**; the new
  DI-audit + mock-purge facts must load the `Animora.Desktop` assembly. If the project
  reference is missing, add it to `tests/Animora.Domain.Tests/Animora.Domain.Tests.csproj`
  (record as a deviation). To reflect over the composition root you need a stable type in
  `Animora.Desktop` (e.g. `typeof(Animora.Desktop.App).Assembly`).

---

## 3. Task list

### [x] T0 — Baseline commit of the plan
> Baseline already committed on main (`a6c0533`); main is two commits ahead of the old AI branch.
> Work continues on `genspark_ai_developer` forked from main, with per-task push + PR updates.
- Ensure `Plan/PHASE-C11-TODO.md` exists (this file).
- **PUSH:** `scripts/git/push.sh "docs(plan): add C11 mock-purge/offline-acceptance TODO"`

---

### [x] T1 — `SyncCursor` entity + configuration + migration **019**  ✅ DONE
> Created `Entities/SyncCursor.cs` (`[LocalOnly]`, ULID, `TenantId`/`Scope`/`LastSyncedAtUtc`,
> `Create`/`MarkSynced`). `Configurations/Sync/SyncCursorConfiguration.cs` declares only key +
> unique `(TenantId,Scope)` index (`IX_SyncCursors_TenantId_Scope`) — **lengths/types are owned
> by `ModelConventions`** (Scope→400 ShortText, DateTimeOffset→INTEGER ticks; explicit
> `HasMaxLength` is overridden by the convention, so it was removed). Migration generated via
> `dotnet ef` as `20260929195619_019_SyncCursor` (+ Designer + snapshot). Verified: targeted
> build 0/0, `has-pending-model-changes` = none. **dotnet-ef was installed as a global tool at
> `/home/user/webapp/.sandbox/dotnet-home/.dotnet/tools` — add to PATH for later ef commands.**
**Goal:** a `[LocalOnly]` last-synced marker row (E03/E04 later extend it).

**T1.1 Entity** — create `src/Animora.Persistence.Sqlite/Entities/SyncCursor.cs`
- Pattern after `Entities/UserPreference.cs` (private ctor, ULID `Id` via
  `Animora.Domain.Primitives.IdGenerator.NewId()`, `static Create(...)`).
- Tag `[LocalOnly]` (`using Animora.Domain.Primitives;`).
- Minimum fields (keep it small — E03/E04 extend):
  - `string Id` (ULID, len 26)
  - `string TenantId` (len 26)
  - `DateTimeOffset? LastSyncedAtUtc` (nullable; null until first successful pass)
  - a stable scope key so there's exactly one cursor row per tenant, e.g.
    `string Scope` (len ≤ 64, default `"default"`).
- Method `void MarkSynced(DateTimeOffset whenUtc)` → sets `LastSyncedAtUtc`.
- XML-doc: one line noting **E03 extends this**; do not add sync-engine fields now.

**T1.2 Configuration** — create
`src/Animora.Persistence.Sqlite/Configurations/Sync/SyncCursorConfiguration.cs`
- Pattern after `Configurations/Diagnostics/UiDraftStateConfiguration.cs`:
  ```csharp
  public sealed class SyncCursorConfiguration : IEntityTypeConfiguration<SyncCursor> {
      public void Configure(EntityTypeBuilder<SyncCursor> b) {
          b.HasKey(e => e.Id);
          b.Property(e => e.TenantId).HasMaxLength(26).IsRequired();
          b.Property(e => e.Scope).HasMaxLength(64).IsRequired();
          b.HasIndex(e => new { e.TenantId, e.Scope }).IsUnique()
             .HasDatabaseName("IX_SyncCursors_TenantId_Scope");
      }
  }
  ```
- Auto-discovered by `ApplyConfigurationsFromAssembly`. `[LocalOnly]` handled by conventions.

**T1.3 Migration 019** — `src/Animora.Persistence.Sqlite/Migrations/`
- Preferred: `source scripts/sandbox/_env.sh && dotnet ef migrations add 019_SyncCursor \
  -p src/Animora.Persistence.Sqlite -s src/Animora.Persistence.Sqlite` (design-time factory
  `LocalDbContextFactory` exists). Verify the generated file is named `..._019_SyncCursor.cs`
  and the snapshot updated.
- Fallback (if `dotnet ef` missing): hand-write, using the **exact format** of
  `20260929083425_018_UserPreferences.cs`:
  ```csharp
  using Microsoft.EntityFrameworkCore.Migrations;
  #nullable disable
  namespace Animora.Persistence.Sqlite.Migrations {
      public partial class _019_SyncCursor : Migration {
          protected override void Up(MigrationBuilder mb) {
              mb.CreateTable(name: "SyncCursors", columns: table => new {
                  Id = table.Column<string>(type:"TEXT", maxLength:26, nullable:false),
                  TenantId = table.Column<string>(type:"TEXT", maxLength:26, nullable:false),
                  Scope = table.Column<string>(type:"TEXT", maxLength:64, nullable:false),
                  LastSyncedAtUtc = table.Column<DateTimeOffset>(type:"TEXT", nullable:true)
              }, constraints: t => { t.PrimaryKey("PK_SyncCursors", x => x.Id); });
              mb.CreateIndex(name:"IX_SyncCursors_TenantId_Scope", table:"SyncCursors",
                  columns:new[]{"TenantId","Scope"}, unique:true);
          }
          protected override void Down(MigrationBuilder mb) => mb.DropTable(name:"SyncCursors");
      }
  }
  ```
  Also add the matching entity block to `AnimoraLocalDbContextModelSnapshot.cs` (copy the
  `UserPreference` block, rename to `SyncCursor`/`SyncCursors`, adjust columns + the
  unique index). A `.Designer.cs` twin mirroring the snapshot's entity set is expected
  by EF; generate via `dotnet ef` when possible.
- **Timestamp prefix:** use a value strictly greater than `20260929083425` (018). Any later
  UTC stamp is fine; if hand-writing, pick e.g. `20260930000000_019_SyncCursor`.

**T1.4 Build check (targeted):**
`source scripts/sandbox/_env.sh && dotnet build src/Animora.Persistence.Sqlite/Animora.Persistence.Sqlite.csproj -c Debug`
→ 0 errors/0 warnings.

- **PUSH:** `scripts/git/push.sh "feat(persistence): add SyncCursor entity + config + migration 019"`

---

### [ ] T2 — `LocalSyncStatusService` (real, server-free) + swap DI + delete mock
**Goal:** the last mock (`MockSyncStatusService`) is replaced by a real
`ISyncStatusService` implementation living in `Animora.Client.Services`.

**T2.1 Create** `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs`
- Namespace `Animora.Client.Services.Sync`. `public sealed class LocalSyncStatusService : ISyncStatusService`.
- Dependencies (constructor-inject what the composition root can provide — verify these are
  registered in `App.axaml.cs`; they are used by C07/C08 already):
  - a way to run `PendingCommandSummaryQuery` — inject the app's query dispatcher
    (`IQueryDispatcher`/`IDispatcher` — confirm the exact contract name used elsewhere in
    `Animora.Client.Services`, e.g. how `PendingCommandSummaryQuery` is invoked by the
    cashier/sales services) OR inject `ICommandJournal` directly if Client.Services is
    allowed to (check existing Client.Services deps; prefer the dispatcher used by peers).
  - `LocalCommandProcessor` (registered? if not, register it in `App.axaml.cs`; it is a
    plain class `(ICommandJournal, ICommandTransport)`).
  - access to a `SyncCursor` row (via `AnimoraLocalDbContext` — but **Client.Services must
    not reference EF** per the layering audit! see gotcha below).
- **LAYERING GOTCHA (important):** T4's DI audit forbids `Animora.Desktop` referencing EF,
  but the phase requires the impl to read a `SyncCursor` *row*. `Animora.Client.Services`
  reading `AnimoraLocalDbContext` directly would pull EF into Client.Services. Check what
  peers do: business queries go through a **query object / repository port**, not EF in
  Client.Services. **Resolution:** read the cursor through the same query/dispatcher seam
  the rest of Client.Services uses (add a tiny `SyncCursorQuery` in Application if none
  exists), OR expose `LastSyncedAt` through the existing pending-summary path. Keep
  Client.Services free of `Microsoft.EntityFrameworkCore`. Confirm by grepping existing
  Client.Services files for `EntityFrameworkCore` (should be none).
- **Behaviour (per phase design decision — implement exactly):**
  - `State`:
    - `SyncState.Offline` when **no `ICommandTransport` endpoint is configured** (today the
      registered transport is `NullCommandTransport`). Detect "no endpoint" — simplest
      correct rule for today: transport is `NullCommandTransport` (or a capability flag it
      exposes). Document the check.
    - `SyncState.Pending` when `PendingCommandSummaryQuery` returns `Total > 0`.
    - `SyncState.Synced` (with `LastSyncedAt` from the `SyncCursor` row) otherwise.
  - `LastSyncedAt` ← the cursor's `LastSyncedAtUtc`.
  - `SyncNowAsync` → set `Syncing`, run **one** `LocalCommandProcessor.RunPassAsync` against
    the configured transport (Null today = legit no-op that still updates the summary), then
    recompute state (update the cursor's `LastSyncedAtUtc` = clock now on a successful pass),
    raise `StateChanged`. On cancel → return to last settled state.
  - Raise `StateChanged` on every transition.
- Keep the class small and deterministic; no `Task.Delay` simulation (that was the mock).

**T2.2 Swap DI** in `src/Animora.Desktop/App.axaml.cs`
- Add `using Animora.Client.Services.Sync;`.
- Replace `services.AddSingleton<ISyncStatusService, MockSyncStatusService>();` with
  `services.AddSingleton<ISyncStatusService, LocalSyncStatusService>();`.
- Register `LocalCommandProcessor` if not already registered (needed by the new service).

**T2.3 Delete** `src/Animora.Desktop/Mocks/MockSyncStatusService.cs` (git rm).

**T2.4 Targeted build:** build `Animora.Client.Services` + `Animora.Desktop`.
- **PUSH:** `scripts/git/push.sh "feat(sync): replace MockSyncStatusService with real LocalSyncStatusService"`

---

### [ ] T3 — Remove `Mocks/` directory entirely + drop its `using`
**Goal:** `src/Animora.Desktop/Mocks/` **does not exist**; no `Mock` identifier in Desktop.
- After T2 deleted the only file, `git rm -r src/Animora.Desktop/Mocks` (or the empty dir).
- Remove `using Animora.Desktop.Mocks;` from `App.axaml.cs` (if not already gone in T2).
- Verify:
  `grep -R "Mock" src/Animora.Desktop --include=*.cs` → returns **nothing outside comments**.
  (Also check `.axaml` files if any reference `Mock`.)
- Targeted build of `Animora.Desktop`.
- **PUSH:** `scripts/git/push.sh "refactor(desktop): remove Mocks directory (Track C mock purge)"`

---

### [ ] T4 — Extend `LayeringRulesTests` with 3 machine-checkable facts
File: `tests/Animora.Domain.Tests/LayeringRulesTests.cs` (append facts; never weaken existing).

**Pre-req:** ensure `Animora.Domain.Tests.csproj` references `Animora.Desktop` (add if missing;
record deviation). Use `typeof(Animora.Desktop.App).Assembly` to load the composition root.

**Fact A — `NoMockNamespaceExists` (R-B34):**
- Assert no type in `Animora.Desktop` assembly has a namespace ending in `.Mocks`
  (`t.Namespace?.EndsWith(".Mocks")` false for all `GetTypes()`), AND
- Assert the composition-root source has no `Mock` identifier: since reading source at test
  time is brittle, assert instead that **no `Animora.Desktop` type name starts with `Mock`**
  and **no type lives in a `*.Mocks` namespace**. (The `grep` acceptance check covers the
  App.axaml.cs source-text side in T3.)

**Fact B — DI audit (one registration per Contracts interface):**
- Reflect over the `Animora.Contracts` assembly: every `public interface` (the DTO/seam
  contracts) has **exactly one** registration in the composition root, and the
  implementation type's assembly is **`Animora.Client.Services`**.
- **Allowed exceptions (implemented in `Animora.Desktop`)**: `INavigationService`,
  `IToastService`, `IDialogService`, `IRecordSheetPresenter`.
- How to enumerate registrations: build the same `IServiceCollection` the app builds. If
  `App.axaml.cs` has a public/internal `ConfigureServices(IServiceCollection)` seam, call it;
  if not, add a testable static that the app and the test both use (minimal refactor — record
  it). Then for each Contracts interface `i`: `services.Where(d => d.ServiceType == i)` count
  == 1 (unless `i` is one of the 4 exceptions or is intentionally unregistered — narrow the
  set to interfaces that are *meant* to be resolved: the phase says "every interface in
  Animora.Contracts"; if some Contracts interfaces are pure markers with no DI, scope the
  fact to those with a registration and assert the impl assembly + single-registration — but
  the phase wants full coverage, so first try full and relax only with a documented reason).
- Implementation-assembly check: `descriptor.ImplementationType?.Assembly.GetName().Name == "Animora.Client.Services"` (or the 4 UI exceptions in `Animora.Desktop`).
  Handle factory registrations (`ImplementationFactory != null`) — if used, resolve or
  special-case with a comment.

**Fact C — forbidden references in `Animora.Desktop`:**
- `Animora.Desktop` references **no** `Microsoft.EntityFrameworkCore`, `System.Data.Common`
  (`DbConnection`), `System.Net.Http` (`HttpClient`), or Datasync (`CommunityToolkit.Datasync`).
- Use `GetReferencedAssemblies()` on `typeof(Animora.Desktop.App).Assembly` +
  `ReferencesNameStartingWith` helper (already in the file).
- **If this fact fails**, the fix is to move the offending dependency out of Desktop **in this
  phase** and record it in PROGRESS.md (T13) — do NOT relax the test.

- Targeted build+run of just Domain.Tests:
  `source scripts/sandbox/_env.sh && dotnet test tests/Animora.Domain.Tests/Animora.Domain.Tests.csproj -c Debug --nologo -v minimal`
- **PUSH:** `scripts/git/push.sh "test(layering): mock-purge + DI-audit + forbidden-ref facts (R-B34/R-B10)"`

---

### [ ] T5 — `OfflineClinicDayScenarioTests` (14-step end-to-end)
File: `tests/Animora.Application.Tests/Scenarios/OfflineClinicDayScenarioTests.cs` (create
`Scenarios/` folder). Uses `Animora.Persistence.Tests.LocalStoreTestFixture` (temp-file DB +
`MigrateAsync` + `FakeClock`). Drive all "now" via `fixture.Clock.Now = ...`.

**Harness setup:** `using var fixture = new LocalStoreTestFixture(); await fixture.MigrateAsync();`
Resolve use-case handlers/services from `fixture.Services` / `fixture.CreateScope()`. Look at
existing use-case tests for the exact handler names & command DTOs per track:
- B03 first-run: `FirstRunSetupTests.cs`
- C01 patients: `PatientsUseCaseTests.cs`
- C04 scheduling/check-in: `AppointmentBookingUseCaseTests.cs`, reception `ReceptionQueueUseCaseTests.cs`
- C02 reception/admit/visit: `ReceptionQueueUseCaseTests.cs`, `VisitEncounterUseCaseTests.cs`
- C03 vitals/dx/Rx/complete: `VisitEncounterUseCaseTests.cs`, `VisitsTriageUseCaseTests.cs`
- C07 invoice from handoff + product line: `PostInvoiceCommandTests.cs`, `SalesCompensationTests.cs`
- C08 cash session + payments: `CashSessionUseCaseTests.cs`, `CashierReusesSalesTests.cs`
- C06 stock snapshot/movement: `InventoryUseCaseTests.cs`
- C09 ledger: `LedgerSingleWriterTests.cs`, `PeriodCloseTests.cs`
- C10 activity feed: `ActivityFeedTests.cs`
- C05 boarding: `BoardingUseCaseTests.cs`
> Read ONLY the specific handler/DTO names you need from these; don't read whole files.

**The 14 steps (assert on codes/ids/numbers only):**
1. First-run setup → tenant + owner/admin (B03).
2. Register owner + pet (C01).
3. Book appointment for **tomorrow** (advance `Clock`), then check it in (C04).
4. Ticket appears in reception queue; admit → enter visit (C02).
5. Record vitals + a diagnosis + **two Rx lines**; complete the visit (C03).
6. Handoff appears in "ready to bill"; create invoice from it; add **one product line** (C07).
7. Open a cash session; take a **partial** cash payment, then the rest (C08, C07).
8. Assert: invoice `Paid`; **two `Payment` rows**; **one `PendingCommand` per business
   mutation**, all in `LocallyPosted`/`PendingSettlement`.
9. Assert: stock for the sold product dropped by **exactly** the sold qty — C06 snapshot AND
   the movement recomputation agree.
10. Assert: ledger has balanced entries; revenue total == invoice total (C09).
11. Assert: activity feed contains the expected **9 events in order** (C10).
12. Close the cash session with an exact count → **variance 0**.
13. **Kill-and-reopen:** dispose the context, reopen the SAME DB file via
    `fixture.CreateContext(fixture.TenantId)`, re-assert **every number above** (R-B09).
14. **Boarding variant:** create a stay, add items, check out, invoice from the boarding
    handoff (C05); assert the same ledger/stock/feed consequences.

**Notes:**
- One continuous flow, single `IClock` (the fixture's `FakeClock`).
- Prefer `[Fact]` with clearly-named local step helpers, or split into `[Fact]` per major
  assertion group sharing a seeded fixture — but the reopen step needs the same file, so keep
  it in ONE test method (or an `IClassFixture` that persists the file across methods).
- If a required use-case handler isn't registered in the fixture's provider, register the
  needed Application use cases in the test setup (mirror how other Application.Tests do it).

- Targeted test run of just this file's project (Application.Tests) — may be slow; if too
  slow, run the single test by filter:
  `dotnet test tests/Animora.Application.Tests/Animora.Application.Tests.csproj --filter FullyQualifiedName~OfflineClinicDayScenario -c Debug --nologo -v minimal`
- **PUSH:** `scripts/git/push.sh "test(scenario): OfflineClinicDay 14-step end-to-end (offline acceptance)"`

---

### [ ] T6 — `StartupBudgetTests` (no blocking work before first frame)
File: `tests/Animora.Application.Tests/StartupBudgetTests.cs` (PERFORMANCE.md §12).
- Assert `App.axaml.cs` startup does **not** `await` migration, seeding, or projection rebuild
  before the first frame — that work runs behind `LocalStoreStartup` + `IStoreReadinessGate`.
- Reflection/source approach: the cleanest machine check is to assert the composition/startup
  path defers store work to the gate. Concretely:
  - Assert `App` startup registers/uses `IStoreReadinessGate` (present:
    `services.AddSingleton<IStoreReadinessGate>(...)` in `App.axaml.cs` line ~238).
  - Assert the startup method (`OnFrameworkInitializationCompleted` or its helper) does not
    call `Database.Migrate`/`MigrateAsync`/seed/rebuild synchronously before showing
    `MainWindow`. If direct source-text assertion is needed, read `App.axaml.cs` at test time
    and assert the ordering (Mock's phase precedent: keep it a code/number assertion, not a
    Persian string).
  - Simplest robust form: assert that the type responsible for deferred work
    (`Animora.Infrastructure.Local.Startup.LocalStoreStartup`) is what performs migration, and
    that `App` awaits the readiness gate rather than the migration directly.
- Keep it deterministic and fast. If the only feasible check is source-text scanning of
  `App.axaml.cs`, load it via a path relative to the test assembly and assert no
  `Migrate`/`EnsureCreated`/`Seed`/`Rebuild` token appears before the `MainWindow` show call.
- Run just this test via filter.
- **PUSH:** `scripts/git/push.sh "test(startup): assert no blocking store work before first frame (PERFORMANCE §12)"`

---

### [ ] T7 — `bench.sh offline` mode + budget capture
File: `scripts/sandbox/bench.sh` (add an `offline` mode) + create
`backend-roadmap/measurements/C11-offline-budget.md`.
- Add an `offline` invocation (positional or `--offline`) that:
  - launches `Animora.Desktop` under Xvfb with `ANIMORA_BENCH=1` (env the app can read to
    emit stopwatch markers; if the app doesn't yet read it, the bench can derive numbers from
    the existing RSS/ttfui polling — keep changes minimal and DO NOT add app features beyond a
    benign `ANIMORA_BENCH` stopwatch log if trivially safe. If adding app instrumentation is
    non-trivial, record measured proxies and note it).
  - runs with **networking irrelevant** (offline product).
  - measures / records the 6 metrics table (below) into the measurements file.
- **Metrics table (record numbers; DO NOT assert in CI; fail only if a metric regresses
  beyond 2× budget → non-zero exit, A-RULE 035):**
  | Metric | Budget | How measured |
  |---|---|---|
  | Time to first frame | ≤ 1200 ms | Main → MainWindow.Opened |
  | Time to dashboard data | ≤ 2500 ms | first frame → snapshot rendered |
  | Working set after 60 s idle | ≤ 300 MB | Process.WorkingSet64 |
  | Patients list page (200 rows) | ≤ 120 ms | query stopwatch, warm |
  | Global search keystroke | ≤ 80 ms | query stopwatch, warm |
  | Post invoice (10 lines) | ≤ 250 ms | command stopwatch, warm |
- Create `backend-roadmap/measurements/` (new folder) and write
  `C11-offline-budget.md` with: the table filled with measured numbers, budget, pass/fail per
  row, and a **hardware description** (CPU model from `/proc/cpuinfo`, RAM, sandbox note).
- Run `scripts/sandbox/bench.sh offline` once; paste its numbers into the file.
- **PUSH:** `scripts/git/push.sh "perf(bench): add offline mode + C11 budget measurements"`

---

### [ ] T8 — Offline smoke run with networking disabled (R-B01)
- Run `scripts/sandbox/run.sh` in a way that proves no network interface is required.
  If the script has no offline flag, run it under a network-disabled shell
  (e.g. `unshare -n` if permitted, or document that `NullCommandTransport` guarantees no
  socket use). Expect `SMOKE RUN OK`.
- This is a verification task; if it only produces logs, still:
- **PUSH:** `scripts/git/push.sh "chore(verify): offline smoke run (no network) — R-B01"`
  (push any log/notes; if nothing changed, the script prints "up to date" — that's fine.)

---

### [ ] T9 — Full build (0/0)
- `scripts/sandbox/build.sh full` → must be **0 warnings / 0 errors**.
- Fix anything that surfaces; if a fix is large, push it separately with a clear message
  before continuing.
- **PUSH:** `scripts/git/push.sh "build: full solution green (0 warnings / 0 errors) for C11"`

---

### [ ] T10 — Full test suite
- `scripts/sandbox/test.sh` → `TESTS OK` across all 5 test projects.
- If a test fails, fix and push the fix (clear message) before re-running.
- **PUSH:** `scripts/git/push.sh "test: full suite green (TESTS OK) for C11"`
  (if no file change, the push prints "up to date".)

---

### [ ] T11 — Acceptance-criteria sweep (verification checklist)
Confirm each, capturing evidence:
- [ ] `src/Animora.Desktop/Mocks/` does not exist.
- [ ] `grep -R "Mock" src/Animora.Desktop --include=*.cs` → nothing outside comments.
- [ ] `LayeringRulesTests` green incl. the 3 new facts.
- [ ] `OfflineClinicDayScenarioTests` green (14 steps + reopen + boarding).
- [ ] `StartupBudgetTests` green.
- [ ] `test.sh` = TESTS OK.
- [ ] `run.sh` = SMOKE RUN OK with no network required.
- [ ] `bench.sh offline` done; `C11-offline-budget.md` has every metric + budget + pass/fail
      + hardware description.
- (Manual 17-screen data check is a human step — note it as "left to reviewer" if the sandbox
  can't do a full UI pass.)
- **PUSH (if any doc/note changed):** `scripts/git/push.sh "chore(verify): C11 acceptance sweep"`

---

### [ ] T12 — Update `PROGRESS.md`
File: `backend-roadmap/PROGRESS.md` (append; follow the existing entry style — Context/
Decision/Rule check/Impact + a "Repo state" list).
- Mark **C11 done**; advance **"Next phase" → `D01`**.
- "Repo state" entry must list: `LocalSyncStatusService` + `SyncCursor` + **migration
  `019_SyncCursor`** (note the 018→019 deviation; **E04 replaces the internals, not the
  registration**), removal of `src/Animora.Desktop/Mocks/`, the 3 new `LayeringRulesTests`
  facts, the end-to-end scenario + its 14 assertions, the startup-budget test,
  `bench.sh offline`, the recorded budget table (**inline the measured numbers**), any
  violation found & fixed by the DI audit, and:
  **"mock deleted: `MockSyncStatusService.cs` — Track C mock purge complete (22 of 22)"**.
- Add a one-line **"Track C complete"** marker so Track D can trust the offline baseline.
- Record deviations: 018→019 migration number; any csproj reference added to Domain.Tests;
  any `ConfigureServices` seam extracted for the DI audit; bench instrumentation approach.
- **PUSH:** `scripts/git/push.sh "docs(progress): mark C11 done; Track C complete; next D01"`

---

### [ ] T13 — Final guard re-check (cheap, no full rebuild)
- Re-run the 3 fast greps/tests that prove the purge (mock grep, Domain.Tests only) to be
  sure nothing regressed after PROGRESS edits (docs-only edits shouldn't, but confirm cheaply).
- No push unless something changed.

---

### [ ] T14 — Delete this plan file (LAST task only)
- `git rm Plan/PHASE-C11-TODO.md` (delete ONLY this file; do not touch the roadmap phase file
  or any other file here — the roadmap phase file deletion, if any, is NOT part of this task;
  re-read the original request: "فقط فایل داخل plan حذف بشه" → only the Plan file).
- **PUSH:** `scripts/git/push.sh "chore(C11): remove plan file after completion"`

---

### [ ] U1 — Optional product packages/prices and consistent contact-for-price presentation
- Implement in the real Inventory/Sales seams and UI; do not invent a price or permit unpriced checkout.
- PUSH: `scripts/git/push.sh "fix(inventory): allow unpriced products with consistent presentation"`

### [ ] U2 — Catalog pagination, category counts and deletion correctness
- Preserve filters across pagination, prevent stale responses, derive counts from the same catalog predicate, and refresh deletion state.
- PUSH: `scripts/git/push.sh "fix(catalog): preserve filters and consistent category lifecycle"`

## 4. Deviations log (fill as you go)
- User explicitly forbids full builds/long tests: T9/T10/T11 use targeted verification instead; never claim a full-suite pass.
- Use AI branch based on newer main, with PR; final plan deletion remains last after U1/U2.
- **018→019:** migration created as `019_SyncCursor` because `018_UserPreferences` exists.
- (add: Domain.Tests → Animora.Desktop reference, if added)
- (add: DI-audit `ConfigureServices` seam, if extracted)
- (add: bench offline instrumentation approach)
- (add: any layering violation found & fixed)
