using Animora.Domain.Primitives;
using Animora.Domain.Time;

namespace Animora.Domain.Identity;

/// <summary>Lifecycle state of a registered installation.</summary>
public enum DeviceStatus
{
    /// <summary>Registered but not yet approved by a manager.</summary>
    Pending = 0,

    /// <summary>Approved; may hold a capability snapshot and sync.</summary>
    Active = 1,

    /// <summary>Revoked; every snapshot bound to it is invalid.</summary>
    Revoked = 2,
}

/// <summary>
/// One approved installation of the desktop app, bound to a user. A
/// <see cref="CapabilitySnapshot"/> is issued per device, so revoking a device immediately
/// removes its offline authority.
/// </summary>
public sealed class Device : AggregateRoot
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Device()
    {
    }

    /// <summary>The <see cref="User"/> this installation belongs to.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Operator-visible name, e.g. <c>"Reception PC"</c>.</summary>
    public string DeviceName { get; private set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex of the sealed per-installation identity. The identity itself never leaves the
    /// device (it lives in the OS keystore); only this hash is stored and replicated, and D11's
    /// license binding compares against it.
    /// </summary>
    public string InstallationIdHash { get; private set; } = string.Empty;

    /// <summary>Platform moniker, e.g. <c>"windows"</c>.</summary>
    public string Platform { get; private set; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public DeviceStatus Status { get; private set; } = DeviceStatus.Pending;

    /// <summary>When the installation first registered, UTC.</summary>
    public DateTimeOffset RegisteredUtc { get; private set; }

    /// <summary>Last server contact, UTC; <see langword="null"/> before the first one.</summary>
    public DateTimeOffset? LastContactUtc { get; private set; }

    /// <summary>When the device was revoked, UTC; <see langword="null"/> while it is not.</summary>
    public DateTimeOffset? RevokedUtc { get; private set; }

    /// <summary>Why the device was revoked; kept for the audit trail.</summary>
    public string? RevocationReason { get; private set; }

    /// <summary>Registers a device in <see cref="DeviceStatus.Pending"/>.</summary>
    public static Device Register(
        string tenantId,
        string userId,
        string deviceName,
        string installationIdHash,
        string platform,
        IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationIdHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        return new Device
        {
            Id = IdGenerator.NewId(now),
            TenantId = tenantId,
            UserId = userId,
            DeviceName = deviceName.Trim(),
            InstallationIdHash = installationIdHash,
            Platform = platform,
            Status = DeviceStatus.Pending,
            RegisteredUtc = now,
        };
    }

    /// <summary><see langword="true"/> only for an approved, unrevoked device.</summary>
    public bool IsUsable => Status == DeviceStatus.Active;

    /// <summary>
    /// Refreshes the operator-visible description (name + platform) from a later <c>register</c> call. The first
    /// server contact for an installation happens at <c>login</c>, which can only bind placeholder values
    /// (<c>"Unnamed device"</c> / <c>"unknown"</c>) because the login request carries no device metadata; the client's
    /// authenticated <c>register</c> call then supplies the real machine name and platform. A revoked device keeps
    /// its stored description so the audit trail stays truthful. Returns <see langword="true"/> when anything changed.
    /// </summary>
    public bool DescribeInstallation(string? deviceName, string? platform)
    {
        if (Status == DeviceStatus.Revoked)
        {
            return false;
        }

        bool changed = false;

        string? name = deviceName?.Trim();
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, DeviceName, StringComparison.Ordinal))
        {
            DeviceName = name;
            changed = true;
        }

        string? platformValue = platform?.Trim();
        if (!string.IsNullOrWhiteSpace(platformValue) && !string.Equals(platformValue, Platform, StringComparison.Ordinal))
        {
            Platform = platformValue;
            changed = true;
        }

        return changed;
    }

    /// <summary>Approves a pending device. A revoked device is never re-approved.</summary>
    /// <exception cref="InvalidOperationException">The device is revoked.</exception>
    public void Approve()
    {
        if (Status == DeviceStatus.Revoked)
        {
            throw new InvalidOperationException("A revoked device cannot be approved; register the installation again.");
        }

        Status = DeviceStatus.Active;
    }

    /// <summary>Records server contact.</summary>
    public void RecordContact(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        LastContactUtc = clock.UtcNow;
    }

    /// <summary>
    /// Revokes the device. Idempotent: a second call keeps the original instant and reason so the
    /// audit trail stays truthful.
    /// </summary>
    public void Revoke(string reason, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == DeviceStatus.Revoked)
        {
            return;
        }

        Status = DeviceStatus.Revoked;
        RevokedUtc = clock.UtcNow;
        RevocationReason = reason.Trim();
    }
}
