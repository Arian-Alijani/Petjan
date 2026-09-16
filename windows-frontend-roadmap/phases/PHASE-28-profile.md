# Phase 28 — Profile & Settings Screen

**Band:** S · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `profile.css` + `appointments.css` (user info block, settings sections/toggles — note the reference page also links `appointments.css`).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/profile.html` and `Animora-UI/profile.css` (both full)
- `Animora-UI/assets/profile.js` — full file (341 lines): user info block,
  settings sections, save-shows-toast pattern (explicitly a static demo
  in the reference — "settings live in memory only; every save shows a
  toast and closes nothing").

## Goal
Replace the Profile placeholder with the profile/settings screen: user
info card (name/role/spec/phone/email) + editable settings sections,
matching the reference's save-and-toast interaction pattern.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/Profile/`)
- `IUserProfileService` — `Task<UserProfile> GetAsync()`,
  `Task SaveAsync(UserProfile)`,
  `Task<AppSettings> GetSettingsAsync()`,
  `Task SaveSettingsAsync(AppSettings)`.
- `UserProfile` DTO (name, role, specialty, phone, email, avatar
  initials), `AppSettings` DTO (whatever toggle/preference sections the
  reference shows — enumerate from `profile.html`'s markup sections).

## Mock to add
- `MockUserProfileService` — returns the same sample profile shape the
  reference shows (re-derived, not copied verbatim), persists in-memory
  only for the session (mirrors the reference's explicit "no real
  persistence" design intent).

## View/ViewModel tasks
1. `Pages/Profile/ProfilePageViewModel.cs`: `Profile`, `Settings` bound
   properties, `SaveProfile`/`SaveSettings` commands (each calls the
   service then shows a success toast via Phase 03's `IToastService`,
   mirroring the reference's explicit "closes nothing, just toasts"
   behavior — do not invent a stricter validation/close flow the
   reference doesn't have).
2. `Pages/Profile/ProfilePageView.axaml(.cs)`: user info card (avatar
   initials tile + fields), settings sections as cards (mirrors
   whatever grouping `profile.html`/`profile.css` shows), Save buttons
   per section.
3. Update `Shell/NavigationMenu.cs`'s `profile` nav item to target
   `ProfilePageViewModel`.

## Out of scope
- Real authentication/account-security flows — none exist in the
  reference beyond static display; do not invent them.

## Acceptance criteria
- App builds/runs; Profile nav item shows the info card + settings
  sections; editing and saving any field shows a success toast and
  retains the edited value in the running session.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 28 done; advance "Next phase" to 29; update "Repo state."
