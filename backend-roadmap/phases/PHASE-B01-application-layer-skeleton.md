# Phase B01 — Application layer skeleton

**Band:** M · **Track:** B · **Depends on:** A11 · **Satisfies:** R-B08, R-B10
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §3, §4, §8, §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §5 (component view), §11.3 (read vs write models)
- `PERFORMANCE.md` §3, §4, §12
- `src/Animora.Application/Ports/*`, `src/Animora.Application/Abstractions/*`
- `src/Animora.Domain/Results/Result.cs`, `src/Animora.Domain/Results/ErrorCodes.cs`

## Goal
Establish the use-case layer: command/query abstractions, a reflection-free dispatcher, a fixed
behavior pipeline (validation → authorization hook → transaction → correlation), the error mapping
policy, and `AddAnimoraApplication()`. No business handler is written here — this phase ships the
machine plus one reference handler pair used by the tests.

## Design decisions (pre-made — implement, do not re-derive)
- **Folders:** `src/Animora.Application/` → `Abstractions/`, `Behaviors/`, `Dispatch/`,
  `Validation/`, `Security/`, `Correlation/`, `Ports/`, `UseCases/<Context>/`.
- `Abstractions/ICommand.cs` — `interface ICommand<TResult>` (marker; `TResult` is the payload inside
  `Result<TResult>`). `Abstractions/IQuery.cs` — `interface IQuery<TResult>`.
  Commands are `sealed record`s named `<Verb><Aggregate>Command`; queries `<Subject>Query`.
- `Abstractions/ICommandHandler.cs` —
  `interface ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>`:
  `Task<Result<TResult>> HandleAsync(TCommand command, CommandContext context, CancellationToken ct)`.
  `Abstractions/IQueryHandler.cs` — `interface IQueryHandler<TQuery, TResult>`:
  `Task<Result<TResult>> HandleAsync(TQuery query, QueryContext context, CancellationToken ct)`.
- `Abstractions/CommandContext.cs` — `sealed record CommandContext(string TenantId, string UserId,
  string DeviceId, string OperationId, string CorrelationId, string? CausationId,
  string? DependsOnOperationId, long? ExpectedAggregateVersion, DateTimeOffset CreatedAtUtc)`.
  `Abstractions/QueryContext.cs` — `sealed record QueryContext(string TenantId, string UserId,
  string CorrelationId)`. These are the **only** carriers of ambient identity into a handler; no
  handler reads a static/current-user singleton.
- `Correlation/ICorrelationContext.cs` + `Correlation/CorrelationContext.cs` — an `AsyncLocal<string>`
  holding the current `CorrelationId` (`ULID`), with `IDisposable Begin(string? id = null)`.
  `Correlation/OperationIdFactory.cs` — `string New()` = `IdGenerator.NewId()`; the single source of
  `OperationId`s client-side (A-RULE 005).
- `Abstractions/IDispatcher.cs`:
  ```
  Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CommandContext ctx, CancellationToken ct = default);
  Task<Result<TResult>> QueryAsync<TResult>(IQuery<TResult> query, QueryContext ctx, CancellationToken ct = default);
  ```
- `Dispatch/HandlerRegistry.cs` — the reflection-free core (PERFORMANCE.md §3):
  a `FrozenDictionary<Type, HandlerEntry>` built at registration time, where
  `HandlerEntry` holds a pre-compiled
  `Func<IServiceProvider, object, object, CancellationToken, Task<object>>` closure produced by a
  generic local function — **no `MethodInfo.Invoke`, no assembly scanning, no
  `Activator.CreateInstance` at call time**. Registration API:
  `ApplicationRegistrationBuilder.AddCommand<TCommand, TResult, THandler>()` /
  `.AddQuery<TQuery, TResult, THandler>()`, called explicitly once per handler.
  A missing handler is a **startup** failure (`RegistryValidation.Validate()` runs in
  `AddAnimoraApplication`), never a runtime surprise.
