using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Animora.Server.Hosting;

/// <summary>
/// Thrown when a literal secret is found in a JSON configuration file. The
/// message names the offending key path only — never the value (ARCHITECTURE.md §4).
/// </summary>
internal sealed class SecretInConfigurationException(string keyPath)
    : Exception($"A secret-looking value was found in a JSON configuration file at key '{keyPath}'. " +
                "Secrets must come from environment variables or the OS secret store, never appsettings*.json.")
{
    /// <summary>The configuration key path that triggered the guard.</summary>
    public string KeyPath { get; } = keyPath;
}

/// <summary>
/// Refuses to boot if a literal connection string or secret appears in a bound
/// JSON configuration file (ARCHITECTURE.md §4: "Secrets never live in config
/// files"). Only JSON providers (file and stream) are scanned;
/// environment-variable and user-secrets providers are exempt. Called in startup
/// step (1).
/// </summary>
internal static partial class SecretGuard
{
    [GeneratedRegex(@"(?i)(password|pwd|secret|connectionstring|apikey|privatekey)")]
    private static partial Regex SensitiveKeyRegex();

    /// <summary>
    /// Scans the JSON providers of <paramref name="configuration"/>; throws
    /// <see cref="SecretInConfigurationException"/> on the first literal secret.
    /// </summary>
    public static void ThrowIfSecretsInJsonFiles(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration is not IConfigurationRoot root)
        {
            return;
        }

        foreach (var provider in root.Providers)
        {
            if (provider is not (JsonConfigurationProvider or JsonStreamConfigurationProvider))
            {
                // Environment variables, user-secrets, in-memory, etc. are exempt.
                continue;
            }

            foreach (var keyPath in EnumerateKeys(provider, parentPath: null))
            {
                if (!provider.TryGet(keyPath, out var value) || string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var leafKey = LeafOf(keyPath);
                if (SensitiveKeyRegex().IsMatch(leafKey) && LooksLikeLiteralSecret(leafKey, value))
                {
                    throw new SecretInConfigurationException(keyPath);
                }
            }
        }
    }

    /// <summary>Depth-first enumeration of every leaf key a provider exposes.</summary>
    private static IEnumerable<string> EnumerateKeys(IConfigurationProvider provider, string? parentPath)
    {
        foreach (var child in provider.GetChildKeys([], parentPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = parentPath is null
                ? child
                : ConfigurationPath.Combine(parentPath, child);

            // A node can be both a value and a parent; yield it, then recurse.
            yield return path;

            foreach (var nested in EnumerateKeys(provider, path))
            {
                yield return nested;
            }
        }
    }

    private static string LeafOf(string keyPath)
    {
        var separator = keyPath.LastIndexOf(ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
        return separator < 0 ? keyPath : keyPath[(separator + ConfigurationPath.KeyDelimiter.Length)..];
    }

    /// <summary>
    /// A value is treated as a literal secret when it carries connection-string
    /// markers (<c>Password=</c>/<c>Pwd=</c>/<c>Host=</c> with a <c>;</c>) or when
    /// it is a non-empty value under a password/secret/apikey/privatekey key.
    /// </summary>
    private static bool LooksLikeLiteralSecret(string leafKey, string value)
    {
        var hasConnectionMarkers = value.Contains(';', StringComparison.Ordinal) &&
            (value.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("Pwd=", StringComparison.OrdinalIgnoreCase) ||
             value.Contains("Host=", StringComparison.OrdinalIgnoreCase));

        if (hasConnectionMarkers)
        {
            return true;
        }

        var isCredentialKey =
            leafKey.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            leafKey.Contains("pwd", StringComparison.OrdinalIgnoreCase) ||
            leafKey.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            leafKey.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
            leafKey.Contains("privatekey", StringComparison.OrdinalIgnoreCase);

        // A non-empty value under a credential-named key is itself the secret.
        return isCredentialKey && !string.IsNullOrWhiteSpace(value);
    }
}
