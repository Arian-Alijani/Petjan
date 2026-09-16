# Phase 02 — Design Tokens & Theme Resources

**Band:** S · **Depends on:** 01 · **Satisfies:** R-4, R-5, R-6

## Read before starting
- `CONVENTIONS.md` §1 (Resources/Theme location), §4 (theming rules)
- `Animora-UI/assets/styles.css` — **only** the `:root { ... }` block
  (lines ~7–44) and the `body`/base typography rules (lines ~1–56). Do not
  read the rest of this 1516-line file; later phases will reference other
  sections when they need them.

## Goal
Translate the reference's design tokens (`:root` CSS variables) into
idiomatic Avalonia `ResourceDictionary` files — colors, radii, shadows,
spacing, typography — as the single shared source every later screen/control
phase consumes. No copying CSS syntax; re-express each token as its
Avalonia equivalent.

## Tasks
1. Create `src/Animora.Desktop/Resources/Theme/Colors.axaml` — a
   `ResourceDictionary` defining a `SolidColorBrush` (and matching `Color`)
   for every `--brand-*`, `--bg*`, `--panel`, `--line*`, `--ink*`, `--muted*`,
   `--danger*`, `--warn*`, `--info*`, `--ok*`, `--lilac*`, `--peach*` variable
   in the reference `:root` block. Use matching resource keys, e.g.
   `Brand600Brush`, `InkBrush`, `DangerBgBrush`, etc. (PascalCase, `Brush`
   suffix for brushes).
2. Create `src/Animora.Desktop/Resources/Theme/Spacing.axaml` — define
   `CornerRadius` resources for `--radius` (16), `--radius-lg` (20),
   `--radius-sm` (10), and a small spacing scale (4/8/12/16/20/24/32 px) as
   `x:Double` resources named `Spacing4`..`Spacing32`, inferred from the
   paddings/gaps visible in the reference CSS (no need to enumerate every
   CSS rule — establish a consistent 4px-based scale that matches what the
   reference visually uses).
3. Create `src/Animora.Desktop/Resources/Theme/Elevation.axaml` — define
   two `BoxShadows` resources, `ShadowCard` and `ShadowPop`, matching the
   reference's `--shadow-card` / `--shadow-pop` values (converted from CSS
   `box-shadow` offsets/blur/color/alpha to Avalonia `BoxShadow` syntax).
4. Create `src/Animora.Desktop/Resources/Theme/Typography.axaml`:
   - Add the Vazirmatn font as an embedded asset. Retrieve a properly
     licensed static/variable Vazirmatn font file (OFL-licensed, matches
     `@import url('...vazirmatn...')` in the reference) and place it under
     `src/Animora.Desktop/Assets/Fonts/`. Reference it via Avalonia's
     `avares://` font resource syntax (`FontFamily="avares://Animora.Desktop/Assets/Fonts#Vazirmatn"`).
     Record the exact font source/version used in `PROGRESS.md`'s decision
     log (no runtime CDN fetch — must be bundled, per `STACK.md`'s
     zero-Internet-at-runtime principle).
   - Define `FontFamily` resource `AppFontFamily` pointing to the bundled font.
   - Define a small type-scale as `x:Double` `FontSize` resources
     (`FontSizeXs`=11, `FontSizeSm`=13, `FontSizeMd`=15, `FontSizeLg`=19,
     `FontSizeXl`=26) matching the reference's observed heading/body sizes.
5. Wire all four dictionaries into `App.axaml`'s
   `Application.Resources` → `ResourceDictionary.MergedDictionaries`, and
   set the application-wide default `FontFamily` to `{StaticResource AppFontFamily}`
   via a base `Style` targeting `TopLevel` (or `Window`/`UserControl`) in `App.axaml`.
6. Update `MainWindow` (from Phase 01) to visibly use two or three of the
   new resources (e.g. background = `{StaticResource BgBrush}`, some text
   using `Brand600Brush`) — just enough to visually prove the resources
   resolve correctly; no real screen content yet.

## Out of scope
- Any reusable `Control`/`UserControl` (Phase 03).
- RTL/localization (Phase 04).
- Any screen content.

## Acceptance criteria
- App builds and runs; `MainWindow` visibly renders with the Vazirmatn font
  and at least brand-teal + neutral background colors from the new
  resources (confirms the merge + font embedding work).
- Every token consumed by a later phase must exist as a named resource in
  one of the four theme files — no raw hex/px literals were introduced.

## PROGRESS.md update
Mark Phase 02 done; advance "Next phase" to 03; log the font asset source
used; update "Repo state."
