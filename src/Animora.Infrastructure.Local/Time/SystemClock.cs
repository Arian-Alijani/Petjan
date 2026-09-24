using Animora.Domain.Time;

namespace Animora.Infrastructure.Local.Time;

/// <summary>Wall-clock <see cref="IClock"/> over <see cref="TimeProvider"/> (defaults to <see cref="TimeProvider.System"/>).</summary>
public sealed class SystemClock(TimeProvider? provider = null) : IClock
{
    private readonly TimeProvider _provider = provider ?? TimeProvider.System;

    public DateTimeOffset UtcNow => _provider.GetUtcNow().ToUniversalTime();
}
