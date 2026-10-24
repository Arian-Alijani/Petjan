# TODO — Appointments calendar interactions + Dashboard loading shift

> Self-contained execution plan. A fresh model can implement straight from this file
> WITHOUT re-exploring the repo. All root-causes, exact file/line anchors, and the
> decided fix are already resolved below. **After finishing each task, run
> `scripts/git/push.sh "<msg>"` (MANDATORY, locked rule).**
>
> Repo: Animora (.NET 10, Avalonia + CommunityToolkit.Mvvm, Persian/RTL).
> Build check (optional, fast): `scripts/sandbox/build.sh` → expect 0/0. Do NOT run full test suite.

---

## CONTEXT YOU NEED (already discovered — do not re-derive)

### Screen 1 — Appointments calendar ("صفحه نوبت‌ها")
The day/week time grid lives in:
- `src/Animora.Desktop/Pages/Appointments/TimeGridSurface.axaml` (+ `.axaml.cs`)
- `src/Animora.Desktop/Pages/Appointments/TimeGridDragController.cs`  ← the pointer-gesture controller
- `src/Animora.Desktop/Pages/Appointments/AppointmentsCalendarViewModel.cs`  ← all data logic

Visual tree of one column (inside `TimeGridSurface.axaml`):
- `Panel Name="ColumnSurface" Tag="drag-surface"` (the drag surface; `DataContext` = `AppointmentColumnViewModel`).
  - hour-separator `ItemsControl`
  - `Border Name="DragBox"` ghost box, `IsHitTestVisible=False`
  - events `ItemsControl` → each event is a `Button Classes="ev-card" Tag="event-card"`
    with `Command="{Binding #Root...OpenBookingCommand}"` and `CommandParameter="{Binding}"`
    (the `AppointmentEventViewModel`).
  - "now" line `Panel` (Height 0).

The 3 required interaction modes:
1. **Drag on empty grid → create new booking** (drag-to-create).
2. **Click an existing card → open its detail** (click-to-view).
3. **Drag a card to another time/day → reschedule** (drag-to-move).

Gesture flow in `TimeGridDragController` (tunnelling handlers on the surface host):
- `OnPointerPressed` (Tunnel): stores a `DragSession`; chooses `DragMode.Move` if the press
  landed on a movable card (`card.Model.IsReadOnlyProjection == false && card.Model.CanMove`),
  else `DragMode.Create`. Does **not** capture and does **not** mark handled (so a plain click
  can still reach the card `Button`).
- `OnPointerMoved` (Tunnel): once movement exceeds `DragThreshold = 4`px it sets
  `IsDragging = true`, calls `e.Pointer.Capture(session.Surface)` and shows the ghost box.
- `OnPointerReleased` (Tunnel): clears session, then **always** calls `e.Pointer.Capture(null)`
  and `HideBox(session)`; if `IsDragging` it runs `MoveAppointmentFromDragAsync` /
  `CreateBookingFromDragAsync`; if not dragging it returns (meaning to let the `Button` click run).

ViewModel entry points (already correct, do not change their bodies):
- `OpenBookingCommand` (generated from `OpenBooking(object?)`, line ~1980): card VM → detail drawer;
  null/other → new-booking sheet. Public, accepts `AppointmentEventViewModel`.
- `CreateBookingFromDragAsync(date, doctorId, startMinutes, durationMinutes)` (line ~2065).
- `MoveAppointmentFromDragAsync(card, newDate, newDoctorId, newStartMinutes)` (line ~2087) —
  re-reads detail for the concurrency version, calls `UpdateAsync`, toasts, `RefreshAfterWrite()`
  on success, `RequestRefresh()` (snap back) on failure. Already server/pull/push-integrated.
- `SupportsTimeGridDrag => !IsMonthView && !CalendarModeIsDayOnly` (line ~2058).

