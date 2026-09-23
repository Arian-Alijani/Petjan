namespace Animora.Domain.Identity;

/// <summary>
/// The central catalog of permission code strings, grouped by bounded context. Later phases
/// only <i>add</i> members; no phase ever renames or removes one — role definitions, stored
/// grants and capability snapshots all key off these exact strings.
/// </summary>
/// <remarks>
/// Consumers must reference the constants, never re-type the literal, so that a rename is a
/// compile error rather than a silent authorization hole.
/// </remarks>
public static class PermissionCodes
{
    #region Patients & reception

    /// <summary>View patient and owner records.</summary>
    public const string PatientsRead = "patients.read";

    /// <summary>Create or edit patient and owner records.</summary>
    public const string PatientsWrite = "patients.write";

    /// <summary>Operate the reception queue (check-in, triage order).</summary>
    public const string ReceptionManage = "reception.manage";

    #endregion

    #region Clinical

    /// <summary>View visit encounters.</summary>
    public const string VisitsRead = "visits.read";

    /// <summary>Edit an open visit encounter.</summary>
    public const string VisitsWrite = "visits.write";

    /// <summary>Close a visit encounter.</summary>
    public const string VisitsComplete = "visits.complete";

    /// <summary>View vaccination records and due dates.</summary>
    public const string VaccinationRead = "vaccination.read";

    /// <summary>Record vaccinations.</summary>
    public const string VaccinationWrite = "vaccination.write";

    #endregion

    #region Appointments & boarding

    /// <summary>View the appointment calendar.</summary>
    public const string AppointmentsRead = "appointments.read";

    /// <summary>Book a new appointment.</summary>
    public const string AppointmentsBook = "appointments.book";

    /// <summary>Reschedule an existing appointment.</summary>
    public const string AppointmentsMove = "appointments.move";

    /// <summary>Cancel an appointment.</summary>
    public const string AppointmentsCancel = "appointments.cancel";

    /// <summary>View boarding stays and cage occupancy.</summary>
    public const string BoardingRead = "boarding.read";

    /// <summary>Create and edit boarding stays.</summary>
    public const string BoardingManage = "boarding.manage";

    /// <summary>Check a boarding stay out and settle it.</summary>
    public const string BoardingCheckout = "boarding.checkout";

    #endregion

    #region Inventory & purchasing

    /// <summary>View stock levels and movements.</summary>
    public const string InventoryRead = "inventory.read";

    /// <summary>Post a manual stock adjustment.</summary>
    public const string InventoryAdjust = "inventory.adjust";

    /// <summary>Receive stock against a purchase.</summary>
    public const string InventoryReceive = "inventory.receive";

    /// <summary>View purchase orders and supplier records.</summary>
    public const string PurchasingRead = "purchasing.read";

    /// <summary>Create and edit purchase orders and suppliers.</summary>
    public const string PurchasingManage = "purchasing.manage";

    #endregion

    #region Sales & payments

    /// <summary>View invoices and sales history.</summary>
    public const string SalesRead = "sales.read";

    /// <summary>Create or edit a draft invoice.</summary>
    public const string SalesDraft = "sales.draft";

    /// <summary>Post an invoice (makes it immutable and financially effective).</summary>
    public const string SalesPost = "sales.post";

    /// <summary>Void a posted invoice.</summary>
    public const string SalesVoid = "sales.void";

    /// <summary>Issue a refund against a posted invoice.</summary>
    public const string SalesRefund = "sales.refund";

    /// <summary>Approve a discount above the automatic threshold.</summary>
    public const string SalesDiscountApprove = "sales.discount.approve";

    /// <summary>Register a payment against an invoice.</summary>
    public const string PaymentsRegister = "payments.register";

    /// <summary>Reverse a registered payment.</summary>
    public const string PaymentsReverse = "payments.reverse";

    /// <summary>Open a cashier session.</summary>
    public const string CashierSessionOpen = "cashier.session.open";

    /// <summary>Close and reconcile a cashier session.</summary>
    public const string CashierSessionClose = "cashier.session.close";

