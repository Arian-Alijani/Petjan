using P = Animora.Domain.Identity.PermissionCodes;

namespace Animora.Domain.Identity;

/// <summary>
/// The authoritative default role→permission matrix. Built from <see cref="PermissionCodes"/>
/// constants only — never re-typed literals — so renaming a constant is a compile error instead
/// of a silent authorization hole.
/// </summary>
/// <remarks>
/// These are <b>defaults</b>: a tenant's stored <see cref="RoleDefinition"/> may diverge, and a
/// <see cref="StaffMembership"/> may add or deny individual codes on top. Only
/// <see cref="StaffRole.Manager"/> and — for the finance subset — <see cref="StaffRole.Accountant"/>
/// receive <see cref="RiskLevel.Critical"/> codes by default.
/// </remarks>
public static class DefaultRolePermissions
{
    private static readonly IReadOnlyDictionary<StaffRole, IReadOnlySet<string>> Matrix =
        new Dictionary<StaffRole, IReadOnlySet<string>>
        {
            [StaffRole.Manager] = Set(P.All),

            [StaffRole.Doctor] = Set(
            [
                P.PatientsRead, P.PatientsWrite,
                P.VisitsRead, P.VisitsWrite, P.VisitsComplete,
                P.VaccinationRead, P.VaccinationWrite,
                P.AppointmentsRead, P.AppointmentsBook, P.AppointmentsMove,
                P.BoardingRead,
                P.InventoryRead,
                P.SalesRead, P.SalesDraft,
                P.ReportsRead,
                P.SettingsRead,
            ]),

            [StaffRole.Receptionist] = Set(
            [
                P.PatientsRead, P.PatientsWrite,
                P.ReceptionManage,
                P.AppointmentsRead, P.AppointmentsBook, P.AppointmentsMove, P.AppointmentsCancel,
                P.VisitsRead,
                P.VaccinationRead,
                P.BoardingRead, P.BoardingManage,
                P.SalesRead, P.SalesDraft,
                P.SettingsRead,
            ]),

            [StaffRole.Cashier] = Set(
            [
                P.PatientsRead,
                P.SalesRead, P.SalesDraft, P.SalesPost,
                P.PaymentsRegister,
                P.CashierSessionOpen, P.CashierSessionClose,
                P.ChequesRead, P.ChequesManage,
                P.InventoryRead,
                P.ReportsRead,
                P.SettingsRead,
            ]),

            [StaffRole.Accountant] = Set(
            [
                P.FinanceRead, P.FinancePost, P.FinancePeriodClose,
                P.SalesRead,
                P.PaymentsRegister, P.PaymentsReverse,
                P.ChequesRead, P.ChequesManage,
                P.PurchasingRead, P.PurchasingManage,
                P.InventoryRead,
                P.ReportsRead,
                P.AuditRead,
                P.SettingsRead,
            ]),
        };

    /// <summary>The full matrix, keyed by role.</summary>
    public static IReadOnlyDictionary<StaffRole, IReadOnlySet<string>> All => Matrix;

    /// <summary>
    /// The default permission set of <paramref name="role"/>; an empty set for a role that has
    /// no entry (fail closed — <c>CONVENTIONS.md</c> §7).
    /// </summary>
    public static IReadOnlySet<string> For(StaffRole role) =>
        Matrix.TryGetValue(role, out var codes) ? codes : Set([]);

    private static IReadOnlySet<string> Set(IReadOnlyList<string> codes)
    {
        var set = new HashSet<string>(codes.Count, StringComparer.Ordinal);
        foreach (var code in codes)
        {
            set.Add(code);
        }

        return set;
    }
}