- `Dispatch/Dispatcher.cs` — `sealed class Dispatcher : IDispatcher`: resolves the entry, then runs the
  **fixed behavior chain in this exact order**:
  1. `CorrelationBehavior` — begins/propagates `CorrelationId`; adds it to every returned failure's
     `Detail` prefix.
  2. `ValidationBehavior` — runs the registered `IValidator<T>`s; on failure returns
     `VALIDATION_FAILED` with a `ValidationFailure[]` payload. **Never throws.**
  3. `AuthorizationBehavior` — calls `IAuthorizationGate.EvaluateAsync(command/query, ctx)`; the
     default `AllowAllAuthorizationGate` is registered now and **replaced by B04**. Failure →
     `PERMISSION_DENIED`.
  4. `EntitlementBehavior` — calls `IEntitlementGate.EvaluateAsync(...)`; default
     `AllowAllEntitlementGate`, replaced by E07. Failure → `ENTITLEMENT_REQUIRED`.
     (Three separate behaviors = the three security planes never collapse — A-RULE 023.)
  5. `TransactionBehavior` — **commands only**: wraps the handler in `IUnitOfWork.ExecuteAsync`.
     Queries bypass it entirely.
  6. `ExceptionMappingBehavior` — the outermost catch: maps `DbUpdateConcurrencyException` →
     `CONCURRENCY_CONFLICT`, `SqliteException`/`IOException` → `STORE_UNAVAILABLE`,
     `OperationCanceledException` → rethrow, everything else → `UNEXPECTED` with the exception
     logged once (never the stack trace in `Detail`).
  The chain is built **once** per handler entry at registration (an array of delegates), not composed
  per call.
- `Validation/IValidator.cs` — `interface IValidator<T>`:
  `ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(T instance, CancellationToken ct)`.
  `Validation/ValidationFailure.cs` — `sealed record ValidationFailure(string Field, string Code)`
  (`Code` is a machine code; the Desktop maps it to Persian text — never a localized string here).
  `Validation/Rules.cs` — allocation-light static guards: `Required`, `MaxLength`, `Range`,
  `PositiveMoney`, `NonNegativeQuantity`, `UlidFormat`, `NotFutureUtc`, `ValidEnum<TEnum>`.
  **No FluentValidation package** (not in STACK.md §11).
- `Security/IAuthorizationGate.cs`, `Security/IEntitlementGate.cs`,
  `Security/AllowAllAuthorizationGate.cs`, `Security/AllowAllEntitlementGate.cs`,
  `Security/ICurrentUserProvider.cs` — `Task<Result<CommandContext>> BuildCommandContextAsync(
  string? operationId, string? dependsOn, long? expectedVersion, CancellationToken ct)` and
  `Result<QueryContext> BuildQueryContext()`. A stub `SingleUserProvider` (reads
  `LocalInstallation.TenantId`, a fixed local user id `"U-LOCAL-OWNER"`, device id from
  `InstallationIdentity`) is registered now and **replaced by B03**.
- `UseCases/Diagnostics/PingQuery.cs` + `PingQueryHandler.cs` — the reference pair
  (`sealed record PingQuery : IQuery<string>`), used by tests to prove the chain works end to end
  without touching business behavior.
- `ApplicationServiceCollectionExtensions.cs` — `AddAnimoraApplication(this IServiceCollection,
  Action<ApplicationRegistrationBuilder>? configure = null)`: registers the correlation context,
  the four gates' defaults, `IDispatcher`, the registry (singleton, frozen), the validators found via
  explicit registration, then calls `RegistryValidation.Validate()` and throws on any gap.
- **Error-mapping policy (single table, referenced by every later phase):**
  `Abstractions/ErrorMapping.cs` — `static ErrorCode FromException(Exception)` implementing exactly
  the `ExceptionMappingBehavior` table above. No other file may catch a broad `Exception`.