#### ROOT CAUSE (confirmed) — why only "create new" works and modes 2 & 3 misbehave
`OnPointerReleased` calls `e.Pointer.Capture(null)` **unconditionally**, including on a plain
click. In Avalonia a `Button` captures the pointer on its *bubbling* `PointerPressed` and raises
`Click` on its *bubbling* `PointerReleased` only while it still holds that capture / `IsPressed`.
The controller's release handler runs in the **Tunnel** phase — i.e. BEFORE the Button's bubbling
release — and `Capture(null)` there drops the Button's capture, firing `PointerCaptureLost` on the
Button which sets `IsPressed = false`. When the bubbling release finally reaches the Button, Click
is **not** raised. Result: **clicking a card never runs `OpenBookingCommand`**, so mode 2
(click-to-view) is dead. The regression was introduced by commit `ffc12516`
("[T2+T3] drag-to-move cards & drag-to-create booking"); before it, the Button click worked.
Drag-to-create works because it does not depend on the Button's click. Drag-to-move's data path is
correct, but the same blanket capture churn makes gestures fragile; the fix below also hardens it.

Note: there is intentionally **no create-on-click** for empty grid cells in day/week view (only
the toolbar «ثبت نوبت جدید» button and drag-create create). Keep it that way — do not add one.

#### DECIDED FIX (calendar) — minimal, standard, low-risk
In `TimeGridDragController.OnPointerReleased`, **only** release capture and hide the box when the
gesture actually became a drag. For a plain click, touch nothing so the `Button`'s native Click
(→ `OpenBookingCommand`) fires normally:

```csharp
private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
{
    if (_session is not { } session) return;
    _session = null;

    if (!session.IsDragging)
    {
        // Plain click: do NOT clear the pointer capture or mark handled — let the
        // event-card Button raise Click (→ OpenBookingCommand) / empty space stays inert.
        return;
    }

    HideBox(session);
    e.Pointer.Capture(null);
    e.Handled = true;

    var (startMinutes, durationMinutes) = session.ResolveRange(e.GetPosition(session.Surface));
    if (session.Mode == DragMode.Move && session.Card is { } card)
        _ = session.ViewModel.MoveAppointmentFromDragAsync(card, session.Column.Date, session.Column.DoctorId, startMinutes);
    else
        _ = session.ViewModel.CreateBookingFromDragAsync(session.Column.Date, session.Column.DoctorId, startMinutes, durationMinutes);
}
```

`OnPointerCaptureLost` already only hides the box + clears session — keep it (it correctly
handles an interrupted drag; for a never-captured click it is a harmless no-op because no capture
was taken). `HideBox`/`UpdateGhost`/`ResolveRange`/press/move handlers stay unchanged.

Why this is correct & non-conflicting across all 3 modes:
- Click a card → no drag → handler returns without clearing capture → Button Click →
  `OpenBookingCommand(card)` → detail drawer. (mode 2 fixed)
- Drag on empty grid → `IsDragging` true, mode Create → `CreateBookingFromDragAsync`. (mode 1)
- Drag a card → press picks mode Move, `IsDragging` true → `MoveAppointmentFromDragAsync`. (mode 3)
- The press handler still never marks handled, so a click still reaches the Button; capture is now
  only ever released by the code path that took it (drag), removing the Button-click interference.
- Layer integrity: all data mutation stays in the ViewModel (`Move/Create...Async`), which already
  does concurrency-version read, `UpdateAsync`, toast, and `RefreshAfterWrite()` so other clients
  stay in sync via the existing pull/push/refresh pipeline. No new data path is added.

---

### Screen 2 — Dashboard top loading banner that shakes the page
Files:
- `src/Animora.Desktop/Pages/Dashboard/DashboardPageView.axaml`
- `src/Animora.Desktop/Pages/Dashboard/DashboardPageViewModel.cs`
- `src/Animora.Desktop/Pages/Dashboard/DashboardViewState.cs`

Layout: the page root is a vertical `StackPanel Spacing="20"`. Its FIRST child is a state
`Border Classes="card"` with `IsVisible="{Binding IsStateVisible}"` containing an indeterminate
`ProgressBar IsVisible="{Binding IsBusy}"` + a `StateText`.

