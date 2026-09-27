using Animora.Domain.Results;

namespace Animora.Application.Ports;

public enum UserNotificationClass
{
    NeedsAttention,
}

public sealed record UserNotification(UserNotificationClass Class, string Code, string Detail);

/// <summary>Temporary B05a seam; B05c replaces the no-op adapter with the desktop notification adapter.</summary>
public interface IUserNotificationPort
{
    Task<Result> RaiseAsync(UserNotification notification, CancellationToken ct = default);
}

public sealed class NullUserNotificationPort : IUserNotificationPort
{
    public Task<Result> RaiseAsync(UserNotification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return Task.FromResult(Result.Ok());
    }
}
