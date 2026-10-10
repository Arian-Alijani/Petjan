using System;
using Animora.Domain.Time;

namespace Animora.Server.Tests.HandlersS09;

/// <summary>
/// A deterministic <see cref="IClock"/> for the A05–A08 domain tests: no test reads the wall
/// clock (CONVENTIONS.md §9).
/// </summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
