# Phase 16 — Boarding — Stay Modals

**Band:** M · **Depends on:** 15 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `boarding.css` (stay-detail/transfer/checkout/new-stay modals, history table, settings rooms/services).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/assets/boarding.js` — **only** lines ~299–928 (stay detail
  modal `_openDetailFor`/`_renderDetailActions`, `renderHistory`,
  `renderSettingsRooms`/`renderSettingsSvcs`, generic `openModal`/`mhead`,
  `openTransferModal`, `openCheckoutModal`, `openNewStayModal`). Skip the
  parts already read in Phase 15.

## Goal
Complete the Boarding screen: stay detail modal (with its status-specific
actions), transfer-room modal, checkout modal, new-stay creation modal,
and the real History/Settings tab content.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

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
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 16 done; advance "Next phase" to 17; update "Repo state."
