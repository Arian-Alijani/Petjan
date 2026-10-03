namespace Animora.Contracts.Boarding;

/// <summary>
/// The kind of a consumed line on a stay, mirroring the reference's
/// <c>items[].kind</c> (<c>svc | item</c>) that the detail modal renders as the
/// «خدمت» / «کالا» tag on each consumed row (Animora-UI/assets/boarding.js).
/// </summary>
public enum BoardingItemKind
{
    /// <summary>A service — reference <c>kind = 'svc'</c> («خدمت»).</summary>
    Service,

    /// <summary>A physical item — reference <c>kind = 'item'</c> («کالا»).</summary>
    Item,
}

/// <summary>
/// The catalog a selectable add-row entry comes from, mirroring the reference's
/// <c>&lt;optgroup&gt;</c> grouping in the detail modal's add row
/// (خدمات پانسیون / خدمات کلینیک / کالاها). The <c>boarding</c> group's entries
/// are stored on the stay as services (<c>kind = 'svc'</c>), exactly as the
/// reference maps a <c>bx:</c> reference.
/// </summary>
public enum BoardingItemSource
{
    /// <summary>Boarding-specific extra services — reference <c>EXTRA_SVCS</c> (optgroup «خدمات پانسیون»).</summary>
    BoardingService,

    /// <summary>Clinic services — reference <c>CLINIC.SERVICES</c> (optgroup «خدمات کلینیک»).</summary>
    ClinicService,

    /// <summary>Clinic goods — reference <c>CLINIC.ITEMS</c> (optgroup «کالاها»).</summary>
    ClinicItem,
}

/// <summary>
/// One consumed line on a stay, mirroring the fields the reference's detail
/// modal reads from <c>items[]</c> (<c>kind</c>/<c>title</c>/<c>qty</c>/
/// <c>price</c>/<c>at</c>) plus the catalog <c>ref</c> it was created from.
/// </summary>
/// <param name="Ref">Catalog reference id (e.g. <c>bx-1</c>, <c>it-3</c>).</param>
/// <param name="Kind">Service/item tag («خدمت» / «کالا»).</param>
/// <param name="Title">Display title (catalog title at time of adding).</param>
/// <param name="Quantity">Units consumed.</param>
/// <param name="Price">Unit price in Rial.</param>
/// <param name="At">The day the line was recorded (used for the day-timeline dot).</param>
public sealed record BoardingStayItem(
    string Ref,
    BoardingItemKind Kind,
    string Title,
    int Quantity,
    long Price,
    DateOnly At);

/// <summary>
/// A stay plus its consumed lines — the shape the reference's
/// <c>_openDetailFor</c> renders (the stay fields it reads plus
/// <c>st.items</c>).
/// </summary>
/// <param name="Stay">The stay header fields (pet/owner/room/dates/rate/status/…).</param>
/// <param name="Items">The consumed services/goods, in insertion order.</param>
public sealed record StayDetail(
    BoardingStay Stay,
    IReadOnlyList<BoardingStayItem> Items);

/// <summary>
/// The payload the new-stay modal submits, mirroring the fields
/// <c>openNewStayModal</c>'s «ثبت پذیرش» handler collects (pet/owner identity,
/// room, dates, nightly rate, payment mode, care note).
/// </summary>
/// <param name="PetName">Pet name («نام حیوان»).</param>
/// <param name="Species">Species («گونه»).</param>
/// <param name="Breed">Breed («نژاد»), «نامشخص» when left blank.</param>
/// <param name="OwnerName">Owner name («نام مالک»).</param>
/// <param name="OwnerPhone">Owner mobile, «—» when left blank.</param>
/// <param name="PatientId">Linked patient id (empty for a fresh/manual entry).</param>
/// <param name="Room">Room id (e.g. <c>A-1</c>).</param>
/// <param name="CheckIn">Check-in date.</param>
/// <param name="CheckOut">Check-out date.</param>
/// <param name="DailyRate">Nightly rate in Rial.</param>
/// <param name="PaymentMode">Prepaid/postpaid.</param>
/// <param name="Note">Care note («یادداشت نگهداری»).</param>
public sealed record NewStayRequest(
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
    string Note);

/// <summary>
/// A room's kind, mirroring the reference's <c>ROOMS[].type</c>
/// (<c>standard | vip</c>) that renders the «معمولی» / «VIP» chip.
/// </summary>
public enum BoardingRoomType
{
    Standard,
    Vip,
}

/// <summary>
/// One boarding room, mirroring the fields the reference's
/// <c>renderSettingsRooms</c> reads from <c>ROOMS</c> plus the derived
/// occupancy (a room is occupied when an active stay references it).
/// </summary>
/// <param name="Id">Room id (e.g. <c>A-1</c>, <c>VIP</c>).</param>
/// <param name="Label">Room label («A-1» / «سوئیت VIP»).</param>
/// <param name="Type">Standard/VIP (chip text).</param>
/// <param name="Species">Species icon hint.</param>
/// <param name="Capacity">Nominal capacity.</param>
/// <param name="IsOccupied">Whether an active stay currently uses the room.</param>
public sealed record RoomInfo(
    string Id,
    string Label,
    BoardingRoomType Type,
    BoardingSpecies Species,
    int Capacity,
    bool IsOccupied);

/// <summary>
/// One boarding extra service, mirroring the fields the reference's
/// <c>renderSettingsSvcs</c> reads from <c>EXTRA_SVCS</c>.
/// </summary>
/// <param name="Id">Service id (e.g. <c>bx-1</c>).</param>
/// <param name="Title">Service title.</param>
/// <param name="Price">Price in Rial.</param>
public sealed record ServiceInfo(
    string Id,
    string Title,
    long Price);

/// <summary>
/// One selectable entry in the detail modal's add row, mirroring the reference's
/// three <c>&lt;optgroup&gt;</c> catalogs (<c>EXTRA_SVCS</c>/<c>SERVICES</c>/
/// <c>ITEMS</c>) that the modal offers for adding a consumed line.
/// </summary>
/// <param name="Ref">Catalog reference id (e.g. <c>bx-1</c>).</param>
/// <param name="Source">Which catalog group it belongs to.</param>
/// <param name="Title">Display title.</param>
/// <param name="Price">Unit price in Rial.</param>
public sealed record BoardingSelectableItem(
    string Ref,
    BoardingItemSource Source,
    string Title,
    long Price);
