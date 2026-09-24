# Phase A10b — Append-only guard, repositories, atomic UoW, typed dispatcher, DI (session 2/4 of A10)

**Track:** A · **Depends on:** A10a ✅ · **Next:** A10c

## Session rules
- Read ONLY this file + "Read only" files. No other phase files, UI, mocks, tests.
- Rules: `CONVENTIONS.md` §§5, 8 only; `ARCHITECTURE.md` §§11.2–11.3 only if a decision is unclear.
- **No tests / no `test.sh`. No migration** (A10d). No outbox/command pipeline (B02), no `AuditEntry` (B05), no production event handlers.
- No edits to Desktop/`App.axaml.cs`/mocks/Contracts.

## Starting state
PR #123 added: 18 ports `src/Animora.Application/Ports/I*Repository.cs` (IOwner, IPet, IVisit, IVaccination, IAppointment, IReceptionQueue, IBoarding, IProduct, IStockMovement, IPurchasing, IInvoice, IPayment, ICashSession, ILedger, ICheque, IExpense, IPeriod, IIdentity) + 18 impls `Persistence.Sqlite/Repositories/*Repository.cs` (FindAsync/RequireAsync/Add/AddRange); `Application/Abstractions/{IUnitOfWork,IDomainEventDispatcher,PagedResult}.cs`; `Context/AppendOnlyGuard.cs` (never invoked); `Repositories/LocalUnitOfWork.cs` (non-atomic); `Repositories/DomainEventDispatcher.cs` (uses `dynamic`). DI registers none of them.

## Read only
`Context/AnimoraLocalDbContext.cs`, `Context/AppendOnlyGuard.cs`, `Context/LocalStoreServiceCollectionExtensions.cs`, `Repositories/LocalUnitOfWork.cs`, `Repositories/DomainEventDispatcher.cs`, `Application/Abstractions/*.cs`; skim ports/repos only for signatures.

## Tasks
1. **Guard.** Call `AppendOnlyGuard.Check(this)` first in `AnimoraLocalDbContext` save override(s) (`:28-45`, async + any sync path) before stamping. Protected: `JournalEntry, JournalLine, MedicalHistoryEntry, StockMovement, Payment, ChequeEvent`. Inspect tracked `Modified`/`Deleted` incl. owned entries; throw with type + key diagnostics. Allow `Added` children under a modified mutable owner; block delete/replace of existing journal lines / cheque events. Corrections = `JournalEntry.CreateReversal` / reversing stock movement. Do not weaken tenant checks, filters, stamping.
2. **Repositories.** Keep port/impl pairs in sync; no EF/IQueryable in Application; add only indispensable indexed aggregate lookups. Tracked `FindAsync` is intentional (context default NoTracking). `RequireAsync` throws on missing (internal defect). Tenant filters stay on.
3. **Atomic UoW** (`LocalUnitOfWork.cs:12-48`):
   - `SaveAsync` opens and owns a transaction if none active; never nests.
   - `ExecuteAsync<T>`: exactly one transaction covering work → save → event dispatch → handler writes → commit; rollback on failed `Result` or mapped store failure.
   - Snapshot tracked `IHasDomainEvents` before save; dispatch buffered events in deterministic order inside the transaction; persist handler additions in the same transaction; clear events only after dispatch + persist + commit succeed. Handler failure → roll back business + handler writes, keep pending events, reset change tracker consistently for retry.
   - Map `DbUpdateConcurrencyException` → `Result.Fail(ErrorCodes.ConcurrencyConflict)`, `SqliteException` → `ErrorCodes.StoreUnavailable` on save, begin and commit. Never swallow `OperationCanceledException` or programming errors.
4. **Typed dispatcher** (`DomainEventDispatcher.cs:8-23`): remove `dynamic`. Singleton map `Type → Func<IServiceProvider, DomainEvent, CancellationToken, Task<Result>>` built from explicit `IDomainEventHandler<T>` registrations (e.g. `services.AddDomainEventHandler<TEvent,THandler>()`); resolve handlers from the scoped provider in order; short-circuit on first failure; no assembly scans/reflection invoke. Must let A12 register a fake handler. Reuse existing Application event contracts.
5. **DI** in `AddAnimoraLocalStore` (`:12-31`), registered once: 18 port/impl pairs scoped, `IUnitOfWork`, `IDomainEventDispatcher`, handler map singleton. Add `IDbContextFactory<AnimoraLocalDbContext>` for short-lived background/query contexts with valid lifetimes (factory options must not capture the scoped interceptor/tenant — use a scoped factory or make interceptor/tenant dependencies singleton-safe). One connection-config source; filters, NoTracking, sensitive logging off preserved. No startup migration. (`LocalSeeder` registration is added in A10c.)

## Gate
- `build.sh full` = 0/0; `dotnet ef dbcontext info --project src/Animora.Persistence.Sqlite` succeeds.
- `grep -rn "dynamic" src/Animora.Persistence.Sqlite/Repositories` → nothing.

## Handoff
PROGRESS: row A10b ✅ + note (guard wiring, UoW semantics, dispatcher registration API name, factory lifetime choice); Next → `A10c`. Commit `feat(store): append-only guard, atomic unit of work and typed dispatcher (A10b)`. Final answer: done / deviations / blockers.
