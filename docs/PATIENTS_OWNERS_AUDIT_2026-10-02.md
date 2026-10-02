# Patients & Owners audit — 2026-10-02

## Scope and phase gate

- Baseline: current product source on `main`; static source review only. No build, tests, app run, benchmarks or DB execution. Findings describe code behavior; timing, N+1 cost, scroll frame-time and rendered layout remain unmeasured.
- Authority: `backend-roadmap/PROGRESS.md` (C01 shipped the Patients/Owners backend; next phase **D01, not started**) and `windows-frontend-roadmap/PROGRESS.md` (Phase 08/09 shipped the directory/sheet UI). Tracks A/B and C01–C11 done.
- Current obligation: working, authorized, durable, server-free local Patients/Owners over encrypted SQLite. Flow: Avalonia View → ViewModel → `Animora.Contracts` → `Client.Services` → Application dispatcher/permission/capability/transaction/journal → Domain + SQLite projection queries.
- **Not defects (roadmap-deferred, do not report as bugs):** no visit source yet → `LastVisit*`/"Recent" sort fall back to name, "has vaccines" filter matches nothing (both explicitly deferred to C03); `TotalSpend` closed in C09; document payload storage is D12 (C01 seeds metadata only); server/API/SQL Server (D01+), Datasync/transport (E03–E06), online licensing (E07), release/perf certification (E08–E09). Absence of these is expected.
- Reviewed: directory list/grid for owners+pets, facet counts, species chips, search/filter/sort/paging, owner sheet, pet file, add-owner/add-pet/weight write paths; their Contracts/services/queries/commands/mappings, indexes, SearchKey stamping, readiness gating and refresh. Excluded (per instruction): `Animora-UI` design reference, unrelated pages.
- **22 findings: 6 P1, 16 P2.** P1 = lost/wrong business data, access-control gap, or unreliable primary flow. P2 = incorrect secondary behavior, scaling/perf, or maintainability risk.

## Path aliases

Repo-relative; `Alias:line` resolves to exact source. Lines target the audited snapshot.

| Alias | Path |
|---|---|
| SQL | `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs` |
| PAGE | `src/Animora.Persistence.Sqlite/Queries/QueryPaging.cs` |
| WECFG | `src/Animora.Persistence.Sqlite/Configurations/Patients/WeightEntryConfiguration.cs` |
| PETCFG | `src/Animora.Persistence.Sqlite/Configurations/Patients/PetConfiguration.cs` |
| OWNCFG | `src/Animora.Persistence.Sqlite/Configurations/Patients/OwnerConfiguration.cs` |
| KEY | `src/Animora.Persistence.Sqlite/Search/ISearchKeyProvider.cs` |
| CTX | `src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs` |
| SVC | `src/Animora.Client.Services/Patients/PatientsService.cs` |
| MAP | `src/Animora.Client.Services/Patients/PatientsMapping.cs` |
| RM | `src/Animora.Application/UseCases/Patients/ReadModels/PatientReadModels.cs` |
| CREATE | `src/Animora.Application/UseCases/Patients/CreateOwnerCommand.cs` |
| UPDOWN | `src/Animora.Application/UseCases/Patients/UpdateOwnerCommand.cs` |
| UPDPET | `src/Animora.Application/UseCases/Patients/UpdatePetInfoCommand.cs` |
| WEIGHT | `src/Animora.Application/UseCases/Patients/AddWeightEntryCommand.cs` |
| VM | `src/Animora.Desktop/Pages/Patients/PatientsPageViewModel.cs` |
| PV | `src/Animora.Desktop/Pages/Patients/PatientsPageView.axaml` |
| OCARD | `src/Animora.Desktop/Pages/Patients/OwnerCardViewModel.cs` |
| WIZ | `src/Animora.Desktop/Pages/Patients/AddPatientWizardViewModel.cs` |
| CMODEL | `src/Animora.Contracts/Patients/PatientModels.cs` |

---

## Findings — correctness / data integrity (P1)

### P01 — P1 · Silent data loss · Owner edit wipes email/city/notes
- **Evidence:** UPDOWN:45–46 calls `owner.UpdateContactDetails(..., email: null, ..., city: null, notes: null)` on every edit. `UpdateOwnerCommand` (UPDOWN:17–18) carries no Email/City/Notes fields, so existing values are overwritten with null. CREATE:49–50 has the same null-wipe shape (benign only because the row is new).
- **Failure:** any owner that has an email, city, or notes (set via another path/seed/future screen) loses them the moment the owner sheet is saved — a one-shot, unrecoverable overwrite with no UI indication.
- **Fix:** carry Email/City/Notes through the command (and Contracts `OwnerDetail`), or read the current owner and preserve unspecified fields instead of passing literal nulls. Never pass `null` for a field the edit form does not own.

