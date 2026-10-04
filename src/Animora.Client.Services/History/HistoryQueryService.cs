using System.Globalization;
using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.History;
using Animora.Contracts.History;
using Animora.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.History;

/// <summary>Scoped dispatch over the persisted feed; no mock-derived events.</summary>
public sealed class HistoryQueryService(IServiceScopeFactory scopes) : IHistoryQueryService
{
    public async Task<HistoryPage> GetPageAsync(HistoryFilter filter)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return new HistoryPage([], 0, 0, 0);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new ActivityFeedQuery(filter), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        var zone = scope.ServiceProvider.GetRequiredService<Animora.Application.Ports.IClinicTimeZoneProvider>().GetClinicTimeZone();
        var events = result.Value!.Items.Select(x => HistoryMapping.Map(x, zone)).ToArray();
        var dayNets = new Dictionary<string, long>();
        if (result.Value.DayNets is { } nets)
        {
            for (var i = 0; i < events.Length; i++)
            {
                if (nets.TryGetValue(result.Value.Items[i].LocalDay, out var net)) dayNets[events[i].Date] = net;
            }
        }

        return new HistoryPage(events, result.Value.TotalCount, result.Value.IncomeTotal, result.Value.ExpenseTotal, dayNets);
    }

    public async Task<string> ExportCsvAsync(HistoryFilter filter, HistoryCsvLabels labels)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new ActivityExportQuery(filter), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        var zone = scope.ServiceProvider.GetRequiredService<Animora.Application.Ports.IClinicTimeZoneProvider>().GetClinicTimeZone();
        var events = result.Value!.Select(x => HistoryMapping.Map(x, zone)).ToArray();
        return await CsvWriter.WriteAsync(events, labels);
    }
}

internal static class HistoryMapping
{
    internal static HistoryEvent Map(ActivityRow row, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(row.OccurredUtc, zone);
        var cal = new PersianCalendar();
        return new HistoryEvent(row.Id, Enum.Parse<HistoryEventKind>(row.Kind),
            $"{cal.GetYear(local.DateTime):0000}/{cal.GetMonth(local.DateTime):00}/{cal.GetDayOfMonth(local.DateTime):00}",
            local.ToString("HH:mm", CultureInfo.InvariantCulture), row.TitleKey, row.DetailText,
            Entity(row), row.AmountMinor, row.AmountSign,
            Enum.Parse<HistoryEventStatus>(row.Status),
            string.IsNullOrWhiteSpace(row.ActorUserId) ? ActorRef.System :
                new ActorRef(row.ActorUserId, row.ActorName, string.Empty, string.Empty));
    }

    /// <summary>
    /// The wire entity tag: <c>Type:Id</c>, plus a third <c>:InvoiceId</c> segment for <c>Payment</c>
    /// rows (the feed's EntityId is the payment id; the unified invoice sheet needs the owning invoice).
    /// The invoice id is read from the row's stored detail args — no extra query per row.
    /// </summary>
    private static string Entity(ActivityRow row)
    {
        var tag = row.EntityType + ":" + row.EntityId;
        if (!string.Equals(row.EntityType, "Payment", StringComparison.Ordinal) || string.IsNullOrEmpty(row.DetailArgsJson))
            return tag;

        try
        {
            using var doc = JsonDocument.Parse(row.DetailArgsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("InvoiceId", out var invoice)
                && invoice.ValueKind == JsonValueKind.String)
            {
                var invoiceId = invoice.GetString();
                if (!string.IsNullOrEmpty(invoiceId)) return tag + ":" + invoiceId;
            }
        }
        catch (JsonException)
        {
            // malformed args → fall back to the plain tag (the opener then targets the payment id, a safe no-op)
        }

        return tag;
    }
}
