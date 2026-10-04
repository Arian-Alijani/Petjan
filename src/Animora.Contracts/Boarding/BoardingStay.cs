namespace Animora.Contracts.Boarding;

/// <summary>
/// A boarding stay's lifecycle status, mirroring the reference's
/// <c>status</c> field (Animora-UI/assets/boarding.js): a stay is
/// <c>active</c> while the pet is lodged, and <c>done</c> once checked out.
/// Every other visible state (اقامت فعلی / عقب‌افتاده / تخلیه امروز / فردا
/// تخلیه) is *derived* from the status plus the check-out date relative to
/// today — exactly as the reference's <c>statusMeta</c>/<c>tabMatches</c>
/// helpers do. This keeps the seam faithful: a future backend supplies the
/// same two fields and the derived states stay identical.
/// </summary>
public enum BoardingStatus
{
    /// <summary>Lodged — the pet is currently staying (<c>status = 'active'</c>).</summary>
    Active,

    /// <summary>Checked out — the stay has ended (<c>status = 'done'</c>).</summary>
    Done,
}

/// <summary>
/// A stay's payment mode, mirroring the reference's <c>paymentMode</c> field
/// (<c>prepaid | postpaid</c>) that <c>payLabel</c>/<c>payTone</c> render as
/// the «پیش‌پرداخت» / «پس‌پرداخت» pill.
/// </summary>
public enum BoardingPaymentMode
{
    /// <summary>Prepaid — <c>payLabel</c> «پیش‌پرداخت», <c>payTone</c> ok.</summary>
    Prepaid,

    /// <summary>Postpaid — <c>payLabel</c> «پس‌پرداخت», <c>payTone</c> info.</summary>
    Postpaid,
}

/// <summary>
/// The species of a boarded pet, mirroring the reference's <c>SPECIES</c>
/// catalog keys (dog/cat/bird/rabbit/rodent/other) that select the per-species
/// icon and label on a stay card.
/// </summary>
public enum BoardingSpecies
{
    Dog,
    Cat,
    Bird,
    Rabbit,
    Rodent,
    Other,
}

/// <summary>
/// One boarding stay, mirroring the fields the reference's <c>renderCards</c>
/// reads from its <c>STAYS</c> collection plus the derived domain values
/// (nights/elapsed/remaining/total cost) the card's timeline and footer show.
/// Dates are carried as real <see cref="DateOnly"/> values (the seam a future
/// backend fills); the View renders them through the Phase 04 Jalali helpers.
/// </summary>
/// <param name="Id">Stable identifier (e.g. <c>bd-1</c>).</param>
/// <param name="PetName">Pet name (e.g. لئو).</param>
/// <param name="Species">Species enum used for the icon key and label.</param>
/// <param name="Breed">Breed line shown beside the species label.</param>
/// <param name="OwnerName">Owner's display name (card footer).</param>
/// <param name="OwnerPhone">Owner mobile (search only).</param>
/// <param name="PatientId">Linked patient record id (may be empty).</param>
/// <param name="Room">Room code / label (e.g. <c>A-2</c> / سوئیت VIP).</param>
/// <param name="CheckIn">Check-in date.</param>
/// <param name="CheckOut">Scheduled check-out date.</param>
/// <param name="DailyRate">Nightly rate in Rial.</param>
/// <param name="PaymentMode">Prepaid/postpaid (footer pill).</param>
/// <param name="Status">Active/done.</param>
/// <param name="Tone">Avatar tone key <c>a</c>..<c>f</c> (reference <c>petTone</c>).</param>
/// <param name="Transferred">True once the stay was checked out and handed to the cashier's ready-to-invoice queue (never true for an active stay).</param>
/// <param name="Note">Free-text care note (detail modal; carried for completeness).</param>
/// <param name="ExtraItemsCost">
/// Total cost of the stay's extra items/services in Rial — pre-aggregated by the
/// service so the card's total cost is derivable without leaking line detail
/// (which is Phase 16's material) into this phase's public shape.
/// </param>
public sealed record BoardingStay(
    string Id,
    string PetName,
    BoardingSpecies Species,
    string Breed,
    string OwnerName,
    string OwnerPhone,
    string PatientId,
    string Room,
    DateOnly CheckIn,
    DateOnly CheckOut,
    long DailyRate,
    BoardingPaymentMode PaymentMode,
    BoardingStatus Status,
    string Tone,
    bool Transferred,
    string Note,
    long ExtraItemsCost);
