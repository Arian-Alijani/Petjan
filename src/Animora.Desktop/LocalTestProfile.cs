using Animora.Domain.Results;
using Animora.Infrastructure.Local.Security;
using Animora.Persistence.Sqlite.Options;
using Animora.Persistence.Sqlite.Seed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Animora.Desktop;

/// <summary>
/// Isolated manual-test storage, selected explicitly or by the Debug launch default. This changes only storage configuration, never authentication,
/// authorization or business services. No existing profile is deleted or reset.
/// </summary>
public sealed class LocalTestProfile
{
    private LocalTestProfile(string name, string dataRoot, string? clinicBakPreviewPath = null)
    {
        Name = name;
        ClinicBakPreviewPath = clinicBakPreviewPath;
        DataDirectory = Path.Combine(Path.GetFullPath(dataRoot), "Animora.LocalTests", name);
    }

    public string Name { get; }
    public string DataDirectory { get; }
    public string? ClinicBakPreviewPath { get; }
    public string WindowSuffix => ClinicBakPreviewPath is not null
        ? $" — بازبینی بکاپ قدیمی؛ غیرعملیاتی [{Name}]"
        : $" — تست محلی [{Name}]";

    public static LocalTestProfile? Parse(string[] args, string? dataRoot = null, bool useDefaultTestProfile = false)
    {
        string? name = null;
        var fresh = false;
        var clinic = false;
        string? previewPath = null;
        foreach (var arg in args)
        {
            if (arg == "--clinic-bak" || arg.StartsWith("--clinic-bak=", StringComparison.Ordinal))
            {
                if (previewPath is not null) throw new ArgumentException("Specify only one Clinic backup preview.");
                previewPath = arg == "--clinic-bak" ? Path.Combine(".sandbox", "clinic-bak", "clinic.json") : arg["--clinic-bak=".Length..];
                if (string.IsNullOrWhiteSpace(previewPath)) throw new ArgumentException("Clinic preview path cannot be empty.");
                continue;
            }
            if (arg.StartsWith("--clinic-bak", StringComparison.Ordinal))
                throw new ArgumentException("Use --clinic-bak[=preview.json].");
            if (arg == "--clinic")
            {
                clinic = true;
                continue;
            }
            if (arg == "--local-test-fresh")
            {
                fresh = true;
                continue;
            }
            if (arg != "--local-test" && !arg.StartsWith("--local-test=", StringComparison.Ordinal))
            {
                if (arg.StartsWith("--local-test", StringComparison.Ordinal))
                    throw new ArgumentException("Use --local-test[=name] or --local-test-fresh.");
                continue; // Leave Avalonia's own arguments intact.
            }
            if (name is not null)
                throw new ArgumentException("Specify only one local-test profile.");
            name = arg == "--local-test" ? "manual" : arg["--local-test=".Length..];
            if (name.Length is < 1 or > 40 || name.Any(c =>
                    !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
                throw new ArgumentException("Test profile names must be 1–40 lowercase letters, digits, '-' or '_'.");
        }
        if (previewPath is not null)
        {
            if (clinic || fresh || name is not null)
                throw new ArgumentException("--clinic-bak cannot be combined with other storage selectors.");
            previewPath = Path.GetFullPath(previewPath);
            return new LocalTestProfile(ClinicBakPreviewData.ProfileName(previewPath),
                dataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), previewPath);
        }
        if (name?.StartsWith(ClinicBakPreviewData.ProfilePrefix, StringComparison.Ordinal) == true)
            throw new ArgumentException("Clinic preview profiles can only be opened with --clinic-bak.");
        if (clinic && (name is not null || fresh))
            throw new ArgumentException("Use --clinic or a local-test profile, not both.");
        if (fresh)
        {
            if (name is not null)
                throw new ArgumentException("Use either a named test profile or --local-test-fresh, not both.");
            name = "fresh-" + Guid.NewGuid().ToString("N");
        }
        if (name is null && useDefaultTestProfile && !clinic)
            name = "manual";
        return name is null ? null : new LocalTestProfile(name,
            dataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    public void ConfigureStore(LocalStoreOptions options)
    {
        options.DatabasePath = Path.Combine(DataDirectory, "animora.db");
        options.BackupDirectory = Path.Combine(DataDirectory, "backups");
        // Reception fixtures create credential-less doctor users BEFORE owner setup and suppress
        // first-run detection. Start empty so the real setup flow can create the test Manager.
        options.SeedSampleData = false;
        options.ClinicBakPreviewPath = ClinicBakPreviewPath;
        options.PlaintextStoreAcknowledged = true; // isolated throw-away profile; SQL-01 warning is for shipped stores
        options.AllowInsecureKeyFallback = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();
    }

    public ISecretStore CreateSecretStore(IOptions<LocalStoreOptions> options, ILogger<FileFallbackSecretStore> logger)
    {
        if (OperatingSystem.IsWindows())
            return new DpapiSecretStore(Path.Combine(DataDirectory, "secrets"));
        if (OperatingSystem.IsMacOS())
            return new ProfileSecretStore(new KeychainSecretStore(), Name);
        // Opt-in Linux manual testing only; production still fails closed without an OS keystore.
        return new FileFallbackSecretStore(options, logger);
    }

    private sealed class ProfileSecretStore(ISecretStore inner, string profile) : ISecretStore
    {
        // SecretName permits hyphens but not underscores. Hash the validated profile name so
        // every accepted profile has a stable, collision-resistant keychain namespace.
        private readonly string _prefix = "local-test-" + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile)));
        private string Key(string name) => $"{_prefix}-{name}";
        public Result<byte[]> TryRead(string name)
        {
            var result = inner.TryRead(Key(name));
            // macOS security's specific errSecItemNotFound exit (44) is absence, not keystore failure.
            // Other failures remain fail-closed. This translation is confined to test-profile keys.
            return result.IsFailure && result.Error == ErrorCodes.StoreKeyUnavailable && result.Detail == "security exit 44"
                ? Result<byte[]>.Fail(ErrorCodes.NotFound)
                : result;
        }
        public Result Write(string name, byte[] value) => inner.Write(Key(name), value);
        public Result Delete(string name) => inner.Delete(Key(name));
    }
}
