using Animora.Application.UseCases.Scheduling.ReadModels;
using Animora.Domain.Scheduling;

namespace Animora.Application.Ports;

public interface IAppointmentQueries
{
    Task<IReadOnlyList<AppointmentEventReadModel>> GetRangeAsync(long startTicks, long endTicks, string? doctorId, CancellationToken ct);
    // Bucketing raw UTC instants in application is necessary: SQLite cannot group by the clinic time zone.
    Task<IReadOnlyDictionary<long, int>> GetDayCountsRawAsync(long startTicks, long endTicks, CancellationToken ct);
    Task<int> GetDoctorBusyCountAsync(string doctorId, long startTicks, long endTicks, CancellationToken ct);
    Task<AppointmentDetailReadModel?> GetDetailAsync(string appointmentId, CancellationToken ct);
    Task<IReadOnlyList<AppointmentEventReadModel>> GetQueueAsync(long startTicks, long endTicks, CancellationToken ct);
    /// <summary>Terminal appointments matching <paramref name="criteria"/>, newest first, one page + whole-scope aggregates.</summary>
    Task<AppointmentHistoryPageReadModel> GetHistoryPageAsync(AppointmentHistoryCriteria criteria, CancellationToken ct);
    Task<IReadOnlyList<ExistingSlot>> GetDoctorSlotsAsync(string doctorId, long startTicks, long endTicks, string? excludeId, CancellationToken ct);
    Task<(int WorkStartMinute, int WorkEndMinute, int WorkDaysMask)?> GetDoctorWorkingHoursAsync(string doctorId, CancellationToken ct);
    Task<IReadOnlyList<DoctorRosterReadModel>> GetDoctorsAsync(CancellationToken ct);
    Task<IReadOnlyList<BookingPatientReadModel>> GetPatientsAsync(
        string search, string speciesKey, int limit, string? patientId, string? appointmentId, CancellationToken ct);
}
