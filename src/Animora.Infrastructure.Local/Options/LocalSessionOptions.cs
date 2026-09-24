namespace Animora.Infrastructure.Local.Options;

/// <summary>Local session policy (A-RULE 029). Remember-device never extends these lifetimes.</summary>
public sealed class LocalSessionOptions
{
    /// <summary>Inactivity after which the session is <b>locked</b> (not signed out).</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Hard lifetime; past it the session ends with <c>SESSION_EXPIRED</c>.</summary>
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Minimum interval between persisted activity touches (PERFORMANCE §4/§12).</summary>
    public TimeSpan TouchThrottle { get; set; } = TimeSpan.FromSeconds(60);
}
