using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Scheduling.ReadModels;
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

    internal TimeZoneInfo GetTimeZone()
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
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
        if (custom.Count == 0) return AppointmentsMapping.Types;

        var types = new List<AppointmentType>(AppointmentsMapping.Types.Count + custom.Count);
        types.AddRange(AppointmentsMapping.Types);
        foreach (var c in custom)
            types.Add(AppointmentsMapping.BuildType(c.Id, c.Label, c.Id, c.ColorHex, c.DurationMinutes));
        return types;
    }

    public async Task<IReadOnlyList<AppointmentEvent>> GetForRangeAsync(DateOnly start, DateOnly end)
    {
        var tz = GetTimeZone();
        return (await QueryAsync(new AppointmentRangeQuery(start, end))).Select(r => r.ToContract(tz)).ToArray();
    }

    public async Task<IReadOnlyList<AppointmentEvent>> GetQueueAsync()
    {
        var tz = GetTimeZone();
        return (await QueryAsync(new AppointmentQueueQuery())).Select(r => r.ToContract(tz)).ToArray();
    }

    public async Task<AppointmentHistoryPage> GetHistoryAsync(AppointmentHistoryFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var tz = GetTimeZone();
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
