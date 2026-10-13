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
                var childImage = ChildImage(child.Table);
                extras.Add($"'{name}', (SELECT coalesce(jsonb_agg({childImage} ORDER BY {childImage}::text), '[]'::jsonb) FROM public.{Q(child.Table)} c WHERE {predicate})");
            }
            var image = "to_jsonb(r)" + (extras.Count == 0 ? "" : " || jsonb_build_object(" + string.Join(",", extras) + ")");
            var sql = full
                ? $"SELECT r.\"Id\", {image}, 'U'::text FROM public.{Q(d.Table)} r WHERE r.\"TenantId\" = @tenant ORDER BY r.\"Id\""
                : $"""
                    WITH keys AS (SELECT DISTINCT ON ("EntityId") "EntityId", "Op" FROM public."TenantJournal"
                      WHERE "TenantId"=@tenant AND "Entity"=@entity AND "Epoch"=@epoch
                        AND "ChangeSequence">@from AND "ChangeSequence"<=@watermark
                      ORDER BY "EntityId", "ChangeSequence" DESC)
                    SELECT k."EntityId", CASE WHEN r."Id" IS NULL THEN NULL ELSE {image} END, k."Op"::text
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
                if (reader.IsDBNull(1))
                {
                    // A missing row is a deletion only when the captured journal operation says so.
                    if (reader.GetString(2) != "D")
                        throw new InvalidOperationException($"Missing sync row for {d.Name}/{id} without a delete operation.");
                    yield return new(d.Name, id, 'D', null, null, true, null);
                    continue;
                }
                using var document = JsonDocument.Parse(reader.GetString(1));
                var row = document.RootElement;
                // Fail closed: corrupt metadata/projection must abort this immutable batch, not manufacture
                // a deletion or skip a change while advancing the cursor. BatchFreezer discards the workspace;
                // the client keeps its existing business rows and checkpoint and can retry after repair.
                if (!TryGetVersion(row, out var version)
                    || !row.TryGetProperty("UpdatedAt", out var updatedAt) || updatedAt.ValueKind != JsonValueKind.String
                    || !updatedAt.TryGetDateTimeOffset(out var updated) || updated == DateTimeOffset.MinValue || updated == DateTimeOffset.MaxValue
                    || !row.TryGetProperty("Deleted", out var deleted) || deleted.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidOperationException($"Invalid sync metadata for {d.Name}/{id}.");

                yield return new(d.Name, id, 'U', version, updated, deleted.GetBoolean(), Project(d.Name, row));
            }
        }
    }

    /// <summary>
    /// Reads the row's concurrency <c>Version</c> as a wire string, returning <see langword="false"/> (never throwing)
    /// when the property is absent, null or not a parseable GUID so the caller can reject the batch without
    /// converting a projection fault into a deletion.
    /// </summary>
    private static bool TryGetVersion(JsonElement row, out string? version)
    {
        version = null;
        if (!row.TryGetProperty("Version", out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        if (!Guid.TryParse(element.GetString(), out var guid))
            return false;
        version = EntityVersionCodec.ToWire(guid);
        return true;
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
            {
                // Optional money value object: only emit when BOTH the amount is a real number AND the
                // currency column is a non-null string. A null/absent amount or currency means the VO is
                // unset — omit the field (client treats it as null). Never GetInt64() a JSON null nor
                // GetProperty() a missing currency column (both threw a raw 500 before).
                if (TryInt64(minor, out var m) && TryString(row, name + "Currency", out var currency))
                    allowed[name] = new { minor = m, currency };
            }
            else if (row.TryGetProperty(name + "Scaled", out var scaled))
            {
                if (TryInt64(scaled, out var s) && TryString(row, name + "Unit", out var unitCode))
                    allowed[name] = new { value = s / 1000m, unitCode };
            }
            else if (name == "Vitals")
                allowed[name] = new { weightKg = row.TryGetProperty("Vitals_WeightKg", out var w) ? Clean(w) : null,
                    temperatureC = row.TryGetProperty("Vitals_TemperatureC", out var t) ? Clean(t) : null };
        }
        return JsonSerializer.SerializeToElement(allowed, SyncSerializerV2.Options);
    }

    /// <summary>True only for a real JSON number that fits an <see cref="long"/>; never throws on null/strings.</summary>
    private static bool TryInt64(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    /// <summary>True only when <paramref name="property"/> is present and a non-null JSON string; never throws.</summary>
    private static bool TryString(JsonElement row, string property, out string value)
    {
        value = string.Empty;
        if (!row.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString()!;
        return true;
    }

    private static object? Clean(JsonElement value) => value.ValueKind switch
    {
        // GroupBy+Last (not ToDictionary) so two members that camelCase to the same key never throw an
        // ArgumentException that would fault the whole batch build; the last value wins deterministically.
        JsonValueKind.Object => value.EnumerateObject().Where(x => x.Name != "TenantId" && x.Name != "tenantId")
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .GroupBy(x => JsonNamingPolicy.CamelCase.ConvertName(x.Name))
            .ToDictionary(g => g.Key, g => Clean(g.Last().Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Clean).ToArray(),
        JsonValueKind.Null => null,
        _ => value.Clone(),
    };
    // Closed child-column lists prevent future table columns from silently becoming wire fields.
    private static string ChildImage(string table)
    {
        string[] columns = table switch
        {
            "ProductVariants" => ["VariantId", "Sku", "AttributeName", "AttributeValue", "CostPriceMinor", "CostPriceCurrency",
                "SalePriceMinor", "SalePriceCurrency", "ReorderLevelScaled", "ReorderLevelUnit", "Barcode", "IsActive"],
            "VisitRxLines" => ["LineId", "ProductId", "DrugName", "Dosage", "Frequency", "DurationDays", "Instructions"],
            "BoardingStayItems" => ["ItemId", "Kind", "CatalogItemId", "Title", "QuantityScaled", "QuantityUnit",
                "UnitPriceMinor", "UnitPriceCurrency", "ConsumedUtc", "RecordedByUserId"],
            "VisitHandoffLines" => ["Id", "VisitId", "OwnerId", "PetId", "DoctorId", "CompletedUtc", "LinesJson", "Status", "InvoiceId"],
            "BoardingHandoffLines" => ["Id", "StayId", "OwnerId", "PetId", "DoctorId", "CompletedUtc", "LinesJson", "Status", "Kind", "InvoiceId"],
            _ => throw new InvalidOperationException("Uncatalogued child projection."),
        };
        return "jsonb_build_object(" + string.Join(",", columns.Select(n => $"'{n}',c.{Q(n)}")) + ")";
    }
    private static string Q(string name) => JournalSqlTemplates.Quote(name);
}
