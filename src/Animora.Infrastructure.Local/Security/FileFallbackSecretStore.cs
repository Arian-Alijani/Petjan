using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local.Security;

/// <summary>
/// DEV ONLY: plaintext secrets at <c>&lt;data&gt;/secrets/&lt;name&gt;.key</c>. Refuses construction unless
/// <see cref="LocalStoreOptions.AllowInsecureKeyFallback"/> is set; warns on every read (CONVENTIONS §7).
/// </summary>
public sealed class FileFallbackSecretStore : ISecretStore
{
    private readonly string _directory;
    private readonly ILogger<FileFallbackSecretStore> _logger;

    public FileFallbackSecretStore(IOptions<LocalStoreOptions> options, ILogger<FileFallbackSecretStore> logger)
    {
        var store = options.Value;
        if (!store.AllowInsecureKeyFallback)
            throw new InvalidOperationException(
                "Insecure key fallback is disabled (LocalStore:AllowInsecureKeyFallback=false).");
        _directory = Path.Combine(store.DataDirectory, "secrets");
        _logger = logger;
    }

    public Result<byte[]> TryRead(string name)
    {
        var valid = SecretName.Validate(name);
        if (valid.IsFailure) return Result<byte[]>.Fail(valid.Error, valid.Detail);
        _logger.LogWarning("Reading secret '{SecretName}' from the INSECURE plaintext file fallback (dev only).", name);
        try
        {
            var path = PathFor(name);
            return File.Exists(path)
                ? File.ReadAllBytes(path)
                : Result<byte[]>.Fail(ErrorCodes.NotFound, $"Secret '{name}' not found.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<byte[]>.Fail(ErrorCodes.StoreKeyUnavailable, ex.GetType().Name);
        }
    }

    public Result Write(string name, byte[] value)
    {
        var valid = SecretName.Validate(name);
        if (valid.IsFailure) return valid;
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(PathFor(name), value);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(PathFor(name), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail(ErrorCodes.StoreKeyUnavailable, ex.GetType().Name);
        }
    }

    public Result Delete(string name)
    {
        var valid = SecretName.Validate(name);
        if (valid.IsFailure) return valid;
        try
        {
            File.Delete(PathFor(name));
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail(ErrorCodes.StoreKeyUnavailable, ex.GetType().Name);
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name + ".key");
}
