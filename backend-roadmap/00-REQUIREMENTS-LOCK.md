# Requirements Lock — Animora Backend & Server

> Locked requirement list. Every phase file's `Satisfies:` line references IDs from this table, and
> every ID below is satisfied by at least one phase (coverage verified in §3).
> Sources: the product requirement list (`R-B01`–`R-B12`), the binding invariants of
> `ARCHITECTURE.md` / `STACK.md` / `PERFORMANCE.md` (`R-B13`–`R-B35`), and the commercial-SaaS
> shape lock (`R-B36`).

## §1 Product requirements

| ID | Requirement | Source | Phases that satisfy it |
|---|---|---|---|
| R-B01 | Every existing screen works against real data, and cross-feature flows are wired: appointment → reception → visit → invoice → payment → cashier → ledger → history; inventory movements from sales/purchases; vaccination schedules; boarding → invoice; cheques → finance; dashboards from real aggregates. | product §1 | C01–C11, D06, D07 |
| R-B02 | Offline-first desktop: full functionality with the server unreachable, on a real local database. | product §2 | A09, A10, A11, B02, C01–C11 |
| R-B03 | Multi-user / multi-device online sync with no divergence across several accounts, and correct convergence after a long offline period (including a full-resync path). | product §3 | D02, D08, E03, E04, E05, E06 |
| R-B04 | Real login plus roles (manager/owner, doctor, receptionist, cashier, accountant — extensible) with real role/permission management and per-role screen/action gating. | product §4 | A03, B03, B04, D03, D04 |
| R-B05 | Server-side subscription management (create/extend/reduce/suspend/cancel, renewal, grace, expiry behavior) plus an operator admin website on the same server. | product §5 | D10, E01, E02 |
| R-B06 | Anti-crack / anti-piracy: signed license, installation binding, tamper and clock-rollback resistance, fail-closed entitlement. | product §6 | D11, E07 |
| R-B07 | Weak-server efficiency: many clients on ~2–4 cores / 4 GB, with measured capacity. | product §7 | D09, D13, E08 |
| R-B08 | Weak-client efficiency: fast startup, low RAM, virtualized lists, no UI-thread blocking. | product §7 | A10, B01, C09, C11, E08 |
| R-B09 | Crash resilience: no data loss on kill/crash/power loss; resumable workers; recoverable local store. | product §8 | A11, B02, B05, D09, E06 |
| R-B10 | Clean code: correct layering, naming and folder placement; no logic in the wrong project; no mixing of client and server code. | product §9 | A01 (enforced by `LayeringRulesTests`), A02, C11, E08 |
| R-B11 | Future-ready seams (structure now, features later, no rework): a web clinic client, and an operator panel able to later show per-tenant stock and per-store charts. | product §10 | D02, D08, E02 |
| R-B12 | The user can build, run and test the app himself throughout Tracks A–C. | product §11 | A01–A11, B01–B05, C01–C11 (CONVENTIONS §11) |
| R-B36 | Commercial SaaS shape: the vendor runs one central multi-tenant server; a customer self-registers and buys a subscription on the public website, then downloads + installs the desktop app and logs in. The website is a separate vendor-side system that hands an async, idempotent, authorized **provisioning intent** to the Subscription context (sharing the operator's create-tenant+subscription operation), never a runtime dependency and never a payment processor inside this solution. Clinics never host a server. | product (this session) · ARCH §3, §13.4, §17 | D10 (intake), D11 (license issued on provisioning), E07 (customer login/activation) |

## §2 Binding invariants

| ID | Requirement | Source | Phases that satisfy it |
|---|---|---|---|
| R-B13 | Two permanently separate pipelines: generic record replication vs. business command settlement. Replication ordering never becomes financial ordering. | ARCH §8.1–8.4 | B02, D05, D08, E03 |
| R-B14 | Idempotency on `(TenantId, OperationId)`: identical retry returns the stored result; same OperationId with a different `CommandFingerprint` is an audited integrity error; at most one financial effect per command. | ARCH §8.3, A-RULE 005/006/019 | B02, D05 |
| R-B15 | Idempotency and ordering are separate concerns; dependency is explicit (`CausationId`/`DependsOnOperationId` + `ExpectedAggregateVersion`) and resolved server-side only. | ARCH §8.4, A-RULE 021/022 | B02, D05, E06 |
| R-B16 | Explicit settlement state machine surfaced in the UI, plus the sync-failure UX contract (what failed / why / financial effect / who must act). | ARCH §8.3, A-RULE 024 | B02, C07, C08, E06 |
| R-B17 | Conflict policy: version mismatch detects, domain policy decides; generic LWW only for UI prefs; posted invoice immutable; no blind client/server-wins on financial facts. | ARCH §8.5, A-RULE 002/003/004 | A07, A08, D06, D07, E05 |
| R-B18 | No destructive sync: an inbound pull never overwrites unsent local business intent. | ARCH §8.2, A-RULE 017 | E05 |
| R-B19 | Tenancy: shared DB / shared schema / discriminator with three mandatory isolation layers that hold with database hardening OFF; client-supplied `TenantId` never trusted. | ARCH §9, A-RULE 012/013 | A03, D02, D04, E08 |
| R-B20 | Authentication ≠ Authorization ≠ Entitlement — checked independently, in order. | ARCH §10.1, A-RULE 023 | B04, D03, D04, E07 |
| R-B21 | Offline session = signed, device-bound capability snapshot with per-capability expiry, failing closed at expiry; a revoked device loses offline capability at next contact. | ARCH §10.4, A-RULE 029 | A03, B04, E07 |
| R-B22 | Append-only balanced double-entry ledger; posted accounting records immutable; corrections are compensating entries. | ARCH §11.1, A-RULE 002 | A08, C09, D07 |
| R-B23 | Movement-based inventory; on-hand is derived, never an absolute stored overwrite. | ARCH §11.2 | A06, C06, D07 |
| R-B24 | Money is integer minor units + currency code; all stored time is UTC (`DateTimeOffset` only via converter); Jalali/Persian digits only in presentation. | STACK §3.3, ARCH §11.1 | A02, A09 |
| R-B25 | Transactional Outbox with an atomic claim state machine, idempotent consumers, retry/circuit-breaker/timeout/bulkhead, jittered reconnect. | ARCH §12, A-RULE 025/026 | B02, D09 |
| R-B26 | File lifecycle `Staged → Uploaded → HashVerified → Committed → Available` with a mandatory reconciliation job and Valet-Key access (a path is never a credential). | ARCH §14, A-RULE 020 | D12 |
| R-B27 | Zero-Internet: no mandatory external service in any runtime path; observability is local-only, with zero external telemetry. | ARCH §17.4, A-RULE 016/032 | A11, D13, E09 |
| R-B28 | Local store hardening: encryption provider seam keyed from the OS keystore, startup assertion `sqlite_version() >= 3.50.2`, WAL + busy timeout, backup/restore and corrupt-DB quarantine/rebuild. | STACK §3.3, §12 K2, T-RULE 004 | A11 |
| R-B29 | Append-only audit of every security and financial action, with correlation ids (TenantId/OperationId/DeviceId/RequestId) flowing end to end. | ARCH §10, §15 | B05, D05, D11 |
| R-B30 | Subscription (commercial) and Licensing (offline entitlement) are architecturally separate mechanisms; the runtime depends only on Licensing. | ARCH §13.1 | D10, D11 |
| R-B31 | Explicit wire contract version per replicated/commanded contract, with unsupported-version rejection and deterministic mixed-fleet behavior across N/N-1/N-2. | ARCH §8.6, §18 | A01, D08, E03, E09 |
| R-B32 | Validation gates executed and recorded: `STACK.md` §12 (G1, K2, native-SQLite assertion, RLS decision) and `ARCHITECTURE.md` §19 gates A–V. | STACK §12, ARCH §19 | E08 |
| R-B33 | Release engineering: deployment topology, install/upgrade/rollback runbook, server backup/restore with a rehearsed recovery, desktop packaging, SBOM + native binary inventory. | ARCH §17, STACK §11 | E09 |
| R-B34 | No mock left behind: every `Animora.Contracts` interface has a real implementation and the DI container references nothing under `Animora.Desktop/Mocks`. | product §1, CONVENTIONS §10 | C11 |
| R-B35 | Reporting/aggregation is isolated from the OLTP budget; every cache is bounded and tenant-safe (key = tenant + user + resource + version). | A-RULE 033/034/035 | C09, D13, E02 |

## §3 Coverage guarantee

**Guarantee:** every requirement `R-B01`–`R-B36` is referenced by at least one phase file's
`Satisfies:` line, and every ID referenced by a phase file exists in this table.

Verification at authoring time:

| Check | Result |
|---|---|
| IDs defined | 36 (`R-B01`–`R-B36`) |
| IDs with ≥ 1 owning phase | 36 / 36 |
| IDs with no owning phase | none |
| Phase `Satisfies:` IDs not defined here | none |
| Requirements owned by exactly one phase (single points of failure) | R-B18 (E05), R-B26 (D12), R-B28 (A11), R-B32 (E08), R-B33 (E09), R-B34 (C11) — each is a single cohesive deliverable by design. R-B36's intake is owned by D10; D11/E07 carry its downstream legs. |

Re-run this check whenever a phase file is edited: the phase's `Satisfies:` list and this table's
"Phases that satisfy it" column must stay mutually consistent.
