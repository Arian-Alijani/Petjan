# Plan — Phase C10: History, Dashboard, Search, Profile

> **Self-contained execution plan.** All research is baked in below. A model can execute
> tasks top-to-bottom with **minimal** repo re-exploration. Read the "Baked-in facts"
> section once, then execute tasks in order.
>
> **Source phase:** `backend-roadmap/phases/PHASE-C10-history-dashboard-search-profile.md`
> **Band:** M · **Track:** C · **Depends on:** C09 (done) · **Satisfies:** R-B01, R-B02

---

## 🔒 LOCKED RULE — push after every task
After completing **each** task below, run:
```bash
scripts/git/push.sh "type(scope): message"
```
(auto: stage-all → commit → fetch → rebase --autostash → push). Exit 3 = run
`setup_github_environment` tool then re-run. **Not pushing = project death.** Also push
immediately after any heavy fix / large code block even mid-task.

The push script already handles branch `main` (current). Keep committing on the current
branch; do **not** switch branches unless a task says so.

---

## Baked-in facts (research results — do NOT re-derive)

### Deviation D-01 — migration numbers (CRITICAL)
The phase says migrations `016_ActivityFeed` / `017_UserPreferences`, but **`016` is already
taken** by C09 (`20260929041422_016_FinanceProjections`). Migrations `010`–`016` exist.
→ **Use `017_ActivityFeed` and `018_UserPreferences`.** Record this deviation in the final
PROGRESS.md entry. Migration timestamp prefix format: `YYYYMMDDHHMMSS_NNN_Name` (e.g.
`20260929050000_017_ActivityFeed`). Use a timestamp after `20260929041422`.

### Architecture: how the activity feed is fed (VERIFIED)
- Domain events flow through `IDomainEventDispatcher`
  (`src/Animora.Persistence.Sqlite/Repositories/DomainEventDispatcher.cs`). Handlers are
  registered explicitly via `AddDomainEventHandler<TEvent,THandler>()` producing
  `DomainEventHandlerRegistration` entries; the dispatcher walks the concrete event type up
  to `DomainEvent`, so a handler registered on the base `DomainEvent` (like
  `OutboxRelayHandler`) receives **every** event.
- `OutboxRelayHandler` (`src/Animora.Persistence.Sqlite/Outbox/OutboxRelayHandler.cs`)
  implements `IDomainEventHandler<DomainEvent>` and enqueues each event to the outbox in the
  same transaction. `MessageTypeFor(e) => e.GetType().Name`.
- **`ActivityFeedHandler` follows the exact same shape as `OutboxRelayHandler`**: it lives in
  `Animora.Application/Activity/` but the phase text places it there. NOTE: it needs to write
  an `ActivityEvent` entity (persistence). Two valid designs — pick **Design A** (cleaner,
  matches "written from the outbox"):
  - **Design A (chosen):** `ActivityFeedHandler` is an `IDomainEventHandler<DomainEvent>` in
    the **Application** layer that maps a domain event → an `ActivityEventDraft` (a pure
    Application record with Kind/Title-key/DetailArgs/Amount/Sign/Status/EntityType/EntityId
    /Actor). A thin persistence writer (registered in Persistence) receives the draft via an
    Application **port** `IActivityFeedWriter` and inserts the `ActivityEvent` row in the
    caller's transaction (never `SaveChanges` itself). This keeps Persian/EF out of
    Application (CONVENTIONS §4/§2). Register the handler on the base `DomainEvent` type; it
    switches on the 14 concrete types and no-ops for the rest.
  - Rationale: mirrors the outbox relay (feed "written from the outbox" = same event stream),
    keeps layering clean, and `RebuildActivityFeedAsync` can reuse the **same mapping** by
    replaying source rows through the same `ActivityFeedMapper`.
- **Keep the mapping logic in one pure static** `ActivityFeedMapper` (Application) used by
  BOTH the live handler and the rebuild path, so "rebuild reproduces the maintained feed
  exactly" is true by construction.

### The 14 event types → ActivityEvent mapping (VERIFIED field names)
All events are `sealed record` in `src/Animora.Domain/Events/*.cs` with common prefix fields
`(string EventId, string TenantId, DateTimeOffset OccurredAtUtc, …)`. Map exactly these 14
(phase list → concrete domain event type → file):

