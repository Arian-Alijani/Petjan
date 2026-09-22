# Phase C10 — History, Dashboard, Search, Profile

**Band:** M · **Track:** C · **Depends on:** C09 · **Satisfies:** R-B01, R-B02
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (C01 `SearchKey`; C05/C03 handoffs; C09 fact tables; B05 `AuditEntry`)
- `PERFORMANCE.md` §6, §12, §15
- `src/Animora.Contracts/History/IHistoryQueryService.cs`,
  `src/Animora.Contracts/History/HistoryModels.cs`
- `src/Animora.Contracts/Dashboard/IDashboardQueryService.cs`,
  `src/Animora.Contracts/Dashboard/DashboardModels.cs`
- `src/Animora.Contracts/Search/ISearchService.cs`, `src/Animora.Contracts/Search/SearchResult.cs`
- `src/Animora.Contracts/Profile/IUserProfileService.cs`,
  `src/Animora.Contracts/Profile/ProfileModels.cs`
- `src/Animora.Contracts/Navigation/UserRole.cs`
- `src/Animora.Desktop/App.axaml.cs` (lines registering `IHistoryQueryService`,
  `IDashboardQueryService`, `ISearchService`, `IUserProfileService`)

## Goal
The four cross-cutting surfaces become real: history is a **materialized activity feed fed by actual
domain events** (no synthetic rows), the dashboard aggregates from C09's facts and live counts,
search runs over real indexes across contexts, and the profile screen persists user profile and app
settings.

## Design decisions (pre-made — implement, do not re-derive)
- **History = a materialized `ActivityEvent` feed, written from the outbox** (never scanned across
  ten tables at query time). New `[LocalOnly]` entity `ActivityEvent`:
  `Id`, `TenantId`, `OccurredUtc`, `LocalDay` (int, clinic-local), `Kind` (`HistoryEventKind`),
  `Title`, `Detail`, `EntityType`, `EntityId`, `AmountMinor`, `AmountSign`, `Status`
  (`HistoryEventStatus`), `ActorUserId`, `ActorName`, `CorrelationId`.
  Written by `ActivityFeedHandler` (`Animora.Application/Activity/ActivityFeedHandler.cs`) from the
  B02 outbox for 14 event types: pet registered, visit completed, vaccination administered,
  appointment booked/cancelled, stay created/checked out, goods received, stock adjusted, invoice
  posted, payment registered, invoice reversed, cheque status changed, expense recorded.
  Indexes: `IX_ActivityEvents_TenantId_OccurredUtc`, `IX_ActivityEvents_TenantId_Kind_OccurredUtc`,
  `IX_ActivityEvents_TenantId_EntityType_EntityId`. Migration `016_ActivityFeed`.
  **Title/Detail are localization keys + JSON arguments**, not Persian sentences
  (`Title = "history.invoice.posted"`, `DetailArgsJson = {"number":"04-000123"}`) — Persian rendering
  stays in `NotificationText`/`HistoryText` in the Desktop layer (CONVENTIONS §4). `HistoryText`
  (`src/Animora.Desktop/Localization/HistoryText.cs`) resolves key + args to the displayed string.
  Rebuild path: `ILocalStoreMaintenance.RebuildActivityFeedAsync` recomputes from source tables.
- **History queries:** `ActivityFeedQuery(HistoryFilter)` → `HistoryPage`, paged (max 200), filtered
  by kind/date-range/actor/text, sorted per `HistorySort` — **one** indexed statement.
  `ExportCsvAsync` streams the same query through `CsvWriter`
  (`Animora.Client.Services/History/CsvWriter.cs`, RFC 4180, UTF-8 **with BOM** so Excel reads
  Persian correctly, max 50 000 rows → `EXPORT_TOO_LARGE`) to a file under the app data folder via
  `ILocalFileStore` (A11) and returns the path. No `string` accumulation of the whole file.
- **Dashboard** (`UseCases/Dashboard/`): `DashboardSnapshotQuery(UserRole Role)` composes **role
  slices**, each a single statement, executed sequentially on one connection and assembled by
  `DashboardComposer` (`Animora.Application/UseCases/Dashboard/DashboardComposer.cs`):
  | Role | Stats | Primary list | Secondary list | Rich widgets |
  |---|---|---|---|---|
  | `Manager` | today revenue, today visits, open invoices, low-stock count | today's appointments | unsettled commands + alerts | `RevenuePulse` (14 days from `DailyFinanceFact`), `DoctorLoads`, `SpeciesMix` |
  | `Doctor` | my visits today, my queue, pending encounters, vaccinations due | my queue | my completed visits | none |
  | `Cashier` | collected today, open invoices, session variance, cheques due | unpaid invoices | today's payments | none |
  The snapshot is cached in `TenantScopedCache` for 60 s with key
  `tenant|user|dashboard:<role>|v<maxActivityEventId>` — a new activity event invalidates it.
  Budget: the whole snapshot must complete in **≤ 8 statements** (asserted by a test counting
  `DbCommand` executions via an EF interceptor).
