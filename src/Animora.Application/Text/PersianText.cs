namespace Animora.Application.Text;

/// <summary>
/// Digit/letter normalization for building and matching search keys (C01). Folds Persian (U+06F0–U+06F9)
/// and Arabic-Indic (U+0660–U+0669) digits to ASCII, and Arabic-form letters to their Persian equivalents,
/// so text typed on any keyboard compares equal. Contains NO user-facing localization strings.
/// </summary>
public static class PersianText
{
    /// <summary>Folds Persian and Arabic-Indic digits to ASCII 0-9; leaves every other char unchanged.</summary>
    public static string NormalizeDigits(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var buffer = new char[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            buffer[i] = c switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)(c - '\u06F0' + '0'),
                >= '\u0660' and <= '\u0669' => (char)(c - '\u0660' + '0'),
                _ => c,
            };
        }
        return new string(buffer);
    }

    /// <summary>Normalizes Arabic-form letters to their Persian equivalents (ي→ی, ك→ک) and drops ZWNJ.</summary>
    public static string NormalizeArabicLetters(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var buffer = new char[raw.Length];
        var len = 0;
        foreach (var c in raw)
        {
            var mapped = c switch
            {
                '\u064A' => '\u06CC', // Arabic yeh -> Persian yeh
                '\u0643' => '\u06A9', // Arabic kaf -> Persian keheh
                '\u200C' => '\0',     // ZWNJ removed
                _ => c,
            };
            if (mapped != '\0') buffer[len++] = mapped;
        }
        return new string(buffer, 0, len);
    }

    /// <summary>Full normalization used for both stored keys and query terms: letters, digits, lowercased, trimmed.</summary>
    public static string Normalize(string? raw) =>
        NormalizeDigits(NormalizeArabicLetters(raw ?? string.Empty)).ToLowerInvariant().Trim();
}
