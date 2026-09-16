# Phase 28 — Profile & Settings Screen

**Band:** S · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/profile.html` and `Animora-UI/profile.css` (both full)
- `Animora-UI/assets/profile.js` — full file (341 lines): user info block,
  settings sections, save-shows-toast pattern (explicitly a static demo
  in the reference — "settings live in memory only; every save shows a
  toast and closes nothing").

## Goal
Replace the Profile placeholder with the profile/settings screen: user
info card (name/role/spec/phone/email) + editable settings sections,
matching the reference's save-and-toast interaction pattern.

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

## PROGRESS.md update
Mark Phase 28 done; advance "Next phase" to 29; update "Repo state."
