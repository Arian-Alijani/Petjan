namespace Animora.Application.Ports;

/// <summary>
/// Read-only view of this installation's tenant and device identity. Implemented by
/// <c>Animora.Infrastructure.Local</c> (Application may not reference the local store — CONVENTIONS §2).
/// </summary>
public interface ILocalInstallationIdentity
{
    string TenantId { get; }

    string DeviceId { get; }
}
