# Plan — Patients & Owners audit remediation (2026-10-02)

> **Self-contained execution plan.** A fresh model must implement every task below **without
> re-auditing the repo**. All file paths, exact current code, root-cause and the chosen fix are
> embedded here. Read once top-to-bottom, then execute task-by-task. Source audit:
> `docs/PATIENTS_OWNERS_AUDIT_2026-10-02.md` (22 findings: 6 P1, 16 P2).

---

## 0. Context (read once)

- **App:** Animora — vet clinic desktop app, .NET 10, Avalonia + MVVM, local **encrypted SQLite**.
  Persian RTL UI. No server yet (server/API/SQL Server is backend phase D01+, not started).
- **Data-flow rule (NEVER break):** Avalonia View → ViewModel → `Animora.Contracts` interface →
  real `Animora.Client.Services` impl → `Animora.Application` dispatcher (permission/validation/
  transaction/journal behaviors) → Domain + SQLite projection queries. **ViewModels never touch
  EF/SQL. Contracts stay pure (no EF/Identity refs).**
- **Scope:** Patients/Owners directory + owner sheet + pet file + add-owner/add-pet/weight write
  paths, and their Contracts/services/queries/commands/mappings/indexes/VM. Excluded: `Animora-UI`
  design reference, unrelated pages.
- **CONVENTIONS (backend-roadmap/CONVENTIONS.md):** §2 layering, §4 sealed types + `Result<T>` for
  expected errors (never throw) + `IClock` + ULIDs + `ConfigureAwait`, §7 no scope-creep / no
  user-existence leak. **PERFORMANCE.md** §-rules: §5 batch collection updates, §6 virtualize long
  lists, §7 cheap visual tree, §13 avoid redundant reloads.

### Build / verify commands (FAST ONLY — no full test suite, no long run)
- `scripts/sandbox/build.sh` → Desktop only, must be **0 warnings / 0 errors**.
- `scripts/sandbox/build.sh full` → whole solution; use **after Persistence/Domain/Contracts edits**.
- `scripts/sandbox/run.sh` → smoke run (`SMOKE RUN OK`); use **only** when a task touches
  DI/migrations/navigation-load lifecycle. Do **not** run the long test suite.

### 🔒 LOCKED push rule (critical — failure = project death)
After **each** completed task: run `scripts/git/push.sh "type(scope): message"`.
- The script does: stage-all → commit → fetch + rebase(autostash) → push. One `✓`/`✗` line.
- Exit `3` = no GitHub creds → call the **`setup_github_environment`** tool once, then re-run.
- Exit `2` = rebase conflict (repo left clean) → resolve, re-run.
- If a task produces a **large/heavy diff** (e.g. migration + snapshot, or a big refactor), push it
  **immediately** with a clear message — do not batch it into a later task.
- Work happens on branch **`genspark_ai_developer`** (already synced to `main` @ `de0e195`).

### Key files (confirmed to exist — do NOT re-search). Repo-relative paths.
| Alias | Path |
|---|---|
| **SQL** | `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs` |
| WECFG | `src/Animora.Persistence.Sqlite/Configurations/Patients/WeightEntryConfiguration.cs` |
| PETCFG | `src/Animora.Persistence.Sqlite/Configurations/Patients/PetConfiguration.cs` |
| OWNCFG | `src/Animora.Persistence.Sqlite/Configurations/Patients/OwnerConfiguration.cs` |
| **SVC** | `src/Animora.Client.Services/Patients/PatientsService.cs` |
| **MAP** | `src/Animora.Client.Services/Patients/PatientsMapping.cs` |
| CMODEL | `src/Animora.Contracts/Patients/PatientModels.cs` (enums + Pet/OwnerSummary) |
| DMODEL | `src/Animora.Contracts/Patients/PatientDetailModels.cs` (OwnerDetail/PetDetail/New*Request) |
| IQS | `src/Animora.Contracts/Patients/IPatientsQueryService.cs` |
| CREATE | `src/Animora.Application/UseCases/Patients/CreateOwnerCommand.cs` |
| UPDOWN | `src/Animora.Application/UseCases/Patients/UpdateOwnerCommand.cs` |
| UPDPET | `src/Animora.Application/UseCases/Patients/UpdatePetInfoCommand.cs` |
| WEIGHT | `src/Animora.Application/UseCases/Patients/AddWeightEntryCommand.cs` |
| DPET | `src/Animora.Domain/Patients/Pet.cs` (enums `PetSpecies`/`PetStatus`/`PetGender` live here) |
| DOWN | `src/Animora.Domain/Patients/Owner.cs` (`UpdateContactDetails` mutator) |
| **VM** | `src/Animora.Desktop/Pages/Patients/PatientsPageViewModel.cs` |
| PV | `src/Animora.Desktop/Pages/Patients/PatientsPageView.axaml` |
| OCARD | `src/Animora.Desktop/Pages/Patients/OwnerCardViewModel.cs` |
| WIZ | `src/Animora.Desktop/Pages/Patients/AddPatientWizardViewModel.cs` |
| INVVM (readiness template) | `src/Animora.Desktop/Pages/Inventory/InventoryPageViewModel.cs` |

### Enum reality (confirmed — root of P03)
- **Domain `PetSpecies`** (DPET:8-26): `Dog=0, Cat=1, Bird=2, Rodent=3, Reptile=4, Other=5`.
  **No Rabbit.** Species is **immutable after Pet creation** (DPET remarks) — a species change makes
  a new record.
- **Contracts `PetSpecies`** (CMODEL:8-16): `Cat, Dog, Bird, Rabbit, Rodent, Other`. **No Reptile.**
- **Domain `PetStatus`** (DPET:41-51): `Active=0, Inactive=1, Deceased=2`.
- **Contracts `PetStatus`** (CMODEL:22-27): `Healthy, FollowUp, Critical`.