### P02 — P1 · Access control · Add-pet from directory bypasses owner-detail authorization boundary
- **Evidence:** VM:321–343 `AddPetAsync` calls `_patientsQueryService.GetOwnerAsync(owner.Id)` then `CreatePetAsync`. SVC:160–168 `GetOwnerAsync` builds a query context (SVC:164) and silently returns `null` when `BuildQueryContext()` fails (`qc.IsFailure → return null`), collapsing an auth/session failure into "owner not found". VM:328–330 then just returns with no feedback.
- **Failure:** a capability/session loss during the directory session surfaces as a silent no-op ("nothing happened" on the add-pet button), indistinguishable from a missing owner; the user gets no "session expired / permission denied" signal. Same silent-null pattern in `GetPetAsync` (SVC:170–178).
- **Fix:** distinguish auth failure from not-found in the detail reads (throw/propagate the `ErrorCode` like the page reads do at SVC:33,39), and surface it in the VM.

### P03 — P1 · Wrong data · Species round-trip is lossy and asymmetric (Reptile↔Rabbit)
- **Evidence:** Domain `PetSpecies` = Dog/Cat/Bird/Rodent/**Reptile**/Other (no Rabbit). Contracts `PetSpecies` = Cat/Dog/Bird/**Rabbit**/Rodent/Other (CMODEL:8–16, no Reptile). MAP:67–74 + SVC:123–130 map Domain→Contracts with `Reptile → Other`; MAP:97–105 + SVC:120–121 map Contracts→Domain with `Rabbit → Other`.
- **Failure:** (a) every reptile pet is displayed and filtered as "Other" — the species is effectively unreachable/mislabeled. (b) The "Rabbit" species chip/filter can never return rows (domain has no Rabbit; a created rabbit is stored as Other) → a permanently empty filter. (c) A pet created as "Rabbit" and reopened shows "Other" — a visible identity change.
- **Fix:** align the two enums (add Rabbit/Reptile to both, or document a single canonical set) and make the map bijective. Until aligned, hide the unreachable chip rather than show a dead filter.

### P04 — P1 · Dead feature · Clinical status filter plumbed through every layer but never wired to UI
- **Evidence:** `PetStatus? status` is a parameter on SQL:21,45,66,85 (`FilterPets`/`FilterOwners`/`GetFacetsAsync`), RM query records, and the Application queries. Every caller passes `Status: null`: SVC:36,55,75 hardcode `Status: null`; VM has no status filter field (grep `PetStatus`/`Status:` in VM → none). The toolbar exposes species + vaccine + sort only.
- **Failure:** an entire filter dimension is implemented end-to-end in the query layer but unreachable — dead code that reads as a working feature, and a latent correctness surface (the status filter is untested because it is never exercised).
- **Fix:** either wire a status filter control in the toolbar/VM (populate `Status`), or remove the unused parameter chain. Decide and document; do not leave a half-connected feature.

### P05 — P1 · Reliability · Patients page ignores the store-readiness gate (unique among Track C pages)
- **Evidence:** VM:92–95 constructor runs `UseLoadStatus(ReloadPageAsync)` + `RefreshOnDataChange(...)` + `_ = ReloadPageAsync()` immediately. VM:382–384 `LoadAsync` waits only on `_loadGate` (a concurrency semaphore), never on `IStoreReadinessGate`. Every other Track C page injects and awaits it first — e.g. InventoryPageViewModel:35,46,118 `await _readiness.WaitAsync()`. C01's log claimed "no ViewModel change required," so the gate was never added here.
- **Failure:** on cold start the first directory load races the async local-store init (migrations/seed). The query can hit a not-ready store; `LoadAsync` swallows it (VM:415–419 catch-all → toast "بارگذاری... انجام نشد") so the user sees a spurious failure toast on the landing screen instead of a readiness wait.
- **Fix:** inject `IStoreReadinessGate` and `await _readiness.WaitAsync()` at the top of `LoadAsync`, matching the other pages.

