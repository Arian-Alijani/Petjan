# 02 — Application Layer Audit (`src/Animora.Application/**`)

Scope: the command/query dispatch pipeline + behaviors, the outbox port, the local command
processor, ports, security plane, use cases. Audit targets emphasised: #2 bug, #3 latent, #5/#6
consistency, #7 sync-adjacent (idempotency/retries), async correctness.

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| APP-01 | High | bug | Journal replay-guard only fires for `ICommand<Unit>`; non-Unit journaled commands (incl. ledger-posting `finance.expense.*`) re-enter the handler on replay → double-posting |
| APP-02 | Medium | latent | `LocalCommandProcessor.DependencyRejected` falls back to ambient `DateTimeOffset.UtcNow` when `IClock` is null — the one ambient-clock read in the layer |
| APP-03 | Low | latent | `RetryPolicy.NextDelay` computes `Math.Pow(2, 20)` via `double`; harmless now, but the retry budget (`MaxAttempts=10`) and the exponent clamp (`20`) disagree, so the cap path is never exercised by real attempts |
| APP-04 | Low | perf | `CommandJournalBehavior.PrerequisiteAsync` uses LINQ `LastOrDefault` on the unsettled set (hot per-journaled-command path) against the no-LINQ convention |
| APP-05 | Low | inconsistency | Behavior "Stage N" doc numbers drift from the actual composition order (ExceptionMapping documented "Stage 6" but is 2nd; CommandJournal/Audit both "Stage 6") |
| APP-06 | Info | — | Pipeline order (Validation→Authorization→Entitlement→Transaction→Journal→Handler) is correct: no unvalidated/unauthorized command reaches the handler or a transaction; `RegistryValidation` fails closed at startup |

---

## APP-06 (baseline verification — pipeline + registry are sound)

- Severity: Info
- Location: `Dispatch/Dispatcher.cs` (`Chain`/`ServerChain`), `Dispatch/RegistryValidation.cs`,
  `Behaviors/TransactionBehavior.cs`, `Behaviors/AuthorizationBehavior.cs`.
- Evidence: the client chain is
  `Correlation → ExceptionMapping → Validation → Authorization → Entitlement → Transaction →
  CommandJournal → Audit → Handler`. Authorization and Entitlement are **outside** Transaction, so a
  denial short-circuits before any transaction opens or any side effect runs. `TransactionBehavior`
  fails closed (`DISPATCH_FAILED`) when no `IUnitOfWork` is registered. `RegistryValidation.Validate`
  throws at startup on duplicate handlers, abstract handlers, a required-but-missing handler, a
  kind mismatch, or an orphan validator — "a gap is a startup failure, never a runtime surprise".
- Why it matters: establishes that the structural security/transaction guarantees hold, so the
  findings below are the genuine residue, not systemic.
- Confidence: High.

---

## APP-01 Replay-guard only covers `ICommand<Unit>` — non-Unit journaled commands can re-post

- Severity: High
- Class: bug (idempotency / double-apply)
- Location: `src/Animora.Application/Behaviors/CommandJournalBehavior.cs` — the replay short-circuit.
- Evidence:
  ```csharp
  var previous = await journal.FindByOperationIdAsync(commandContext.OperationId, ct)...;
  if (previous.Value is { } stored && stored.CommandFingerprint == fingerprint
      && typeof(TResult) == typeof(Unit))          // <-- guard limited to Unit
      return Result<TResult>.Ok((TResult)(object)Unit.Value);
  ```
  The in-source comment concedes the gap: *"Unit commands have a canonical stored success outcome;
  other result shapes need an explicit durable response before they can be safely reconstructed."*
  No such durable response exists, so for any `TResult != Unit` the guard does not fire and `next(...)`
  re-enters the handler.
- Supporting fact: of the 43 `IJournaledCommand` types, **42 return `ICommand<string>`** (new
  aggregate id) and 3 return read-model types; only the `Unit` ones are protected. Concretely,
  `UseCases/Finance/ExpenseCommands.cs` declares `CreateExpenseCommand : ICommand<string>,
  IJournaledCommand` and `ReverseExpenseCommand(... ) : ICommand<string>, IJournaledCommand` — both
  **post to the ledger** (`PostingRules.ForExpensePosted`). The highest-risk money commands
  `PostInvoiceCommand`/`RegisterPaymentCommand` happen to be `ICommand<Unit>` (protected), but the
  expense family is not.
- Why it is a problem: the local processor retries and the server can re-deliver (at-least-once
  outbox / resumable journal). A second pass with the same `OperationId` and fingerprint will, for a
  `string`-returning journaled command, run the handler again. Create-style handlers that mint the
  aggregate id deterministically from the operation id (`IdGenerator.ForOperation`, slot 0) are
  self-idempotent — the second create hits a duplicate-key / already-exists path. But a handler that
  *also posts a journal entry as a side effect* (expense create/reverse) will attempt a second
  posting on replay; whether it double-posts depends on the handler's own guard, which is exactly the
  "explicit durable response" the comment says is missing.
- Scenario / impact: offline client replays a `CreateExpenseCommand` after a crash/lease expiry
  (A-RULE 026 reclaims the lease) → second ledger posting → the expense is counted twice in
  `DailyFinanceFact`/owner snapshot, or the handler throws on the duplicate and the command is
  wrongly marked failed. Tenant-visible accounting error. Time horizon: first replay of any non-Unit
  journaled command that has a posting side effect.
