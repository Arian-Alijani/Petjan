using Animora.Contracts.Navigation;
using Animora.Domain.Identity;

namespace Animora.Client.Services.Mapping;

/// <summary>Explicit, exhaustive <see cref="StaffRole"/> → <see cref="UserRole"/> mapping (B03c).</summary>
public static class RoleMapping
{
    public static UserRole ToUserRole(StaffRole role) => role switch
    {
        StaffRole.Manager => UserRole.Manager,
        StaffRole.Doctor => UserRole.Doctor,
        StaffRole.Receptionist => UserRole.Receptionist,
        StaffRole.Cashier => UserRole.Cashier,
        // Fallback: UserRole has no Accountant member yet, so accountants get the Manager shell variant
        // until the Contracts enum gains one. Screen/action gating is decided by permissions (B04), not this.
        StaffRole.Accountant => UserRole.Manager,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown staff role."),
    };

    /// <summary>Stable, non-localized role key (matches <c>RoleDefinition.DisplayKey</c>); UI text is Desktop-owned.</summary>
    public static string ToRoleKey(StaffRole role) => role switch
    {
        StaffRole.Manager => "role.manager",
        StaffRole.Doctor => "role.doctor",
        StaffRole.Receptionist => "role.receptionist",
        StaffRole.Cashier => "role.cashier",
        StaffRole.Accountant => "role.accountant",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown staff role."),
    };
}
