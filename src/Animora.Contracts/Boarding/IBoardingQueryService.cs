namespace Animora.Contracts.Boarding;

/// <summary>
/// Data seam for the Boarding screen (عرض اقامت‌ها). The desktop ViewModel
/// depends only on this interface; the in-memory
/// <c>MockBoardingQueryService</c> is the single swappable implementation a
/// future backend replaces (R-18) — no ViewModel or View change is required to
/// do so. The stay read model and every mutation the board, detail modal,
/// transfer/checkout/new-stay modals and the History/Settings tabs perform live
/// behind this one seam.
/// </summary>
public interface IBoardingQueryService
{
    /// <summary>
    /// Every boarding stay the clinic has on record, mirroring the reference's
    /// <c>STAYS</c> collection that the status tabs, counts and stay cards
    /// derive from (Animora-UI/assets/boarding.js).
    /// </summary>
    Task<IReadOnlyList<BoardingStay>> GetStaysAsync();

    /// <summary>
    /// One stay plus its consumed lines, mirroring what the reference's
    /// <c>_openDetailFor</c> reads (the stay header fields plus <c>st.items</c>).
    /// Returns <c>null</c> when no stay has the given id.
    /// </summary>
    Task<StayDetail?> GetDetailAsync(string stayId);

    /// <summary>
    /// Records a consumed line (service/good) against an active stay, mirroring
    /// the reference's add-row handler (which re-opens the detail modal after
    /// the mutation).
    /// </summary>
    Task AddItemAsync(string stayId, string itemRef, int quantity);

    /// <summary>
    /// Removes a consumed line by its index, mirroring the reference's
    /// <c>[data-rm]</c> handler.
    /// </summary>
    Task RemoveItemAsync(string stayId, int index);

    /// <summary>
    /// Marks a stay's invoice as transferred to the cashier, mirroring the
    /// reference's transfer confirm (<c>st.transferred = true</c>).
    /// </summary>
    Task TransferAsync(string stayId);

    /// <summary>
    /// Ends a stay («تخلیه»), mirroring the reference's checkout confirm
    /// (<c>st.status = 'done'</c>).
    /// </summary>
    Task CheckoutAsync(string stayId);

    /// <summary>
    /// Creates a new stay («ثبت پذیرش») and returns it, mirroring the
    /// reference's new-stay submit (which unshifts the stay onto <c>STAYS</c>).
    /// </summary>
    Task<BoardingStay> CreateStayAsync(NewStayRequest request);

    /// <summary>
    /// The room roster with derived occupancy, mirroring the reference's
    /// <c>renderSettingsRooms</c> (which reads <c>ROOMS</c> plus the active
    /// stays' rooms).
    /// </summary>
    Task<IReadOnlyList<RoomInfo>> GetRoomsAsync();

    /// <summary>
    /// The boarding extra-service catalog, mirroring the reference's
    /// <c>renderSettingsSvcs</c> (which reads <c>EXTRA_SVCS</c>).
    /// </summary>
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync();

    /// <summary>
    /// The selectable entries the detail modal's add row offers, mirroring the
    /// reference's three <c>&lt;optgroup&gt;</c> catalogs
    /// (<c>EXTRA_SVCS</c>/<c>SERVICES</c>/<c>ITEMS</c>).
    /// </summary>
    Task<IReadOnlyList<BoardingSelectableItem>> GetSelectableItemsAsync();

    /// <summary>
    /// A read-only projection of the clinic's patient records that the new-stay
    /// modal's «از پرونده‌های موجود» autocomplete offers, mirroring the
    /// reference's <c>knownPets</c> suggestion list.
    /// </summary>
    Task<IReadOnlyList<BoardingKnownPet>> GetKnownPetsAsync();

    /// <summary>
    /// One ordered page of the boarding history ledger (check-in / item added /
    /// item removed / transferred / check-out) plus the totals computed over the
    /// <b>whole</b> filtered set. The service filters/sorts/pages on the caller's
    /// behalf so no unbounded result set ever crosses the seam (PERFORMANCE.md §6).
    /// </summary>
    Task<BoardingHistoryPage> GetHistoryAsync(BoardingHistoryFilter filter);
}

/// <summary>
/// A patient-record suggestion for the new-stay modal's existing-file picker,
/// mirroring the reference's <c>knownPets</c> entries.
/// </summary>
/// <param name="PetName">Pet name.</param>
/// <param name="Species">Species.</param>
/// <param name="Breed">Breed line.</param>
/// <param name="OwnerName">Owner name.</param>
/// <param name="OwnerPhone">Owner mobile.</param>
/// <param name="PatientId">Linked patient record id.</param>
public sealed record BoardingKnownPet(
    string PetName,
    BoardingSpecies Species,
    string Breed,
    string OwnerName,
    string OwnerPhone,
    string PatientId);