- Confidence: High for the guard limitation (code fact); Medium for the concrete double-post (needs
  the expense handler's internal guard confirmed — follow-up in T10).

## APP-02 Ambient-clock fallback in the command processor

- Severity: Medium
- Class: latent (time/clock discipline)
- Location: `src/Animora.Application/Commands/LocalCommandProcessor.cs:139` — `DependencyRejected`.
- Evidence:
  ```csharp
  SettledAtUtc: clock?.UtcNow ?? DateTimeOffset.UtcNow,
  ```
  and the ctor `LocalCommandProcessor(ICommandJournal, ICommandTransport, IClock? clock = null)` —
  `clock` is optional and nullable.
- Why it is a problem: `IClock` is documented as "the only source of now for Domain and Application
  code: reading the ambient system clock directly is forbidden". This is the single place in the
  layer that reads `DateTimeOffset.UtcNow` directly (verified by grep). When the processor is
  constructed without a clock, the locally-recorded `SettledAtUtc` of a dependency-rejected command
  comes from the machine clock instead of the injected (testable, skew-controlled) clock — it also
  makes that settlement timestamp non-deterministic in tests.
- Scenario / impact: minor divergence between a locally-synthesised `DEPENDENCY_REJECTED` settlement
  timestamp and the server's clock; harmless for ordering (settlement order is server-authoritative)
  but it breaks the "no ambient clock" invariant and the test-determinism guarantee. Fix: make
  `IClock` required, or default to the injected clock, never `DateTimeOffset.UtcNow`.
- Confidence: High.

## APP-03 Retry exponent clamp vs retry budget mismatch

- Severity: Low
- Class: latent / dead-config
- Location: `src/Animora.Application/Commands/RetryPolicy.cs` — `NextDelay(int attempts)`.
- Evidence:
  ```csharp
  var exponent = Math.Clamp(attempts, 1, 20); // 2^20 s already exceeds the cap
  var seconds = Math.Pow(2, exponent);
  return seconds >= Cap.TotalSeconds ? Cap : TimeSpan.FromSeconds(seconds);
  // but MaxAttempts = 10
  ```
- Why it is a problem: `MaxAttempts = 10`, so a command/outbox message is dead-lettered at attempt
  10. The delay for attempt 10 is `2^10 = 1024 s ≈ 17 min`, which already exceeds the 15-min `Cap`,
  so the cap is reached by attempt 10 — fine. But the clamp upper bound of `20` (`2^20 s`) can never
  be reached because the budget is spent at 10. The clamp is dead; worse, it signals an intended
  20-attempt budget that the code does not honour. `Math.Pow(2,20)` on `double` is exact here, so no
  overflow — but the two constants should agree to avoid future confusion.
- Scenario / impact: no runtime defect today; a maintainer raising `MaxAttempts` past 20 would hit
  the silent clamp and get a shorter-than-expected backoff. Recorded as a latent config drift.
- Confidence: High.

## APP-04 LINQ on the per-journaled-command prerequisite path

- Severity: Low
- Class: perf / convention
- Location: `src/Animora.Application/Behaviors/CommandJournalBehavior.cs` — `PrerequisiteAsync`.
- Evidence: `var post = unsettled.Value!.LastOrDefault(c => c.CommandType == CommandTypes.SalesInvoicePost);`
- Why it is a problem: this runs for every journaled command that targets an existing aggregate (the
  common follow-up case: add line, register payment, post). `LastOrDefault` allocates an enumerator
  and walks the whole unsettled set. The set is small (unsettled commands for one aggregate), so the
  cost is negligible — flagged only for the PERFORMANCE.md §2 "no LINQ on hot paths" convention and
  because it is on the write path, not a one-off.
- Scenario / impact: negligible; a reverse indexed `for` loop removes the allocation.
- Confidence: High.

## APP-05 Behavior "Stage N" documentation numbers drift from the real order

- Severity: Low
- Class: inconsistency (doc vs code)
- Location: behavior XML docs vs `Dispatch/Dispatcher.cs` `Chain`.
- Evidence: `ExceptionMappingBehavior` summary says "Stage 6 — the outermost catch" yet it is the
  **2nd** element of `Chain` (outer→inner). `CommandJournalBehavior` says "Stage 6" and
  `AuditBehavior` is also late — multiple "Stage 6" labels. `ValidationBehavior` "Stage 2",
  `AuthorizationBehavior` "Stage 3", `EntitlementBehavior` "Stage 4", `TransactionBehavior` "Stage 5"
  are consistent, but the tail numbering is wrong/duplicated.
- Why it is a problem: the *behaviour* is correct (the chain array is the source of truth); only the
  doc comments are stale. A reader trusting "Stage 6 … outermost catch" for ExceptionMapping would
  misread the composition. Pure documentation defect.
- Scenario / impact: maintainer confusion only. Recorded per audit target #5 (consistency).
- Confidence: High.

---

### Notes carried forward
- Idempotency/replay (APP-01) and dependency ordering (`PrerequisiteAsync`, `DependencyStateAsync`)
  are re-examined end-to-end against the server side in **T7 (sync)**; APP-01's concrete money impact
  is validated against the expense handler in **T10 (sales-accounting)**.
- The 86-`CommandTypes` vs registered-handler count is best verified at the registration roots
  (`*Registration.cs` + `ApplicationServiceCollectionExtensions`), which call `RegistryValidation` —
  that check already fails closed at startup, so an unregistered command type is a startup error, not
  a latent runtime gap. Cross-checked structurally; no orphan found in the registration wiring read.
