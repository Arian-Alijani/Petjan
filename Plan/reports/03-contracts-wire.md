# 03 — Contracts + Wire Consistency Audit

Scope: `src/Animora.Contracts/**` (Desktop↔services seam) + `src/Animora.Contracts.Wire/**`
(server↔client transport). Audit targets emphasised: #5 cross-layer and #6 server↔client
inconsistency (wire DTOs, command/error codes, versioning, pagination, time).

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| CW-01 | High | inconsistency (server↔client) | Wire-v2 `ExpectedServerVersion` precondition is validated server-side but **never populated by any client code** — optimistic concurrency is dormant |
| CW-02 | Medium | inconsistency (server↔client) | Wire-v2 `RuleSetVersion` is evaluated server-side (`CommandIntakeService`) but **never set by the client** — rule-set gating is dormant |
| CW-03 | Medium | inconsistency | `CommandTypes` live count is **94** (and `CommandTypes.All` holds 94); the design baseline/docs say "86" — docs are stale against the single source of truth |
| CW-04 | Low | inconsistency | Error `Category` is dropped on the wire: domain `ErrorCode(Code, Category)` → wire `Code` string only; the client re-derives category from a local catalog, which can drift |
| CW-05 | Low | latent | `CommandEnvelope.CreatedAtUtc` is explicitly "informational only / never ordering"; some journaled-command builders omit the v2 fields, so the envelope silently defaults to the lowest-precondition form |
| CW-06 | Info | — | Both csproj files are reference-free (Contracts/Wire carry no impl types — invariant holds); `WireContract.Version=2`, `SupportedContractVersions=[1,2]`, `SyncPage.PageHash`/`PageOrdinal` pagination + integrity all present and sound |

---

## CW-06 (baseline verification — contract hygiene holds)

- Severity: Info
- Location: `Animora.Contracts.csproj`, `Animora.Contracts.Wire.csproj`, `WireContract.cs`,
  `Sync/SyncPage.cs`, `Sync/SyncRecord.cs`, `Errors/WireApiError.cs`.
- Evidence: both csproj bodies are empty `<Project Sdk="Microsoft.NET.Sdk"></Project>` — **no
  reference to any `Animora.*` implementation type**. `WireContract` exposes an explicit
  `Version = 2`, `IsSupported(1|2)`, a `426`-style `X-Animora-Supported-Versions` header and a
  per-request `X-Animora-Contract-Version` header — a real versioning handshake, not an afterthought.
  `SyncPage(BatchId, PageOrdinal, PageCount, Records, PageHash)` carries ordering + an integrity
  hash; `WireApiError(Code, Detail, Fields, CorrelationId)` is the single wire error shape;
  `SettlementResult` carries the full failure-UX contract (`Outcome`, `ErrorCode`, `FinancialEffect`,
  `ResponsibleRole`) so "a bare failure is impossible by shape".
- Why it matters: confirms the seam discipline is intact; the findings below are specific dormant
  fields and doc drift, not structural rot.
- Confidence: High.

---

## CW-01 Wire-v2 `ExpectedServerVersion` validated by the server but never sent by the client

- Severity: High
- Class: inconsistency (server↔client), latent-correctness
- Location: `src/Animora.Contracts.Wire/Commands/CommandEnvelope.cs` (field +
  `Create(... expectedServerVersion = null ...)`); server consumer
  `src/Animora.Server/Commands/CommandEnvelopeValidator.cs:24`
  (`|| version.ToString("D") != envelope.ExpectedServerVersion`); **no client producer**.
- Evidence: a repository-wide grep for a *non-null* writer of `ExpectedServerVersion` returns only
  the server validator. The two client envelope builders —
  `CommandJournalBehavior` (`CommandEnvelope.Create(... expectedAggregateVersion ...)`) and
  `LocalCommandProcessor.ToEnvelope` — call the overload **without** `expectedServerVersion`, so it
  defaults to `null`. The envelope doc itself states: *"Absent ⇒ no precondition."*
- Why it is a problem: wire v2 introduced `ExpectedServerVersion` (canonical UUID the client last
  observed) precisely so that *"when present and different from the server's current aggregate
  version the command settles `Conflict` before any handler runs."* Because the client never
  populates it, this precondition is permanently inert; conflict detection falls back to the legacy
  numeric `ExpectedAggregateVersion` hint, which the same envelope doc marks as *"Detection only …
  the server ignores it as concurrency evidence."* Net effect: the modern optimistic-concurrency
  path is wired on the server but dead on the client, so two devices editing the same aggregate rely
  entirely on the server's post-hoc conflict policy instead of a cheap pre-handler short-circuit.
- Scenario / impact: concurrent edits from two devices of one tenant do not get the intended
  pre-handler `Conflict`; the command runs the handler and conflicts are resolved later (more work,
  wider race window). Correctness is not lost (server policy still converges per A-RULE 004) but the
  v2 guarantee is unmet. This is the single most consequential consistency gap in this layer and is
  re-examined in the sync matrix (T7).
- Confidence: High (grep is decisive; the client simply never writes the field).

## CW-02 Wire-v2 `RuleSetVersion` evaluated by the server but never set by the client

- Severity: Medium
- Class: inconsistency (server↔client)
- Location: `CommandEnvelope.RuleSetVersion` (default `null`); server consumer
  `src/Animora.Server/Commands/CommandIntakeService.cs:112`
  (`Result<int> ruleVersion = rules.Evaluate(envelope.RuleSetVersion);`); no client producer.