### Migration facts (confirmed)
- Latest migration = `023` (`20261002120000_023_PatientsPerfIndexes.cs`). **Next IDs: `024`, `025`…**
- Pattern: `Migrations/<timestamp>_NNN_Name.cs` (+ `.Designer.cs`) + update
  `Migrations/AnimoraLocalDbContextModelSnapshot.cs`. Tenant/soft-delete filters are auto-applied by
  `Conventions/ModelConventions.cs`. Table names: `Pets`, `Owners`, `WeightEntries`.
- **Index-only migrations are preferred** (lowest risk). Template: `023_PatientsPerfIndexes.cs`.

### ✅ Already-shipped perf work (do NOT redo — verify only)
Migration `023` + EF configs already added these three indexes (WECFG/PETCFG/OWNCFG confirm them):
- `IX_WeightEntries_PetId_MeasuredUtc` → **satisfies P07's index half**.
- `IX_Pets_TenantId_BirthDateUtc` + `IX_Owners_TenantId_RegisteredUtc` → **satisfies P13 fully**.
So in the tasks below P13 is "verify-only", and P07 only needs the *denormalization* decision
(not the index). A prior `Plan/patients-directory-perf.md` existed for the perf-report subset; this
plan supersedes it and covers the **full** audit (correctness included).

---

## Task list

**Status legend:** `[ ]` todo · `[~]` in progress · `[x]` done.
**Ordering rationale:** P1 correctness first (data-loss/access/wrong-data), then the cheap
query/robustness P2s, then the heavier UI-render P2s, then verify + PR + delete-plan. Each task is
independently pushable. **After every task → run the push command shown.**

---

### [x] T01 — P01 (P1) · Owner edit wipes email/city/notes (silent data loss)
**Root cause (confirmed):** `UpdateOwnerCommand` (UPDOWN:17-18) has **no** Email/City/Notes fields.
Its handler (UPDOWN:45-46) calls `owner.UpdateContactDetails(..., email: null, ..., city: null,
notes: null)` — literal nulls — on every edit. `CreateOwnerCommand` handler (CREATE:49-50) has the
same null-wipe shape (benign: new row). Domain `Owner` **does** persist `Email`/`City`/`Notes`
(DOWN:47,53,59) and its mutator signature is
`UpdateContactDetails(fullName, phonePrimary, phoneSecondary?, nationalId?, email?, address?, city?, gender, notes?)` (DOWN:103).
`OwnerDetail` (DMODEL:122-137) carries **none** of Email/City/Notes — so the edit form genuinely
does not own them today; the bug is that save *destroys* values set elsewhere (seed/future screen).
**Chosen fix (minimal, preserve-unspecified — avoids widening Contracts/UI for fields the form can't
edit yet):** in `UpdateOwnerCommandHandler`, read the current owner (already does: `owner`) and
**preserve** its existing `Email`/`City`/`Notes` instead of passing null:
```csharp
owner.UpdateContactDetails(c.FullName, c.PhonePrimary, c.PhoneSecondary, c.NationalId,
    email: owner.Email, address: c.Address, city: owner.City, gender: c.Gender, notes: owner.Notes);
```
Do **not** pass literal null for a field the command does not own. Leave `CreateOwnerCommand` as-is
(new row → null is correct) but add a one-line comment there noting the null is intentional for a
new row. Do not change Contracts/UI in this task.
**Files:** UPDOWN only (+ 1 comment in CREATE).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "fix(patients): preserve owner email/city/notes on edit (stop silent data loss) [P01]"`

---

### [x] T02 — P02 + P06 (P1) · Detail reads swallow auth failure as "not found"; load catch-all flattens every error
**Root cause (confirmed):**
- SVC `GetOwnerAsync` (SVC:160-168) and `GetPetAsync` (SVC:170-178): on `qc.IsFailure` they
  `return null`, collapsing an auth/session failure into "owner/pet not found". The page reads
  (`GetPetsPageAsync` SVC:33, `GetOwnersPageAsync` SVC:52, facets SVC:72) instead **throw**
  `new InvalidOperationException(qc.Error.Code)` — inconsistent.
- VM `AddPetAsync` (VM:321-344): `GetOwnerAsync` → if `detail is null` just `return` (VM:329-331),
  no feedback → a capability/session loss looks like a silent no-op on the add-pet button.
- VM `LoadAsync` catch-all (VM:415-419) catches `Exception`, discards `ErrorCode`, shows one generic
  Persian toast — permission-denied / session-expired / store-unavailable / real bug all identical.
**Chosen fix:**
1. In SVC `GetOwnerAsync`/`GetPetAsync`: replace `if (qc.IsFailure) return null;` with
   `if (qc.IsFailure) throw new InvalidOperationException(qc.Error.Code);` (match the page reads).
   Keep returning `null` **only** for a genuine not-found (query success + null value) — that path
   is unchanged.
2. In VM `AddPetAsync`: the `detail is null` branch now means genuine not-found; keep the early
   return but show an info toast: `_toast.Show("مالک پیدا نشد", Tone.Info);` before returning. Auth
   failures now throw and are handled by the command's own try (see step 3 pattern) — wrap the
   `GetOwnerAsync` call so a thrown auth error surfaces a danger toast rather than crashing: catch
   `InvalidOperationException` around the `GetOwnerAsync`+modal open and
   `_toast.Show("دسترسی لازم را ندارید یا نشست منقضی شده", Tone.Danger);`.
3. In VM `LoadAsync` catch: keep the generic toast as fallback but **log/branch the code**. Minimal,
   dependency-free approach: catch `InvalidOperationException ex` first and map a few known codes to
   distinct messages (session/permission vs. generic), then a final `catch (Exception)` for the rest.
   Known codes to distinguish (strings from `ErrorCodes`): session/permission codes → "دسترسی لازم
   را ندارید یا نشست منقضی شده؛ دوباره وارد شوید" (Tone.Danger); everything else → existing generic
   toast. **Before writing, grep `ErrorCodes` for the exact session/permission constant names**
   (e.g. `grep -rn "Session\|Permission\|Unauthor\|Forbidden" src/Animora.Domain/**/ErrorCodes*.cs`);
   use the real constant values, not guessed strings. Do not over-engineer — 2 buckets is enough.
**Files:** SVC, VM. (No Contracts change.)
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "fix(patients): distinguish auth failure from not-found + surface it in VM [P02,P06]"`

