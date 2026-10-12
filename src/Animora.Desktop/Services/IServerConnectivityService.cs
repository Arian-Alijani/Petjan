using System.Threading;
using System.Threading.Tasks;

namespace Animora.Desktop.Services;

/// <summary>The three states the login screen shows for the link to the central server.</summary>
public enum ServerConnectivityState
{
    /// <summary>A probe has not finished yet.</summary>
    Unknown,

    /// <summary>The server answered its liveness probe: the internet and the server are both reachable.</summary>
    Online,

    /// <summary>The server is configured but did not answer (no internet, server down, or timed out).</summary>
    Offline,

    /// <summary>No server address is configured; this build runs local-only, so there is nothing to reach.</summary>
    NotConfigured,
}

/// <summary>
/// Probes whether the central sync server is reachable, used by the login screen to show a live
/// connection indicator. It never throws and never signs anything in — it only pings the public
/// liveness endpoint so the user can tell an unreachable-server problem apart from a wrong password.
/// </summary>
public interface IServerConnectivityService
{
    /// <summary>Performs one liveness probe and returns the resulting state.</summary>
    Task<ServerConnectivityState> CheckAsync(CancellationToken ct = default);
}
