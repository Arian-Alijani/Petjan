namespace Animora.Contracts.Boarding;

/// <summary>
/// Data seam for the Boarding screen (عرض اقامت‌ها). The desktop ViewModel
/// depends only on this interface; the in-memory
/// <c>MockBoardingQueryService</c> is the single swappable implementation a
/// future backend replaces (R-18) — no ViewModel or View change is required to
/// do so.
/// </summary>
public interface IBoardingQueryService
{
    /// <summary>
    /// Every boarding stay the clinic has on record, mirroring the reference's
    /// <c>STAYS</c> collection that the status tabs, counts and stay cards
    /// derive from (Animora-UI/assets/boarding.js).
    /// </summary>
    Task<IReadOnlyList<BoardingStay>> GetStaysAsync();
}