| # | Phase description | Domain event type | File | Key fields for the row |
|---|---|---|---|---|
| 1 | pet registered | `PetRegisteredEvent` | ClinicalEvents.cs | PetId, OwnerId |
| 2 | visit completed | `VisitCompletedEvent` | ClinicalEvents.cs | VisitId, PetId, OwnerId, DoctorUserId |
| 3 | vaccination administered | `VaccinationAdministeredEvent` | ClinicalEvents.cs | RecordId, PetId, VaccineCode |
| 4 | appointment booked | `AppointmentBookedEvent` | SchedulingEvents.cs | AppointmentId, PetId, OwnerId, StartUtc |
| 5 | appointment cancelled | `AppointmentCancelledEvent` | SchedulingEvents.cs | AppointmentId, Reason |
| 6 | stay created | `StayActivatedEvent` | BoardingEvents.cs | StayId, PetId, OwnerId, RoomId |
| 7 | stay checked out | `StayCheckedOutEvent` | BoardingEvents.cs | StayId, PetId, RoomId, ActualCheckOutUtc |
| 8 | goods received | `GoodsReceiptPostedEvent` | InventoryEvents.cs | ReceiptId, SupplierId, LineCount |
| 9 | stock adjusted | `StockMovementRecordedEvent` | InventoryEvents.cs | MovementId, ProductId, VariantId, QuantityValue |
| 10 | invoice posted | `InvoicePostedEvent` | SalesEvents.cs | InvoiceId, GrandTotalMinor, Currency |
| 11 | payment registered | `PaymentRegisteredEvent` | SalesEvents.cs | PaymentId, InvoiceId, AmountMinor |
| 12 | invoice reversed | `InvoiceRefundedEvent` | SalesEvents.cs | InvoiceId, AdjustmentId, AmountMinor |
| 13 | cheque status changed | `ChequeStateChangedEvent` | FinanceEvents.cs | ChequeId (+ state) |
| 14 | expense recorded | `ExpensePostedEvent` | FinanceEvents.cs | (expense id, amount, category) |

> **Verify #13/#14 exact fields before writing the mapper** (only ChequeStateChangedEvent
> head and ExpensePostedEvent head were partially read): quick
> `grep -A14 "record ChequeStateChangedEvent\|record ExpensePostedEvent" src/Animora.Domain/Events/FinanceEvents.cs`.
> Note: `InvoiceVoidedEvent` exists too but the phase says "invoice reversed" = Refunded; do
> NOT map Voided (out of the 14). Cheque cleared/bounced are subsumed under
> `ChequeStateChangedEvent` per the phase's single "cheque status changed" entry — map only
> `ChequeStateChangedEvent` (one row per status change).

### Title/Detail = localization keys, NOT Persian (CONVENTIONS §4)
- `ActivityEvent.Title` stores a **key** e.g. `"history.invoice.posted"`; `DetailArgsJson`
  stores JSON args e.g. `{"number":"04-000123"}`. **No non-ASCII** in Title (asserted by a
  test). Catalog of keys lives in `ActivityEventKeys.cs` (Application).
- Persian rendering happens ONLY in Desktop `HistoryText.cs` (key + args → Persian string).

### AmountMinor / AmountSign convention
- `AmountMinor` = absolute minor units (long, ≥0); `AmountSign` = +1 income / -1 expense / 0
  none. Income: payment registered (+), invoice posted (context: it's a receivable — the
  reference treats invoice posted as income line, sign +1), cheque cleared (+). Expense:
  expense recorded (-1), goods received (-1 cost), invoice reversed/refunded (-1).
  Neutral (0): pet registered, appointment booked/cancelled, stay created/checked out, stock
  adjusted (unless it carries value), vaccination. Keep the sign decision in the mapper
  table; the `ActivityFeedTests` asserts documented amount+sign per type.

### ActivityEvent entity fields (from phase Design decisions — implement exactly)
`Id, TenantId, OccurredUtc, LocalDay (int, clinic-local yyyymmdd or day-number), Kind
(HistoryEventKind), Title (key string), DetailArgsJson (string), EntityType (string),
EntityId (string), AmountMinor (long), AmountSign (int), Status (HistoryEventStatus),
ActorUserId (string), ActorName (string), CorrelationId (string?)`.
`[LocalOnly]` entity. `HistoryEventKind` + `HistoryEventStatus` enums already exist in
`src/Animora.Contracts/History/HistoryModels.cs` — reuse them (Persistence already references
Contracts; verify with a quick grep, else define local mirror enums as C09 did for DiscountKind).

