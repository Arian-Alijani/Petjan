# Phase A02 — Domain kernel

**Band:** S · **Track:** A · **Depends on:** A01 · **Satisfies:** R-B10, R-B24
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §6 (items 3, 11), §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §8.2 (syncable shape), §11.1 (money/ledger), §21 (A-RULE 009, 010, 011)
- `STACK.md` §3.3 (money = integer minor units; `DateTimeOffset` limitation), §15 (T-RULE 006)
- `src/Animora.Domain/Animora.Domain.csproj`, `src/Animora.Contracts.Wire/WireContract.cs`

## Goal
Create the domain primitives every later domain, persistence, application and server phase builds on:
entity bases with the syncable shape, `Money`/`Quantity` value objects, ULID id generation, the clock
abstractions, the `Result`/`ErrorCode` catalog and the domain-event base. No business entity yet.

## Design decisions (pre-made — implement, do not re-derive)
- **Folder layout inside `src/Animora.Domain/`:** `Primitives/`, `ValueObjects/`, `Events/`, `Results/`,
  `Time/`, `Identity/` (empty until A03).
- `Primitives/SyncableEntity.cs` — `public abstract class SyncableEntity`:
  `string Id` (init-settable, ULID), `string TenantId`, `DateTimeOffset UpdatedAt`,
  `string? Version` (opaque, server-issued; null until first server write), `bool Deleted`.
  Protected parameterless ctor for EF. **No EF attributes** (mapping lives in configurations).
  Add XML docs stating `UpdatedAt`/`Version` are transport bookkeeping only (A-RULE 009/010).
- `Primitives/LocalOnlyEntity.cs` — `public abstract class LocalOnlyEntity`: `string Id`,
  `DateTimeOffset CreatedAtUtc`. Never replicated.
- `Primitives/LocalOnlyAttribute.cs` — `[AttributeUsage(AttributeTargets.Class)] public sealed class
  LocalOnlyAttribute : Attribute`. Marker consumed by A09's model builder and E03's sync scope.
- `Primitives/IHasDomainEvents.cs` — `IReadOnlyList<DomainEvent> DomainEvents { get; }`,
  `void ClearDomainEvents()`. `Primitives/AggregateRoot.cs` — `abstract class AggregateRoot :
  SyncableEntity, IHasDomainEvents` with a `private readonly List<DomainEvent> _events` and a
  `protected void Raise(DomainEvent e)`.
- `Events/DomainEvent.cs` — `public abstract record DomainEvent(string EventId, string TenantId,
  DateTimeOffset OccurredAtUtc)`. `EventId` is a ULID. No `INotification`, no MediatR.
- `ValueObjects/Money.cs` — `public readonly record struct Money(long Minor, string Currency)`:
  - `Currency` is a 3-letter ISO code; the product default is `"IRT"` with **scale 0**
    (`Money.Irt(long toman)` factory).
  - `public static readonly Money ZeroIrt`.
  - Operators `+`, `-`, unary `-`, `*` (by `int` and by `long`), comparison operators, `CompareTo`.
  - Every binary operator throws `InvalidOperationException` on a currency mismatch (a defect, not
    expected control flow).
  - `Money.Allocate(int parts)` → `IReadOnlyList<Money>` distributing remainder minor units to the
    first parts (largest-remainder), so split/discount math never loses or invents a unit.
  - `Money.ApplyPercent(decimal percent)` rounds with `MidpointRounding.AwayFromZero` on the minor
    unit — the single rounding policy for the whole product.
  - `CurrencyScale.Of(string currency)` static helper: `IRT` → 0, `USD`/`EUR` → 2, default 2.
- `ValueObjects/Quantity.cs` — `public readonly record struct Quantity(decimal Value, string UnitCode)`:
  non-negative for stock on-hand contexts is **not** enforced here (movements may be negative);
  operators `+`/`-` with unit-code equality check; `Quantity.Zero(string unitCode)`.
