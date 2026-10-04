using System.Globalization;

namespace Animora.Application.Numbering;

/// <summary>
/// The single owner of the persisted document-number shape <c>PREFIX-YY-NNNNNN</c> (two-digit Jalali
/// year, six-digit year-scoped sequence, ASCII digits). Every allocator (invoice, expense, journal)
/// formats through here so the pattern can never drift; the UI applies Persian digits for display only.
/// Numbers issued before this scheme existed are never rewritten, so consumers must tolerate any length.
/// </summary>
public static class DocumentNumber
{
    public const string InvoicePrefix = "INV";
    public const string ExpensePrefix = "EXP";
    public const string JournalPrefix = "JV";

    /// <summary>Digits of the sequence segment.</summary>
    public const int SequenceWidth = 6;

    /// <summary>Formats <c>PREFIX-YY-NNNNNN</c> from a four-digit Jalali year and the allocated sequence.</summary>
    public static string Format(string prefix, string jalaliYear, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (jalaliYear is null || jalaliYear.Length != 4)
            throw new InvalidOperationException($"Invalid Jalali year '{jalaliYear}'.");
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{jalaliYear[^2..]}-{sequence:D6}");
    }

    /// <summary>Extracts the four-digit Jalali year from a period code (<c>yyyy</c> or <c>yyyy-MM</c>).</summary>
    public static string JalaliYearOf(string periodCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(periodCode);
        var separator = periodCode.IndexOf('-');
        var year = separator < 0 ? periodCode : periodCode[..separator];
        if (year.Length != 4)
            throw new InvalidOperationException($"Invalid Jalali period code '{periodCode}'.");
        return year;
    }
}
