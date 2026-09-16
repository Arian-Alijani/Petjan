# Phase 04 — RTL & Localization Utilities

**Band:** S · **Depends on:** 01 · **Satisfies:** R-6

## Read before starting
- `CONVENTIONS.md` §4 (RTL rule)
- `Animora-UI/assets/layout.js` — only the `window.toFa` function
  (search for `window.toFa =`, near the end of the file) — behavior
  reference for Persian-digit formatting only.
- `Animora-UI/assets/cheques-data.js` — only the `window.J = { ... }` block
  (search for `window.J =`) — behavior reference for Gregorian↔Jalali
  conversion and the `J.format`/`J.diff` shape only. Do not read the rest
  of this file.

## Goal
Provide small, idiomatic .NET utility classes for the three
localization behaviors the reference relies on everywhere: RTL layout,
Persian-digit number formatting, and Jalali calendar display — as shared
services every screen phase can inject/call, so no phase re-invents this.

## Tasks
1. In `App.axaml`, set the root `Window`/`TopLevel` style (or
   `Application.Current`'s default) `FlowDirection="RightToLeft"` so every
   descendant control mirrors by default, matching `<html dir="rtl">` in
   the reference. Confirm this is set at the single top-level location
   `CONVENTIONS.md` §4 specifies (not per-screen).
2. Create `src/Animora.Desktop/Localization/PersianNumberFormatter.cs`:
   a static/pure class exposing `ToPersianDigits(object value)` (converts
   ASCII digits 0–9 to Persian ٠١٢٣٤٥٦٧٨٩, matching `toFa`'s behavior) and
   `FormatGrouped(long value)` (thousands separator using `٬`, matching
   the reference's `group()` helper in `assets/dashboard.js`). Write from
   .NET string/culture idioms — do not port the JS regex verbatim, re-derive
   the same output behavior in C#.
3. Create `src/Animora.Desktop/Localization/JalaliDate.cs`: a small,
   correct Gregorian↔Persian(Jalali) calendar converter (a `readonly
   struct JalaliDate { int Year, Month, Day }` with `FromDateTime`/
   `ToDateTime` and a `ToString(string format)` producing the Persian
   month-name + Persian-digit output the reference shows, e.g. "۱۴ مهر
   ۱۴۰۴"). Use .NET's built-in `System.Globalization.PersianCalendar` as
   the conversion engine (do not hand-roll the Jalali math — .NET already
   ships a correct implementation) and layer the Persian month-name table
   + digit formatting on top of it.
4. Create `src/Animora.Desktop/Localization/PersianMonthNames.cs`: the
   12 Persian month name constants (فروردین..اسفند) as a static
   `string[]`, consumed by `JalaliDate.ToString`.
5. Add a tiny `IValueConverter` implementations in
   `src/Animora.Desktop/Localization/Converters.cs`:
   `PersianDigitsConverter` and `JalaliDateConverter`, registered as
   `x:Static` resources or in `App.axaml` resources under keys
   `PersianDigitsConverter` / `JalaliDateConverter`, so `.axaml` bindings
   can do `{Binding Count, Converter={StaticResource PersianDigitsConverter}}`.
6. Update the Phase 03 control gallery (or `MainWindow` if the gallery was
   removed) to render one `TextBlock` proving Persian-digit formatting and
   one proving Jalali date formatting, confirming end-to-end wiring.

## Out of scope
- Any screen-specific date pickers/calendars (later phases build their own
  UI on top of `JalaliDate`, e.g. the Appointments calendar in Phase 13).
- Localized string tables / resource (.resx) files for UI copy — the
  reference is single-language (Persian) hardcoded in each screen's XAML
  text; this roadmap does not add a translation/resource-string layer
  unless a later phase file says otherwise (none currently do).

## Acceptance criteria
- App builds/runs; the proof `TextBlock`s render correctly right-to-left,
  in Persian digits, with a correctly-converted Jalali date matching
  today's real Jalali date (spot-check against a known Gregorian→Jalali
  reference date).

## PROGRESS.md update
Mark Phase 04 done; advance "Next phase" to 05; update "Repo state."
