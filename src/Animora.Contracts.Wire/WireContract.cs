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
    public const int Version = 2;

    /// <summary>Header carrying the comma-separated supported versions on a <c>426</c> response.</summary>
    public const string SupportedVersionsHeaderName = "X-Animora-Supported-Versions";

    /// <summary>Every wire version the server still accepts (v1 envelopes remain valid).</summary>
    public static IReadOnlyList<int> SupportedContractVersions { get; } = [1, 2];

    /// <summary>True when <paramref name="version"/> is a supported wire-contract version.</summary>
    public static bool IsSupported(int version) => version is 1 or 2;

    /// <summary>HTTP header carrying <see cref="Version"/> on every wire request.</summary>
    public const string HeaderName = "X-Animora-Contract-Version";
}
