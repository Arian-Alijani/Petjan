using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.UseCases.Visits;
using Animora.Contracts.Visits;
using Animora.Domain.Clinical;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Visits;

/// <summary>Scoped dispatch per call; no EF or dispatcher captured by the singleton service.</summary>
public sealed class VisitEncounterService(IServiceScopeFactory scopes, OperationIdFactory ids,
    DebounceBuffer notes) : IVisitEncounterService
{
    public async Task<EncounterDraft> GetAsync(string encounterId) =>
        (await QueryAsync(new EncounterDraftQuery(encounterId))).Draft.ToContract();

    public Task SaveVitalsAsync(string encounterId, Vitals vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);
        return SendAsync(new SaveVisitVitalsCommand(encounterId,
            vitals.WeightKg is { } w ? (decimal)w : null,
            vitals.TemperatureCelsius is { } t ? (decimal)t : null,
            vitals.PulseBpm, vitals.RespirationRate));
    }

    public Task SaveHistoryNoteAsync(string encounterId, string note) => SaveNoteAsync(encounterId, VisitNoteField.History, note);
    public Task SaveExamNoteAsync(string encounterId, string note) => SaveNoteAsync(encounterId, VisitNoteField.Exam, note);
    public Task SaveDiagnosisAsync(string encounterId, string diagnosis) => SaveNoteAsync(encounterId, VisitNoteField.Diagnosis, diagnosis);
    public Task SavePlanAsync(string encounterId, string plan) => SaveNoteAsync(encounterId, VisitNoteField.Plan, plan);

    private Task SaveNoteAsync(string encounterId, VisitNoteField field, string text)
    {
        notes.Enqueue(encounterId, field, ct => SendAsync(new SaveVisitNoteCommand(encounterId, field, text ?? string.Empty), ct));
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => notes.FlushAsync(cancellationToken);

    public async Task AddRxLineAsync(string encounterId, RxLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        await FlushAsync();
        await SendAsync(new AddPrescriptionLineCommand(encounterId, line.CatalogId, line.Title,
            line.Kind == RxCatalogKind.Service ? VisitLineSource.Service : VisitLineSource.Product,
            line.UnitPrice, line.Quantity, line.Id));
    }

    public async Task RemoveRxLineAsync(string encounterId, string rxLineId)
    {
        await FlushAsync();
        await SendAsync(new RemovePrescriptionLineCommand(encounterId, rxLineId));
    }

    public async Task<IReadOnlyList<RxCatalogItem>> GetCatalogAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new RxCatalogQuery(), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!.Select(r => r.ToContract()).ToArray();
    }

    public async Task<EncounterSummary> CompleteAsync(string encounterId)
    {
        await FlushAsync();
        var summary = await SendAsync(new CompleteVisitCommand(encounterId),
            ids.Deterministic("visit-complete:" + encounterId));
        return summary.ToContract();
    }

    private async Task<T> QueryAsync<T>(IQuery<T> query)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }

    private async Task<T> SendAsync<T>(ICommand<T> command, string? operationId = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>()
            .BuildCommandContextAsync(operationId, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }

    private async Task SendAsync<T>(ICommand<T> command, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>()
            .BuildCommandContextAsync(null, null, null, ct);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!, ct);
        if (result.IsFailure) throw new ServiceException(result.Error);
    }
}
