# Plan — Patients Directory performance pass (C01 follow-up)

> Self-contained execution plan. A fresh model should be able to implement every task below
> **without re-auditing the repo**. All file paths, exact current code, root-cause analysis,
> and the chosen fix are embedded here. Read this file top-to-bottom once, then execute.

---

## 0. Context (read once)

- **App:** Animora — vet clinic desktop app, .NET 10, Avalonia + MVVM, local encrypted SQLite.
- **Scope of this plan:** performance of the **Patients directory screen only** (pets + owners
  list, search, facets, detail reads). The architecture is already healthy (server-side paging,
  non-blocking load, debounced search, virtualized lists, `AsNoTracking`, `SemaphoreSlim` load
  gate). This pass fixes the **query/index layer** and the **grid render** — no structural rewrite.
- **Performance standard:** `PERFORMANCE.md` numbered `§` rules. Relevant here: §5 (batch
  collection updates), §6 (virtualise long lists), §7 (cheap visual tree), §13 (avoid redundant
  reloads).
- **Data-flow rule (never break):** View → ViewModel → `Animora.Contracts` interface → real
  `Animora.Client.Services` impl → `Animora.Application` dispatch → SQLite. ViewModels never touch
  EF/SQL. Seed data lives in Persistence seeders, not UI.

### Key files (confirmed to exist — do not re-search)
| Concern | Path |
|---|---|
| **Hot query file (N+1, LIKE, facets)** | `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs` |
| WeightEntry EF config (no index today) | `src/Animora.Persistence.Sqlite/Configurations/Patients/WeightEntryConfiguration.cs` |
| Pet EF config (indexes) | `src/Animora.Persistence.Sqlite/Configurations/Patients/PetConfiguration.cs` |
| Owner EF config (indexes) | `src/Animora.Persistence.Sqlite/Configurations/Patients/OwnerConfiguration.cs` |
| WeightEntry entity (append-only, `MeasuredUtc`, `WeightKg`, `PetId`) | `src/Animora.Domain/Patients/WeightEntry.cs` |
| Pet entity (`BirthDateUtc`, `RegisteredUtc`) | `src/Animora.Domain/Patients/Pet.cs` |
| Owner entity (`RegisteredUtc`) | `src/Animora.Domain/Patients/Owner.cs` |
| Grid render (`Chunk(3)`) | `src/Animora.Desktop/Pages/Patients/PatientsPageViewModel.cs` (`PublishOwners`/`PublishPets`, ~line 440-452) |
| Grid XAML (`UniformGrid`, nested `ItemsControl`) | `src/Animora.Desktop/Pages/Patients/PatientsPageView.axaml` (~line 686-742) |
| VM load lifecycle (ctor + OnNavigatedTo) | `src/Animora.Desktop/Pages/Patients/PatientsPageViewModel.cs` (~line 92-95, 347-423) |

### Migration facts (confirmed)
- Latest migration = `022` (`20261001065258__022_DashboardWidgetIndexes`). **Next IDs: `023`, `024`, …**
- Migration pattern: file `Migrations/<timestamp>_NNN_Name.cs` + `.Designer.cs` + update
  `AnimoraLocalDbContextModelSnapshot.cs`. Tenant/soft-delete filters are auto-applied by
  `Conventions/ModelConventions.cs` — index columns that start with `TenantId` match existing style.
- **Index-only** migrations (no new column) are preferred for lowest risk. See
  `20260927120000_009_PatientsSearchKeys.cs` and `__022_DashboardWidgetIndexes.cs` as templates.
- **Build/verify commands (fast only — NO full test run):**
  - `scripts/sandbox/build.sh` → must be **0 warnings / 0 errors** (Desktop).
  - `scripts/sandbox/build.sh full` → whole solution, use after Persistence/Domain edits.
  - Do **not** run the long test suite or smoke run unless a task says so.

