# Phase 03 — Base Control Library

**Band:** M · **Depends on:** 02 · **Satisfies:** R-4, R-5

## Read before starting
- `CONVENTIONS.md` §1, §2, §4
- `Animora-UI/assets/styles.css` — sections: `.btn*` (buttons), `.card*`
  (cards), `.stat*` (stat/KPI cards), `.tbl*`/table rules, `.badge`,
  status pill classes (`.danger-bg`/`.ok-bg`/`.warn-bg`/`.info-bg` usage
  pattern), toast styles (search for `.toast`), and modal/drawer base
  styles (search for `.modal`, `.drawer`). Skim only these class groups —
  not the whole file.
- `Animora-UI/assets/icons.js` — full file (it's small, 88 lines): this is
  the reference icon set.

## Goal
Build the small, reusable Avalonia control set that every screen phase
will compose from, so screen phases never hand-roll a button/card/badge
style again. This is the "shared visual vocabulary" layer.

## Tasks
1. `Resources/Theme/Icons.axaml`: re-express the reference's inline-SVG
   icon set as Avalonia `Geometry`/`PathIcon` resources (or a
   `DrawingImage` resource per icon) — re-drawn from the reference SVG
   `path`/`polyline`/`circle` data using Avalonia's own vector primitives,
   not embedded raw SVG strings. Cover at minimum the icon keys used by
   Phase 05's sidebar (`dashboard, calendar, users, clipboard, syringe,
   home, cart, box, receipt, chart, cheque, clock, shield`) plus common
   action icons (`plus, check, x, search, edit, trash, more, filter,
   chevronDown, chevronLeft, chevronRight`). Name resources `Icon<Name>`
   (PascalCase).
2. `Controls/AppButton.axaml(.cs)`: a `UserControl` (or `Button`
   `ControlTheme` in `Resources/Controls/Button.axaml` — prefer
   `ControlTheme` restyle of `Button` for idiomatic Avalonia) exposing
   variants matching `.btn-primary`, `.btn-outline`, `.btn-ghost`,
   `.btn-danger-ghost` from the reference, as a `Classes="primary|outline|
   ghost|danger-ghost"` styling convention on the base `Button`.
3. `Resources/Controls/Card.axaml`: a `ControlTheme` for a `Border`-based
   `Card` (background=`PanelBrush`, border=`Line2Brush`, corner radius=
   `{StaticResource CornerRadius}` (16), shadow=`ShadowCard`), plus a
   `CardHeader` style (bottom border, header text style) matching
   `.card`/`.card-header`/`.card-body`.
4. `Controls/StatCard.axaml(.cs)`: composite `UserControl` matching `.stat`
   — icon tile, value (large number), label, optional delta chip
   (`+12%`/`-4%` styled green/red) — bindable properties `Icon`, `Value`,
   `Label`, `Delta`, `IsDeltaPositive`.
5. `Controls/Badge.axaml(.cs)`: small pill control matching `.badge` +
   status-pill pattern (tone: `neutral|danger|warn|info|ok`), bindable
   `Text`, `Tone`.
6. `Controls/DataTable.axaml(.cs)`: a thin composite wrapping Avalonia's
   `DataGrid` (or a `ItemsControl`-based custom rows list if `DataGrid` is
   not desired) styled to match `.tbl` (row hover, header row style,
   zebra-free flat look per reference) — expose `ItemsSource`, and let
   consuming screens define columns/row templates; this control only
   supplies the **styling shell**, not per-screen columns.
7. `Controls/EmptyState.axaml(.cs)`: icon + title + subtitle centered block,
   matching the reference's empty-state pattern (seen across
   patients/inventory/sale/boarding screens).
8. `Controls/ToastHost.axaml(.cs)` + a simple `IToastService`
   (interface in `Animora.Desktop/Services/IToastService.cs`,
   implementation `ToastService.cs`): a corner-anchored stack of transient
   toast messages (icon + text + tone), matching the reference's
   `window.toast(msg, icon)` pattern/visual, driven by a service call
   `IToastService.Show(text, tone)` rather than a static global. Register
   `IToastService` in DI (Phase 01's composition root).
9. `Controls/ModalHost.axaml(.cs)` + `Controls/DrawerHost.axaml(.cs)`:
   generic overlay hosts (dim backdrop + centered panel / right-anchored
   sliding panel respectively) matching the reference's modal/drawer look.
   Expose a minimal `IDialogService` (`ShowModal(UserControl content)`,
   `ShowDrawer(UserControl content)`, `Close()`) — implementation lives in
   `Services/DialogService.cs`, registered in DI. Screen phases will supply
   the *content* control; this phase only supplies the host chrome.
10. Add a small `Pages/_ControlGallery/ControlGalleryView.axaml` (temporary,
    dev-only) that instantiates one of each new control so it can be
    visually smoke-tested; wire `MainWindow` (temporarily) to show this
    gallery. Phase 05 will replace `MainWindow`'s content with the real
    shell — this gallery view may be deleted or left under a debug-only
    flag at that point (Phase 05 decides; note it in that phase's tasks).

## Out of scope
- Sidebar/topbar/navigation (Phase 05).
- Any screen-specific layout.
- Real icon parity for every single reference icon — only the subset
  listed in Task 1; later screen phases add any additional icon they need
  to `Icons.axaml` as a small addition within their own phase.

## Acceptance criteria
- App builds/runs; the control gallery view visibly shows a styled button
  (all 4 variants), a card, a stat card, a badge (multiple tones), an empty
  state, and can trigger a toast and open a modal and a drawer on demand
  (e.g. via temporary buttons in the gallery).

## PROGRESS.md update
Mark Phase 03 done; advance "Next phase" to 04; note any icon keys
deferred; update "Repo state."
