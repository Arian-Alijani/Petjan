using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Animora.Contracts.Sales;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// UI-side stock ceiling for invoice-line quantity edits (defense in depth behind the server gate in
/// <c>EditInvoiceLineCommand</c>). One catalog read yields the on-hand whole units of every stock-tracked good;
/// the per-line ceiling is those units minus what the OTHER lines of the same variant already hold.
/// </summary>
internal static class InvoiceStockCaps
{
    /// <summary>Reads the catalog once and maps every stock-tracked item id (variant id) to its whole on-hand units.</summary>
    public static async Task<IReadOnlyDictionary<string, int>> LoadAsync(ISalesQueryService service)
    {
        var snapshot = await service.GetCatalogAsync();
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in snapshot.Items)
        {
            if (item.TracksStock)
            {
                map[item.Id] = item.AvailableUnits;
            }
        }

        return map;
    }

    /// <summary>
    /// The highest quantity <paramref name="line"/> may hold. <see cref="int.MaxValue"/> for services, custom lines
    /// and untracked goods (no ceiling).
    /// </summary>
    public static int MaxFor(InvoiceLine line, IReadOnlyList<InvoiceLine> allLines, IReadOnlyDictionary<string, int> onHand)
    {
        if (line.Kind != InvoiceLineKind.Item || line.IsCustom || string.IsNullOrEmpty(line.Ref)
            || !onHand.TryGetValue(line.Ref, out var available))
        {
            return int.MaxValue;
        }

        long others = 0;
        foreach (var other in allLines)
        {
            if (other.Index != line.Index && string.Equals(other.Ref, line.Ref, StringComparison.Ordinal))
            {
                others += other.Quantity;
            }
        }

        return (int)Math.Max(0, available - others);
    }

    /// <summary>The Persian refusal shown when a quantity would exceed the on-hand stock.</summary>
    public static string ExceedsMessage(string title, int max) =>
        max <= 0
            ? $"«{title}» موجودی ندارد و تعداد آن قابل افزایش نیست"
            : $"بیش از موجودی «{title}» قابل افزودن نیست؛ حداکثر {SalePresentation.Fa(max)} عدد";
}
