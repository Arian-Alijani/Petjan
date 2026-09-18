using Animora.Contracts.Appointments;

namespace Animora.Desktop.Mocks;

/// <summary>
/// In-memory implementation of <see cref="IAppointmentsQueryService"/>. All
/// data lives in the shared <see cref="AppointmentStore"/> so the Phase 13
/// calendar and the Phase 14 booking panel observe the same event set — the
/// single swap point a future backend replaces (R-18).
/// </summary>
public sealed class MockAppointmentsQueryService : IAppointmentsQueryService
{
    private readonly AppointmentStore _store;

    public MockAppointmentsQueryService(AppointmentStore store) => _store = store;

    public Task<IReadOnlyList<AppointmentDoctor>> GetDoctorsAsync() =>
        Task.FromResult(AppointmentStore.Doctors);

    public Task<IReadOnlyList<AppointmentType>> GetTypesAsync() =>
        Task.FromResult(AppointmentStore.Types);

    public Task<IReadOnlyList<AppointmentEvent>> GetForRangeAsync(DateOnly start, DateOnly end) =>
        Task.FromResult(_store.Range(start, end));

    public Task<IReadOnlyList<AppointmentEvent>> GetQueueAsync() =>
        Task.FromResult(_store.Range(_store.Today, _store.Today.AddDays(20)));

    public Task<IReadOnlyList<AppointmentEvent>> GetHistoryAsync() =>
        Task.FromResult(_store.Range(_store.Today.AddDays(-60), _store.Today));
}
