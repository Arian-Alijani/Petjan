namespace Animora.Contracts.Wire.Auth;

/// <summary>Result of <c>POST /auth/device/register</c> (S04).</summary>
/// <param name="DeviceId">The newly created device's ULID.</param>
/// <param name="Status">Device status name (always <c>Pending</c> at registration).</param>
public sealed record DeviceRegisterResponse(
    string DeviceId,
    string Status);
