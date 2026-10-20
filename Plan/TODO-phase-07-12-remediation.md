# Plan — Remediate Audit Findings (Reports 07–12)

> **Purpose of this file.** Executable task list to *fix* the defects catalogued in
> `Plan/reports/07-server-auth.md` … `11-desktop-crosslayer.md` (12 is the synthesis roll-up, no
> findings of its own). The reports are **findings only**; this file is the **fix plan**. It is written
> to be fully self-contained: a model opening it in a brand-new session executes each task with
> *minimal* re-investigation — exact file paths, line anchors, current code, target shape and
> acceptance checks are inlined. Do **not** re-audit; the reports are verified faithful to live code
> (cross-checked 2026-10-20, anchors re-confirmed against the current tree).
>
> **Reports 01–06 are already remediated** (commits `T01`…`T30`, e.g. `d5b82ea5`…`e5448258`). This
> phase continues the exact same workflow for 07–12. The prior-phase plan was deleted as its final task
> (`7199d662`); this file follows the same lifecycle (deleted by the last task here).

## Repo ground truth (read once, rely on it)

- Solution `Animora.sln`, **.NET 10**, C#. Only `src/` + `tests/` in scope. `Animora-UI/`, `db-sample/`
  are **never touched**.
- Money = integer `Money(long Minor, string Currency)` with `checked(...)` + single rounding boundary
  (`ApplyPercent`, `MidpointRounding.AwayFromZero`). **Never introduce `decimal`/`double` money.**
- Time = `IClock.UtcNow` only; clinic day via `IClinicTimeZoneProvider` (Asia/Tehran). No ambient
  `DateTime.Now/UtcNow/Today`.
- Perf convention (PERFORMANCE.md §2): no LINQ on hot/write paths; indexed `for` loops.
- `CommandTypes` = **94**, `ErrorCodes` = **161** (live `*.cs` are the single source of truth).
- Server: ASP.NET Core 10, PostgreSQL (Npgsql) PG migrations 101–111 + KeyRing 101. Client SQLite
  migrations 001–039 (039 added in `T24`). Server auth is **opaque DP bearer tokens, no JWT**; role is
  under `AuthClaimTypes.Role` (deliberately NOT `ClaimTypes.Role`).

## BUILD / TEST POLICY (locked)

- **No full-solution build, no full test run, no long test suites.** (Explicit user constraint.)
- Validate by (a) targeted re-read of the edited file; (b) at most a single-project
  `dotnet build src/<Project>/<Project>.csproj` **only if quick & necessary** for a non-trivial edit —
  skip for small/obvious edits.
- Any **heavy** fix (big refactor, new table, migration, new service) → push immediately with a clear
  conventional-commit message, don't batch.

## PUSH POLICY (locked — violating = project death)

After completing **each task**, run from repo root:

```bash
scripts/git/push.sh "type(scope): message"
```

Does: `add -A` → `commit` → `fetch` + `rebase --autostash` → `push -u`, prints one `✓`/`✗`.
- Exit `3` = no credentials → call the `setup_github_environment` tool once, then re-run.
- Exit `2` = rebase conflict → resolve (prefer remote), re-run.
- Work branch: **`genspark_ai_developer`** unless the session is already on another; one commit per task.

## ⚠️ De-duplication already resolved (do NOT redo these)

Resolved in advance so you skip dead work:

- **SAC-05 (two "due" authorities)** is the **same defect as DOM-03**, already fixed by `T04`
  (`8fe9987d`). `RecurringExpense.IsDue` now takes `daysInMonth` and compares via `EffectiveDueDay`,
  agreeing with `Evaluate`. **→ NO TASK.** (Verify only if paranoid: `RecurringExpense.cs:248-249`.)
- **SAC-03 truncation half** is the **same as DOM-02**, already fixed by `T02` (`509ec407`): the fixed
  discount is now `new Money((long)Math.Round(discountValue, AwayFromZero), currency)` at
  `InvoiceTotals.cs:102`. **The *uncapped* half of SAC-03 is still open** → that is the only SAC-03 work
  left (task **T33**, the cap).
- **AUTH-02** (S07–S20 authored-but-unexecuted) is **not a code defect** — it is a build/verify-status
  risk and the policy explicitly bans full builds/tests. **→ NO CODE TASK**; see note under T31.
- All `*-0N` "baseline verification" entries (`AUTH-05`, `ISO-05`, `SAC-08`, `BAK-08`, `UI-06`) are Info
  confirmations, **not defects → NO TASK**.
- Report 12 (`12-synthesis.md`) adds no new IDs → **NO TASK**.

## Severity-ordered execution order

