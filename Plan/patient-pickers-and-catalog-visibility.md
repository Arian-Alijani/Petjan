# Plan — Unify patient/owner pickers on the booking pattern + fix catalog (کالا/خدمات) visibility

> **Self-contained execution plan.** A fresh model must implement every task below **without
> re-auditing the repo**. All file paths, the verified current code, the root cause, the chosen fix
> and acceptance criteria are embedded here. Read §0 once, then execute tasks top-to-bottom.
>
> **🔒 LOCKED RULE — push after EVERY task:** after completing **each** task below, immediately run
> `scripts/git/push.sh "type(scope): message"`. **Not pushing = project death / your failure.**
> - Exit `3` (no credentials) → call the `setup_github_environment` tool once, then re-run the command.
> - Exit `2` (rebase conflict, repo left clean) → resolve preferring remote, then re-run.
> - Work on branch `genspark_ai_developer`. The FIRST push uses `scripts/git/push.sh -b genspark_ai_developer "msg"`;
>   every subsequent push stays on that branch with `scripts/git/push.sh "msg"`.
> - If a heavy fix / large block of code is written mid-task, push it immediately with a clear message — do not batch.
>
> **The final task (T8) deletes ONLY this file** (`Plan/patient-pickers-and-catalog-visibility.md`).
> Do **not** touch the other two Plan files, any docs, or any roadmap file.

---

## 0. Context (read once, do not re-derive)

### 0.1 App + stack
- **App:** Animora (repo "Petjan") — self-hosted veterinary clinic management + accounting **desktop** app.
  .NET 10, **Avalonia + MVVM** (CommunityToolkit.Mvvm), Persian/RTL UI, Jalali calendar, local encrypted SQLite,
  offline-first. UI files are `.axaml` (Avalonia), not WPF.
- **Namespace layout:** `src/Animora.Desktop` (views + VMs), `src/Animora.Contracts` (interfaces + DTOs),
  `src/Animora.Client.Services` (service impls over the dispatcher), `src/Animora.Application`
  (use cases / queries / commands), `src/Animora.Persistence.Sqlite` (EF queries), `src/Animora.Domain`.

### 0.2 THE ONE ARCHITECTURAL RULE (never break)
Data flows: **View → ViewModel → `Animora.Contracts` interface (constructor DI) → real
`Animora.Client.Services` impl → `Animora.Application` dispatch → `Animora.Domain` + `Animora.Persistence.Sqlite`.**
- ViewModels depend **only** on Contracts interfaces — never on EF/SQL or a concrete service.
- **No sample/mock/design-time branching in `Pages/**`.** Seed data lives in Persistence seeders.
- To add a data need: extend the interface + DTO in `Contracts/<Area>/`, implement in the matching
  `Client.Services/<Area>/` over the dispatcher. The Application query records and SQL handlers for the
  three screens below **already accept a search term** (see §0.6) — most tasks only thread it through
  the Contracts signature + Client.Services + the Desktop VM.
- A toast must never claim success unless the command returned success.

### 0.3 Build / verify (focused only — per user: NO full test runs, NO builds unless unavoidable)
- The user explicitly forbids: running the full test suite, writing tests, and taking builds/benchmarks.
- Do **not** run `scripts/sandbox/build.sh`, the test suite, or a smoke run as a routine step.
- Keep each change small, idiomatic and self-consistent; re-read the file you edited to confirm correctness
  instead of compiling. (If you ever introduce a genuinely large/structural change you are unsure about, a
  single fast Desktop build is tolerable — but default to NOT building.)

