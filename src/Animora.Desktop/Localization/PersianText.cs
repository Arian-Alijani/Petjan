using System.Text;

namespace Animora.Desktop.Localization;

/// <summary>
/// Normalizes user-entered Persian text for the directory's search, mirroring
/// the reference's <c>norm()</c> helper (Animora-UI/assets/patients-page.js):
/// Persian digits → ASCII, Arabic yeh/kaf → Persian, plus lower-casing and
/// trimming. Written with .NET string idioms, not a ported JS regex.
/// </summary>
public static class PersianText
{
    private const char PersianZero = '\u06F0';
    private const char PersianNine = '\u06F9';
    private const char ArabicYeh = '\u064A';   // ي
    private const char PersianYeh = '\u06CC';  // ی
    private const char ArabicKaf = '\u0643';   // ك
    private const char PersianKaf = '\u06A9';  // ک

    /// <summary>
    /// Returns the comparison form of <paramref name="value"/>, or an empty
    /// string for null/whitespace input.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Fold(value.Trim());
    }

    /// <summary>
    /// The same character folding as <see cref="Normalize"/> but <b>without trimming</b>, so the result has
    /// exactly the length of <paramref name="value"/> and every index maps 1:1 back to the original text.
    /// Use it when a match found in the folded text must be sliced out of the original (search highlight).
    /// </summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case >= PersianZero and <= PersianNine:
                    buffer.Append((char)('0' + (ch - PersianZero)));
                    break;
                case ArabicYeh:
                    buffer.Append(PersianYeh);
                    break;
                case ArabicKaf:
                    buffer.Append(PersianKaf);
                    break;
                default:
                    buffer.Append(char.ToLowerInvariant(ch));
                    break;
            }
        }

        return buffer.ToString();
    }

    /// <summary>Whether the normalized <paramref name="haystack"/> contains the normalized needle.</summary>
    public static bool Contains(string haystack, string normalizedNeedle) =>
        haystack.Contains(normalizedNeedle, System.StringComparison.Ordinal);
}
