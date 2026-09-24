using System.Text.RegularExpressions;
using Animora.Domain.Results;

namespace Animora.Infrastructure.Local.Security;

/// <summary>Named secret storage backed by the OS keystore (CONVENTIONS §7). Never throws to callers.</summary>
public interface ISecretStore
{
    Result<byte[]> TryRead(string name);
    Result Write(string name, byte[] value);
    Result Delete(string name);
}

internal static partial class SecretName
{
    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex Pattern();

    public static Result Validate(string? name) =>
        !string.IsNullOrEmpty(name) && Pattern().IsMatch(name)
            ? Result.Ok()
            : Result.Fail(ErrorCodes.ValidationFailed, "Secret name must match ^[a-z0-9-]+$.");
}
