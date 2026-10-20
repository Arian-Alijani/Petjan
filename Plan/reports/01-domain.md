# 01 — Domain Layer Audit (`src/Animora.Domain/**`)

Scope: the entire Domain project (entities, aggregates, value objects, primitives, pure
calculators). Audit targets emphasised: #2 bugs, #3 latent, #5 cross-layer consistency, overflow
and money correctness. The Domain is pure (no I/O, no clock reads), so perf (#1/#4) findings here
are about allocation/algorithmic cost only.

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| DOM-01 | Medium | latent | `StockCalculator.WeightedAverageCost` corrupts the running average after an oversell takes quantity ≤ 0 |
| DOM-02 | Medium | bug | Fixed-amount invoice discount silently truncates a fractional `decimal` to minor units |
| DOM-03 | Low | inconsistency | `RecurringExpenseSchedule.IsDue` is dead/divergent logic vs `Evaluate` (two "is it due" definitions) |
| DOM-04 | Low | latent | `InvoiceTotals.Compute` / `ComputeDue` sum raw `long` minor units with no `checked` overflow guard |
| DOM-05 | Low | perf | `TreatmentScheduleExpander.ExpandSpecific` uses LINQ `Distinct().ToList()` against the no-LINQ convention |
| DOM-06 | Info | — | "Zero project references" invariant **verified holding**; money-as-integer, monotonic ULID, UTC clock contracts all sound |

---

## DOM-06 (baseline verification — invariant holds)

- Severity: Info
- Class: inconsistency (none found)
- Location: `src/Animora.Domain/Animora.Domain.csproj`
- Evidence: the csproj body is literally `<Project Sdk="Microsoft.NET.Sdk"></Project>` — **no
  `ProjectReference`, no `PackageReference`**. The "ZERO project refs by design" invariant holds.
- Why it matters: confirms the Domain cannot take a hidden dependency on EF/Infrastructure; any
  future `using` of a non-BCL type would fail to compile, so the invariant is self-enforcing.
- Corroborating good design (no defect, recorded so later tasks can rely on it):
  - `Money` is `readonly record struct (long Minor, string Currency)`; every arithmetic path uses
    `checked(...)` (`Add`/`Subtract`/`Negate`/`Multiply`), currency mismatch throws, and the single
    rounding boundary is `ApplyPercent` (`MidpointRounding.AwayFromZero`). No `decimal`/`float`
    money field exists. `Allocate` is largest-remainder and sums back exactly.
  - `IdGenerator` is a monotonic Crockford ULID with a per-process high-water lock; the explicit
    instant overload keeps a *separate* high-water state so a past timestamp cannot reset the
    monotonic path. `ForOperation` derives deterministic child ids from the operation id so
    offline-client and server mint identical ids (key to sync idempotency — relevant to T7).
  - `IClock.UtcNow` is the only "now" source; Domain never reads the ambient clock.
- Confidence: High.

---

## DOM-01 Weighted-average cost corrupts after an oversell drives quantity ≤ 0

- Severity: Medium
- Class: latent
- Location: `src/Animora.Domain/Inventory/StockCalculator.cs` — `WeightedAverageCost`, the
  outbound branch and the `average` recompute.
- Evidence:
  ```csharp
  else { // outbound
      runningQuantity += quantity;          // quantity < 0
      runningValue    += quantity * average; // value reduced by average
  }
  average = runningQuantity > 0m ? runningValue / runningQuantity : average;
  ```
- Why it is a problem: `StockCalculator.EnsureSufficient(..., allowNegative: true)` is an explicit
  supported clinic policy ("lets the tenant sell into negative stock … the ledger stays correct").
  When cumulative outbound exceeds inbound, `runningQuantity` becomes ≤ 0. The guard then **freezes
  `average` at its last positive value**, while `runningValue` keeps being driven *negative* by the
  outbound lines. On the next inbound (restock), `runningValue` starts from a negative base and the
  recomputed `average = runningValue / runningQuantity` is skewed low (or negative) for an
  arbitrary number of subsequent movements until the ledger value climbs back above zero.
- Scenario / impact: a clinic that sells into negative stock (allowed policy) and later restocks
  gets an understated — possibly negative — moving-average cost on the affected variant. That cost
  flows into COGS postings (`PostingRules.ForStockMovementRecorded`) and `OwnerFinancialSnapshot`,
  so margins and inventory valuation silently drift. Correct-now (nobody oversells today), breaks
  later when oversell + restock happens. Time horizon: first oversell→restock cycle per variant.
- Confidence: High (pure function, deterministic; a unit test with [+10 @100, −12, +5 @100] shows
  the skew).

## DOM-02 Fixed-amount invoice discount truncates a fractional decimal

- Severity: Medium
- Class: bug / inconsistency
- Location: `src/Animora.Domain/Sales/InvoiceTotals.cs` — `Compute`.
- Evidence:
  ```csharp
  var invoiceDiscount = discountKind == DiscountKind.Percent
      ? netAfterLineDiscount.ApplyPercent(discountValue)
      : new Money((long)discountValue, currency);   // <-- cast, not rounded
  ```
- Why it is a problem: the percent branch rounds away-from-zero through the product's single
  rounding boundary; the fixed-amount branch instead does a C# `(long)` cast, which **truncates
  toward zero**. A caller that passes `discountValue = 1500.9` (or any non-integer decimal reaching
  this method) loses the `.9` with no rounding and no validation. The two discount kinds therefore
  round by different rules, violating "nothing else may round money".
- Scenario / impact: any UI/handler that forwards a fractional fixed discount produces a GrandTotal
  one minor unit low vs. the rounded expectation; because `GrandTotal` is recomputed identically on
  client and server the two agree, but the posted ledger disagrees with what the operator typed.
  Low blast radius today (callers likely pass whole minor units) but it is an undocumented silent
  truncation on the money path.
- Confidence: Medium (depends on whether any caller passes a non-integer fixed amount — needs the
  Application/Contracts cross-check in T3/T4; flagged here as the Domain-level root).

## DOM-03 Two divergent "is it due" definitions for recurring expenses

- Severity: Low
- Class: inconsistency
- Location: `src/Animora.Domain/Finance/RecurringExpense.cs` — `RecurringExpenseSchedule.IsDue` vs
  `RecurringExpenseSchedule.Evaluate`.
- Evidence:
  ```csharp
  public static bool IsDue(int dayOfMonth, int currentDayOfMonth, bool alreadyPaidThisPeriod) =>
      currentDayOfMonth >= dayOfMonth && !alreadyPaidThisPeriod;         // ignores daysInMonth clamp
  // vs Evaluate(...) which uses EffectiveDueDay(dayOfMonth, daysInMonth) and distinguishes Due/Overdue
  ```
- Why it is a problem: `IsDue` compares against the raw `dayOfMonth`, while `Evaluate` compares
  against `EffectiveDueDay` (clamped to the month length). For a template with `DayOfMonth = 31` in
  a 30-day (or 29/30-day Jalali) month, `Evaluate` treats day 30 as `Due` but `IsDue` returns
  `false` (30 ≥ 31 is false). Two public helpers answer the same question differently; whichever the
  Application layer happens to call changes the reminder/overdue behaviour at month-end.
- Scenario / impact: month-end recurring costs (rent/salary set to day 31) are mis-flagged for one
  day each short month if any caller uses `IsDue`. Latent-style: only triggers on short months and
  only for day 29/30/31 templates.
- Confidence: Medium (impact depends on which helper the App layer uses — resolve in T10).

## DOM-04 Totals sum raw `long` minor units without `checked`

- Severity: Low
- Class: latent
- Location: `src/Animora.Domain/Sales/InvoiceTotals.cs` — `Compute` (`grossMinor += gross;
  lineDiscountMinor += discount;`), `ComputeDue` (`adjustedMinor += adjustments[i].Amount.Minor;`),
  and `AllocateByWeight` (`totalWeight += weights[i];`).
- Evidence: these accumulators are plain `long +=`, not `checked`, unlike `Money`'s own operators
  which are all `checked(...)`. The intermediate `(decimal)amount.Minor * weights[i]` in
  `AllocateByWeight` is fine, but the `long` sums are not overflow-guarded.
- Why it is a problem: `Money` deliberately uses `checked` everywhere to turn an overflow into a
  thrown defect rather than a silently wrapped negative. The totals algorithm — the one place that
  sums *many* line amounts — drops that guarantee. For IRT (scale 0) the per-row magnitudes are
  large (whole toman), so a pathological invoice with very many high-value lines could wrap `long`
  silently and produce a negative subtotal instead of throwing.
- Scenario / impact: not reachable with realistic invoice sizes today (would need ~9.2e18 total
  toman), hence Low; but it is an inconsistency with the module's own overflow policy and would
  manifest as a wrong-sign total rather than a clean failure. Cheapest fix: wrap the accumulators in
  `checked` to match `Money`.
- Confidence: High (code fact), Low exploitability.

## DOM-05 `Distinct().ToList()` in the schedule expander violates the no-LINQ hot-path rule

- Severity: Low
- Class: perf / inconsistency
- Location: `src/Animora.Domain/Clinical/TreatmentScheduleExpander.cs` — `ExpandSpecific`.
- Evidence:
  ```csharp
  return Result<List<DateOnly>>.Ok(s.Days.Distinct().ToList());
  ```
- Why it is a problem: `StockCalculator` documents the project convention "walked with indexed
  `for` loops and no LINQ (PERFORMANCE.md §2)". `ExpandSpecific` breaks it with `Distinct().ToList()`
  (allocates an enumerator + a HashSet + a list). Bounded (`Days.Count ≤ MaxOccurrences*2 = 730`) so
  the cost is tiny — this is a consistency/style nit, not a runtime risk. The surrounding
  `ExpandEvery`/`ExpandWeekly` correctly use indexed loops.
- Scenario / impact: negligible runtime; recorded only because the audit weights perf heavily and
  the convention is explicit. Also note `days.Sort()` after expansion is fine (bounded ≤ 730).
- Confidence: High.

---

### Notes carried forward
- `Cheque` state machine, `AccountingPeriod` (strictly-forward, no-reopen), `PostingRules`
  (pure event→journal, balance guaranteed by `JournalEntry.Create`) were inspected and show no
  Domain-level defect; their *application* correctness (period gate flags, double-posting) is the
  subject of T10 (sales-accounting).
- `TreatmentScheduleExpander` is correctly bounded (`MaxOccurrences`, `MaxSpanDays`, past/future
  windows) — a good example of a latent-growth guard already in place.
