using System.Text;
using Animora.Application.UseCases.History;

namespace Animora.Client.Services.History;

/// <summary>RFC 4180 export with UTF-8 BOM for spreadsheet compatibility.</summary>
public static class CsvWriter
{
    public static async Task<string> WriteAsync(IReadOnlyList<ActivityRow> rows, CancellationToken ct = default)
    {
        if (rows.Count > 50_000) throw new InvalidOperationException("EXPORT_TOO_LARGE");
        await using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true))
        {
            await writer.WriteLineAsync("Date,Time,Title,Detail,Entity,Amount,Sign,Status,Actor");
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                var values = new[] { row.OccurredUtc.ToString("yyyy/MM/dd"), row.OccurredUtc.ToString("HH:mm"),
                    row.TitleKey, row.DetailArgsJson, row.EntityType + ":" + row.EntityId,
                    row.AmountMinor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    row.AmountSign.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Status, row.ActorName };
                await writer.WriteLineAsync(string.Join(",", values.Select(Escape)));
            }
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0
        ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
