using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Sales;
using Animora.Contracts.Sync;

namespace Animora.Desktop.Pages.Sales;

/// <summary>Outcome of the pre-settlement review.</summary>
internal enum PreSettleVerdict
{
    /// <summary>Online and every line is still sellable — go ahead.</summary>
    Proceed,

    /// <summary>No server reachable — the review is skipped; the cashier is warned and may continue (offline-first).</summary>
    Offline,

    /// <summary>Online, but a line was removed from the catalog or its stock dropped — settlement must wait for a fix.</summary>
    Blocked,
}

/// <summary>The verdict plus the (Persian) message to show for <see cref="PreSettleVerdict.Offline"/> / <see cref="PreSettleVerdict.Blocked"/>.</summary>
internal readonly record struct PreSettleOutcome(PreSettleVerdict Verdict, string Message);

/// <summary>
/// Pre-settlement review of an invoice (the cashier's «ثبت/تسویه»). Online: flush + pull through the existing
/// <see cref="ISyncStatusService.SyncNowAsync"/>, read the fresh catalog ONCE, and compare the invoice's lines with it
/// (a product/service that was removed, or a good whose on-hand dropped below what the invoice holds). Offline: nothing
/// is checked, a clear warning is returned and the settlement may continue. Only a never-posted <c>Draft</c> is
/// reviewed — a posted invoice's stock is already deducted, so comparing it with on-hand would raise false shortages.
/// </summary>
internal static class PreSettleReview
{
    internal const string OfflineWarning =
        "در حال حاضر آفلاین هستید؛ بهتر است آنلاین شوید تا ناهماهنگی با سرور پیش نیاید. پرداخت به‌صورت محلی ثبت می‌شود و پس از اتصال اعمال خواهد شد.";

    private const int MaxListed = 3;

    public static async Task<PreSettleOutcome> RunAsync(ISalesQueryService sales, ISyncStatusService? sync, string invoiceId,
        CancellationToken ct = default)
    {
        if (sync is null || sync.State == SyncState.Offline)
        {
            return new PreSettleOutcome(PreSettleVerdict.Offline, OfflineWarning);
        }

        try
        {
            await sync.SyncNowAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            Animora.Desktop.Diagnostics.AppTrace.Warn($"pre-settle sync failed — {ex.Message}", "sales");
        }

        // The pass may have discovered we are offline after all.
        if (sync.State == SyncState.Offline)
        {
            return new PreSettleOutcome(PreSettleVerdict.Offline, OfflineWarning);
        }

        var invoice = await sales.GetInvoiceAsync(invoiceId);
        if (invoice is null || invoice.State != InvoiceState.Draft)
        {
            return new PreSettleOutcome(PreSettleVerdict.Proceed, string.Empty);
        }

        var catalog = await sales.GetCatalogAsync();
        var problems = Review(invoice.Lines, catalog.Items);
        return problems.Count == 0
            ? new PreSettleOutcome(PreSettleVerdict.Proceed, string.Empty)
            : new PreSettleOutcome(PreSettleVerdict.Blocked, Format(problems));
    }

    /// <summary>Pure comparison of the invoice lines against the fresh catalog; one message per problem line.</summary>
    internal static List<string> Review(IReadOnlyList<InvoiceLine> lines, IReadOnlyList<CatalogItem> items)
    {
        var byId = new Dictionary<string, CatalogItem>(items.Count, StringComparer.Ordinal);
        foreach (var item in items)
        {
            byId[item.Id] = item;
        }

        var problems = new List<string>();
        var heldByRef = new Dictionary<string, int>(StringComparer.Ordinal);
        var firstTitle = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.IsCustom || string.IsNullOrEmpty(line.Ref))
            {
                continue;
            }

            if (!byId.ContainsKey(line.Ref))
            {
                problems.Add($"«{line.Title}» دیگر در فهرست محصولات و خدمات وجود ندارد");
                continue;
            }

            if (line.Kind == InvoiceLineKind.Item)
            {
                heldByRef[line.Ref] = heldByRef.GetValueOrDefault(line.Ref) + line.Quantity;
                firstTitle.TryAdd(line.Ref, line.Title);
            }
        }

        foreach (var (id, held) in heldByRef)
        {
            var item = byId[id];
            if (item.TracksStock && held > item.AvailableUnits)
            {
                problems.Add($"موجودی «{firstTitle[id]}» کافی نیست (موجود: {SalePresentation.Fa(item.AvailableUnits)}، در فاکتور: {SalePresentation.Fa(held)})");
            }
        }

        return problems;
    }

    private static string Format(List<string> problems)
    {
        var listed = string.Join("؛ ", problems.GetRange(0, Math.Min(MaxListed, problems.Count)));
        var more = problems.Count > MaxListed ? $" و {SalePresentation.Fa(problems.Count - MaxListed)} مورد دیگر" : string.Empty;
        return $"ثبت فاکتور انجام نشد؛ ابتدا فاکتور را اصلاح کنید: {listed}{more}";
    }
}
