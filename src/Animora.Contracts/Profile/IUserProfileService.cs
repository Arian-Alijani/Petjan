using System.Threading.Tasks;

namespace Animora.Contracts.Profile;

/// <summary>
/// Backend seam for the Profile &amp; Settings screen (R-18). The mock
/// implementation lives in the Desktop layer; a future backend implements the
/// same interface and the DI registration swaps it with zero View/ViewModel
/// changes. Persistence is in-memory only for the session, mirroring the
/// reference prototype's explicit "settings live in memory only" design.
/// </summary>
public interface IUserProfileService
{
    /// <summary>The signed-in user's profile (identity, team, sessions, stats).</summary>
    Task<UserProfile> GetAsync();

    /// <summary>Persists the edited profile fields (session-scoped in the mock).</summary>
    Task SaveAsync(UserProfile profile);

    /// <summary>Every editable preference section the settings screen exposes.</summary>
    Task<AppSettings> GetSettingsAsync();

    /// <summary>Persists the edited settings (session-scoped in the mock).</summary>
    Task SaveSettingsAsync(AppSettings settings);
}