Indexes: `IX_ActivityEvents_TenantId_OccurredUtc`,
`IX_ActivityEvents_TenantId_Kind_OccurredUtc`,
`IX_ActivityEvents_TenantId_EntityType_EntityId`.

### UserPreference entity (Design decisions)
`TenantId, UserId, Key, ValueJson`. Unique index
`IX_UserPreferences_TenantId_UserId_Key`. `[LocalOnly]`. Needs a surrogate `Id` (ULID) like
other entities — check a sibling entity for the PK pattern (`IdGenerator.NewId`).

### Contracts are FIXED — do not change (VERIFIED)
- `IHistoryQueryService`: `Task<HistoryPage> GetPageAsync(HistoryFilter)`,
  `Task<string> ExportCsvAsync(HistoryFilter)`. **Note contract returns CSV as `string`**;
  the phase wants streaming to a file via `ILocalFileStore` returning a path. Resolve:
  `HistoryQueryService.ExportCsvAsync` streams to a temp file via `CsvWriter`+`ILocalFileStore`
  then returns file contents/path per the contract's `string` return — **re-read the exact
  return semantics** the ViewModel expects before finalizing (the phase says "returns the
  path"; contract says returns CSV text). If they conflict, honor the **contract signature**
  (return type `string`) and put the file path in it, OR return the CSV text but still stream
  internally to bound memory + enforce 50 000-row `EXPORT_TOO_LARGE`. Pick the option that
  compiles against the unchanged interface. **No `Animora.Contracts` change allowed.**
- `IDashboardQueryService`: `Task<DashboardSnapshot> GetSnapshotAsync(UserRole role)`.
- `ISearchService`: `IReadOnlyList<SearchResult> DefaultSuggestions { get; }` +
  `Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken ct)`.
  `SearchResult(Title, Subtitle, IconKey, Tone, Type Target, string? DrillId)`. `Tone` is one
  of teal/blue/violet/green/amber. `Target` is a ViewModel `Type`.
- `IUserProfileService`: `GetAsync`, `SaveAsync(UserProfile)`, `GetSettingsAsync`,
  `SaveSettingsAsync(AppSettings)`. Models are mutable classes (`UserProfile`, `AppSettings`,
  `ProfileStat`, `ProfileTeamMember`, `ProfileSession`, `NotificationChannelSetting`).
- `UserRole` enum: Manager, Cashier, Doctor, Receptionist.

### Client-service pattern (COPY from C09 `FinanceQueryService`)
Singleton taking `IServiceScopeFactory scopes`; per call: `await using var scope =
scopes.CreateAsyncScope();` resolve dispatcher + `ICurrentUserProvider` +
`IClinicTimeZoneProvider`; `context = provider.BuildQueryContext()`; if `IsFailure` return
empty; `await dispatcher.QueryAsync(new XQuery(...), context.Value!)`; map to contract via a
`*Mapping` static. Mapping statics live beside the service (e.g. `HistoryMapping.cs`).
See `src/Animora.Client.Services/Finance/FinanceQueryService.cs` for the canonical shape.

### Dashboard cache & budget
- Cache in `TenantScopedCache<string,DashboardSnapshot>`
  (`src/Animora.Client.Services/Caching/TenantScopedCache.cs` — has `TryGet/Set/Invalidate`).
  Key = `"{tenant}|{user}|dashboard:{role}|v{maxActivityEventId}"`, TTL 60 s (store a
  timestamp alongside the value or a small record with expiry). A new activity event bumps
  `maxActivityEventId` so the key changes → natural invalidation.
- Budget: whole snapshot ≤ **8** `DbCommand` executions. Assert via an EF interceptor in the
  test (`DbCommandInterceptor` counting `ReaderExecuting`). Compose role slices sequentially
  on **one** connection in `DashboardComposer`
  (`Animora.Application/UseCases/Dashboard/DashboardComposer.cs`).

### Role → dashboard slices (from phase table — implement exactly)
| Role | Stats | Primary list | Secondary list | Rich widgets |
|---|---|---|---|---|
| Manager | today revenue, today visits, open invoices, low-stock count | today's appointments | unsettled commands + alerts | RevenuePulse (14d from DailyFinanceFact), DoctorLoads, SpeciesMix |
| Doctor | my visits today, my queue, pending encounters, vaccinations due | my queue | my completed visits | none |
| Cashier | collected today, open invoices, session variance, cheques due | unpaid invoices | today's payments | none |

Manager revenue must equal the finance screen's figure for the same day → read from
`DailyFinanceFact` (same source C09 uses). Receptionist role: not in the phase table — map it
to the Manager slice OR a minimal safe default (verify what `DashboardPageViewModel` expects;
default to Manager-like read-only if unspecified). Doctor snapshot must contain **no** manager
widget (RevenuePulse/DoctorLoads/Species all null) — asserted by test.

### Search: 5-source UNION ALL (one statement)
Sources, each limited to `Limit` (default 8), ordered by static source-rank then recency:
1. owners — match `SearchKey` (C01) 2. pets — `SearchKey` 3. invoices — `Number`
4. products — `Name`/`Sku` 5. appointments — pet name via projection.
- Normalize term with C01 `PersianText.NormalizeDigits` + `NormalizeArabicLetters`
  (verify namespace: `grep -rn "class PersianText" src/`). Terms < 2 chars → return empty,
  **no query**. Map results to `SearchResult` with a navigation `Target` (ViewModel Type the
  command palette understands) + `DrillId`.
- One `SELECT` for the whole search (UNION ALL) — asserted by `GlobalSearchQueryTests`.

### Search debounce/cancel (SearchService, PERFORMANCE.md §4)
Debounce 200 ms; swap a `CancellationTokenSource` so a new keystroke cancels the prior
in-flight query. `CommandPaletteViewModel` is UNCHANGED (`ISearchService` already takes a
`CancellationToken`).

### Profile
- `UserProfileQuery()` + `AppSettingsQuery()` read real `User` + new `UserPreference` table.
- `SaveUserProfileCommand` + `SaveAppSettingsCommand` are **NOT** journaled — do **not**
  implement `IJournaledCommand` (ARCHITECTURE §8.5: UI prefs are the one LWW-eligible
  category). No `CommandTypes` entry added.
- Permission: none beyond authentication (user edits own profile). `ProfileTeamMember` list
  requires capability `identity.staff.view` (gate that part only).
- `ProfileStat` from C09 facts + `ActivityEvent` counts scoped to the user.
- `ProfileSession` rows from B03 `LocalSessions` table (real sessions). "Sign out other
  sessions" maps to B03 `SignOutCommand` for the target session id.

### RebuildActivityFeedAsync + seeding
- Add `Task<Result<int>> RebuildActivityFeedAsync(CancellationToken ct)` to
  `ILocalStoreMaintenance` + impl in `LocalStoreMaintenance.cs`. It recomputes the feed from
  source tables using the SAME `ActivityFeedMapper` and returns rows written. Pattern: copy
  `RebuildFinanceProjectionsAsync` / `RebuildStockSnapshotsAsync` shape (they already exist).
- `LocalSeeder.cs`: after all sample seeding, call `RebuildActivityFeedAsync` (same
  proof-of-rebuild discipline as C09). No new sample data.

### New error codes (append to `src/Animora.Domain/Results/ErrorCodes.cs`)
`EXPORT_TOO_LARGE`, `PROFILE_NOT_FOUND`, `SETTING_KEY_UNKNOWN`. Follow existing pattern:
`public static readonly ErrorCode X = new("CODE", ErrorCode.<Category>);` with an XML doc.
Categories available: `Integrity`, `Conflict`, plus others (grep the file). Suggested:
EXPORT_TOO_LARGE → Validation/Conflict; PROFILE_NOT_FOUND → NotFound; SETTING_KEY_UNKNOWN →
Validation. Verify exact category constants present before use.

### CsvWriter
`Animora.Client.Services/History/CsvWriter.cs` — streaming RFC 4180, UTF-8 **with BOM** (so
Excel reads Persian), max 50 000 rows → `EXPORT_TOO_LARGE`. Write via `ILocalFileStore` (A11
port — grep `interface ILocalFileStore`). No whole-file `string` accumulation.

### ViewModels — add readiness-gate line only (3 files)
`HistoryPageViewModel.cs`, `DashboardPageViewModel.cs`, `ProfilePageViewModel.cs` each gain
the readiness-gate line. **Find the exact line** by looking at a C09-updated ViewModel that
already has it (e.g. a Finance page VM) — copy that one line verbatim. `StoreReadinessGate`
lives in `src/Animora.Client.Services/Diagnostics/StoreReadinessGate.cs`.

### DI swaps (4 lines) + mock deletes (4 files)
`src/Animora.Desktop/App.axaml.cs`: swap the 4 lines registering `IHistoryQueryService`,
`IDashboardQueryService`, `ISearchService`, `IUserProfileService` from the Mock impls to the
real Client.Services impls. Delete:
`MockHistoryQueryService.cs`, `MockDashboardQueryService.cs`, `MockSearchService.cs`,
`MockUserProfileService.cs` (keep `MockSyncStatusService.cs` — out of scope, → C11). Also
register the new domain-event handler + query use cases + the `IActivityFeedWriter` port impl
in the appropriate registration extension methods (find where C09 registered its queries:
grep `AddFinanceUseCases` / registration files).

### Build / test scripts
- Build gate: `scripts/sandbox/build.sh full` (0 warn / 0 err). **Per user directive: NO full
  build / NO long full test.** Run **targeted** builds only:
  `dotnet build src/Animora.Application/... `, Persistence, Client.Services, and the 3 test
  projects touched — like B03b/B03c did. Do a full `build.sh full` ONLY at the very end if
  time allows and the user hasn't re-forbidden it.
- Tests to write (targeted): `ActivityFeedTests`, `DashboardBudgetTests`,
  `GlobalSearchQueryTests`. Run only these test classes, not the whole suite.

---

## Task list

> Legend: `[ ]` todo · `[~]` in progress · `[x]` done. **Push after each task.**

### [x] T00 — Confirm baked-in facts (DONE — results locked below)
LOCKED RESULTS:
- **`ChequeStateChangedEvent`** fields: `(EventId, TenantId, OccurredAtUtc, string ChequeId,
  ChequeState FromState, ChequeState ToState)`. Row: EntityType="Cheque", EntityId=ChequeId,
  Amount=0, Sign=0, Status maps from `ToState` (best-effort → `HistoryEventStatus`), Title key
  `history.cheque.statuschanged`, DetailArgs `{"from":FromState,"to":ToState}`.
- **`ExpensePostedEvent`** fields: `(EventId, TenantId, OccurredAtUtc, string ExpenseId, string
  CategoryCode, long AmountMinor, string Currency, PaymentMethod? SettledMethod)`. Row:
  EntityType="Expense", EntityId=ExpenseId, Amount=AmountMinor, **Sign=-1**, Status=Normal/Done,
  Title `history.expense.recorded`, DetailArgs `{"category":CategoryCode}`.
- **PersianText** = `Animora.Application.Text.PersianText` (static): `NormalizeDigits(string?)`,
  `NormalizeArabicLetters(string?)`, and `Normalize(...)` = lower+trim of both. Application can
  use it directly (search query is in Application).
- **`ILocalFileStore`** (`Animora.Application.Ports`) is **content-addressed** — `PutAsync(Stream)
  →Result<string hash>`, `OpenAsync(hash)`, `DeleteAsync`. **No file paths.** → RESOLUTION for
  export: the `IHistoryQueryService.ExportCsvAsync` contract returns `string` (CSV text). Return
  the **CSV text** as the string, but build it through the streaming `CsvWriter` into a
  `MemoryStream`/`StreamWriter` (BOM) to bound memory and enforce the 50 000-row cap → fail with
  `EXPORT_TOO_LARGE`. Do NOT try to return a filesystem path (no path API exists; contract is
  `string`). Ignore the phase's "returns the path" wording — the fixed contract wins
  (CONVENTIONS: no Contracts change). Record this in PROGRESS as deviation **D-02**.
- **Readiness-gate line**: ctor-inject `IStoreReadinessGate _readiness;` and call
  `await _readiness.WaitAsync();` before the first data load in the VM's load method (copy from
  `src/Animora.Desktop/Pages/Finance/FinancePageViewModel.cs` lines 32/43/46/206).
