namespace Animora.Contracts.Navigation;

/// <summary>
/// Clinic staff roles, mirroring the reference's ROLES keys
/// (manager/cashier/doctor in Animora-UI/assets/layout.js). A future auth
/// phase drives which role the current session resolves to.
/// </summary>
public enum UserRole
{
    Manager,
    Cashier,
    Doctor,

    /// <summary>Front desk (B03c). Appended so the existing numeric values are preserved.</summary>
    Receptionist,
}
