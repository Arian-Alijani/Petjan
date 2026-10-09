namespace Animora.Contracts.Wire.Auth;

/// <summary>Body of <c>POST /auth/device/{id}/revoke</c> (S05).</summary>
/// <param name="Reason">Why the device is revoked (stored on the device and audited).</param>
public sealed record DeviceRevokeRequest(string Reason);
