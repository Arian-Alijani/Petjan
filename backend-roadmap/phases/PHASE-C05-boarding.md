# Phase C05 — Boarding

**Band:** M · **Track:** C · **Depends on:** C03 · **Satisfies:** R-B01, R-B02
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (the **C01 pattern**; C03's `VisitBillingHandoff`; C04's
  `ClinicCalendar`)
- `PERFORMANCE.md` §5, §6
- `src/Animora.Contracts/Boarding/IBoardingQueryService.cs`,
  `src/Animora.Contracts/Boarding/BoardingStay.cs`,
  `src/Animora.Contracts/Boarding/BoardingDetail.cs`,
  `src/Animora.Contracts/Boarding/BoardingHistoryModels.cs`
- `src/Animora.Domain/Boarding/BoardingStay.cs`, `src/Animora.Domain/Boarding/Room.cs`,
  `src/Animora.Domain/Boarding/BoardingServiceDef.cs`, `src/Animora.Domain/Boarding/StayItem.cs`
- `src/Animora.Application/Ports/IBoardingRepository.cs`
- `src/Animora.Desktop/App.axaml.cs` (line registering `IBoardingQueryService`)

## Goal
The boarding screen runs on real `BoardingStay`, `Room` and `BoardingServiceDef` rows: stays with
per-day accrual, added items, room transfer, and a checkout that produces the same
**billing handoff** shape C03 introduced, so C07 can invoice a stay exactly as it invoices a visit.

## Design decisions (pre-made — implement, do not re-derive)
- **Charge accrual is derived, never stored as a running total** (the same discipline as
  movement-based inventory). `StayChargeCalculator`
  (`Animora.Domain/Boarding/StayChargeCalculator.cs`, pure static):
  `static Money Accrued(BoardingStay stay, IReadOnlyList<BoardingServiceDef> services, long asOfUtc)`
  = Σ over `StayItem`s, where an item with `BoardingServiceBilling.PerNight` multiplies by
  `NightsBetween(stay.CheckInUtc, min(asOfUtc, stay.CheckOutUtc ?? asOfUtc))` and
  `BoardingServiceBilling.OneTime`/`PerUnit` multiplies by `Quantity`.
  `NightsBetween` counts **clinic-local calendar nights** via `ClinicCalendar` (C04), minimum 1.
  The screen's "accrued so far" number therefore always recomputes and can never drift.
- **Queries** (`UseCases/Boarding/`):
  - `BoardingStayListQuery()` → active + arriving-today stays with pet/owner names, room label and
    accrued total, one projection, no N+1.
  - `BoardingStayDetailQuery(string StayId)` → `StayDetailReadModel` including items with unit
    prices and the derived accrual.
  - `BoardingRoomsQuery()` → rooms with `Occupied`/`Free` computed from active stays in the same
    statement (`LEFT JOIN` + `COUNT`), never per room.
  - `BoardingServicesQuery()`, `BoardingSelectableItemsQuery()` (services **plus** sellable
    `Product` rows of kind `Consumable`/`Medicine`, unioned and tagged with
    `BoardingItemSource`), `BoardingKnownPetsQuery(string? Search)` (reuses C01 `SearchKey`).
  - `BoardingHistoryQuery(BoardingHistoryFilter)` → `BoardingHistoryPage`, paged (max 200), sorted
    per `BoardingHistorySort`, over completed stays.
- **Commands** (`UseCases/Boarding/`, all `IJournaledCommand` +
  `[RequiresPermission("boarding.stay.manage")]` + `[Auditable(AuditCategory.Clinical, ...)]`):
  `CreateBoardingStayCommand`, `AddStayItemCommand`, `RemoveStayItemCommand`,
  `TransferStayRoomCommand`, `CheckoutStayCommand`.
  `CommandTypes` += `boarding.stay.create`, `boarding.item.add`, `boarding.item.remove`,
  `boarding.stay.transfer`, `boarding.stay.checkout` (5).
- **`RemoveItemAsync(stayId, index)` index semantics:** the Contracts member removes by **ordinal
  position in the detail's item list**. `BoardingMapping` therefore returns items in a stable order
  (`StayItem.AddedUtc`, then `StayItem.Id`) and `RemoveStayItemCommand` takes
  `(StayId, int Index, string ExpectedItemId)` — the service resolves the id from the last-read
  detail and passes both; a mismatch returns `STAY_ITEM_INDEX_STALE` instead of deleting the wrong
  row. This is the general rule for every index-based Contracts member in Track C (C07/C08 cite it).
