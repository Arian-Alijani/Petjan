using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using Animora.Domain.Identity;

namespace Animora.Desktop.Shell;

/// <summary>
/// The ordinal, explicit mapping from the <b>actual</b> sidebar keys (<c>NavigationMenu.Items</c>) to the
/// catalog permission code the shell gates entry on (B04c). The static <c>NavigationMenu</c> is deliberately
/// not modified: the shared navigation definition stays presentation-only and gating lives here, in the Desktop.
/// </summary>
/// <remarks>
/// Substitutions recorded against the phase text (which invented 15 keys including reception/purchasing/
/// cashier entries that do not exist): <c>dashboard</c>→<c>patients.read</c> (the dashboard is the receptionist's
/// landing surface, so a Receptionist keeps it); <c>history</c>→<c>audit.read</c>; <c>profile</c>→<c>settings.read</c>.
/// There is no <c>*.view</c> permission in the catalog, so every entry maps to a real read constant. Because the
/// default Receptionist role lacks <c>reports.read</c>, <c>finance.read</c> and <c>cheques.read</c>, that role
/// never sees <c>reports</c>, <c>expense</c> or <c>cheques</c>; a Manager (the full catalog) sees every entry.
/// </remarks>
public static class NavPermissions
{
    /// <summary>The real nav keys → the permission code that reveals them (ordinal keys).</summary>
    public static FrozenDictionary<string, string> ByKey { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["dashboard"] = PermissionCodes.PatientsRead,
        ["appointments"] = PermissionCodes.AppointmentsRead,
        ["patients"] = PermissionCodes.PatientsRead,
        ["visits"] = PermissionCodes.VisitsRead,
        ["vaccine"] = PermissionCodes.VaccinationRead,
        ["boarding"] = PermissionCodes.BoardingRead,
        ["sale"] = PermissionCodes.SalesRead,
        ["inventory"] = PermissionCodes.InventoryRead,
        ["cheques"] = PermissionCodes.ChequesRead,
        ["statistics"] = PermissionCodes.ReportsRead,
        ["history"] = PermissionCodes.AuditRead,
        ["profile"] = PermissionCodes.SettingsRead,
        ["purchasing"] = PermissionCodes.PurchasingRead,
        ["reception"] = PermissionCodes.ReceptionManage,
        ["cashier"] = PermissionCodes.SalesRead,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Page admission is shared by every navigation entry point, including targets
    // without a sidebar entry.
    private static readonly FrozenDictionary<Type, string> ByPage = new Dictionary<Type, string>
    {
        [typeof(Pages.Dashboard.DashboardPageViewModel)] = "dashboard",
        [typeof(Pages.Appointments.AppointmentsCalendarViewModel)] = "appointments",
        [typeof(Pages.Patients.PatientsPageViewModel)] = "patients",
        [typeof(Pages.Visits.VisitsTriagePageViewModel)] = "visits",
        [typeof(Pages.Visits.VisitEncounterViewModel)] = "visits",
        [typeof(Pages.Vaccination.VaccinationPageViewModel)] = "vaccine",
        [typeof(Pages.Boarding.BoardingPageViewModel)] = "boarding",
        [typeof(Pages.Sales.SalePageViewModel)] = "sale",
        [typeof(Pages.Inventory.InventoryPageViewModel)] = "inventory",
        [typeof(Pages.Cheques.ChequesPageViewModel)] = "cheques",
        [typeof(Pages.Statistics.StatisticsPageViewModel)] = "statistics",
        [typeof(Pages.History.HistoryPageViewModel)] = "history",
        [typeof(Pages.Profile.ProfilePageViewModel)] = "profile",
        [typeof(Pages.Purchasing.PurchasingPageViewModel)] = "purchasing",
        [typeof(Pages.Reception.ReceptionPageViewModel)] = "reception",
        [typeof(Pages.Cashier.CashierPageViewModel)] = "cashier",
    }.ToFrozenDictionary();

    public static string? KeyFor(Type page) => ByPage.GetValueOrDefault(page);

    public static string? For(Type page) => KeyFor(page) is { } key ? For(key) : null;

    /// <summary>The required permission code for a nav key, or <see langword="null"/> for an unmapped key (fail closed).</summary>
    public static string? For(string key) =>
        !string.IsNullOrEmpty(key) && ByKey.TryGetValue(key, out var code) ? code : null;

    /// <summary>
    /// Every permission code any nav entry gates on. Used by the topbar to decide read-only mode from "no
    /// permitted write capability", not from one unrelated denial.
    /// </summary>
    public static IReadOnlyCollection<string> Codes { get; } =
        ByKey.Values.Distinct(StringComparer.Ordinal).ToArray();
}
