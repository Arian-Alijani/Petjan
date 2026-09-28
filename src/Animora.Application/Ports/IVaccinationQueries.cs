using Animora.Domain.Clinical;

namespace Animora.Application.Ports;

public sealed record VaccinationDueRow(string Id, string PetId, string PetName, string Species,
    string OwnerName, string OwnerPhone, string VaccineCode, string VaccineName, int DoseNumber,
    DateTimeOffset AdministeredUtc, DateTimeOffset? NextDueUtc, string DoctorId, string? BatchNumber,
    VaccinationDueBucket Bucket);

public sealed record VaccinationHistoryRow(string Id, string? RecordId, string PetName, string Species,
    string OwnerName, string Title, string? Detail, DateTimeOffset OccurredUtc, string ActorId);

public sealed record SmsTemplateReadModel(string Id, string Name, string Channel, string Body);

public interface IVaccinationQueries
{
    Task<IReadOnlyList<VaccinationDueRow>> GetDueAsync(DateOnly localDay, CancellationToken ct);
    Task<(IReadOnlyList<VaccinationHistoryRow> Items, int Total)> GetHistoryAsync(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? search, bool oldest,
        IReadOnlyCollection<string>? titles, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<SmsTemplateReadModel>> GetTemplatesAsync(CancellationToken ct);
    Task<VaccinationDueRow?> GetRecordAsync(string id, DateOnly localDay, CancellationToken ct);
    Task<bool> HasNextDoseAsync(string petId, string vaccineCode, int doseNumber, CancellationToken ct);
    Task<bool> IsMarkedDoneAsync(string recordId, CancellationToken ct);
}
