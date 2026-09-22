# Shared Conventions — Animora Backend & Server

> Read once per session. Every phase file assumes these rules instead of restating them.
> A phase may not relax a rule here; if it needs an exception it must name it explicitly.
> Authority order on conflict: `ARCHITECTURE.md` / `STACK.md` > this file > a phase file.

## §1 Solution layout (target state; each project is created by exactly one phase)

```
src/
  Animora.Domain/                 entities, value objects, invariants, domain events. Zero deps.          [A01]
  Animora.Application/            use cases (command/query handlers), ports, validation, policies.        [A01]
  Animora.Contracts/              EXISTING desktop-facing interfaces + DTOs. Extend, never break.         [exists]
  Animora.Contracts.Wire/         versioned wire DTOs: sync contracts, command envelopes, API errors.     [A01]
  Animora.Persistence.Sqlite/     local EF Core 10 store: AnimoraLocalDbContext, configs, migrations.     [A01]
  Animora.Infrastructure.Local/   OS keystore, encryption provider, clocks, local files, crash handling.  [A01]
  Animora.Client.Services/        implements Animora.Contracts over Application + Persistence.Sqlite.     [A01]
  Animora.Sync.Abstractions/      ISyncEngine, SyncScope, SyncResult, conflict/diagnostic contracts.      [exists/E03]
  Animora.Sync.Datasync/          the ONLY project allowed to reference Datasync client types.            [E03]
  Animora.Persistence.SqlServer/  server EF Core 10 store: AnimoraServerDbContext, named filters.         [D01]
  Animora.Server/                 ASP.NET Core 10 host: Business API + /tables/* + workers + admin host.  [D01]
  Animora.Server.Licensing/       license issuance/verification, key rotation, signing.                   [D11]
  Animora.Web.Admin/              Blazor Web App operator panel, hosted by Animora.Server.                [E01]
  Animora.Desktop/                EXISTING. Only DI registrations, Login and thin VM wiring change.       [exists]
tests/
  Animora.Domain.Tests/           invariants, money math, ledger, movements.                              [A01]
  Animora.Application.Tests/      handlers, idempotency, authorization, conflict policy.                  [A01]
  Animora.Persistence.Tests/      EF mappings, migrations, filters, concurrency.                           [A01]
  Animora.Sync.Tests/             delta/paging/resume/conflict simulation against the port.                [A01]
  Animora.Server.Tests/           API contract, tenancy isolation, outbox, licensing.                      [A01]
```

Every new project: TFM `net10.0` (inherited from `Directory.Build.props`), nullable enabled, added to
`Animora.sln`, all package versions pinned centrally in `Directory.Packages.props`
(`ManagePackageVersionsCentrally=true`, exact versions, no floating ranges).
Tests: xUnit; persistence tests use `Microsoft.EntityFrameworkCore.Sqlite` against temp **file** DBs
(never `:memory:` for migration tests). Server integration tests must skip-with-reason, not fail, when
no SQL Server instance is reachable.

## §2 Layering & dependency rules

- `Animora.Domain` references **nothing** (no EF, no DI, no Contracts).
- `Animora.Application` references `Animora.Domain` + `Animora.Contracts` (DTOs only) + `Animora.Contracts.Wire`.
- Infrastructure/persistence projects reference Application/Domain — **never the reverse**.
- `Animora.Desktop` references `Animora.Contracts` + `Animora.Client.Services` (composition root only).
  **No View, ViewModel, or `.axaml` may reference EF Core, Datasync, SQL, or HTTP types.**
- Only `Animora.Sync.Datasync` may `using CommunityToolkit.Datasync.Client` (T-RULE 001, A-RULE 001).
- Only `Animora.Server` + `Animora.Persistence.SqlServer` may reference the Datasync **server** packages.
- Client projects never reference server projects and vice versa. Shared meaning travels only through
  `Animora.Domain`, `Animora.Application`, `Animora.Contracts.Wire`.
- Dependency direction is verified by a test in `Animora.Domain.Tests`
  (`LayeringRulesTests`) that asserts assembly reference sets — added in A01, extended per phase.

## §3 Naming

