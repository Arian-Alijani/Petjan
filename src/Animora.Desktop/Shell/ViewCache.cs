using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace Animora.Desktop.Shell;

/// <summary>
/// Per-ViewModel View reuse for the shell's convention-based view resolution.
///
/// The shell's <c>ContentControl</c> (and the modal/drawer/sheet hosts) resolve
/// a View through <see cref="ViewLocator"/> every time their content changes.
/// Without caching, each navigation to a previously-visited page rebuilt that
/// page's entire visual tree from scratch — re-inflating XAML, re-measuring and
/// re-arranging every control. Because the page ViewModels are DI singletons
/// that keep their data in memory anyway, the View can safely be kept alive
/// alongside its ViewModel and re-hosted on the next visit: the expensive
/// first build is paid once per ViewModel, and returning to a heavy screen
/// (Patients/Appointments) is instant.
///
/// The cache is keyed by ViewModel <em>instance</em> (identity), not by type:
/// two transient ViewModels of the same type (e.g. two opened Owner File
/// sheets) each get their own View, so transient drill-in screens keep their
/// existing "fresh instance per open" semantics.
///
/// Keyed by <see cref="ConditionalWeakTable{TKey,TValue}"/> for leak safety: the
/// cached View holds its ViewModel via <c>DataContext</c>, so a plain
/// <c>Dictionary&lt;object, Control&gt;</c> would root every transient ViewModel
/// (Edit/Detail/Sheet instances) forever. An ephemeron table's reachability
/// ignores the value→key edge, so an entry — and the ViewModel it holds — is
/// collectable as soon as nothing else references that ViewModel. Singleton
/// page ViewModels stay referenced for the app's life, so their Views stay
/// cached for the app's life; transient ones are released naturally.
/// </summary>
public sealed class ViewCache
{
    // Built and read only on the UI thread (navigation), so no locking is
    // needed; ConditionalWeakTable is thread-safe regardless.
    private readonly ConditionalWeakTable<object, Control> _views = new();

    // UI-04: one cached spare View per ViewModel for the rare "canonical View still hosted" case (same weak semantics).
    private readonly ConditionalWeakTable<object, Control> _spares = new();

    /// <summary>
    /// Returns the cached View for <paramref name="viewModel"/>, creating it via
    /// <paramref name="factory"/> (and caching it) on first request.
    ///
    /// Reuse is only valid when the cached View is currently unparented: a View
    /// still attached to another host cannot be re-hosted, so in that (rare)
    /// case a spare View is returned (built once, then cached per ViewModel —
    /// UI-04) and the canonical cached instance is left untouched.
    /// </summary>
    public Control GetOrCreate(object viewModel, Func<Control> factory)
    {
        if (_views.TryGetValue(viewModel, out var cached))
        {
            // A cached View is reusable only while it is not hosted anywhere: a
            // Control that still has a logical parent (it is currently shown by
            // a ContentPresenter) cannot be added to another host. The shell
            // hosts one page at a time, so this is a defensive guard; in that
            // rare case we hand back a fresh, uncached instance and leave the
            // canonical cached one undisturbed.
            if (cached.Parent is null)
            {
                return cached;
            }

            // UI-04: forcibly detaching the parented View is unsafe (its presenter owns it and may be mid-layout/animation),
            // so the second host gets a SPARE instance — but the spare is itself cached per ViewModel, so repeated requests
            // while the canonical View stays hosted reuse one rebuilt tree instead of re-inflating XAML every time. A spare
            // that is itself still hosted falls through to one more fresh build (replacing the spare).
            if (_spares.TryGetValue(viewModel, out var spare) && spare.Parent is null)
            {
                return spare;
            }

            var rebuilt = factory();
            _spares.AddOrUpdate(viewModel, rebuilt);
            return rebuilt;
        }

        var created = factory();
        _views.Add(viewModel, created);
        return created;
    }
}
