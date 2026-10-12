using System.Text;
using System.Text.Json;
using Animora.Domain.Boarding;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Purchasing;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Activity;

/// <summary>
/// Resolves the ids carried by a feed row's language-neutral args into display names <b>once, at write time</b>
/// (live dispatch and rebuild share this), so the History screen never shows raw ULIDs and never re-parses JSON
/// per rendered row. Output is one <c>Key=Value</c> line per fact (ASCII keys; Persian labels belong to the
/// Desktop <c>HistoryText</c>) plus a compact search string.
/// </summary>
internal static class ActivityDetailResolver
{
    private const int MaxValueLength = 80;
    private const int MaxTotalLength = 400; // ShortText column bound.

    /// <summary>Plain (non-id) args surfaced as-is: arg name → line key.</summary>
    private static readonly (string Arg, string Key)[] PlainArgs =
    [
        ("VaccineCode", "Vaccine"), ("Reason", "Reason"), ("category", "Category"), ("from", "From"),
        ("to", "To"), ("LineCount", "Count"), ("QuantityValue", "Quantity"), ("Method", "Method"),
    ];

    /// <summary>
    /// Display name of the user that performed an action (audit "who"), looked up in the local user table; empty when the
    /// user is unknown on this device. Results (including misses) are memoised in <paramref name="cache"/> when supplied,
    /// so a rebuild over a long outbox issues one query per distinct actor.
    /// </summary>
    public static async Task<string> ResolveActorNameAsync(
        AnimoraLocalDbContext db, string? actorUserId, IDictionary<string, string>? cache, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actorUserId)) return string.Empty;
        if (cache is not null && cache.TryGetValue(actorUserId, out var known)) return known;
        var name = await db.Set<User>().AsNoTracking().Where(u => u.Id == actorUserId)
            .Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? string.Empty;
        if (cache is not null) cache[actorUserId] = name;
        return name;
    }

    public static async Task<(string Detail, string Search)> ResolveAsync(
        AnimoraLocalDbContext db, string argsJson, CancellationToken ct)
    {
        var args = Parse(argsJson);
        if (args.Count == 0) return (string.Empty, string.Empty);

        var lines = new List<(string Key, string Value)>(6);
        args.TryGetValue("OwnerId", out var ownerId);

        if (args.TryGetValue("PetId", out var petId))
        {
            var pet = await db.Set<Pet>().AsNoTracking().Where(p => p.Id == petId)
                .Select(p => new { p.Name, p.OwnerId }).FirstOrDefaultAsync(ct);
            if (pet is not null)
            {
                lines.Add(("Pet", pet.Name));
                ownerId ??= pet.OwnerId;
            }
        }

        if (ownerId is not null)
        {
            var owner = await db.Set<Owner>().AsNoTracking().Where(o => o.Id == ownerId)
                .Select(o => o.FullName).FirstOrDefaultAsync(ct);
            if (owner is not null) lines.Add(("Owner", owner));
        }

        if (args.TryGetValue("ProductId", out var productId))
        {
            var product = await db.Set<Product>().AsNoTracking().Where(p => p.Id == productId)
                .Select(p => p.Name).FirstOrDefaultAsync(ct);
            if (product is not null) lines.Add(("Product", product));
        }

        if (args.TryGetValue("SupplierId", out var supplierId))
        {
            var supplier = await db.Set<Supplier>().AsNoTracking().Where(x => x.Id == supplierId)
                .Select(x => x.Name).FirstOrDefaultAsync(ct);
            if (supplier is not null) lines.Add(("Supplier", supplier));
        }

        if (args.TryGetValue("RoomId", out var roomId))
        {
            var room = await db.Set<Room>().AsNoTracking().Where(x => x.Id == roomId)
                .Select(x => x.DisplayName).FirstOrDefaultAsync(ct);
            if (room is not null) lines.Add(("Room", room));
        }

        if (args.TryGetValue("InvoiceId", out var invoiceId))
        {
            var number = await db.Set<Invoice>().AsNoTracking().Where(x => x.Id == invoiceId)
                .Select(x => x.InvoiceNumber).FirstOrDefaultAsync(ct);
            if (number is not null) lines.Add(("Invoice", number));
        }

        foreach (var (arg, key) in PlainArgs)
        {
            if (args.TryGetValue(arg, out var value)) lines.Add((key, value));
        }

        return Build(lines);
    }

    private static (string Detail, string Search) Build(List<(string Key, string Value)> lines)
    {
        var detail = new StringBuilder();
        var search = new StringBuilder();
        foreach (var (key, raw) in lines)
        {
            var value = Clean(raw);
            if (value.Length == 0) continue;
            if (detail.Length > 0) { detail.Append('\n'); search.Append(' '); }
            detail.Append(key).Append('=').Append(value);
            search.Append(value);
        }

        return (Truncate(detail), Truncate(search));
    }

    private static string Truncate(StringBuilder text) =>
        text.Length <= MaxTotalLength ? text.ToString() : text.ToString(0, MaxTotalLength);

    private static string Clean(string value)
    {
        var text = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return text.Length <= MaxValueLength ? text : text[..MaxValueLength];
    }

    private static Dictionary<string, string> Parse(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var text = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                if (!string.IsNullOrEmpty(text)) result[property.Name] = text;
            }
        }
        catch (JsonException)
        {
            // Malformed args never block a feed write: the row simply carries no resolved detail.
        }

        return result;
    }
}