### LOCKED push rule (critical)
After **each** completed task: run `scripts/git/push.sh "type(scope): message"`. If it exits
code `3` (no creds) → call `setup_github_environment` tool once, re-run. **No push = failure.**
Work on branch `genspark_ai_developer` (first push uses `-b genspark_ai_developer`).

---

## Root-cause analysis (distilled — no re-derivation needed)

The report lists 8 issues. Below is the **exact current code** and the **decided fix** for each.

### Issue 1 + P1 — N+1 "latest weight" correlated subquery, no index
`PatientDirectoryQueries.GetPetsAsync` (and `GetOwnerAsync`) project each pet's latest weight with a
per-row correlated subquery:
```csharp
db.Set<WeightEntry>().Where(w => w.PetId == p.Id)
    .OrderByDescending(w => w.MeasuredUtc).Select(w => (decimal?)w.WeightKg).FirstOrDefault()
```
`WeightEntryConfiguration` has **only** `HasKey(e => e.Id)` — **no index** on `(PetId, MeasuredUtc)`.
So each of 50 rows triggers a full WeightEntry scan + sort. **Fix (P1, chosen):** add composite
index `(PetId, MeasuredUtc)` on WeightEntry → the subquery becomes an index seek + top-1.
> Note: P2 (denormalized `CurrentWeightKg` column on Pet) is the fuller fix but requires schema +
> domain mutator + updating `AddWeightEntryCommandHandler`. **Deferred** — P1 (index) delivers the
> bulk of the win at far lower risk and keeps the append-only WeightEntry invariant intact. P1 is
> in-scope; P2 is explicitly out-of-scope for this pass (documented, not implemented).

### Issue 2 + P3 — N+1 owner pet-count
`GetOwnersAsync` and `GetOwnerAsync` count pets per owner row:
```csharp
db.Set<Pet>().Count(p => p.OwnerId == o.Id)
```
Index `IX_Pets_TenantId_OwnerId` already exists, so each COUNT is an index seek — acceptable.
**Decision:** P3 (denormalized `PetCount` on Owner) is NOT worth the write-path complexity here;
the existing index already makes this O(log n) per row. **Leave as-is**, record rationale. No task.

### Issue 3 + P5 — `LIKE '%term%'` leading wildcard defeats SearchKey index
```csharp
q = q.Where(p => EF.Functions.Like(p.SearchKey, $"%{term}%"));   // Pets
owners = owners.Where(o => EF.Functions.Like(o.SearchKey, $"%{term}%"));  // Owners
```
Leading `%` means `IX_*_TenantId_SearchKey` cannot be used → full scan on every keystroke. This is
the **biggest latency risk while typing**. Full FTS5 (P5) is a large change (new virtual tables,
triggers, sync considerations). **Chosen pragmatic fix:** keep behaviour correct but reduce cost —
switch to **prefix match (`term%`)** which *is* index-usable, OR keep contains-match but gate it.
**Decision for this pass:** implement **prefix search (`$"{term}%"`)** so the existing
`SearchKey` index is used. `SearchKey` already concatenates name + phone + breed + microchip, so a
prefix on the normalized key still matches the leading token. This is the single highest-value,
lowest-risk change. (FTS5 remains a documented future option, not done here.)
> If prefix-only noticeably hurts recall for mid-token matches, the fallback is a two-tier query
> (prefix first, contains only if prefix returns 0) — only add that if clearly needed; do not
> over-engineer. Default = plain prefix.

### Issue 4 — Facets run 3 queries serially on one DbContext
`GetFacetsAsync` issues CountPets + CountOwners + GroupBy(species). They sit in `Task.WhenAll`
beside the page query, but **one `DbContext` is not thread-safe** so EF serializes them → effectively
5 sequential queries. **Fix:** the species `GroupBy` already returns per-species counts; derive
`totalPets` from the **sum of the species-group counts** instead of a separate `CountAsync`,
removing one query. Keep owners count (different table). Net: 3 facet queries → 2. Low risk, exact
same numbers. (Caching facets across page-only changes = P6/§13, see Issue 7 task — optional.)

