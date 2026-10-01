using Avalonia.Threading;

namespace Animora.Desktop.Services;

/// <summary>
/// pub/sub لحظهٔ‌ای بدون state انباشته. شنونده‌ها همیشه روی UI thread صدا زده
/// می‌شوند؛ اگر <see cref="Notify"/> از thread دیگری آمد، marshal می‌شود.
/// </summary>
public sealed class DataChangeNotifier : IDataChangeNotifier
{
    private readonly object _gate = new();
    private Action<DataDomain>[] _handlers = [];

    public void Notify(params DataDomain[] domains)
    {
        if (domains.Length == 0) return;

        if (Dispatcher.UIThread.CheckAccess()) Dispatch(domains);
        else Dispatcher.UIThread.Post(() => Dispatch(domains));
    }

    public IDisposable Subscribe(Action<DataDomain> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate) _handlers = [.. _handlers, handler];
        return new Subscription(this, handler);
    }

    private void Dispatch(DataDomain[] domains)
    {
        // snapshot کپی‌-روی-نوشتن: شنونده می‌تواند حین اعلان اشتراک را لغو کند.
        Action<DataDomain>[] snapshot;
        lock (_gate) snapshot = _handlers;

        foreach (var domain in domains)
        {
            foreach (var handler in snapshot)
            {
                try { handler(domain); }
                catch (Exception) { /* خطای یک شنونده نباید بقیه را از اعلان محروم کند */ }
            }
        }
    }

    private void Unsubscribe(Action<DataDomain> handler)
    {
        lock (_gate) _handlers = [.. _handlers.Where(h => h != handler)];
    }

    private sealed class Subscription(DataChangeNotifier owner, Action<DataDomain> handler) : IDisposable
    {
        private Action<DataDomain>? _handler = handler;

        public void Dispose()
        {
            var h = Interlocked.Exchange(ref _handler, null);
            if (h is not null) owner.Unsubscribe(h);
        }
    }
}
