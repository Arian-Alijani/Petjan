namespace Animora.Desktop.Services;

/// <summary>
/// Opens the Patients records' centered "sheet" overlays (Owner File / Patient
/// File). The reference renders both as <c>.sheet-root</c> dialogs over the
/// directory — not as separate pages — so the Page ViewModel asks this seam to
/// present a record instead of navigating away. Keeping the resolve-and-show
/// step behind a service means the ViewModels never construct a View and the
/// shell stays the only owner of overlay plumbing (CONVENTIONS.md §3).
///
/// Both operations are asynchronous because the sheet ViewModel loads its record
/// through an <c>async</c> data seam before the overlay is shown; the caller
/// awaits this so it never blocks the UI thread (PERFORMANCE.md §4).
/// </summary>
public interface IRecordSheetPresenter
{
    /// <summary>Opens the Owner File sheet (reference <c>.owner-sheet</c>, 680×760) for the given owner id.</summary>
    Task OpenOwnerSheetAsync(string ownerId);

    /// <summary>Opens the Patient File sheet (reference <c>.pet-sheet</c>, 1120×880) for the given pet id.</summary>
    Task OpenPetSheetAsync(string petId);

    /// <summary>Opens the Visit Detail sheet (980×780) for the given visit id.</summary>
    Task OpenVisitSheetAsync(string visitId);
}
