using Animora.Contracts.Identity;
using Animora.Contracts.Navigation;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Fixed design-time identity returning the reference's default "manager"
/// role (دکتر آریا نیک / مدیر کلینیک / آ, per ROLES in
/// Animora-UI/assets/layout.js). This is the seam a future auth phase
/// replaces with a real session-backed implementation.
/// </summary>
public sealed class MockCurrentUserContext : ICurrentUserContext
{
    public string Name => "دکتر آریا نیک";

    public string RoleLabel => "مدیر کلینیک";

    public string Initials => "آ";

    public UserRole Role => UserRole.Manager;
}