- **Search** (`UseCases/Search/`): `GlobalSearchQuery(string Term, int Limit = 8)` runs **one**
  `UNION ALL` over five indexed sources — owners (`SearchKey`), pets (`SearchKey`), invoices
  (`Number`), products (`Name`/`Sku`), appointments (pet name via the projection) — each limited to
  `Limit`, ordered by a static source rank then recency. Term normalization reuses C01's
  `PersianText.NormalizeDigits`/`NormalizeArabicLetters`. Terms shorter than 2 characters return
  empty without a query. Results map to `SearchResult` with a navigation target key the existing
  command palette already understands. Debounced at 200 ms in `SearchService` with cancellation of
  the prior in-flight query (`CancellationTokenSource` swap) — PERFORMANCE.md §4.
- **Profile** (`UseCases/Profile/`): `UserProfileQuery()` and `AppSettingsQuery()` read the real
  `User` + a new `[LocalOnly]` `UserPreference` table (`TenantId`, `UserId`, `Key`, `ValueJson`,
  unique `IX_UserPreferences_TenantId_UserId_Key`). `SaveUserProfileCommand` and
  `SaveAppSettingsCommand` are **not** journaled (`IJournaledCommand` deliberately **not**
  implemented): UI preferences are the one category ARCHITECTURE.md §8.5 allows generic LWW for, so
  they replicate as ordinary state and carry no business intent. `CommandTypes` += none.
  Permission: none beyond authentication (a user always edits their own profile;
  `ProfileTeamMember` list requires `identity.staff.view`).
  `ProfileStat` values come from C09 facts + `ActivityEvent` counts scoped to the user.
  `ProfileSession` rows come from B03's `LocalSessions` table (real sessions, with a
  "sign out other sessions" action mapped to B03's `SignOutCommand` for the target session id).
  Migration `017_UserPreferences`.
- **Sample data:** none new — the feed and dashboard derive from C01–C09 sample data via
  `RebuildActivityFeedAsync` at the end of seeding (same proof-of-rebuild discipline as C09).
- **ViewModel changes (exhaustive):** `HistoryPageViewModel`, `DashboardPageViewModel`,
  `ProfilePageViewModel` each gain the readiness-gate line. `CommandPaletteViewModel` is unchanged
  (`ISearchService` already takes a `CancellationToken`).