### P06 — P1 · Error masking · Load catch-all hides every failure as one generic toast
- **Evidence:** VM:415–419 catches `Exception` and shows a single Persian toast, discarding the `ErrorCode`. SVC throws `InvalidOperationException(error.Code)` for all failures (SVC:33,39,52,58,76), so permission-denied, session-expired, store-unavailable and genuine bugs are indistinguishable at the VM.
- **Failure:** operators cannot tell a transient readiness/permission issue from a real fault; diagnostics lose the originating code; a NullReference bug and "store busy" look identical.
- **Fix:** map known `ErrorCode`s to distinct messages (or at least log the code), and let unexpected exceptions bubble to the global handler rather than being flattened into the generic toast.

---

## Findings — performance / scaling (P2)

### P07 — P2 · N+1 · Correlated last-weight subquery per pet row, over an unindexed table
- **Evidence:** SQL:37–38 (directory) and SQL:131–132 (owner sheet roster) project each pet's latest weight via a per-row correlated subquery: `db.Set<WeightEntry>().Where(w => w.PetId == p.Id).OrderByDescending(w => w.MeasuredUtc)...FirstOrDefault()`. WECFG:9–12 defines only `HasKey(Id)` — **no index on `(PetId, MeasuredUtc)`**.
- **Failure:** each 50-row page does 50 correlated lookups; each lookup is a `WeightEntry` scan + sort because no supporting index exists → cost grows with the weight table, not the page. Opening an owner with many pets multiplies it.
- **Fix:** add `(PetId, MeasuredUtc DESC)` index on `WeightEntry` (migration), and/or denormalize `CurrentWeightKg` onto `Pet` (updated on weight append). Report `backend-roadmap` items P1/P2 cover this.

### P08 — P2 · N+1 · Correlated pet-count subquery per owner row
- **Evidence:** SQL:61 (directory) and SQL:123 (owner detail) run `db.Set<Pet>().Count(p => p.OwnerId == o.Id)` per owner row.
- **Failure:** one COUNT per owner per page. `IX_Pets_TenantId_OwnerId` (PETCFG:13) makes each acceptable, but it remains per-row and multiplies with large page sizes (up to 200).
- **Fix:** denormalize `PetCount` on `Owner` (incremented on pet create/soft-delete), or accept the indexed per-row count as a documented trade-off.

### P09 — P2 · Full scan · Search uses leading-wildcard `LIKE '%term%'`, defeating the SearchKey index
- **Evidence:** SQL:91 `EF.Functions.Like(p.SearchKey, $"%{term}%")` and SQL:107 for owners. Indexes `IX_Pets_TenantId_SearchKey` (PETCFG:15) / `IX_Owners_TenantId_SearchKey` (OWNCFG:15) exist but a leading `%` makes them unusable → full-table scan per keystroke (debounced 220 ms, VM:178).
- **Failure:** the single biggest typing-latency risk; cost scales linearly with row count on every search character.
- **Fix:** prefix search (`term%`) where UX allows, or SQLite FTS5 over the tokenized key. If substring match is required, document the scan and cap result size.

### P10 — P2 · Redundant DB round-trips · Facets issue 3 extra queries alongside each page load
- **Evidence:** VM:392–405 awaits `GetDirectoryFacetsAsync` in `Task.WhenAll` with the page query. SQL:65–81 `GetFacetsAsync` runs 3 statements (pet count + owner count + species GROUP BY). All share one scoped `DbContext` per call but EF serializes on a non-thread-safe context, so `Task.WhenAll` does not parallelize — effectively 5 sequential statements (3 facet + COUNT + page) per toolbar change.
- **Failure:** every keystroke/filter/page change = ~5 statements; facets recomputed even when only the page index changed (facets are identical across pages of the same filter).
- **Fix:** recompute facets only when the filter (not the page) changes; cache per filter signature. Optionally fold the owner/pet totals into fewer statements.

### P11 — P2 · Virtualization defeated · Grid layout chunks cards into `UniformGrid` rows
- **Evidence:** VM:445,451 `FilteredOwners.Chunk(3)`/`FilteredPets.Chunk(3)` → `*CardRowViewModel`; PV:686–689 renders each row's `Cards` in a non-virtualizing `UniformGrid Columns="3"` inside the virtualized `ListBox.records`. Virtualization works at the *row* granularity, not the *card*.
- **Failure:** the owner card template is deep (avatar + 4 contact lines + tags `ItemsControl` + pets `ItemsControl` + footer), so realizing a row realizes 3 heavy cards at once; scroll frame-time in grid mode rises with card complexity.
- **Fix:** use a virtualizing wrap layout (e.g. `ItemsRepeater`/virtualizing `WrapPanel`) instead of `Chunk(3)`+`UniformGrid`, and/or flatten the card template.

