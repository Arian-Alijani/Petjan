using System.Threading;

namespace Animora.Desktop;

/// <summary>
/// Windows single-instance guard for the tray-resident app. Once the app lives in the system tray (auto-started
/// with Windows, or its window closed to the tray) a second launch must NOT open a second process on the same
/// SQLite store — it instead signals the running instance to show its window and exits.
/// One guard per storage profile (clinic default vs each <c>--local-test</c> profile), so isolated test
/// profiles can still run side by side. Non-Windows platforms are a no-op (named events are Windows-only).
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _showSignal;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstanceGuard(Mutex? mutex, EventWaitHandle? showSignal)
    {
        _mutex = mutex;
        _showSignal = showSignal;
    }

    /// <summary>
    /// Tries to become the primary instance. Returns <c>null</c> when another instance already owns the profile
    /// (that instance has been asked to show itself) — the caller should exit. Never throws: if the guard cannot
    /// be established the app simply runs unguarded, as it did before.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string profileKey, out bool isPrimary)
    {
        isPrimary = true;
        if (!OperatingSystem.IsWindows()) return new SingleInstanceGuard(null, null);

        try
        {
            var key = "Animora.VetPlus." + Sanitize(profileKey);
            var mutex = new Mutex(initiallyOwned: true, @"Local\" + key + ".Mutex", out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(@"Local\" + key + ".Show");
                    signal.Set();
                }
                catch (Exception)
                {
                    // The primary instance is still starting and has not created the event yet; it will show a
                    // window by itself on boot, nothing more to do.
                }
                isPrimary = false;
                return null;
            }

            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + key + ".Show");
            return new SingleInstanceGuard(mutex, showSignal);
        }
        catch (Exception)
        {
            return new SingleInstanceGuard(null, null);
        }
    }

    /// <summary>Starts listening for «show yourself» signals from later launches; <paramref name="onShow"/> runs on a pool thread.</summary>
    public void StartListening(Action onShow)
    {
        if (_showSignal is null) return;
        var thread = new Thread(() =>
        {
            var handles = new[] { _showSignal, _stop.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                try { onShow(); }
                catch (Exception) { /* never let a failed restore kill the listener */ }
            }
        })
        { IsBackground = true, Name = "Animora.SingleInstance" };
        thread.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _mutex?.ReleaseMutex(); } catch (Exception) { /* not owned / already released */ }
        _mutex?.Dispose();
        _showSignal?.Dispose();
        _stop.Dispose();
    }

    private static string Sanitize(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }
}
