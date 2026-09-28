namespace Animora.Contracts.Wire.Commands;

/// <summary>
/// The catalog of every business command type name that crosses the wire. Format
/// <c>&lt;context&gt;.&lt;aggregate&gt;.&lt;verb&gt;</c>, lowercase, dot-separated. Later phases <b>add</b>
/// entries; no phase ever renames one — a stored command's type is permanent (CONVENTIONS §3).
/// </summary>
public static class CommandTypes
{
    // Sales.
    public const string SalesInvoicePost = "sales.invoice.post";
    public const string SalesInvoiceVoid = "sales.invoice.void";
    public const string SalesInvoiceRefund = "sales.invoice.refund";
    public const string SalesPaymentRegister = "sales.payment.register";
    public const string SalesAdjustmentAdd = "sales.adjustment.add";
    public const string SalesInvoiceCreate = "sales.invoice.create";
    public const string SalesInvoiceLineAdd = "sales.invoice.line.add";
    public const string SalesInvoiceLineEdit = "sales.invoice.line.edit";
    public const string SalesInvoiceLineRemove = "sales.invoice.line.remove";
    public const string SalesInvoiceDiscountSet = "sales.invoice.discount.set";
    public const string SalesInvoiceInsuranceSet = "sales.invoice.insurance.set";
    public const string SalesInvoiceOwnerSet = "sales.invoice.owner.set";
    public const string SalesInvoiceDelete = "sales.invoice.delete";
    public const string SalesInvoiceLineReturn = "sales.invoice.line.return";

    // Cashier.
    public const string CashierSessionOpen = "cashier.session.open";
    public const string CashierSessionClose = "cashier.session.close";
    public const string CashierMovementRecord = "cashier.movement.record";

    // Inventory.
    public const string InventoryStockAdjust = "inventory.stock.adjust";
    public const string InventoryReceiptPost = "inventory.receipt.post";

    // Purchasing.
    public const string PurchasingOrderSubmit = "purchasing.order.submit";

    // Inventory & purchasing (C06).
    public const string InventoryProductCreate = "inventory.product.create";
    public const string InventoryProductUpdate = "inventory.product.update";
    public const string InventoryVariantAdd = "inventory.variant.add";
    public const string InventoryProductDelete = "inventory.product.delete";
    public const string PurchasingOrderCreate = "purchasing.order.create";
    public const string PurchasingReceiptRecord = "purchasing.receipt.record";
    public const string PurchasingOrderCancel = "purchasing.order.cancel";

    // Finance.
    public const string FinancePeriodClose = "finance.period.close";
    public const string FinanceExpensePost = "finance.expense.post";
    public const string FinanceChequeTransition = "finance.cheque.transition";

    // Scheduling & boarding.
    public const string SchedulingAppointmentBook = "scheduling.appointment.book";
    public const string SchedulingAppointmentMove = "scheduling.appointment.move";
    public const string SchedulingAppointmentCancel = "scheduling.appointment.cancel";
    public const string SchedulingAppointmentCheckin = "scheduling.appointment.checkin";
    public const string SchedulingBookingPatientCreate = "scheduling.bookingpatient.create";
    public const string BoardingStayCheckout = "boarding.stay.checkout";
    public const string BoardingStayCreate = "boarding.stay.create";
    public const string BoardingItemAdd = "boarding.item.add";
    public const string BoardingItemRemove = "boarding.item.remove";
    public const string BoardingStayTransfer = "boarding.stay.transfer";

    // Patients & owners (C01).
    public const string PatientsOwnerCreate = "patients.owner.create";
    public const string PatientsOwnerUpdate = "patients.owner.update";
    public const string PatientsPetCreate = "patients.pet.create";
    public const string PatientsPetUpdate = "patients.pet.update";
    public const string PatientsPetWeightAdd = "patients.pet.weight.add";

    // Reception & visits triage (C02).
    public const string ReceptionTicketAdmit = "reception.ticket.admit";
    public const string ReceptionTicketEnter = "reception.ticket.enter";
    public const string ReceptionTicketLeave = "reception.ticket.leave";
    public const string ReceptionTicketNoShow = "reception.ticket.noshow";
    public const string ReceptionTicketUrgent = "reception.ticket.urgent";
    public const string ReceptionWalkInCreate = "reception.walkin.create";
    public const string VisitsVisitStart = "visits.visit.start";

    // Visit encounter & vaccination (C03).
    public const string VisitsVisitVitals = "visits.visit.vitals";
    public const string VisitsVisitNote = "visits.visit.note";
    public const string VisitsRxAdd = "visits.rx.add";
    public const string VisitsRxRemove = "visits.rx.remove";
    public const string VisitsVisitComplete = "visits.visit.complete";
    public const string VaccinationRecordAdminister = "vaccination.record.administer";
    public const string VaccinationRecordReminder = "vaccination.record.reminder";

    /// <summary>Every declared command type, in declaration order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        SalesInvoicePost, SalesInvoiceVoid, SalesInvoiceRefund, SalesPaymentRegister, SalesAdjustmentAdd,
        SalesInvoiceCreate, SalesInvoiceLineAdd, SalesInvoiceLineEdit, SalesInvoiceLineRemove,
        SalesInvoiceDiscountSet, SalesInvoiceInsuranceSet, SalesInvoiceOwnerSet, SalesInvoiceDelete,
        SalesInvoiceLineReturn,
        CashierSessionOpen, CashierSessionClose, CashierMovementRecord,
        InventoryStockAdjust, InventoryReceiptPost,
        PurchasingOrderSubmit,
        InventoryProductCreate, InventoryProductUpdate, InventoryVariantAdd, InventoryProductDelete,
        PurchasingOrderCreate, PurchasingReceiptRecord, PurchasingOrderCancel,
        FinancePeriodClose, FinanceExpensePost, FinanceChequeTransition,
        SchedulingAppointmentBook, SchedulingAppointmentMove, SchedulingAppointmentCancel,
        SchedulingAppointmentCheckin, SchedulingBookingPatientCreate,
        BoardingStayCheckout, BoardingStayCreate, BoardingItemAdd, BoardingItemRemove, BoardingStayTransfer,
        PatientsOwnerCreate, PatientsOwnerUpdate, PatientsPetCreate, PatientsPetUpdate, PatientsPetWeightAdd,
        ReceptionTicketAdmit, ReceptionTicketEnter, ReceptionTicketLeave, ReceptionTicketNoShow,
        ReceptionTicketUrgent, ReceptionWalkInCreate, VisitsVisitStart,
        VisitsVisitVitals, VisitsVisitNote, VisitsRxAdd, VisitsRxRemove, VisitsVisitComplete,
        VaccinationRecordAdminister, VaccinationRecordReminder,
    ];
}
