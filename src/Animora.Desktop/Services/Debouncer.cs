using System;
using Avalonia.Threading;

namespace Animora.Desktop.Services;

/// <summary>
/// Lightweight, reusable debounce helper. Wraps a <b>single</b>
/// <see cref="DispatcherTimer"/> created once in the constructor; each
/// <see cref="Debounce"/> call only stops and restarts that timer, so no new
/// timer is allocated per keystroke (avoids the leak/allocation pattern of
/// rebuilding a <see cref="DispatcherTimer"/> inside every input handler).
///
/// The timer fires <b>once</b> per idle window: on tick it stops itself and
/// then runs the latest action. Typical use is a ViewModel search field:
/// <code>
/// private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(160));
/// partial void OnSearchTextChanged(string value) => _searchDebounce.Debounce(ApplyFilter);
/// </code>
/// </summary>
public sealed class Debouncer
{
    private readonly DispatcherTimer _timer;
    private Action? _pending;

    public Debouncer(TimeSpan interval)
    {
        // Created ONCE. Subsequent Debounce() calls only Stop()/Start() it.
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// Schedules <paramref name="action"/> to run once after the configured
    /// interval of inactivity. Calling again before the interval elapses
    /// resets the timer and replaces the pending action, so only the most
    /// recent call fires.
    /// </summary>
    public void Debounce(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pending = action;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Cancels any pending action without firing it.</summary>
    public void Cancel()
    {
        _pending = null;
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Fire only once per window: stop before invoking so a re-entrant
        // Debounce() inside the action can re-arm the timer cleanly.
        _timer.Stop();
        var action = _pending;
        _pending = null;
        action?.Invoke();
    }
}
