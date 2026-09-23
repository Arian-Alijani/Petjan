namespace Animora.Domain.Time;

/// <summary>
/// The only source of "now" for Domain and Application code: reading the ambient system
/// clock directly is forbidden there (CONVENTIONS.md §4), which also keeps tests free of
/// wall-clock dependencies (CONVENTIONS.md §9).
/// </summary>
/// <remarks>Implementations live in <c>Animora.Infrastructure.Local</c> (Phase A11).</remarks>
public interface IClock
{
    /// <summary>Current instant, always in UTC (offset zero).</summary>
    DateTimeOffset UtcNow { get; }
}
