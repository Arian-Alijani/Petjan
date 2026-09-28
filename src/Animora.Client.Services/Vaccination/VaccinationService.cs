using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Vaccination;
using Animora.Contracts.Vaccination;
using Microsoft.Extensions.DependencyInjection;
using ContractRecord = Animora.Contracts.Vaccination.VaccinationRecord;

namespace Animora.Client.Services.Vaccination;

/// <summary>Local dispatcher-backed vaccination service. SMS actions create an audit record only.</summary>
public sealed class VaccinationService(IServiceScopeFactory scopes, OperationIdFactory ids) : IVaccinationService
{
    public async Task<IReadOnlyList<ContractRecord>> GetRecordsAsync() =>
        (await QueryAsync(new VaccinationDueQuery(DateOnly.FromDateTime(DateTime.UtcNow))))
            .Select(r => r.ToContract()).ToArray();

    public async Task<IReadOnlyList<VaccineCatalogEntry>> GetCatalogAsync() =>
        (await QueryAsync(new VaccinationCatalogQuery())).Select(r => r.ToContract()).ToArray();

    public async Task<IReadOnlyList<SmsTemplate>> GetSmsTemplatesAsync() =>
        (await QueryAsync(new SmsTemplatesQuery())).Select(r => r.ToContract()).ToArray();

    public async Task<ContractRecord> MarkDoneAsync(string recordId)
    {
        await SendAsync(new MarkVaccinationDoneCommand(recordId),
            ids.Deterministic("vaccination-done:" + recordId));
        return await FindAsync(recordId);
    }

    public async Task<SmsPreview> BuildSmsPreviewAsync(string recordId, string templateId)
    {
        var record = await FindAsync(recordId);
        var template = (await GetSmsTemplatesAsync()).FirstOrDefault(t => t.Id == templateId)
            ?? throw new InvalidOperationException("SMS_TEMPLATE_NOT_FOUND");
        return SmsTemplateRenderer.Render(template, record);
    }

    public async Task<ContractRecord> SendSmsAsync(string recordId, string message)
    {
        // This is a local reminder audit; there is no external gateway in C03.
        await SendAsync(new RecordVaccinationReminderCommand(recordId, message));
        return (await FindAsync(recordId)) with { SmsState = SmsState.Sent };
    }

    public async Task<VaccinationHistoryPage> GetHistoryAsync(VaccinationHistoryFilter filter)
    {
        var page = await QueryAsync(new VaccinationHistoryQuery(filter));
        return new VaccinationHistoryPage(page.Items.Select(r => r.ToContract()).ToArray(), page.TotalCount);
    }

    private async Task<ContractRecord> FindAsync(string id)
    {
        var row = (await QueryAsync(new VaccinationDueQuery(DateOnly.FromDateTime(DateTime.UtcNow))))
            .FirstOrDefault(r => r.Id == id);
        return row?.ToContract() ?? throw new InvalidOperationException("VACCINATION_NOT_FOUND");
    }

    private async Task<T> QueryAsync<T>(IQuery<T> query)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        return result.Value!;
    }

    private async Task SendAsync<T>(ICommand<T> command, string? operationId = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>()
            .BuildCommandContextAsync(operationId, null, null, default);
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
    }
}
