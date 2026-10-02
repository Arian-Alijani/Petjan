using Animora.Application.Ports;
using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Scheduling.ReadModels;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Xunit;
using static Animora.Application.Tests.ReceptionTestDoubles;

namespace Animora.Application.Tests;

internal sealed class TestZone : IClinicTimeZoneProvider
{
    public TimeZoneInfo GetClinicTimeZone() => TimeZoneInfo.Utc;
}

internal sealed class BookingQueriesFake : IAppointmentQueries
{
    public readonly List<ExistingSlot> Busy = [];
    public (int WorkStartMinute, int WorkEndMinute, int WorkDaysMask)? Hours = (480, 1200, 127);
    public Task<IReadOnlyList<ExistingSlot>> GetDoctorSlotsAsync(string doctorId, long startTicks, long endTicks, string? excludeId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExistingSlot>>(Busy.Where(s => s.AppointmentId != excludeId &&
            s.DoctorUserId == doctorId && s.Slot.StartUtc.UtcTicks < endTicks &&
            s.Slot.EndUtc.UtcTicks > startTicks).ToArray());
    public Task<(int WorkStartMinute, int WorkEndMinute, int WorkDaysMask)?> GetDoctorWorkingHoursAsync(string doctorId, CancellationToken ct) =>
        Task.FromResult(Hours);
    public Task<IReadOnlyList<AppointmentEventReadModel>> GetRangeAsync(long startTicks, long endTicks, string? doctorId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AppointmentEventReadModel>>([]);
    public Task<IReadOnlyDictionary<long, int>> GetDayCountsRawAsync(long startTicks, long endTicks, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<long, int>>(new Dictionary<long, int>());
    public Task<int> GetDoctorBusyCountAsync(string doctorId, long startTicks, long endTicks, CancellationToken ct) => Task.FromResult(0);
    public Task<AppointmentDetailReadModel?> GetDetailAsync(string appointmentId, CancellationToken ct) => Task.FromResult<AppointmentDetailReadModel?>(null);
    public Task<IReadOnlyList<AppointmentEventReadModel>> GetQueueAsync(long startTicks, long endTicks, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AppointmentEventReadModel>>([]);
    public Task<AppointmentHistoryPageReadModel> GetHistoryPageAsync(AppointmentHistoryCriteria criteria, CancellationToken ct) =>
        Task.FromResult(new AppointmentHistoryPageReadModel([], new(0, 0, 0, 0, 0, 0), false));
    public Task<IReadOnlyList<DoctorRosterReadModel>> GetDoctorsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DoctorRosterReadModel>>([]);
    public Task<IReadOnlyList<BookingPatientReadModel>> GetPatientsAsync(string search, string speciesKey, int limit, string? patientId, string? appointmentId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BookingPatientReadModel>>([]);
}

public sealed class AppointmentBookingUseCaseTests
{
    private static readonly DateOnly Day = new(2026, 3, 2);

    [Fact]
    public async Task SameDoctorConflict_CarriesBlockingAppointmentId_ButTouchingSlotSucceeds()
    {
        var fake = new BookingQueriesFake();
        fake.Busy.Add(new ExistingSlot("blocking", "doctor", new AppointmentSlot(
            new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero), 30), AppointmentState.Scheduled));
        var handler = new BookAppointmentCommandHandler(new FakeAppointmentRepository(), fake, new TestZone(), Clock);
        var collision = await handler.HandleAsync(new BookAppointmentCommand(Day, 9 * 60 + 15,
            30, "CHECKUP", "doctor", "pet", "owner", null), Context, default);
        Assert.Equal(ErrorCodes.AppointmentSlotConflict, collision.Error);
        Assert.Equal("blocking", collision.Detail);
        var adjacent = await handler.HandleAsync(new BookAppointmentCommand(Day, 9 * 60 + 30,
            30, "CHECKUP", "doctor", "pet", "owner", null), Context, default);
        Assert.True(adjacent.IsSuccess);
    }

    [Fact]
    public async Task OutsideWorkingHours_IsRejected()
    {
        var handler = new BookAppointmentCommandHandler(new FakeAppointmentRepository(),
            new BookingQueriesFake(), new TestZone(), Clock);
        var result = await handler.HandleAsync(new BookAppointmentCommand(Day, 7 * 60,
            30, "CHECKUP", "doctor", "pet", "owner", null), Context, default);
        Assert.Equal(ErrorCodes.DoctorOutsideWorkingHours, result.Error);
    }

    [Fact]
    public async Task CheckedInCannotCancel_AndCheckInCreatesExactlyOneQueueRow()
    {
        var appointments = new FakeAppointmentRepository();
        var queue = new FakeQueueRepository();
        var appointment = Appointment.Book(TenantId, "pet", "owner", "CHECKUP",
            new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero), 30,
            AppointmentSource.Phone, "U-1", Clock, "doctor").Value!;
        appointments.Add(appointment);
        var checkin = new CheckInAppointmentCommandHandler(appointments, queue, Clock);
        Assert.True((await checkin.HandleAsync(new CheckInAppointmentCommand(appointment.Id), Context, default)).IsSuccess);
        Assert.True((await checkin.HandleAsync(new CheckInAppointmentCommand(appointment.Id), Context, default)).IsSuccess);
        Assert.Single(queue.Items);
        var cancel = await new CancelAppointmentCommandHandler(appointments, Clock)
            .HandleAsync(new CancelAppointmentCommand(appointment.Id, null), Context, default);
        Assert.Equal(ErrorCodes.AppointmentStateInvalid, cancel.Error);
    }
}
