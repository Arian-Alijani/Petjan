using System.Security.Cryptography;
using System.Text;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Security;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local.Storage;

/// <summary>
/// Immutable storage identity for one server/clinic login workspace. A new workspace (not a mutable
/// database path on a live provider) is required when switching tenants, so existing scopes and workers
/// can never cross stores. The legacy local database is deliberately neither opened nor migrated here.
/// </summary>
public sealed class ActiveTenantStore
{
    public ActiveTenantStore(string dataRoot, string serverAddress, string tenantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        var server = new Uri(serverAddress, UriKind.Absolute);
        if (server.Scheme is not ("https" or "http")) throw new ArgumentException("HTTP server required.");
        var identity = server.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/')
            + "\u001f" + tenantCode.Trim().ToUpperInvariant();
        Slug = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        DataDirectory = Path.Combine(Path.GetFullPath(dataRoot), "tenants", Slug);
    }

    public string Slug { get; }
    public string DataDirectory { get; }
    public string DatabasePath => Path.Combine(DataDirectory, "animora.db");

    public void ConfigureStore(LocalStoreOptions options)
    {
        options.DatabasePath = DatabasePath;
        options.BackupDirectory = Path.Combine(DataDirectory, "backups");
        options.SeedSampleData = false;
        options.ClinicBakPreviewPath = null;
    }

    public ISecretStore CreateSecretStore(IOptions<LocalStoreOptions> options, ILogger<FileFallbackSecretStore> logger)
    {
        if (OperatingSystem.IsWindows()) return new DpapiSecretStore(Path.Combine(DataDirectory, "secrets"));
        if (OperatingSystem.IsMacOS()) return new NamespacedSecretStore(new KeychainSecretStore(), Slug);
        if (options.Value.AllowInsecureKeyFallback) return new FileFallbackSecretStore(options, logger);
        return new UnavailableSecretStore();
    }

    private sealed class NamespacedSecretStore(ISecretStore inner, string slug) : ISecretStore
    {
        private string Key(string name) => $"tenant-{slug}-{name}";
        public Result<byte[]> TryRead(string name)
        {
            var result = inner.TryRead(Key(name));
            return result.IsFailure && result.Error == ErrorCodes.StoreKeyUnavailable && result.Detail == "security exit 44"
                ? Result<byte[]>.Fail(ErrorCodes.NotFound) : result;
        }
        public Result Write(string name, byte[] value) => inner.Write(Key(name), value);
        public Result Delete(string name) => inner.Delete(Key(name));
    }

    private sealed class UnavailableSecretStore : ISecretStore
    {
        public Result<byte[]> TryRead(string name) => Result<byte[]>.Fail(ErrorCodes.StoreKeyUnavailable);
        public Result Write(string name, byte[] value) => Result.Fail(ErrorCodes.StoreKeyUnavailable);
        public Result Delete(string name) => Result.Fail(ErrorCodes.StoreKeyUnavailable);
    }
}
