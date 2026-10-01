# PERFORMANCE.md — Animora performance standards

Team standards for keeping the desktop UI responsive. Code comments across the
repo reference these sections (e.g. `PERFORMANCE.md §4`); this file is the
single source of truth for what each rule means. Sections are numbered to match
those references — **do not renumber existing sections**; append new ones
instead.

Scope: the Avalonia/MVVM desktop frontend (`src/Animora.Desktop`). These are
engineering guidelines, not micro-optimisation trivia: each one guards a real
hot path (startup, navigation, render, binding) or a known failure mode.

---

## §1 — Measure before you optimise

No performance change ships without a before/after rationale. Prefer the
sandbox benchmarks (`scripts/sandbox/bench.sh`) or a focused reproduction over
intuition. If a change has no measurable effect on a hot path, it does not
belong in a "perf" commit.

## §2 — Allocation-light in loops and per-item work

Any code that runs once per list item, per row, or per layout pass must avoid
gratuitous allocation (LINQ chains in tight loops, `string.Format`, boxing,
temporary collections). Hoist constants, cache lookups outside the loop, and
prefer `for` over `foreach` on arrays/indexers where it measurably helps.

## §3 — No reflection on the navigation / view-resolution hot path

The shell resolves a View for every ViewModel it hosts, and navigation happens
on every menu click. View resolution must **not** use runtime reflection
(`Type.GetType` by name, `Activator.CreateInstance`, attribute scanning).

- View resolution goes through **`Shell/ViewFactory`**: a compile-time
  `Type → Func<Control>` registry (dictionary lookup + direct constructor call).
  This replaced the old name-string + `Type.GetType` + `Activator.CreateInstance`
  path.
- `Shell/ViewLocator` stays a thin `IDataTemplate` that consults the factory and
  the view cache; it holds no static mutable state and does no reflection.

If you add a screen, register its View in `ViewFactory` — never reintroduce
name-based lookup.

## §4 — Never block the UI thread; loads are asynchronous and awaited

All data loads, sheet/dialog presentations, and navigation-time work must be
`async` and **awaited** — never synchronously blocked with `.Result`,
`.Wait()`, or `.GetAwaiter().GetResult()` on the UI thread. Doing so can
deadlock the dispatcher and always stutters the shell.

The established pattern in page ViewModels:

```csharp
public MyPageViewModel(IMyQueryService service, ...)
{
    ...
    // Non-blocking init (PERFORMANCE.md §4): never block the UI thread.
    _ = LoadAsync();
}

private async Task LoadAsync()
{
    IsBusy = true;
    try
    {
        Apply(await _service.GetSnapshotAsync());
    }
    finally
    {
        IsBusy = false;
    }
}
```

- The constructor kicks off `_ = LoadAsync()` and returns immediately.
- `LoadAsync` is `private async Task LoadAsync()` — **never `async void`** (an
  `async void` cannot be awaited, loses exceptions, and cannot be tested).
- The services now run on the real local SQLite backend (Track C complete), so
  `LoadAsync` awaits a genuine database read off the UI thread; the shell stays
  responsive and the UI fills when the Task completes. (A future Datasync/server
  service can replace the same seam with no UI change.)
- Presentation of record sheets/dialogs (`RecordSheetPresenter`,
  `NavigationService`) follows the same rule: the load is awaited, never blocked.

## §5 — Batch collection updates that feed the UI

Rebuilding a bound `ObservableCollection<T>` by calling `Clear()` and then
`Add(...)` in a loop raises one `CollectionChanged` (and one layout pass) **per
item** — an O(n) notification storm. Use
`Controls/RangeObservableCollection<T>`:

- `ReplaceAll(IEnumerable<T>)` — clear + repopulate with a **single**
  `Reset` notification (plus `Count` / `Item[]` property changes).
- `AddRange(IEnumerable<T>)` — append with a single `Reset`.

`Reset` is fully supported by Avalonia item binding and CommunityToolkit.Mvvm,
so the visible content is identical — only the notification count changes. Use
it for any bound collection whose contents are recomputed (filters, sorts,
segment switches, re-renders). Plain `List<T>` used as an internal buffer is
*not* affected and does not need this.

## §6 — Virtualise long lists