- **Application → Contracts: YES** (`Animora.Application.csproj` references Contracts). So the
  Application mapper/queries/read-models MAY use `HistoryEventKind`, `HistoryEventStatus`,
  `UserRole` from Contracts directly.
- **Persistence → Contracts: NO** (only Domain + Application). → the `ActivityEvent` **entity**
  must NOT reference Contracts enums. **Store `Kind` and `Status` as `string` columns** on the
  entity; the Application read-model/mapper parses them into the Contracts enums. (Same style as
  C09 storing enums as strings.) Deviation **D-03**.

### [x] T01 — Entities, configurations, migrations, error codes
Do (order matters — build must stay green):
1. `src/Animora.Persistence.Sqlite/Entities/ActivityEvent.cs` — `[LocalOnly]` sealed class,
   fields per "ActivityEvent entity fields" above, private ctor + internal `Create(...)`
   factory (copy PK/`IdGenerator.NewId` pattern from a sibling entity, e.g.
   `DailyFinanceFact.cs`).
2. `src/Animora.Persistence.Sqlite/Entities/UserPreference.cs` — `[LocalOnly]` sealed class,
   fields per above + surrogate Id.
3. `src/Animora.Persistence.Sqlite/Configurations/Activity/ActivityEventConfiguration.cs` +
   `UserPreferenceConfiguration.cs` — table/columns + the 3 activity indexes + the unique
   preference index. Copy a sibling configuration for style (enum→string conversions like C09).
