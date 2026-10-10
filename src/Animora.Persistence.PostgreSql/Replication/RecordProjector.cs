using System.Runtime.CompilerServices;
using System.Text.Json;
using Animora.Contracts.Wire.Sync;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
namespace Animora.Persistence.PostgreSql.Replication;

/// <summary>Tenant-qualified SQL row images, projected through the closed wire allowlist, including deleted roots.</summary>
public sealed class RecordProjector
{
    public async IAsyncEnumerable<SyncRecord> ReadAsync(AnimoraServerDbContext db, FeedPosition position,
        long from, bool full, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var d in SyncContractCatalog.ProjectionOrder)
        {
            var extras = new List<string>();
            foreach (var child in d.Children)
            {
                var name = child.Table switch { "ProductVariants" => "Variants", "VisitRxLines" => "Prescriptions",
                    "BoardingStayItems" => "Items", _ => "BillingHandoffs" };
                var predicate = $"c.{Q(child.RootIdColumn)} = r.\"Id\"" +
                    (child.HasTenantId ? " AND c.\"TenantId\" = @tenant" : "");
                extras.Add($"'{name}', (SELECT coalesce(jsonb_agg(to_jsonb(c) - 'TenantId' ORDER BY to_jsonb(c)::text), '[]'::jsonb) FROM public.{Q(child.Table)} c WHERE {predicate})");
            }
            var image = "to_jsonb(r)" + (extras.Count == 0 ? "" : " || jsonb_build_object(" + string.Join(",", extras) + ")");
            var sql = full
                ? $"SELECT r.\"Id\", {image} FROM public.{Q(d.Table)} r WHERE r.\"TenantId\" = @tenant ORDER BY r.\"Id\""
                : $"""
                    WITH keys AS (SELECT DISTINCT ON ("EntityId") "EntityId", "Op" FROM public."TenantJournal"
                      WHERE "TenantId"=@tenant AND "Entity"=@entity AND "Epoch"=@epoch
                        AND "ChangeSequence">@from AND "ChangeSequence"<=@watermark
                      ORDER BY "EntityId", "ChangeSequence" DESC)
                    SELECT k."EntityId", CASE WHEN r."Id" IS NULL THEN NULL ELSE {image} END
                    FROM keys k LEFT JOIN public.{Q(d.Table)} r ON r."Id"=k."EntityId" AND r."TenantId"=@tenant
                    ORDER BY CASE WHEN r."Id" IS NULL THEN 1 ELSE 0 END, k."EntityId"
                    """;
            await using var cmd = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection(),
                (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
            cmd.Parameters.AddWithValue("tenant", db.CurrentTenantId);
            cmd.Parameters.AddWithValue("entity", d.Name); cmd.Parameters.AddWithValue("epoch", position.Epoch);
            cmd.Parameters.AddWithValue("from", from); cmd.Parameters.AddWithValue("watermark", position.LastSequence);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetString(0);
                if (reader.IsDBNull(1)) { yield return new(d.Name, id, 'D', null, null, true, null); continue; }
                using var document = JsonDocument.Parse(reader.GetString(1));
                var row = document.RootElement;
                var version = EntityVersionCodec.ToWire(Guid.Parse(row.GetProperty("Version").GetString()!));
                yield return new(d.Name, id, 'U', version, row.GetProperty("UpdatedAt").GetDateTimeOffset(),
                    row.GetProperty("Deleted").GetBoolean(), Project(d.Name, row));
            }
        }
    }

    public static JsonElement Project(string entity, JsonElement row)
    {
        var contract = typeof(SyncRecord).Assembly.GetType("Animora.Contracts.Wire.Sync.Contracts." + entity + "SyncContract")
            ?? throw new InvalidOperationException("Uncatalogued sync contract.");
        var allowed = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in contract.GetProperties())
        {
            var name = property.Name;
            if (row.TryGetProperty(name, out var value)) allowed[name] = Clean(value);
            else if (row.TryGetProperty(name + "Minor", out var minor))
                allowed[name] = new { minor = minor.GetInt64(), currency = row.GetProperty(name + "Currency").GetString() };
            else if (row.TryGetProperty(name + "Scaled", out var scaled))
                allowed[name] = new { value = scaled.GetInt64() / 1000m, unitCode = row.GetProperty(name + "Unit").GetString() };
            else if (name == "Vitals")
                allowed[name] = new { weightKg = row.TryGetProperty("Vitals_WeightKg", out var w) ? Clean(w) : null,
                    temperatureC = row.TryGetProperty("Vitals_TemperatureC", out var t) ? Clean(t) : null };
        }
        return JsonSerializer.SerializeToElement(allowed, SyncSerializerV2.Options);
    }

    private static object? Clean(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Where(x => x.Name != "TenantId" && x.Name != "tenantId")
            .OrderBy(x => x.Name, StringComparer.Ordinal).ToDictionary(x => JsonNamingPolicy.CamelCase.ConvertName(x.Name), x => Clean(x.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Clean).ToArray(),
        JsonValueKind.Null => null,
        _ => value.Clone(),
    };
    private static string Q(string name) => JournalSqlTemplates.Quote(name);
}
