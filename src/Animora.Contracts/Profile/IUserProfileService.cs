using System.Collections.Generic;
using System.Threading.Tasks;

namespace Animora.Contracts.Profile;

/// <summary>
/// Durable profile, settings and clinic staff management seam.
/// Authorization and validation are enforced by Application, never by the view.
/// </summary>
public interface IUserProfileService
{
    /// <summary>The signed-in user's profile (identity, team, sessions, stats).</summary>
    Task<UserProfile> GetAsync();

    /// <summary>Persists the edited profile fields in the local store.</summary>
    Task SaveAsync(UserProfile profile);

    /// <summary>Every editable preference section the settings screen exposes.</summary>
    Task<AppSettings> GetSettingsAsync();

    /// <summary>Persists the edited settings in the local store.</summary>
    Task SaveSettingsAsync(AppSettings settings);

    /// <summary>Account status from the configured authority, or explicit unknown fields before server integration.</summary>
    Task<ClinicAccountStatus> GetAccountStatusAsync();

    Task<StaffDirectory> GetStaffAsync();
    Task AddStaffAsync(CreateStaffRequest request);
    Task SetStaffActiveAsync(string membershipId, bool active, string? expectedChangeToken = null);

    /// <summary>Replaces a staff member's password (needs users.manage; changing another manager's also needs roles.manage).</summary>
    Task SetStaffPasswordAsync(string membershipId, string newPassword, string? expectedChangeToken = null);

    Task SaveStaffAccessAsync(string membershipId, IReadOnlyList<string> permissionCodes, string? expectedChangeToken = null);

    /// <summary>Approves a staff member's pending device (server-authoritative; needs the devices-manage permission).</summary>
    Task ApproveStaffDeviceAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Revokes a staff member's device with an audited reason (server-authoritative; needs the devices-manage permission).</summary>
    Task RevokeStaffDeviceAsync(string deviceId, string reason, CancellationToken ct = default);

    /// <summary>Re-enables a previously revoked staff device (server-authoritative; needs the devices-manage permission).</summary>
    Task ReinstateStaffDeviceAsync(string deviceId, CancellationToken ct = default);
}