4. Migrations: `017_ActivityFeed`, `018_UserPreferences` (**D-01**). Prefer generating with
   `dotnet ef migrations add 017_ActivityFeed -o Migrations` via the `.sandbox/tools/dotnet-ef`
   (B03a installed it). If EF tool unavailable, hand-write migration + `.Designer` + update the
   model snapshot (last resort; error-prone). Verify the snapshot compiles.
5. `src/Animora.Domain/Results/ErrorCodes.cs` — append `EXPORT_TOO_LARGE`, `PROFILE_NOT_FOUND`,
   `SETTING_KEY_UNKNOWN` (verify category constants first).
Targeted build: Domain + Persistence.
Push: `scripts/git/push.sh "feat(C10): add ActivityEvent + UserPreference entities, migrations 017/018, 3 error codes"`

### [x] T02 — ActivityEventKeys + mapper + handler + writer port
Do:
1. `src/Animora.Application/Activity/ActivityEventKeys.cs` — const string catalog of the ~14
   Title keys (e.g. `PetRegistered = "history.pet.registered"`, `InvoicePosted =
   "history.invoice.posted"`, …). ASCII only.
2. `src/Animora.Application/Activity/ActivityFeedMapper.cs` — pure static: `DomainEvent →
   ActivityEventDraft?` (null for the ~non-14). One switch, one row per event, sets
   Kind/TitleKey/DetailArgs(JSON)/Amount/Sign/Status/EntityType/EntityId/Actor per the mapping
   table + amount/sign convention above. This is the SINGLE source of mapping truth.
