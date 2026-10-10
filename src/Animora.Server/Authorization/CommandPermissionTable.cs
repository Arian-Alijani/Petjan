using System.Collections.Frozen;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;

namespace Animora.Server.Authorization;

/// <summary>
/// Frozen map <c>wire CommandType → required PermissionCodes.*</c>. It <b>starts empty</b> in S05; the command
/// phases (S07–S10) append one row per command type. A command type with no row is <b>denied</b>
/// (<see cref="CommandAuthorizer"/> fails closed), so an unmapped command can never slip through.
/// </summary>
internal static class CommandPermissionTable
{
    /// <summary>The mapping. Append rows here (keep sorted by command type).</summary>
    public static readonly FrozenDictionary<string, string> Rows =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CommandTypes.BoardingItemAdd] = PermissionCodes.BoardingManage,
            [CommandTypes.BoardingItemRemove] = PermissionCodes.BoardingManage,
            [CommandTypes.BoardingRoomCreate] = PermissionCodes.SettingsWrite,
            [CommandTypes.BoardingRoomUpdate] = PermissionCodes.SettingsWrite,
            [CommandTypes.BoardingStayCheckout] = PermissionCodes.BoardingCheckout,
            [CommandTypes.BoardingStayCreate] = PermissionCodes.BoardingManage,
            [CommandTypes.BoardingStayReschedule] = PermissionCodes.BoardingManage,
            [CommandTypes.BoardingStayTransfer] = PermissionCodes.BoardingManage,
            [CommandTypes.CashierMovementRecord] = PermissionCodes.CashierSessionOpen,
            [CommandTypes.CashierSessionClose] = PermissionCodes.CashierSessionClose,
            [CommandTypes.CashierSessionOpen] = PermissionCodes.CashierSessionOpen,
            [CommandTypes.FinanceChequeCreate] = PermissionCodes.ChequesManage,
            [CommandTypes.FinanceChequeDelete] = PermissionCodes.ChequesManage,
            [CommandTypes.FinanceChequeTransition] = PermissionCodes.ChequesManage,
            [CommandTypes.FinanceChequeUpdate] = PermissionCodes.ChequesManage,
            [CommandTypes.FinanceExpensePost] = PermissionCodes.FinancePost,
            [CommandTypes.FinanceExpenseReverse] = PermissionCodes.FinancePost,
            [CommandTypes.FinanceRecurringAdd] = PermissionCodes.FinancePost,
            [CommandTypes.FinanceRecurringEdit] = PermissionCodes.FinancePost,
            [CommandTypes.FinanceRecurringPay] = PermissionCodes.FinancePost,
            [CommandTypes.FinanceRecurringToggle] = PermissionCodes.FinancePost,
            [CommandTypes.InventoryCategoryCreate] = PermissionCodes.InventoryAdjust,
            [CommandTypes.InventoryCategoryDelete] = PermissionCodes.InventoryAdjust,
            [CommandTypes.InventoryCategoryRename] = PermissionCodes.InventoryAdjust,
            [CommandTypes.InventoryProductCreate] = PermissionCodes.InventoryAdjust,
            [CommandTypes.InventoryProductDelete] = PermissionCodes.InventoryAdjust,
            [CommandTypes.InventoryProductUpdate] = PermissionCodes.InventoryAdjust,
            [CommandTypes.InventoryStockAdjust] = PermissionCodes.InventoryAdjust,
            [CommandTypes.InventoryStockSet] = PermissionCodes.InventoryAdjust,
            [CommandTypes.InventoryVariantAdd] = PermissionCodes.InventoryAdjust,
            [CommandTypes.PatientsOwnerCreate] = PermissionCodes.PatientsOwnerEdit,
            [CommandTypes.PatientsOwnerDelete] = PermissionCodes.PatientsOwnerEdit,
            [CommandTypes.PatientsOwnerUpdate] = PermissionCodes.PatientsOwnerEdit,
            [CommandTypes.PatientsPetCreate] = PermissionCodes.PatientsPetEdit,
            [CommandTypes.PatientsPetDelete] = PermissionCodes.PatientsPetEdit,
            [CommandTypes.PatientsPetUpdate] = PermissionCodes.PatientsPetEdit,
            [CommandTypes.PatientsPetWeightAdd] = PermissionCodes.PatientsPetEdit,
            [CommandTypes.PurchasingOrderCancel] = PermissionCodes.PurchasingManage,
            [CommandTypes.PurchasingOrderCreate] = PermissionCodes.PurchasingManage,
            [CommandTypes.PurchasingReceiptRecord] = PermissionCodes.InventoryReceive,
            [CommandTypes.PurchasingSupplierCreate] = PermissionCodes.PurchasingManage,
            [CommandTypes.ReceptionTicketAdmit] = PermissionCodes.ReceptionManage,
            [CommandTypes.ReceptionTicketEnter] = PermissionCodes.ReceptionManage,
            [CommandTypes.ReceptionTicketLeave] = PermissionCodes.ReceptionManage,
            [CommandTypes.ReceptionTicketNoShow] = PermissionCodes.ReceptionManage,
            [CommandTypes.ReceptionTicketUrgent] = PermissionCodes.ReceptionManage,
            [CommandTypes.ReceptionWalkInCreate] = PermissionCodes.ReceptionManage,
            [CommandTypes.SalesInsurerCreate] = PermissionCodes.SettingsWrite,
            [CommandTypes.SalesInsurerDelete] = PermissionCodes.SettingsWrite,
            [CommandTypes.SalesInsurerUpdate] = PermissionCodes.SettingsWrite,
            [CommandTypes.SalesInvoiceCreate] = PermissionCodes.SalesDraft,
            [CommandTypes.SalesInvoiceDelete] = PermissionCodes.SalesDraft,
            [CommandTypes.SalesInvoiceDiscountSet] = PermissionCodes.SalesDraft,
            [CommandTypes.SalesInvoiceInsuranceSet] = PermissionCodes.SalesDraft,
            [CommandTypes.SalesInvoiceLineAdd] = PermissionCodes.SalesDraft,
            [CommandTypes.SalesInvoiceLineEdit] = PermissionCodes.SalesDraft,
            [CommandTypes.SalesInvoiceLineRemove] = PermissionCodes.SalesDraft,
            [CommandTypes.SalesInvoiceLineReturn] = PermissionCodes.SalesVoid,
            [CommandTypes.SalesInvoiceOwnerSet] = PermissionCodes.SalesDraft,
            [CommandTypes.SalesInvoicePost] = PermissionCodes.SalesPost,
            [CommandTypes.SalesInvoiceRefund] = PermissionCodes.SalesVoid,
            [CommandTypes.SalesInvoiceVoid] = PermissionCodes.SalesVoid,
            [CommandTypes.SalesPaymentRegister] = PermissionCodes.SalesPost,
            [CommandTypes.SalesServiceRateCreate] = PermissionCodes.SettingsWrite,
            [CommandTypes.SalesServiceRateUpdate] = PermissionCodes.SettingsWrite,
            [CommandTypes.SchedulingAppointmentBook] = PermissionCodes.AppointmentsBook,
            [CommandTypes.SchedulingAppointmentCancel] = PermissionCodes.AppointmentsCancel,
            [CommandTypes.SchedulingAppointmentCheckin] = PermissionCodes.AppointmentsBook,
            [CommandTypes.SchedulingAppointmentMove] = PermissionCodes.AppointmentsMove,
            [CommandTypes.SchedulingBookingPatientCreate] = PermissionCodes.AppointmentsBook,
            [CommandTypes.VaccinationCatalogAdd] = PermissionCodes.VaccinationWrite,
            [CommandTypes.VaccinationRecordAdminister] = PermissionCodes.VaccinationWrite,
            [CommandTypes.VaccinationRecordReminder] = PermissionCodes.VaccinationWrite,
            [CommandTypes.VaccinationTreatmentCancel] = PermissionCodes.VaccinationWrite,
            [CommandTypes.VaccinationTreatmentCreate] = PermissionCodes.VaccinationWrite,
            [CommandTypes.VaccinationTreatmentOccurrenceDone] = PermissionCodes.VaccinationWrite,
            [CommandTypes.VaccinationTreatmentUpdate] = PermissionCodes.VaccinationWrite,
            [CommandTypes.VisitsRxAdd] = PermissionCodes.VisitsWrite,
            [CommandTypes.VisitsRxRemove] = PermissionCodes.VisitsWrite,
            [CommandTypes.VisitsVisitComplete] = PermissionCodes.VisitsComplete,
            [CommandTypes.VisitsVisitNote] = PermissionCodes.VisitsWrite,
            [CommandTypes.VisitsVisitStart] = PermissionCodes.VisitsWrite,
            [CommandTypes.VisitsVisitVitals] = PermissionCodes.VisitsWrite,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The permission required for <paramref name="commandType"/>, or <see langword="null"/> when unmapped.</summary>
    public static string? RequiredFor(string commandType) =>
        commandType is not null && Rows.TryGetValue(commandType, out string? code) ? code : null;
}
