namespace Animora.Contracts.Appointments;

/// <summary>Status bucket of the archive filter. History only ever holds terminal appointments.</summary>
public enum AppointmentHistoryStatusFilter
{
    All,
    Completed,
    NoShow,
    Cancelled,
}

/// <summary>Invoice bucket of the archive filter (only invoices really linked to the appointment's visit count).</summary>
public enum AppointmentInvoiceFilter
{
    All,
    None,
    Unpaid,
    Partial,
    Paid,
}

/// <summary>
/// One archive request (A01): every filter is resolved by the backend, never over a preloaded window.
/// <see cref="From"/> / <see cref="To"/> are clinic-local days (null = unbounded; the end is clamped to today).
/// </summary>
public sealed record AppointmentHistoryFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    AppointmentHistoryStatusFilter Status = AppointmentHistoryStatusFilter.All,
    string? DoctorId = null,
    string? TypeId = null,
    AppointmentInvoiceFilter Invoice = AppointmentInvoiceFilter.All,
    string? Search = null,
    int Page = 0,
    int PageSize = 50);

/// <summary>Aggregates over the whole filtered scope (not only the returned page); amounts are in toman.</summary>
public sealed record AppointmentHistoryTotals(
    int Total, int Completed, int Cancelled, int NoShow, long PaidAmount, long OwedAmount)
{
    public static AppointmentHistoryTotals Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>One page of archive rows (newest first) plus the scope-wide totals.</summary>
public sealed record AppointmentHistoryPage(
    IReadOnlyList<AppointmentEvent> Items, AppointmentHistoryTotals Totals, bool HasMore);
