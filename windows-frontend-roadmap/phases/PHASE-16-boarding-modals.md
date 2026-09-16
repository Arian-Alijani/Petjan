# Phase 16 — Boarding — Stay Modals

**Band:** M · **Depends on:** 15 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/assets/boarding.js` — **only** lines ~299–928 (stay detail
  modal `_openDetailFor`/`_renderDetailActions`, `renderHistory`,
  `renderSettingsRooms`/`renderSettingsSvcs`, generic `openModal`/`mhead`,
  `openTransferModal`, `openCheckoutModal`, `openNewStayModal`). Skip the
  parts already read in Phase 15.

## Goal
Complete the Boarding screen: stay detail modal (with its status-specific
actions), transfer-room modal, checkout modal, new-stay creation modal,
and the real History/Settings tab content.

## Contracts to add/extend (`Animora.Contracts/Boarding/`)
- Extend `IBoardingQueryService` with:
  `Task<StayDetail> GetDetailAsync(stayId)`,
  `Task TransferAsync(stayId, string newRoom)`,
  `Task CheckoutAsync(stayId, CheckoutRequest)`,
  `Task<BoardingStay> CreateStayAsync(NewStayRequest)`,
  `Task<IReadOnlyList<BoardingStay>> GetHistoryAsync()`,
  `Task<IReadOnlyList<RoomInfo>> GetRoomsAsync()`,
  `Task<IReadOnlyList<ServiceInfo>> GetServicesAsync()`.
- `StayDetail`, `CheckoutRequest`, `NewStayRequest`, `RoomInfo`,
  `ServiceInfo` DTOs (mirror the corresponding render functions' fields).

## Mock to add
- Extend `MockBoardingQueryService` with the above, backed by the same
  in-memory stay list from Phase 15; a small static room/service list.

## View/ViewModel tasks
1. `Pages/Boarding/StayDetailModalViewModel.cs` + `.axaml`: detail fields
   + status-specific action buttons (mirrors `_renderDetailActions`),
   opened via Phase 03 `IDialogService.ShowModal` from Phase 15's
   `OpenStayDetail`.
2. `Pages/Boarding/TransferRoomModalViewModel.cs` + `.axaml`: room picker
   + confirm (mirrors `openTransferModal`).
3. `Pages/Boarding/CheckoutModalViewModel.cs` + `.axaml`: checkout summary
   + confirm (mirrors `openCheckoutModal`).
4. `Pages/Boarding/NewStayModalViewModel.cs` + `.axaml`: pet/owner pick +
   room/dates/services form (mirrors `openNewStayModal`), opened from
   Phase 15's `OpenNewStay`.
5. `Pages/Boarding/BoardingHistoryTabView.axaml` /
   `BoardingSettingsTabView.axaml`: replace Phase 15's placeholder
   sections with real content (mirrors `renderHistory`/
   `renderSettingsRooms`/`renderSettingsSvcs`).
6. Wire Phase 15's `OpenStayDetail`/`OpenNewStay` placeholders to real
   modal navigation; ensure detail modal's transfer/checkout actions open
   the corresponding modals (modal-from-modal, matching the reference's
   own nested-modal pattern).

## Out of scope
- None — this phase completes the Boarding screen entirely.

## Acceptance criteria
- App builds/runs; every action from Phase 15's board (detail/new-stay)
  and this phase's modals (transfer/checkout) works end-to-end and
  updates the board/list/history correctly; Settings tab shows rooms/
  services lists.

## PROGRESS.md update
Mark Phase 16 done; advance "Next phase" to 17; update "Repo state."