| Kind | Rule | Example |
|---|---|---|
| Entity | singular PascalCase noun | `Invoice`, `StockMovement` |
| Value object | singular noun, `sealed record` | `Money`, `Quantity`, `CapabilitySnapshot` |
| Command handler | `<Verb><Aggregate>CommandHandler` | `PostInvoiceCommandHandler` |
| Command | `<Verb><Aggregate>Command` | `RegisterPaymentCommand` |
| Query | `<Subject>Query` + `<Subject>QueryHandler` | `PatientDirectoryQuery` |
| EF configuration | `<Entity>Configuration` | `InvoiceConfiguration` |
| Migration | `<NNN>_<Purpose>` | `001_LocalCoreSchema` |
| Contracts impl | `<Context>Service` in `Animora.Client.Services` | `PatientsService` |
| Wire sync DTO | `<Entity>SyncContract` | `PetSyncContract` |
| Wire command DTO | `<Command>CommandV<N>` | `PostInvoiceCommandV1` |
| Port (interface) | `I<Capability>` in Application/`Ports` | `IPermissionEvaluator` |
| Repository | `I<Aggregate>Repository` / `<Aggregate>Repository` | `IInvoiceRepository` |
| Background worker | `<Purpose>Worker` | `OutboxRelayWorker` |
| Test class | `<Subject>Tests` | `MoneyTests`, `IdempotencyStoreTests` |
| Error code | `SCREAMING_SNAKE` string const in `ErrorCodes` | `INVOICE_ALREADY_POSTED` |

File-scoped namespaces. One public type per file (nested private types allowed).
Folder = namespace. Namespace root = project name.

## §4 C# style

- Nullable enabled **and honored** — no `!` suppression without an adjacent comment stating why.
- `sealed` by default on classes; `abstract` only for declared base types.
- `record` (or `sealed record`) for DTOs and value objects; `class` for entities with identity.
- Boundaries expose `IReadOnlyList<T>` / `IReadOnlyDictionary<,>`, never `List<T>`.
- Async all the way; every async public method takes `CancellationToken ct = default` and passes it on.
  No `async void`, no `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()`.
- `ConfigureAwait` policy: library projects (Domain/Application/Persistence/Infrastructure/Sync/Server)
  use `ConfigureAwait(false)`; `Animora.Client.Services` methods that return to a ViewModel do **not**
  (the Avalonia dispatcher context must be preserved for the caller).
- No reflection on hot paths (PERFORMANCE.md §3): handler dispatch uses a compile-time registered
  `Type → Func<...>` delegate map, never assembly scanning at call time.
- No exceptions for expected control flow. Application boundaries return `Result<T>` / `Result` with an
  `ErrorCode`. Exceptions are reserved for defects and infrastructure faults.
- `TimeProvider`/`IClock` injected — never `DateTime.Now`/`DateTimeOffset.UtcNow` in Domain/Application.
- Ids are `string` (ULID, 26 chars, Crockford base32, lexicographically sortable) generated by
  `IdGenerator` — never `Guid.NewGuid()` in business code.

## §5 EF Core rules (both stores)

- EF Core **10** only. EF Core 11 must never resolve in the graph (T-RULE 003).
- Lazy loading is off; no proxies. Reads use `AsNoTracking()` + explicit `Select` projections.
- Named query filters, always two separable filters where both apply:
  `"Tenant"` and `"SoftDelete"` — a maintenance path lifts `SoftDelete` only, never `Tenant` (STACK.md §6.4).
- Required navigations into a filtered entity are modeled **optional** or filtered on both sides
  (STACK.md §6.4 required-navigation pitfall).
- `Money` persists as two columns: `<Prop>Minor` (`INTEGER`/`bigint`) + `<Prop>Currency` (`TEXT(3)`),
  via a single `ComplexProperty`/owned configuration. Never a `decimal` column on SQLite.
- `DateTimeOffset` only via a `ValueConverter` to UTC ticks (`long`) on SQLite; `datetimeoffset2(7)` on
  SQL Server. All stored time is UTC. Jalali/Persian digits exist only in Desktop presentation code.
- Enums persist as `int` with an explicit converter; never `HasConversion<string>()` on hot tables.
- Every query that backs a grid, calendar range, or lookup has a covering index named
  `IX_<Table>_<Cols>`; paging is mandatory (`Skip`/`Take` with a bounded max page size of 200).
- Migrations are additive and sequential. **A phase never edits a previous phase's migration.**
- `EnsureCreated` is forbidden outside a test fixture. Startup calls `MigrateAsync()`.
- SQLite pragma baseline (set in `AnimoraLocalDbContext` connection interceptor):
  `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout=5000`, `foreign_keys=ON`.
- Server concurrency = `rowversion`. Local concurrency = the `Version` column of `SyncableEntity`.

## §6 Offline-first & sync spine (`ARCHITECTURE.md` §8 condensed — obey, do not re-derive)

