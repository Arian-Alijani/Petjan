# Plan — Remediate Audit Findings (Reports 01–06)

> **Purpose of this file.** This is the executable task list to *fix* the defects catalogued in
> `Plan/reports/01-domain.md` … `06-sync-system.md`. The reports are **findings only**; this file is
> the **fix plan**. It is written to be fully self-contained: a model opening it in a brand-new
> session can execute each task with *minimal* re-investigation — exact file paths, line anchors,
> current code, target code shape, and acceptance checks are inlined. Do **not** re-audit; the reports
> are already verified faithful to live code (cross-checked 2026-10-20).

## Repo ground truth (read once, rely on it)

- Solution: `Animora.sln`. Target framework **.NET 10**, C#. Only `src/` + `tests/` are in scope.
  `Animora-UI/` and `db-sample/` are **never touched**.
- Money is an integer type: `Money = readonly record struct (long Minor, string Currency)` in
  `src/Animora.Domain/` with `checked(...)` arithmetic and a single rounding boundary
  (`ApplyPercent`, `MidpointRounding.AwayFromZero`). **Never introduce `decimal`/`double` money.**
- Time discipline: `IClock.UtcNow` is the ONLY "now". Domain/Application must never read
  `DateTime.Now/UtcNow/Today` or `DateTimeOffset.UtcNow`. Clinic day = clinic TZ (Asia/Tehran) via
  `IClinicTimeZoneProvider`.
- Perf convention (PERFORMANCE.md §2): no LINQ on hot/write paths; walk with indexed `for` loops.
- `CommandTypes` live count = **94** (not 86). `ErrorCodes` = 161. These are the single source of truth.

## BUILD / TEST POLICY (locked)

- **No full-solution build, no full test run, no long test suites.** (Explicit user constraint.)
- Validate changes by: (a) targeted re-reading of the edited file, (b) at most a single-project
  `dotnet build src/<Project>/<Project>.csproj` **only if quick and necessary** to confirm a
  non-trivial edit compiles — skip it for small/obvious edits.
- If a fix turns out **heavy/large** (big refactor, new persistence table, migration, new service),
  push immediately with a clear conventional-commit message (locked rule below), don't batch it.

## PUSH POLICY (locked — violating = project death)

After completing **each task below**, run from repo root:

```bash
scripts/git/push.sh "type(scope): message"
```

It does: `add -A` → `commit` → `fetch` + `rebase --autostash` → `push -u`, prints one `✓`/`✗`.
- Exit `3` = no credentials → call the `setup_github_environment` tool once, then re-run.
- Exit `2` = rebase conflict → resolve (prefer remote), re-run.
- Work branch: **`genspark_ai_developer`** (current). One commit per task; squash only if trivially noisy.

## Severity-ordered execution order

Do High → Medium → Low. Within a layer, keep edits atomic. Suggested order (dependencies noted):
High: APP-01, CW-01(+CW-05), SQL-01, SQL-02, INF-01, SYN-01, SYN-02.
Medium: DOM-01, DOM-02, CW-02, CW-03, SQL-03, INF-02, INF-03, APP-02, SYN-03, SYN-04, SYN-05.
Low/Info: DOM-03, DOM-04, DOM-05, APP-03, APP-04, APP-05, CW-04, SQL-04, SQL-05, INF-04, INF-05, SYN-06, doc-count fixes.

---

# Report 01 — Domain (`src/Animora.Domain/**`)

## [x] T01 · DOM-01 — Fix weighted-average cost corruption after oversell (Medium, latent)
- **File:** `src/Animora.Domain/Inventory/StockCalculator.cs`, method `WeightedAverageCost` (~lines 66–106).
- **Current code (the defect):**
  ```csharp
  var runningQuantity = 0m; var runningValue = 0m; var average = 0m;
  // inbound:  runningQuantity += quantity; runningValue += quantity * movement.UnitCost.Minor;
  // outbound: runningQuantity += quantity;              // quantity < 0
  //           runningValue    += quantity * average;    // reduce by running average
  average = runningQuantity > 0m ? runningValue / runningQuantity : average; // freezes avg when qty<=0
  var rounded = Math.Round(average, 0, MidpointRounding.AwayFromZero);
  ```
- **Problem:** when oversell drives `runningQuantity <= 0`, `average` freezes at last positive while
  `runningValue` keeps going negative; next restock recomputes a skewed/negative average.
- **Fix shape (choose minimal correct):** when `runningQuantity <= 0m`, **reset the ledger baseline**
  so stale negative value cannot skew the next inbound: set `runningValue = runningQuantity * average`
  is already the invariant; the real fix is — when `runningQuantity <= 0m` set `runningValue = 0m` **and**
  keep `average` as-is (so a fresh restock starts a clean average from the new inbound only). Concretely:
  ```csharp
  average = runningQuantity > 0m ? runningValue / runningQuantity : average;
  if (runningQuantity <= 0m) runningValue = 0m; // drop stale negative value; next inbound re-seeds avg
  ```
  Keep integer-money discipline (final `Math.Round(... AwayFromZero)` unchanged). Do NOT change the
  inbound branch.
- **Acceptance:** replay `[+10 @100, -12, +5 @100]` → after restock the average must be `100` (not
  negative/understated). Verify by reasoning through the loop; no new LINQ, keep `for`/indexed style.

## [x] T02 · DOM-02 — Round fixed-amount discount instead of truncating (Medium, bug)
- **File:** `src/Animora.Domain/Sales/InvoiceTotals.cs`, method `Compute`.
- **Current code:**
  ```csharp
  var invoiceDiscount = discountKind == DiscountKind.Percent
      ? netAfterLineDiscount.ApplyPercent(discountValue)
      : new Money((long)discountValue, currency);   // truncates toward zero
  ```
- **Fix:** replace the truncating cast with away-from-zero rounding to match the money rounding policy:
  ```csharp
  : new Money((long)Math.Round(discountValue, MidpointRounding.AwayFromZero), currency);
  ```
  (`discountValue` is the fixed-amount decimal.) Keep currency arg. No other rounding introduced.
- **Acceptance:** `discountValue = 1500.9m` → discount Minor `1501`, not `1500`. Whole-number inputs
  unchanged.

## [x] T03 · DOM-04 — Guard total accumulators with `checked` (Low, latent)
- **File:** `src/Animora.Domain/Sales/InvoiceTotals.cs` — methods `Compute`, `ComputeDue`, `AllocateByWeight`.
- **Current:** plain `long +=` accumulators: `grossMinor += gross; lineDiscountMinor += discount;`
  (Compute), `adjustedMinor += adjustments[i].Amount.Minor;` (ComputeDue), `totalWeight += weights[i];`
  (AllocateByWeight).
