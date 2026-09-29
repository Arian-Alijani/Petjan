using System.Globalization;
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
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        var zone = scope.ServiceProvider.GetRequiredService<Animora.Application.Ports.IClinicTimeZoneProvider>().GetClinicTimeZone();
        return new HistoryPage(result.Value!.Items.Select(x => HistoryMapping.Map(x, zone)).ToArray(),
            result.Value.TotalCount, result.Value.IncomeTotal, result.Value.ExpenseTotal);
    }

    public async Task<string> ExportCsvAsync(HistoryFilter filter)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new ActivityExportQuery(filter), context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        return await CsvWriter.WriteAsync(result.Value!);
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
            local.ToString("HH:mm", CultureInfo.InvariantCulture), row.TitleKey, row.DetailArgsJson,
            row.EntityType + ":" + row.EntityId, row.AmountMinor, row.AmountSign,
            Enum.Parse<HistoryEventStatus>(row.Status),
            string.IsNullOrWhiteSpace(row.ActorUserId) ? ActorRef.System :
                new ActorRef(row.ActorUserId, row.ActorName, string.Empty, string.Empty));
    }
}