#### ROOT CAUSE (confirmed) — the "shake/jump"
`IsStateVisible => ViewState != DashboardViewState.Ready` (ViewModel line ~80). `ViewState` is
resolved by `DashboardViewStateResolver.Resolve(...)`: when data is already shown AND a refresh is
running it returns `Refreshing` (not `Ready`). A background refresh timer (`OnRefreshTick`) fires
periodically, so on each refresh `IsStateVisible` flips `false → true → false`. Because the banner
is a layout child at the top of the `StackPanel`, showing/hiding it adds/removes its height **plus**
the 20px `Spacing`, pushing all cards down and snapping them back each refresh → the page visibly
jumps. The user wants the jump gone while keeping the dashboard's data-update system working.

#### DECIDED FIX (dashboard) — do not let a background refresh toggle a layout child
When data is already on screen, a `Refreshing` refresh must NOT insert/remove the top banner.
Hide the banner for the `Refreshing` state (data is visible, nothing to announce that justifies a
reflow). Keep it for the states that genuinely need the operator's attention or have no data:
`InitialLoading`, `Empty`, `Stale`, `Error`.

Implementation — change `IsStateVisible` in `DashboardPageViewModel.cs` (line ~80) from:
```csharp
public bool IsStateVisible => ViewState != DashboardViewState.Ready;
```
to:
```csharp
// A background refresh over already-shown data must not toggle a layout child (it reflows the page
// and makes it "shake"). The refresh still runs and republishes data; we just don't announce it
// with a top banner. Banner stays for states with no/attention-needing data.
public bool IsStateVisible => ViewState is not (DashboardViewState.Ready or DashboardViewState.Refreshing);
```
The existing `[NotifyPropertyChangedFor]`/`NotifyViewState()` wiring (lines ~70, ~89-96) already
raises `IsStateVisible`; no extra notification is needed. `IsBusy`/the refresh coordinator are
untouched, so data still updates on every tick (`OnRefreshStateChanged` → `NotifyViewState`),
`LastUpdatedText` still advances, and `Stale`/`Error` still surface failures. No layout element is
added or removed during a normal refresh → no shake.

(Optional, only if you want a visible "updating" hint without reflow: a zero-height overlay such as
a top `ProgressBar` with `IsVisible="{Binding IsBusy}"` placed in a `Panel`/overlay that does not
participate in the `StackPanel` flow. NOT required by the task — the task says remove the shaking
indicator but keep updates working. Prefer the minimal change above; skip the overlay unless asked.)

---

## TASK LIST (do in order; push after EACH)

- [ ] **T1 — Fix calendar pointer-capture regression (modes 1–3).**
  Edit `src/Animora.Desktop/Pages/Appointments/TimeGridDragController.cs` →
  rewrite `OnPointerReleased` exactly as in "DECIDED FIX (calendar)" above so a plain click no
  longer clears the Button's capture. Do not change the press/move handlers, `ResolveRange`,
  `UpdateGhost`, `HideBox`, or the ViewModel.
  ➜ `scripts/git/push.sh "fix(appointments): don't drop pointer capture on click so card click opens detail (modes 1-3 work)"`

- [ ] **T2 — Fix dashboard refresh banner layout shift.**
  Edit `src/Animora.Desktop/Pages/Dashboard/DashboardPageViewModel.cs` → change `IsStateVisible`
  to exclude `Refreshing` (and `Ready`) as in "DECIDED FIX (dashboard)". No XAML change needed.
  ➜ `scripts/git/push.sh "fix(dashboard): don't toggle top state banner on background refresh (stop page shake); updates still run"`

- [ ] **T3 — Optional sanity build (fast, Desktop only).**
  `scripts/sandbox/build.sh` → expect 0 warnings / 0 errors. If it fails, fix and push again.
  (Skip only if the environment cannot build; do NOT run the full test suite.)
  ➜ push only if a code change was needed.

- [ ] **T4 — FINAL: delete this plan file.**
  Delete ONLY `Plan/TODO-calendar-dashboard-fixes.md` (leave everything else, incl. Plan/reports).
  ➜ `scripts/git/push.sh "chore(plan): remove completed calendar+dashboard fix plan"`

---

## GUARDRAILS
- Push after every task (locked rule). If `push.sh` exits 3 → call `setup_github_environment` once, re-run.
- Keep changes minimal and targeted; no unrelated refactors.
- ViewModels own data; views own geometry/pointer — preserve that seam.
- Do not add a create-on-click to empty grid cells; drag-create + toolbar button are the create paths.
