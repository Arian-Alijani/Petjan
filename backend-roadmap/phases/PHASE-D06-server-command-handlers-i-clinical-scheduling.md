# Phase D06 — Server command handlers I — clinical & scheduling

**Band:** M · **Track:** D · **Depends on:** D05 · **Satisfies:** R-B01, R-B17
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §3, §4, §5, §6 (items 2, 9), §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D05 entry — `IServerCommandHandler<TPayload>`,
  `ServerCommandContext`, `ServerCommandEffect`, `ServerCommandRegistry`, `CommandPermissions`,
  `IServerUnitOfWork`; C02/C03/C04/C05 entries — the client command names and payload shapes;
  A04/A05 entries — the clinical and scheduling domain invariants)
- `ARCHITECTURE.md` §8.5 (the conflict-policy matrix rows for patient demographics, medical history,
  appointment slot), §11.3 · `STACK.md` §6.2
- `A-RULE 002`, `A-RULE 003`, `A-RULE 004`, `A-RULE 014`, `A-RULE 018`
- `src/Animora.Domain/Patients/**`, `src/Animora.Domain/Clinical/**`,
  `src/Animora.Domain/Scheduling/**`, `src/Animora.Domain/Boarding/**`
- `src/Animora.Contracts.Wire/Commands/CommandTypes.cs`

## Goal
Every **non-financial** business command the desktop can raise settles on the server against the
real SQL Server model, with the domain conflict policy — not a row-version resolver — deciding each
outcome: patients/owners, visits/triage, encounter/vaccination, appointments, boarding. The
handlers re-run the **same domain invariants** the client already ran; the server's answer is the
authoritative one.

