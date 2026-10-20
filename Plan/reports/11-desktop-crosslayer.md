# 11 — Desktop UI/DI + Cross-Layer Consistency Audit (audit target #5)

Scope: `src/Animora.Desktop/{App.axaml.cs (DI root), Shell, Services, Pages (spot-check), Controls,
Localization}` + the Statistics module (new, uncovered by AGENTS.md). Cross-checked against
`Client.Services` (scope-per-call) and `Contracts` interfaces.

**Verification outcome.** The presentation layer is disciplined and largely correct. No ViewModel references
`DbContext` / EF / concrete persistence types (clean Contracts-only dependency); every query/command service
opens a **fresh async scope per call** and never captures a scoped dependency on a singleton; all DB work runs
off the UI thread inside `Task.Run` (the code explicitly notes Microsoft.Data.Sqlite's async reads are
synchronous); the view cache is a `ConditionalWeakTable` (leak-safe); sign-out **rebuilds the whole DI
provider** so one clinic's in-memory rows never leak into the next session; and the root provider is disposed
on exit so background workers drain cleanly. The findings are a small set of latent/structural concerns and
one UI-thread-affinity caveat, not rule violations.

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| UI-01 | Medium | latent (memory) | Every page ViewModel is a **DI singleton that keeps its loaded data in memory for the whole session** (`RangeObservableCollection`s, chart series). Memory is the sum of the last load of *every* visited screen and only released on sign-out (provider rebuild); a long shift over large directories has a steadily-held working set with no per-page eviction |
| UI-02 | Medium | latent (UI-thread affinity) | Statistics (and other) `LoadCoreAsync` publish to bound `ObservableProperty`/`RangeObservableCollection` on whatever context resumes the `await`. It is correct **only because** the load is entered from the UI thread (the captured SynchronizationContext marshals the continuation back). There is no explicit `Dispatcher.UIThread` guard — a future non-UI caller of `LoadAsync` would mutate UI-bound collections off-thread |
| UI-03 | Low | latent (event leak) | `OnFrameworkInitializationCompleted` subscribes `Dispatcher.UIThread.UnhandledException` and `ProfilePageViewModel.SignedOut` on each provider build; on sign-out the provider is rebuilt and re-subscribed. The `SignedOut` handler unsubscribes itself, but the per-build dispatcher hook and process hooks are re-registered every sign-out (`RegisterProcessHooks` again) without an explicit unhook — repeated sign-in/out cycles accumulate registrations on process-global hooks |
| UI-04 | Low | perf | `ViewCache.GetOrCreate` returns a **fresh, uncached** View whenever the cached one is still parented; the shell hosts one page at a time so this is rare, but a modal/sheet/drawer host showing the same VM while the page is also shown rebuilds the visual tree each time (the "instant return" optimization silently does not apply) |
| UI-05 | Low | inconsistency | DI registration comments still describe several services as "mock"/"in-memory" (`InventoryQueryService` "stateless deterministic mock", Cashier/History/Profile "mock") though they are wired to the real local backend elsewhere; stale comments in the composition root invite wrong lifetime assumptions during future edits |
| UI-06 | Info | — | Verified sound: zero EF/DbContext leakage into ViewModels; scope-per-call services with no captured scoped deps; all DB work off the UI thread; `ConditionalWeakTable` view cache; full provider rebuild + dispose on sign-out (tenant isolation + worker drain); Contracts interfaces used by VMs all have a real registration |

---

## UI-06 (baseline verification — the presentation layer honors its rules)

- Severity: Info
- Location: `App.axaml.cs` (DI root), `Client.Services/**` (scope-per-call), `Shell/ViewCache.cs`,
  `Shell/ViewLocator.cs`.