- `Primitives/IdGenerator.cs` — `public static class IdGenerator`:
  `static string NewId()` returns a **26-char Crockford base32 ULID** (48-bit ms timestamp +
  80-bit randomness from `RandomNumberGenerator`), monotonic within the same millisecond by
  incrementing the random component under a lock. `static string NewId(DateTimeOffset at)` for tests.
  Ids are lexicographically sortable — this is the only id generator in the product (CONVENTIONS §4).
- `Time/IClock.cs` — `DateTimeOffset UtcNow { get; }`.
  `Time/IMonotonicClock.cs` — `long ElapsedTicks { get; }` plus
  `DateTimeOffset HighWaterUtc { get; }` and `void Observe(DateTimeOffset serverUtc)`: the
  high-water value never decreases, which is what defeats clock rollback (A-RULE 011; used by D11/E07).
  Implementations live in `Animora.Infrastructure.Local` (A11) — **not** in Domain.
- `Results/ErrorCode.cs` — `public readonly record struct ErrorCode(string Code, string Category)`;
  categories are the const strings `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Integrity`,
  `Infrastructure`, `Entitlement`.
- `Results/ErrorCodes.cs` — the **central catalog**: one `public static readonly ErrorCode` per code,
  grouped by region comment per bounded context. Seed it with the cross-cutting codes only:
  `VALIDATION_FAILED`, `NOT_FOUND`, `CONCURRENCY_CONFLICT`, `PERMISSION_DENIED`,
  `TENANT_MISMATCH`, `IDEMPOTENCY_FINGERPRINT_MISMATCH`, `DEPENDENCY_UNSETTLED`,
  `ENTITLEMENT_REQUIRED`, `OFFLINE_CAPABILITY_EXPIRED`, `STORE_UNAVAILABLE`, `UNEXPECTED`.
  Later phases **add** codes to this file; no phase renames or removes one.
- `Results/Result.cs` — `public readonly record struct Result` and
  `public readonly record struct Result<T>`: `bool IsSuccess`, `T? Value`, `ErrorCode Error`,
  `string? Detail` (never a user-facing localized string — the Desktop maps codes to Persian text).
  Factories `Result.Ok()`, `Result.Fail(ErrorCode, string? detail)`, `Result<T>.Ok(T)`,
  `Result<T>.Fail(...)`, implicit conversion from `T` to `Result<T>`. Plus `Map`/`Bind` helpers.