Lists that can grow large must be presented in a virtualising container
(`ItemsControl` with virtualization / `DataGrid`) rather than realised eagerly.
Avoid patterns that force full realisation (e.g. wrapping every item in an
unbounded, non-virtualising panel) on screens with unbounded row counts.

## §7 — Keep the visual tree and styles cheap

Deeply nested templates and heavy per-item styles are a common frame-time
regression. Prefer flat templates, share style resources, and avoid repeated
inline setters that defeat style reuse. When a screen rebuilds often, keep its
item-template depth shallow.

## §8 — Coalesce repeated invalidations

Do not raise property-changed notifications (or geometry/measure invalidation)
in a tight loop when a single update after the loop is equivalent. Compute the
final value, then notify once. Where several derived properties change
together, raise them together (a `RaiseDerived()`-style helper).

## §9 — Keep bindings cheap

Prefer compiled bindings (this project enables
`AvaloniaUseCompiledBindingsByDefault`). Avoid heavy value converters on hot
paths and avoid binding directly to methods that allocate. Compute display
strings once per update and expose them as properties rather than recomputing
on every binding read.

## §10 — Reduce allocation in the render/binding hot path

Anything reached during a render pass or a binding evaluation must avoid
needless allocation. Two concrete, enforced examples:

- **Brushes**: never build a fresh `SolidColorBrush` via `Color.Parse` per item
  or per render pass. Use **`Controls/BrushCache.Solid(hex)`**, which returns a
  shared, immutable instance per colour. A brush is immutable and freely
  shareable, so allocating one per call is pure GC pressure.
- **Custom-drawn controls** (`WeightChart`, `FinanceBars`, rich lines) reuse
  immutable pens/brushes/geometry constants instead of rebuilding them each
  draw.
- **Formatting**: hot formatting helpers (e.g. `PersianNumberFormatter`) avoid
  needless intermediate strings/allocation; format into the final result where
  possible.

## §11 — Precompute immutable geometry, do not rebuild per frame

Static/derived shapes (chart paths, donut slices, area fills) are computed once
when their inputs change and stored as immutable geometry, then handed to the
renderer — they are not recomputed on every frame.

## §12 — Defer work off the startup path

App startup must stay short. Do not run data loads, warm-ups, or non-essential
initialisation synchronously before the first frame. Kick long-running work off
the UI thread and let the shell render; reflect in-flight state (e.g. `IsBusy`)
rather than blocking.

## §13 — Avoid redundant reloads and re-renders

Recompute derived state only when its inputs actually change. Cache the last
input and short-circuit no-op updates; never re-run an expensive build (filter,
sort, geometry) when nothing it depends on changed.

## §14 — Threading: marshal once, correctly

Background work belongs off the UI thread, but every UI mutation (collection
change, property set) must be marshalled back to the UI thread exactly once.
Avoid per-item `Dispatcher.UIThread.Post`; batch first, dispatch once.

## §15 — Caches must be bounded

Every cache must have a bounded key space, so it cannot grow without limit over
a long-running session.

- `Controls/BrushCache` keys on distinct hex colours from the design system — a
  small, fixed set — and is lock-free on reads (`ConcurrentDictionary`).
- When introducing a new cache, justify its bound: a fixed key domain, an LRU
  eviction policy, or an explicit size cap. Unbounded caches are not allowed.

## §16 — No heavy logging or diagnostics in hot paths

Do not write to the console/log from render, binding, or per-item paths —
logging is synchronous and allocates. Keep startup diagnostics to a single
informational line (see `Program.cs`) and guard verbose/diagnostic logging
behind the DevTools build. Structured logging in hot paths is treated as a
performance bug.

---

## Known technical debt (tracked, not yet addressed)

- **Singleton page ViewModels load in their constructor.** Many page ViewModels
  (the singleton pages) trigger `_ = LoadAsync()` from their constructor, so
  data loads only once per process. Against the real backend such a screen can
  show stale data until restart, and it couples view lifetime to data lifetime.
  The standard, lifecycle-aware alternative is an `OnNavigatedTo`-style hook
  (load on navigate / when shown, optionally with a refresh policy). The
  **Dashboard** has already moved to this model — it re-activates on navigation
  and refreshes on an activation-scoped timer with a source-generation data
  cache (D04/D19/D20/D37/D38) — but the remaining singleton pages still load in
  their constructor. Migrating them is a deliberate future refactor, recorded as
  debt rather than rewritten now.