### 0.4 THE REFERENCE PATTERN (the gold standard every picker must match)
The target UX/visual is **نوبت‌ها → ثبت نوبت جدید → انتخاب بیمار و مالک**, implemented by the Appointments
**Booking panel**:
| Concern | Path |
|---|---|
| Reference VM | `src/Animora.Desktop/Pages/Appointments/BookingPanelViewModel.cs` |
| Reference View (patient block = lines ~140–315) | `src/Animora.Desktop/Pages/Appointments/BookingPanelView.axaml` |
| Reference styles (`pat-card`, `pat-search-input`, `pat-opt`, `pat-empty`, `pat-new`, `mini-chip`) | `src/Animora.Desktop/Pages/Appointments/BookingPanelStyles.axaml` |
| Reference row/chip VMs | `src/Animora.Desktop/Pages/Appointments/BookingPanelItemViewModels.cs` (`BookingPatientOptionViewModel`, `BookingKindChipViewModel`) |
| Reference seam | `IAppointmentBookingService.SearchPatientsAsync(search, speciesKey, ct)` → `BookingPatientsQuery` |

**The pattern's behavior (verified in `BookingPanelViewModel.cs`):**
1. **No eager full-list load.** The patient list starts empty; nothing is fetched until the user types.
2. **Min 2 chars required.** `canSearch = PersianText.Normalize(query).Length >= 2`. Below that, the list
   stays empty and a hint shows: «برای انتخاب بیمار، حداقل ۲ حرف از نام یا شماره تماس را وارد کنید.»
3. **Debounced** server search: `_queryDebounce.Debounce(() => _ = SearchPatientsAsync())`
   (`Debouncer` is in `Animora.Desktop.Controls`; booking uses it via `QueueChars`/`_queryDebounce`).
4. **Cancellation + version guard:** `_patientSearchVersion` (monotonic int) + a `CancellationTokenSource`
   so a slow earlier response never overwrites a newer query's rows. See `StopPatientSearch()` / `SearchPatientsAsync()`.
5. **Bounded output:** `PatientPageSize = 20`; `rows.Take(PatientPageSize)`. The ScrollViewer is `MaxHeight=260`.
   A "showing first 20, refine your search" message appears when more rows exist.
6. **Result-count + empty/searching messages:** `PatientResultCount` ("۳ نتیجه"), `IsPatientSearching`
   (shows an indeterminate `ProgressBar`), `ShowPatientSearchMessage`, `PatientSearchMessage`.
7. **Selected-patient card** (`IsPatientCardVisible`, `pat-card`): avatar tile, name + kind badge, owner +
   phone rows, a «تغییر بیمار» button that flips back to the picker (`IsPatientSearchVisible`).
8. **Species filter chips** (`BookingKindChipViewModel`: همه/سگ/گربه/پرنده/سایر) passed to the server as
   `speciesKey`.

