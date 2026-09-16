# Phase 07 — Reception & Queue Screen

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/reception.html` (full — 8884 bytes)
- `Animora-UI/assets/reception.js` — full file (476 lines): queue list,
  stats, walk-in modal, ticket actions (admit/enter-visit/leave/no-show/
  toggle-urgent/call-doctor).

## Goal
Replace the Reception placeholder with the live-queue screen: hero/stats
row, the patient queue list with per-row state + actions, and the
"add walk-in" modal.

## Contracts to add (`Animora.Contracts/Reception/`)
- `IReceptionQueueService` — `Task<IReadOnlyList<QueueTicket>> GetQueueAsync()`,
  `Task AdmitAsync(ticketId)`, `Task EnterVisitAsync(ticketId)`,
  `Task LeaveQueueAsync(ticketId)`, `Task MarkNoShowAsync(ticketId)`,
  `Task ToggleUrgentAsync(ticketId)`, `Task<QueueTicket> CreateWalkInAsync(WalkInRequest req)`.
- `QueueTicket` DTO — mirror the fields `renderList`/`actionsFor` in the
  reference use (patient/owner name, requested doctor, state, urgent flag,
  arrival time, sequence number).
- `WalkInRequest` DTO — name, owner, phone, doctorId, urgent flag (mirrors
  `createWalkin` params).

## Mock to add
- `MockReceptionQueueService` in `Animora.Desktop/Mocks` — in-memory list
  of `QueueTicket`, mutated in place by the action methods (no persistence
  needed); seed 4–6 sample tickets across different states.

## View/ViewModel tasks
1. `Pages/Reception/ReceptionPageViewModel.cs`: injects
   `IReceptionQueueService`; exposes `ObservableCollection<QueueTicket>
   Queue`, stat properties (waiting count, urgent count, etc. — derived
   from `Queue`, matching `renderStats`), and `[RelayCommand]` methods
   `Admit`, `EnterVisit`, `LeaveQueue`, `MarkNoShow`, `ToggleUrgent`,
   `OpenWalkInModal`, `SubmitWalkIn` (each taking a `QueueTicket`/`WalkInRequest`
   parameter as appropriate), calling the service then refreshing `Queue`.
2. `Pages/Reception/ReceptionPageView.axaml(.cs)`: hero/stat row (Phase 03
   `StatCard`s matching `renderStats`), queue list (`ItemsControl` or
   `DataTable`, each row showing patient/owner/doctor/time/state pill +
   the action buttons relevant to that ticket's state, matching
   `actionsFor`'s conditional action set).
3. `Pages/Reception/WalkInModalView.axaml(.cs)` + a small
   `WalkInModalViewModel` (name/owner/phone/doctor-picker/urgent-toggle
   fields + Save/Cancel commands): opened via Phase 03's `IDialogService`
   from `ReceptionPageViewModel.OpenWalkInModal`.
4. Update `Shell/NavigationMenu.cs`'s `reception`... **note:** reception
   is not a top-level `NAV_ITEMS` entry in the reference (it's reached via
   dashboard quick-links/deep-links only) — do not add a new sidebar item;
   instead register `ReceptionPageViewModel` as the navigation target used
   by Phase 06's dashboard "پذیرش" links and any other quick-action already
   pointing at it, replacing the placeholder.

## Out of scope
- Hash-based deep-linking (`interpretHash` in the reference) — not
  applicable; Avalonia navigation replaces this mechanism entirely.
- Calling doctors / real notification integration — `CallDoctor` is a
  mock/no-op action (toast confirmation only).

## Acceptance criteria
- App builds/runs; navigating from the Dashboard's reception quick-link
  reaches this screen; queue list renders with correct per-state actions;
  admitting/leaving/no-show/urgent-toggle visibly update the list; the
  walk-in modal opens, accepts input, and adds a new ticket to the list.

## PROGRESS.md update
Mark Phase 07 done; advance "Next phase" to 08; update "Repo state."
