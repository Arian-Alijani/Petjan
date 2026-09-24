using System.Diagnostics;
using System.Runtime.Versioning;
using Animora.Domain.Results;

namespace Animora.Infrastructure.Local.Security;

/// <summary>macOS login keychain via <c>/usr/bin/security</c> (service "Animora", account = secret name).</summary>
/// <remarks>Secret values are never logged. Nonzero exit → failed Result, never an exception.</remarks>
[SupportedOSPlatform("macos")]
public sealed class KeychainSecretStore : ISecretStore
{
    private const string Tool = "/usr/bin/security";
    private const string Service = "Animora";

    public Result<byte[]> TryRead(string name)
    {
        var valid = SecretName.Validate(name);
        if (valid.IsFailure) return Result<byte[]>.Fail(valid.Error, valid.Detail);
        var run = Run("find-generic-password", "-s", Service, "-a", name, "-w");
        if (run.Exit != 0) return Result<byte[]>.Fail(ErrorCodes.StoreKeyUnavailable, $"security exit {run.Exit}");
        try
        {
            return Convert.FromBase64String(run.Output.Trim());
        }
        catch (FormatException)
        {
            return Result<byte[]>.Fail(ErrorCodes.StoreKeyUnavailable, "Keychain item is not valid base64.");
        }
    }

    public Result Write(string name, byte[] value)
    {
        var valid = SecretName.Validate(name);
        if (valid.IsFailure) return valid;
        var run = Run("add-generic-password", "-U", "-s", Service, "-a", name, "-w", Convert.ToBase64String(value));
        return run.Exit == 0 ? Result.Ok() : Result.Fail(ErrorCodes.StoreKeyUnavailable, $"security exit {run.Exit}");
    }

    public Result Delete(string name)
    {
        var valid = SecretName.Validate(name);
        if (valid.IsFailure) return valid;
        var run = Run("delete-generic-password", "-s", Service, "-a", name);
        return run.Exit == 0 ? Result.Ok() : Result.Fail(ErrorCodes.StoreKeyUnavailable, $"security exit {run.Exit}");
    }

    private static (int Exit, string Output) Run(params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(Tool)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var process = Process.Start(info);
            if (process is null) return (-1, string.Empty);
            var stdout = process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return (-1, string.Empty);
            }
            return (process.ExitCode, stdout.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (-1, string.Empty);
        }
    }
}