### Issue 5 + P7 — Grid mode `Chunk(3)` + `UniformGrid` weakens virtualization
`PublishOwners`/`PublishPets` build `OwnerRows`/`PetRows` via `.Chunk(3)` into row VMs, rendered by
`ItemsControl` with `<UniformGrid Columns="3"/>` (non-virtualizing panel) inside the virtualized
ListBox. Virtualization works at the **row** level only; deep owner-card template (avatar + 4
contact lines + tags + pets ItemsControl + footer) inflates scroll frame-time. **Full fix (P7,
ItemsRepeater/WrapPanel)** is a non-trivial XAML rework. **Decision for this pass:** keep the
existing row-chunk approach (it already virtualizes per row and is bounded) but **do not expand
scope** — the measured win from the query fixes is larger. Record Issue 5/6/7 as a **documented
UI-render follow-up**, not implemented, UNLESS it is trivially safe. → **No code task** (kept to
avoid a risky, unmeasured XAML rewrite). Documented only.

### Issue 6 — Nested non-virtual ItemsControls per card
Same root as Issue 5 (contact lines / tags / pets ItemsControls per card). Tied to the P7/P8 UI
rework. **Not implemented this pass** (documented follow-up) — avoids risky deep-template surgery.

### Issue 7 + P10/P11/§13 — Possible double load on first entry
Ctor calls both `UseLoadStatus(ReloadPageAsync)` and directly `_ = ReloadPageAsync()`, and
`OnNavigatedTo` calls `_ = ReloadPageAsync()` again. `_loadGate` prevents *concurrent* loads but not
back-to-back ones; first entry may load twice. **Fix:** add a `_hasLoadedOnce` guard so the ctor's
eager load and the first `OnNavigatedTo` don't double-fire (§13 redundant-reload). Keep
`RefreshOnDataChange` reloads intact. Low risk.

### Issue 8 + P9 — Sort on Age/Recent not index-backed
`GetPetsAsync` sorts `Age` by `p.BirthDateUtc`; `GetOwnersAsync` sorts by `o.RegisteredUtc`. No
index on `(TenantId, BirthDateUtc)` (Pet) or `(TenantId, RegisteredUtc)` (Owner) → filesort.
**Fix (P9):** add those two composite indexes. Low risk, same migration as the WeightEntry index.

---

## TASK LIST

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done.
**After finishing each task → `scripts/git/push.sh "<msg>"` (LOCKED).**

---