3. `src/Animora.Application/Ports/IActivityFeedWriter.cs` — port: `Task WriteAsync(
   ActivityEventDraft draft, CommandContext ctx, CancellationToken ct)` (no SaveChanges).
4. `src/Animora.Application/Activity/ActivityFeedHandler.cs` —
   `IDomainEventHandler<DomainEvent>`; maps via `ActivityFeedMapper`, if non-null calls
   `IActivityFeedWriter`. Register on base `DomainEvent` (like OutboxRelayHandler).
5. Persistence impl `src/Animora.Persistence.Sqlite/.../ActivityFeedWriter.cs` inserting the
   `ActivityEvent` row in the ambient transaction (resolve LocalDay from clinic timezone; use
   `IClinicTimeZoneProvider`/`IClock` already in DI).
6. Register handler + writer in DI (Persistence registration where domain-event handlers are
   added; grep `AddDomainEventHandler`).
Targeted build: Application + Persistence.
Push: `scripts/git/push.sh "feat(C10): ActivityFeedHandler + mapper + writer port for 14 event types"`

### [~] T03 — RebuildActivityFeedAsync + seeding hook
Do:
1. Add `RebuildActivityFeedAsync` to `ILocalStoreMaintenance` + impl in
   `LocalStoreMaintenance.cs` — replay source rows through `ActivityFeedMapper` (reuse it),
   delete+reinsert current tenant's `ActivityEvent` rows, return count. Copy shape of
   `RebuildFinanceProjectionsAsync`.
2. `LocalSeeder.cs` — call `RebuildActivityFeedAsync` at the END of seeding (after all sample
   data), matching C09's finance-rebuild placement.
Targeted build: Persistence.
Push: `scripts/git/push.sh "feat(C10): RebuildActivityFeedAsync + seed-time feed rebuild"`

### [~] T04 — History feed query + CsvWriter + export path
Do:
1. `src/Animora.Persistence.Sqlite/Queries/ActivityFeedQueries.cs` — paged projection of
   `ActivityEvent`: one indexed statement, filter by kind/date-range/actor/text, sort per
   `HistorySort`, max 200 rows/page, plus totals (count, income sum, expense sum) over the
   whole filtered set.
