using Animora.Contracts.Diagnostics;

namespace Animora.Client.Services.Diagnostics;

/// <summary>Routes machine-readable notifications to presentation callbacks without taking a UI dependency.</summary>
public sealed class ToastNotificationAdapter(
    Action<string, NotificationSeverity> showToast,
    Action<string, NotificationAction> showAction,
    Func<string, string?, string> resolveText) : IUserNotificationPort
{
    public void Notify(NotificationSeverity severity, string code, string? entityRef,
        NotificationAction? action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var text = resolveText(code, entityRef);
        showToast(text, severity);
        if (action is not null)
        {
            showAction(text, action);
        }
    }
}
