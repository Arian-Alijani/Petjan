using System.Security.Cryptography;
using System.Text;
namespace Animora.Server.Licensing.Verification;

public static class DeviceFingerprint
{
    public static string Compute(IReadOnlyList<string> stableComponents)
    {
        ArgumentNullException.ThrowIfNull(stableComponents);
        var normalized = stableComponents.Select(x => (x ?? string.Empty).Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant()).ToArray();
        if (normalized.Length == 0 || normalized.Any(x => x.Length == 0 || x.Contains('\u001f'))
            || normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length) throw new ArgumentException("Components must be nonempty and unique.", nameof(stableComponents));
        Array.Sort(normalized, StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("animora.installation.v1\u001f" + string.Join('\u001f', normalized))));
    }
}
