namespace Animora.Contracts.Wire.Auth;

/// <summary>
/// Body of the authenticated <c>POST /auth/device/register</c> (S04). Creates a device in
/// <c>Pending</c>; a Manager approves it later via an S05 command.
/// </summary>
/// <param name="DeviceName">Human-readable device label.</param>
/// <param name="InstallationIdHash">Opaque per-installation hash (never the raw installation id).</param>
/// <param name="Platform">Client platform string (e.g. "windows").</param>
public sealed record DeviceRegisterRequest(
    string DeviceName,
    string InstallationIdHash,
    string Platform);
