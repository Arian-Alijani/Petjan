using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.UseCases.Scheduling;
using Animora.Contracts.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Appointments;

/// <summary>
/// Appointment settings seam (A17). Every call dispatches an Application use case in its own scope, so the
/// authorization stage (<c>appointments.read</c> for reads, <c>settings.write</c> for every mutation) applies to a
/// direct Contracts call exactly as it does to the settings tab. Persistence sits behind
/// <c>IAppointmentSettingsStore</c>; this class holds no state and does no file I/O.
/// </summary>
public sealed class AppointmentSettingsService(IServiceScopeFactory scopes) : IAppointmentSettingsService
{
    public Task<AppointmentSettings> GetAsync() => QueryAsync(new GetAppointmentSettingsQuery());

    public Task SaveAsync(AppointmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SendAsync(new SaveAppointmentSettingsCommand(settings));
    }

    public Task<CustomAppointmentType> AddTypeAsync(string label, int durationMinutes, bool requiresDoctor) =>
        SendAsync(new AddAppointmentTypeCommand(label, durationMinutes, requiresDoctor));

    public Task RemoveTypeAsync(string typeId) => SendAsync(new RemoveAppointmentTypeCommand(typeId));

    public Task SetCalendarModeAsync(CalendarMode mode) => SendAsync(new SetCalendarModeCommand(mode));

    public Task SetSmsAsync(AppointmentSmsSettings sms)
    {
        ArgumentNullException.ThrowIfNull(sms);
        return SendAsync(new SetAppointmentSmsCommand(sms));
    }

    private async Task<T> QueryAsync<T>(IQuery<T> query)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        return result.Value!;
    }

    private async Task<T> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        return result.Value!;
    }

    private Task SendAsync(ICommand<Unit> command) => SendAsync<Unit>(command);
}
