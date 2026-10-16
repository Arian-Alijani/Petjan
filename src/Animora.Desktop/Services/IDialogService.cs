namespace Animora.Desktop.Services;

/// <summary>
/// Generic overlay host contract (dim backdrop + centered modal panel /
/// right-anchored sliding drawer panel / centered record sheet), matching
/// Animora-UI's modal/drawer/sheet looks. Screen phases supply a ViewModel or
/// control; this service only manages showing/closing the host chrome.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows <paramref name="content"/> in a centered modal dialog. The panel is
    /// capped at <paramref name="maxWidth"/> — 520 for the default
    /// <c>.modal</c>, 760 for the wide <c>.modal.wide</c> wizard/add-pet dialogs.
    /// </summary>
    void ShowModal(object content, double maxWidth = 520);

    /// <summary>
    /// Shows <paramref name="content"/> in a right-anchored sliding drawer.
    /// <paramref name="width"/> defaults to the appointments booking panel's
    /// 420px; screens with a wider panel (e.g. the cheques details drawer =
    /// 480px) pass their transcribed width.
    /// </summary>
    void ShowDrawer(object content, double width = 420);

    /// <summary>
    /// Shows <paramref name="content"/> in a large, centered record "sheet"
    /// (Animora-UI's <c>.sheet-root</c>): used by the Owner File (680×760) and
    /// Patient File (1120×880). Sizes are transcribed from the reference's
    /// <c>.owner-sheet</c> / <c>.pet-sheet</c> rules.
    /// </summary>
    void ShowSheet(object content, double width = 680, double height = 760);

    /// <summary>
    /// Runs the OS save-file flow for a text file (the reference's browser
    /// download in <c>HIST.exportCSV</c>, adapted to the desktop's standard
    /// <c>IStorageProvider.SaveFilePickerAsync</c>). Returns true when the user
    /// saved the file, false if they cancelled or no window is attached — the
    /// platform call stays here, not inside a screen View or the data service.
    /// </summary>
    Task<bool> SaveTextFileAsync(string suggestedName, string content, string extension = "csv");

    /// <summary>
    /// Runs the OS open-file flow (<c>IStorageProvider.OpenFilePickerAsync</c>) for the given extension patterns
    /// (e.g. <c>*.png</c>). Returns the chosen files — empty when the user cancelled or no window is attached.
    /// The platform call stays here, not inside a screen View or ViewModel.
    /// </summary>
    Task<IReadOnlyList<PickedFile>> PickFilesAsync(string title, IReadOnlyList<string> patterns, bool allowMultiple = true);

    /// <summary>Closes every overlay (modal, drawer and sheet) at once.</summary>
    void Close();

    /// <summary>
    /// Closes only the modal layer, leaving any drawer/sheet underneath untouched. Used by a modal that is
    /// opened on top of a sheet (e.g. the patient wizard launched from the booking panel) so the sheet's
    /// in-progress state survives.
    /// </summary>
    void CloseModal();
}

/// <summary>A file chosen through <see cref="IDialogService.PickFilesAsync"/>; its bytes are opened lazily.</summary>
/// <param name="Name">File name with extension (no directory).</param>
/// <param name="Size">Size in bytes, or <see langword="null"/> when the platform did not report one.</param>
/// <param name="OpenReadAsync">Opens a fresh read stream; the caller disposes it.</param>
public sealed record PickedFile(string Name, long? Size, Func<Task<Stream>> OpenReadAsync);