- Evidence: grep for `RuleSetVersion` shows exactly one non-doc hit — the server intake evaluation.
  The client builders never set it, so `rules.Evaluate(null)` always runs the "current rule-set"
  branch.
- Why it is a problem: `RuleSetVersion` (A-RULE 037) lets the server know which business rule-set a
  command was authored under so it can reject/adapt commands authored on an outdated rule-set. With
  the client never stamping it, an offline client that authored a command under an older rule-set
  cannot be detected; the server treats every command as current-rule-set. The capability is
  half-built.
- Scenario / impact: after a server-side rule-set bump, queued offline commands authored under the
  prior rules are applied as if current — a potential silent business-rule mismatch rather than the
  intended controlled rejection/migration. Lower severity than CW-01 because rule-set bumps are rare.
- Confidence: High.

## CW-03 `CommandTypes` count is 94, not the documented 86

- Severity: Medium
- Class: inconsistency (doc vs single source of truth)
- Location: `src/Animora.Contracts.Wire/Commands/CommandTypes.cs`.
- Evidence: the file declares **94** `public const string` command names and
  `public static IReadOnlyList<string> All { get; } = [...]` enumerates **94** entries (verified
  equal — the `All` list is complete, no const is missing from it). The audit/design baseline and
  several docs state "86 (39 non-financial + 47 money/stock)".
- Why it is a problem: the baseline the auditor was handed is stale; any doc table of command types
  (and any consumer that hard-codes "86") disagrees with the live catalog by 8 types — consistent
  with the new families noted in ground truth (`vaccination.treatment.*` ×4, `finance.recurring.*`
  ×4 = +8). The code is internally consistent (consts == `All`), so the risk is doc trust, not a
  runtime gap. Recorded so later tasks do not re-assert "86".
- Scenario / impact: no runtime defect; prevents a false "missing command types" finding elsewhere
  and flags every "86" doc for correction.
- Confidence: High.

## CW-04 Error `Category` is not carried on the wire

- Severity: Low
- Class: inconsistency
- Location: domain `src/Animora.Domain/Results/ErrorCode.cs` (`record struct ErrorCode(string Code,
  string Category)`) vs wire `Errors/WireApiError.cs` (`Code` string only) and
  `Commands/SettlementResult.cs` (`string? ErrorCode`).
- Evidence: the domain error is a `(Code, Category)` pair (Validation / NotFound / Conflict /
  Forbidden / Integrity / Infrastructure / Entitlement). The wire shapes carry only the `Code`
  string; `Category` is dropped. The client must re-derive the category from its own local
  `ErrorCodes` catalog to decide UX (retryable? forbidden? entitlement?).
- Why it is a problem: category is re-derived from a parallel client-side catalog. If the server
  introduces a new code (or recategorises one) and the client's catalog is not updated in lockstep,
  the client classifies the failure wrongly (e.g. treats a `Conflict` as `Infrastructure` and
  retries, or a `Forbidden` as retryable). The 161-code catalog is duplicated knowledge across the
  boundary with only `Code` transmitted.
- Scenario / impact: minor today (both sides ship from one repo), but a genuine drift surface:
  a server-only new error code arrives at an older client with no category, forcing a default
  classification. Cheap mitigation: carry `Category` on `WireApiError`/`SettlementResult`.
- Confidence: High (shape fact); Medium on real-world drift (depends on release coupling).

## CW-05 Journaled-command envelope builders omit the v2 precondition fields

- Severity: Low
- Class: latent / consistency (tie-in to CW-01)
- Location: `src/Animora.Application/Behaviors/CommandJournalBehavior.cs` and
  `src/Animora.Application/Commands/LocalCommandProcessor.cs` — both call `CommandEnvelope.Create`
  through the overload that stops at `expectedAggregateVersion`, leaving `expectedServerVersion` and
  `ruleSetVersion` at their defaults.
- Evidence: `CommandEnvelope.Create(... c.ExpectedAggregateVersion)` in `ToEnvelope` — the last two
  optional parameters are not supplied.
- Why it is a problem: this is the mechanical root of CW-01/CW-02 on the client side — even if a
  caller wanted to set `ExpectedServerVersion`, the journaling path discards it because it never
  threads it through. Recorded separately because fixing CW-01/CW-02 requires changing these two
  builders (and `PendingCommandRecord`) to carry and forward the observed server version.
- Scenario / impact: see CW-01/CW-02. No independent impact.
- Confidence: High.

---

### Notes carried forward
- CW-01/CW-02/CW-05 feed directly into the **T7 sync** conflict/ordering matrix (two-device, server
  -authoritative settlement vs local provisional) and the **T8 server-auth** read of
  `CommandEnvelopeValidator` / `CommandIntakeService`.
- `SettlementOutcome` (Accepted/Rejected/Conflict/Deferred) maps cleanly to the domain
  `SettlementState` (Settled/Rejected/Conflict/Reversed/NeedsAttention) consumed by
  `LocalCommandProcessor` — note the wire enum has no `Reversed`/`NeedsAttention`; those are
  local-only lifecycle states, which is correct (server never *sends* them), but worth confirming in
  T7 that a server-side reversal is delivered as a separate settlement, not an outcome value.
