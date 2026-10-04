using System.Globalization;
using System.Text;
using Animora.Contracts.History;

namespace Animora.Client.Services.History;

/// <summary>
/// RFC 4180 export with UTF-8 BOM for spreadsheet compatibility. Rows arrive already mapped to the
/// on-screen <see cref="HistoryEvent"/> shape (clinic-local Jalali date/time), and text columns are
/// localized through <see cref="HistoryCsvLabels"/>, so the file equals the visible ledger.
/// </summary>
public static class CsvWriter
{
    public const int MaxRows = 50_000;

    public static async Task<string> WriteAsync(
        IReadOnlyList<HistoryEvent> rows, HistoryCsvLabels labels, CancellationToken ct = default)
    {
        if (rows.Count > MaxRows) throw new InvalidOperationException("EXPORT_TOO_LARGE");
        await using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true))
        {
            await writer.WriteLineAsync(string.Join(",", labels.Header.Select(Escape)));
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                string[] values =
                [
                    row.Date, row.Time, labels.Title(row.Title), labels.Detail(row.Detail), row.Entity,
                    row.Amount.ToString(CultureInfo.InvariantCulture),
                    row.AmountSign.ToString(CultureInfo.InvariantCulture),
                    labels.Status(row.Status), row.Actor.Name,
                ];
                await writer.WriteLineAsync(string.Join(",", values.Select(Escape)));
            }
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0
        ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
