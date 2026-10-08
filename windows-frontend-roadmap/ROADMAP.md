# Windows Frontend Roadmap — Phase Plan

> Scope: **Avalonia Native Windows desktop client only.** Fidelity target:
> `Animora-UI/*.html` (+ `assets/*.css`, `assets/*.js` as behavioral
> reference — never copied). Stack: `STACK.md` §3.1. Architecture:
> `ARCHITECTURE.md` §7 (client roles), §21 (A-RULEs applicable to a
> presentation client). This file is the phase index; execution detail
> lives one-file-per-phase under `phases/`.

## 0. Sizing bands (every phase must self-declare one, in its own file)

| Band | Meaning | Typical output |
|---|---|---|
| **S — Small** | 1–3 new files, no new external concept | a single simple screen, a utility module |
| **M — Medium** | 4–10 new files, one cohesive feature slice | a screen with a list + detail, a control set |

**No phase may be sized above M.** Any reference screen whose JS exceeds
~700 lines is split into ≥2 phases (list/board vs. modal/drawer/wizard) —
this is why several screens below have a "Part 1/Part 2" pair.

## 1. Dependency rule

Phases execute **in numeric order**. A phase may only start once
`PROGRESS.md` shows all lower-numbered phases as `Done`. Screen phases
(06+) all depend on the foundation phases (01–05) but not on each other
**except** where a "Part 2" phase is explicitly listed as depending on its
"Part 1" (same screen, split for size only).

## 2. Phase index

| # | Phase | Band | Primary `Animora-UI` reference | Depends on |
|---|---|---|---|---|
| 01 | Solution & Project Scaffold | S | — (no UI reference) | — |
| 02 | Design Tokens & Theme Resources | S | `assets/styles.css` (`:root`, base type/spacing) | 01 |
| 03 | Base Control Library | M | `assets/styles.css` (`.btn*`, `.card*`, `.badge`, `.stat`, `.tbl`, modal/toast), `assets/icons.js` | 02 |
| 04 | RTL & Localization Utilities | S | `assets/layout.js` (`toFa`), `assets/cheques-data.js` (`window.J`) — behavior reference only | 01 |
| 05 | App Shell: Sidebar, Topbar, Navigation | M | `assets/layout.js` (`renderSidebar`, topbar, `NAV_ITEMS`, `ROLES`) | 02, 03, 04 |
| 06 | Dashboard Screen (role-based) | M | `dashboard.html`, `assets/dashboard.js`, `assets/dashboard-views.js` | 05 |
| 07 | Reception & Queue Screen | M | `reception.html`, `assets/reception.js` | 05 |
| 08 | Patients & Owners — List/Grid | M | `patients.html`, `assets/patients-data.js`, `assets/patients-ui.js`, `assets/patients-page.js` (list/search/cards section) | 05 |
| 09 | Patients & Owners — Pet File / Owner Sheet | M | `assets/patients-page.js` (owner sheet + pet file tabs section) | 08 |
| 10 | Visits — Doctor Rail & Triage | M | `visits.html`, `assets/visit.js` (triage/rail/new-visit section) | 05 |
| 11 | Visits — Clinical Encounter Wizard | M | `assets/visit.js` (wizard/vitals/history/plan/rx/review section) | 10 |
| 12 | Vaccination Screen | S | `vaccination.html`, `assets/vaccination-data.js`, `assets/vaccination.js` | 05 |
| 13 | Appointments — Calendar Views | M | `appointments.html`, `assets/appointments.js` | 05 |
| 14 | Appointments — Booking Panel | M | `assets/appointments-panel.js` | 13 |
| 15 | Boarding — Board & List | M | `boarding.html`, `assets/boarding.js` (cards/tabs/history/settings section) | 05 |
| 16 | Boarding — Stay Modals | M | `assets/boarding.js` (detail/transfer/checkout/new-stay modal section) | 15 |
| 17 | Inventory — Product List & Settings | M | `inventory.html`, `assets/inventory-data.js`, `assets/inventory.js` (list/settings/subnav section) | 05 |
| 18 | Inventory — Product Modal & Stock Adjust | M | `assets/inventory.js` (product modal/tabs/stock-adjust section) | 17 |
| 19 | Purchase & Suppliers Screen | S | `purchase.html` | 05 |
| 20 | Sale/POS — Invoice Workspace | M | `sale.html`, `assets/sale-page.js` (sidebar/invoice content/history section) | 05 |
| 21 | Sale/POS — Catalog & Payment Modals | M | `assets/sale-page.js` (catalog/payment/refund/custom-item section) | 20 |
| 22 | Cashier Screen (Invoice & Payment Mgmt) | M | `cashier.html`, `assets/cashier.js` | 05 |
| 23 | Cheques — List Views & Side Panels | M | `cheques.html`, `assets/cheques-data.js`, `assets/cheques.js` | 05 |
| 24 | Cheques — Add/Edit Drawer & Bulk Actions | M | `assets/cheques-ui.js` | 23 |
| 25 | Finance — Overview & Income Tabs | M | `expense.html`, `reports.html`, `assets/finance-data.js`, `assets/finance-page.js` (tabs/overview/income section) | 05 |
| 26 | Finance — Expense & Profit Tabs | M | `assets/finance-page.js` (expense/profit section + expense modal) | 25 |
| 27 | History — Unified Ledger | M | `history.html`, `assets/history.js` | 05 |
| 28 | Profile & Settings Screen | S | `profile.html`, `profile.css`, `assets/profile.js` | 05 |
| 29 | Cross-Screen Navigation Wiring & Visual QA | M | all `*.html` (final comparison pass only, no new screens) | 06–28 |

