using System.Threading.Tasks;

namespace Animora.Desktop.Services;

/// <summary>
/// The single seam that turns one history-feed row into the <b>same</b> unified record screen the
/// rest of the app opens for that entity. Every history surface — the main History page and the
/// Vaccination / Boarding / Sales (Cashier) in-page History tabs — routes a row click through this
/// one service, so an invoice row opens the exact invoice sheet the صندوق page opens, a pet row opens
/// the Pet file sheet, and so on. Keeping the resolve-and-show step behind a single service means the
/// ViewModels never construct a View and there is only one place that knows each destination
/// (CONVENTIONS.md §3; no duplicated navigation logic across the four history surfaces).
/// </summary>
public interface IHistoryRecordOpener
{
    /// <summary>
    /// Opens the unified screen for the given feed entity. <paramref name="entityType"/> is the feed's
    /// source-aggregate tag (case-sensitive, ordinal): <c>Invoice</c>, <c>Payment</c>, <c>Pet</c>,
    /// <c>Owner</c>, <c>Stay</c>, <c>Vaccination</c>, <c>Visit</c>, <c>Appointment</c>, <c>Cheque</c>,
    /// <c>Expense</c>, <c>GoodsReceipt</c>, <c>StockMovement</c> or <c>Product</c>. Unknown/empty tags
    /// are a safe no-op (the row stays inert, never crashes).
    /// </summary>
    /// <param name="entityType">The feed's <c>EntityType</c> tag.</param>
    /// <param name="entityId">The feed's <c>EntityId</c> (e.g. the invoice/pet/stay id).</param>
    /// <param name="invoiceId">
    /// For <c>Payment</c> rows the invoice sheet needs the owning invoice id (the payment id in
    /// <paramref name="entityId"/> is not the invoice). Pass it here when known; falls back to
    /// <paramref name="entityId"/> otherwise.
    /// </param>
    Task OpenAsync(string entityType, string entityId, string? invoiceId = null);
}