- Evidence:
  - **No persistence leak into VMs:** a scan of `src/Animora.Desktop/Pages/**` for
    `DbContext` / `Microsoft.EntityFrameworkCore` / `AnimoraLocalDbContext` / `Persistence.` returns nothing —
    ViewModels talk only to Contracts interfaces.
  - **Scope-per-call, no capture:** services such as `StatisticsQueryService` take `IServiceScopeFactory` and
    do `await using var scope = scopes.CreateAsyncScope();` per method, resolving `IDispatcher` /
    `ICurrentUserProvider` inside the scope — a singleton service never holds a scoped dependency.
  - **Off the UI thread:** `QueryAsync`/`SendAsync` wrap the whole scoped call in `Task.Run` with the explicit
    note "Microsoft.Data.Sqlite runs async reads synchronously: keep the whole scoped call off the caller (UI)
    thread."
  - **Leak-safe view reuse:** `ViewCache` keys Views by ViewModel instance via `ConditionalWeakTable`, so a
    transient sheet/detail VM's View is collectable as soon as the VM is unreferenced.
  - **Tenant isolation on sign-out:** `OnProfileSignedOut` disposes the previous `IServiceProvider` (draining
    `FinanceProjectionWorker`/`MaintenanceScheduler`) and calls `ConfigureServices()` afresh, so "a fresh
    composition also discards every singleton page, view cache, navigation history and dialog host" — the
    previous clinic's in-memory rows cannot reach the next user's shell.
  - **Registration completeness:** every Contracts interface a VM consumes (`IStatisticsQueryService`,
    `ISalesQueryService`, `ICashierService`, `IDashboardQueryService`, …) has a concrete singleton registration
    in `ConfigureServices`.
- Why it matters: establishes that the MVVM/DI discipline and thread model are correct, so the remaining items
  are performance/lifetime refinements rather than defects.
- Confidence: High.

## UI-01 Singleton page ViewModels hold their data for the whole session

- Severity: Medium
- Class: latent (memory growth / working set)
- Location: `App.axaml.cs ConfigureServices` — `DashboardPageViewModel`, `PatientsPageViewModel`,
  `SalePageViewModel`, `CashierPageViewModel`, `HistoryPageViewModel`, all four Statistics section VMs, etc.
  are `AddSingleton`; their collections (`RangeObservableCollection<…>`, chart point lists) hold the last load.
- Why it is a problem: the design keeps page VMs warm (fast "navigate back") by making them singletons that
  retain data in memory. The cost is that the shell's steady-state memory is the **sum of the most recent load
  of every screen the user visited**, held until sign-out rebuilds the provider. For screens backed by large
  directories (Patients/Owners, History, Statistics expense pages that `LoadMore`-append), a long working
  session accumulates and never evicts. There is no LRU/`OnNavigatedFrom`-clear for the heavy collections.
- Scenario / impact: a reception PC left signed-in all day, paging through thousands of history/expense rows
  across screens, holds all of them resident; memory grows with usage and only resets on sign-out. Not a leak
  (bounded by "what was loaded"), but an unbounded-with-usage working set. Mitigation: clear the largest
  collections in `OnNavigatedFrom` for list-heavy pages, or make those VMs transient.
- Confidence: Medium (needs a memory profile to size impact; the retention mechanism is certain from the
  singleton + in-memory-collection design).

## UI-02 Statistics publish relies on implicit UI-thread resumption

- Severity: Medium
- Class: latent (thread affinity)
- Location: `Pages/Statistics/StatisticsSectionViewModel.LoadAsync` → `LoadCoreAsync` (e.g.
  `StatisticsClientsViewModel.LoadCoreAsync`):
  ```csharp
  await Task.WhenAll(statsTask, speciesTask, topTask, seriesTask);
  …
  PublishStats(stats);            // mutates [ObservableProperty] + RangeObservableCollection bound to UI
  PublishSpecies(speciesTask.Result);
  ```
- Why it is a problem: the four reads run on `Task.Run` threads inside the service, but the VM `await`s them
  **without** `ConfigureAwait(false)`, so the continuation that calls `PublishStats`/mutates the bound
  collections resumes on the context captured when `LoadAsync` was entered. This is correct today only because
  `LoadAsync` is invoked from the UI thread (Avalonia SynchronizationContext marshals the continuation back).
  Nothing in the method asserts UI-thread affinity: a future caller that starts the load from a background
  thread (a sync-change bridge, a timer without dispatcher hop) would mutate UI-bound `ObservableCollection`s
  off the UI thread — Avalonia's classic cross-thread collection-change crash.
