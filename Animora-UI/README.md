# Animora-UI — reference prototype (look, feel & feature scope only)

## What this is

A static, browser-only prototype (plain HTML + CSS + vanilla JavaScript,
Persian/RTL) that demonstrates the **visual design and feature set** the
real Animora product must implement. It was built to let the product/design
decisions be reviewed and approved quickly, in a throwaway tech stack that
has **nothing to do with** the actual implementation stack.

It is not a partial implementation, not a prototype to evolve, and not a
codebase to port. Treat it exactly like a Figma export that happens to be
clickable: reference it for *what a screen should look like and do*, then
implement that screen from scratch in the real stack (`STACK.md`), following
the real architecture (`ARCHITECTURE.md`).

## What it is not

- **Not the tech stack.** The real client is **Avalonia Native (desktop)**
  and **Blazor Web App (web)** per `STACK.md` §3. This prototype's HTML/CSS/
  JS has no relationship to either.
- **Not the data/business logic.** Every `*-data.js` file here (e.g.
  `assets/patients-data.js`, `assets/finance-data.js`) is in-memory demo
  data for the click-through — not a schema, not a contract, not something
  to translate 1:1 into `Animora.Contracts` or `Animora.Domain`.
- **Not the sync/offline model.** This prototype has no offline story, no
  conflict resolution, no server. All of `ARCHITECTURE.md` §8 (the most
  safety-critical part of the real system) has no counterpart here.
- **Not to be extended with new features.** If a new capability is needed
  that isn't shown here, design it consistent with `ARCHITECTURE.md`/
  `STACK.md` and, if useful, mock it up separately — don't grow this
  directory into a second product.

## What to actually take from it

- **Page inventory / feature scope** — the `*.html` files name the surfaces
  the real product needs: dashboard, reception & queue, patients & owners,
  visits & records, vaccination, appointments, boarding, inventory,
  purchasing, sales/cashier, cheque management, expenses, reports, profile/
  settings. Cross-reference each against `ARCHITECTURE.md` §6's bounded
  contexts to see which context(s) a screen belongs to.
- **Interaction/role model** — `shot_role.py` demonstrates role-based
  dashboard views (e.g. `doctor`, `manager`); this maps to the RBAC + policy
  + resource-based authorization model in `ARCHITECTURE.md` §10.3, not to
  its own `sessionStorage`-based demo switch.
- **Visual language** — minimal/modern styling, RTL Persian layout, the
  `*.css` files — as design reference for whoever implements the real
  Avalonia/Blazor UI (design-system decisions themselves are out of scope
  for `ARCHITECTURE.md`/`STACK.md`, which are architecture/technology docs).
- **`*.srcmap.json` files** are prototype-tooling artifacts (source maps for
  the design tool that generated these pages) — ignore them; they carry no
  product information beyond what the corresponding `.html` already shows.

## Local tooling in this folder

`shot_role.py` and `shot_tabs.py` are one-off Playwright screenshot scripts
used to review the prototype during design (they expect a local static
server on `:8080` serving this folder, e.g. `python -m http.server 8080`).
They are dev-only convenience scripts for *this prototype*, not part of any
product build — do not wire them into the real project's CI/build.
