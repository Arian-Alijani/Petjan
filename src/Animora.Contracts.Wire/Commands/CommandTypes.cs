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

    // Cashier.
    public const string CashierSessionOpen = "cashier.session.open";
    public const string CashierSessionClose = "cashier.session.close";

    // Inventory.
    public const string InventoryStockAdjust = "inventory.stock.adjust";
    public const string InventoryReceiptPost = "inventory.receipt.post";

    // Purchasing.
    public const string PurchasingOrderSubmit = "purchasing.order.submit";

    // Finance.
    public const string FinancePeriodClose = "finance.period.close";
    public const string FinanceExpensePost = "finance.expense.post";
    public const string FinanceChequeTransition = "finance.cheque.transition";

    // Scheduling & boarding.
    public const string SchedulingAppointmentBook = "scheduling.appointment.book";
    public const string SchedulingAppointmentMove = "scheduling.appointment.move";
    public const string BoardingStayCheckout = "boarding.stay.checkout";

    /// <summary>Every declared command type, in declaration order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        SalesInvoicePost, SalesInvoiceVoid, SalesInvoiceRefund, SalesPaymentRegister, SalesAdjustmentAdd,
        CashierSessionOpen, CashierSessionClose,
        InventoryStockAdjust, InventoryReceiptPost,
        PurchasingOrderSubmit,
        FinancePeriodClose, FinanceExpensePost, FinanceChequeTransition,
        SchedulingAppointmentBook, SchedulingAppointmentMove,
        BoardingStayCheckout,
    ];
}
