using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.UseCases.Visits;
using Animora.Contracts.Visits;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Visits;

public sealed class VisitsTriageService(IServiceScopeFactory scopes, OperationIdFactory ids) : IVisitsTriageService
{
    public Task<IReadOnlyList<DoctorRailInfo>> GetDoctorsAsync() => QueryAsync(
        new TriageDoctorRailQuery(DateOnly.FromDateTime(DateTime.Today)),
        rows => (IReadOnlyList<DoctorRailInfo>)rows.Select(x => x.ToContract()).ToArray());

    public Task<IReadOnlyList<BookedAppointmentRef>> GetBookedForDoctorAsync(string doctorId) => QueryAsync(
        new BookedForDoctorQuery(doctorId, DateOnly.FromDateTime(DateTime.Today)),
        rows => (IReadOnlyList<BookedAppointmentRef>)rows.Select(x => x.ToContract()).ToArray());

    public IReadOnlyList<VisitTypeInfo> GetVisitTypes() => VisitTypeCatalog.All;

    public Task<IReadOnlyList<VisitPatientRef>> GetPatientsAsync() => QueryAsync(
        new TriagePatientsQuery(null),
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
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        return map(result.Value!);
    }

    private async Task<Animora.Application.UseCases.Visits.ReadModels.EncounterDraftReadModel> SendAsync(
        ICommand<Animora.Application.UseCases.Visits.ReadModels.EncounterDraftReadModel> command, string? operationId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = await provider.BuildCommandContextAsync(operationId, null, null, default);
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        return result.Value!;
    }
}
