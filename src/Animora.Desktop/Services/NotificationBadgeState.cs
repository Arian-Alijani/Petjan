using System;
using System.Threading.Tasks;
using Animora.Contracts.Notifications;
using Animora.Desktop.Diagnostics;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Services;

/// <summary>
/// The single, shared "unread messages" number behind the header bell and the sidebar entry. It recomputes through
/// <see cref="INotificationService.GetUnreadCountAsync"/> whenever <see cref="DataDomain.Notifications"/> is announced
/// (a message arrived, was read, or a schedule fell due) - never by polling - and raises <see cref="Changed"/> on the
/// UI thread (the notifier's handlers and the awaited continuation both run there).
/// </summary>
public sealed class NotificationBadgeState : IDisposable
{
    private readonly INotificationService _service;
    private readonly IDisposable _subscription;
    private int _generation;

    public NotificationBadgeState(INotificationService service, IDataChangeNotifier changes)
    {
        _service = service;
        _subscription = changes.Subscribe(domain =>
        {
            if (domain == DataDomain.Notifications) _ = RefreshAsync();
        });
    }

    /// <summary>Unread messages for the signed-in user (0 when signed out / unknown).</summary>
    public int Count { get; private set; }

    /// <summary>Raised on the UI thread after <see cref="Count"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>«۳» / «۹۹+» - the text shown inside the badge.</summary>
    public string Text => Count > 99 ? PersianNumberFormatter.ToPersianDigits("99") + "+" : PersianNumberFormatter.ToPersianDigits(Count);

    /// <summary>Re-reads the unread total; a slower, older answer never overwrites a newer one.</summary>
    public async Task RefreshAsync()
    {
        var token = ++_generation;
        int count;
        try
        {
            count = await _service.GetUnreadCountAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppTrace.Warn($"notification badge refresh failed — {ex.Message}", "notifications");
            return;
        }

        if (token != _generation || count == Count) return;
        Count = count;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _subscription.Dispose();
}
