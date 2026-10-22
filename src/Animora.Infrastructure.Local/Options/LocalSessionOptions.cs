namespace Animora.Infrastructure.Local.Options;

/// <summary>
/// Local session policy (A-RULE 029, revised). Remember-device never extends these lifetimes.
/// </summary>
/// <remarks>
/// Inactivity does <b>not</b> lock the session: leaving the app open and idle, or not interacting for a long
/// stretch, must never drop the shell to the read-only/locked state. The periodic online check the operator must
/// stay reachable for (subscription/entitlement) is governed by <see cref="AbsoluteLifetime"/> and the online
/// live-access loop, never by a local idle timer.
/// </remarks>
public sealed class LocalSessionOptions
{
    /// <summary>Hard lifetime; past it the session ends with <c>SESSION_EXPIRED</c>.</summary>
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(12);
}