> **Reuse first, don't reinvent:** the booking VM's search state machine + the AXAML blocks (lines ~204–311
> picker pane; lines ~147–203 selected card; `BookingPanelStyles.axaml` classes) are the template to port.
> Keep styling consistent with each destination page (colors/resources already in each page's styles).

### 0.5 The four target pickers (current state — verified)

| # | Screen / entry | Picker lives in | Current behavior (the defect) |
|---|---|---|---|
| **A** | **واکسیناسیون → ثبت واکسن → کادر انتخاب بیمار** | `Pages/Vaccination/AddVaccineModalViewModel.cs` + `AddVaccineModalView.axaml` | Patient is a **plain `PatientQuery` text box** (`AddVaccineModalView.axaml` line 26, `TextBox Text="{Binding PatientQuery}"`). No search, no list, no selection, no owner. It is a **design mock** (see §0.7). |
| **B** | **پانسیون → پذیرش جدید → جست‌وجوی بیمار** | `Pages/Boarding/NewStayModalViewModel.cs` + `NewStayModalView.axaml` | **Eager-loads ALL pets** into `_allPets` via `GetKnownPetsAsync()` in `LoadAsync`, then filters **client-side** in `ApplyPetFilter()` with `.Contains()`. No min-length, no server search, no debounce. (`IsExistingMode` picker.) |
| **C** | **فروش و صندوق → فاکتور جدید → انتخاب مشتری** | `Pages/Sales/OwnerPickerModalViewModel.cs` + `OwnerPickerModalView.axaml` | Already server-side + debounced (`GetOwnersAsync(query)`, `Debouncer`, `_generation` token) **but loads ALL owners when the query is empty** (no min-length gate; `FindOwnersAsync` returns `Take(100)` of everything). Closest to the pattern; needs only the min-length gate + matching messages/visuals. |
| **D** | **ویزیت و پرونده → انتخاب بیمار** | `Pages/Visits/VisitsTriagePageViewModel.cs` + `VisitsTriagePageView.axaml` | **Eager-loads patients** via `GetPatientsAsync()` (server returns top 50 by recent visit, search=null) into `_allPatients` in `LoadAsync`, then filters **client-side** in `BuildPatientMatches()` with `.Contains().Take(6)`. No min-length gate, no debounce on the server call. |

### 0.6 Backend search plumbing that ALREADY EXISTS (use it — do NOT add migrations for pickers)
- **Visits (D):** `TriagePatientsQuery(string? Search)` → `ITriageQueries.GetRecentPatientsAsync(search, ct)` →
  `TriageQueries.cs` already does `EF.Functions.Like(p.SearchKey, "%term%")` server-side, `Take(50)`.
  The Client.Services method `VisitsTriageService.GetPatientsAsync()` currently passes `new TriagePatientsQuery(null)`.
- **Boarding (B):** `BoardingKnownPetsQuery(string? Search = null)` → `IBoardingQueries.GetKnownPetsAsync(search, ct)`
  → `BoardingQueries.cs` already filters by search server-side, `Take(200)`.
  `IBoardingQueryService.GetKnownPetsAsync()` currently passes no search.
- **Sales (C):** `OwnerLookupQuery(string? Search = null)` → `ISalesQueries.FindOwnersAsync(search, ct)` →
  `SalesQueries.cs` does `Like(o.SearchKey, "%term%")`, `Take(100)`, **but runs even when search is empty**
  (returns the first 100 owners). Fix is a VM-side min-length gate (preferred — no server change needed),
  optionally also returning `[]` for empty search in the VM.
- **Appointments (reference):** `BookingPatientsQuery` → `AppointmentQueries.GetPatientsAsync(...)` returns `[]`
  for `< 2` chars and is bounded to 21 rows. This is the behavior to emulate everywhere.

> **Key implication:** none of the four picker tasks need a DB migration or new SQL. They thread the search
> term through the **Contracts interface signature** (add `string? search` / `string search`) + the
> **Client.Services** call + the **Desktop VM** (debounce + min-length + version/cancellation + bounded render).

### 0.7 Vaccination mock caveat (scope for Task A)
`AddVaccineModalViewModel` is documented in its own header as a **design mock**: `Submit()` just shows
«واکسن ثبت شد (نمونه طراحی)» and the fields are not persisted. **Task A's scope is limited to the
«کادر انتخاب بیمار» (the patient-selection box)**: replace the single `PatientQuery` text box with the
**reference patient picker** (server search, min-2, debounce, bounded list, selected-patient card with owner
+ phone). Do **NOT** try to wire a full vaccination persistence backend (out of scope; the submit stays a
mock as-is). The modal is opened from `VaccinationPageViewModel.OpenAddVaccine()` (line 484–486):
`new AddVaccineModalViewModel(_catalog, _dialogs, _toast)` — the ctor must gain the patient-search seam
(`IPatientsQueryService` or a booking-style search service, see §0.8) via DI from that call site.

### 0.8 Which search seam Task A should use
`VaccinationPageViewModel` already depends on patient data (`RefreshOnDataChange(..., DataDomain.Patients)`).
Two viable seams already exist and return pet+owner+phone+species:
- **Preferred:** `IAppointmentBookingService.SearchPatientsAsync(search, speciesKey, ct)` returning
  `BookingPatient(Id, Name, Kind, OwnerName, Phone, AvatarTone, SpeciesKey, SpeciesIconKey)`. This is the
  exact DTO the reference picker + `BookingPatientOptionViewModel` already render, so Task A can reuse
  `BookingPatientOptionViewModel` / `BookingKindChipViewModel` verbatim. Check it is registered in
  `src/Animora.Desktop/App.axaml.cs` (it is used by Appointments, so it is). Inject it into
  `VaccinationPageViewModel` (ctor) and pass it into `AddVaccineModalViewModel`.
- Alternative: `IPatientsQueryService.GetPetsPageAsync(PatientsDirectoryQuery)` (bounded page). More work to
  map to a display row. **Use the preferred booking seam** unless it is somehow unavailable.

### 0.9 THE CATALOG (کالا/خدمات) VISIBILITY BUG — root cause (verified, no re-derivation needed)
User symptom: after adding a product (کالا) in Inventory, it does **not** appear when adding items in
Boarding / during a Visit; sometimes even services (خدمات) are not visible.

There are **two independent, verified defects**, both in the **Sales catalog path** (which Sales **and**
some flows reuse). Boarding and Visits use their own product-level queries and are mostly fine — see below.

**Defect C1 — Sales catalog inner-joins on variants → products without an active variant vanish.**
`src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs` `GetCatalogAsync` (lines ~145–167):
```csharp
var products = await (from p in db.Set<Product>().AsNoTracking()
    where p.IsActive
    join c in db.Set<ProductCategory>()... into categories
    from c in categories.DefaultIfEmpty()
    from v in p.Variants          // ← INNER join over variants
    where v.IsActive              // ← drops any product whose variants are all inactive / none
    orderby p.Name, v.VariantId
    select new SalesCatalogItemReadModel(v.VariantId, p.Name, ...)).Take(1000)...
```
A product with no active variant (or zero variants) produces **zero** catalog rows → it never shows in Sales.
Services are created as stock-free `ProductKind.Service` products (via the «نرخ خدمات» editor) and, depending
on how they were created, may also lack an active `ProductVariant`, which is why **services can disappear too**.
**Fix (C1):** make the product projection variant-independent for the catalog row, OR left-join variants and
fall back to the product's own default price when no active variant exists. Chosen approach: project one row
**per product** using the product's `DefaultSalePrice` when it has no active variant, and per active variant
otherwise — so every active product appears at least once. Mirror the Visits/Boarding product-level approach
(they already use `db.Set<Product>().Where(p => p.IsActive)` directly — see `RxCatalogQueries.cs` and
`BoardingQueries.GetSelectableAsync`). Keep the `Take(1000)` bound. Preserve the existing
`SalesCatalogItemReadModel` shape (Id, Title, Kind "Service"/"Item", Category, PriceMinor). For products with
active variants keep per-variant rows (so a variant id can still be added to an invoice); for products with
no active variant, emit a single row keyed by a stable product-level id the add path accepts.
> ⚠️ Before changing the id scheme, verify the **add path** (`SalesQueries`/`AddLine`, `SalesMapping`
> `KindOfCatalogId`, and `InvoiceLine.CatalogItemId`) can resolve the id you emit for the no-variant case.
> If adding by product-id is not supported downstream, the safe minimal fix is: keep variant rows, and
> additionally emit a product-level fallback row **only** when the product has no active variant, mapping its
> add to the first (any) variant or surfacing a clear "no sellable variant" message. Pick the smallest change
> that makes every active product visible without breaking `AddLineAsync`.

**Defect C2 — Sales category filter chips never match → clicking a category shows 0 items.**
- `src/Animora.Client.Services/Sales/SalesMapping.cs` `ToContract(SalesCatalogReadModel)` (lines ~202–238)
  builds category chips with `Key = Slug(item.Category)` where `Slug` (lines ~408–420) **drops every
  non-ASCII char** → every Persian category name collapses to `"misc"` (all Persian letters removed).
- But each `CatalogItem.GroupKey` is set to the **raw Persian** `item.Category` in
  `ToContract(SalesCatalogItemReadModel)` (line ~241): `new(row.Id, kind, title, row.Category, row.Category, ...)`
  — i.e. `GroupLabel = GroupKey = Persian DisplayName`.
- In `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs` `Render()` the category branch is
  `list = list.Where(x => x.GroupKey == _filterKey)` where `_filterKey` is the chip's `Key` (the slug).
  **slug ("misc") never equals the Persian GroupKey → zero rows.** Also every Persian category slugs to the
  **same** "misc", so distinct categories collide.
**Fix (C2):** make the chip `Key` and the item `GroupKey` use the **same** stable value. Two clean options —
pick ONE and apply consistently:
  - (Preferred, minimal) In `SalesMapping.ToContract(SalesCatalogItemReadModel)` set the item's `GroupKey`
    to `Slug(row.Category)` (the same slug the chips use), keeping `GroupLabel = row.Category` for display.
    Then chip `Key == item.GroupKey` for real categories. **Also fix `Slug`** so distinct Persian labels don't
    all collapse to "misc": when the slug would be empty/"misc", derive a stable key from the label (e.g. a
    short hash or an index) so each category chip is unique. (The `"all"`/`"svc"`/`"item"` keys are already
    ASCII and must stay as-is; `Render()` handles `"svc"/"item"` via the `x.Kind` branch before the category branch.)
  - (Alternative) Keep `GroupKey = Persian` and change the chip `Key` to the Persian category too (and compare
    on the Persian value). Less clean because keys are meant to be stable/ASCII; prefer the first option.

**Boarding/Visits catalog sanity (verified — likely already OK, confirm only, do not over-edit):**
- `RxCatalogQueries.cs` (Visits) already returns **every active product** as Item + Services first, product-level
  (no variant join). Comment in the file documents a prior fix for PetShop products not showing. ✔
- `BoardingQueries.GetSelectableAsync` (Boarding) returns all active `BoardingServiceDef` + all active `Product`
  (product-level), `Take(500)`. Its modal filter (`BoardingCatalogModalViewModel`) keys chips by
  `source.ToString()` and filters by `x.Source.ToString() == _filterKey` → **consistent**, works. ✔
- Visits reuses the shared **Sales `CatalogModalViewModel`** but builds its own `CatalogItem`s with
  `GroupKey = "svc"/"item"` and only `all/svc/item` chips (see `VisitEncounterViewModel.BuildCatalogSnapshot`),
  so it hits the `x.Kind` branch, not the broken category branch → Visits catalog filter works. ✔
- **Therefore the catalog-visibility fix is C1 + C2 in the Sales path only.** After fixing C1/C2, confirm the
  three surfaces (Sales invoice, Boarding stay items, Visit rx/items) each show newly-created products/services.
  Boarding/Visits should already be correct; if a product still fails to appear there, the remaining cause is a
  `p.IsActive == false` product or the `Take()` window — note it but do not expand scope without evidence.

### 0.10 Shared helpers you will reuse (confirmed to exist)
- `Animora.Desktop.Controls.Debouncer` — `new Debouncer(TimeSpan.FromMilliseconds(160–200))`, `.Debounce(Action)`.
- `Animora.Desktop.Controls.RangeObservableCollection<T>` — `.ReplaceAll(IEnumerable<T>)` (batch update, §5-friendly).
- `Animora.Application.Text.PersianText.Normalize(string)` / `.Contains(a,b)` / `.Fold(string)` — folding for Persian/Arabic.
- `Animora.Desktop.Localization.PersianNumberFormatter` / `JalaliCalendar` — digit + date helpers.
- Error→toast: `Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(ex, fallback)` +
  `Tone.Danger` (the seam throws `InvalidOperationException`/`ServiceException` carrying the error code).
- Booking row/chip VMs reusable for Task A: `BookingPatientOptionViewModel`, `BookingKindChipViewModel`
  (`Pages/Appointments/BookingPanelItemViewModels.cs`). The `BookingPatient` DTO carries `AvatarTone` +
  `SpeciesIconKey` for the avatar tile.

---

## Tasks

> Legend: **[status: pending|done]**. Flip to `done` only after the change is complete AND pushed.
> Each task is independently pushable. Keep diffs minimal and consistent with the destination page's style.

### T1 — Sales catalog: make every active product visible (Defect C1) [status: done]
**Files:** `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs` (`GetCatalogAsync`, ~145–167);
verify add-path compatibility in `src/Animora.Client.Services/Sales/SalesMapping.cs` (`KindOfCatalogId`,
`ToContract`) and the `AddLineAsync` chain in `SalesQueryService.cs` / `SalesQueries.cs`.
**Do:** replace the inner `from v in p.Variants where v.IsActive` join so a product with no active variant is
still returned (per §0.9 C1: per-variant rows when active variants exist; one product-level fallback row
otherwise, with an id the add path accepts — verify first). Keep `Take(1000)`, keep `SalesCatalogItemReadModel`
shape, keep Service/Item kind + category.
**Acceptance:** a newly created product/service appears in the Sales «افزودن خدمت یا کالا» drawer; adding it to
an invoice still works (no broken id). No migration added.
**Push:** `scripts/git/push.sh -b genspark_ai_developer "fix(sales): surface products without an active variant in the catalog"`
(first push on the branch; subsequent tasks use `scripts/git/push.sh "..."`).

### T2 — Sales catalog: fix category-chip filter (Defect C2) [status: done]
**Files:** `src/Animora.Client.Services/Sales/SalesMapping.cs` (`ToContract` for item + snapshot, `Slug`).
**Do:** align the chip `Key` and the item `GroupKey` (set item `GroupKey = Slug(row.Category)`, keep
`GroupLabel = row.Category`), and fix `Slug` so distinct Persian category labels produce **distinct** stable
keys instead of all collapsing to `"misc"`. Leave `all/svc/item` keys untouched (the VM's `x.Kind` branch
handles svc/item before the category branch).
**Acceptance:** in the Sales catalog drawer, clicking a category chip filters to that category's items (not an
empty list); multiple categories no longer collide.
**Push:** `scripts/git/push.sh "fix(sales): align catalog category chip keys with item group keys"`