High → Medium → Low. Suggested sequence (IDs are this file's task numbers):
- **High:** T31 (SAC-01), T32 (SAC-02), T36 (BAK-01), T37 (BAK-02).
- **Medium:** T33 (SAC-03 cap), T34 (SAC-04), T35 (SAC-06), T38 (BAK-03), T39 (BAK-04), T40 (BAK-05),
  T41 (ISO-02), T42 (AUTH-01), T45 (UI-01), T46 (UI-02).
- **Low:** T43 (ISO-01 guard-comment), T44 (ISO-03/04 doc), T47 (AUTH-03), T48 (AUTH-04), T49 (SAC-07),
  T50 (BAK-06), T51 (BAK-07), T52 (UI-03), T53 (UI-04), T54 (UI-05).
- **Final:** T55 — delete this plan file.

> Each task starts `[ ]`; set to `[x]` after its push succeeds. Tasks are independent unless noted.

---

# Report 09 — Sales + Accounting (`src/Animora.Domain/**`, `src/Animora.Persistence.Sqlite/Finance/**`)

> Highest-value correctness cluster. SAC-01/04/07 all sit on the finance-projection path and are best
> reasoned about together, but each lands as its own commit.

## [x] T31 · SAC-01 — Make the rebuild reproduce the live void projection (High, bug/inconsistency)
- **Files:** `src/Animora.Persistence.Sqlite/Finance/FinanceProjectionStore.cs` (live, `InvoiceVoidedEvent`
  case, lines ~36–50) vs `src/Animora.Persistence.Sqlite/Finance/FinanceProjectionRebuilder.cs`
  (`RebuildAsync`/`FoldLocalFinanceAsync`, lines ~52 and ~85 filter out voids; ~127–132 fold refunds).
- **The divergence (verified):**
  - Live void (store, l.40–42):
    ```csharp
    (await Daily(e.TenantId, day, ct)).Apply(revenue: -invoice.Subtotal.Minor,
        discount: -invoice.DiscountTotal.Minor, insurance: -invoice.InsuranceCoverage.Minor,
        refund: invoice.GrandTotal.Minor, invoices: -1);
    ```
    i.e. it reverses revenue/discount/insurance, **adds GrandTotal to `Refund`**, and **decrements the
    invoice count** on the void's day.
  - Rebuild (rebuilder, l.52 & l.85) folds **only** non-void posted invoices:
    `.Where(i => i.State != InvoiceState.Draft && i.State != InvoiceState.Void && i.PostedUtc != null)`.
    So a voided invoice contributes nothing: revenue nets to its never-posted baseline, **no `Refund` is
    booked**, and `Invoices` is the posted-minus-void residual — not "posted then −1". Different
    `DailyFinanceFact.Refund` and `Invoices` for any day with a void.
- **Decision (locked):** the live warm path is the source of truth ("facts are a cache with a proof").
  **Change the rebuilder to reproduce the live path for voids** (do NOT change the live store). Concretely
  the rebuilder must, for voided-and-previously-posted invoices:
  1. fold their posted economics on the **post day** exactly as a posted invoice would (so the day the
     invoice was posted still shows its revenue/discount/insurance and `Invoices +1`), **and**
  2. on the **void day**, book `Refund += GrandTotal`, reverse revenue/discount/insurance, and
     `Invoices -= 1` — mirroring the store.
  - **Investigate first (small):** confirm whether `Invoice` exposes a void day/timestamp distinct from
    `PostedUtc`. `grep -n "Void\|void\|Posted\|Occurred" src/Animora.Domain/Sales/Invoice.cs | head`. The
    void day is the `LocalDay(voidUtc, zone)`; the post day is `LocalDay(PostedUtc, zone)`. If the voided
    invoice does **not** persist a void timestamp locally, the live path keys off the *event* day — in
    that case the rebuild cannot perfectly reconstruct the void day from entity state; then the correct
    minimal fix is to **include voided invoices in the posted fold on their post day** and **book the
    refund + `-1` count on the void day using whatever void timestamp the entity has** (look for a
    `VoidedUtc`/`UpdatedUtc`/state-change field). Document in the commit which timestamp was used.
- **Fix shape (rebuilder):** drop the blanket `State != Void` exclusion where it suppresses the posted
  fold; add a parallel fold that, for `State == Void && PostedUtc != null`, applies the void-day refund/
  reversal/`-1` just like the store's `InvoiceVoidedEvent` arm. Keep `AsNoTracking`, indexed loops, no new
  LINQ on the hot aggregation (`.Where` for the stream query is acceptable as elsewhere in this file).
- **Acceptance:** for a day containing one posted-then-voided invoice, a full
  `RebuildFinanceProjectionsAsync` yields the **same** `DailyFinanceFact.Refund` and `Invoices` as the
  live `ApplyAsync` path produced. Reason through both code paths on a single void example; no live-store
  change.
- **Heavy-fix note:** touches the rebuilder's fold structure — push as
  `fix(persistence): rebuild reproduces live void projection (refund + count) [SAC-01]`.

## [x] T32 · SAC-02 — Range-check / widen document-number format past 999,999 (High, latent overflow)
- **File:** `src/Animora.Application/Numbering/DocumentNumber.cs`, `Format` (consumed by
  `InvoiceNumberAllocator`, `JournalNumberAllocator`, `ExpenseNumberAllocator`):
  ```csharp
  return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{jalaliYear[^2..]}-{sequence:D6}");
  ```
- **Problem:** `D6` is a **minimum** width; the 1,000,000th sequence in one Jalali year renders 7 digits,
  breaking the fixed-width `PREFIX-YY-NNNNNN` contract (lexicographic sort of reports/exports, fixed-width
  columns). Counter is `long` so it never wraps; only the *format* silently changes shape.
- **Decision (locked):** keep the 6-digit contract but **fail loudly** rather than silently widen, because
  a legal document-number format must not change shape mid-year. Add an explicit guard:
  ```csharp
  if (sequence > 999_999)
      throw new InvalidOperationException(
          $"Document sequence {sequence} exceeds the {prefix}-YY-NNNNNN 6-digit contract for year {jalaliYear}.");
  ```
  (Place it before the `string.Create`.) This converts a silent corruption into a precise, early failure a
  high-volume tenant will see as a controlled error, not a mis-sorted ledger. Do **not** change `D6`.
  - If the team would rather widen the contract, that is a cross-system (DB column width, report layout)
    change and is **out of scope** here — the range-check is the safe, local fix. Note the choice in the
    commit.
- **Acceptance:** `Format(prefix, year, 1_000_000)` throws; `Format(prefix, year, 999_999)` → `...-999999`
  unchanged. No change to the happy path's rendered string.

## [x] T33 · SAC-03 — Cap the fixed-amount invoice discount (Medium, bug) — truncation half already done
- **File:** `src/Animora.Domain/Sales/InvoiceTotals.cs`, `Compute` (lines ~98–104).
- **Already fixed (T02/DOM-02):** the truncating `(long)discountValue` cast is now
  `new Money((long)Math.Round(discountValue, MidpointRounding.AwayFromZero), currency)` — **do not touch
  the rounding.**
- **Still open (the real SAC-03 work):** the fixed discount is **uncapped**, so a fixed amount larger than
  `netAfterLineDiscount` drives `DiscountTotal > Subtotal` → negative line bases + negative `GrandTotal` in
  a *draft* (only `Invoice.Post` rejects it via `INVOICE_NEGATIVE_TOTAL`). Current:
  ```csharp
  var invoiceDiscount = discountKind == DiscountKind.Percent
      ? netAfterLineDiscount.ApplyPercent(discountValue)
      : new Money((long)Math.Round(discountValue, MidpointRounding.AwayFromZero), currency);
  ```
- **Fix:** clamp the fixed-amount discount to `netAfterLineDiscount` (never negative, never exceeding the
  net). Keep integer-money discipline:
  ```csharp
  var fixedMinor = (long)Math.Round(discountValue, MidpointRounding.AwayFromZero);
  if (fixedMinor < 0) fixedMinor = 0;
  if (fixedMinor > netAfterLineDiscount.Minor) fixedMinor = netAfterLineDiscount.Minor;
  var invoiceDiscount = discountKind == DiscountKind.Percent
      ? netAfterLineDiscount.ApplyPercent(discountValue)
      : new Money(fixedMinor, currency);
  ```
  (Percent branch is already bounded by `SetDiscount`'s `percent > 100` reject; leave it.) This makes the
  draft totals non-negative, so downstream tax/insurance never run on a negative base.
- **Acceptance:** fixed discount `10_000_000` on a net of `500_000` → `invoiceDiscount.Minor == 500_000`,
  `GrandTotal >= 0` in the draft; a normal in-range fixed discount is unchanged. Confirm `netAfterLineDiscount`
  is in scope at that point (it is — declared at l.98).

## [x] T34 · SAC-04 — Bound the finance-projection rebuild (Medium, perf/latent growth)
- **File:** `src/Animora.Persistence.Sqlite/Finance/FinanceProjectionRebuilder.cs` —
  `RebuildAsync`/`FoldPeerFinanceAsync`:
  ```csharp
  var localInvoiceIds = (await db.Set<Invoice>().AsNoTracking()
          .Select(i => i.Id).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
  ```
  plus unbounded `ToListAsync` of invoices / lines (`SelectMany`) / sale movements / expenses / refunds
  with **no date bound**.
- **Problem:** every full rebuild pulls the entire financial history into memory on single-threaded SQLite;
  the `HashSet<string>` of all invoice ids is the clearest unbounded allocation. Cost grows with clinic
  lifetime; the peer-pull that *triggers* a rebuild gets steadily more expensive.
- **Decision (locked, minimal-but-real):** make the rebuild **streamed/batched** rather than fully
  materialized, and where the caller knows the affected window, **bound by day/period**.
  1. **Investigate the call sites (small):** `grep -rn "RebuildFinanceProjections\|FoldPeerFinance\|RebuildAsync" src/ | grep -i finance`
     — identify the maintenance caller (`RebuildFinanceProjectionsAsync`) and the peer-pull trigger. If the
     peer-pull knows the changed days, thread an optional `DateOnly from`/`to` (or affected-days set) into
     `RebuildAsync` and scope the queries to it; the maintenance "rebuild everything" path passes null.
  2. **Replace the all-ids `HashSet` with a streamed existence check** or a batched id load (page the ids
     in chunks; don't hold them all). For the per-row folds, switch the `ToListAsync` materializations to
     `AsAsyncEnumerable()` streaming where the fold is a running accumulation (it is — into `DailyFinanceFact`
     accumulators), so memory stays O(batch) not O(all history). Keep `AsNoTracking`.
- **Acceptance:** rebuild no longer materializes every invoice id into one `HashSet`; per-row folds stream;
  when a window is supplied only that window's rows are read. Reason the memory is now bounded; result for a
  full rebuild is numerically identical to before (same accumulation, different iteration).
- **Heavy-fix note:** data-access reshape — push as
  `perf(persistence): stream/bound finance-projection rebuild [SAC-04]`.

## [x] T35 · SAC-06 — Block double clearing of a recovered bounced cheque (Medium, latent/bug)
- **File:** `src/Animora.Domain/Finance/Cheque.cs` — state machine + `MarkCleared`. Transitions allow
  `Bounced → InPortfolio → Deposited → Cleared`, and `MarkCleared` raises a **second** `ChequeClearedEvent`;
  the aggregate carries a single `JournalEntryId` and does not record that clearing was already posted.
- **Problem:** correctness relies entirely on the posting/outbox layer de-duping by event id. The domain
  gives no second line of defence against a re-clear (or a replay) producing two debit-bank/credit-cheque
  entries.
- **Decision (locked):** add a domain-level guard so a cheque cannot raise a *second* clearing. The clean
  invariant: a cheque that has **already cleared once** must not clear again for the *same* clearing. Since
  a bounced→recovered→re-cleared cheque is a legitimate lifecycle, the guard is: **`MarkCleared` must be
  idempotent on an already-posted clearing** — track whether a clearing journal entry is already associated,
  and refuse (or no-op) a re-clear that would re-post.
  1. **Investigate (small):** read `Cheque.cs` for the fields set in `MarkCleared` (`JournalEntryId`,
     `State`, any `ClearedUtc`). `grep -n "MarkCleared\|JournalEntryId\|ChequeClearedEvent\|ClearedUtc\|State" src/Animora.Domain/Finance/Cheque.cs`.
  2. **Fix:** in `MarkCleared`, if the cheque is already `Cleared` (or already has a clearing
     `JournalEntryId` for the current deposit), return a domain failure (`Result.Fail(ErrorCodes.<existing
     cheque-state code>)`) or guard the second `ChequeClearedEvent` so it is not raised again. Prefer
     raising a clear domain error over silent no-op so the caller knows. Keep the legitimate
     bounce→recover→deposit→clear path working **once**.
- **Acceptance:** first clear → one `ChequeClearedEvent`; an immediate second `MarkCleared` without a new
  deposit cycle → no second event (fails or no-ops). The legitimate recover-then-clear still clears once.
  Reason through the state machine; no posting-layer change required.

## [x] T49 · SAC-07 — Batch the per-event invoice fetch in the projection drain (Low, perf/N+1)
- **File:** `src/Animora.Persistence.Sqlite/Finance/FinanceProjectionStore.cs` — `ApplyAsync`: each
  `InvoicePostedEvent`/`InvoiceVoidedEvent`/`PaymentRegisteredEvent` does
  `db.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == …, ct)` (tracking, with `Lines`) **per event**.
- **Problem:** N round-trips + N line-collection loads as the outbox batch drains; immaterial now, N+1 as
  batches grow after offline catch-up.
- **Fix:** if `ApplyAsync` processes events **one at a time** (confirm the drain loop:
  `grep -rn "ApplyAsync\|foreach\|drain\|outbox" src/Animora.Persistence.Sqlite/Finance/FinanceProjectionStore.cs src/Animora.Persistence.Sqlite/Workers/FinanceProjectionWorker.cs | head`),
  add a **per-drain-batch invoice cache**: collect the distinct `InvoiceId`s of the batch, load them once
  with `AsNoTracking().Where(i => ids.Contains(i.Id)).Include(i => i.Lines)`, and resolve each event from the
  dictionary. If the current signature is strictly per-event with no batch handle, this is a **small
  refactor** of the worker drain to pass the batch; if that is too invasive for a Low item, the minimal
  acceptable change is to switch the per-event fetch to `AsNoTracking()` (the store only reads the invoice)
  to drop change-tracker overhead, and leave the batching as a documented TODO. Prefer real batching if the
  drain already has the batch in hand.
- **Acceptance:** either one invoice load per drain batch (distinct ids) instead of per event, or at minimum
  `AsNoTracking` on the fetch with a TODO noting the batching opportunity. No projection-value change.

---

# Report 10 — Backup System (`src/Animora.Persistence.Sqlite/Maintenance/**`, `src/Animora.Client.Services/Diagnostics/**`, `src/Animora.Persistence.PostgreSql/Operator/**`, `src/Animora.Server/Admin/**`)

## [x] T36 · BAK-01 — Make the local backup restorable off-machine (High, DR completeness)
- **File:** `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` (`BackupAsync`, `Open` via
  `connectionSetup.Prepare`), `src/Animora.Persistence.Sqlite/Context/KeySource.cs`
  (`OsKeystore`/`InsecureFallback`).
- **Problem:** `BackupAsync` produces an **encrypted** `.db` whose key lives **only** in the OS keystore of
  the machine that made it. On PC death / OS reinstall / move-to-new-machine the backup is unreadable — a
  backup you cannot restore is not a backup. No key-escrow in the artifact.
- **Decision (locked):** add a **passphrase-wrapped key export** alongside the backup so a backup can be
  restored on a new machine with an owner-supplied passphrase. Do **not** write the raw key in plaintext.
  1. **Investigate (small):** how the key is obtained/applied — `grep -n "Prepare\|key\|Key\|passphrase\|KeySource\|Pragma" src/Animora.Persistence.Sqlite/Context/KeySource.cs src/Animora.Persistence.Sqlite/Context/*EncryptionProvider*.cs | head`.
     Confirm whether a passphrase path already exists (the store cipher is `PRAGMA key` via the multi-cipher
     provider; `T16/SQL-03` changed it to a raw keyspec). There is likely already a way to derive a key from
     a passphrase.
  2. **Fix shape:** on `BackupAsync`, write a sidecar `<backup>.keywrap` next to the `.db` containing the
     store key **wrapped** with a key derived (PBKDF2/Argon2 — use whatever KDF the infra layer already
     exposes; `grep -rn "Rfc2898\|PBKDF2\|Argon2\|DeriveKey" src/Animora.Infrastructure.Local src/Animora.Persistence.Sqlite | head`) from an **owner backup passphrase**. If no backup-passphrase concept
     exists yet, the minimal honest fix is: (a) make the key-escrow **explicit and documented** by adding a
     `ExportWrappedKeyAsync(passphrase)` + `ImportWrappedKeyAsync(passphrase)` pair used by backup/restore,
     and (b) have `BackupAsync` emit the `.keywrap` sidecar when a passphrase is configured; otherwise emit a
     **loud warning** in the maintenance log that the backup is machine-bound and unrecoverable elsewhere.
  3. **Restore side:** `RestoreAsync` must, when restoring onto a machine whose keystore lacks the key,
     unwrap from the `.keywrap` sidecar with the passphrase and install the key before opening.
- **Acceptance:** a backup made with a configured passphrase can be opened on a machine with a fresh keystore
  by supplying the passphrase; without a passphrase the backup still works locally but logs a prominent
  "machine-bound, not portable" warning. No raw key ever written to disk. Document exactly what was
  implemented vs left as a documented limitation.
- **Heavy-fix note:** new escrow path — push as
  `feat(persistence): passphrase-wrapped key escrow for portable local backup [BAK-01]`.

## [ ] T37 · BAK-02 — Add per-tenant server backup/restore (High, backup/isolation)
- **File:** `src/Animora.Persistence.PostgreSql/Operator/DatabaseBackupService.cs` (`CreateAsync` =
  whole-DB `pg_dump --format=custom`), `src/Animora.Server/Admin/AdminBackupAdapter.cs`,
  `AdminBackupEndpoints.cs`.
- **Problem:** one whole-DB dump spans every tenant → a single download is a cross-tenant PII export and a
  restore is all-or-nothing (cannot restore one tenant without rolling back everyone). This is the exact
  cross-tenant-bleed-in-a-shared-backup risk audit target #13 names.
- **Decision (locked):** add a **per-tenant logical backup** path that filters by `TenantId`, keeping the
  existing whole-DB dump for full-DR but making per-tenant the default for tenant-scoped restore/handoff.
  1. **Investigate (bounded):** the `pg_dump` invocation shape and how tenant rows are keyed —
     `grep -n "pg_dump\|--table\|--format\|psi\|Arguments\|Environment" src/Animora.Persistence.PostgreSql/Operator/DatabaseBackupService.cs | head`.
     `pg_dump` cannot filter by a column; a per-tenant extract needs either (a) `--format=plain` + a
     `WHERE TenantId=` via `COPY (SELECT … WHERE tenant)` per table, or (b) a logical export written through
     EF using the tenant-bound read context (report 08 shows `ServerReadContextFactory.Create` yields a
     tenant-bound no-tracking context, and every `SyncableEntity` has the `Tenant` filter).
  2. **Fix shape (prefer the EF route — it reuses the isolation wall):** add
     `CreateTenantBackupAsync(tenantId, …)` that opens a tenant-bound read context, serializes each
     `SyncableEntity` set to a versioned export file (NDJSON/JSON per table), under the same hardened
     name-whitelist + operator-auth + antiforgery + rate-limit + audit the whole-DB path already enforces
     (keep `BAK-08` guarantees). A per-tenant **restore** replays the export under a tenant-bound write
     context (stamping enforces tenant). This is a **large** change.
  3. If the full per-tenant restore is too large for one pass, land the **export** half (tenant-scoped,
     isolated artifact) first and push it; the import half can be a follow-up task — but do **not** leave the
     whole-DB dump as the only tenant-handoff mechanism silently; document the state.
- **Acceptance:** an operator can produce a backup artifact containing exactly one tenant's rows (verified by
  the tenant-bound context filter), under the same auth/anti-traversal guards; restore (or at least a
  documented import path) applies it under tenant stamping. No change to the existing whole-DB path.
- **Heavy-fix note:** new per-tenant export/import — push as
  `feat(server): per-tenant logical backup export (tenant-isolated artifact) [BAK-02]`.

## [ ] T38 · BAK-03 — Add a scheduled server backup (Medium, RPO/latent)
- **File:** no scheduler exists; only `AdminBackupAdapter.CreateAsync` (operator-triggered, `form.Confirmed`)
  calls `DatabaseBackupService.CreateAsync`. (`grep -rn "DatabaseBackupService\|CreateAsync\|BackgroundService" src/Animora.Server/Admin src/Animora.Server/Hosting | head`.)
- **Problem:** recovery point = last manual click; on host loss, up to days of every tenant's data lost. No
  PITR/WAL archiving.
- **Decision (locked, minimal honest scope):** PITR/WAL archiving is Postgres-ops territory (out of app
  scope) — **document it as the recommended operational control** and implement the **in-app part**: a
  `BackgroundService` that triggers `DatabaseBackupService.CreateAsync` on a configurable cadence (e.g.
  daily) behind the same single-flight gate, honouring the retention/prune already in the service.
  1. **Investigate (small):** how other server `BackgroundService`s are registered —
     `grep -rn "BackgroundService\|AddHostedService" src/Animora.Server | head` — follow that pattern.
  2. **Fix:** add `ScheduledBackupService : BackgroundService` reading a cadence from server options
     (default daily), calling the existing create path; respect the `SemaphoreSlim Gate(1,1)` single-flight
     so it never collides with a manual backup. Register via `AddHostedService`. Add a config flag to disable
     it. Emit audit rows like the manual path.
- **Acceptance:** with the scheduler enabled, a backup is created on cadence without operator action, serialized
  against manual backups; disabling the flag restores manual-only behaviour. WAL/PITR is documented as the
  ops-side complement, not implemented in-app. Note the split in the commit.
- **Heavy-fix note:** new hosted service — push as
  `feat(server): scheduled server backup background service [BAK-03]`.

## [ ] T39 · BAK-04 — Persist local backup cadence across restarts (Medium, latent)
- **File:** `src/Animora.Client.Services/Diagnostics/MaintenanceScheduler.cs`:
  ```csharp
  private DateTimeOffset? _lastBackupUtc;                         // l.22 — in-memory only
  using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));  // l.30
  if (_lastBackupUtc is null || now - _lastBackupUtc >= TimeSpan.FromDays(1)) { … _lastBackupUtc = now; } // l.50-53
  ```
  (same pattern for `_lastCompactUtc` l.57, `_lastVacuumUtc` l.62.)
- **Problem:** `_lastBackupUtc` resets to `null` every process start → the first 15-min tick after each launch
  always backs up (daily throttle never carries across restarts); a kiosk rebooting nightly keeps only ~5
  launches' worth of restore points regardless of calendar days; no catch-up after long sleep.
- **Decision (locked):** read the last-backup timestamp from the persisted `StoreMaintenanceLog` (the report
  notes it exists but is not read back) instead of starting from `null`.
  1. **Investigate (small):** `grep -rn "StoreMaintenanceLog\|LastBackup\|MaintenanceLog\|RecordBackup" src/Animora.Persistence.Sqlite src/Animora.Client.Services | head` — find how maintenance runs are
     logged and whether the latest backup timestamp is queryable.
  2. **Fix:** on `MaintenanceScheduler` start, seed `_lastBackupUtc` (and compact/vacuum) from the most
     recent corresponding `StoreMaintenanceLog` entry; after each run, the existing log write already records
     it. Keep the in-memory field as a cache, but initialize it from durable state so the daily throttle and
     retention honour calendar days, not launches. Add catch-up: if `now - lastBackup >= 1 day`, back up once
     on first tick (already the behaviour, now correctly gated).
- **Acceptance:** relaunching the app within 24h of the last backup does **not** immediately re-back-up;
  the daily cadence is measured against the persisted last-backup time; retention reflects calendar days.
  Reason through the start-up seed + tick logic.

## [x] T40 · BAK-05 — Rebuild finance projections on restore (Medium, consistency)
- **File:** `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` — `RestoreAsync`
  (l.342: `var snapshots = await RebuildStockSnapshotsAsync(ct);` and the symmetric stash-restore at l.370).
  Finance facts (`DailyFinanceFact`/`ProductSalesFact`/`OwnerFinancialSnapshot`) and the activity feed come
  back **verbatim** from the backup; only stock is re-proven.
- **Problem:** a restored store can show finance facts that no longer match its own ledger (compounded by
  SAC-01's divergence). The restore comment "re-prove it from the ledger" is applied to stock only.
- **Fix (one-line symmetry):** after the stock rebuild in `RestoreAsync` (and `RestoreStashAsync` if it does
  the same), also call `RebuildFinanceProjectionsAsync(ct)` (and `RebuildActivityFeedAsync(ct)` if cheap and
  present), with the same failure handling (`if (…).IsFailure → Result.Fail(StoreRestoreFailed, … previous
  store kept in {quarantine})`). Confirm the method names exist:
  `grep -n "RebuildFinanceProjectionsAsync\|RebuildActivityFeedAsync\|RebuildStockSnapshotsAsync" src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs`.
  **Order:** do stock first (current), then finance, then activity feed — finance may depend on stock COGS.
  **Depends on T31** landing first so the finance rebuild it now calls is itself correct.
- **Acceptance:** `RestoreAsync` rebuilds stock **and** finance projections (and activity feed) before
  returning success; a restore of an older backup yields finance facts that match the restored ledger. Same
  quarantine-on-failure semantics.

## [ ] T50 · BAK-06 — Prune by the backup's embedded timestamp, not mtime (Low, latent)
- **File:** `src/Animora.Persistence.PostgreSql/Operator/DatabaseBackupService.cs` — `List`/`Prune`:
  ```csharp
  .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, …)
  …
  foreach (var stale in List(directory).Skip(RetainCount)) TryDelete(…);
  ```
- **Problem:** any mtime touch (AV scan, copy-in, filesystem restore) re-ranks a file as "newest", so a
  genuinely newer dump can be pruned while an older recently-touched one survives.
- **Fix:** the name carries a sortable UTC stamp (`^animora-\d{8}T\d{6}Z-[0-9a-f]{8}\.dump$`). Make the
  **name's embedded timestamp the primary ordering key** (parse the `YYYYMMDDTHHMMSSZ` block, order
  descending by it), with `LastWriteTimeUtc` only as a tiebreaker. Keep `IsBackupFileName` as the filter so
  only validly-named files are considered.
- **Acceptance:** `List` orders by the name-embedded UTC stamp; touching a file's mtime no longer changes its
  prune rank; retention keeps the newest `RetainCount` by actual backup time. No change to create/download.

## [x] T51 · BAK-07 — Make the restore copy idempotent (Low, latent)
- **File:** `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` —
  `RestoreAsync` (l.332: `File.Copy(backupPath, Store.DatabasePath, overwrite: false);`) and
  `RestoreStashAsync` (l.242: `File.Copy(stash, Store.DatabasePath, overwrite: false);`). `MoveToQuarantine`
  only moves files that `File.Exists`.
- **Problem:** if a stray `-wal`/`-shm` or a prior aborted restore left a file at `Store.DatabasePath`
  between the quarantine-move and the copy, `File.Copy(overwrite:false)` throws `IOException` and the restore
  fails with the live store already quarantined → no live DB until manual recovery.
- **Fix:** the destination was *just* quarantined and should be empty, so the copy is safe to make
  idempotent. Either: (a) change both to `overwrite: true`; or (b) before the copy, delete any residual
  `Store.DatabasePath` + `-wal` + `-shm` (there is already a helper that enumerates the store's sidecar
  files — reuse it; `grep -n "\-wal\|\-shm\|DatabasePath\|sidecar\|SidecarSuffix" src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs | head`). Prefer (b) + `overwrite: true` for
  belt-and-braces. Keep the quarantine-move before it.
- **Acceptance:** a residual file at the destination no longer aborts the restore; the live store is present
  after a successful restore in all cases. Reason through the move→clean→copy sequence.

> **BAK-08** is Info/verification only — **no task.**

---

# Report 08 — Multi-Tenant Isolation (`src/Animora.Persistence.PostgreSql/{Conventions,Identity,Tenancy}/**`)

## [ ] T41 · ISO-02 — Assert "tenant data ⇒ SyncableEntity" at model build (Medium, isolation latent)
- **File:** `src/Animora.Persistence.PostgreSql/Conventions/ServerModelConventions.cs` — `ApplyTenancy`
  (l.40–48 iterates `modelBuilder.Model.GetEntityTypes()`, applies the `Tenant` filter + stamping **only**
  to types assignable to `SyncableEntity`, l.48 `if (!typeof(SyncableEntity).IsAssignableFrom(clrType)) continue;`).
- **Problem:** the entire isolation guarantee is keyed on inheritance. A future server table modelled as a
  plain entity but holding a `TenantId`/tenant-scoped data silently gets **no** `Tenant` filter and no
  stamping → cross-tenant leakage until noticed. No compile/model-build assertion catches it.
- **Decision (locked):** add a **model-build guard** that fails startup if any mapped entity looks
  tenant-bearing (has a `TenantId` property, or any property named like a tenant key) but does **not** derive
  `SyncableEntity`. This turns a silent latent leak into a fail-fast at startup.
  1. **Investigate (small):** confirm how `ApplyTenancy` is invoked and whether there is a single place after
     all entities are configured to run the assertion (end of `ApplyTenancy`, or `OnModelCreating` tail).
     `grep -n "ApplyTenancy\|OnModelCreating\|GetEntityTypes" src/Animora.Persistence.PostgreSql/Conventions/ServerModelConventions.cs src/Animora.Persistence.PostgreSql/Context/*Context*.cs | head`.
  2. **Fix:** after the tenancy loop, iterate all entity types once more; for each **non-`SyncableEntity`**
     type, check `entityType.FindProperty("TenantId") is not null` (or a property whose name ends with
     `TenantId`). If found → `throw new InvalidOperationException($"Entity {clrType} carries a tenant key but
     does not derive SyncableEntity — it would escape the Tenant filter/stamping.");`. Allow a small explicit
     **opt-out allowlist** (e.g. the deliberately-keyless `UserLookupEntry` from ISO-01, and any true
     cross-tenant operator view) so legitimate exceptions are declared, not accidental.
- **Acceptance:** adding a plain entity with a `TenantId` (outside the allowlist) fails model build with the
  explicit message; the current model (all tenant tables already `SyncableEntity`, plus the allowlisted
  keyless lookups) builds unchanged. Reason by inspecting the model types; no migration.

## [ ] T43 · ISO-01 — Harden + document the one keyless cross-tenant read (Low, isolation seam)
- **File:** `src/Animora.Persistence.PostgreSql/Identity/UserLookupContext.cs` —
  `context.Users.AsNoTracking().Where(u => u.Id == userId).FirstOrDefaultAsync(ct)` over `public."Users"`
  with **no** tenant filter (deliberate pre-binding seam).
- **Problem:** safe today (ULID unguessable, callers only `Bind` the returned `TenantId`), but it is the lone
  unfiltered path; any widening (lookup by email/name without tenant, or returning the entry to an untrusted
  caller) becomes cross-tenant disclosure. No guardrail documents/enforces the narrow contract.
- **Decision (locked, low-churn):** this is a seam to **guard against future misuse**, not a live bug. The
  fix is defensive narrowing + an explicit contract:
  1. Ensure the lookup is **strictly by ULID id only** — confirm there is no by-name/by-email overload on
     this context (`grep -n "public\|DbSet\|FindBy\|Where\|Users" src/Animora.Persistence.PostgreSql/Identity/UserLookupContext.cs`). If an unfiltered by-name/by-email query exists, that is the real risk → restrict or
     remove it.
  2. Add a prominent XML-doc / comment on the context + method stating the invariant: *"Keyless, tenant-
     filter-free by design for pre-binding token validation. MUST be queried only by unguessable ULID id,
     and the returned `TenantId` used only to `Bind` context — never returned to or filtered by untrusted
     caller input. Any widening is a cross-tenant disclosure."* Tie it to the ISO-01 finding id.
- **Acceptance:** the context exposes only the by-id lookup (no weaker-key query); the invariant is
  documented at the declaration site referencing ISO-01. No behaviour change if already id-only.

## [ ] T44 · ISO-03 + ISO-04 — Document operator-bypass breadth & provisioning scope invariant (Low, doc/guard)
- **Files:** `src/Animora.Persistence.PostgreSql/Tenancy/NoUnnamedIgnoreFiltersInterceptor.cs`
  (`QueryCompilationStarting` — `IgnoreQueryFilters(["Tenant"])` allowed when `IsOperator`);
  `src/Animora.Persistence.PostgreSql/Identity/TenantProvisioner.cs` (`CreateTenantWithOwnerAsync` binds the
  new tenant on the scope's context; the "use a dedicated scope" requirement is a comment).
- **Problem (both Low, design-correct, enforcement-gap):** ISO-03 — a single `IsOperator` boolean gates
  cross-tenant read for the whole context; ISO-04 — provisioning clobbers the scope's tenant binding if the
  scope is reused.
- **Decision (locked, minimal):** these are not runtime bugs today; make the invariants **explicit and
  harder to misuse** without a large refactor:
  - **ISO-04:** in `CreateTenantWithOwnerAsync`, add a cheap precondition that the scope's tenant context is
    **unbound** before binding (e.g. assert `!tenantContext.IsBound` / `tenantContext.TenantId is null` and
    throw a clear error if already bound). `grep -n "Bind\|IsBound\|TenantId\|AuthTenantContext" src/Animora.Persistence.PostgreSql/Identity/TenantProvisioner.cs` to find the context API. This converts the
    comment into an enforced guard with near-zero risk.
  - **ISO-03:** add/clarify a doc comment at the operator-bypass site stating that operator-context code is
    **the tenant wall** and must be audited as such (reference the backup per-tenant work T37). No code
    change beyond the comment unless a cheap per-query scoping is obvious.
- **Acceptance:** provisioning throws if invoked on an already-bound scope; the operator-bypass site
  documents its breadth. No change to the happy provisioning path (callers already use a dedicated scope).

---

# Report 07 — Server Host, Auth & Authorization (`src/Animora.Server/{Auth,Authorization}/**`)

> **AUTH-02 has NO code task:** it is a "authored-but-unexecuted S07–S20" build/verify-status risk, and the
> locked policy bans full builds / long test runs. Record it as a known residual risk in the commit for any
> adjacent auth change; do not attempt to build/smoke those phases here.

## [ ] T42 · AUTH-01 — Make device admission a policy flag (default keep current) (Medium, provisioning)
- **File:** `src/Animora.Server/Auth/LoginService.cs` — `LoginAsync` (l.131–135) and the device
  register/approve path (l.289–355):
  ```csharp
  Device candidate = Device.Register(tenant.Id, user.Id, "Unnamed device", installationHash, "unknown", clock);
  candidate.Approve();   // all devices auto-approved; manual manager approval removed
  ```
- **Problem:** any valid credential activates a **new** device immediately; the only control is after-the-fact
  `Revoke`. For a commercial multi-tenant product this weakens defence against credential-stuffing/shared
  credentials. It is a **deliberate policy choice**, so the fix is to make it *configurable*, not to force a
  behaviour change.
- **Decision (locked):** introduce a per-tenant (or server-default) **device-admission policy** flag:
  `AutoApprove` (current behaviour, default) vs `RequireApproval` (new device registered as **pending**,
  not `Approve()`d, until an admin approves). Keep the idempotent `(user, installation)` convergence.
  1. **Investigate (small):** the `Device` aggregate's states — `grep -n "Approve\|Pending\|Revoke\|Reinstate\|DeviceState\|Register" src/Animora.Domain/**/Device.cs` (find the file:
     `grep -rln "class Device\|record Device" src/Animora.Domain`). Confirm a `Pending`/unapproved state
     exists or can be represented.
  2. **Fix:** read the admission policy from tenant/server options; in `LoginAsync`, call `candidate.Approve()`
     **only** when the policy is `AutoApprove`; otherwise leave the device pending and return a clear
     "device awaiting approval" outcome (reuse an existing error/status code if present —
     `grep -n "DEVICE\|Device" src/Animora.Domain/Results/ErrorCodes.cs | head`). Default the flag to
     `AutoApprove` so existing behaviour is unchanged unless a tenant opts into stricter admission.
- **Acceptance:** with the default flag, behaviour is identical (auto-approve); with `RequireApproval`, a new
  installation is created pending and login reports awaiting-approval instead of a full session. No change to
  the auth crypto/rate-limit core.
- **Heavy-fix note:** touches login flow + options — push as
  `feat(server): configurable device-admission policy (default auto-approve) [AUTH-01]`.

## [ ] T47 · AUTH-03 — Widen/prewarm the RBAC decision cache (Low, perf)
- **File:** `src/Animora.Server/Authorization/{AuthzDecisionCache,MembershipResolver,CommandAuthorizer}.cs`.
- **Problem:** on a decision-cache miss, every command does membership + role-definition DB reads on the hot
  path (bounded by the authz budget but repeated for high-command-rate tenants).
- **Decision (locked, minimal):** reduce the miss rate without changing the security model.
  1. **Investigate (small):** the cache key + TTL — `grep -n "TimeSpan\|TTL\|Expiration\|Cache\|key\|Get\|Set" src/Animora.Server/Authorization/AuthzDecisionCache.cs | head`.
  2. **Fix (pick the cheap, safe one):** cache at the **(tenant, user, permission)** granularity with a
     short TTL if not already; and/or prewarm a user's membership+roles once per request/session rather than
     per command (resolve membership once, reuse for the request's commands). Keep fail-closed on unmapped
     commands. Do not extend TTL so long that a revoke is noticeably delayed — keep it short (seconds) and
     invalidate on security-stamp rotation if an invalidation hook exists.
- **Acceptance:** repeated commands by the same (tenant,user) within the TTL hit the cache instead of the
  store; a role/permission change still takes effect within the TTL / on stamp rotation. No correctness
  change. (If the cache already does this, record that and make it a no-op doc note.)

## [ ] T48 · AUTH-04 — Document (and optionally tighten) access-token revocation bound (Low, security/time)
- **File:** `src/Animora.Server/Auth/AccessTokenIssuer.cs` (DP ticket with `ExpiresUtc`),
  `LoginService.RefreshAsync` (`RotateSecurityStamp`).
- **Problem:** no server-side deny-list; an already-issued access token stays valid until
  `BearerTokenExpiration` even after a device Revoke / stamp rotation (per-request validation doesn't re-check
  the stamp against the live user). Compromise window = access-token lifetime.
- **Decision (locked, low-risk):** this is an accepted trade-off for most threat models; the task is to
  **bound and document** it, and optionally tighten cheaply:
  1. **Confirm the window:** `grep -n "BearerTokenExpiration\|ExpiresUtc\|Expiration\|TimeSpan.From" src/Animora.Server/Auth/*.cs src/Animora.Server/Hosting/*.cs | head` — find the configured access-token
     lifetime. Document it (and that refresh re-reads the stamp) in a comment at the issuer.
  2. **Optional cheap tightening (only if obvious & low-risk):** if the access-token lifetime is long (e.g.
     > a few minutes), shorten it via config so revocation is bounded tighter, leaning on the existing
     refresh rotation. Do **not** build a full per-request stamp re-check / deny-list here (that is a larger
     change) unless explicitly requested.
- **Acceptance:** the compromise-window bound is documented at the issuer; if the lifetime was unreasonably
  long, it is shortened via config. No new deny-list infrastructure unless requested. Record the decision.

> **AUTH-05** is Info/verification only — **no task.**

---

# Report 11 — Desktop UI/DI + Cross-Layer (`src/Animora.Desktop/**`)

## [ ] T45 · UI-01 — Evict heavy page-VM collections on navigate-away (Medium, memory growth)
- **File:** `src/Animora.Desktop/App.axaml.cs` `ConfigureServices` — page VMs are `AddSingleton`
  (`DashboardPageViewModel`, `PatientsPageViewModel`, `SalePageViewModel`, `CashierPageViewModel`,
  `HistoryPageViewModel`, the four Statistics section VMs, …), each holding its last load in
  `RangeObservableCollection`/chart lists for the whole session (released only on sign-out provider rebuild).
- **Problem:** steady-state memory = sum of the most recent load of every visited screen; list-heavy pages
  (Patients/Owners, History, Statistics `LoadMore`-append) accumulate with usage. Not a leak, but an
  unbounded-with-usage working set; no per-page eviction.
- **Decision (locked, targeted):** add an `OnNavigatedFrom`-style clear for the **largest** collections on
  the list-heavy pages, rather than making every VM transient (transient would lose the warm-navigate
  benefit the design intends).
  1. **Investigate (small):** the navigation hook — `grep -rn "OnNavigatedFrom\|OnNavigatedTo\|INavigation\|Navigated\|Deactivated" src/Animora.Desktop/Shell src/Animora.Desktop/Pages | head`. Find whether page
     VMs get a navigate-away callback.
  2. **Fix:** for the heaviest list pages (History, Patients/Owners, Statistics expense pages that append),
     clear the big `RangeObservableCollection`s (and reset paging state) in the navigate-away callback, so
     re-navigation reloads page 1 instead of retaining everything. Leave small/cheap pages warm. If no
     navigate-away hook exists, add a minimal one on the base page VM.
- **Acceptance:** navigating away from a list-heavy page releases its large collections (memory drops);
  navigating back reloads correctly. Light pages keep their warm behaviour. Reason about which collections
  are the big ones; no functional regression.

## [ ] T46 · UI-02 — Guard Statistics publish onto the UI thread (Medium, thread affinity)
- **File:** `src/Animora.Desktop/Pages/Statistics/StatisticsSectionViewModel.cs` (and the concrete section
  VMs, e.g. `StatisticsClientsViewModel.LoadCoreAsync`): after `await Task.WhenAll(...)` the continuation
  calls `PublishStats`/`PublishSpecies` which mutate `[ObservableProperty]` + `RangeObservableCollection`
  bound to the UI. Correct **only** because `LoadAsync` is entered from the UI thread (captured
  SynchronizationContext marshals back); nothing asserts UI-thread affinity.
- **Problem:** a future non-UI caller (sync-change bridge, timer without dispatcher hop) would mutate
  UI-bound collections off-thread → Avalonia cross-thread collection-change crash.
- **Fix:** wrap the publish calls in `Dispatcher.UIThread.Post(...)`/`await Dispatcher.UIThread.InvokeAsync(...)`
  so mutation of bound collections is always on the UI thread regardless of caller context. Apply to the
  publish section of each `LoadCoreAsync` in the Statistics VMs.
  1. **Investigate (small):** `grep -rn "PublishStats\|PublishSpecies\|Publish\|RangeObservableCollection\|Dispatcher" src/Animora.Desktop/Pages/Statistics | head` — enumerate the publish sites.
  2. Prefer a single small helper `PublishOnUi(Action)` used by all section VMs for consistency.
- **Acceptance:** statistics publish runs on the UI thread even when `LoadAsync` is invoked from a background
  context; UI-thread entry still works unchanged. No behaviour change for the current UI-thread caller.

## [ ] T52 · UI-03 — Make global exception hooks idempotent across sign-out (Low, handler leak)
- **File:** `src/Animora.Desktop/App.axaml.cs` — `OnFrameworkInitializationCompleted`
  (`RegisterProcessHooks`, `Dispatcher.UIThread.UnhandledException += …`) and `OnProfileSignedOut` (rebuilds
  provider, then `GetRequiredService<GlobalExceptionHandler>().RegisterProcessHooks()` again).
- **Problem:** each sign-in/out cycle re-subscribes process-global, multicast hooks
  (`AppDomain.CurrentDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException`) on a new handler
  without removing the previous → accumulating live subscribers rooting disposed providers.
- **Fix:** make `RegisterProcessHooks` **idempotent** — either unsubscribe before subscribing (store the
  delegate and `-=` then `+=`), or guard with a `static bool _processHooksRegistered` so the process-global
  hooks are wired **once per process** (the per-provider `GlobalExceptionHandler` can forward to the current
  provider via a swappable field rather than re-subscribing). The self-unsubscribing `SignedOut` handler is
  fine; only the process-global hooks need the guard.
  1. **Investigate (small):** `grep -rn "RegisterProcessHooks\|UnhandledException\|UnobservedTaskException\|AppDomain" src/Animora.Desktop | head` — confirm what `RegisterProcessHooks` subscribes.
- **Acceptance:** repeated sign-in/out does not accumulate process-global subscriptions; a crash fires the
  hook once; old providers are not rooted by stale delegates. Reason through the register path.

## [ ] T53 · UI-04 — Reparent cached View instead of uncached rebuild (Low, perf)
- **File:** `src/Animora.Desktop/Shell/ViewCache.cs` — `GetOrCreate`:
  ```csharp
  if (cached.Parent is null) return cached;
  return factory();   // fresh, uncached — rebuilds XAML when the cached View is still parented
  ```
- **Problem:** when the same VM's cached View is currently parented (e.g. a sheet re-opened mid-animation, or
  a VM in both a drawer and the page), the second request rebuilds the whole visual tree and does not cache
  it. Rare under the single-host invariant, but pure wasted work when it happens.
- **Fix (minimal, respect the single-host invariant):** when the cached View is still parented, **detach it
  from its old parent** (clear the old parent's content/child) and return the cached instance, rather than
  rebuilding — so the one cached View moves to the new host. Only do this if the shell truly hosts one at a
  time (it does per UI-06); guard against detaching a View that must remain in its current host. If detaching
  is unsafe in some host, keep the rebuild but **cache the rebuilt instance** so repeats are cheap.
  1. **Investigate (small):** `grep -n "Parent\|GetOrCreate\|ConditionalWeakTable\|factory\|Detach\|Content" src/Animora.Desktop/Shell/ViewCache.cs` — understand the cache structure and parenting.
- **Acceptance:** re-requesting a parented VM's View no longer rebuilds XAML every time (either reparented or
  the rebuild is cached); the normal single-host navigation path is unchanged. Visual correctness preserved.

## [ ] T54 · UI-05 — Correct stale "mock" comments in the composition root (Low, doc inconsistency)
- **File:** `src/Animora.Desktop/App.axaml.cs` `ConfigureServices` — comments still call real services
  "mock"/"in-memory": l.453 ("instead of the in-memory mock"), l.485, l.512, l.527 ("so a singleton
  preserves mutations"), l.533, l.545 ("Profile … mock persists edits in-memory only") — while the
  registrations bind the **real** `InventoryQueryService`/`CashierService`/`HistoryQueryService`/
  `UserProfileService` over the local backend (scope-per-call, stateless).
- **Problem:** comments contradict the registrations; a maintainer could wrongly assume a singleton preserves
  data, or hesitate to change a lifetime justified on false grounds. Pure doc hazard.
- **Fix:** **comment-only** edits. Correct each stale comment to describe the real service (e.g. "real
  `CashierService` over the local backend; stateless, scope-per-call — the singleton holds no data"). Remove
  the "preserves mutations/in-memory mock" rationales. Do **not** change any registration or lifetime.
  Re-read the surrounding lines to match each comment to its actual registration.
- **Acceptance:** no composition-root comment describes a real service as a mock/in-memory store; no code or
  lifetime change (`git diff` shows only comment lines).

> **UI-06** is Info/verification only — **no task.**

---

# Final

## [ ] T55 · Delete this plan file
- **Only after every task above is `[x]` and pushed.** This is the closing task, mirroring the 01–06
  lifecycle (prior plan deleted in `7199d662`).
- **Do:** `git rm Plan/TODO-phase-07-12-remediation.md` then
  `scripts/git/push.sh "chore(plan): T55 remove completed 07-12 remediation plan"`.
- **Scope guard:** delete **only** this file. Do **not** touch `Plan/reports/**`, roadmap files, or any
  other doc. The `Plan/reports/` audit set stays as the permanent record.
- **Acceptance:** `Plan/TODO-phase-07-12-remediation.md` no longer exists; `Plan/reports/**` untouched;
  push succeeds.