**29 phases total.** Every phase is S or M — no phase is large. Phases
06–28 (23 phases) cover all 17 reference screens; five of the heavier
screens (Patients, Visits, Appointments, Boarding, Inventory, Sale/POS,
Cheques, Finance — 8 screens) are split into two phases each purely to
keep every phase small, per the Locked sizing rule.

## 3. What "done" means globally

The roadmap is complete when Phase 29 is marked `Done` in `PROGRESS.md`:
every screen in `Animora-UI/*.html` has a corresponding Avalonia page that
builds, runs, and is **visually indistinguishable from its reference** (not
merely "comparable" — see §3a and `CONVENTIONS.md` §4a); all screens are
reachable through the app shell's navigation exactly as they are reachable in
the reference (sidebar items + cross-screen quick actions); no backend/sync
wiring exists yet (by design — that is the next, separate roadmap) but every
screen's data dependency is expressed as an `Animora.Contracts` interface with
a mock implementation behind it, so that the future backend/sync roadmap can
swap in a real implementation with **zero** View/ViewModel changes.

## 3a. Global fidelity mandate (applies to every screen phase; overrides softer wording)

The one purpose of this roadmap is a rebuild that a professional designer,
seeing it beside the `Animora-UI` reference, **cannot distinguish from the
reference**. This is enforced by `CONVENTIONS.md` §4a (Fidelity Protocol) and
§6 (Definition of Done, fidelity gate + visual verification). In particular,
for phases 06–29:

- **Nothing visible in the reference may be dropped, simplified, merged, or
  deferred.** Every box, table (with identical columns in identical order),
  card, chart, donut, gauge, progress ring, badge, tab, sub-nav and text label
  is rebuilt. Charts/donuts/gauges/rings are first-class deliverables, drawn
  with Avalonia primitives — never omitted "for budget."
- **Every measurable value comes from the screen's own CSS** (the files listed
  in `CONVENTIONS.md` §8), transcribed exactly — paddings, gaps, radii,
  borders, font sizes/weights, grid templates, fixed/min/max sizes, fills,
  shadows — reusing Phase 02 tokens where they apply. No eyeballing.
- **All user-facing Persian text is reproduced character-for-character;** all
  numbers/dates go through the Phase 04 helpers.
- **A screen phase is not done when it builds — it is done when it has been
  visually verified element-by-element against the reference** (`CONVENTIONS.md`
  §6.7 + §7a) and matches.

If a phase file anywhere below still contains softer language (e.g. "at a
glance," "nice-to-have," "acceptable to omit"), the rules in this section and
`CONVENTIONS.md` §4a take precedence.

## 4. Explicit non-goals of this roadmap (do not implement, even if asked mid-phase)

- Web frontend (Blazor Web App) — separate roadmap.
- macOS-specific packaging/signing — separate roadmap (UI code is shared
  with Windows via Avalonia; only Windows packaging/build is this
  roadmap's concern).
- Backend (ASP.NET Core API, Datasync server, PostgreSQL schema) —
  separate roadmap.
- Real `ISyncEngine`/Datasync client wiring, real EF Core/SQLite local
  store — separate roadmap. Phases 01–29 use mock services behind
  `Animora.Contracts` interfaces only.
- Authentication/login flow UI — not present in `Animora-UI` reference
  (it opens straight to `dashboard.html`); not invented here. If added
  later, it is a new phase appended after 29, not retrofitted into it.

## 5. Where to look next

- Starting fresh? Open `PROGRESS.md`.
- Executing a specific phase? Open `phases/PHASE-<NN>-*.md` for that phase
  number only, plus `CONVENTIONS.md`.
- Need the full requirement rationale? `00-REQUIREMENTS-LOCK.md` (not
  needed for routine phase execution).