### T3 — Visit catalog visibility sanity confirm (no code unless a real gap is found) [status: done — confirmed OK, no code change]
**Files (read-only confirm):** `src/Animora.Persistence.Sqlite/Queries/RxCatalogQueries.cs`,
`src/Animora.Desktop/Pages/Visits/VisitEncounterViewModel.cs` (`BuildCatalogSnapshot`).
**Do:** confirm Visits shows newly-created products/services (it uses product-level Rx catalog + all/svc/item
chips, per §0.9). If and only if a concrete gap is found (e.g. a product kind filtered out), fix it minimally.
Otherwise make **no** code change — this task is a guard, not busywork.
**Push (only if a change was made):** `scripts/git/push.sh "fix(visits): <concrete catalog gap>"`. If no change,
mark done and note "confirmed OK, no change" — no empty commit needed (the push script reports "nothing to commit").

### T4 — Sales «انتخاب مشتری» picker: add min-length gate + pattern messages/visuals (Target C) [status: done]
**Files:** `src/Animora.Desktop/Pages/Sales/OwnerPickerModalViewModel.cs` + `OwnerPickerModalView.axaml`.
**Current:** server-side + debounced already; but on empty query it shows the first 100 owners (eager-ish) and
lacks the reference's min-2 hint / result-count / searching indicator.
**Do:** gate `SearchAsync` so a normalized query `< 2` chars does **not** query the server — clear `Rows`,
show a hint message «برای انتخاب مشتری، حداقل ۲ حرف از نام یا شمارهٔ تماس را وارد کنید.». Keep the existing
`Debouncer` + `_generation` token. Add: `IsSearching` indicator (ProgressBar), `ResultCount` text
("N نتیجه"), and an empty-state message consistent with the reference. Keep the guest-sale option. Match the
reference look using Sales styles already on the page (avatar tile, name, file code, phone). Preserve the
`TryPickAsync` failure-keeps-modal-open behavior.
**Acceptance:** opening the picker shows the hint, not a 100-row dump; typing ≥2 chars searches server-side,
shows a count + spinner; selecting assigns the owner; guest still works.
**Push:** `scripts/git/push.sh "feat(sales): gate owner picker on min-2 search with result-count + hint like booking"`