---

### [x] T03 — P03 (P1) · Species enum mismatch (Reptile↔Rabbit) is lossy/asymmetric
**Root cause (confirmed):** Domain has `Reptile` (no Rabbit); Contracts has `Rabbit` (no Reptile).
Maps fold both to Other: MAP:67-74 (`MapSpecies` Domain→Contracts: Reptile → `_` → Other),
MAP:97-105 (`ToDomain` Contracts→Domain: `Rabbit → Other`), SVC:123-130 (`MapSpeciesToContract`
same). Effects: (a) every reptile displays/filters as "Other" (unreachable); (b) the "Rabbit" chip
can never match (domain stores rabbits as Other) → permanently empty filter; (c) a pet created as
"Rabbit" reopens as "Other" (visible identity change).
**Decision (canonical set — align both enums; least surprising, future-proof, no dead chips):** add
the missing member to **each** enum so the map is bijective:
- Contracts `PetSpecies` (CMODEL): add `Reptile`. Final set e.g.
  `Cat, Dog, Bird, Rabbit, Rodent, Reptile, Other`.
- Domain `PetSpecies` (DPET): add `Rabbit`. **Append with a new explicit numeric value** to avoid
  renumbering persisted rows: `Reptile=4, Other=5` already exist → add `Rabbit = 6`. Do NOT reorder
  existing values (they are stored as ints in SQLite).
- Make all three maps bijective: MAP `MapSpecies` (Domain→Contracts) add `Reptile→Reptile`,
  `Rabbit→Rabbit`; MAP `ToDomain` (Contracts→Domain) `Rabbit→Rabbit`, `Reptile→Reptile`; SVC
  `MapSpeciesToContract` add `Reptile→Reptile` and `Rabbit→Rabbit` (drop the lossy `_ → Other` for
  these two; keep `Other→Other` default).
- **No migration needed** (enum stored as int; adding a new int value adds no column/constraint).
  The species column already holds 0..5; new value 6 is forward-compatible.
- VM `BuildSpeciesChips` (VM:460-464) iterates a hardcoded chip list `Dog,Cat,Bird,Rabbit,Rodent,
  Other` — **add `Reptile`** so reptiles get a chip (and chips already hide zero-count species at
  VM:466-468, so no dead chip appears when none exist).
**Files:** CMODEL, DPET, MAP, SVC, VM. **No migration.**
**Verify:** `scripts/sandbox/build.sh full` 0/0. (Contracts+Domain+Desktop all touched.)
**Push:** `scripts/git/push.sh "fix(patients): align Pet species enums (add Rabbit domain + Reptile contract); bijective map [P03]"`

---

### [x] T04 — P04 (P1) · Dead clinical-status filter plumbed end-to-end but never wired
**Root cause (confirmed):** `PetStatus? status` is a parameter on SQL `GetPetsAsync`/`GetOwnersAsync`/
`GetFacetsAsync` (SQL:21,45,66) and the Application query records, but **every** caller passes
`Status: null` (SVC:36,55,75). VM has no status field; the toolbar exposes species+vaccine+sort only.
**Decision (REMOVE the dead chain — do NOT add new UI; status filtering is not an audited product
requirement and adding a control is scope-creep per CONVENTIONS §7):** strip the unused `status`
parameter from the query layer so the code stops implying a working feature.
- SQL: remove `PetStatus? status` from `GetPetsAsync`, `GetOwnersAsync`, `GetFacetsAsync`,
  `FilterPets`, `FilterOwners` signatures and the `if (status is {} st) ...` / cross-filter uses.
  The `IPatientDirectoryQueries` interface (find it:
  `grep -rn "interface IPatientDirectoryQueries" src/`) must be updated to match.
- Application query records (`PetDirectoryQuery`, `OwnerDirectoryQuery`, `PatientsDirectoryFacetsQuery`
  — find them: `grep -rn "record PetDirectoryQuery\|record OwnerDirectoryQuery\|record PatientsDirectoryFacetsQuery" src/`):
  remove the `Status` positional member; update their handlers that forward to SQL.