2. `src/Animora.Application/UseCases/History/*.cs` — `ActivityFeedQuery(HistoryFilter)` →
   `HistoryPage`-shaped read model + handler. Also the export streaming query (yields rows,
   not a page) with the 50 000-row cap → `EXPORT_TOO_LARGE`.
3. `src/Animora.Client.Services/History/CsvWriter.cs` — streaming RFC-4180, UTF-8 **with BOM**,
   writes via `ILocalFileStore`, enforces 50 000 rows.
4. `src/Animora.Client.Services/History/HistoryQueryService.cs` +
   `HistoryMapping.cs` — real `IHistoryQueryService` (copy FinanceQueryService pattern).
   `GetPageAsync` maps read model → `HistoryPage`. `ExportCsvAsync` streams via CsvWriter and
   returns per the unchanged contract signature (see "Contracts are FIXED" note — resolve the
   path-vs-text question by matching what compiles + what the VM uses).
5. `src/Animora.Desktop/Localization/HistoryText.cs` — key + args JSON → Persian display
   string (all Persian lives here). Cover every key in `ActivityEventKeys`.
Targeted build: Application + Persistence + Client.Services + Desktop.
Push: `scripts/git/push.sh "feat(C10): history feed query, streaming CsvWriter, HistoryQueryService, HistoryText"`

### [~] T05 — Dashboard queries + composer + service + cache/budget
Do:
1. `src/Animora.Persistence.Sqlite/Queries/DashboardQueries.cs` — one statement per role slice
   (stats, primary/secondary lists, and manager widgets from `DailyFinanceFact` etc.). Keep
   total statements ≤ 8 for any single role.
2. `src/Animora.Application/UseCases/Dashboard/*.cs` — `DashboardSnapshotQuery(UserRole)`,
   `DashboardComposer` assembling slices sequentially on one connection, 3 role slice methods.
   Manager revenue = same-day `DailyFinanceFact` figure.
3. `src/Animora.Client.Services/Dashboard/DashboardQueryService.cs` + `DashboardMapping.cs` —
   real `IDashboardQueryService`; 60 s `TenantScopedCache` keyed
   `tenant|user|dashboard:{role}|v{maxActivityEventId}`.
Targeted build: Application + Persistence + Client.Services.
Push: `scripts/git/push.sh "feat(C10): dashboard slices, DashboardComposer, cached DashboardQueryService"`

### [~] T06 — Global search (union) + SearchService
Do:
1. `src/Animora.Persistence.Sqlite/Queries/GlobalSearchQueries.cs` — one `UNION ALL` over the
   5 sources, each `Limit`-capped, ordered by source-rank then recency.
2. `src/Animora.Application/UseCases/Search/GlobalSearchQuery.cs` — `GlobalSearchQuery(string
   Term, int Limit = 8)`; term normalization via `PersianText`; <2 chars → empty, no query.
3. `src/Animora.Client.Services/Search/SearchService.cs` + mapping — real `ISearchService`
   with `DefaultSuggestions` + `SearchAsync` (200 ms debounce, CTS swap cancellation). Map to
   `SearchResult` with a valid navigation `Target` ViewModel type + `DrillId`.
Targeted build: Application + Persistence + Client.Services.
Push: `scripts/git/push.sh "feat(C10): global union search query + debounced SearchService"`

### [ ] T07 — Profile queries/commands + UserProfileService
Do:
1. `src/Animora.Application/UseCases/Profile/*.cs` — `UserProfileQuery()`, `AppSettingsQuery()`
   (read `User` + `UserPreference` + `LocalSessions` + C09 facts/ActivityEvent counts);
   `SaveUserProfileCommand`, `SaveAppSettingsCommand` — **NOT** `IJournaledCommand`, no
   `CommandTypes` entry. `SETTING_KEY_UNKNOWN` on unknown pref key; `PROFILE_NOT_FOUND` when
   user missing. `ProfileTeamMember` list gated by `identity.staff.view`.
2. Persistence queries for the above (read `UserPreference`, `LocalSessions`).
3. `src/Animora.Client.Services/Profile/UserProfileService.cs` + mapping — real
   `IUserProfileService`; `GetAsync/SaveAsync/GetSettingsAsync/SaveSettingsAsync`. Sign-out
   other session → B03 `SignOutCommand` for the target session id.