### T5 — Boarding «جست‌وجوی بیمار» (پذیرش جدید) → server-side booking pattern (Target B) [status: done]
**Files:**
- `src/Animora.Contracts/Boarding/IBoardingQueryService.cs` — change `GetKnownPetsAsync()` to accept a search
  term: `Task<IReadOnlyList<BoardingKnownPet>> GetKnownPetsAsync(string? search = null)` (keep back-compat default).
- `src/Animora.Client.Services/Boarding/BoardingQueryService.cs` (~123) — pass the term:
  `new BoardingKnownPetsQuery(search)` (the query record + SQL already accept it, §0.6).
- `src/Animora.Desktop/Pages/Boarding/NewStayModalViewModel.cs` — stop eager-loading `_allPets` in `LoadAsync`;
  replace `ApplyPetFilter()` (client-side `.Contains`) with a debounced, min-2, version-guarded server search
  calling `_service.GetKnownPetsAsync(term)`; render bounded results (≤20) into `KnownPets`; keep the
  existing-file/manual mode switch, the selected-pet card (`SelectedPet*`, `HasSelectedPet`), `PickPet`,
  `ClearPet`, and `SubmitAsync` unchanged except for not relying on a preloaded list.
- `src/Animora.Desktop/Pages/Boarding/NewStayModalView.axaml` — update the picker block to the reference layout
  (search box with icon + clear button, result count, searching ProgressBar, bounded ScrollViewer list, empty/
  hint message), styled to the Boarding modal's existing look.
