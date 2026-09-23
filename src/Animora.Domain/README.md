# Animora.Domain

Entities, value objects, invariants and domain events. **Zero dependencies**: this project
has no `PackageReference` and no `ProjectReference` — not even to `Animora.Contracts` — and
is enforced by `LayeringRulesTests.Domain_HasNoForbiddenReferences` (CONVENTIONS.md §2).

## Folder layout

| Folder | Contents |
|---|---|
| `Primitives/` | `SyncableEntity`, `LocalOnlyEntity`, `LocalOnlyAttribute`, `AggregateRoot`, `IHasDomainEvents`, `IdGenerator` |
| `ValueObjects/` | `Money`, `CurrencyScale`, `Quantity` |
| `Events/` | `DomainEvent` (plain abstract record — no MediatR, no `INotification`) |
| `Results/` | `ErrorCode`, `ErrorCodes` (central catalog), `Result`, `Result<T>` |
| `Time/` | `IClock`, `IMonotonicClock` — ports only; implementations live in `Animora.Infrastructure.Local` (A11) |
| `Identity/` | Tenant / User / Role / Device entities — filled in by A03 |

## Standing rules inside this project

- **No `Guid`.** Ids are 26-char Crockford base32 ULIDs from `IdGenerator` (CONVENTIONS.md §4).
- **No direct system-clock read** in domain logic — take the instant from `IClock`.
- **No `decimal` money.** `Money` is `long Minor` + ISO currency code (STACK.md §3.3, R-B24).
  `Money.ApplyPercent` (`MidpointRounding.AwayFromZero`) is the product's only money-rounding site;
  `Money.Allocate` is the only split, and it conserves every minor unit.
- **`UpdatedAt` / `Version` are transport bookkeeping only** — a delta cursor and an opaque
  concurrency token, never a business sequence (A-RULE 009 / 010).
- **Wall-clock time is never an authority.** `IMonotonicClock.HighWaterUtc` only ever increases
  (A-RULE 011).
- **No exceptions for expected control flow** — boundaries return `Result` / `Result<T>` with an
  `ErrorCode`. `ErrorCodes` is append-only: later phases add codes, never rename or remove one.
- Value objects are `readonly record struct`; entities are `class` with identity; `sealed` by default.
