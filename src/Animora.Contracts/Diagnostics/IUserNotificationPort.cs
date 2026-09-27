namespace Animora.Contracts.Diagnostics;

/// <summary>Presentation-neutral seam for surfacing actionable client diagnostics.</summary>
public interface IUserNotificationPort
{
    void Notify(NotificationSeverity severity, string code, string? entityRef,
        NotificationAction? action);
}
