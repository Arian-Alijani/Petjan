using System.Collections.Generic;

namespace Animora.Contracts.History;

/// <summary>
/// The module/kind tag of a unified-ledger event, mirroring the reference's
/// <c>CATEGORIES</c> registry keys (Animora-UI/assets/history.js). Each value
/// maps 1:1 to a Phase 02 semantic tone + icon in the View — this contract
/// stays free of UI resources.
/// </summary>
public enum HistoryEventKind
{
    /// <summary>«فروش / درآمد» — sales & daily revenue (ok tone).</summary>
    Sale,

    /// <summary>«نوبت» — appointments (info tone).</summary>
    Appointment,

    /// <summary>«ثبت بیمار» — patient registration (lilac tone).</summary>
    Patient,

    /// <summary>«واکسن» — vaccination (ok tone).</summary>
    Vaccine,

    /// <summary>«هزینه» — expenses (danger tone).</summary>
    Expense,

    /// <summary>«انبار / کالا» — inventory events (warn tone).</summary>
    Inventory,

    /// <summary>«چک» — cheques (muted tone).</summary>
    Cheque,

    /// <summary>«پانسیون» — boarding stays (lilac tone).</summary>
    Boarding,

    /// <summary>«ویزیت» — clinical visits (info tone).</summary>
    Visit,

    /// <summary>«خرید / تأمین» — purchases / restock (warn tone).</summary>
    Purchase,
}

/// <summary>
/// The lifecycle/state chip of an event row, mirroring the reference's
/// <c>STATUS</c> map (Animora-UI/assets/history.js). Each value carries the
/// reference's exact Persian label and Phase 02 tone in the View.
/// </summary>
public enum HistoryEventStatus
{
    /// <summary>«پرداخت شد» (ok).</summary>
    Paid,

    /// <summary>«نیمه‌پرداخت» (warn).</summary>
    Partial,

    /// <summary>«پرداخت نشده» (danger).</summary>
    Unpaid,

    /// <summary>«انجام شد» (ok).</summary>
    Done,

    /// <summary>«لغو شده» (danger).</summary>
    Cancelled,

    /// <summary>«تأیید شده» (ok).</summary>
    Confirmed,

    /// <summary>«در انتظار» (warn).</summary>
    Pending,

    /// <summary>«جدید» (info).</summary>
    New,

    /// <summary>«معوق» (danger).</summary>
    Overdue,

    /// <summary>«تسویه شد» (ok).</summary>
    Cleared,

    /// <summary>«برگشت خورد» (danger).</summary>
    Bounced,

    /// <summary>«موجودی کم» (warn).</summary>
    LowStock,

    /// <summary>«عادی» (mute).</summary>
    Normal,

    /// <summary>«ترخیص» (ok).</summary>
    Discharge,

    /// <summary>«ورود» (info).</summary>
    CheckIn,
}

/// <summary>
/// A single row of the unified chronological ledger, mirroring the row shape
/// the reference's renderer builds from its per-source event builders
/// (Animora-UI/assets/history.js <c>build*Events</c>): a timestamp, a kind
/// tag, a title + detail line, an entity (owner / doctor / party), a signed
/// amount and a status. The View binds only to this contract (R-18).
/// </summary>
/// <param name="Id">Stable identifier (used for de-duplication, mirroring the reference's <c>id</c>).</param>
/// <param name="Kind">The module/kind tag.</param>
/// <param name="Date">Event date as an ASCII Jalali <c>yyyy/MM/dd</c> string (rendered through Phase 04).</param>
/// <param name="Time">Optional <c>HH:mm</c> time, or empty when the event has no clock part.</param>
/// <param name="Title">The primary description line (Persian).</param>
/// <param name="Detail">The secondary description line, or empty.</param>
/// <param name="Entity">The entity / doctor / party column value, or empty.</param>
/// <param name="Amount">The absolute amount (toman); 0 when the event carries no money.</param>
/// <param name="AmountSign">+1 = income, -1 = expense, 0 = no amount (the reference's <c>amtSign</c>).</param>
/// <param name="Status">The status chip value.</param>
public sealed record HistoryEvent(
    string Id,
    HistoryEventKind Kind,
    string Date,
    string Time,
    string Title,
    string Detail,
    string Entity,
    long Amount,
    int AmountSign,
    HistoryEventStatus Status);

/// <summary>
/// The filter applied to the ledger, mirroring the reference's toolbar state
/// (<c>HS</c> in Animora-UI/assets/history.js): a period / date range, a
/// module-kind multi-select and a free-text search.
/// </summary>
/// <param name="Period">Period id: <c>today</c> | <c>7</c> | <c>30</c> | <c>all</c> (the reference's chip keys).</param>
/// <param name="Kinds">Selected module kinds; empty = all (the reference's <c>cats</c> set).</param>
/// <param name="Search">Free-text query, matched against title/detail/entity.</param>
public sealed record HistoryFilter(
    string Period,
    IReadOnlyCollection<HistoryEventKind> Kinds,
    string Search);
