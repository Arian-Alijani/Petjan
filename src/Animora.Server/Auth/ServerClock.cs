using Animora.Domain.Time;

namespace Animora.Server.Auth;

/// <summary>Server <see cref="IClock"/> over <see cref="TimeProvider"/> (the client clock lives in a forbidden project).</summary>
internal sealed class ServerClock(TimeProvider provider) : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => provider.GetUtcNow();
}
