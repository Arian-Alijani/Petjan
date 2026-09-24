# Phase A10d — Read helpers, migration 002, A10 close-out (session 4/4 of A10)

**Track:** A · **Depends on:** A10c ✅ · **Next:** A11a

## Session rules
- Read ONLY this file + "Read only" files. No other phase files, UI, mocks, tests.
- Rules: `PERFORMANCE.md` §§3, 6 only if a query decision is unclear.
- **No tests / no `test.sh`.** No encryption/backup (A11), no audit (B05), no service adapters (Track C).

## Read only
`Application/Abstractions/PagedResult.cs`, `Options/LocalStoreOptions.cs`, `Context/LocalDbContextFactory.cs`, the StockMovement / Payment / Invoice / JournalEntry configurations (column names only).

## Tasks
1. `Queries/QueryPaging.cs` — `ToPagedAsync<T>` → `PagedResult<T>`: page ≥ 1 validated, pageSize clamped to `[1, min(LocalStoreOptions.MaxPageSize, 200)]`, stable `ORDER BY` required before Skip/Take, `CountAsync` + bounded `ToListAsync` (two SQL commands by design).
2. `Queries/StockOnHandQuery.cs` — one grouped SQL over tenant-filtered `StockMovement` by `(ProductId, VariantId)` (VariantId is non-null string) summing the fixed-scale INTEGER quantity (A10a) — already signed; there is no `SignedQuantity`. Returns `IReadOnlyDictionary<(string ProductId, string VariantId), decimal>`. Never persist absolute stock.
3. `Queries/InvoiceBalanceQuery.cs` — group signed `Payment.Amount.Minor` by `InvoiceId`; paid/due vs `Invoice.GrandTotal` (+ adjustments), currency + tenant checks.
4. `Queries/LedgerBalanceQuery.cs` — group by `JournalEntry.PeriodId` + `JournalLine.AccountCode`, `Side` + `Amount.Minor` → debit − credit (`SignedMinor` is a domain property, not a column).
5. All helpers: one SQL command per aggregate (no N+1, no client eval), bounded inputs, no tracking, tenant preserved; XML-doc the signatures + return DTOs for Track C.
6. **Migration:** exactly one additive `002_LocalBusinessSchema` via EF tooling (design-time `LocalDbContextFactory`) covering all A10 mappings, owned-row tables, seed/reference tables, constraints; snapshot updated. `001_LocalCoreSchema.cs` + Designer stay byte-identical (`git diff --stat` shows no change). Inspect generated SQL: money = INTEGER minor + TEXT currency, quantity sums INTEGER, owned row tables present, no JSON money.

## Gate (A10 acceptance)
- `dotnet ef migrations list` → `001_LocalCoreSchema`, `002_LocalBusinessSchema`; `migrations has-pending-model-changes` → none.
- `build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK` (desktop still on mocks).
- Do not assert SQL counts by counting `SELECT` substrings (A12 verifies real command counts).

## Handoff
PROGRESS: rows A10d ✅ **and A10 ✅**; Next → `A11a` (`phases/PHASE-A11a-*.md`); append a concise A10 "Repo state" entry (files, contracts, DI, migration 002, verification, deviations, no tests) and this **A12 test debt**: schema/owned-row tables/indexes/round trips + no JSON money; append-only update/delete protection per protected type; transaction + handler-failure rollback, exactly-once in-transaction dispatch, mapped concurrency error; stock negative adjustment, signed partial payments/refunds, ledger debit−credit each from one SQL aggregate; paging bounds/count; idempotent double seed, tenant match, 20 accounts, all permissions/5 roles, 7 units, 5 categories, 8 rooms, 1 open period, catalog values. Commit `feat(store): read helpers and 002_LocalBusinessSchema (A10d)`. Final answer: done / validation actually run / deviations / blockers.
