using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Animora.Infrastructure.Local.Security;

/// <summary>
/// Local, offline machine fingerprint (anti-cloning anchor for D11/E07). Missing parts contribute a stable
/// empty string; never throws.
/// </summary>
public sealed class InstallationIdentity
{
    /// <summary>Lowercase hex SHA-256 of machine name + OS install marker + primary volume marker.</summary>
    public string ComputeMachineFingerprintHash()
    {
        var material = string.Join('|', Safe(() => Environment.MachineName), Safe(InstallMarker), Safe(VolumeMarker));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string InstallMarker()
    {
        if (OperatingSystem.IsWindows())
            return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string
                   ?? string.Empty;
        if (OperatingSystem.IsMacOS()) return MacPlatformUuid();
        return File.Exists("/etc/machine-id") ? File.ReadAllText("/etc/machine-id").Trim() : string.Empty;
    }

    private static string VolumeMarker()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        // GetVolumeInformation-free: stable properties of the system drive.
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root)) return string.Empty;
        var drive = new DriveInfo(root);
        return drive.IsReady ? $"{drive.Name}:{drive.DriveFormat}:{drive.VolumeLabel}:{drive.TotalSize}" : string.Empty;
    }

    private static string MacPlatformUuid()
    {
        var info = new ProcessStartInfo("/usr/sbin/ioreg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-rd1", "-c", "IOPlatformExpertDevice" }) info.ArgumentList.Add(a);
        using var process = Process.Start(info);
        if (process is null) return string.Empty;
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(5_000)) return string.Empty;
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains("\"IOPlatformUUID\"", StringComparison.Ordinal)) continue;
            var parts = line.Split('"');
            return parts.Length >= 4 ? parts[3] : string.Empty;
        }
        return string.Empty;
    }

    private static string Safe(Func<string> part)
    {
        try
        {
            return part() ?? string.Empty;
        }
#pragma warning disable CA1031 // Fingerprint parts are best-effort by contract: never throw.
        catch (Exception)
#pragma warning restore CA1031
        {
            return string.Empty;
        }
    }
}
