namespace Animora.Desktop.Services;

/// <summary>
/// Desktop print seam. Screens build a self-contained, print-ready HTML document
/// (own <c>@page</c> size, embedded fonts) and hand it here; the service owns the
/// platform work — writing the file and opening it in the OS default browser,
/// whose native print dialog (printer / «Save as PDF», A4/A3) does the rest.
/// Keeping this out of ViewModels mirrors <see cref="IDialogService.SaveTextFileAsync"/>.
/// </summary>
public interface IPrintService
{
    /// <summary>
    /// Writes <paramref name="html"/> to the app's print folder and opens it for
    /// preview/print. Returns false when the file could not be written or opened.
    /// </summary>
    Task<bool> OpenHtmlAsync(string fileStem, string html);

    /// <summary>
    /// The <c>@font-face</c> CSS block embedding the app's Vazirmatn weights
    /// (base64), so printed documents render identically on any machine. Cached.
    /// </summary>
    string EmbeddedFontCss { get; }
}
