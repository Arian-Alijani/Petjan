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

    /// <summary>Upper bound for the <c>Platform</c> field the server accepts (<c>LoginService.MaxPlatform</c>).</summary>
    private const int MaxPlatform = 64;

    /// <summary>
    /// A human-readable client platform string such as <c>"windows · Windows 10.0.19045"</c>. The leading token stays one of the
    /// stable codes (<c>windows</c>/<c>macos</c>/<c>linux</c>) so any code that keys on the family still works, and the OS
    /// version is appended so the manager's «دستگاه‌ها» card shows the real system instead of a bare/unknown value.
    /// </summary>
    private static string Platform()
    {
        string family = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

        string description;
        try
        {
            // e.g. "Microsoft Windows 10.0.19045"; trimmed of the "Microsoft " prefix for a shorter, clearer label.
            description = System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim();
            const string microsoft = "Microsoft ";
            if (description.StartsWith(microsoft, StringComparison.OrdinalIgnoreCase))
            {
                description = description[microsoft.Length..].Trim();
            }
        }
#pragma warning disable CA1031 // OS description is best-effort cosmetic data: never let it break registration.
        catch (Exception)
#pragma warning restore CA1031
        {
            description = string.Empty;
        }

        string platform = string.IsNullOrWhiteSpace(description) ? family : $"{family} · {description}";
        return platform.Length <= MaxPlatform ? platform : platform[..MaxPlatform];
    }
}
