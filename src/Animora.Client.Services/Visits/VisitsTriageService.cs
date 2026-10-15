using System.Net;
using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.Ports;
using Animora.Client.Services.Identity;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Application.UseCases.Visits;
using Animora.Contracts.Appointments;
using Animora.Contracts.Visits;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Visits;

public sealed class VisitsTriageService(
    IServiceScopeFactory scopes,
    OperationIdFactory ids,
    IAppointmentSettingsService appointmentSettings) : IVisitsTriageService
{
    public async Task<IReadOnlyList<DoctorRailInfo>> GetDoctorsAsync()
    {
        await RefreshDoctorDirectoryAsync().ConfigureAwait(false);
        return await QueryAsync(new TriageDoctorRailQuery(DateOnly.FromDateTime(DateTime.Today)),
            rows => (IReadOnlyList<DoctorRailInfo>)rows.Select(x => x.ToContract()).ToArray()).ConfigureAwait(false);
    }

    private async Task RefreshDoctorDirectoryAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService<OnlineApi>() is not { } api) return; // Standalone/offline installation.
        var provider = sp.GetRequiredService<ICurrentUserProvider>();
        var queryContext = provider.BuildQueryContext();
        if (queryContext.IsFailure) throw new ServiceException(queryContext.Error);
        var sessions = sp.GetRequiredService<ISessionStore>();
        var stored = await sessions.ReadAsync().ConfigureAwait(false);
        if (stored.IsFailure) throw new ServiceException(stored.Error);
        if (stored.Value is not { } session || session.Role != nameof(StaffRole.Manager)) return;
        if (session.TenantId != queryContext.Value!.TenantId || session.UserId != queryContext.Value.UserId)
            throw new ServiceException(ErrorCodes.TenantMismatch);

        var exchange = await api.SendAsync(HttpMethod.Get, "auth/staff", body: null, bearer: null).ConfigureAwait(false);
        using var response = exchange.Response;
        // An unreachable server keeps the last successfully mirrored roster usable offline.
        if (response is null || OnlineApi.IsTransient(response.StatusCode)) return;
        if (!response.IsSuccessStatusCode)
        {
            var code = await OnlineApi.ReadErrorCodeAsync(response).ConfigureAwait(false);
            if (code is not null) throw new ServiceException(code, ErrorCode.Forbidden);
            throw new ServiceException(response.StatusCode == HttpStatusCode.Unauthorized
                ? ErrorCodes.SessionExpired : ErrorCodes.PermissionDenied);
        }
        var directory = await OnlineApi.ReadAsync<StaffDirectoryWire>(response).ConfigureAwait(false);
        if (directory?.Members is null || directory.Members.Any(m => m is null
                || !Enum.TryParse<StaffRole>(m.Role, out var role) || !Enum.IsDefined(role)))
            throw new ServiceException(ErrorCodes.SyncProtocolViolation);
        if (!directory.CanManageUsers) throw new ServiceException(ErrorCodes.PermissionDenied);
        // Recheck the identity after the HTTP await so a concurrent tenant/login switch cannot import
        // another clinic's roster under the newly active session.
        var current = await sessions.ReadAsync().ConfigureAwait(false);
        if (current.IsFailure) throw new ServiceException(current.Error);
        if (current.Value?.TenantId != session.TenantId || current.Value?.UserId != session.UserId)
            throw new ServiceException(ErrorCodes.SessionExpired);
        var doctors = directory.Members.Where(m => m.Role == nameof(StaffRole.Doctor))
            .Select(m => new VisitDoctorDirectoryEntry(m.UserId, m.Username, m.Name, m.IsActive)).ToArray();
        var context = await provider.BuildCommandContextAsync(null, null, null, default).ConfigureAwait(false);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await sp.GetRequiredService<IDispatcher>().SendAsync(
            new RefreshVisitDoctorDirectoryCommand(session.TenantId, session.UserId, doctors), context.Value!).ConfigureAwait(false);
        if (result.IsFailure) throw new ServiceException(result.Error);
    }

    public Task<IReadOnlyList<BookedAppointmentRef>> GetBookedForDoctorAsync(string doctorId) => QueryAsync(
        new BookedForDoctorQuery(doctorId, DateOnly.FromDateTime(DateTime.Today)),
        rows => (IReadOnlyList<BookedAppointmentRef>)rows.Select(x => x.ToContract()).ToArray());

    public IReadOnlyList<VisitTypeInfo> GetVisitTypes() => VisitTypeCatalog.All;

    public async Task<IReadOnlyList<VisitTypeInfo>> GetVisitTypesAsync()
    {
        var merged = VisitTypeCatalog.All.ToList();
        try
        {
            var settings = await appointmentSettings.GetAsync().ConfigureAwait(false);
            foreach (var custom in settings.CustomTypes)
            {
                // Built-ins win on an Id collision; custom types keep their persisted Persian label + color.
                if (merged.Any(t => string.Equals(t.Id, custom.Id, StringComparison.Ordinal))) continue;
                merged.Add(new VisitTypeInfo(custom.Id, custom.Label, "IconBriefcase",
                    string.IsNullOrWhiteSpace(custom.ColorHex) ? "#0d9488" : custom.ColorHex,
                    custom.DurationMinutes > 0 ? custom.DurationMinutes : 30));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // Settings unreadable (permission / file problem): the built-in Persian catalog still works.
        }

        return merged;
    }

    public Task<IReadOnlyList<VisitPatientRef>> GetPatientsAsync(string? search = null) => QueryAsync(
        new TriagePatientsQuery(string.IsNullOrWhiteSpace(search) ? null : search.Trim()),
        rows => (IReadOnlyList<VisitPatientRef>)rows.Select(x => x.ToContract()).ToArray());

    public async Task<EncounterDraft> StartFromTicketAsync(string ticketId) =>
        (await SendAsync(new StartVisitFromTicketCommand(ticketId),
            ids.Deterministic("visit-start:" + ticketId))).ToContract();

    public async Task<EncounterDraft> StartNewAsync(NewVisitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await SendAsync(new StartNewVisitCommand(request.DoctorId, request.PatientName,
            request.PatientKind, request.OwnerName, request.OwnerPhone, request.TypeId,
            request.IsUrgent, request.Note), null)).ToContract();
    }

    private async Task<TOut> QueryAsync<TModel, TOut>(IQuery<TModel> query, Func<TModel, TOut> map)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return map(result.Value!);
    }

    private async Task<Animora.Application.UseCases.Visits.ReadModels.EncounterDraftReadModel> SendAsync(
        ICommand<Animora.Application.UseCases.Visits.ReadModels.EncounterDraftReadModel> command, string? operationId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = await provider.BuildCommandContextAsync(operationId, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }
}
