using System.Diagnostics;
using Microsoft.Win32;

namespace Animora.Infrastructure.Local.Identity;

/// <summary>
/// Supplies the stable, machine-bound components that <c>DeviceFingerprint.Compute</c> hashes into the installation id hash sent to
/// the server. The raw components never leave the machine: callers hash them immediately and transmit only the 64-hex digest.
/// </summary>
public interface IDeviceFingerprintSource
{
    /// <summary>Returns the non-empty, unique, stable machine components (never throws; always at least one).</summary>
    IReadOnlyList<string> Collect();
}

/// <summary>
/// Default <see cref="IDeviceFingerprintSource"/>: machine name, OS install marker (machine GUID / <c>machine-id</c> / platform UUID)
/// and primary volume marker. Each component is labelled so two empty parts can never collide, and missing parts are simply omitted
/// (the OS platform component is always present, so the list is never empty).
/// </summary>
public sealed class DeviceFingerprintSource : IDeviceFingerprintSource
{
    /// <inheritdoc />
    public IReadOnlyList<string> Collect()
    {
        var parts = new List<string>(4);
        Add(parts, "machine", Safe(() => Environment.MachineName));
        Add(parts, "install", Safe(InstallMarker));
        Add(parts, "volume", Safe(VolumeMarker));
        Add(parts, "os", Safe(() => Environment.OSVersion.Platform.ToString()));
        return parts;
    }

    private static void Add(List<string> parts, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !value.Contains('\u001f', StringComparison.Ordinal)) parts.Add($"{label}:{value.Trim()}");
    }

    private static string InstallMarker()
    {
        if (OperatingSystem.IsWindows())
            return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string ?? string.Empty;
        if (OperatingSystem.IsMacOS()) return MacPlatformUuid();
        return File.Exists("/etc/machine-id") ? File.ReadAllText("/etc/machine-id").Trim() : string.Empty;
    }

    private static string VolumeMarker()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
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
