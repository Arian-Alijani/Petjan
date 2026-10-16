using Animora.Desktop.Localization;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// Shared presentation helpers of the radiology / laboratory forms of the encounter: Jalali date parsing and
/// display, and the attachment file-type policy (what may be attached and what may be opened from the app).
/// </summary>
public static class ParaclinicPresentation
{
    /// <summary>Largest single attachment accepted from disk (the bytes go into the local content-addressed store).</summary>
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;

    /// <summary>The file-picker patterns offered when attaching radiology images / reports.</summary>
    public static readonly IReadOnlyList<string> AttachmentPatterns =
        ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.gif", "*.webp", "*.tif", "*.tiff", "*.pdf", "*.dcm"];

    /// <summary>
    /// Parses a picker value (ASCII Jalali <c>yyyy/MM/dd</c>) into the calendar date it denotes; the time part is
    /// midnight local. Returns <see langword="false"/> for an empty or impossible date.
    /// </summary>
    public static bool TryParseJalali(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            date = JalaliCalendar.Parse(PersianNumberFormatter.ToAsciiDigits(value).Trim()).ToDateTime();
            return true;
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>The «۱۷ مهر ۱۴۰۵» display of a recorded instant (Persian digits, local calendar day).</summary>
    public static string DisplayDate(DateTime value) =>
        JalaliDate.FromDateTime(value).ToString("long");

    /// <summary>MIME type recorded with an attachment, derived from its extension.</summary>
    public static string ContentTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".tif" or ".tiff" => "image/tiff",
            ".pdf" => "application/pdf",
            ".dcm" => "application/dicom",
            _ => "application/octet-stream",
        };

    /// <summary>
    /// Whether an attachment may be launched with the operating system's default viewer. Attachments can arrive
    /// from other clients through sync, so only passive image / document types are ever opened — never an
    /// executable or script that happens to carry an attachment record.
    /// </summary>
    public static bool IsViewable(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" or ".tif" or ".tiff" or ".pdf" or ".dcm";

    /// <summary>A file name that is safe to create inside the temp folder (no path segments / invalid characters).</summary>
    public static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "attachment" : name;
    }
}
