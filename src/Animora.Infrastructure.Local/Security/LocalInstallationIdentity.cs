using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Context;

namespace Animora.Infrastructure.Local.Security;

/// <summary>
/// <see cref="ILocalInstallationIdentity"/> over the bootstrapped installation row: tenant from
/// <see cref="LocalInstallationState"/>; device = the installation id, or — before bootstrap — the machine
/// fingerprint hash from <see cref="InstallationIdentity"/> (computed once).
/// </summary>
public sealed class LocalInstallationIdentity(LocalInstallationState state, InstallationIdentity identity)
    : ILocalInstallationIdentity
{
    private readonly Lazy<string> _fingerprint = new(identity.ComputeMachineFingerprintHash);

    public string TenantId => state.TenantId;

    public string DeviceId => state.Installation?.InstallationId is { Length: > 0 } id ? id : _fingerprint.Value;
}
