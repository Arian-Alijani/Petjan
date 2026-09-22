using Animora.Contracts.Navigation;

namespace Animora.Contracts.Identity;

/// <summary>
/// The identity of the signed-in clinic user, consumed by the shell's user
/// chip / side-user card. Mirrors one entry of the reference's ROLES map
/// (Animora-UI/assets/layout.js). A future auth phase replaces the mock
/// implementation with a real session-backed one — no shell/View changes.
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>Stable user id, e.g. "u-aria". The audit/actor seam a future auth layer fills.</summary>
    string UserId { get; }

    /// <summary>Full display name, e.g. "دکتر آریا نیک".</summary>
    string Name { get; }

    /// <summary>Role label, e.g. "مدیر کلینیک".</summary>
    string RoleLabel { get; }

    /// <summary>Avatar initials, e.g. "آ".</summary>
    string Initials { get; }

    /// <summary>Structured role, for role-based screen variants (Phase 06+).</summary>
    UserRole Role { get; }
}
