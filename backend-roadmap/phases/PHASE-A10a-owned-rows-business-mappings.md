# Phase A10a — Business mappings + relational owned rows (session 1/4 of A10)

**Track:** A · **Depends on:** A09 ✅ · **Next:** A10b · Split of original A10 (A10a→A10d, one session each).

## Session rules (read first, keep token use low)
- Read ONLY this file + the files named under "Read only". Do not open other phase files, the UI, mocks, or tests.
- Rules: `backend-roadmap/CONVENTIONS.md` §§2, 4, 5 only. If the actual domain shape differs from text here, follow the domain shape; never invent properties.
- **No tests, do not run `scripts/sandbox/test.sh`** (Track A A05–A11 policy; owed to A12).
- **No migration in this session** (single migration `002_LocalBusinessSchema` is generated in A10d). Never touch `Migrations/*001_LocalCoreSchema*`.
- No edits to Desktop, `App.axaml.cs`, mocks, `Animora.Contracts`. dotnet not on PATH → `scripts/sandbox/AI_GUIDE.md`.

## Starting state (do not rediscover)
.NET 10 / EF Core 10 SQLite. A09: `AnimoraLocalDbContext`, named Tenant/SoftDelete filters, default NoTracking, UTC-tick dates, Money = INTEGER minor + TEXT(3) currency, Quantity = invariant decimal TEXT, local `Version`. PR #123 already added 15 business configs in `src/Animora.Persistence.Sqlite/Configurations/{Inventory,Purchasing,Sales,Cashier,Finance}/*.cs` and `Conventions/OwnedRows.cs` — **repair, do not recreate**. Defects to fix here: `OwnedRows.Map` uses reflection + JSON converters for Money/Quantity (`OwnedRows.cs:21-30`); owned types miss scalar conventions because `ModelConventions.Apply` skips owned types (`ModelConventions.cs:18`); some indexes missing/not unique.

## Read only
`Conventions/OwnedRows.cs`, `Conventions/ModelConventions.cs`, `Converters/*`, the 15 business config files; open a domain type only when a compiler/model error needs its constructor/properties.

## Tasks
1. **Owned rows → real relational tables.** Map `ProductVariant, PurchaseOrderLine, GoodsReceiptLine, InvoiceLine, InvoiceAdjustment, CashMovement, JournalLine, ChequeEvent` to tables `ProductVariants, PurchaseOrderLines, GoodsReceiptLines, InvoiceLines, InvoiceAdjustments, CashMovements, JournalLines, ChequeEvents`: stable composite owner/child keys, field-backed navigations, children loaded when the aggregate is tracked, each value an individually queryable/indexable column. No reflection-driven mapping.
2. **Exact value columns in owned rows.** Replace JSON converters: every Money → `<Name>Minor INTEGER` + `<Name>Currency TEXT(3)`; every Quantity → unit column + exact value column. Apply UTC-ticks, bounded strings and int enums to owned rows explicitly (write a small `ApplyOwnedScalarConventions` helper used by OwnedRows). No REAL/NUMERIC for money. A09's JSON child workaround is historical — not allowed for A10 rows.
3. **Summable quantity.** For `StockMovement` and every other quantity column that is SQL-summed, store Quantity.Value as exact **INTEGER fixed-scale (×1000, 3 dp)** with a converter to/from `Quantity.Value` (add under `Converters/`). Never SQLite `SUM` over decimal TEXT.
4. **Materialization.** Immutable positional child records with Money/Quantity ctor args need a real EF strategy (owned complex/value mapping, field ctor binding). If EF cannot bind, make only the minimal compatible domain adjustment (e.g. private parameterless ctor + private setters) keeping invariants intact.
5. **Indexes** (tenant-scoped uniques, no cross-tenant owner/child joins):
   - `Finance/JournalEntryConfiguration.cs:15` → `IX_JournalEntries_TenantId_EntryNumber` **unique**.
   - `Inventory/ProductConfiguration.cs:15` → `IX_Products_TenantId_Sku` unique with `HasFilter("\"Sku\" IS NOT NULL")`.
   - Add `IX_JournalLines_AccountCode` on JournalLines.
   - `IX_ProductCategories_TenantId_Name` only if a name lookup actually exists; `IX_Suppliers_TenantId_Name` already exists.
   - Keep existing: unique invoice number, account code, period code; indexes on products, stock, POs/receipts, suppliers, invoices/payments, cash sessions, cheques, expenses.
6. `StockLevel` is never an entity/table (verify it is ignored).

## Gate
- `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- Model builds: `dotnet ef dbcontext info --project src/Animora.Persistence.Sqlite` succeeds (design-time via `Context/LocalDbContextFactory.cs`; fix the factory if needed without weakening runtime DI).
- `grep -rn "HasConversion.*Json\|JsonSerializer" src/Animora.Persistence.Sqlite/Conventions src/Animora.Persistence.Sqlite/Configurations` → nothing for A10 rows.

## Handoff
`backend-roadmap/PROGRESS.md`: set row A10a ✅ + date + one-line note (owned tables, fixed-scale quantity converter name, index changes, any domain adjustment); set Next phase → `A10b` (`phases/PHASE-A10b-*.md`). Commit `feat(store): relational owned rows and exact business mappings (A10a)`. Final answer: done / deviations / blockers only; never claim unrun builds.
