namespace Animora.Contracts.Wire;

/// <summary>
/// Versioned wire-contract metadata shared by every replicated/command DTO that
/// crosses the desktop↔server boundary. The version is a first-class constant
/// from day one (ARCHITECTURE.md §8.6): an unsupported version triggers a
/// controlled resync, never a silent mis-map. Bumped only by a phase that
/// changes a wire shape (ROADMAP §3.8).
/// </summary>
public static class WireContract
{
    /// <summary>Current wire contract version. Bump only on a wire-shape change.</summary>
    public const int Version = 1;

    /// <summary>HTTP header carrying <see cref="Version"/> on every wire request.</summary>
    public const string HeaderName = "X-Animora-Contract-Version";
}
