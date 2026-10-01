using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Scheduling;
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
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!, ct);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
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

    public async Task<IReadOnlyList<AppointmentEvent>> GetHistoryAsync()
    {
        var tz = GetTimeZone();
        return (await QueryAsync(new AppointmentHistoryQuery())).Select(r => r.ToContract(tz)).ToArray();
    }
}
