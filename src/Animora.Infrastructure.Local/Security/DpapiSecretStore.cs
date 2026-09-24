using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Animora.Domain.Results;

namespace Animora.Infrastructure.Local.Security;

/// <summary>Windows DPAPI (CurrentUser) blobs under <c>%LOCALAPPDATA%/Animora/secrets</c>, ACL'd to the current user.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _directory;

    public DpapiSecretStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Animora", "secrets");
    }

    public Result<byte[]> TryRead(string name)
    {
        var valid = SecretName.Validate(name);
        if (valid.IsFailure) return Result<byte[]>.Fail(valid.Error, valid.Detail);
        try
        {
            var path = PathFor(name);
            if (!File.Exists(path)) return Result<byte[]>.Fail(ErrorCodes.NotFound, $"Secret '{name}' not found.");
            return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
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
            var dir = Directory.CreateDirectory(_directory);
            RestrictDirectory(dir);
            var path = PathFor(name);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, ProtectedData.Protect(value, null, DataProtectionScope.CurrentUser));
            File.Move(temp, path, overwrite: true);
            RestrictFile(new FileInfo(path));
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
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

    private string PathFor(string name) => Path.Combine(_directory, name + ".bin");

    private static void RestrictDirectory(DirectoryInfo dir)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(CurrentUser(), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
            AccessControlType.Allow));
        dir.SetAccessControl(security);
    }

    private static void RestrictFile(FileInfo file)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(CurrentUser(), FileSystemRights.FullControl,
            AccessControlType.Allow));
        file.SetAccessControl(security);
    }

    private static SecurityIdentifier CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!;
    }
}
