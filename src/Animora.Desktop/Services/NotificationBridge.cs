using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Identity;
using Animora.Contracts.Notifications;
using Animora.Desktop.Controls;
using Animora.Desktop.Diagnostics;
using Animora.Desktop.Localization;
using Avalonia.Threading;

namespace Animora.Desktop.Services;

/// <summary>
/// Real-time delivery of messages WITHOUT any sync: the local <see cref="INotificationService"/> raises
/// <see cref="INotificationService.FeedChanged"/> (any thread) when a message was composed, became due or a notice was
/// issued; this bridge turns that into <see cref="DataDomain.Notifications"/> (so the open page, the header badge and
/// the sidebar badge refresh at once) and shows one toast for each message that newly arrived for the signed-in user.
/// The first evaluation per user - and the one after a sign-in - only seeds the "already seen" set, so old unread
/// messages never replay as toasts at start-up.
/// </summary>
public sealed class NotificationBridge : IDisposable
{
    private readonly INotificationService _service;
    private readonly IDataChangeNotifier _changes;
    private readonly IToastService _toast;
    private readonly ICurrentUserContext _user;
    private readonly ICapabilityGate _gate;
    private readonly SemaphoreSlim _gateLock = new(1, 1);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private string? _seededFor;
    private bool _started;

    public NotificationBridge(INotificationService service, IDataChangeNotifier changes, IToastService toast,
        ICurrentUserContext user, ICapabilityGate gate)
    {
        _service = service;
        _changes = changes;
        _toast = toast;
        _user = user;
        _gate = gate;
    }

    /// <summary>Begins observing the feed and the sign-in/out signal. Idempotent.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _service.FeedChanged += OnFeedChanged;
        _gate.CapabilitiesChanged += OnCapabilitiesChanged;
        AppTrace.Info("NotificationBridge started — feed changes refresh badges/page and toast new messages.", "notifications");
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        _service.FeedChanged -= OnFeedChanged;
        _gate.CapabilitiesChanged -= OnCapabilitiesChanged;
        _gateLock.Dispose();
    }

    private void OnFeedChanged(object? sender, EventArgs e)
    {
        _changes.Notify(DataDomain.Notifications); // marshalled to the UI thread by the notifier
        _ = EvaluateAsync();
    }

    // A fresh authority (sign-in, user switch) is when the badge must be (re)computed and the seen-set reseeded.
    private void OnCapabilitiesChanged(object? sender, EventArgs e)
    {
        _changes.Notify(DataDomain.Notifications);
        _ = EvaluateAsync();
    }

    private async Task EvaluateAsync()
    {
        try
        {
            await _gateLock.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            var userId = _user.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                _seededFor = null;
                _seen.Clear();
                return;
            }

            var feed = await _service.GetFeedAsync(unreadOnly: true).ConfigureAwait(false);
            var fresh = feed.Items.Where(i => !i.IsRead && !_seen.Contains(i.Id)).ToList();
            foreach (var item in feed.Items) _seen.Add(item.Id);

            if (_seededFor != userId)
            {
                _seededFor = userId; // first look for this user: seed silently
                return;
            }

            if (fresh.Count == 0) return;
            var text = fresh.Count == 1
                ? $"پیام جدید: {fresh[0].Title}"
                : $"{PersianNumberFormatter.ToPersianDigits(fresh.Count)} پیام جدید دریافت کردید";
            Dispatcher.UIThread.Post(() => _toast.Show(text, Tone.Brand));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppTrace.Warn($"NotificationBridge evaluation failed — {ex.Message}", "notifications");
        }
        finally
        {
            try
            {
                _gateLock.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