```
1  State replication != business intent. Two permanently separate pipelines, never merged:
     (a) generic record replication of flat entities via ISyncEngine
     (b) business command settlement via the durable idempotent command pipeline
2  Server is the single global authority. Offline clients hold PROVISIONAL truth.
3  Syncable shape: Id (ULID string) | UpdatedAt (server-stamped) | Version (opaque) | Deleted | TenantId.
   Local-only tables carry the [LocalOnly] marker and are never replicated.
4  Command envelope: OperationId | TenantId | UserId | DeviceId | CommandType | AggregateId | Payload
   | CreatedAtUtc (informational) | LocalSequence | Status | CommandFingerprint
   (+ CausationId/DependsOnOperationId + ExpectedAggregateVersion when dependent).
5  Idempotency key (TenantId, OperationId): identical retry returns the stored result.
   Same OperationId + different CommandFingerprint = security/integrity error, audited (A-RULE 019).
6  Idempotency != ordering. Dependency is explicit and resolved SERVER-SIDE only (A-RULE 021, §8.4).
7  Settlement states: Draft -> LocallyPosted -> PendingSettlement -> Settled | Rejected | Conflict
   | Reversed | NeedsAttention. Always visible in the UI, never hidden.
8  Sync-failure UX contract: every non-settled command answers what failed / why / does it have a
   financial effect / who must act. "Sync failed" alone is a defect (A-RULE 024).
9  Version mismatch DETECTS; domain policy DECIDES (A-RULE 004). Generic LWW is allowed ONLY for UI
   prefs/display settings. Posted invoice = immutable (reversal/credit note only). Ledger and audit =
   append-only forever. Inventory = movement-based. Appointment slot = domain validation, reject.
10 No destructive sync: an inbound pull never overwrites unsent local business intent (A-RULE 017).
11 UpdatedAt / Version / DatasyncOperationId are transport bookkeeping, never business sequence
   (A-RULE 009/010, T-RULE 006).
12 Datasync is pinned EXACTLY 10.1.2 on both sides, always behind ISyncEngine; OData is a /tables/*
   implementation detail no business client may depend on (T-RULE 002/007).
13 Every replicated contract carries an explicit contract version; an unsupported version triggers a
   controlled resync, never a silent mis-map (ARCHITECTURE.md §8.6).
14 Client wall-clock time is never a sync or licensing authority (A-RULE 011).
15 Full-resync procedure: drop pending replication ops + delta tokens for the type, pull, rebuild
   derived local state — never a normal path, always explicit and audited.
```

## §7 Security rules

- Three planes, checked independently and in this order: **Authentication → Authorization → Entitlement**
  (A-RULE 023). Never merged, never short-circuited.
- `TenantId` is resolved only from authenticated membership, server-side, on every request
  (A-RULE 012). A client-supplied `TenantId` is ignored and audited if present.
- Mandatory isolation layers (must hold with database hardening OFF — `ARCHITECTURE.md` §9.2):
  1 application authorization, 2 data-access tenant filter, 3 replication data-view filter + server-side
  write stamping. SQL RLS is optional hardening only.
- Secrets (DB key, tokens, license material) live in the OS keystore (DPAPI/Keychain) or the server's
  persisted Data Protection key ring — never in config files, never in logs, never in a repo file.
- Every security-relevant and every financial action writes an append-only audit entry
  (actor, tenant, device, operation, before/after reference, correlation ids).
- Defaults fail **closed**: unknown permission = deny; expired capability snapshot = read-only/locked;
  unverifiable license = gated capabilities off; missing key ring = refuse to start.
- Passwords: ASP.NET Core Identity `PasswordHasher<T>` primitives only. No custom crypto anywhere.
- A filesystem path is never a credential — file access is Valet-Key gated (A-RULE 020).

## §8 Performance rules

Binding: `PERFORMANCE.md` §3 (no reflection on hot paths), §4 (never block the UI thread),
§5 (batch collection updates), §6 (virtualize long lists), §12 (defer work off startup),
§15 (bounded caches). Backend-specific additions:

- Paging everywhere. No unbounded `ToListAsync()` on a business table. Max page size 200.
- Weak-server principle: push safe computation to clients; sync is delta-only; a request never does
  work a background worker can do.
- Reporting/aggregation never rides the OLTP transaction path (A-RULE 034); read models are projected
  and queried under a separate budget.
- Caches are bounded **and** tenant-safe: every key includes tenant + user + resource + version scope
  (A-RULE 033); a missing key component is a test failure, not a bug report.