- **No `Guid`, no `DateTime.Now`, no `decimal` money anywhere in this project.**

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Primitives/SyncableEntity.cs` | create | syncable shape base |
| `src/Animora.Domain/Primitives/LocalOnlyEntity.cs` | create | local-only base |
| `src/Animora.Domain/Primitives/LocalOnlyAttribute.cs` | create | `[LocalOnly]` marker |
| `src/Animora.Domain/Primitives/AggregateRoot.cs` | create | syncable aggregate + event buffer |
| `src/Animora.Domain/Primitives/IHasDomainEvents.cs` | create | event-buffer contract |
| `src/Animora.Domain/Primitives/IdGenerator.cs` | create | monotonic ULID generator |
| `src/Animora.Domain/Events/DomainEvent.cs` | create | abstract domain-event record |
| `src/Animora.Domain/ValueObjects/Money.cs` | create | minor-unit money + operators + Allocate/ApplyPercent |
| `src/Animora.Domain/ValueObjects/CurrencyScale.cs` | create | currency → minor-unit scale |
| `src/Animora.Domain/ValueObjects/Quantity.cs` | create | decimal quantity + unit code |
| `src/Animora.Domain/Time/IClock.cs` | create | UTC clock port |
| `src/Animora.Domain/Time/IMonotonicClock.cs` | create | monotonic + high-water clock port |
| `src/Animora.Domain/Results/ErrorCode.cs` | create | code + category record struct |
| `src/Animora.Domain/Results/ErrorCodes.cs` | create | central catalog, 11 cross-cutting codes |
| `src/Animora.Domain/Results/Result.cs` | create | `Result` / `Result<T>` + Map/Bind |
| `src/Animora.Domain/README.md` | modify | state the folder layout + "zero dependencies" rule |
| `tests/Animora.Domain.Tests/MoneyTests.cs` | create | arithmetic, currency mismatch, Allocate, ApplyPercent rounding |
| `tests/Animora.Domain.Tests/QuantityTests.cs` | create | add/sub, unit mismatch |
| `tests/Animora.Domain.Tests/IdGeneratorTests.cs` | create | length 26, sortability, same-ms monotonicity, uniqueness |
| `tests/Animora.Domain.Tests/ResultTests.cs` | create | Ok/Fail, implicit conversion, Map/Bind |

## Implementation steps
1. Create the five folders and `SyncableEntity`/`LocalOnlyEntity`/`LocalOnlyAttribute`.
2. Add `DomainEvent`, `IHasDomainEvents`, `AggregateRoot` (event list is never exposed as mutable).
3. Implement `IdGenerator` (Crockford base32 alphabet `0123456789ABCDEFGHJKMNPQRSTVWXYZ`, 10 chars
   timestamp + 16 chars randomness, lock-guarded monotonic increment for same-ms calls).
4. Implement `CurrencyScale`, then `Money` with all operators, `Allocate`, `ApplyPercent`.
5. Implement `Quantity`.
6. Add `IClock` and `IMonotonicClock` (interfaces only — implementations are A11).
7. Add `ErrorCode`, `ErrorCodes` (11 codes), `Result`/`Result<T>`.
8. Write the four test classes. `MoneyTests` must cover: `Allocate(3)` of 100 IRT → `34/33/33` summing
   exactly to 100; `ApplyPercent(9m)` rounding away from zero; mismatch throwing.
   `IdGeneratorTests` must generate 10 000 ids in a loop and assert strict ascending order and zero
   duplicates.
9. Update `src/Animora.Domain/README.md`.
10. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/test.sh` = `TESTS OK`;
    `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Domain primitives: `SyncableEntity`, `LocalOnlyEntity`, `LocalOnlyAttribute`, `AggregateRoot`,
  `IHasDomainEvents`, `DomainEvent`, `IdGenerator`.
- Value objects: `Money`, `Quantity`, `CurrencyScale`.
- Ports: `IClock`, `IMonotonicClock`.
- Results: `ErrorCode`, `ErrorCodes` (11 codes), `Result`, `Result<T>`.
- No DbContext, no migration, no `Animora.Contracts` change.

## Rules that apply here
- **A-RULE 009 / A-RULE 010** — XML docs on `UpdatedAt`/`Version` must state they are cursor/concurrency
  metadata only, never a business sequence.
- **A-RULE 011** — `IMonotonicClock.HighWaterUtc` is monotonic by contract; document that wall-clock
  time is never a licensing/sync authority.
- **T-RULE 006** — do not name anything `DatasyncOperationId` or repurpose library vocabulary.
- **STACK.md §3.3 / R-B24** — money is `long Minor` + currency; no `decimal` money field exists.
- **CONVENTIONS §2** — `Animora.Domain` gains **zero** package or project references in this phase.
- **CONVENTIONS §4** — `readonly record struct` for value objects; `sealed`/`abstract` as specified.

## Out of scope
- Tenant/User/Role/Device entities → A03.
- Any clinical, scheduling, inventory, sales or finance entity → A04–A08.
- `IClock`/`IMonotonicClock` implementations and DI registration → A11.
- EF value converters for `Money`/`DateTimeOffset` → A09.
- Command envelope and settlement states → B02.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `Animora.Domain.csproj` contains no `PackageReference` and no `ProjectReference`
  (`LayeringRulesTests` still green).
- `grep -rn "DateTime.Now\|DateTimeOffset.UtcNow\|Guid.NewGuid" src/Animora.Domain` returns nothing.
- `MoneyTests`, `QuantityTests`, `IdGeneratorTests`, `ResultTests` all green; the 10 000-id
  monotonicity test passes.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`, desktop unchanged.

## PROGRESS.md update
Mark A02 done; advance "Next phase" to `A03`; append a "Repo state" entry listing: the 15 domain kernel
files, the `ErrorCodes` catalog seed (11 codes), the 4 test classes, no DI change, no mock deleted, no
migration created, deviations.
