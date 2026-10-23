namespace Animora.Application.Activity;

/// <summary>Stable, language-independent keys stored in the materialized feed.</summary>
public static class ActivityEventKeys
{
    public const string PetRegistered = "history.pet.registered";
    public const string PetDeleted = "history.pet.deleted";
    public const string OwnerDeleted = "history.owner.deleted";
    public const string VisitCompleted = "history.visit.completed";
    public const string VaccinationAdministered = "history.vaccine.administered";
    public const string AppointmentBooked = "history.appointment.booked";
    public const string AppointmentCancelled = "history.appointment.cancelled";
    public const string StayActivated = "history.stay.activated";
    public const string StayCheckedOut = "history.stay.checkedout";
    public const string GoodsReceiptPosted = "history.goods.received";
    public const string StockMovementRecorded = "history.stock.adjusted";
    public const string InvoiceVoided = "history.invoice.voided";
    public const string InvoicePosted = "history.invoice.posted";
    public const string PaymentRegistered = "history.payment.registered";
    public const string InvoiceRefunded = "history.invoice.refunded";
    public const string ChequeStateChanged = "history.cheque.statuschanged";
    public const string ExpensePosted = "history.expense.recorded";
}
