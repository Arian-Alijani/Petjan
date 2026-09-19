using System.Globalization;
using System.Text;

namespace Animora.Desktop.Localization;

/// <summary>
/// Pure, allocation-light Persian number formatting helpers. Re-derives the
/// output behavior of the reference's <c>toFa</c> (Animora-UI/assets/layout.js)
/// and <c>group()</c> (Animora-UI/assets/dashboard.js) helpers using .NET
/// string idioms rather than porting the JS regex.
/// </summary>
public static class PersianNumberFormatter
{
    // U+06F0..U+06F9 — Persian (Extended Arabic-Indic) digits, matching the
    // reference's ['۰'..'۹'] map.
    private const char PersianZero = '\u06F0';

    // U+066C — Arabic thousands separator, matching the reference's group() '٬'.
    private const char ThousandsSeparator = '\u066C';

    private const int GroupSize = 3;

    /// <summary>
    /// Converts every ASCII digit (0–9) in the string form of <paramref name="value"/>
    /// to its Persian equivalent (۰۱۲۳۴۵۶۷۸۹). Non-digit characters are preserved.
    /// </summary>
    public static string ToPersianDigits(object? value)
    {
        var text = value as string ?? value?.ToString() ?? string.Empty;
        return ToPersianDigits(text.AsSpan());
    }

    /// <summary>
    /// Converts every ASCII digit (0–9) in <paramref name="text"/> to Persian digits.
    /// </summary>
    public static string ToPersianDigits(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return string.Empty;
        }

        // Fast path: most values already contain no ASCII digit to convert
        // (Persian-formatted strings, labels, symbols). Returning the original
        // text avoids allocating a StringBuilder + a copy for that common case
        // (PERFORMANCE.md §10: no needless allocation in a hot formatting path).
        var index = 0;
        for (; index < text.Length; index++)
        {
            if (text[index] is >= '0' and <= '9')
            {
                break;
            }
        }

        if (index == text.Length)
        {
            return text.ToString();
        }

        var buffer = new StringBuilder(text.Length);
        buffer.Append(text[..index]);
        for (; index < text.Length; index++)
        {
            var ch = text[index];
            buffer.Append(ch is >= '0' and <= '9' ? (char)(PersianZero + (ch - '0')) : ch);
        }

        return buffer.ToString();
    }

    /// <summary>
    /// Converts every Persian (U+06F0..U+06F9) or Arabic-Indic (U+0660..U+0669)
    /// digit in <paramref name="text"/> back to its ASCII equivalent, leaving all
    /// other characters untouched — the inverse of <see cref="ToPersianDigits"/>,
    /// used to parse user-entered Jalali dates / weights from numeric inputs.
    /// </summary>
    public static string ToAsciiDigits(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            buffer.Append(ch switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),
                >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
                _ => ch,
            });
        }

        return buffer.ToString();
    }

    /// <summary>
    /// Formats <paramref name="value"/> with a Persian thousands separator (٬) and
    /// Persian digits — e.g. 1234567 → "۱٬۲۳۴٬۵۶۷". Matches the reference's
    /// <c>fa(group(n))</c> composition.
    /// </summary>
    public static string FormatGrouped(long value)
    {
        var grouped = GroupAscii(value);
        return ToPersianDigits(grouped);
    }

    /// <summary>
    /// Inserts the Arabic thousands separator (٬) into the ASCII decimal form of
    /// <paramref name="value"/> without converting digits — the direct analogue of
    /// the reference's <c>group()</c>.
    /// </summary>
    public static string GroupAscii(long value)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        var negative = digits.StartsWith('-');
        if (negative)
        {
            digits = digits[1..];
        }

        if (digits.Length <= GroupSize)
        {
            return negative ? "-" + digits : digits;
        }

        var firstGroup = digits.Length % GroupSize;
        var capacity = digits.Length + (digits.Length - 1) / GroupSize + (negative ? 1 : 0);
        var buffer = new StringBuilder(capacity);

        if (negative)
        {
            buffer.Append('-');
        }

        var index = 0;
        if (firstGroup > 0)
        {
            buffer.Append(digits, 0, firstGroup);
            index = firstGroup;
        }

        for (; index < digits.Length; index += GroupSize)
        {
            if (index > 0)
            {
                buffer.Append(ThousandsSeparator);
            }

            buffer.Append(digits, index, GroupSize);
        }

        return buffer.ToString();
    }
}