### P12 — P2 · Visual-tree weight · Multiple nested non-virtualized `ItemsControl` per owner card
- **Evidence:** OCARD builds `ContactLines`/`Tags`/`Pets` collections; PV renders each as a nested `ItemsControl` (PV:715–726 tags; plus contact-lines and pets controls per card). A full grid page materializes dozens of small `ItemsControl`s.
- **Failure:** individually light but cumulatively heavy; compounds P11.
- **Fix:** render fixed-arity rows (contact lines are a fixed 4 — OCARD:45–51) as explicit elements rather than an `ItemsControl`.

### P13 — P2 · Unindexed sort · "Age" sort on `Pet.BirthDateUtc` / owner `RegisteredUtc` is a filesort
- **Evidence:** SQL:30 orders pets by `BirthDateUtc`; SQL:53–54 orders owners by `RegisteredUtc`. No `(TenantId, BirthDateUtc)` on Pet (PETCFG only has OwnerId/Name/SearchKey) and no `(TenantId, RegisteredUtc)` on Owner (OWNCFG only has Phone/FullName/SearchKey).
- **Failure:** every age/recent-sorted page sorts the filtered set without index support.
- **Fix:** add `(TenantId, BirthDateUtc)` on Pet and `(TenantId, RegisteredUtc)` on Owner.

### P14 — P2 · Wasteful enumeration · Add-patient wizard loads the entire directory (2 × up-to-200 rows) per open
- **Evidence:** WIZ:102–103 `GetOwnersAsync()` + `GetPetsAsync()`. SVC:85–107 route these to `ReadDirectoryAsync` (SVC:142–158), which pages at `MaxPageSize=200` until exhausted — a whole-directory pull into RAM, including the per-pet weight N+1 (P07) for every pet.
- **Failure:** opening the add-owner wizard drags the full owner+pet directory (capped at the page ceiling per page, looped) with its N+1 weights; cost scales with directory size and is paid even when the user only types a new owner.
- **Fix:** make the wizard's in-modal lookup a server-side search (reuse `GetOwnersPageAsync` with the typed term) instead of a full enumeration.

---

## Findings — logic / robustness (P2)

### P15 — P2 · Redundant load · Constructor + `OnNavigatedTo` + change-notifier can triple-load on first entry
- **Evidence:** VM:92 `UseLoadStatus(ReloadPageAsync)`, VM:94 `RefreshOnDataChange(... ReloadPageAsync)`, VM:95 explicit `_ = ReloadPageAsync()`, and VM:347–350 `OnNavigatedTo` fires another `ReloadPageAsync` when `!IsBusy`. `_loadGate` serializes but does not dedupe queued reloads.
- **Failure:** first navigation can run 2–3 back-to-back identical loads (each ~5 statements per P10) before settling; wasted DB work and transient UI churn.
- **Fix:** a "first load done" guard so the constructor and `OnNavigatedTo` do not both trigger an initial load; coalesce redundant reloads.

### P16 — P2 · Name parsing · First/last name split by first space corrupts multi-part names
- **Evidence:** MAP:47–51 `SplitName` splits on the first space; SVC:188,219 recombine as `$"{First} {Last}".Trim()`. Write stores one `FullName` (CREATE:48), read splits it back.
- **Failure:** a last name with spaces ("محمد رضا تقوی") round-trips as First="محمد", Last="رضا تقوی"; edit→save re-joins fine, but any UI treating First/Last distinctly is wrong, and leading/duplicate spaces normalize unexpectedly. Owners with a single-token name get Last="".
- **Fix:** store first/last distinctly in the domain/command, or document FullName as the only identity and stop surfacing a structured First/Last.

### P17 — P2 · Status semantics · Domain `Deceased`/`Inactive` map to UI `Critical`/`FollowUp`
- **Evidence:** MAP:83–88 `MapStatus`: Domain `Active→Healthy`, `Inactive→FollowUp`, and the `_` default (**Deceased**) `→ Critical`.
- **Failure:** a deceased pet shows the "critical" (danger) clinical pill — a clinically wrong and potentially distressing label; "Inactive"→"FollowUp" conflates administrative inactivity with a clinical follow-up state.
- **Fix:** add explicit UI states for Inactive/Deceased (or an explicit Deceased pill), not a fall-through to Critical.

