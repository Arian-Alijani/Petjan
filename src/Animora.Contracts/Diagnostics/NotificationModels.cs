namespace Animora.Contracts.Diagnostics;

public enum NotificationSeverity
{
    Info,
    Warning,
    Error,
    NeedsAttention,
}

public sealed record NotificationAction(string ActionCode, string? TargetRoute);
