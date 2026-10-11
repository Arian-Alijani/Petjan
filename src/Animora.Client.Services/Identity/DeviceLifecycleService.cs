using Animora.Application.Security;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Identity;

namespace Animora.Client.Services.Identity;

/// <summary>
/// Device lifecycle against the server: registers this installation (<c>POST /auth/device/register</c>, idempotent per user + installation
/// hash) and — when the server reports the device revoked — wipes every piece of offline authority (session, credential mirror, sealed
/// licence, capability snapshot), so from the next contact on this installation can no longer act offline.
/// </summary>
public sealed class DeviceLifecycleService(OnlineApi api, IDeviceFingerprintSource fingerprint, IOfflineAuthorityWiper wiper)
{
    private const int MaxDeviceName = 128;

    /// <summary>Registers (or re-reads) this installation's device. <c>DEVICE_REVOKED</c> triggers <see cref="HandleRevokedAsync"/>.</summary>
    public async Task<Result<DeviceRegisterResponse>> RegisterAsync(string bearer, CancellationToken ct = default)
    {
        var request = new DeviceRegisterRequest(DeviceName(), InstallationHash.Compute(fingerprint), Platform());
        var exchange = await api.SendAsync(HttpMethod.Post, "auth/device/register", request, bearer, ct).ConfigureAwait(false);
        if (exchange.Response is null) return Result<DeviceRegisterResponse>.Fail(ErrorCodes.NetworkUnavailable, exchange.Detail);

        using var response = exchange.Response;
        if (response.IsSuccessStatusCode)
        {
            var body = await OnlineApi.ReadAsync<DeviceRegisterResponse>(response, ct).ConfigureAwait(false);
            return body is null
                ? Result<DeviceRegisterResponse>.Fail(ErrorCodes.SyncProtocolViolation, "Device register response is empty.")
                : Result<DeviceRegisterResponse>.Ok(body);
        }

        var code = await OnlineApi.ReadErrorCodeAsync(response, ct).ConfigureAwait(false);
        if (string.Equals(code, ErrorCodes.DeviceRevoked.Code, StringComparison.Ordinal))
        {
            await HandleRevokedAsync(ct).ConfigureAwait(false);
            return Result<DeviceRegisterResponse>.Fail(ErrorCodes.DeviceRevoked);
        }

        return OnlineApi.IsTransient(response.StatusCode)
            ? Result<DeviceRegisterResponse>.Fail(ErrorCodes.ServerBusy, $"Server answered {(int)response.StatusCode}.")
            : Result<DeviceRegisterResponse>.Fail((int)response.StatusCode is 401 ? ErrorCodes.SessionExpired : ErrorCodes.ValidationFailed,
                $"Server answered {(int)response.StatusCode}.");
    }

    /// <summary>The server revoked this device: offline authority is gone (idempotent).</summary>
    public Task<Result> HandleRevokedAsync(CancellationToken ct = default) => wiper.WipeAsync(ct);

    private static string DeviceName()
    {
        var name = Environment.MachineName;
        if (string.IsNullOrWhiteSpace(name)) name = "Animora desktop";
        return name.Length <= MaxDeviceName ? name : name[..MaxDeviceName];
    }

    private static string Platform() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
}