- **Room transfer:** `TransferStayRoomCommand(StayId, TargetRoomId)` validates target capacity
  (`Room.Capacity` minus active stays, computed in the transaction), appends a
  `StayRoomAssignment` (A05 value object, kept as an **append-only history** rather than mutating the
  current room), and sets `BoardingStay.CurrentRoomId`. A full room returns `ROOM_AT_CAPACITY`.
  The Contracts member `TransferAsync(stayId)` has no target parameter, so the service resolves
  "next free room of the same `RoomType`" and returns `ROOM_NONE_AVAILABLE` when there is none —
  documented behavior, no Contracts reshape.
- **Checkout → handoff:** `CheckoutStayCommand` in one transaction: validates state, stamps
  `CheckOutUtc`, freezes the accrual into `FrozenTotalMinor` on the stay (a *snapshot for audit*,
  not a source of truth — the calculator still reproduces it), writes a **`BoardingBillingHandoff`**
  row reusing the C03 shape — a second `[LocalOnly]` entity with the identical column set plus
  `StayId` (unique) instead of `VisitId`, `Status` reusing `HandoffStatus` — raises
  `BoardingStayCheckedOutEvent`, and releases the room. Migration `011_BoardingHandoff`.
  **Decision:** two tables rather than one polymorphic table, because C07 joins them differently and
  a nullable-FK union table would break the `Tenant` filter's required-navigation rule
  (CONVENTIONS §5).
- **`GetReadyEncountersAsync` (C07) reads both handoff tables** — recorded here so C07's phase file
  names them.
- **Sample data:** `Seed/BoardingSampleData.cs` — 8 rooms (3 types), 6 service definitions,
  5 active stays with items, 9 completed stays for the history tab.
  `Room` and `BoardingServiceDef` rows themselves are **reference data in `LocalSeeder`** (they exist
  on a fresh install regardless of `SeedSampleData`); only the stays are sample data.
