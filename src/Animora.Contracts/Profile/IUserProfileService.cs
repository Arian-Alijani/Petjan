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
    Task SaveStaffAccessAsync(string membershipId, IReadOnlyList<string> permissionCodes, string? expectedChangeToken = null);
}