    /// <summary>View cheques.</summary>
    public const string ChequesRead = "cheques.read";

    /// <summary>Register and progress cheques through their lifecycle.</summary>
    public const string ChequesManage = "cheques.manage";

    #endregion

    #region Finance & reporting

    /// <summary>View ledger accounts and journal entries.</summary>
    public const string FinanceRead = "finance.read";

    /// <summary>Post a journal entry.</summary>
    public const string FinancePost = "finance.post";

    /// <summary>Close an accounting period.</summary>
    public const string FinancePeriodClose = "finance.period.close";

    /// <summary>Run reports.</summary>
    public const string ReportsRead = "reports.read";

    #endregion

    #region Administration

    /// <summary>View clinic settings.</summary>
    public const string SettingsRead = "settings.read";

    /// <summary>Change clinic settings.</summary>
    public const string SettingsWrite = "settings.write";

    /// <summary>Create, edit and disable users.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>Change role definitions and permission grants.</summary>
    public const string RolesManage = "roles.manage";

    /// <summary>Approve and revoke devices.</summary>
    public const string DevicesManage = "devices.manage";

    /// <summary>Read the append-only audit trail.</summary>
    public const string AuditRead = "audit.read";

    #endregion

    private static readonly string[] AllCodes =
    [
        PatientsRead, PatientsWrite,
        ReceptionManage,
        VisitsRead, VisitsWrite, VisitsComplete,
        VaccinationRead, VaccinationWrite,
        AppointmentsRead, AppointmentsBook, AppointmentsMove, AppointmentsCancel,
        BoardingRead, BoardingManage, BoardingCheckout,
        InventoryRead, InventoryAdjust, InventoryReceive,
        PurchasingRead, PurchasingManage,
        SalesRead, SalesDraft, SalesPost, SalesVoid, SalesRefund, SalesDiscountApprove,
        PaymentsRegister, PaymentsReverse,
        CashierSessionOpen, CashierSessionClose,
        ChequesRead, ChequesManage,
        FinanceRead, FinancePost, FinancePeriodClose,
        ReportsRead,
        SettingsRead, SettingsWrite,
        UsersManage, RolesManage, DevicesManage,
        AuditRead,
    ];

    private static readonly HashSet<string> CriticalCodes =
    [
        SalesVoid, SalesRefund, PaymentsReverse, FinancePeriodClose,
        UsersManage, RolesManage, DevicesManage,
    ];

    private static readonly HashSet<string> HighCodes =
    [
        SalesPost, PaymentsRegister, CashierSessionClose,
        InventoryAdjust, FinancePost, SalesDiscountApprove,
    ];

    private static readonly HashSet<string> AllSet = new(AllCodes, StringComparer.Ordinal);

    /// <summary>Every declared permission code, in catalog order.</summary>
    public static IReadOnlyList<string> All => AllCodes;

    /// <summary><see langword="true"/> when <paramref name="code"/> is a declared permission code.</summary>
    public static bool Exists(string code) => AllSet.Contains(code);

    /// <summary>
    /// Risk classification of a code: the two explicit escalation sets first, then every
    /// read-only code as <see cref="RiskLevel.Low"/>, and everything else
    /// <see cref="RiskLevel.Normal"/>.
    /// </summary>
    public static RiskLevel RiskOf(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (CriticalCodes.Contains(code))
        {
            return RiskLevel.Critical;
        }

        if (HighCodes.Contains(code))
        {
            return RiskLevel.High;
        }

        return code.EndsWith(".read", StringComparison.Ordinal) ? RiskLevel.Low : RiskLevel.Normal;
    }

    /// <summary>The whole catalog materialized as <see cref="Permission"/> entities.</summary>
    public static IReadOnlyList<Permission> CreateCatalog()
    {
        var catalog = new List<Permission>(AllCodes.Length);
        foreach (var code in AllCodes)
        {
            catalog.Add(Permission.Create(code, RiskOf(code)));
        }

        return catalog;
    }
}
