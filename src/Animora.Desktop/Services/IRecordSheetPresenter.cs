namespace Animora.Desktop.Services;

/// <summary>
/// Opens the Patients records' centered "sheet" overlays (Owner File / Patient
/// File). The reference renders both as <c>.sheet-root</c> dialogs over the
/// directory — not as separate pages — so the Page ViewModel asks this seam to
/// present a record instead of navigating away. Keeping the resolve-and-show
/// step behind a service means the ViewModels never construct a View and the
/// shell stays the only owner of overlay plumbing (CONVENTIONS.md §3).
/// </summary>
public interface IRecordSheetPresenter
{
    /// <summary>Opens the Owner File sheet (reference <c>.owner-sheet</c>, 680×760) for the given owner id.</summary>
    void OpenOwnerSheet(string ownerId);

    /// <summary>Opens the Patient File sheet (reference <c>.pet-sheet</c>, 1120×880) for the given pet id.</summary>
    void OpenPetSheet(string petId);
}