- **New error codes:** `HANDLER_NOT_REGISTERED`, `VALIDATION_FAILED` (exists), `DISPATCH_FAILED`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Abstractions/ICommand.cs` | create | command/query markers |
| `src/Animora.Application/Abstractions/ICommandHandler.cs` | create | command handler contract |
| `src/Animora.Application/Abstractions/IQueryHandler.cs` | create | query handler contract |
| `src/Animora.Application/Abstractions/CommandContext.cs` | create | ambient command identity record |
| `src/Animora.Application/Abstractions/QueryContext.cs` | create | ambient query identity record |
| `src/Animora.Application/Abstractions/IDispatcher.cs` | create | send/query façade |
| `src/Animora.Application/Abstractions/ErrorMapping.cs` | create | exception → `ErrorCode` table |
| `src/Animora.Application/Dispatch/HandlerRegistry.cs` | create | frozen delegate map + builder |
| `src/Animora.Application/Dispatch/Dispatcher.cs` | create | fixed 6-step behavior chain |
| `src/Animora.Application/Dispatch/RegistryValidation.cs` | create | startup completeness check |
| `src/Animora.Application/Behaviors/*.cs` | create | the 6 behaviors, one file each |
| `src/Animora.Application/Validation/IValidator.cs` | create | validator port |
| `src/Animora.Application/Validation/ValidationFailure.cs` | create | field + machine code |
| `src/Animora.Application/Validation/Rules.cs` | create | 8 allocation-light guards |
| `src/Animora.Application/Security/IAuthorizationGate.cs` | create | authorization plane port |
| `src/Animora.Application/Security/IEntitlementGate.cs` | create | entitlement plane port |
| `src/Animora.Application/Security/AllowAllAuthorizationGate.cs` | create | default, replaced in B04 |
| `src/Animora.Application/Security/AllowAllEntitlementGate.cs` | create | default, replaced in E07 |
| `src/Animora.Application/Security/ICurrentUserProvider.cs` | create | context builder port |
| `src/Animora.Application/Security/SingleUserProvider.cs` | create | local stub, replaced in B03 |
| `src/Animora.Application/Correlation/ICorrelationContext.cs` | create | correlation port |
| `src/Animora.Application/Correlation/CorrelationContext.cs` | create | `AsyncLocal` implementation |
| `src/Animora.Application/Correlation/OperationIdFactory.cs` | create | the only `OperationId` source |
| `src/Animora.Application/UseCases/Diagnostics/PingQuery.cs` | create | reference query |
| `src/Animora.Application/UseCases/Diagnostics/PingQueryHandler.cs` | create | reference handler |
| `src/Animora.Application/ApplicationServiceCollectionExtensions.cs` | create | `AddAnimoraApplication()` |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 2 codes |
| `tests/Animora.Application.Tests/DispatcherTests.cs` | create | chain order, ping round-trip, missing handler |
| `tests/Animora.Application.Tests/BehaviorPipelineTests.cs` | create | each behavior's short-circuit + order proof |
| `tests/Animora.Application.Tests/ValidationRulesTests.cs` | create | all 8 guards, success + failure |
| `tests/Animora.Application.Tests/NoReflectionTests.cs` | create | dispatch path allocates no `MethodInfo`; registry is frozen |

## Implementation steps
1. Add the abstractions (`ICommand`, `IQuery`, handlers, contexts, `IDispatcher`, `ErrorMapping`).
2. Add correlation + `OperationIdFactory`.
3. Add the validation primitives and `Rules`.
4. Add the three security ports plus their allow-all defaults and `SingleUserProvider`.
5. Implement `HandlerRegistry` + `ApplicationRegistrationBuilder`: each `AddCommand`/`AddQuery` call
   compiles its invoker closure once and stores it; the dictionary is frozen in
   `AddAnimoraApplication`.
6. Implement the six behaviors as separate classes, then `Dispatcher` composing them **at
   registration time** into a delegate array per entry.
7. Implement `RegistryValidation.Validate()`: every registered command/query type has exactly one
   handler entry; every handler's `TResult` matches its contract.
8. Add the `PingQuery` pair and `AddAnimoraApplication()`.
9. Write the four test classes. `BehaviorPipelineTests` records the order into a list via test doubles
   and asserts the exact sequence `Correlation → Validation → Authorization → Entitlement →
   Transaction → Handler`; it also asserts `TransactionBehavior` is absent for queries.
10. `NoReflectionTests` asserts `HandlerRegistry` exposes a `FrozenDictionary` and that no type in
    `Animora.Application/Dispatch` contains a `MethodInfo`/`Activator` reference
    (source-level grep assertion via `Assembly` metadata is acceptable: assert no `MethodInfo` field
    exists on the registry types).
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- `ICommand<TResult>`, `IQuery<TResult>`, `ICommandHandler<,>`, `IQueryHandler<,>`,
  `CommandContext`, `QueryContext`, `IDispatcher`, `ErrorMapping`.
- `HandlerRegistry`, `ApplicationRegistrationBuilder`, `RegistryValidation`, `Dispatcher`,
  the 6 behaviors.
- `IValidator<T>`, `ValidationFailure`, `Rules`.
- `IAuthorizationGate`, `IEntitlementGate`, `ICurrentUserProvider` + the three defaults.
- `ICorrelationContext`, `CorrelationContext`, `OperationIdFactory`.
- `PingQuery`/`PingQueryHandler`, `AddAnimoraApplication()`. `ErrorCodes` += 2.
- No migration, no `Animora.Contracts` change, no DI change in `App.axaml.cs`.

## Rules that apply here
- **PERFORMANCE.md §3** — dispatch is a frozen delegate map; no reflection, no scanning at call time.
- **A-RULE 023** — authorization and entitlement are **separate** behaviors, evaluated in order, never
  merged into one gate.
- **A-RULE 005** — `OperationIdFactory` is the single client-side `OperationId` source; `CommandContext`
  always carries one.
- **CONVENTIONS §4** — handlers return `Result`/`Result<T>`; exceptions only for defects, mapped once
  in `ExceptionMappingBehavior`.
- **CONVENTIONS §2** — `Animora.Application` gains no EF Core or Datasync reference; the transaction
  behavior depends only on `IUnitOfWork`.
- **PERFORMANCE.md §4** — every handler signature is async with a `CancellationToken`; no sync-over-
  async anywhere in the chain.

## Out of scope
- `PendingCommand`, idempotency, settlement states, local outbox → B02.
- Real `ICurrentUserProvider`/login → B03. Real authorization gate → B04. Real entitlement gate → E07.
- Any business command or query handler → B02 onward and Track C.
- Audit writing and structured logging → B05.
- Registering `AddAnimoraApplication()` in the desktop composition root → C01.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `DispatcherTests` green: `PingQuery` round-trips through the chain; an unregistered command returns
  `HANDLER_NOT_REGISTERED`; `AddAnimoraApplication` throws at startup when a registered command has no
  handler.
- `BehaviorPipelineTests` green: the recorded order is exactly
  `Correlation → Validation → Authorization → Entitlement → Transaction → Handler`; a failing validator
  short-circuits with `VALIDATION_FAILED` and the handler is never invoked; a denying authorization gate
  returns `PERMISSION_DENIED` before the entitlement gate runs; queries never open a transaction.
- `ValidationRulesTests` green for all 8 guards.
- `NoReflectionTests` green: the registry type exposes a `FrozenDictionary` field and no
  `MethodInfo`/`Activator` member exists in `Animora.Application.Dispatch`.
- A thrown `SqliteException` inside a handler surfaces as `Result.Fail(STORE_UNAVAILABLE)`, not an
  exception escaping `SendAsync`.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`; the desktop is
  untouched.

## PROGRESS.md update
Mark B01 done; advance "Next phase" to `B02`; append a "Repo state" entry listing: the abstractions,
`HandlerRegistry` + builder API, the fixed 6-behavior chain and its order, the validation primitives,
the three security-plane ports and their temporary defaults (and which phase replaces each),
correlation + `OperationIdFactory`, `AddAnimoraApplication()`, `ErrorCodes` += 2, the 4 test classes,
no DI change, no mock deleted, no migration, deviations.