- **Fix:** wrap each `long`/accumulator sum in `checked(...)` to match `Money`'s own checked policy, e.g.
  `grossMinor = checked(grossMinor + gross);`. The `(decimal)amount.Minor * weights[i]` stays as-is.
- **Acceptance:** overflow now throws `OverflowException` instead of silently wrapping; normal sums
  unchanged. Keep indexed loops.

## [x] T04 · DOM-03 — Reconcile the two "is it due" definitions (Low, inconsistency)
- **File:** `src/Animora.Domain/Finance/RecurringExpense.cs` — `RecurringExpenseSchedule.IsDue` vs `Evaluate`.
- **Problem:** `IsDue(dayOfMonth, currentDayOfMonth, alreadyPaidThisPeriod)` compares against raw
  `dayOfMonth`; `Evaluate` uses `EffectiveDueDay(dayOfMonth, daysInMonth)` (clamped to month length).
  For `DayOfMonth=31` in a 30-day month they disagree.
- **Decision (locked after analysis):** `Evaluate` is the correct, richer definition (clamps + Due/Overdue).
  **Make `IsDue` delegate to the clamped logic** so both agree. Add a `daysInMonth` parameter OR, if
  changing the signature is undesirable for callers, re-implement `IsDue` to clamp internally. First
  **grep callers**: `grep -rn "\.IsDue(" src/ tests/`. If zero non-test callers → simplest fix: make
  `IsDue` call `EffectiveDueDay`-based comparison (requires `daysInMonth`; add the param and update the
  (likely test-only) callers). If callers exist and can't take a new param → have `IsDue` internally
  derive via `Evaluate` returning not-NotDue. Pick the lower-churn path and document the choice in the
  commit message.
