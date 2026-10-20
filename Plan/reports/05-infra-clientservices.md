# 05 — Infrastructure.Local + Client.Services Audit (`src/Animora.Infrastructure.Local/**` + `src/Animora.Client.Services/**`, excluding `Sync/`)

Scope: OS keystore/encryption, native SQLite assertion, clocks + time zone, local file store,
local logging/redaction, finance projection worker; and the Client.Services service impls for
scope-per-call correctness, EF/SQL leaks, `TenantScopedCache` bounds + tenant-key correctness
(isolation #12), debounce buffers, mapping fidelity. `Sync/` is deferred to T7.
Audit targets emphasised: #3 latent (time/clock/culture), #4/#1 perf, #5 inconsistency, #12 isolation.

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| INF-01 | High | latent (time/culture) | Business-date queries use `DateTime.Today` / `DateTime.UtcNow` directly instead of `IClock` + `IClinicTimeZoneProvider` — "today" is machine-local/UTC, not clinic-TZ Jalali day |
| INF-02 | Medium | isolation (#12) | `TenantScopedCache` does **not enforce** the "key MUST include tenant + user" invariant — it is a bare LRU; tenant-keying is caller-convention only, with no compile- or run-time guard |
| INF-03 | Medium | latent | `DebounceBuffer` hard cap of 32 pending notes **throws** `InvalidOperationException` on overflow instead of forcing a flush; a fast typist across many open encounters can hit it |
| INF-04 | Low | latent / correctness | `FinanceProjectionWorker` **drains and drops** outbox messages of unregistered type (logs a warning, returns null) — a mis-registered financial event is silently lost after one log line |
| INF-05 | Low | perf | `MonotonicClock` / `ClinicTimeZoneProvider` open a **fresh DI scope + DbContext per call** on singletons; fine for the rare clock path but `ClinicTimeZoneProvider.GetClinicTimeZone()` (sync) can run a DB query on the caller thread on first use |
| INF-06 | Info | — | Keystore/secret-store design is sound: Windows DPAPI + ACL, macOS Keychain via `/usr/bin/security`, fail-closed `UnavailableSecretStore`, dev-only plaintext gated behind `AllowInsecureKeyFallback`; secrets never logged; `LogRedaction` recursively redacts sensitive structured properties; service impls follow the scope-per-call C01 pattern with no EF/SQL leak |

---

## INF-06 (baseline verification — security/scope hygiene is strong)

- Severity: Info
- Location: `Security/{DpapiSecretStore,KeychainSecretStore,FileFallbackSecretStore}.cs`,
  `LocalInfrastructureServiceCollectionExtensions.CreateSecretStore`, `Diagnostics/LogRedaction.cs`,
  `Patients/PatientsService.cs` (representative service).
- Evidence:
  - Secret store is selected fail-closed: `DpapiSecretStore` on Windows (DPAPI CurrentUser +
    `SetAccessRuleProtection(isProtected:true)` ACL to `WindowsIdentity.GetCurrent().User`), `KeychainSecretStore`
    on macOS, else `FileFallbackSecretStore` only when `AllowInsecureKeyFallback` is set, otherwise
    `UnavailableSecretStore` where every op returns `StoreKeyUnavailable`.
  - Secret values are never logged; `FileFallbackSecretStore` logs a warning on every read and sets
    `UnixFileMode.UserRead|UserWrite`.
  - `LogRedaction.Rewrite` recurses through `SequenceValue`/`StructureValue`/`DictionaryValue` and redacts
    `Password/PasswordHash/Passphrase/Token/Key/Signature/NationalId/Phone` to `[redacted]` before the sink.
  - `PatientsService` is a singleton that resolves the dispatcher + provider from a **fresh async scope per
    call** (`await using var scope = scopes.CreateAsyncScope()`), never captures a DbContext, contains no EF/SQL,
    and clamps page size (`MaxPageSize = 200`). This is the uniform C01 pattern across the 24 impls.
- Why it matters: establishes that the keystore seam, redaction, and the scope-per-call rule are not the
  defect source; the findings below are residual time/cache/overflow issues, not systemic.
- Confidence: High.

---

## INF-01 Business-date queries bypass the clock + time-zone abstraction

- Severity: High
- Class: latent (time / culture / timezone)
- Location:
  - `Client.Services/Reception/ReceptionQueueService.cs:19` — `DateOnly.FromDateTime(DateTime.Today)`
  - `Client.Services/Visits/VisitsTriageService.cs:26,77` — `DateOnly.FromDateTime(DateTime.Today)`
  - `Client.Services/Vaccination/VaccinationService.cs:17,127` — `DateOnly.FromDateTime(DateTime.UtcNow)`
- Evidence (ReceptionQueueService):
  ```csharp
  .QueryAsync(new ReceptionQueueQuery(DateOnly.FromDateTime(DateTime.Today)), context.Value!);
  ```
  VaccinationService computes the "due today" boundary from `DateTime.UtcNow`:
  ```csharp
  (await QueryAsync(new VaccinationDueQuery(DateOnly.FromDateTime(DateTime.UtcNow))))
  ```
- Why it is a problem: the product ships `IClock` (over `TimeProvider`) and `IClinicTimeZoneProvider`
  precisely so "the current clinic day" is derived in the clinic time zone (Asia/Tehran) and is test-injectable.
  These three services instead read the **machine wall clock**: `DateTime.Today` is the OS local date (not
  necessarily the clinic TZ on a mis-configured desktop), and `DateTime.UtcNow` is the UTC date — for a Tehran
  clinic (UTC+03:30) the UTC date rolls over 3.5 h early, so late-evening local appointments/vaccinations fall
  on the **wrong day**. Jalali day boundaries are derived from these, compounding the drift. None of the three
  calls is injectable, so the bug is invisible to time-travel tests.
- Scenario / impact: Reception queue and triage rail show the wrong day's patients near midnight local time;
  "vaccinations due today" is computed against a UTC day that is 3.5 h ahead of the clinic day → due reminders
  surface/omit a day early for the last ~3.5 h of each local day. Silent, data-dependent, hard to reproduce
  outside that window.
- Confidence: High (code-evident; exact horizon = any clinic whose TZ ≠ UTC, i.e. all of them).

## INF-02 `TenantScopedCache` does not enforce its own tenant/user keying invariant

- Severity: Medium
- Class: isolation (#12)
- Location: `Client.Services/Caching/TenantScopedCache.cs` (whole type).
- Evidence: the class doc says *"A key MUST include tenant and user IDs"*, but the implementation is a plain
  generic LRU over `Dictionary<TKey, …>` + `LinkedList`. Nothing in `TryGet`/`Set`/`Invalidate` inspects,
  derives, or validates a tenant/user component — correctness is 100% caller convention.
- Why it is a problem: in a single-tenant-per-local-store model the blast radius is small, but the type is
  generic and reusable; any caller that keys on, say, a bare entity id (forgetting tenant/user) gets
  cross-user cache bleed with **no guard** to catch it. The invariant is documented, not enforced, so a
  future call site violating it fails silently rather than at compile/review time.
- Scenario / impact: a projection cached under a non-tenant key would be served across user/tenant switches
  on the same install (e.g. staff account switch) until `Clear()`. Latent isolation hazard that widens if the
  cache is ever reused server-side or multi-tenant-side.
- Confidence: Medium (needs a call-site census to confirm current keys all include tenant+user; the type
  itself offers no protection regardless).

## INF-03 `DebounceBuffer` overflow throws instead of flushing

- Severity: Medium
- Class: latent
- Location: `Client.Services/Visits/DebounceBuffer.cs`, `Enqueue`:
  ```csharp
  if (_pending.Count >= 32) throw new InvalidOperationException("Debounce buffer is full; flush before adding more notes.");
  ```
- Why it is a problem: the buffer keys pending writes by `(EncounterId, VisitNoteField)`. 32 distinct keys is
  reachable with several encounters open, each with multiple note fields edited within the 600 ms debounce
  window. On the 33rd, the enqueue **throws into the UI edit path** rather than draining — a keystroke handler
  raising `InvalidOperationException` is a poor failure mode for an offline-first clinical note editor.
- Scenario / impact: rapid multi-field editing across open encounters surfaces a hard error mid-typing;
  worst case the exception is unhandled on the input thread. The fix shape (auto-flush the oldest, or back-
  pressure) is a design choice, but *throwing on overflow* is the latent defect.
- Confidence: High (code-evident; trigger is a busy multi-encounter session).

## INF-04 Finance outbox worker silently drops unregistered message types

- Severity: Low
- Class: latent / correctness (financial)
- Location: `Infrastructure.Local/Workers/FinanceProjectionWorker.cs`, `Deserialize` → `LogAndDrop`:
  ```csharp
  _ => LogAndDrop(m),   // logs a warning, returns null → message is Completed (drained) without a projection
  ```
- Why it is a problem: the comment acknowledges *"a lost event must leave a trace"*, and it does log a warning
  — but it then **acks** the message (`CompleteAsync`), permanently removing it. In a money/stock projection
  pipeline, a type that is in the outbox but not in the `switch` (e.g. a newly added event whose projection
  registration was forgotten) is dropped after a single log line, leaving the finance facts silently behind
  the ledger with no replay path. A dead-letter/park state would be safer than Complete.
- Scenario / impact: a future financial event family added to the outbox relay but not to the worker switch
  produces a permanent projection gap that only a log-scrape reveals. Diverges projections from the
  authoritative ledger (compounds T10 findings).
- Confidence: High (code-evident).

## INF-05 Singleton clock/TZ providers do synchronous DB work on the caller thread on first use

- Severity: Low
- Class: perf
- Location: `Infrastructure.Local/Time/ClinicTimeZoneProvider.cs` (`GetClinicTimeZone()` sync path),
  `Time/MonotonicClock.cs` (`EnsureLoaded`/`Persist`).
- Evidence: both are singletons that, on cache miss, open `scopes.CreateScope()` + a DbContext and run an EF
  query/`ExecuteSqlRaw` **synchronously**. `ClinicTimeZoneProvider.GetClinicTimeZone()` has no async option on
  that sync entry point, so the first caller (which may be a UI thread before the async hydrate ran) blocks on
  a SQLite query.
- Why it is a problem: on an encrypted SQLite store a first-use query under contention is not free; running it
  on the UI thread (A23 warns against this) can produce a visible hitch. Cached thereafter, so impact is a
  one-time first-use stall per provider, not steady state.
- Scenario / impact: minor startup/first-navigation jank; not a correctness issue. Mitigated if the async
  hydrate (`GetClinicTimeZoneAsync`) always wins the race, which is not guaranteed.
- Confidence: Medium (needs runtime check to confirm which path fires first).