- **ViewModel changes (exhaustive):** `BoardingPageViewModel` gains the readiness-gate line.
- **New error codes:** `STAY_NOT_FOUND`, `STAY_NOT_EDITABLE`, `STAY_ITEM_INDEX_STALE`,
  `ROOM_AT_CAPACITY`, `ROOM_NONE_AVAILABLE`, `STAY_ALREADY_CHECKED_OUT`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Boarding/StayChargeCalculator.cs` | create | derived accrual, pure |
| `src/Animora.Application/UseCases/Boarding/*.cs` | create | 6 queries + 5 commands + handlers/validators |
| `src/Animora.Application/UseCases/Boarding/ReadModels/*.cs` | create | 5 read models |
| `src/Animora.Persistence.Sqlite/Entities/BoardingBillingHandoff.cs` | create | `[LocalOnly]` handoff row |
| `src/Animora.Persistence.Sqlite/Configurations/Boarding/BoardingBillingHandoffConfiguration.cs` | create | mapping + 2 indexes |
| `src/Animora.Persistence.Sqlite/Configurations/Boarding/BoardingStayConfiguration.cs` | modify | `FrozenTotalMinor`, room-assignment rows |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `011_BoardingHandoff` + snapshot |
| `src/Animora.Persistence.Sqlite/Queries/BoardingQueries.cs` | create | stays/detail/rooms/items/history projections |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` | modify | seed rooms + service definitions (reference data) |
| `src/Animora.Persistence.Sqlite/Seed/BoardingSampleData.cs` | create | stays + history |
| `src/Animora.Client.Services/Boarding/BoardingQueryService.cs` | create | real `IBoardingQueryService` |
| `src/Animora.Client.Services/Boarding/BoardingMapping.cs` | create | read models ↔ Contracts DTOs, stable item order |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify | add 5 command types |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap `IBoardingQueryService` |
| `src/Animora.Desktop/Pages/Boarding/BoardingPageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Mocks/MockBoardingQueryService.cs` | delete | replaced |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 6 codes |
| `tests/Animora.Domain.Tests/StayChargeCalculatorTests.cs` | create | night counting, billing modes, boundaries |
| `tests/Animora.Application.Tests/BoardingUseCaseTests.cs` | create | transfer capacity, stale index, checkout handoff |
| `tests/Animora.Persistence.Tests/BoardingQueryTests.cs` | create | room occupancy in one statement, paging |

## Implementation steps
1. Add `StayChargeCalculator` + its tests first (pure domain, fastest feedback); append the 6 error
   codes.
2. Add `BoardingBillingHandoff` + configuration, `FrozenTotalMinor`, the room-assignment rows;
   generate `011_BoardingHandoff`.
3. Write `BoardingQueries` — room occupancy and stay accrual each in a single statement.
4. Write the 6 queries and 5 commands with validators, permissions, audit attributes and the new
   `CommandTypes` entries.
5. Implement the stale-index rule in `RemoveStayItemCommand` and the ordering guarantee in
   `BoardingMapping`.
6. Implement `TransferStayRoomCommand` (capacity check inside the transaction, append-only
   assignment history) and the service-side "next free room of same type" resolution.
7. Implement `CheckoutStayCommand` exactly as specified, including the handoff row and the event.
8. Seed rooms + services as reference data; add `BoardingSampleData` behind `SeedSampleData`.
9. Swap the DI line, add the ViewModel line, delete the mock.
10. Write the three test classes.
11. Verify manually: creating a stay, adding/removing items, transferring rooms, checking out — the
    accrued total matches a hand-computed value, checkout creates exactly one handoff row, and the
    history tab pages correctly.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Domain: `StayChargeCalculator`.
- Application: 6 boarding queries, 5 boarding commands, 5 read models.
- Persistence: `BoardingBillingHandoff`, `BoardingStay.FrozenTotalMinor`, room-assignment rows,
  migration `011_BoardingHandoff`, `BoardingQueries`, `BoardingSampleData`, seeded rooms + services.
- Client services: `BoardingQueryService`, `BoardingMapping`.
- Wire: 5 `CommandTypes` entries. `ErrorCodes` += 6. No `Animora.Contracts` change.
- DI: `IBoardingQueryService` → `BoardingQueryService`.
  **Mock deleted: `MockBoardingQueryService.cs`.**

## Rules that apply here
- **A-RULE 018 / R-B23 discipline** — the accrued total is derived; `FrozenTotalMinor` is an audit
  snapshot that the calculator must be able to reproduce (asserted in a test).
- **A-RULE 002** — room-assignment history and the handoff row are append-only.
- **CONVENTIONS §5 / PERFORMANCE.md §6** — occupancy and accrual are single-statement projections;
  history is paged at 200.
- **A-RULE 005** — every stay mutation is a journaled command, settlement-ready for D06.
- **CONVENTIONS §10** — the mock is deleted in the same commit as the swap.
- **R-B24** — nights are counted in the clinic time zone via `ClinicCalendar`, never with
  `DateTime.Now`.

## Out of scope
- Invoicing a checked-out stay → C07 (reads `BoardingBillingHandoff`).
- Stock consumption for boarding items → C06 defines the movement; C07 posts it at invoice time.
- Server-side boarding commands → D06.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `src/Animora.Desktop/Mocks/MockBoardingQueryService.cs` does not exist; no DI line references it.
- `StayChargeCalculatorTests` green: a stay checked in and out on the same day bills 1 night; a
  per-night item over 3 nights bills 3×; a one-time item bills once regardless of nights; the
  frozen total equals a recomputation at `CheckOutUtc`.
- `BoardingUseCaseTests` green: transferring into a full room returns `ROOM_AT_CAPACITY`; removing
  an item with a stale index returns `STAY_ITEM_INDEX_STALE` and deletes nothing; checkout creates
  exactly one `BoardingBillingHandoff` and releases the room; a second checkout returns
  `STAY_ALREADY_CHECKED_OUT`.
- `BoardingQueryTests` green: the rooms query emits one `SELECT` and correct occupancy; the history
  query respects `BoardingHistorySort` and the 200 page cap.
- Manual: all boarding flows work offline and survive restart.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C05 done; advance "Next phase" to `C06`; append a "Repo state" entry listing:
`StayChargeCalculator` and the derived-accrual rule, `BoardingBillingHandoff` + migration
`011_BoardingHandoff` (**C07 consumes both handoff tables**), the stale-index rule for index-based
Contracts members (**C07/C08 reuse it**), rooms/services as reference data, the 6 queries +
5 commands, `BoardingQueryService`/`BoardingMapping`, the DI swap, **mock deleted:
`MockBoardingQueryService.cs`**, `CommandTypes` += 5, `ErrorCodes` += 6, the 3 test classes,
deviations.
