using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Scheduling;
using Animora.Contracts.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Appointments;

/// <summary>Real appointment calendar reads; each call owns an isolated dispatch scope.</summary>
public sealed class AppointmentsQueryService(IServiceScopeFactory scopes) : IAppointmentsQueryService
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
        (await QueryAsync(new AppointmentDoctorsQuery())).Select(r => r.ToContract()).ToArray();

    public Task<IReadOnlyList<AppointmentType>> GetTypesAsync() => Task.FromResult(AppointmentsMapping.Types);

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
