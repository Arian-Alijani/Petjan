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
        (await QueryAsync(new VaccinationDueQuery(await ClinicDayWindow.TodayDateAsync(scopes).ConfigureAwait(false))))
            .Select(r => r.ToContract()).ToArray();

    public async Task<IReadOnlyList<VaccineCatalogEntry>> GetCatalogAsync() =>
        (await QueryAsync(new VaccinationCatalogQuery())).Select(r => r.ToContract()).ToArray();

    public async Task<IReadOnlyList<SmsTemplate>> GetSmsTemplatesAsync() =>
        (await QueryAsync(new SmsTemplatesQuery())).Select(r => r.ToContract()).ToArray();

    public async Task<string> RegisterDoseAsync(VaccinationDoseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.PetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.VaccineName);

        var name = input.VaccineName.Trim();
        // The vial's commercial name is the display value; the protocol code drives the dose schedule.
        // A free-text name with no explicit code falls back to the name itself (unknown codes get the
        // annual-booster interval, which is the domain's documented behaviour).
        var code = string.IsNullOrWhiteSpace(input.VaccineCode) ? name : input.VaccineCode!.Trim();
        var administeredUtc = new DateTimeOffset(
            DateTime.SpecifyKind(input.AdministeredDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            TimeSpan.Zero);
        var doseNumber = input.DoseNumber < 1 ? 1 : input.DoseNumber;

        return await SendAsync(new RegisterVaccinationCommand(input.PetId, code, name, doseNumber,
            administeredUtc, input.TotalDoses, input.Lot, null, input.Notes),
            ids.Deterministic($"vaccination-register:{input.PetId}:{code}:{doseNumber}:{administeredUtc:yyyyMMdd}"));
    }

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

    public async Task<IReadOnlyList<OwnerSmsReminder>> GetSmsHistoryForOwnerAsync(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return [];
        var rows = await QueryAsync(new OwnerSmsRemindersQuery(ownerId));
        return rows.Select(r => new OwnerSmsReminder(r.Id, r.SentUtc.LocalDateTime, r.PetId, r.PetName,
            r.VaccineName, "SMS", r.Body)).ToArray();
    }

    public async Task<VaccinationHistoryPage> GetHistoryAsync(VaccinationHistoryFilter filter)
    {
        var page = await QueryAsync(new VaccinationHistoryQuery(filter));
        return new VaccinationHistoryPage(page.Items.Select(r => r.ToContract()).ToArray(), page.TotalCount);
    }

    public async Task<string> CreateTreatmentPlanAsync(TreatmentPlanInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return await SendAsync(new CreateTreatmentPlanCommand(input.PetId, input.ItemType.ToDomain(), input.Kind.ToDomain(),
            input.Title, input.Schedule.ToDomain(), input.VaccineCode, input.Dosage, input.Notes, input.NotifyOwner,
            input.RescheduledDayCount));
    }

    public async Task UpdateTreatmentPlanAsync(TreatmentPlanUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        await SendAsync(new UpdateTreatmentPlanCommand(update.PlanId, update.Kind.ToDomain(), update.Title,
            update.Dosage, update.Notes, update.NotifyOwner));
    }

    public async Task CancelTreatmentPlanAsync(string planId) =>
        await SendAsync(new CancelTreatmentPlanCommand(planId));

    public async Task<IReadOnlyList<CalendarOccurrence>> GetCalendarAsync(DateOnly from, DateOnly to, CalendarFilter? filter = null)
    {
        var kinds = filter?.Kinds is { Count: > 0 } k ? k.Select(x => x.ToDomain()).Distinct().ToArray() : null;
        var rows = await QueryAsync(new TreatmentCalendarQuery(from, to, kinds, filter?.ItemType?.ToDomain(), filter?.PetId));
        return rows.Select(r => r.ToContract()).ToArray();
    }

    public async Task MarkOccurrenceDoneAsync(string occurrenceId) =>
        await SendAsync(new MarkTreatmentOccurrenceDoneCommand(occurrenceId),
            ids.Deterministic("treatment-done:" + occurrenceId));

    public async Task<IReadOnlyList<TreatmentPlanSummary>> GetPlansForPetAsync(string petId)
    {
        if (string.IsNullOrWhiteSpace(petId)) return [];
        return (await QueryAsync(new TreatmentPlansForPetQuery(petId))).Select(r => r.ToContract()).ToArray();
    }

    public async Task<string> AddCatalogVaccineAsync(VaccineCatalogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return await SendAsync(new AddVaccineCatalogItemCommand(input.Code, input.Name, input.Protects,
            input.TotalDoses, input.BoosterDays, input.Stock, input.MinStock));
    }

    private async Task<ContractRecord> FindAsync(string id)
    {
        var row = (await QueryAsync(new VaccinationDueQuery(await ClinicDayWindow.TodayDateAsync(scopes).ConfigureAwait(false))))
            .FirstOrDefault(r => r.Id == id);
        return row?.ToContract() ?? throw new InvalidOperationException("VACCINATION_NOT_FOUND");
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
}