Targeted build: Application + Persistence + Client.Services.
Push: `scripts/git/push.sh "feat(C10): profile/settings queries + non-journaled save commands + UserProfileService"`

### [ ] T08 — DI swaps, ViewModel readiness lines, delete mocks
Do:
1. `src/Animora.Desktop/App.axaml.cs` — swap the 4 DI registrations to the real services;
   register new use cases + `IActivityFeedWriter` + domain-event handler if not already done
   in T02.
2. Add the readiness-gate line to `HistoryPageViewModel.cs`, `DashboardPageViewModel.cs`,
   `ProfilePageViewModel.cs` (verbatim from a C09 VM).
3. Delete `MockHistoryQueryService.cs`, `MockDashboardQueryService.cs`, `MockSearchService.cs`,
   `MockUserProfileService.cs`. Confirm no DI/reference remains (`grep -rn "Mock<name>"`).
Targeted build: Desktop.
Push: `scripts/git/push.sh "refactor(C10): swap 4 DI seams to real services, add readiness gates, delete 4 mocks"`

### [ ] T09 — Tests (3 classes, targeted run only)
Do:
1. `tests/Animora.Application.Tests/ActivityFeedTests.cs` — each of the 14 event types
   produces exactly one `ActivityEvent` with documented key/amount/sign;
   `RebuildActivityFeedAsync` reproduces the maintained feed exactly; assert no
   `ActivityEvent.Title` has a non-ASCII char.
2. `tests/Animora.Application.Tests/DashboardBudgetTests.cs` — ≤ 8 `DbCommand` per role (EF
   `DbCommandInterceptor` counting readers); manager revenue == finance figure same day;
   doctor snapshot has no manager widget (all 3 manager widgets null).
3. `tests/Animora.Persistence.Tests/GlobalSearchQueryTests.cs` — one `SELECT` for the whole
   search; Persian-digit phone + Arabic-form name both match; 1-char term issues no query;
   per-source limits hold.
Run ONLY these classes (e.g. `dotnet test --filter "FullyQualifiedName~ActivityFeedTests|
FullyQualifiedName~DashboardBudgetTests|FullyQualifiedName~GlobalSearchQueryTests"`).
Push: `scripts/git/push.sh "test(C10): ActivityFeed, DashboardBudget, GlobalSearchQuery tests"`

### [ ] T10 — Final verify + PROGRESS.md + optional full build
Do:
1. If not re-forbidden and time allows: `scripts/sandbox/build.sh full` = 0/0. Otherwise a
   final targeted build of all touched projects. Fix any warnings/errors (push heavy fixes
   immediately with a clear message).
2. Update `backend-roadmap/PROGRESS.md`: mark C10 done; "Next phase" → `C11`; append a
   "Repo state" entry listing: `ActivityEvent` feed + 14 mapped event types + key/args rule,
   **migrations `017_ActivityFeed` + `018_UserPreferences` (deviation D-01: 016 was taken by
   C09)**, `RebuildActivityFeedAsync`, `DashboardComposer` + ≤8-statement budget, union search
   design, "profile commands not journaled" decision, 4 client services + `CsvWriter` +
   `HistoryText`, 4 DI swaps, mocks deleted (the 4 names), `ErrorCodes` += 3, 3 test classes,
   and the ExportCsv contract-vs-path resolution decision.
Push: `scripts/git/push.sh "docs(progress): mark C10 done and record repo state"`

### [ ] T11 — Delete this plan file (FINAL task)
Do: `rm Plan/PHASE-C10-plan.md` (only this file; do not touch roadmap/other files).
Push: `scripts/git/push.sh "chore(C10): remove plan file after completion"`

---

## Acceptance checklist (from phase — verify before T10 done)
- [ ] The 4 mock files gone; no DI line references them.
- [ ] `ActivityFeedTests` green (14 types, exact key/amount/sign; rebuild exact; ASCII titles).
- [ ] `DashboardBudgetTests` green (≤8 stmts/role; manager revenue == finance; doctor no
      manager widget).
- [ ] `GlobalSearchQueryTests` green (one SELECT; Persian-digit + Arabic-form match; 1-char no
      query; per-source limits).
- [ ] History export = UTF-8-BOM CSV, Persian correct; 50 001 rows → `EXPORT_TOO_LARGE`.
- [ ] Profile/settings survive restart; signing out another session invalidates it.
- [ ] Build 0/0 (targeted at minimum).