## Design decisions (pre-made — implement, do not re-derive)
- **Scope = exactly the command types C01–C05 registered in `CommandTypes`.** The complete list,
  grouped, is the handler inventory (one `sealed class <Verb><Aggregate>ServerHandler` per row, all
  in `src/Animora.Server/Handlers/Clinical/` and `.../Scheduling/`):
  | Group | `CommandType` prefix | Commands |
  |---|---|---|
  | Owners & patients (C01) | `patients.` | `owner.register`, `owner.update`, `pet.register`, `pet.update`, `pet.weight.record` |
  | Reception & visits (C02) | `reception.` / `visits.` | `queue.enqueue`, `queue.call`, `queue.admit`, `queue.cancel`, `visit.open`, `visit.triage.record`, `visit.assign`, `visit.cancel` |
  | Encounter & vaccination (C03) | `visits.` / `vaccination.` | `visit.note.save`, `visit.rx.add`, `visit.rx.remove`, `visit.complete`, `vaccination.record`, `vaccination.schedule`, `vaccination.reminder.mark` |
  | Appointments (C04) | `scheduling.` | `appointment.book`, `appointment.move`, `appointment.cancel`, `appointment.checkin`, `appointment.noshow` |
  | Boarding (C05) | `boarding.` | `stay.create`, `stay.item.add`, `stay.room.assign`, `stay.checkout`, `stay.cancel` |
  Every one gets its `CommandPermissions` entry in the same commit (D05's test enforces coverage).
  **No new `CommandTypes` constant is invented here** — if a name is missing, that is a C-phase
  defect to log in the decision log, not a new name to coin.
- **Payload shapes are the wire payloads the client already serializes.** Each handler declares
  `sealed record <Name>PayloadV1` in `src/Animora.Contracts.Wire/Payloads/Clinical/` (or
  `/Scheduling/`) and the **client's `IJournaledCommand` serialization must produce exactly that
  shape** — a `PayloadContractTests` fact in `Animora.Application.Tests` round-trips each client
  command through its wire payload and fails on any field drift. This is the guard that keeps the
  two halves of the pipeline honest without a shared serializer hack.
- **Handler skeleton (identical for all 30, written once as a documented template):**
  1. Load the aggregate by `(TenantId, Id)` **tracked** (writes need tracking; D02's filters make
     the tenant scope implicit).
  2. If absent and the command is an update → `AGGREGATE_NOT_FOUND` → `Rejected`.
  3. **Concurrency detection** — if the envelope carried `ExpectedAggregateVersion`, D05 already
     compared it; handlers additionally rely on EF's `rowversion` check at `SaveChanges`. A
     `DbUpdateConcurrencyException` maps to `Conflict` + `CONCURRENCY_CONFLICT` — **detection only**.
  4. **Domain policy decides** (A-RULE 004): the handler calls the same domain method the client
     called (`Visit.Complete(...)`, `Appointment.MoveTo(...)`, `BoardingStay.CheckOut(...)`), so a
     rejection is a *domain* rejection with the domain's own error code — never a generic
     "server wins".
  5. Return `ServerCommandEffect(EntityId, EntityVersion (base64 via `RowVersionCodec`),
     FinancialEffect.None, AggregateVersion)`. **Every handler in this phase returns
     `FinancialEffect.None`** — that is what makes it phase D06 and not D07; a test asserts it.
  6. Raise domain events into the outbox via `IServerOutbox.EnqueueAsync` (the port is declared
     here with a **no-op default implementation**; D09 supplies the real table + relay, so no
     handler is touched again in D09).
  All handlers use `IServerUnitOfWork.ExecuteAsync`, so the business write and D05's idempotency row
  commit in one transaction (gate G).
- **Per-type conflict policy, exactly as `ARCHITECTURE.md` §8.5 mandates — no generic resolver
  anywhere in this phase:**
  | Data | Policy implemented here |
  |---|---|
  | Patient/owner demographic field | `DemographicMergePolicy` — field-aware: a field the server changed since the client's base version is **preserved** and reported in `SettlementResult.Detail` as `merged:<field>` (the client shows a review prompt); non-conflicting fields apply. Never a whole-row overwrite. |
  | Medical history / visit notes | **Append-only**: `visit.note.save` writes a new `MedicalHistoryEntry` revision; an existing revision is never mutated. History is preserved in full. |
  | Appointment slot | `SlotAvailabilityPolicy` — domain validation against the server's authoritative calendar; a taken slot is **rejected** (`SLOT_TAKEN`) with the conflicting appointment's id in `Detail`, never silently moved. |
  | Vaccination record | Idempotent on `(PetId, VaccineCode, AdministeredOn)` — a duplicate record is a **replay**, not a second dose. |
  | Boarding stay | Domain workflow; a stay already `CheckedOut` refuses further item additions (`STAY_CLOSED`). |
  | Reception queue entry | Domain state machine; calling an already-admitted ticket is a **no-op success** (idempotent), not an error. |
  `ResponsibilityMap` (D05) gains the rows for each new error code, so the UX contract stays
  complete.
- **Cross-feature effects stay inside the domain**, mirroring the client exactly: `visit.complete`
  creates the `VisitBillingHandoff` row (C03's shape) and `stay.checkout` the
  `BoardingBillingHandoff` row (C05's) — the invoice itself is **D07's** command. This keeps the
  clinical→money seam identical on both sides (R-B01).
- **Read-back is not this phase's job.** A handler never returns a projection; the client learns the
  new state through replication (D08) or its own local write. `ServerCommandEffect.EntityVersion` is
  the only state token returned (A-RULE 014).
- **Registration:** `src/Animora.Server/Handlers/ClinicalHandlerRegistration.cs` and
  `SchedulingHandlerRegistration.cs` — explicit `registry.Add<TPayload, THandler>("<command.type>")`
  calls, one line per handler, no assembly scanning (PERFORMANCE.md §3).
- **Tests are organized by *flow*, not by handler**, because the flows are what R-B01 promises:
  `tests/Animora.Server.Tests/Flows/ClinicalFlowTests.cs` (owner → pet → appointment → check-in →
  queue → visit → triage → notes → complete → handoff exists) and
  `Flows/BoardingFlowTests.cs` (stay → items → room → checkout → handoff exists), plus
  `Handlers/ConflictPolicyTests.cs` (one fact per matrix row above) and
  `Handlers/NoFinancialEffectTests.cs`.
- **New error codes:** `AGGREGATE_NOT_FOUND`, `SLOT_TAKEN`, `STAY_CLOSED`, `VISIT_ALREADY_COMPLETED`,
  `QUEUE_STATE_INVALID`, `VACCINATION_DUPLICATE` — added **only if** the owning C-phase did not
  already define them; the phase file's implementation step 1 is to check `ErrorCodes` first and
  reuse (`ROADMAP.md` §3.3 — one definition per identifier).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Contracts.Wire/Payloads/Clinical/*.cs` | create | ~20 payload records |
| `src/Animora.Contracts.Wire/Payloads/Scheduling/*.cs` | create | ~10 payload records |
| `src/Animora.Application/Ports/IServerOutbox.cs` | create | outbox port + `NullServerOutbox` default |
| `src/Animora.Server/Handlers/Clinical/*.cs` | create | 20 handlers (patients, reception, visits, vaccination) |
| `src/Animora.Server/Handlers/Scheduling/*.cs` | create | 10 handlers (appointments, boarding) |
| `src/Animora.Server/Handlers/Policies/DemographicMergePolicy.cs` | create | field-aware merge |
| `src/Animora.Server/Handlers/Policies/SlotAvailabilityPolicy.cs` | create | authoritative slot check |
| `src/Animora.Server/Handlers/ClinicalHandlerRegistration.cs` | create | explicit registry lines |
| `src/Animora.Server/Handlers/SchedulingHandlerRegistration.cs` | create | explicit registry lines |
| `src/Animora.Server/Commands/CommandPermissions.cs` | modify | one entry per new command type |
| `src/Animora.Server/Commands/SettlementResultFactory.cs` | modify | `ResponsibilityMap` rows for the new codes |
| `src/Animora.Server/Program.cs` | modify | call both registrations |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | add only the genuinely-new codes |
| `tests/Animora.Application.Tests/PayloadContractTests.cs` | create | client command ↔ wire payload round-trip |
| `tests/Animora.Server.Tests/Flows/ClinicalFlowTests.cs` | create | the full clinical chain server-side |
| `tests/Animora.Server.Tests/Flows/BoardingFlowTests.cs` | create | the boarding chain server-side |
| `tests/Animora.Server.Tests/Handlers/ConflictPolicyTests.cs` | create | one fact per policy-matrix row |
| `tests/Animora.Server.Tests/Handlers/NoFinancialEffectTests.cs` | create | every D06 handler returns `None` |

## Implementation steps
1. Read `ErrorCodes` and `CommandTypes`; list which codes already exist. Add **only** missing ones.
2. Write the wire payload records for all 30 command types.
3. Add `IServerOutbox` + `NullServerOutbox` and register the null default.
4. Write the two policy classes.
5. Write the 30 handlers against the documented skeleton, each calling its domain method.
6. Write both registration files; extend `CommandPermissions` and `ResponsibilityMap`.
7. Write `PayloadContractTests` first among the tests — it is the one that catches shape drift.
8. Write the two flow tests and the two handler test classes.
9. Verify against a real SQL Server: the clinical chain settles end to end, each aggregate's
   `rowversion` advances, `VisitBillingHandoff`/`BoardingBillingHandoff` rows appear, and the
   idempotency row for every command is `Settled`/`Accepted`.
10. Re-run D05's `CommandPermissionMapTests` — it must still be green with the 30 new types.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Wire: ~30 `<Name>PayloadV1` payload records under `Payloads/Clinical/` and `Payloads/Scheduling/`.
  `WireContract.Version` stays `1`.
- Application: `IServerOutbox` + `NullServerOutbox`.
- Server: 30 `IServerCommandHandler<TPayload>` implementations, `DemographicMergePolicy`,
  `SlotAvailabilityPolicy`, two registration files; `CommandPermissions` and `ResponsibilityMap`
  extended.
- `ErrorCodes` += up to 6 (only those not already defined by C02–C05).
- **No migration** — D02's `102_ServerCoreSchema` already mapped every entity these handlers touch.
- No `Animora.Contracts` change, no new `CommandTypes` constant, no DI change in `App.axaml.cs`.

## Rules that apply here
- **A-RULE 004 / R-B17** — `rowversion` only **detects**; every outcome is decided by the domain
  method or the named policy class. No `ClientWins`/`ServerWins` resolver appears anywhere.
- **A-RULE 003** — not applicable positively here (nothing financial), and enforced negatively:
  `NoFinancialEffectTests` proves no D06 handler claims a financial effect.
- **A-RULE 002** — medical history and visit notes are append-only revisions; no in-place mutation.
- **A-RULE 014** — handlers return `ServerCommandEffect` only; no server write model or projection
  crosses the wire.
- **A-RULE 018** — the billing handoff rows are write-model facts, not denormalized read models.
- **`ARCHITECTURE.md` §8.5** — the conflict-policy matrix rows are implemented literally, each with
  its own test fact.
- **R-B01** — the clinical → billing seam produces the same handoff rows the client produces, so the
  cross-feature flow is identical online and offline.

## Out of scope
- Invoices, payments, cash sessions, cheques, stock movements, ledger → D07.
- The real outbox table and relay worker → D09 (`IServerOutbox` stays a no-op here).
- Replication of the resulting rows to clients → D08.
- Client-side submission of these commands → E06.
- Any new domain invariant — the domain is complete from A04/A05; this phase only calls it.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `PayloadContractTests` green: every client `IJournaledCommand` serializes to **exactly** its
  `<Name>PayloadV1` shape (no extra, no missing, no renamed field).
- `ClinicalFlowTests` green (or skipped with reason): owner → pet → appointment → check-in → queue
  entry → visit open → triage → note revision → rx lines → visit complete, all `Accepted`, and a
  `VisitBillingHandoff` row exists in `Pending` state.
- `BoardingFlowTests` green: stay create → 2 items → room assign → checkout → a
  `BoardingBillingHandoff` row exists; a further `stay.item.add` returns `Rejected` + `STAY_CLOSED`.
- `ConflictPolicyTests` green, one fact per row: a demographic field changed on both sides merges
  field-wise and reports `merged:<field>`; a second note save adds a revision instead of
  overwriting; a double-booked slot is `Rejected` + `SLOT_TAKEN` naming the conflicting id; a
  duplicate vaccination is an idempotent replay; an already-admitted queue call is a no-op success;
  a stale `rowversion` yields `Conflict` + `CONCURRENCY_CONFLICT`.
- `NoFinancialEffectTests` green: all 30 handlers return `FinancialEffect.None`.
- D05's `CommandPermissionMapTests` and `SettlementContractTests` still green with the new types.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D06 done; advance "Next phase" to `D07`; append a "Repo state" entry listing: the 30 handled
`CommandTypes` (grouped as above), the wire payload namespace `Animora.Contracts.Wire.Payloads.*`
(**D07 extends it**), `PayloadContractTests` as the shape-drift guard (**E06 relies on it**),
`IServerOutbox` + `NullServerOutbox` (**D09 replaces the implementation only**),
`DemographicMergePolicy` and `SlotAvailabilityPolicy` (**E05 reuses their decisions**), the
documented handler skeleton, the `CommandPermissions`/`ResponsibilityMap` extensions, the new error
codes, the "every D06 handler is `FinancialEffect.None`" invariant, the flow-test names,
deviations.
