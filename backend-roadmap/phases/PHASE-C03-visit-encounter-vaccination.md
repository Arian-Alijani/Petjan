# Phase C03 — Visit encounter & vaccination (HISTORICAL STUB — already executed)

**Band:** M · **Track:** C · **Depends on:** C02 · **Satisfies:** R-B01, R-B02
**Status:** ✅ Done 2026-09-28 · **Runnable after this phase:** desktop offline yes · server required no

> **Why this file is a stub.** The original self-contained C03 phase file was never committed to the
> repository (no commit in any branch's history touches `phases/PHASE-C03*`), although the phase was
> executed in full. This stub restores the "every phase has a file" invariant for D/E reviewers
> (`docs/Phase-Problem.md` §9). It is **not executable work** — do not re-implement C03. It contains
> only facts recorded elsewhere in the repo; the sources below are authoritative.

## Where the truth lives
| Need | Source |
|---|---|
| What C03 delivered (domain, wire, application, persistence, client services, mocks deleted, tests, verification) | `backend-roadmap/PROGRESS.md` → `### C03 — Visit encounter & vaccination on the real local DB (2026-09-28)` |
| Why C03 deviated from its original plan (D1–D9) | `backend-roadmap/PROGRESS.md` → decision log `### C03 — migration number, reused permissions, note merge, complete/next-dose modeling` |
| Status-table row | `backend-roadmap/PROGRESS.md` → Phase status → `C03` |
| Roadmap position | `backend-roadmap/ROADMAP.md` → Track C table, row `C03` |

## One-paragraph summary (facts from the PROGRESS.md C03 entry)
C03 put the visit encounter and vaccination screens on the real local SQLite store (the C01 pattern):
per-field encounter commands (vitals, note, add/remove prescription line) and `CompleteVisit` — the
single-transaction **C07 billing-handoff seam** (complete visit → one `MedicalHistoryEntry` → one
`VisitBillingHandoff` → best-effort reception ticket `Done` → `MarkReadyForInvoice`); vaccination
due/catalog/history/SMS-template queries plus `MarkVaccinationDone` and `RecordVaccinationReminder`
(**no network**, template rendering only). New `[LocalOnly]` `VisitBillingHandoff` and `SmsTemplateRow`
shipped in migration **`010_C03Schema`** (numbered `010`, not the original plan's `009`, which C01 had
taken). The seven wire commands it registered are `visits.visit.vitals`, `visits.visit.note`,
`visits.rx.add`, `visits.rx.remove`, `visits.visit.complete`, `vaccination.record.administer`,
`vaccination.record.reminder`. Mocks deleted: `MockVisitEncounterService`, `VisitEncounterStore`,
`MockVaccinationService`.

## Hand-off to forward phases
- **D06** handles these seven commands server-side (they are part of its 39-command inventory) and
  reproduces the `VisitBillingHandoff` seam; **D07** consumes the handoff when invoicing.
- The `vaccination.treatment.*` family (treatment plans, migration `031`) is **post-C11** and is *not*
  part of C03 — see `PROGRESS.md` → "Post-C11 drift".
