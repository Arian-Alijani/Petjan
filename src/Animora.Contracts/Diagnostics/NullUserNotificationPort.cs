namespace Animora.Contracts.Diagnostics;

/// <summary>
/// No-op notification sink for headless/host-less compositions (e.g. background workers or the
/// standalone manual-test data tool) that have no UI to surface toasts or modals. A real UI adapter
/// (<c>ToastNotificationAdapter</c> in the desktop app) is registered on top of this and wins, so this
/// only takes effect when no presentation layer is present.
/// </summary>
public sealed class NullUserNotificationPort : IUserNotificationPort
{
    public void Notify(NotificationSeverity severity, string code, string? entityRef,
        NotificationAction? action)
    {
        // Intentionally does nothing: there is no user-facing surface in a host-less composition.
    }
}