### [x] Task 1 — Add WeightEntry `(PetId, MeasuredUtc)` index  (fixes Issue 1 / P1)  ✅ DONE
**File:** `src/Animora.Persistence.Sqlite/Configurations/Patients/WeightEntryConfiguration.cs`
**Do:** inside `Configure`, after `HasKey`, add:
```csharp
builder.HasIndex(e => new { e.PetId, e.MeasuredUtc })
    .HasDatabaseName("IX_WeightEntries_PetId_MeasuredUtc");
```
(Match the naming style of existing `IX_Pets_*` / `IX_Owners_*`. The latest-weight subquery filters
by `PetId` and orders by `MeasuredUtc DESC` → this composite serves both.)
**Verify:** `scripts/sandbox/build.sh full` compiles 0/0 (no migration yet — that's Task 4).
**Note:** the actual DB table name for WeightEntry — confirm the EF entity set name used in the
snapshot (likely `WeightEntries`). Match the migration (Task 4) to whatever the snapshot shows.
**Push:** `scripts/git/push.sh "perf(patients): index WeightEntry(PetId,MeasuredUtc) for latest-weight subquery"`

---

### [x] Task 2 — Add Pet `(TenantId, BirthDateUtc)` + Owner `(TenantId, RegisteredUtc)` indexes  (fixes Issue 8 / P9)  ✅ DONE
**Files:**
- `src/Animora.Persistence.Sqlite/Configurations/Patients/PetConfiguration.cs` — add:
  ```csharp
  builder.HasIndex(e => new { e.TenantId, e.BirthDateUtc }).HasDatabaseName("IX_Pets_TenantId_BirthDateUtc");
  ```
- `src/Animora.Persistence.Sqlite/Configurations/Patients/OwnerConfiguration.cs` — add:
  ```csharp
  builder.HasIndex(e => new { e.TenantId, e.RegisteredUtc }).HasDatabaseName("IX_Owners_TenantId_RegisteredUtc");
  ```
**Why:** `GetPetsAsync` Age sort = `OrderBy(p.BirthDateUtc)`; `GetOwnersAsync` Recent/Age sort =
`OrderBy(ByDescending)(o.RegisteredUtc)`. These make both sorts index-backed instead of filesort.
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "perf(patients): sort indexes Pet(TenantId,BirthDateUtc) + Owner(TenantId,RegisteredUtc)"`

---

### [ ] Task 3 — Search: prefix match to re-enable SearchKey index  (fixes Issue 3 / P5)
**File:** `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs`
**Do:** in `FilterPets` change
```csharp
q = q.Where(p => EF.Functions.Like(p.SearchKey, $"%{term}%"));
```
to
```csharp
q = q.Where(p => EF.Functions.Like(p.SearchKey, $"{term}%"));
```
and in `FilterOwners` change the owner `Like` the same way (`$"{term}%"`).
**Why:** leading `%` forbids index use; `term%` lets `IX_*_TenantId_SearchKey` serve the predicate.
`SearchKey` is a normalized concatenation whose first token is the primary name — a prefix match on
the normalized term is the right semantics for type-ahead. Update the method's XML-doc comment to
say "prefix match (index-usable)" instead of implying contains.
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "perf(patients): prefix search (term%) so SearchKey index is used instead of full scan"`

---

### [x] Task 4 — EF migration `023` for the new indexes (Tasks 1+2)  (schema realization)  ✅ DONE
> Hand-wrote `20261002120000_023_PatientsPerfIndexes.cs` + `.Designer.cs` (Designer body copied from the
> updated snapshot) + updated `AnimoraLocalDbContextModelSnapshot.cs` with the 3 new `HasIndex` entries.
> WeightEntry table confirmed = `WeightEntries`. `build.sh full` = 0 errors.
> **Side-effect fix (pushed with this batch):** `build.sh full` surfaced PRE-EXISTING stale test calls
> (unrelated to indexes) that used the old `IPatientDirectoryQueries` arg counts and a stale
> `CompleteVisitCommandHandler` ctor. Fixed to current signatures so the solution builds clean:
> `tests/Animora.Persistence.Tests/PatientDirectoryQueryTests.cs` (+ReadModels using),
> `tests/Animora.Application.Tests/PatientsUseCaseTests.cs`,
> `tests/Animora.Application.Tests/OfflineBudgetMeasurements.cs` (+ReadModels using),
> `tests/Animora.Application.Tests/VisitEncounterUseCaseTests.cs` (added existing `FakeAppointmentRepository`).
**Why separate task:** EF config `HasIndex` only takes effect once a migration + snapshot exist.
**Do (preferred — hand-write to avoid design-time tooling flakiness; mirror `__022_DashboardWidgetIndexes.cs`):**
1. Create `src/Animora.Persistence.Sqlite/Migrations/<ts>_023_PatientsPerfIndexes.cs` (+ `.Designer.cs`),
   timestamp > `20261001065258`. `Up` creates the 3 indexes; `Down` drops them:
   ```csharp
   migrationBuilder.CreateIndex("IX_WeightEntries_PetId_MeasuredUtc", "WeightEntries",
       new[] { "PetId", "MeasuredUtc" });
   migrationBuilder.CreateIndex("IX_Pets_TenantId_BirthDateUtc", "Pets",
       new[] { "TenantId", "BirthDateUtc" });
   migrationBuilder.CreateIndex("IX_Owners_TenantId_RegisteredUtc", "Owners",
       new[] { "TenantId", "RegisteredUtc" });
   ```
   **Confirm the real table name for WeightEntry** from `AnimoraLocalDbContextModelSnapshot.cs`
   (grep for `WeightEntry` → use the exact `ToTable`/entity set name).
2. Update `AnimoraLocalDbContextModelSnapshot.cs`: add the 3 `.HasIndex(...).HasDatabaseName(...)`
   entries under the Pet / Owner / WeightEntry builders so the snapshot matches the model (otherwise
   the build/migration-consistency check fails).
**Alternative (if tooling available):** `dotnet ef migrations add 023_PatientsPerfIndexes` from the
Persistence project with the correct startup project — then inspect the generated file matches the 3
indexes and nothing else. If tooling isn't wired in this sandbox, use the hand-written path above.
**Verify:** `scripts/sandbox/build.sh full` 0/0. (Do NOT run the long test suite.)
**Push:** `scripts/git/push.sh "feat(persistence): migration 023 — Patients perf indexes (weight/sort)"`
> If this migration + snapshot turns into a large/heavy diff, push immediately with a clear message
> per the locked rule.

---

### [ ] Task 5 — Facets: drop redundant total-pets COUNT  (fixes Issue 4)
**File:** `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs`, `GetFacetsAsync`.
**Do:** compute `speciesCounts` first (the grouped query), then derive `totalPets` as the sum of its
values instead of a separate `CountAsync`:
```csharp
var speciesCounts = await FilterPets(db.Set<Pet>().AsNoTracking(), search, species: null, status)
    .GroupBy(p => p.Species)
    .Select(g => new { Species = g.Key, Count = g.Count() })
    .ToDictionaryAsync(x => x.Species, x => x.Count, ct);
var totalPets = speciesCounts.Values.Sum();          // was a separate CountAsync
var totalOwners = await FilterOwners(search, species, status).CountAsync(ct);
```
**Important correctness check:** the old `totalPets` applied the **active species filter**, but
`speciesCounts` is computed with `species: null` (chips show full taxonomy). Confirm the intended
semantics: facet `totalPets` should be the count under the FULL filter EXCEPT species (so chips +
total agree). The sum of species-group counts == total-ignoring-species, which is the correct number
for the "all species" chip. Verify this matches how the UI consumes `TotalPets`
(`BuildSpeciesChips` uses `_totalPetMatches` for the "همه گونه‌ها" chip). If the UI expects
species-filtered total, keep the separate count instead and only remove duplication where safe —
**read `BuildSpeciesChips` in the VM before finalizing.** Default: sum-of-groups (matches the
"all species" chip).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "perf(patients): derive facet total-pets from species groups (one fewer query)"`

---

### [ ] Task 6 — First-load guard against double reload  (fixes Issue 7 / §13)
**File:** `src/Animora.Desktop/Pages/Patients/PatientsPageViewModel.cs`
**Current:** ctor (~L92-95) does `UseLoadStatus(ReloadPageAsync)`, `RefreshOnDataChange(...)`,
`_ = ReloadPageAsync();`. `OnNavigatedTo` (~L347-349) does `if (!IsBusy) _ = ReloadPageAsync();`.
**Do:** add a private field `private bool _hasLoadedOnce;`. Set it `true` at the start of the first
successful `LoadAsync` (or right where the ctor kicks the eager load), and in `OnNavigatedTo` skip
the reload the very first time it fires if the ctor already started/finished the initial load:
```csharp
public override void OnNavigatedTo()
{
    base.OnNavigatedTo();          // keep existing base call if present
    if (!_hasLoadedOnce) { _hasLoadedOnce = true; return; }  // ctor already triggered first load
    if (!IsBusy) _ = ReloadPageAsync();
}
```
Set `_hasLoadedOnce = true` in the ctor right after `_ = ReloadPageAsync();` so the first
`OnNavigatedTo` is treated as the already-done initial load. Keep all data-change-driven reloads.
**Caution:** verify the base `OnNavigatedTo` signature/behavior before editing; preserve any base
call. Keep `_loadGate` as the concurrency guard — this guard only removes the *redundant first*
reload, not the gate.
**Verify:** `scripts/sandbox/build.sh` (Desktop) 0/0. Smoke-run (`scripts/sandbox/run.sh`) since this
touches navigation/load lifecycle → expect `SMOKE RUN OK`.
**Push:** `scripts/git/push.sh "perf(patients): guard first-entry double reload (PERFORMANCE.md §13)"`

---

### [ ] Task 7 — Document the deferred UI-render + denorm follow-ups (no code)
**Why:** Issues 5/6/7-render (grid `Chunk(3)`/`UniformGrid`, nested ItemsControls, P7/P8) and the
P2/P3 denormalization options are intentionally **out of scope** for this low-risk query/index pass.
Record that decision so a future session doesn't re-audit.
**Do:** append a short note to the existing C01 performance follow-up log that backend-roadmap keeps
(the "reviewer UI/performance follow-up" referenced in `backend-roadmap/PROGRESS.md`). If no obvious
single follow-up file exists, add a concise `<!-- -->` comment block at the top of
`PatientDirectoryQueries.cs` summarizing: (a) P1 index done, (b) P5 now prefix-search, (c) P2/P3
denorm deferred + why, (d) grid P7/P8 render rework deferred + why. Keep it to a few lines
(token-aware). **Do not create new roadmap files.**
**Verify:** `scripts/sandbox/build.sh full` 0/0 (comment-only, should be trivial).
**Push:** `scripts/git/push.sh "docs(patients): record deferred denorm + grid-render perf follow-ups"`

---

### [ ] Task 8 — Final verification pass
**Do:**
- `scripts/sandbox/build.sh full` → 0/0.
- `scripts/sandbox/run.sh` → `SMOKE RUN OK` (confirms DI + migrations apply cleanly with the new
  indexes). **This is the only run; no long test suite.**
- Quick self-review of the diff: 3 EF configs, 1 migration (+snapshot), 1 query file (search +
  facets), 1 VM (load guard), 1 doc note.
**Push (if any fixup was needed):** `scripts/git/push.sh "test(patients): verify build + smoke after perf pass"`

---

### [ ] Task 9 — Open / update the Pull Request
**Do:** ensure branch `genspark_ai_developer` is pushed; open/update PR → `main` with a summary:
indexes (weight/sort), prefix search, facet query reduction, first-load guard, deferred-items note.
Provide the PR URL to the user.
**Push:** already handled by prior `push.sh` runs; just create/update the PR and share the link.

---

### [ ] Task 10 (FINAL) — Delete this plan file
**Do:** `rm /home/user/webapp/Plan/patients-directory-perf.md` (and `rmdir Plan` if empty).
Touch nothing else — no other files, no roadmap files.
**Push:** `scripts/git/push.sh "chore(plan): remove completed Patients perf plan"`

---

## Out-of-scope (explicitly NOT doing — recorded so no one re-investigates)
- **P2** denormalized `CurrentWeightKg` on Pet — needs schema+domain mutator+command change; index (P1) suffices.
- **P3** denormalized `PetCount` on Owner — existing `IX_Pets_TenantId_OwnerId` already makes the COUNT a seek.
- **P5 full FTS5** — large change (virtual tables/triggers/sync); prefix search (Task 3) is the pragmatic win.
- **P7/P8** grid `ItemsRepeater`/`WrapPanel` rework + shallower card template — risky unmeasured XAML surgery.
- No full test suite / no long builds (per instruction). Fast `build.sh` + one smoke run only.