- **Acceptance:** `DayOfMonth=31`, 30-day month, day 30, not paid → `IsDue` now returns `true`
  (matches `Evaluate`'s `Due`).

## [x] T05 · DOM-05 — Remove LINQ `Distinct().ToList()` from the expander (Low, perf/convention)
- **File:** `src/Animora.Domain/Clinical/TreatmentScheduleExpander.cs` — `ExpandSpecific`.
- **Current:** `return Result<List<DateOnly>>.Ok(s.Days.Distinct().ToList());`
- **Fix:** replace with an indexed-loop dedup (sort then skip equal neighbours, or a manual pass into a
  `List<DateOnly>` checking the last appended). `s.Days.Count` is bounded ≤ 730, so a simple:
  ```csharp
  var days = new List<DateOnly>(s.Days.Count);
  for (int i = 0; i < s.Days.Count; i++) { var d = s.Days[i]; if (!days.Contains(d)) days.Add(d); }
  days.Sort();
  return Result<List<DateOnly>>.Ok(days);
  ```
  (If `s.Days` ordering must be preserved, drop the `Sort()`; the report notes a later `days.Sort()` is
  acceptable. Keep behaviour: dedup + the existing sort semantics.) No `using System.Linq` reliance here.
- **Acceptance:** duplicates removed, result sorted as before, no LINQ call remains in the method.

> **DOM-06** is Info/verification only — **no task**.

---

# Report 02 — Application (`src/Animora.Application/**`)

## [x] T06 · APP-01 — Extend replay-guard to non-Unit journaled commands (High, idempotency bug)
- **File:** `src/Animora.Application/Behaviors/CommandJournalBehavior.cs` (replay short-circuit ~lines 40–46).
- **Current code:**
  ```csharp
  var previous = await journal.FindByOperationIdAsync(commandContext.OperationId, ct).ConfigureAwait(false);
  if (previous.Value is { } stored && stored.CommandFingerprint == fingerprint
      && typeof(TResult) == typeof(Unit))                 // guard limited to Unit
      return Result<TResult>.Ok((TResult)(object)Unit.Value);
  ```
- **Problem:** 42 of 43 `IJournaledCommand` return `ICommand<string>` (new aggregate id); only `Unit`
  ones are replay-protected. Non-Unit journaled commands re-enter the handler on replay → risk of
  double ledger posting (`finance.expense.*`: `CreateExpenseCommand`, `ReverseExpenseCommand` in
  `UseCases/Finance/ExpenseCommands.cs`, both post via `PostingRules.ForExpensePosted`).
- **Root cause / why the guard is Unit-only:** there is no durable stored *response* for non-Unit
  results, so the success value can't be reconstructed on replay.
- **Fix plan (durable response, minimal, two steps):**
  1. **Inspect the journal record shape** to see if a response/result field already exists:
     `grep -rn "CommandFingerprint\|ResultJson\|ResponseJson\|StoredResult" src/Animora.Application src/Animora.Persistence.Sqlite | head`.
     Look at the `PendingCommandRecord` / journal entity and `ICommandJournal`.
  2. **If a result-payload column exists (or is cheap to add):** on first successful execution, persist
     the serialized `TResult` alongside the settlement; on replay with matching fingerprint, deserialize
     and return it for ANY `TResult` (not just `Unit`). For `string`-returning creates this returns the
     same aggregate id. **If adding a column requires a SQLite migration**, that is a *heavy* change —
     add migration `034_JournalResultPayload` (follow the `0NN__Name` double-underscore convention used
     by 020/022/024–033; physical head is `033_OnlineEntitlement`), push immediately per the heavy-fix
     rule, keep nullable column so existing rows are unaffected.
  3. **If a durable response is genuinely out of scope for this pass:** apply the *safe narrower* fix —
     still short-circuit non-Unit replays by returning the **deterministically-derived create id**
     (`IdGenerator.ForOperation(operationId, slot 0)`) when the stored command is a create-style
     `IJournaledCommand` returning `string` and the handler's id is operation-derived. This prevents the
     handler re-running. Prefer step 2; only fall back to step 3 if migration is undesirable — and
     **document the decision in the commit**.
- **Acceptance:** replaying a settled `CreateExpenseCommand` (same OperationId+fingerprint) returns the
  stored result WITHOUT re-entering the handler (no second `PostingRules.ForExpensePosted`). `Unit`
  path unchanged.
- **Cross-ref:** validate the expense handler's own guard in T10 report context (not required to land this).
- **Heavy-fix note:** if this grows into a migration + journal schema change, push separately with
  `fix(application): persist journaled-command result for safe non-Unit replay`.

## [x] T07 · APP-02 — Remove ambient-clock fallback in LocalCommandProcessor (Medium, latent)
- **File:** `src/Animora.Application/Commands/LocalCommandProcessor.cs` — `DependencyRejected` (~line 139)
  and the ctor `LocalCommandProcessor(ICommandJournal, ICommandTransport, IClock? clock = null)`.
- **Current:** `SettledAtUtc: clock?.UtcNow ?? DateTimeOffset.UtcNow,` — the only ambient-clock read in
  the layer, when `clock` is null.
- **Fix:** make `IClock` **required** (non-nullable ctor param, remove `= null` default) and use
  `clock.UtcNow` directly. Then **check DI registration** so nothing constructs it without a clock:
  `grep -rn "new LocalCommandProcessor\|AddScoped<.*LocalCommandProcessor\|AddSingleton<.*LocalCommandProcessor" src/ tests/`.
  Update any registration / test that passed no clock to pass the injected `IClock`. If a test needs a
  clock, use the existing fake/`TimeProvider`-backed clock already in the test project.
- **Acceptance:** no `DateTimeOffset.UtcNow`/`DateTime.*` remains in this file (`grep -n "DateTime" src/Animora.Application/Commands/LocalCommandProcessor.cs` → empty); compiles; registrations updated.

## [x] T08 · APP-03 — Align retry exponent clamp with MaxAttempts (Low, dead-config)
- **File:** `src/Animora.Application/Commands/RetryPolicy.cs` — `NextDelay(int attempts)`.
- **Current:** `var exponent = Math.Clamp(attempts, 1, 20);` while `MaxAttempts = 10`. Clamp upper bound
  (20) is unreachable → misleading.
- **Fix:** change the clamp upper bound to `MaxAttempts` (reference the constant, don't hardcode) so the
  two agree: `var exponent = Math.Clamp(attempts, 1, MaxAttempts);`. Behaviour unchanged for attempts
  1–10 (the `Cap` already dominates); this just removes the dead/misleading `20`.
- **Acceptance:** no magic `20`; exponent bound == `MaxAttempts`; cap logic (`>= Cap.TotalSeconds ? Cap`) untouched.

## [ ] T09 · APP-04 — Replace `LastOrDefault` with reverse indexed loop (Low, perf)
- **File:** `src/Animora.Application/Behaviors/CommandJournalBehavior.cs` — `PrerequisiteAsync` (~line 92).
- **Current:** `var post = unsettled.Value!.LastOrDefault(c => c.CommandType == CommandTypes.SalesInvoicePost);`
- **Fix:** reverse `for` loop over the list to find the last match without enumerator/closure alloc:
  ```csharp
  var list = unsettled.Value!;
  PendingCommandRecord? post = null; // use the actual element type from the list
  for (int i = list.Count - 1; i >= 0; i--)
      if (list[i].CommandType == CommandTypes.SalesInvoicePost) { post = list[i]; break; }
  ```
  (Confirm the element type via the surrounding code / `unsettled.Value` declaration.)
- **Acceptance:** same selection semantics (last matching); no LINQ; compiles.

## [ ] T10 · APP-05 — Fix stale "Stage N" doc comments (Low, doc inconsistency)
- **Files:** behavior XML-doc summaries vs real order in `src/Animora.Application/Dispatch/Dispatcher.cs`
  (`Chain`/`ServerChain`). Real client chain (outer→inner):
  `Correlation → ExceptionMapping → Validation → Authorization → Entitlement → Transaction → CommandJournal → Audit → Handler`.
- **Fix:** correct the `/// <summary>Stage N …</summary>` numbers in each behavior to match position:
  Correlation=1, ExceptionMapping=2 (fix: currently says "Stage 6 outermost catch"), Validation=3,
  Authorization=4, Entitlement=5, Transaction=6, CommandJournal=7, Audit=8. (Report listed 2/3/4/5 as
  already consistent for Validation/Auth/Entitlement/Transaction — re-read each file's summary and set the
  number to its actual index; the authoritative order is the `Chain` array.) **Comment-only edits.**
- **Acceptance:** each behavior's documented stage == its index in `Chain`; no duplicate "Stage 6"; no
  code/behaviour change.

> **APP-06** is Info/verification only — **no task**.

---

# Report 03 — Contracts + Wire (`src/Animora.Contracts/**`, `src/Animora.Contracts.Wire/**`)

> CW-01, CW-02, CW-05 are the same client-side mechanical root: the client never threads the v2
> precondition fields through the envelope builders. Do them together (one task T11) because fixing
> CW-01/CW-02 **requires** the CW-05 builder change. CW-03/CW-04 are independent.

## [ ] T11 · CW-01 + CW-02 + CW-05 — Populate wire-v2 precondition fields from the client (High)
- **Server side already consumes them (do NOT change server logic, just confirm):**
  - `src/Animora.Server/Commands/CommandEnvelopeValidator.cs:24` validates `ExpectedServerVersion`
    (`version.ToString("D") != envelope.ExpectedServerVersion`).
  - `src/Animora.Server/Commands/CommandIntakeService.cs:112` evaluates `rules.Evaluate(envelope.RuleSetVersion)`.
- **Wire type (already has the fields + overload):** `src/Animora.Contracts.Wire/Commands/CommandEnvelope.cs`
  — record params `string? ExpectedServerVersion = null` (line 49), `string? RuleSetVersion = null` (line 50);
  `Create(...)` overload takes `expectedServerVersion` + `ruleSetVersion` (lines 72–76).
- **Client builders that currently omit them (the defect):**
  - `src/Animora.Application/Behaviors/CommandJournalBehavior.cs` — `CommandEnvelope.Create(... c.ExpectedAggregateVersion)`.
  - `src/Animora.Application/Commands/LocalCommandProcessor.cs` — `ToEnvelope` (same truncated Create call).
- **Fix plan:**
  1. **Thread an observed server version through `PendingCommandRecord`.** Find the record:
     `grep -rln "record PendingCommandRecord\|class PendingCommandRecord" src/`. Add a nullable
     `string? ExpectedServerVersion` (and optionally `string? RuleSetVersion`) field if not present.
     Where the pending command is created (the journaling write path), populate `ExpectedServerVersion`
     with the canonical aggregate version the client last observed. **Source of the observed version:**
     the create/update command carries `ExpectedAggregateVersion` today; the *canonical UUID* version is
     the server-authoritative one delivered via state-sync replica metadata — locate it:
     `grep -rn "CanonicalVersion\|ServerVersion\|RowVersion\|version" src/Animora.Persistence.Sqlite/Configurations | head`
     and the replica materializer. If a canonical per-aggregate server version is **already stored
     locally** (e.g. on the replicated entity), read it there; if it is **not stored locally**, that is a
     larger change — in that case implement CW-02 (`RuleSetVersion`, which the client *does* know from its
     active rule-set config) fully, and for CW-01 thread the field end-to-end but leave the value `null`
     until a canonical-version source exists, documenting the limitation in the commit. Prefer a real
     value if the canonical version is retrievable.
  2. **Forward the fields in both builders:** change both `CommandEnvelope.Create(...)` calls to pass
     `expectedServerVersion: record.ExpectedServerVersion` and `ruleSetVersion: <active rule-set version>`.
  3. **RuleSetVersion source:** find the client's active rule-set/business-rule version:
     `grep -rn "RuleSet\|ruleSetVersion\|A-RULE 037\|rule.set" src/Animora.Application src/Animora.Client.Services | head`.
     Stamp it as an integer string.
- **Acceptance:** `grep -rn "expectedServerVersion:" src/Animora.Application` shows both builders pass it;
  envelopes produced now carry a non-null `RuleSetVersion` (and `ExpectedServerVersion` when a canonical
  version is available). Server validators unchanged. Document in commit which of CW-01 value-population
  was fully vs partially achieved.
- **Heavy-fix note:** if this needs a new locally-stored canonical version column (migration), push
  separately with `feat(sync): thread wire-v2 preconditions (ExpectedServerVersion/RuleSetVersion)`.

## [ ] T12 · CW-03 — Fix stale "86 CommandTypes" docs to 94 (Medium, doc inconsistency)
- **Source of truth:** `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` declares **94** consts and
  `All` enumerates 94. (+8 vs old 86 = `vaccination.treatment.*` ×4 + `finance.recurring.*` ×4.)
- **Fix:** find and correct every doc that claims 86:
  `grep -rn "86" ARCHITECTURE.md STACK.md AGENTS.md backend-roadmap docs Plan/reports/00-INDEX.md | grep -i "command\|type\|86 "`.
  Update the count to **94** and, where the "(39 non-financial + 47 money/stock)" breakdown appears, note
  the +8 new-family types. **Docs only** — do not touch `CommandTypes.cs`.
- **Acceptance:** no doc asserts "86 command types"; `00-INDEX.md` line (~43) corrected if it still says 86.
- **Note:** `Plan/reports/00-INDEX.md` is a report file — correcting a factual count in it is fine (it is
  not the final-deletion file). Do NOT modify the 01–06 report *findings* content.

## [ ] T13 · CW-04 — Carry error `Category` on the wire (Low, inconsistency)
- **Files:** domain `src/Animora.Domain/Results/ErrorCode.cs` (`record struct ErrorCode(string Code, string Category)`);
  wire `src/Animora.Contracts.Wire/Errors/WireApiError.cs` (currently `Code` only) and
  `src/Animora.Contracts.Wire/Commands/SettlementResult.cs` (`string? ErrorCode`).
- **Fix:** add an optional `string? Category = null` to `WireApiError` (and optionally `SettlementResult`),
  then populate it at the **server** mapping site where domain `ErrorCode` → wire error is built
  (`grep -rn "new WireApiError(" src/Animora.Server | head`) by passing `errorCode.Category`. On the
  **client**, prefer the wire `Category` when present, falling back to the local catalog derivation when
  null (backward-compat with older servers). Keep the field optional so v1/older peers still deserialize.
- **Acceptance:** `WireApiError` has `Category`; server populates it; client uses it when non-null. No
  breaking change to existing JSON (additive, nullable).
- **Heavy-fix note:** if touching many server mapping sites, push separately.

> **CW-06** is Info/verification only — **no task**.

---

# Report 04 — Local Persistence SQLite (`src/Animora.Persistence.Sqlite/**`)

## [ ] T14 · SQL-01 — Make encrypted local store the shipped default (High, isolation)
- **Files:** `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` (provider
  selection, lines ~50–66), `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs`
  (`EncryptionEnabled`, `K2Approved` — both default `false`),
  `Context/MultipleCiphersEncryptionProvider.cs`, `Context/NoEncryptionProvider.cs`.
- **Current:** `store is { EncryptionEnabled: true, K2Approved: true } ? MultipleCiphers : NoEncryption`
  → ships **plaintext** unless both flags set. PII/financial data at rest unencrypted by default.
- **Fix (two parts; the composition root is the decisive one):**
  1. **Verify the Desktop composition root** (this is the real blast radius; report said confirm in T6/T12):
     `grep -rn "EncryptionEnabled\|K2Approved\|AddLocalStore\|LocalStoreOptions" src/Animora.Desktop | head`.
     If the Desktop does NOT set both flags → the app ships unencrypted. **Set them there** (Desktop DI /
     appsettings / options wiring) to `EncryptionEnabled = true`, `K2Approved = true` for release builds.
  2. **Guard against silent plaintext:** do NOT flip the library default blindly if the cipher bundle
     (E08 native pin) may be absent in some build configs — instead, make the selection **fail-closed and
     explicit**: if `EncryptionEnabled` is requested but `K2Approved`/cipher bundle is unavailable, throw a
     clear startup error (the file already notes "Fail before any open"), rather than silently downgrading
     to `NoEncryptionProvider`. Confirm the cipher bundle availability path before enabling by default.
  3. If enabling by default is blocked by the native-bundle pin not being present in the sandbox, at
     minimum (a) wire the Desktop root to enable it, and (b) add a loud warning log + diagnostic when
     `NoEncryptionProvider` is selected in a non-dev configuration.
- **Acceptance:** Desktop release composition enables encryption (both flags true); selecting
  `NoEncryptionProvider` in release is either impossible or emits a prominent warning; requesting
  encryption without the bundle fails fast, never silently plaintext. Document exactly what was changed
  and any remaining native-bundle precondition in the commit.
- **Heavy-fix note:** composition-root + options change; push as `fix(persistence): enable encrypted local store by default (SQL-01)`.

## [ ] T15 · SQL-02 — Add row retention/compaction for append-only growth tables (High, latent growth)
- **Files:** `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` (`Prune()` ~line 612 only
  trims **backup files**; Rebuild* methods never delete rows), `Activity/ActivityFeedWriter.cs`,
  `Activity/SyncedActivityFeed.cs`. Append-only audit guard: `Context/AppendOnlyGuard.cs` (audit must stay
  append-only — do NOT delete audit rows arbitrarily).
- **Unbounded tables:** `ActivityEvent`, `OutboxMessage` (even after dispatch), `AuditEntry`,
  `SyncCursor`/replica history.
- **Fix plan (retention policy, age + row-count caps):**
  1. Add a new maintenance method, e.g. `CompactAsync(RetentionPolicy policy, CancellationToken)` on
     `LocalStoreMaintenance`, that deletes:
     - **Dispatched** `OutboxMessage` rows older than N days (only rows in a terminal/Completed state —
       confirm the status column via the OutboxMessage configuration/entity).
     - `ActivityEvent` older than the activity retention window (age or keep-last-N per aggregate).
     - `SyncCursor`/replica history superseded rows (keep current checkpoint only).
     - **AuditEntry:** per compliance, do NOT hard-delete. Instead archive/roll (e.g. export to a rolled
       file then delete, OR leave out of this pass if archiving is out of scope) — respect `AppendOnlyGuard`.
       If audit archiving is heavy, scope it out and note it; implement outbox+activity+cursor retention now.
  2. Add retention knobs to `LocalStoreOptions` (e.g. `OutboxRetentionDays`, `ActivityRetentionDays`,
     with sane defaults) — mirror the existing `BackupRetentionCount` style.
  3. Wrap deletes in a transaction + batch (`DELETE ... LIMIT`/chunked) to avoid a huge single statement;
     after large deletes, the existing `VacuumAsync` can reclaim space (note: VACUUM only helps *after*
     rows are actually deleted).
  4. Wire `CompactAsync` into wherever periodic maintenance is scheduled (`grep -rn "VacuumAsync\|BackupAsync\|LocalStoreMaintenance" src/Animora.Desktop src/Animora.Client.Services | head`).
- **Acceptance:** dispatched outbox + old activity + superseded cursor rows are deleted by the new
  retention pass; audit handled per compliance (archived or explicitly deferred); options expose the
  windows; indexed/batched deletes (no unbounded single statement).
- **Heavy-fix note:** new maintenance method + options + wiring is a sizable change — push as
  `feat(persistence): row retention/compaction for append-only tables (SQL-02)`.

## [ ] T16 · SQL-03 — Key SQLCipher via keyspec, not interpolated PRAGMA literal (Medium, security robustness)
- **File:** `src/Animora.Persistence.Sqlite/Context/MultipleCiphersEncryptionProvider.cs` — `Apply`.
- **Current:** `Execute(connection, $"PRAGMA key = '{passphrase.Replace("'", "''", ...)}';");` — key
  built into `CommandText` (leak surface via SQL trace/log hooks; relies solely on quote-doubling).
- **Fix:** prefer passing the key via the connection string / `SqliteConnectionStringBuilder`
  (`Password=`) or a `PRAGMA key = x'<hex>'` **raw-key** form that does not place the plaintext passphrase
  into an ad-hoc `CommandText`. Check which SQLCipher binding is referenced
  (`grep -rn "SQLitePCL\|SqlCipher\|MultipleCiphers\|Password=" src/Animora.Persistence.Sqlite src/Animora.Infrastructure.Local | head`)
  and use its supported keyspec API. If the binding genuinely only supports `PRAGMA key`, at minimum
  ensure no SQL logging interceptor is attached to the keying connection and the key string is cleared
  after use. Keep behaviour identical (DB opens with the same key).
- **Acceptance:** plaintext key no longer appears in an interpolated `CommandText`, OR (if unavoidable)
  keying connection is provably excluded from any SQL trace/log. Encryption still opens the DB.
- **Depends on:** reachable only when encryption is enabled (SQL-01). Do after/with T14.

## [ ] T17 · SQL-04 — Set store-wide NoTracking default + fix the 3 non-opted queries (Low, perf)
- **File:** `src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs` (no `QueryTrackingBehavior`
  set today). Query files: `src/Animora.Persistence.Sqlite/Queries/**` (24 of 27 use `AsNoTracking`; 3 don't).
- **Fix:**
  1. In `OnConfiguring`/options wiring set
     `optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);` (or set
     `ChangeTracker.QueryTrackingBehavior = NoTracking` in the ctor).
  2. **Find any write-read path that relied on tracking** and add explicit `.AsTracking()` there. Identify
     the 3 non-opted query files for confirmation they are read-only:
     `grep -rL "AsNoTracking" src/Animora.Persistence.Sqlite/Queries` (lists files NOT containing it) —
     confirm each is read-only; with the global default they become NoTracking automatically (safe).
  3. Repositories that load-then-save entities must use `.AsTracking()` explicitly — audit repos:
     `grep -rn "AsTracking\|Update(\|SaveChanges" src/Animora.Persistence.Sqlite/Repositories | head`.
- **Acceptance:** DbContext defaults to NoTracking; write paths explicitly `AsTracking()`; the 3 outlier
  queries confirmed read-only. No behavioural change to writes.

## [ ] T18 · SQL-05 — Cache the pragma array in the connection interceptor (Low, micro-perf)
- **File:** `src/Animora.Persistence.Sqlite/Context/SqliteConnectionInterceptor.cs` — `Pragmas()` builds a
  fresh `string[]` (+ interpolated busy-timeout) on every `ConnectionOpened`.
- **Fix:** build the array **once** in the singleton (busy-timeout depends only on startup-fixed options),
  store in a `private readonly string[] _pragmas` set in the ctor, and have `ConnectionOpened` iterate it.
- **Acceptance:** no per-open array/string allocation; pragmas applied identically. Quick build check optional.

> **SQL-06** is Info/verification only — **no task**.

---

# Report 05 — Infrastructure.Local + Client.Services (`src/Animora.Infrastructure.Local/**`, `src/Animora.Client.Services/**` excl. `Sync/`)

## [ ] T19 · INF-01 — Use IClock + clinic TZ for business-"today" queries (High, time/culture)
- **Files (3 services, 4 call sites):**
  - `src/Animora.Client.Services/Reception/ReceptionQueueService.cs:19` — `DateOnly.FromDateTime(DateTime.Today)`
  - `src/Animora.Client.Services/Visits/VisitsTriageService.cs:26,77` — `DateOnly.FromDateTime(DateTime.Today)`
  - `src/Animora.Client.Services/Vaccination/VaccinationService.cs:17,127` — `DateOnly.FromDateTime(DateTime.UtcNow)`
- **Problem:** machine wall clock / UTC date instead of clinic-TZ (Asia/Tehran, UTC+03:30) Jalali day;
  "today" is wrong for the last ~3.5h of each local day and not test-injectable.
- **Fix:** inject `IClock` and `IClinicTimeZoneProvider` into each service (constructor), and compute the
  clinic day as: `clock.UtcNow` → convert to clinic TZ → `DateOnly.FromDateTime(local.DateTime)`.
  - Find the exact helper: `grep -rn "IClinicTimeZoneProvider\|GetClinicTimeZone\|ClinicToday\|ToClinicTime" src/Animora.Infrastructure.Local src/Animora.Client.Services src/Animora.Contracts | head`.
    There is likely an existing extension to get the clinic-local now; **reuse it** rather than rolling a
    new conversion. If there's a `ClinicToday()`/`GetClinicTimeZoneAsync()` helper, use it.
  - For `VaccinationService` (currently sync + `DateTime.UtcNow`), use the async TZ path if the method is
    async; if sync, use the provider's sync `GetClinicTimeZone()` (note INF-05 — see T23).
- **DI:** confirm these services resolve from a scope that has `IClock`/`IClinicTimeZoneProvider` registered
  (they are registered in Infrastructure.Local — `grep -rn "IClock\|IClinicTimeZoneProvider" src/Animora.Infrastructure.Local/*ServiceCollection*`).
- **Acceptance:** `grep -n "DateTime.Today\|DateTime.UtcNow" src/Animora.Client.Services/Reception/ReceptionQueueService.cs src/Animora.Client.Services/Visits/VisitsTriageService.cs src/Animora.Client.Services/Vaccination/VaccinationService.cs` → empty. Clinic day derived via clock+TZ; injectable for tests.
- **Heavy-fix note:** 3 services + DI wiring — if large, push as `fix(clientservices): derive clinic day via IClock+clinic TZ (INF-01)`.

## [ ] T20 · INF-02 — Enforce tenant/user keying in TenantScopedCache (Medium, isolation)
- **File:** `src/Animora.Client.Services/Caching/TenantScopedCache.cs` (bare LRU; invariant "key MUST
  include tenant+user" is doc-only, unenforced).
- **Fix (pick the enforcement that fits current call sites — census first):**
  `grep -rn "TenantScopedCache\|new TenantScopedCache\|\.TryGet(\|\.Set(" src/Animora.Client.Services | head -40`.
  Preferred: change the public API so a key **cannot** be formed without tenant+user — e.g. make
  `TryGet`/`Set`/`Invalidate` take `(tenantId, userId, TKey subKey)` and internally compose the composite
  key, instead of accepting a raw `TKey`. Update all call sites to pass tenant+user (both are available in
  the per-call scope/context the services already resolve). If API change is too broad, add a required
  `ITenantContext`/scope accessor to the cache ctor and **prefix every key** with
  `tenant:{id}:user:{id}:` inside the cache.
- **Acceptance:** it is impossible to store/fetch a cache entry without a tenant+user component; call sites
  updated; cross-user bleed structurally prevented. Note any call site that previously keyed on a bare id.
- **Heavy-fix note:** API change rippling to call sites — push as `fix(clientservices): enforce tenant/user cache keying (INF-02)`.

## [ ] T21 · INF-03 — DebounceBuffer overflow should flush, not throw (Medium, latent)
- **File:** `src/Animora.Client.Services/Visits/DebounceBuffer.cs`, `Enqueue`:
  `if (_pending.Count >= 32) throw new InvalidOperationException("Debounce buffer is full; ...");`
- **Fix:** on reaching the cap, **trigger a flush** of pending notes instead of throwing into the UI edit
  path. Inspect how `Flush`/drain is invoked (there is a 600ms debounce + a flush delegate/callback).
  Replace the throw with: invoke the flush path synchronously/await it (draining `_pending`), then enqueue
  the new note. If a flush callback isn't directly available inside `Enqueue`, apply back-pressure by
  evicting+flushing the oldest entry. The cap of 32 can remain as a *soft* threshold that forces a flush.
- **Acceptance:** adding a 33rd distinct `(EncounterId, VisitNoteField)` no longer throws; pending notes
  are flushed and the new note is accepted. No `throw new InvalidOperationException(...full...)` remains.

## [ ] T22 · INF-04 — FinanceProjectionWorker: park/dead-letter unknown types, don't drop (Low, financial correctness)
- **File:** `src/Animora.Infrastructure.Local/Workers/FinanceProjectionWorker.cs` — `Deserialize` → `LogAndDrop`
  (`_ => LogAndDrop(m)` currently logs a warning then returns null → message `Completed` (acked) → lost).
- **Fix:** instead of acking/completing an unregistered-type message, move it to a **dead-letter / parked**
  state so it is not permanently lost and can be replayed after the projection is registered. Check the
  outbox message status model: `grep -rn "CompleteAsync\|DeadLetter\|Parked\|Abandon\|status\|Attempt" src/Animora.Persistence.Sqlite/Configurations/*Outbox* src/Animora.Infrastructure.Local/Workers/FinanceProjectionWorker.cs | head`.
  If a dead-letter/parked status exists, transition to it (keep the warning log). If none exists, the
  minimal safe fix is to **leave the message unacked** (do not call `CompleteAsync`) so it stays pending
  (but then guard against a hot retry loop — e.g. increment an attempt counter and park after N). Prefer a
  real parked state if the schema supports it.
- **Acceptance:** an unregistered financial event is no longer silently `Completed`; it is parked/dead-
  lettered (recoverable) with the existing warning retained; no infinite tight retry loop.
- **Heavy-fix note:** if it needs a new outbox status + migration, push separately.

## [ ] T23 · INF-05 — Avoid sync DB work on caller/UI thread in clock/TZ providers (Low, perf)
- **Files:** `src/Animora.Infrastructure.Local/Time/ClinicTimeZoneProvider.cs` (`GetClinicTimeZone()` sync
  path opens scope+DbContext + EF query on cache miss), `Time/MonotonicClock.cs` (`EnsureLoaded`/`Persist`).
- **Fix:** ensure the async hydrate wins so the sync `GetClinicTimeZone()` never blocks on SQLite from the
  UI thread. Options: (a) eagerly hydrate at startup (call `GetClinicTimeZoneAsync()` during composition
  before first UI navigation); (b) cache a sane default (Asia/Tehran) until the async hydrate completes so
  the sync path returns the default instead of running a blocking query on first use. Keep correctness:
  once hydrated, use the stored value. Confirm the hydrate entry point: `grep -rn "GetClinicTimeZoneAsync\|EnsureLoaded\|Hydrate" src/Animora.Infrastructure.Local/Time`.
- **Acceptance:** first-use sync call does not run a blocking EF query on the UI thread (returns cached/
  default, or the app pre-hydrates at startup). Steady-state behaviour unchanged.

> **INF-06** is Info/verification only — **no task**.

---

# Report 06 — Sync System (`src/Animora.Sync.Custom/**`, server Sync/Commands, reconciler)

> Architecture reminder (confirmed): two strictly separated pipelines. **State sync = pull-only**
> (manifest→batch→verify pages→atomic commit; no push). **Writes = command outbox** (envelope→server
> intake→settlement) reconciled by `SettlementReconciler`. Money/stock state is **intentionally
> pull-excluded** (`SyncExclusions`: Invoice/Payment/CashSession/JournalEntry/Cheque/Expense/
> RecurringExpense(+Payment)/StockMovement/PurchaseOrder/GoodsReceipt). Do not try to add money to the
> pull feed — the correct fix for SYN-02 is a settlement→business-state reconciliation pass.

## [ ] T24 · SYN-01 — Persist the provisional→server id map (High, sync design)
- **File:** `src/Animora.Sync.Custom/CommandIdRemapService.cs` —
  `private readonly ConcurrentDictionary<(string TenantId, string LocalId), string> _map = new();`
  (in-memory only; lost on restart → dependent commands ship dead provisional ids after a creator settles
  then app restarts before dependents submit).
- **Fix plan:** add durable backing behind the existing seam (the doc already says "persistence can be
  added behind it without touching callers"):
  1. Add a small SQLite-backed store for the mapping: table `CommandIdRemap(TenantId, LocalId, ServerId,
     CreatedAtUtc)` keyed by `(TenantId, LocalId)`. Follow the migration convention `034__...` (double
     underscore, next after physical head `033_OnlineEntitlement`) — **this is a heavy change (migration);
     push separately**. Add an `ICommandIdRemapStore` port in Contracts/Application and the SQLite impl in
     `Persistence.Sqlite`.
  2. Change `CommandIdRemapService` to **write-through**: `Register(local, server)` writes DB + memory;
     `Resolve(local)` reads memory, falls back to DB on miss (and re-populates memory). Keep the
     `ConcurrentDictionary` as an L1 cache.
  3. Retention: these mappings can be pruned once all dependents are settled (tie into SQL-02 retention /
     or prune on successful dependent settlement) — but correctness first; prune later.
- **Acceptance:** after `Register`, killing+restarting the process still resolves `local→server` via DB;
  dependent commands remap correctly. Callers unchanged (seam preserved).
- **Heavy-fix note:** migration + new port + impl — push as `fix(sync): persist provisional→server id remap across restart (SYN-01)`.

## [ ] T25 · SYN-02 — Reconcile settled money/stock back into local business state (High, sync/inconsistency)
- **Files:** `src/Animora.Sync.Custom/SettlementReconciler.cs` (`ReconcileOneAsync` →
  `journal.ApplySettlementAsync(settlement)` updates only the **command record**, not the invoice/payment/
  stock the UI reads); `src/Animora.Persistence.PostgreSql/Replication/SyncExclusions.cs` (money excluded
  from pull — by design, keep it).
- **Problem:** local provisional invoice/payment/stock rows stay provisional forever; a second device never
  sees the first device's settled money via state sync; originator never replaces provisional figures with
  server-authoritative ones. (Diagnostic D04/D05/D11.)
- **Fix plan (settlement-driven business reconciliation — this is the structural core; likely heavy):**
  1. **Decide the authoritative source for the settled figures.** Since money is pull-excluded, the server
     must return the authoritative post-settlement business values **in/with the settlement response**.
     Inspect `SettlementResult` / settlement payload: `grep -rn "SettlementResult\|FinancialEffect\|settlement" src/Animora.Contracts.Wire/Commands src/Animora.Server/Commands | head`.
     `SettlementResult` already carries `FinancialEffect` — determine whether it contains enough to update
     the local invoice/payment/stock row to authoritative values. If yes → use it. If not → either
     (a) extend the settlement response with the authoritative figures, OR (b) add a narrow server endpoint
     the reconciler calls after settlement to fetch the authoritative money/stock image for that aggregate.
  2. **Apply a business-state reconciliation step** in `SettlementReconciler.ReconcileOneAsync` after the
     command-record update: when a money/stock command settles `Accepted`, update the corresponding local
     provisional business row (invoice/payment/cash/stock) to the server-authoritative values (mark it
     settled, not provisional). This must be idempotent (re-reconcile = same result) and transactional with
     the command-record settlement write.
  3. **Scope control:** this is large. If full reconciliation across all money families is too big for one
     pass, implement it for the **highest-value path first** (invoice post + payment), push, then extend to
     cash/stock/cheque/expense in follow-up commits. Each increment pushed separately.
- **Acceptance:** after a money command settles, the local business row the UI reads reflects the server-
  authoritative figures (no longer "provisional forever"); re-running reconciliation is idempotent. Two
  devices converge on money/stock without a manual full resync (at least for the implemented families).
- **Heavy-fix note (definite):** push incrementally — `fix(sync): reconcile settled money into local business state (SYN-02) — invoices+payments` etc.

## [ ] T26 · SYN-05 — Global oldest-first ordering in the reconciler (Medium, ordering)
- **File:** `src/Animora.Sync.Custom/SettlementReconciler.cs` — `ReconcileAsync`:
  `foreach (var record in paged.Items.OrderBy(r => r.LocalSequence))` only sorts **within** the 50-item
  page from `GetUnsettledPageAsync(page, 50)`; across page boundaries ordering isn't global.
- **Fix:** ensure global `LocalSequence` ordering. Preferred: make the **query** order by `LocalSequence`
  globally so pages are already globally ordered — inspect `GetUnsettledPageAsync`:
  `grep -rn "GetUnsettledPageAsync\|OrderBy.*LocalSequence\|LocalSequence" src/Animora.Persistence.Sqlite src/Animora.Application src/Animora.Sync.Custom | head`.
  If the SQL already orders by `LocalSequence` ascending then pagination is globally ordered and the fix is
  just to **remove the misleading in-memory `OrderBy`** (or keep it as a no-op safety sort) and document
  that pages are globally ordered. If the query does NOT order globally, add
  `ORDER BY LocalSequence ASC` to the unsettled-page query. Also replace the LINQ `OrderBy` with the
  query-guaranteed order (perf convention), iterating `paged.Items` directly.
- **Acceptance:** reconciliation processes unsettled commands in strictly ascending global `LocalSequence`
  across page boundaries; confirmed by reading the query's ORDER BY. Each re-submit remains idempotent.

## [ ] T27 · SYN-04 — Bound request-body buffering + handle double-401 gracefully (Medium, resilience/perf)
- **File:** `src/Animora.Sync.Custom/BearerAuthHandler.cs` — `SendAsync`:
  `byte[]? body = null; if (request.Content is not null) body = await request.Content.ReadAsByteArrayAsync(ct)…`
  (buffers the ENTIRE body on every protected call to enable one 401-retry; a double-401 surfaces the
  original 401 and aborts the in-flight batch).
- **Fix:**
  1. **Avoid unconditional full-body buffering:** only buffer when a retry is actually needed, or cap the
     buffered size. Preferred approach: attempt the request once; on 401, refresh the token, then **re-create
     the request content** for the single retry rather than pre-buffering every request. If the content is
     not re-creatable (one-shot stream), buffer only then. For large page/command uploads this removes the
     per-request LOH allocation. If a size cap is simpler, cap buffering (e.g. only buffer ≤ some KB; above
     that, don't pre-buffer and let the caller's idempotent re-submit handle it).
  2. **Double-401:** keep the single-flight refresh + one retry (correct, do not loop). The abort is
     acceptable (checkpoint is safe), but return a **clear, typed** result/exception so the orchestrator
     retries from the last checkpoint cleanly rather than surfacing a raw 401. Confirm the orchestrator's
     handling: `grep -rn "401\|Unauthorized\|ResyncRequired\|checkpoint" src/Animora.Client.Services/Sync/SyncOrchestrator.cs | head`.
- **Acceptance:** large uploads no longer always allocate a full `byte[]` copy; single-flight refresh+retry
  preserved; double-401 ends the session cleanly (checkpoint not advanced) without a raw abort. No refresh loop.
- **Heavy-fix note:** if request re-creation touches content construction broadly, push separately.

## [ ] T28 · SYN-03 — Prevent masking an earlier rejected prerequisite (Medium, UX-correctness)
- **Scope:** presentation/read-model layer — settlement status must not show "success/paid/done" when an
  earlier prerequisite in the same business operation was `Rejected` (confirms D13/D15/D09).
- **Fix plan (needs a UI/read-model trace — scope carefully):**
  1. Locate where aggregate sync/settlement status is presented for a business operation (sales post→payment):
     `grep -rn "SettlementState\|SyncStatus\|Settled\|Rejected\|badge\|status" src/Animora.Client.Services/Sync src/Animora.Desktop/Pages src/Animora.Desktop/Controls | head -40`.
  2. Introduce a **dependency-aware status rollup**: a dependent command (e.g. payment) is only presented
     as successful if its prerequisite (invoice post) settled `Accepted`. Compute the operation status as
     the worst terminal state among the operation's commands (any `Rejected` prerequisite ⇒ the operation
     is not "done"). Surface the rejected prerequisite's error to the operator.
  3. Also ensure a terminal per-command failure is not hidden behind a generic healthy sync badge — the
     badge must reflect per-operation terminal failures.
- **Acceptance:** when an invoice-post prerequisite is `Rejected`, the dependent payment/operation is NOT
  shown as successful; the operator sees the prerequisite failure. (Verify by reading the status rollup
  logic; no full UI test run required.)
- **Heavy-fix note:** if this needs a new status-rollup helper + UI binding changes, push separately. If
  the exact presentation site is ambiguous after a focused search, implement the rollup in the read-model/
  service layer (`Client.Services`) and document the UI binding point for follow-up.

## [x] T29 · SYN-06 — Align staging cap with max batch pages (Low, perf)
- **File:** `src/Animora.Sync.Custom/HttpStateSyncEngine.cs` — `MaxStagedPages = 64`; `Stage()` only keeps a
  page if `_staged.Count < MaxStagedPages`; `DownloadAndApplyAsync` allows `batch.PageCount` up to
  `_options.MaxBatchPages`. If `MaxBatchPages > 64`, tail pages are re-downloaded on every retry.
- **Fix:** make staging cover all stage-able pages: either set `MaxStagedPages = _options.MaxBatchPages`
  (so staging ≥ possible page count), or make staging size-bounded by **bytes** rather than a fixed 64
  count, or document+clamp `MaxBatchPages <= MaxStagedPages`. Simplest correct: tie `MaxStagedPages` to
  `MaxBatchPages` (so every page in a batch can be staged). Watch memory — if `MaxBatchPages` can be very
  large, prefer a byte-budget cap instead of unbounded page staging.
- **Acceptance:** for a batch with `PageCount <= MaxBatchPages`, no verified page is dropped from staging;
  retries don't re-download already-verified tail pages. No correctness change to verification.

> **SYN-00 / SYN-07** are Info/verification only — **no task** (SYN-00 just notes a missing prior-art doc;
> do not recreate it).

---

# Final task (do LAST, after all above are done + pushed)

## [ ] T30 · Delete this plan file
- After every task T01–T29 is complete and pushed, **delete only this file**:
  `rm Plan/TODO-phase-01-06-remediation.md` then `scripts/git/push.sh "chore(plan): remove completed 01-06 remediation plan"`.
- Do **NOT** touch any other file in `Plan/` (keep `Plan/reports/**`), and do **NOT** touch any roadmap
  files. This deletion is the final step only.

---

## Progress tracker (update as you go — mark [x] and push after EACH)

| Task | Finding | Sev | Status |
|---|---|---|---|
| T01 | DOM-01 | Med | [x] |
| T02 | DOM-02 | Med | [x] |
| T03 | DOM-04 | Low | [x] |
| T04 | DOM-03 | Low | [x] |
| T05 | DOM-05 | Low | [x] |
| T06 | APP-01 | High | [x] |
| T07 | APP-02 | Med | [x] |
| T08 | APP-03 | Low | [x] |
| T09 | APP-04 | Low | [ ] |
| T10 | APP-05 | Low | [ ] |
| T11 | CW-01/02/05 | High | [ ] |
| T12 | CW-03 | Med | [ ] |
| T13 | CW-04 | Low | [ ] |
| T14 | SQL-01 | High | [ ] |
| T15 | SQL-02 | High | [ ] |
| T16 | SQL-03 | Med | [ ] |
| T17 | SQL-04 | Low | [ ] |
| T18 | SQL-05 | Low | [ ] |
| T19 | INF-01 | High | [ ] |
| T20 | INF-02 | Med | [ ] |
| T21 | INF-03 | Med | [ ] |
| T22 | INF-04 | Low | [ ] |
| T23 | INF-05 | Low | [ ] |
| T24 | SYN-01 | High | [ ] |
| T25 | SYN-02 | High | [ ] |
| T26 | SYN-05 | Med | [ ] |
| T27 | SYN-04 | Med | [ ] |
| T28 | SYN-03 | Med | [ ] |
| T29 | SYN-06 | Low | [x] |
| T30 | delete this file | — | [ ] |
