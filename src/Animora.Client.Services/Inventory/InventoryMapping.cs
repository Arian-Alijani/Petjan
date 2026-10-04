using System.Globalization;
using Animora.Application.UseCases.Inventory.ReadModels;
using Animora.Contracts.Inventory;
using ContractProduct = Animora.Contracts.Inventory.Product;
using ContractVariant = Animora.Contracts.Inventory.ProductVariant;

namespace Animora.Client.Services.Inventory;

/// <summary>
/// Read-model ↔ Contracts-Inventory mapping (C06). <c>Contracts</c> carries display-shaped values
/// (Jalali date/time strings, emoji glyphs, tone keys, pre-formatted Persian figures) that the local
/// store does not persist, so the derivations live here — never in the ViewModel and never in SQL.
/// </summary>
internal static class InventoryMapping
{
    /// <summary>
    /// Maps one catalog row. <see cref="ProductVariant.Stock"/> is the snapshot on-hand, never a stored
    /// absolute (R-B23). The Contracts shape has no expiry field source in the read model, so an
    /// already-recorded movement's expiry is not surfaced here.
    /// </summary>
    internal static ContractProduct ToContract(this ProductReadModel row) =>
        new(row.Id, row.Sku, row.Name, row.Brand, row.CategoryId,
            row.Notes ?? string.Empty, AttributeOf(row.Variants), VariantsOf(row.Variants));

    internal static IReadOnlyList<ContractVariant> VariantsOf(IReadOnlyList<ProductVariantReadModel> variants)
    {
        var rows = new ContractVariant[variants.Count];
        for (var i = 0; i < variants.Count; i++)
        {
            var v = variants[i];
            rows[i] = new ContractVariant(
                v.VariantId, v.AttributeValue, v.Sku, ScaledToInt(v.OnHandScaled),
                ScaledToInt(v.ReorderLevelScaled), v.CostMinor, v.PriceMinor, null);
        }
        return rows;
    }

    /// <summary>
    /// The variant dimension shown by the product modal: null when every variant is the implicit single
    /// one (<c>DEFAULT</c> attribute), otherwise the shared attribute name and its options.
    /// </summary>
    internal static UnitInfo? AttributeOf(IReadOnlyList<ProductVariantReadModel> variants)
    {
        if (variants.Count == 0) return null;
        var name = variants[0].AttributeName;
        if (name.Length == 0 || string.Equals(name, "DEFAULT", StringComparison.Ordinal)) return null;

        var options = new string[variants.Count];
        for (var i = 0; i < variants.Count; i++) options[i] = variants[i].AttributeValue;
        return new UnitInfo(name, options);
    }

    internal static CategoryInfo ToContract(this CategoryReadModel row) =>
        new(row.Id, row.Name, ToneOf(row.Id), row.ProductCount);

    internal static Contracts.Inventory.StockMovement ToContract(this MovementReadModel row, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(row.OccurredUtc.UtcDateTime, zone);
        return new(row.Id, row.ProductId, row.VariantId, KindOf(row.Kind), ScaledToInt(row.QuantityScaled),
            SourceOf(row), row.RecordedByUserId, Jalali(local), local.ToString("HH:mm", CultureInfo.InvariantCulture),
            row.Reason);
    }

    internal static ProductDetail ToContract(this ProductDetailReadModel row, TimeZoneInfo zone) =>
        new(row.Product.ToContract(), row.Movements.Select(m => m.ToContract(zone)).ToArray());

    /// <summary>Contracts movement kind: <c>in</c> / <c>out</c> / <c>adjust</c>.</summary>
    private static string KindOf(string kind) => kind switch
    {
        "Receipt" or "TransferIn" or "ReturnIn" => "in",
        "AdjustmentIn" or "AdjustmentOut" => "adjust",
        _ => "out", // Sale, Consumption, Loss, TransferOut, ReturnOut.
    };

    /// <summary>
    /// The movement's human source line: the causing document where the ledger records one, else the
    /// movement's own reason. Persian text because the Contracts field is rendered verbatim.
    /// </summary>
    private static string SourceOf(MovementReadModel row) => row.SourceEntityType switch
    {
        "GoodsReceipt" => "خرید " + PersianFormat.Digits(row.SourceEntityId ?? string.Empty),
        "Invoice" => "فروش " + PersianFormat.Digits(row.SourceEntityId ?? string.Empty),
        "Seed" => "موجودی اولیه",
        null or "" => string.Equals(row.Reason, "OpeningBalance", StringComparison.Ordinal)
            ? "موجودی اولیه"
            : row.Reason ?? "حرکت انبار",
        _ => row.SourceEntityType,
    };

    /// <summary>
    /// The UI tone key (<c>a</c>…<c>f</c>). The domain has no presentation column, so the tone is a stable
    /// derivation of the row id — the same row always renders the same tint (PERFORMANCE.md §2: no LINQ
    /// closure, no per-call state).
    /// </summary>
    internal static string ToneOf(string id)
    {
        var sum = 0;
        for (var i = 0; i < id.Length; i++) sum = (sum + id[i]) % 6;
        return ToneAlphabet[sum];
    }

    internal static int ScaledToInt(long scaled) =>
        (int)Math.Round(scaled / 1000m, MidpointRounding.AwayFromZero);

    internal static long IntToScaled(int whole) => whole * 1000L;

    /// <summary>Jalali <c>yyyy/MM/dd</c>, matching how the other Client.Services mappings render dates.</summary>
    private static string Jalali(DateTime local)
    {
        var calendar = new PersianCalendar();
        return $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00}";
    }

    private static readonly string[] ToneAlphabet = ["a", "b", "c", "d", "e", "f"];
}

/// <summary>
/// Persian display formatting for the Contracts fields that are documented as pre-formatted strings
/// (Inventory + Purchasing). Digits are transliterated and thousands are grouped with the Persian
/// thousands separator, matching the reference screens.
/// </summary>
internal static class PersianFormat
{
    private const char ThousandsSeparator = '\u066C'; // ٬

    internal static string Digits(string value)
    {
        if (value.Length == 0) return value;
        Span<char> buffer = stackalloc char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            buffer[i] = c is >= '0' and <= '9' ? (char)('\u06F0' + (c - '0')) : c;
        }
        return new string(buffer);
    }

    /// <summary>An integer count, e.g. <c>۶</c>.</summary>
    internal static string Count(int value) => Digits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A grouped toman amount, e.g. <c>۲۸٬۶۵۰٬۰۰۰ تومان</c>.</summary>
    internal static string Toman(long toman) => Digits(Group(toman)) + " تومان";

    /// <summary>A compact toman amount for a KPI tile, e.g. <c>۱۲۸ م</c>; falls back to the full amount.</summary>
    internal static string CompactToman(long toman) => toman >= 1_000_000L
        ? Digits((toman / 1_000_000L).ToString(CultureInfo.InvariantCulture)) + " م"
        : Toman(toman);

    /// <summary>Groups an integer with the Persian thousands separator.</summary>
    internal static string Group(long value)
    {
        var negative = value < 0;
        var text = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        var groups = (text.Length + 2) / 3;
        var builder = new System.Text.StringBuilder(text.Length + groups + (negative ? 1 : 0));
        if (negative) builder.Append('-');
        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && (text.Length - i) % 3 == 0) builder.Append(ThousandsSeparator);
            builder.Append(text[i]);
        }
        return builder.ToString();
    }
}