- Batch writes (`AddRange`, single `SaveChangesAsync` per unit of work). No N+1: every collection load
  is an explicit `Include` or a projection join.
- Background work is bulkheaded — workers cannot starve request handling (bounded concurrency + queue).
- Capacity numbers are measured with a published harness, never asserted (A-RULE 035).
- Local startup budget: the desktop must reach first frame without awaiting any DB migration,
  sync pass, or license round-trip (PERFORMANCE.md §12).

## §9 Testing policy

| Project | Owns |
|---|---|
| `Animora.Domain.Tests` | invariants, state machines, money/quantity math, ledger balance, movement math, layering assertions |
| `Animora.Application.Tests` | handler behavior, validation, authorization decisions, idempotency, conflict policy, settlement transitions |
| `Animora.Persistence.Tests` | EF mappings, converters, migrations applying cleanly, named filters, concurrency, pragma/WAL behavior |
| `Animora.Sync.Tests` | delta/paging/resume/conflict simulation **against `ISyncEngine`**, never against Datasync types |
| `Animora.Server.Tests` | API contracts, tenancy isolation, command intake/idempotency, outbox claim, licensing, files |

Rules: every phase adds tests for the behavior it introduces. Domain invariants and the
idempotency/ordering/conflict paths are **mandatory** test targets, not optional. A test never asserts on
a Persian UI string. No test depends on wall-clock time — inject `IClock`.

## §10 Mock-retirement protocol (applies to every Track C phase, verbatim)

```
Per bounded context, in its Track C phase:
 1. Implement the real service in Animora.Client.Services, over Application + local DbContext.
 2. Extend Animora.Contracts ONLY if a genuinely missing operation is needed (add, never reshape
    existing members in a way that breaks a View/ViewModel; if a break is unavoidable, the phase
    file must list every call site to update).
 3. Seed realistic first-run reference data via a Seed/ module in Persistence.Sqlite (NOT in Mocks).
 4. Swap the DI registration in src/Animora.Desktop/App.axaml.cs (one line per service).
 5. DELETE the corresponding Mock*.cs file(s) in the same phase. No context keeps both.
 6. Zero changes to View .axaml files; ViewModel changes only where an async/lifecycle detail
    genuinely requires it, and each such change must be listed in the phase file.
 7. Verify: build 0/0, smoke run OK, and the screen's flows exercised against the local DB.
```

## §11 Runnability rule (Tracks A–C, verbatim)

```
Through the end of Track C, the app MUST remain buildable and runnable on a plain Windows/dev
machine with NO server, NO SQL Server, NO network: `scripts/sandbox/build.sh full` = 0 warnings /
0 errors, `scripts/sandbox/run.sh` = SMOKE RUN OK, and the screen(s) the phase touched work against
the real local SQLite database. From Track D onward, server-dependent features may require the
server; the desktop must still start and operate offline.
```

## §12 Definition of Done (every phase)

1. `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**.
2. `scripts/sandbox/test.sh` = all test projects green (added in A01).
3. Runnability per §11 (Tracks A–C) or "desktop still starts offline" (Tracks D–E).
4. No forbidden dependency introduced (§2 layering test passes; no Redis/broker/cloud IdP/telemetry/
   EF Core 11/Native-AOT-with-EF/per-tenant DB/hard-coded TLS version).
5. No `A-RULE`/`T-RULE`/`PERFORMANCE.md` rule violated; the phase's "Rules that apply here" list is
   explicitly re-verified.
6. Every acceptance criterion in the phase file objectively checked.
7. `backend-roadmap/PROGRESS.md` updated: phase marked done, "Next phase" advanced, "Repo state" entry
   appended, any deviation logged with justification.
8. Committed per the repo git workflow (branch `genspark_ai_developer`, conventional commit message).

## §13 Session protocol

1. Read `backend-roadmap/PROGRESS.md` (state + "Next phase" pointer).
2. Read **only** your phase file.
3. Read **only** the doc sections and repo files your phase file names.
4. Implement exactly its scope — nothing from a later phase, nothing "while we're here".
5. Verify every acceptance criterion.
6. Update `PROGRESS.md` (§12.7) and commit.

**Never**: read `Animora-UI/**` or `Database-Sampel/**`; read a `windows-frontend-roadmap/phases/**`
file; re-litigate a stack or architecture decision; refactor a prior phase unless your phase file says
to; introduce a technology not in `STACK.md` §11; leave a design choice to "the next session".