- SVC:36,55,75: drop the `Status: null` argument at each call site.
**CAUTION:** this touches signatures across Persistence + Application + Client.Services — build full
and fix every caller the compiler flags (tests too). If the removal turns into a large diff, push
immediately. If a future reviewer prefers *wiring* a UI instead, that is a separate product decision;
this task's chosen path is removal (documented).
**Files:** SQL, `IPatientDirectoryQueries`, Application query records + handlers, SVC. Possibly tests.
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "refactor(patients): remove dead PetStatus filter chain (never wired to UI) [P04]"`

---

### [x] T05 — P05 (P1) · Patients page ignores the store-readiness gate
**Root cause (confirmed):** VM ctor (VM:92-95) kicks `UseLoadStatus(ReloadPageAsync)` +
`RefreshOnDataChange` + eager `_ = ReloadPageAsync()`. `LoadAsync` (VM:382-384) waits only on
`_loadGate` (a `SemaphoreSlim`), **never** on `IStoreReadinessGate`. Every other Track C page awaits
it first (template INVVM:46 ctor injects `IStoreReadinessGate readiness`; INVVM:118 `LoadAsync`
begins `await _readiness.WaitAsync();`). On cold start the first directory load races local-store
init (migrations/seed) → may hit a not-ready store → caught → spurious failure toast on landing.
**Chosen fix (copy the INVVM pattern exactly):**
1. Add field `private readonly IStoreReadinessGate _readiness;` and a ctor parameter
   `IStoreReadinessGate readiness` (place it right after `IPatientsQueryService patientsQueryService`
   in the ctor signature); assign `_readiness = readiness;`. `using Animora.Contracts.Diagnostics;`
   (confirm namespace from `IStoreReadinessGate.cs`).
2. In `LoadAsync` (VM:382) make the **first** line inside `try` (before `_loadGate.WaitAsync()` is
   fine, but match INVVM which awaits readiness first) `await _readiness.WaitAsync();`. Keep
   `_loadGate` as the concurrency guard.
3. DI: the VM is constructed via DI — confirm the container resolves `IStoreReadinessGate`
   (it does: `StoreReadinessGate` is registered and injected into other pages). No registration
   change expected; if the VM is `new`-ed anywhere, fix that call site (grep
   `new PatientsPageViewModel(`).
**Files:** VM (+ possibly a DI/new call site).
**Verify:** `scripts/sandbox/build.sh` (Desktop) 0/0, then `scripts/sandbox/run.sh` → `SMOKE RUN OK`
(touches load lifecycle + DI).
**Push:** `scripts/git/push.sh "fix(patients): await IStoreReadinessGate before first load (match other pages) [P05]"`

---

### [x] T06 — P09 (P2) · Leading-wildcard LIKE defeats the SearchKey index
**Root cause (confirmed):** SQL:91 `EF.Functions.Like(p.SearchKey, $"%{term}%")` (pets) and SQL:107
(owners) use a leading `%` → `IX_Pets_TenantId_SearchKey` / `IX_Owners_TenantId_SearchKey` (both
exist) are unusable → full-table scan per debounced keystroke (220 ms, VM:178). Biggest typing-latency
risk.
**Chosen fix:** change both to **prefix** match `$"{term}%"` (drop the leading `%`). `SearchKey` is a
normalized concatenation whose first token is the primary name, so a prefix match on the normalized
term is correct type-ahead semantics and lets the index serve the predicate. Update each method's
XML-doc to say "prefix match (index-usable)" instead of implying contains.
**Files:** SQL (two `Like` lines + two doc comments).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "perf(patients): prefix search (term%) so SearchKey index is used, not a full scan [P09]"`

---

### [x] T07 — P10 (P2) · Facets issue a redundant total-pets COUNT
**Root cause (confirmed):** `GetFacetsAsync` (SQL:65-81) runs 3 statements — `totalPets` CountAsync
(SQL:70), `totalOwners` CountAsync (SQL:71), species GROUP BY (SQL:75-78). The species GROUP BY is
computed with `species: null` (full taxonomy under text filter), and its values already **sum** to
the species-ignoring pet total — the same number the "all species" chip needs
(VM `BuildSpeciesChips` uses `_totalPetMatches` for the "همه گونه‌ها" chip at VM:458).
**Chosen fix:** compute `speciesCounts` first, then derive `totalPets = speciesCounts.Values.Sum();`
and delete the separate `totalPets` CountAsync. Keep `totalOwners` CountAsync. Net: 2 statements
instead of 3.
```csharp
var speciesCounts = await FilterPets(db.Set<Pet>().AsNoTracking(), search, species: null)
    .GroupBy(p => p.Species).Select(g => new { Species = g.Key, Count = g.Count() })
    .ToDictionaryAsync(x => x.Species, x => x.Count, ct);
var totalPets   = speciesCounts.Values.Sum();     // was a separate CountAsync
var totalOwners = await FilterOwners(search, species).CountAsync(ct);
```
**NOTE:** after T04 the `status` parameter is gone from these signatures — write this against the
**post-T04** signatures (no `status` arg). If T04 is not yet done, keep `status` in the call.
**Semantics check already resolved:** sum-of-groups == pet count ignoring the species filter, which
is exactly the "all species" chip number — correct.
**Files:** SQL (`GetFacetsAsync`).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "perf(patients): derive facet total-pets from species groups (one fewer query) [P10]"`

---

### [x] T08 — P15 (P2) · First-entry double/triple reload
**Root cause (confirmed):** ctor fires `UseLoadStatus(ReloadPageAsync)` (VM:92),
`RefreshOnDataChange(...)` (VM:94) and explicit `_ = ReloadPageAsync()` (VM:95); `OnNavigatedTo`
(VM:347-350) fires another `ReloadPageAsync` when `!IsBusy`. `_loadGate` serializes but does not
dedupe queued reloads → first navigation runs 2–3 identical loads (~5 statements each).
**Chosen fix:** add `private bool _hasLoadedOnce;`. Set it `true` in the ctor right after
`_ = ReloadPageAsync();` (VM:95). In `OnNavigatedTo`, skip the very first reload because the ctor
already started it:
```csharp
public override void OnNavigatedTo()
{
    base.OnNavigatedTo();   // keep if base defines it; else omit — VERIFY base signature first
    if (!_hasLoadedOnce) { _hasLoadedOnce = true; return; }
    if (!IsBusy) _ = ReloadPageAsync();
}
```
Keep all data-change-driven reloads and `_loadGate`. **Verify the base `OnNavigatedTo` signature/
behavior before editing** (PageViewModelBase) — preserve any base call; current method has no
`base.` call so only add one if the base declares it `virtual` with a body.
**Files:** VM.
**Verify:** `scripts/sandbox/build.sh` (Desktop) 0/0, then `scripts/sandbox/run.sh` → `SMOKE RUN OK`
(load/navigation lifecycle).
**Push:** `scripts/git/push.sh "perf(patients): guard first-entry double reload (PERFORMANCE.md §13) [P15]"`

---

### [x] T09 — P16 (P2) · First/last name split by first space corrupts multi-part names
**Root cause (confirmed):** MAP:47-51 `SplitName` splits `FullName` on the **first** space; SVC:188
(`CreateOwnerAsync`) and SVC:219 (`SaveOwnerAsync`) recombine `$"{First} {Last}".Trim()`. The domain
stores one `FullName` (CREATE:48). So "محمد رضا تقوی" round-trips as First="محمد", Last="رضا تقوی";
single-token names get Last="". Any UI treating First/Last distinctly is wrong.
**Decision (document FullName as the single identity — lowest-risk; the write path already recombines,
and the domain has no separate first/last):** keep the mechanical split but make the contract honest
and the split loss-free for display:
- Keep `SplitName` as the *display* decomposition but add a code comment that `FullName` is the
  single source of truth and First/Last are a best-effort display split only (first token = First,
  remainder = Last). This matches the recombine in SVC (no data loss on save because save re-joins
  with a single space).
- **Do not** change the domain/command to store separate first/last (that is a larger product change
  touching DMODEL `OwnerDetail`, the sheet UI, and a migration — out of scope here).
- If a cleaner behavior is desired with zero schema change: for a single-token name set
  `Last = ""` (already does) and ensure the recombine does not emit a double space (it uses `.Trim()`
  — already safe). No functional code change strictly required beyond the clarifying comment, UNLESS
  review finds a concrete UI that mis-renders — then split the LAST space instead of the first for
  Persian "given + family" convention. **Default: comment-only clarification.**
**Files:** MAP (comment; optional one-line split tweak only if a mis-rendering UI is found).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "docs(patients): document FullName as single identity; clarify First/Last display split [P16]"`

---

### [x] T10 — P17 (P2) · Domain Deceased/Inactive map to UI Critical/FollowUp
**Root cause (confirmed):** MAP:83-88 `MapStatus`: `Active→Healthy`, `Inactive→FollowUp`, and the
`_` default (**Deceased**) `→ Critical`. A deceased pet shows the red "critical" clinical pill
(clinically wrong/distressing); "Inactive"→"FollowUp" conflates administrative inactivity with a
clinical follow-up state.
**Decision (add explicit UI states):** the UI `PetStatus` enum has only `Healthy, FollowUp, Critical`.
Deceased/Inactive are **not** clinical severities. Options:
- **(A) Minimal, no-Contracts-change:** map `Deceased→FollowUp` is still wrong; map `Inactive→Healthy`
  loses info. Rejected.
- **(B) Chosen — extend the UI status enum** so pills are accurate: add `Inactive` and `Deceased` to
  Contracts `PetStatus` (CMODEL:22-27) → `Healthy, FollowUp, Critical, Inactive, Deceased`. Then
  MAP `MapStatus` becomes bijective: `Active→Healthy, Inactive→Inactive, Deceased→Deceased` and
  (if a domain `Critical`-equivalent appears later) default→`FollowUp`. Update the pill
  presentation: find where UI `PetStatus` → tone/label is rendered (grep
  `PetStatus.Critical\|PetStatus.FollowUp\|StatusTone\|StatusLabel` in Desktop; likely
  `PatientsPresentation` or a converter) and add neutral tones for Inactive (muted/grey) and
  Deceased (neutral/dark, **not** danger-red).
- Reptile-style chips/filters are NOT affected (status isn't a chip after T04 removes the filter).
**CAUTION:** adding enum members touches Contracts (pure) + Desktop presentation. Build full. If the
status→tone mapping lives in XAML converters, update those too (grep for the enum name in `.axaml`).
**Files:** CMODEL, MAP, Desktop status presentation (converter/`PatientsPresentation`).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "fix(patients): explicit Inactive/Deceased pills instead of Critical fall-through [P17]"`

---

### [x] T11 — P18 (P2) · Allergies/chronic conditions round-trip through a joined string
**Root cause (confirmed):** write SVC:234-235 `string.Join(", ", pet.Allergies)` /
`string.Join(", ", pet.ChronicConditions)`; read MAP:55-57 `Split` on `[',', '،', '\n']`. A single
entry containing a comma ("دارو، نوع ب") is split into two on read → count drift across save/reload.
Stored as `AllergyNotes`/`ChronicNotes` single strings on `Pet` (DPET).
**Decision (lowest-risk delimiter hardening — structural owned-rows/JSON is a schema change = out of
scope):** switch the join/split to a **newline** delimiter only (`'\n'`), which users do not type as
a list separator, and stop splitting on commas:
- Write SVC:234-235: `string.Join("\n", pet.Allergies)` and `string.Join("\n", pet.ChronicConditions)`.
- Read MAP:55-57 `Split`: split on `['\n']` only (keep `RemoveEmptyEntries | TrimEntries`). Update
  the XML-doc: "newline-separated; commas are preserved inside a value".
- **Back-compat note:** existing rows joined with ", " will now read as a single entry. This is
  acceptable (no data loss — the text is preserved verbatim; only the list segmentation changes) and
  there is no production data yet (pre-release). Add a one-line comment stating this.
**Files:** SVC (two joins), MAP (`Split`).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "fix(patients): newline-delimit allergy/chronic lists so commas survive round-trip [P18]"`

---

### [x] T12 — P19 (P2) · `default(DateTime)` used as a null sentinel for pet birth date
**Root cause (confirmed):** SVC:234 `pet.BirthDate == default ? null : ToUtc(pet.BirthDate)`;
MAP:16/38 map a null `BirthDateUtc` to `(default).UtcDateTime` (0001-01-01). So "unknown" and a
genuine min-date are indistinguishable; UI shows 0001-01-01 for unknown births unless special-cased.
`PetSummary.BirthDate`/`PetDetail.BirthDate` are **non-nullable** `DateTime` (CMODEL:77, DMODEL:98).
**Decision (make the optional date nullable end-to-end — the correct fix; contained to Contracts +
mapping + any UI that formats it):**
- Contracts: change `PetSummary.BirthDate` (CMODEL:77) and `PetDetail.BirthDate` (DMODEL:98) to
  `DateTime?`. (`NewPetRequest.BirthDate` is already `DateTime?` — DMODEL:150.)
- MAP:16/38: map `m.BirthDateUtc?.UtcDateTime` (yields `null` when absent) instead of
  `(m.BirthDateUtc ?? default).UtcDateTime`.
- SVC:234: `pet.BirthDate is { } bd ? ToUtc(bd) : null` (drop the `== default` sentinel).
- **UI impact:** every binding/formatter reading `.BirthDate` must handle null. Grep
  `BirthDate` across `src/Animora.Desktop/Pages/Patients/**` and the Jalali date formatter usages;
  show an em-dash/"نامشخص" for null. This is the main work — enumerate and fix each consumer the
  compiler flags (nullable `DateTime?` won't implicitly satisfy a `DateTime` binding converter).
**CAUTION:** Contracts change ripples to Desktop + possibly tests. Build full; fix all consumers.
If the diff grows large, push immediately.
**Files:** CMODEL, DMODEL, MAP, SVC, Desktop Patients consumers of `BirthDate`.
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "fix(patients): nullable BirthDate end-to-end (stop default-date-as-null sentinel) [P19]"`

---

### [x] T13 — P20 + P14 (P2) · Unbounded directory contract + wizard full-enumeration
**Root cause (confirmed):** SVC `GetPetsAsync`/`GetOwnersAsync` (SVC:85-107) route to
`ReadDirectoryAsync` (SVC:142-158) which loops `page++` until `page*pageSize >= total` — only the
page size (200) is bounded, not the total row count. WIZ:102-103 (`AddPatientWizardViewModel`) calls
both per open → pulls the whole owner+pet directory into RAM (with the per-pet weight subquery).
At 50k rows this is ~250 round-trips. These unpaged members are the whole-table read C01 claims to
have removed, still reachable via the wizard.
**Decision (retire the unpaged members; move the wizard onto the paged/search API):**
1. Change WIZ to use server-side search instead of full enumeration: replace `GetOwnersAsync()`/
   `GetPetsAsync()` (WIZ:102-103) with `GetOwnersPageAsync(query)` driven by the wizard's typed term
   (reuse `PatientsDirectoryQuery` with `Search: <typed>`, small `PageSize` e.g. 25, `Page: 1`).
   **Read WIZ first** to see how the returned list is consumed (it likely populates an owner picker);
   wire the typed-term lookup (debounced if a textbox) to a single paged call. If the wizard only
   needs an owner **picker**, a top-N page on search is sufficient.
2. Remove `GetPetsAsync()`/`GetOwnersAsync()` from `IPatientsQueryService` (IQS — confirm they are
   the unpaged ones) and from SVC, plus `ReadDirectoryAsync`. If any other caller exists (grep
   `GetOwnersAsync()\|GetPetsAsync()` across `src/`), migrate it to the paged API too.
**CAUTION:** this removes interface members → every caller must move to paged. Medium diff; if large,
push immediately. If WIZ's UX genuinely needs a full list (it should not — it's a picker), hard-cap
instead and document — but default is retire + paged search.
**Files:** WIZ, IQS, SVC. Possibly other callers + tests.
**Verify:** `scripts/sandbox/build.sh full` 0/0, then `scripts/sandbox/run.sh` → `SMOKE RUN OK`
(wizard is a navigable modal).
**Push:** `scripts/git/push.sh "refactor(patients): retire unpaged directory reads; wizard uses paged search [P14,P20]"`

---

### [x] T14 — P21 (P2) · Every create/update/weight does a full detail re-read
**Root cause (confirmed):** SVC `CreateOwnerAsync` (SVC:194), `CreatePetAsync` (SVC:209),
`SaveOwnerAsync` (SVC:223), `SavePetInfoAsync` (SVC:238), `AddWeightEntryAsync` (SVC:251) each end
with `(await GetOwnerAsync(...))!` / `(await GetPetAsync(...))!`. For pets that re-read runs the
weight subquery (P07) + full detail projection; for owners the pet-count subquery (P08). A weight
append (hot path) pays a full pet-detail round-trip just to return the DTO.
**Decision (accept-and-document for now; a lighter targeted re-read is a larger refactor):** the
"return the mutated projection from the command" option requires the command handlers to return a
read model (they return `Unit`/id today). That is a cross-layer change. **Chosen minimal action:**
- Keep the read-after-write (callers legitimately need the updated DTO) but ensure the re-read is the
  **lightest** path: confirm `GetPetAsync`/`GetOwnerAsync` project only what the DTO needs
  (they do — see SQL `GetPetAsync`/`GetOwnerAsync`). No extra query beyond the documented N+1 which
  is index-backed after migration 023 (`IX_WeightEntries_PetId_MeasuredUtc`) and
  `IX_Pets_TenantId_OwnerId` (pet-count).
- Add a short code comment at the top of the write methods block in SVC noting the read-after-write
  is intentional (callers need the fresh DTO) and is index-backed, and that returning the projection
  from the command is a deferred optimization.
- **No functional change** unless profiling (out of scope) shows a hot-path problem. This task is a
  documented acceptance, matching the audit's "accept the indexed per-row cost as a documented
  trade-off" guidance for the sibling P08.
**Files:** SVC (comment only).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "docs(patients): document intentional index-backed read-after-write on writes [P21]"`

---

### [ ] T15 — P07 + P08 (P2) · Correlated last-weight & pet-count subqueries (denormalization decision)
**Root cause (confirmed):** SQL:37-38 (directory) + SQL:131-132 (owner sheet) project each pet's
latest weight via a per-row correlated subquery on `WeightEntry`. SQL:61 (directory) + SQL:123
(owner detail) run `Count(p => p.OwnerId == o.Id)` per owner row.
**Already mitigated:** `IX_WeightEntries_PetId_MeasuredUtc` (migration 023) turns the weight subquery
into an index-seek+top-1; `IX_Pets_TenantId_OwnerId` (PETCFG:13) backs the pet COUNT. So both are
index-backed per-row operations today, not scans.
**Decision (accept the indexed per-row cost; DEFER denormalization):** denormalizing
`CurrentWeightKg` onto `Pet` (updated on weight append) and `PetCount` onto `Owner` (incremented on
pet create/soft-delete) is a schema + write-path change (new columns, migration, invalidation logic,
and a backfill) — higher risk than its payoff for a local single-clinic SQLite DB with bounded page
sizes (≤200). The audit itself frames P08's fix as "or accept the indexed per-row count as a
documented trade-off". **This task = documented acceptance only, no code change**, EXCEPT a concise
note so a future session does not re-audit:
- Add a short `// Perf note (P07/P08): ...` comment block above the two projections in SQL stating:
  (a) both are per-row correlated subqueries, (b) index-backed by `IX_WeightEntries_PetId_MeasuredUtc`
  / `IX_Pets_TenantId_OwnerId`, (c) denormalization (`Pet.CurrentWeightKg`, `Owner.PetCount`) is the
  future optimization if a measured hot-path appears, (d) deferred intentionally (bounded page size).
**Files:** SQL (comment only).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "docs(patients): record P07/P08 subqueries are index-backed; denorm deferred [P07,P08]"`

---

### [ ] T16 — P11 + P12 (P2) · Grid virtualization defeated by Chunk(3)+UniformGrid + nested ItemsControls
**Root cause (confirmed):** VM `PublishOwners`/`PublishPets` (VM:445,451) do `.Chunk(3)` into
`*CardRowViewModel`; PV (~686-742) renders each row's `Cards` in a non-virtualizing
`UniformGrid Columns="3"` inside the virtualized `ListBox`. Virtualization works at *row* granularity,
not *card* — realizing a row realizes 3 heavy cards. P12: each owner card nests multiple
non-virtualized `ItemsControl`s (contact lines, tags, pets) (OCARD builds ContactLines/Tags/Pets;
PV renders them).
**Decision (HIGHER-RISK UI rework — do this LAST among code tasks, isolated, verify with smoke run):**
Replace the `Chunk(3)` + `UniformGrid` with a virtualizing wrap layout so cards virtualize
individually.
1. **Read PV first** (the grid regions ~686-742 for owners and the pets equivalent) and VM
   `PublishOwners`/`PublishPets` + the `*CardRowViewModel`/`*CardRowViewModel` types.
2. In PV, swap the grid `ListBox`+`UniformGrid` rows for an `ItemsRepeater` with a
   `UniformGridLayout` (Avalonia) **or** a `ListBox` whose `ItemsPanel` is a virtualizing
   `WrapPanel`, bound directly to the flat `FilteredOwners`/`FilteredPets` (NOT the chunked rows).
   Prefer the simplest that virtualizes: Avalonia's `ItemsRepeater` + `UniformGridLayout`
   (`MinItemWidth`, `MinItemHeight`, `MaximumRowsOrColumns="3"`).
3. In VM, **remove** the `.Chunk(3)` publishing + the `OwnerRows`/`PetRows` row collections and the
   `*CardRowViewModel` types if nothing else uses them (grep first). Bind the grid to the flat
   card collections.
4. P12 flattening (optional within this task, keep token/scope lean): if the contact-lines block is
   a fixed arity (OCARD shows 4 contact lines — confirm), render them as explicit `TextBlock`s
   instead of an `ItemsControl`. Tags/pets are variable → leave as-is unless trivial.
**CAUTION:** XAML layout change is the riskiest item here — it can regress the visual grid. Do it
alone, build Desktop, and **smoke-run** to confirm the page renders. If it balloons in size/risk,
push immediately with a clear message and consider splitting owners-grid and pets-grid into two
pushes. If `ItemsRepeater` is not already referenced, confirm the Avalonia package provides it before
committing to it (grep existing `ItemsRepeater` usage in the repo for a working precedent).
**Files:** PV, VM (+ maybe delete `*CardRowViewModel.cs`), optionally OCARD.
**Verify:** `scripts/sandbox/build.sh` (Desktop) 0/0, then `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
**Push:** `scripts/git/push.sh "perf(patients): virtualizing grid (ItemsRepeater) replaces Chunk(3)+UniformGrid [P11,P12]"`

---

### [ ] T17 — P13 (P2) · Age/Recent sort indexes — VERIFY ONLY (already shipped)
**Status:** `IX_Pets_TenantId_BirthDateUtc` (PETCFG) and `IX_Owners_TenantId_RegisteredUtc` (OWNCFG)
already exist, created by migration `023_PatientsPerfIndexes`. The directory sorts (SQL:30
`BirthDateUtc`, SQL:53-54 `RegisteredUtc`) are therefore index-backed. **No code change.**
**Do:** confirm the two indexes are present in BOTH the EF config (PETCFG/OWNCFG) and
`AnimoraLocalDbContextModelSnapshot.cs`, and that migration 023's `Up` creates them. If all present
→ tick this task done with no commit. Only if a gap is found, add the missing piece and push.
**Verify:** grep `IX_Pets_TenantId_BirthDateUtc` and `IX_Owners_TenantId_RegisteredUtc` in configs,
snapshot, and migration 023 → all three places each.
**Push (only if a fix was needed):** `scripts/git/push.sh "fix(persistence): ensure age/recent sort indexes present [P13]"`

---

### [ ] T18 — P22 (P2) · Read model dead columns / placeholder projections (keep marked)
**Root cause (confirmed):** `PetSummaryReadModel` carries `LastVisitUtc`/`LastVisitType` always
projected `null` (SQL:39,132 hardcode `null, null`); MAP:17 maps `HasVaccines:false`,
`NextVaccine*:null` unconditionally. OCARD:72-81 computes `LastVisitText`/`HasVaccines` from
always-empty data. These are **documented C03 placeholders** (visit/vaccine source arrives in backend
phase C03) — acceptable, but they read as live features and risk masking a wiring gap when C03 lands.
**Decision (keep as placeholders, make the deferral explicit — no behavior change):**
- Add a one-line comment at SQL:39 and SQL:132 (the `null, null` projections): "C03 placeholder —
  no visit source yet; populated when the Visits context lands."
- Add a one-line comment at MAP:17 (the `HasVaccines:false, NextVaccine*:null`) with the same note.
- In OCARD (the `LastVisitText`/`HasVaccines` computed members ~72-81), add a comment that these read
  from C03-placeholder fields and currently render empty by design; do not add UI that implies live
  data. **No logic change.**
**Files:** SQL, MAP, OCARD (comments only).
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "docs(patients): mark LastVisit/HasVaccines as C03 placeholders (no live source yet) [P22]"`

---

### [ ] T19 — Final verification pass
**Do:**
- `scripts/sandbox/build.sh full` → **0/0**.
- `scripts/sandbox/run.sh` → `SMOKE RUN OK` (confirms DI + migrations apply; the VM readiness-gate
  injection + the grid rework + any Contracts change all load). **Only run; no long test suite.**
- Quick self-review of the cumulative diff against the task list: every P1 (T01-T05) applied; P2s
  T06-T18 applied or documented as decided; no stray TODOs; no new roadmap files created.
- If the build surfaces **pre-existing** stale test signatures (e.g. from T04's removed `status`
  param or T12/T13's Contracts changes), fix them to the new signatures (this is expected and
  allowed — the solution must build clean). Do NOT add new test logic.
**Push (if any fixup was needed):** `scripts/git/push.sh "test(patients): build + smoke green after audit remediation"`

---

### [ ] T20 — Sync branches & confirm (NO PR per user instruction — direct to both branches)
**User rule:** apply changes **directly**, keep `main` and `genspark_ai_developer` fully synced,
**do not open a PR**. All task pushes went to `genspark_ai_developer` via `push.sh`. To mirror onto
`main` without a PR:
```bash
git fetch origin --prune
git push origin origin/genspark_ai_developer:main      # fast-forward main to the work branch
git fetch origin --prune
# verify both point at the same SHA:
[ "$(git rev-parse origin/main)" = "$(git rev-parse origin/genspark_ai_developer)" ] && echo "✅ synced"
```
If `main` has protection that rejects a direct push, report it to the user (they asked for no PR);
otherwise both branches must end identical. Provide the user the final SHA + branch state.
**Push:** (this task performs the mirror push above; no `push.sh` needed.)

---

### [ ] T21 (FINAL) — Delete this plan file
**Do:** `rm /home/user/webapp/Plan/patients-owners-audit-fixes.md` (and `rmdir Plan` only if empty).
Touch **nothing else** — no other files, no roadmap files, no the source audit doc
(`docs/PATIENTS_OWNERS_AUDIT_2026-10-02.md`) — leave it; only THIS plan file is removed.
**Push:** `scripts/git/push.sh "chore(plan): remove completed Patients & Owners audit plan"`
then re-run T20's mirror push so `main` also reflects the deletion.

---

## Appendix — finding → task map (all 22 covered)
| Finding | Task | Kind |
|---|---|---|
| P01 | T01 | fix |
| P02 | T02 | fix |
| P03 | T03 | fix |
| P04 | T04 | refactor (remove dead) |
| P05 | T05 | fix |
| P06 | T02 | fix |
| P07 | T15 | doc (index already shipped) |
| P08 | T15 | doc (accept indexed) |
| P09 | T06 | perf |
| P10 | T07 | perf |
| P11 | T16 | perf (UI rework) |
| P12 | T16 | perf (UI rework) |
| P13 | T17 | verify only (shipped) |
| P14 | T13 | refactor |
| P15 | T08 | perf |
| P16 | T09 | doc/clarify |
| P17 | T10 | fix |
| P18 | T11 | fix |
| P19 | T12 | fix |
| P20 | T13 | refactor |
| P21 | T14 | doc (accept) |
| P22 | T18 | doc/mark |

**Principle applied throughout:** do not add features the audit did not ask for; prefer the
lowest-risk fix that removes the defect; document (not silently keep) every intentional deferral;
push after each task; keep both branches synced; delete this plan last.