- **New error codes:** `EXPORT_TOO_LARGE`, `PROFILE_NOT_FOUND`, `SETTING_KEY_UNKNOWN`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Activity/ActivityFeedHandler.cs` | create | outbox → `ActivityEvent` for 14 event types |
| `src/Animora.Application/Activity/ActivityEventKeys.cs` | create | localization-key catalog |
| `src/Animora.Application/UseCases/History/*.cs` | create | feed query + read model |
| `src/Animora.Application/UseCases/Dashboard/*.cs` | create | snapshot query + `DashboardComposer` + 3 role slices |
| `src/Animora.Application/UseCases/Search/GlobalSearchQuery.cs` | create | 5-source union search |
| `src/Animora.Application/UseCases/Profile/*.cs` | create | 2 queries + 2 non-journaled commands |
| `src/Animora.Persistence.Sqlite/Entities/ActivityEvent.cs` | create | `[LocalOnly]` feed row |
| `src/Animora.Persistence.Sqlite/Entities/UserPreference.cs` | create | `[LocalOnly]` settings row |
| `src/Animora.Persistence.Sqlite/Configurations/Activity/*.cs` | create | 2 configurations + indexes |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `016_ActivityFeed`, `017_UserPreferences` + snapshots |
| `src/Animora.Persistence.Sqlite/Queries/ActivityFeedQueries.cs` | create | paged feed projection |
| `src/Animora.Persistence.Sqlite/Queries/DashboardQueries.cs` | create | the role slices |
| `src/Animora.Persistence.Sqlite/Queries/GlobalSearchQueries.cs` | create | the union projection |
| `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` | modify | add `RebuildActivityFeedAsync` |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` | modify | rebuild the feed after sample seeding |
| `src/Animora.Client.Services/History/HistoryQueryService.cs` | create | real `IHistoryQueryService` |
| `src/Animora.Client.Services/History/CsvWriter.cs` | create | streaming RFC-4180 writer with BOM |
| `src/Animora.Client.Services/Dashboard/DashboardQueryService.cs` | create | real `IDashboardQueryService` |
| `src/Animora.Client.Services/Search/SearchService.cs` | create | real `ISearchService` with debounce+cancel |
| `src/Animora.Client.Services/Profile/UserProfileService.cs` | create | real `IUserProfileService` |
| `src/Animora.Desktop/Localization/HistoryText.cs` | create | key+args → Persian string |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap 4 DI lines |
| `src/Animora.Desktop/Pages/History/HistoryPageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Pages/Dashboard/DashboardPageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Pages/Profile/ProfilePageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Mocks/MockHistoryQueryService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/MockDashboardQueryService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/MockSearchService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/MockUserProfileService.cs` | delete | replaced |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 3 codes |
| `tests/Animora.Application.Tests/ActivityFeedTests.cs` | create | 14 event types produce correct rows; rebuild |
| `tests/Animora.Application.Tests/DashboardBudgetTests.cs` | create | ≤ 8 statements per role; correct numbers |
| `tests/Animora.Persistence.Tests/GlobalSearchQueryTests.cs` | create | one statement, normalization, ranking |

## Implementation steps
1. Add `ActivityEvent`, `UserPreference`, their configurations and migrations `016`/`017`; append
   the 3 error codes.
2. Write `ActivityEventKeys` + `ActivityFeedHandler` covering all 14 event types; add
   `RebuildActivityFeedAsync` and call it at the end of seeding.
3. Write `ActivityFeedQueries` + `ActivityFeedQuery`; write `CsvWriter` and the export path through
   `ILocalFileStore`.
4. Write `DashboardQueries` (one statement per slice) + `DashboardComposer` + the snapshot query,
   with the 60 s tenant-safe cache and the statement-count budget.
5. Write `GlobalSearchQueries` + `GlobalSearchQuery` with Persian normalization and source ranking.
6. Write the profile queries/commands over `User` + `UserPreference` + `LocalSessions`.
7. Write the four client services + `HistoryText`.
8. Swap the four DI lines, add the three ViewModel lines, delete the four mocks.
9. Write the three test classes.
10. Verify manually: the history feed shows real events from the flows exercised in C02–C09 and
    filters/exports correctly; each role's dashboard shows numbers matching the finance screen;
    Ctrl+K search finds an owner by Persian name, a pet by microchip, an invoice by number and a
    product by SKU; profile edits and app settings survive a restart and a sign-out.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Application: `ActivityFeedHandler`, `ActivityEventKeys`, `DashboardComposer`, 6 queries,
  2 non-journaled commands, their read models.
- Persistence: `ActivityEvent`, `UserPreference`, `ActivityFeedQueries`, `DashboardQueries`,
  `GlobalSearchQueries`, `RebuildActivityFeedAsync`, migrations `016_ActivityFeed`,
  `017_UserPreferences`.
- Client services: `HistoryQueryService`, `CsvWriter`, `DashboardQueryService`, `SearchService`,
  `UserProfileService`. Desktop: `HistoryText`.
- `ErrorCodes` += 3. No `CommandTypes` entry (profile commands are deliberately not journaled).
  No `Animora.Contracts` change.
- DI: 4 swaps. **Mocks deleted: `MockHistoryQueryService.cs`, `MockDashboardQueryService.cs`,
  `MockSearchService.cs`, `MockUserProfileService.cs`.**

## Rules that apply here
- **A-RULE 018 / 034** — the feed and dashboard read denormalized projections maintained off the
  OLTP path; no query fans out across write tables.
- **A-RULE 033 / PERFORMANCE.md §15** — the dashboard cache key carries tenant + user + role + a
  version component and the cache is bounded.
- **ARCHITECTURE.md §8.5** — UI preferences are the only LWW-eligible data, which is exactly why the
  profile commands are not journaled.
- **PERFORMANCE.md §4 / §6** — search is debounced and cancellable; the feed and export are streamed
  and paged.
- **CONVENTIONS §4** — no Persian string in Application or Client.Services; the feed stores keys.
- **CONVENTIONS §10** — four mocks deleted with the swaps.

## Out of scope
- Removing the remaining placeholder mocks (`MockSyncStatusService`) and the final audit → C11.
- Operator-side dashboards → E02.
- Server-side activity feed → D13 exposes diagnostics; the tenant feed stays local.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- The four mock files do not exist; no DI line references them.
- `ActivityFeedTests` green: each of the 14 event types produces exactly one `ActivityEvent` with the
  documented key, amount and sign; `RebuildActivityFeedAsync` reproduces the maintained feed exactly;
  no `ActivityEvent.Title` contains a non-ASCII character (keys only).
- `DashboardBudgetTests` green: each role's snapshot executes ≤ 8 `DbCommand`s; the manager's
  revenue equals the finance screen's figure for the same day; the doctor's snapshot contains no
  manager widget.
- `GlobalSearchQueryTests` green: one `SELECT` for the whole search; a Persian-digit phone and an
  Arabic-form name both match; a 1-character term issues no query; per-source limits hold.
- History export produces a UTF-8-BOM CSV that opens with correct Persian text; 50 001 rows returns
  `EXPORT_TOO_LARGE`.
- Profile and settings changes survive restart; signing out another session invalidates it.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C10 done; advance "Next phase" to `C11`; append a "Repo state" entry listing: the
`ActivityEvent` feed + the 14 mapped event types + the key/args localization rule, migrations
`016_ActivityFeed` and `017_UserPreferences`, `RebuildActivityFeedAsync`, `DashboardComposer` + the
≤ 8-statement budget, the union search design, the "profile commands are not journaled" decision,
the 4 client services + `CsvWriter` + `HistoryText`, the 4 DI swaps, **mocks deleted:
`MockHistoryQueryService.cs`, `MockDashboardQueryService.cs`, `MockSearchService.cs`,
`MockUserProfileService.cs`**, `ErrorCodes` += 3, the 3 test classes, deviations.
