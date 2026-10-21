using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Scheduling.ReadModels;
using Animora.Application.UseCases.Vaccination;
using Animora.Contracts.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Appointments;

/// <summary>Real appointment calendar reads; each call owns an isolated dispatch scope.</summary>
public sealed class AppointmentsQueryService(IServiceScopeFactory scopes, IAppointmentSettingsService settings)
    : IAppointmentsQueryService
{
    internal async Task<T> QueryAsync<T>(IQuery<T> query, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        if (context.IsFailure)
            throw new AppointmentOperationException(context.Error.Code, context.Error.Category, context.Detail);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!, ct);
        if (result.IsFailure)
            throw new AppointmentOperationException(result.Error.Code, result.Error.Category, result.Detail);
        return result.Value!;
    }

    // A23: the singleton provider hydrates the zone asynchronously and caches it, so no caller ever runs a
    // synchronous database read on its own thread.
    internal async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>()
            .GetClinicTimeZoneAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AppointmentDoctor>> GetDoctorsAsync() =>
        (await QueryAsync(new AppointmentDoctorsQuery())).Select((r, i) => r.ToContract(i)).ToArray();

    /// <summary>
    /// The fixed catalog types followed by the clinic's own types (Settings tab). Catalog types are never
    /// dropped; custom ids are prefixed (<c>c-</c>) so they cannot collide with catalog codes.
    /// </summary>
    public async Task<IReadOnlyList<AppointmentType>> GetTypesAsync()
    {
        var custom = (await settings.GetAsync()).CustomTypes;
        var types = new List<AppointmentType>(AppointmentsMapping.Types.Count + custom.Count
            + AppointmentsMapping.TreatmentTypes.Count + 1);
        types.AddRange(AppointmentsMapping.Types);
        types.Add(AppointmentsMapping.BoosterType);
        types.AddRange(AppointmentsMapping.TreatmentTypes);
        foreach (var c in custom)
            types.Add(AppointmentsMapping.BuildType(c.Id, c.Label, c.Id, c.ColorHex, c.DurationMinutes));
        return types;
    }

    public async Task<IReadOnlyList<AppointmentEvent>> GetForRangeAsync(DateOnly start, DateOnly end)
    {
        var tz = await GetTimeZoneAsync();
        var events = (await QueryAsync(new AppointmentRangeQuery(start, end))).Select(r => r.ToContract(tz)).ToList();
        events.AddRange(await GetDueEventsAsync(start, end));
        return events;
    }

    /// <summary>
    /// The due items of the «دارو و واکسن» calendar in range — treatment-plan days and the next-dose reminders of
    /// administered vaccinations — as read-only calendar entries. One query feeds both this calendar and the
    /// vaccination screen, so the two can never disagree. Strictly additive: a missing vaccination permission or
    /// an over-wide range yields no entries, never a broken calendar.
    /// </summary>
    private async Task<IReadOnlyList<AppointmentEvent>> GetDueEventsAsync(DateOnly start, DateOnly end)
    {
        try
        {
            var rows = await QueryAsync(new TreatmentCalendarQuery(start, end));
            return rows.Select(r => r.ToDueEvent()).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<AppointmentEvent>> GetQueueAsync()
    {
        var tz = await GetTimeZoneAsync();
        return (await QueryAsync(new AppointmentQueueQuery())).Select(r => r.ToContract(tz)).ToArray();
    }

    public async Task<AppointmentHistoryPage> GetHistoryAsync(AppointmentHistoryFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var tz = await GetTimeZoneAsync(ct);
        var page = await QueryAsync(new AppointmentHistoryQuery(
            filter.From, filter.To,
            filter.Status switch
            {
                AppointmentHistoryStatusFilter.Completed => AppointmentHistoryStatus.Completed,
                AppointmentHistoryStatusFilter.NoShow => AppointmentHistoryStatus.NoShow,
                AppointmentHistoryStatusFilter.Cancelled => AppointmentHistoryStatus.Cancelled,
                _ => AppointmentHistoryStatus.All,
            },
            filter.DoctorId, filter.TypeId,
            filter.Invoice switch
            {
                AppointmentInvoiceFilter.None => HistoryInvoiceFilter.None,
                AppointmentInvoiceFilter.Unpaid => HistoryInvoiceFilter.Unpaid,
                AppointmentInvoiceFilter.Partial => HistoryInvoiceFilter.Partial,
                AppointmentInvoiceFilter.Paid => HistoryInvoiceFilter.Paid,
                _ => HistoryInvoiceFilter.All,
            },
            filter.Search, filter.Page, filter.PageSize), ct);
        var s = page.Summary;
        return new AppointmentHistoryPage(
            page.Items.Select(r => r.ToContract(tz)).ToArray(),
            new AppointmentHistoryTotals(s.Total, s.Completed, s.Cancelled, s.NoShow, s.PaidMinor, s.OwedMinor),
            page.HasMore);
    }
}
