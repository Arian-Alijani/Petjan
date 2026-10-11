using Animora.Infrastructure.Local.Identity;
using Animora.Server.Licensing.Verification;

namespace Animora.Client.Services.Identity;

/// <summary>The only place raw machine components become the 64-hex installation hash; the raw values never leave this call.</summary>
public static class InstallationHash
{
    /// <summary>Hashes <paramref name="source"/>'s components with <see cref="DeviceFingerprint.Compute"/>.</summary>
    public static string Compute(IDeviceFingerprintSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return DeviceFingerprint.Compute(source.Collect());
    }
}