### P18 — P2 · Lossy clinical notes · Allergies/chronic conditions round-trip through a joined string
- **Evidence:** write SVC:234–235 `string.Join(", ", pet.Allergies)` / chronic; read MAP:55–57 `Split` on `[',', '،', '\n']`. A single allergy entry containing a comma ("دارو، نوع ب") is split into two on read.
- **Failure:** any note value containing the delimiter is silently fragmented; count drifts across save/reload.
- **Fix:** store the list structurally (owned rows/JSON), or escape/choose a delimiter that cannot appear in a value.

### P19 — P2 · Date handling · `default(DateTime)` used as a null sentinel for pet birth date
- **Evidence:** SVC:234 `pet.BirthDate == default ? null : ToUtc(pet.BirthDate)`; MAP:16,38 map a null `BirthDateUtc` to `(default).UtcDateTime` (01-01-0001). ToUtc (SVC:259–261) re-tags Kind.
- **Failure:** `0001-01-01` is both "unknown" and a theoretically valid value; a genuinely zero/era date and "unknown" are indistinguishable, and the UI shows the min date for unknown births unless specially handled.
- **Fix:** use `DateTime?`/`DateTimeOffset?` end-to-end for optional dates; never overload `default` as null.

### P20 — P2 · Unbounded directory contract · `GetOwnersAsync`/`GetPetsAsync` loop every page with no ceiling on total rows
- **Evidence:** SVC:142–158 loops `page++` until `page*pageSize >= total`; only the page size is bounded, not the total. The Contracts directory method is unpaged by shape (SVC:139–141 comment).
- **Failure:** at 50k rows this is ~250 sequential round-trips returning 50k rows into a `List` — the exact whole-table materialization C01 claims to have removed, still reachable via the wizard path (P14) and any future caller of the unpaged members.
- **Fix:** remove/retire the unpaged directory members, or hard-cap them and force callers onto the paged API.

### P21 — P2 · Read-after-write cost · Every create/update/weight does a full detail re-read
- **Evidence:** SVC:194,209,223,238,251 each end with `(await GetOwnerAsync(...))!` / `GetPetAsync(...)!` after the command. For pets that re-read includes the weight N+1 (P07); for owners it includes the pet-count subquery (P08).
- **Failure:** a weight append (hot path on the pet file) pays a full pet detail projection + weight list read + N+1 just to return the updated DTO.
- **Fix:** return the mutated projection from the command, or re-read with a lighter targeted query; avoid the full detail round-trip on every write.

### P22 — P2 · Maintainability · Read model carries dead columns and placeholder projections
- **Evidence:** RM:6–9 `PetSummaryReadModel` has `LastVisitUtc`/`LastVisitType` always projected as `null` (SQL:39,132 hardcode `null, null`); MAP:17 maps `HasVaccines:false`, `NextVaccine*:null` unconditionally. OCARD:72–81 computes `LastVisitText`/`HasVaccines` from always-empty data.
- **Failure:** acceptable as documented C03 placeholders, but the always-null columns and the owner-card "last visit across pets" logic (OCARD:73–79) are dead branches that read as live features and risk masking a real wiring gap when C03 lands.
- **Fix:** when C03 adds the visit source, verify these projections are actually populated; until then keep them clearly marked (they are) and avoid adding UI that implies live data.

---

## Cross-reference to the supplied performance report

Supplied-report items map to findings here: its مورد ۱ → **P07**; مورد ۲ → **P08**; مورد ۳ → **P09**; مورد ۴ → **P10**; مورد ۵ → **P11**; مورد ۶ → **P12**; مورد ۷ → **P15**; مورد ۸ → **P13**. Suggested fixes P1–P11 in that report align with the Fix lines above (index `(PetId, MeasuredUtc)`, denormalized weight/pet-count, FTS/prefix search, facet caching, virtualized grid, age indexes, first-load guard). This audit adds correctness findings not in that report: **P01** (owner edit data loss), **P02/P06** (silent/flattened error handling), **P03** (species enum mismatch), **P04** (dead status filter), **P05** (missing readiness gate), **P16–P22** (name/status/notes/date round-trip and contract issues).

## Notes

- Report only; no code changed, no fix applied, per instruction.
- Static review; no build/test/run executed, per instruction.
- Line numbers target the audited snapshot and may drift with later edits; resolve by the quoted code, not the number alone.