**Keep:** manual-entry mode and the stay form (room/rate/dates/payment/note) exactly as-is.
**Acceptance:** opening «پذیرش جدید» shows an empty picker with the min-2 hint (no full pet dump); typing ≥2
chars searches server-side with debounce; selecting fills the pet card; submit still creates the stay.
**Push:** `scripts/git/push.sh "feat(boarding): server-side debounced patient search in new-stay like booking"`

### T6 — Visits «انتخاب بیمار» → server-side booking pattern (Target D) [status: pending]
**Files:**
- `src/Animora.Contracts/Visits/*` (the `IVisitsTriageService` interface) — change `GetPatientsAsync()` to
  `GetPatientsAsync(string? search = null)` (the `TriagePatientsQuery(Search)` + SQL already accept it, §0.6).
  (Interface file: find the one declaring `GetPatientsAsync` — it's the triage service contract.)
- `src/Animora.Client.Services/Visits/VisitsTriageService.cs` (~50) — pass the term:
  `new TriagePatientsQuery(search)`.
- `src/Animora.Desktop/Pages/Visits/VisitsTriagePageViewModel.cs` — stop storing `_allPatients` from an eager
  load; wire `OnPatientSearchTextChanged` through a `Debouncer` (there is already a `_searchDebounce` for the
  booked list — add a separate debouncer for patient search or reuse carefully) + a min-2 gate + a version/
  cancellation guard that calls `_service.GetPatientsAsync(term)` and fills `PatientMatches` (bounded, e.g.
  ≤6–20). Keep `PickedPatient`, `CanStartNew`, `StartNewAsync`, quick-add flow, and the booked-ticket list
  untouched. Remove the client-side `.Contains()` scan in `BuildPatientMatches()` (replace its body with the
  server-driven fill, or repoint it to render the server results).
- `src/Animora.Desktop/Pages/Visits/VisitsTriagePageView.axaml` — align the patient-search block to the
  reference (hint under 2 chars, result count/searching indicator optional but consistent, bounded match list,
  selected-patient card already exists via `PickedPatient*`). Style to the Visits page's existing look.
**Keep:** doctor rail, booked-ticket list + its own search, visit-type chips, StartFromTicket flow — unchanged.
**Acceptance:** the «انتخاب بیمار» box no longer pre-loads 50 patients; typing ≥2 chars drives a debounced
server search; picking a match enables «شروع ویزیت»; starting a new visit still works.
**Push:** `scripts/git/push.sh "feat(visits): server-side debounced patient search in triage like booking"`

### T7 — Vaccination «ثبت واکسن → کادر انتخاب بیمار» → real booking-style picker (Target A) [status: pending]
**Files:**
- `src/Animora.Desktop/Pages/Vaccination/VaccinationPageViewModel.cs` — inject the patient-search seam
  (`IAppointmentBookingService`, §0.8) via the ctor and register-check it in `App.axaml.cs` (already registered,
  used by Appointments). Pass it into `AddVaccineModalViewModel` at `OpenAddVaccine()` (lines 484–486).
- `src/Animora.Desktop/Pages/Vaccination/AddVaccineModalViewModel.cs` — replace the single `PatientQuery` string
  with the reference picker state machine: `QueryText`, `Debouncer`, min-2 gate, version/cancellation guard,
  `PatientList` (`RangeObservableCollection<BookingPatientOptionViewModel>`), species `KindChips`
  (`BookingKindChipViewModel`), `IsPatientSearching`, `PatientResultCount`, `ShowPatientSearchMessage`,
  `PatientSearchMessage`, `IsPatientSearchVisible`/`IsPatientCardVisible`, a `SelectPatient` command and a
  selected-patient card (name + kind + owner + phone) with «تغییر بیمار». Reuse `BookingPatientOptionViewModel`
  / `BookingKindChipViewModel` and `IAppointmentBookingService.SearchPatientsAsync(term, speciesKey, ct)`
  verbatim. The modal's vaccine fields + mock `Submit` stay as-is (§0.7); only the patient box changes. Gate
  `Submit` so it requires a selected patient (friendly toast if none).
- `src/Animora.Desktop/Pages/Vaccination/AddVaccineModalView.axaml` — replace the line-26 `TextBox` with the
  reference picker block (port from `BookingPanelView.axaml` lines ~147–311), styled to the vaccination modal
  (reuse `BookingPanelStyles.axaml` classes or mirror them locally). Keep the rest of the modal layout.
**Acceptance:** opening «ثبت واکسن جدید» shows a real patient picker (empty + min-2 hint); typing ≥2 chars
searches server-side with debounce + species chips; selecting shows the patient card with owner + phone; the
rest of the vaccine form is unchanged; submit requires a selected patient.
**Push:** `scripts/git/push.sh "feat(vaccination): replace mock patient field with booking-style patient picker"`

### T8 — FINAL: delete this plan file only [status: pending]
**Do:** delete **only** `Plan/patient-pickers-and-catalog-visibility.md`. Do not touch any other file.
**Push:** `scripts/git/push.sh "chore(plan): remove completed patient-picker + catalog-visibility plan"`

---

## Execution order & notes
- Recommended order: **T1 → T2 → T3** (catalog visibility, highest user pain), then **T4 → T5 → T6 → T7**
  (pickers, C is smallest/closest to the pattern so it validates the approach cheaply), then **T8**.
- After each task: flip its `[status: pending]` → `[status: done]` in this file, then **push** (the push also
  commits the status flip). The LOCKED push rule in §0 is mandatory after every task.
- Do not run full builds/tests (user rule §0.3). Re-read edited files to self-verify.
- Keep every picker visually consistent with its own page's existing styles; the reference defines the
  *structure/behavior*, not a hard color override.
