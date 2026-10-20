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

    /// <summary>
    /// Active business rule-set version the client authors commands under (A-RULE 037). Stamped onto every
    /// wire-v2 <see cref="Commands.CommandEnvelope.RuleSetVersion"/> so the server can reject a command authored
    /// under a rule set it no longer supports (<c>RULESET_UNSUPPORTED</c>) before any handler runs. Must stay in
    /// lock-step with the server's <c>RuleSetPolicy.Current</c>; bump only on a business-rule-set change.
    /// </summary>
    public const int RuleSetVersion = 1;

    /// <summary><see cref="RuleSetVersion"/> as the canonical integer-string the envelope carries on the wire.</summary>
    public static readonly string RuleSetVersionString = RuleSetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Header carrying the comma-separated supported versions on a <c>426</c> response.</summary>
    public const string SupportedVersionsHeaderName = "X-Animora-Supported-Versions";

    /// <summary>Every wire version the server still accepts (v1 envelopes remain valid).</summary>
    public static IReadOnlyList<int> SupportedContractVersions { get; } = [1, 2];

    /// <summary>True when <paramref name="version"/> is a supported wire-contract version.</summary>
    public static bool IsSupported(int version) => version is 1 or 2;

    /// <summary>HTTP header carrying <see cref="Version"/> on every wire request.</summary>
    public const string HeaderName = "X-Animora-Contract-Version";
}