- Scenario / impact: a refactor that triggers a statistics refresh from a non-UI callback produces intermittent
  `InvalidOperationException`/visual corruption that is hard to reproduce. The `.Result` reads are themselves
  safe (tasks already completed by `WhenAll`). Mitigation: wrap the publish in `Dispatcher.UIThread.Post`/
  `InvokeAsync`, or document+assert UI-thread entry.
- Confidence: Medium (correct at present; the fragility is real and unguarded).

## UI-03 Per-build global hooks re-registered on every sign-out

- Severity: Low
- Class: latent (handler accumulation)
- Location: `App.axaml.cs` — `OnFrameworkInitializationCompleted` (`RegisterProcessHooks`,
  `Dispatcher.UIThread.UnhandledException += …`) and `OnProfileSignedOut` (rebuilds provider, then
  `Services.GetRequiredService<GlobalExceptionHandler>().RegisterProcessHooks()` again).
- Why it is a problem: sign-out rebuilds the DI provider and re-runs `RegisterProcessHooks()` on the *new*
  `GlobalExceptionHandler`. If `RegisterProcessHooks` subscribes `AppDomain.CurrentDomain.UnhandledException` /
  `TaskScheduler.UnobservedTaskException` (process-global, multicast) without first removing the previous
  handler, each sign-in/out cycle adds another live subscriber rooted on the old (disposed) provider's handler,
  so a crash fires N stale handlers and the old providers are kept alive by the delegate. The self-unsubscribing
  `SignedOut` handler is fine; the process-global hooks are the concern.
- Scenario / impact: a kiosk with many sign-in/out cycles slowly accumulates exception-hook subscriptions and
  leaks the handlers they root. Low impact (handlers are small, crashes are rare) but a genuine
  cycle-proportional leak. Needs confirmation of `RegisterProcessHooks` idempotency.
- Confidence: Medium (depends on `RegisterProcessHooks` internals; the re-registration call on each sign-out is
  certain).

## UI-04 View cache falls back to an uncached rebuild when the View is parented

- Severity: Low
- Class: perf
- Location: `Shell/ViewCache.GetOrCreate`:
  ```csharp
  if (cached.Parent is null) return cached;
  return factory();   // fresh, uncached
  ```
- Why it is a problem: when the same ViewModel's cached View is currently hosted somewhere (parented), a second
  request rebuilds the whole visual tree and does **not** cache it, so every such request re-inflates XAML. The
  shell hosts one page at a time so this is rare for page VMs, but a VM shown simultaneously in two hosts
  (e.g. a sheet re-opened while still animating out, or a VM bound in both a drawer and the page) repeatedly
  pays the full build cost that the cache is meant to avoid.
- Scenario / impact: occasional visible jank on a heavy screen re-opened before its previous instance detaches;
  purely performance, never incorrect. Acceptable given the documented single-host invariant.
- Confidence: High (code path is explicit).

## UI-05 Composition-root comments describe real services as "mock"

- Severity: Low
- Class: inconsistency (maintainability)
- Location: `App.axaml.cs ConfigureServices` — e.g. "Phase 17: Inventory product list & settings. Stateless
  deterministic mock", "Phase 22: Cashier … Stateful in-memory mock", "Phase 27: History … synthetic mock",
  "Phase 28: Profile … mock persists edits in-memory only" — while the registrations bind the real
  `InventoryQueryService`, `CashierService`, `HistoryQueryService`, `UserProfileService` over the local backend.
- Why it is a problem: the comments contradict the actual registrations and the documented scope-per-call real
  services. A maintainer trusting the "stateful in-memory mock, so a singleton preserves mutations" rationale
  could wrongly assume the singleton lifetime carries data (it does not — the real service is stateless and
  scope-per-call), or hesitate to change a lifetime that the comment justifies on false grounds.
- Scenario / impact: no runtime defect; a documentation/lifetime-reasoning hazard localized to the DI root.
- Confidence: High (comment vs registration mismatch is visible in the same file).
