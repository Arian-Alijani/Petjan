using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Scheduling.ReadModels;

namespace Animora.Application.UseCases.Scheduling;

public static class SchedulingRegistration
{
    public static ApplicationRegistrationBuilder AddSchedulingUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<AppointmentDoctorsQuery, IReadOnlyList<DoctorRosterReadModel>, AppointmentDoctorsQueryHandler>()
        .AddQuery<BookingPatientsQuery, IReadOnlyList<BookingPatientReadModel>, BookingPatientsQueryHandler>()
        .AddQuery<AppointmentRangeQuery, IReadOnlyList<AppointmentEventReadModel>, AppointmentRangeQueryHandler>()
        .AddQuery<AppointmentDayCountsQuery, IReadOnlyDictionary<DateOnly, int>, AppointmentDayCountsQueryHandler>()
        .AddQuery<DoctorBusyCountQuery, int, DoctorBusyCountQueryHandler>()
        .AddQuery<AppointmentDetailQuery, AppointmentDetailReadModel?, AppointmentDetailQueryHandler>()
        .AddQuery<AppointmentQueueQuery, IReadOnlyList<AppointmentEventReadModel>, AppointmentQueueQueryHandler>()
        .AddQuery<AppointmentHistoryQuery, IReadOnlyList<AppointmentEventReadModel>, AppointmentHistoryQueryHandler>()
        .AddQuery<AvailableSlotsQuery, IReadOnlyList<TimeSlotReadModel>, AvailableSlotsQueryHandler>()
        .AddCommand<BookAppointmentCommand, string, BookAppointmentCommandHandler>()
        .AddValidator<BookAppointmentCommand, BookAppointmentCommandValidator>()
        .AddCommand<RescheduleAppointmentCommand, Unit, RescheduleAppointmentCommandHandler>()
        .AddCommand<CancelAppointmentCommand, Unit, CancelAppointmentCommandHandler>()
        .AddCommand<CheckInAppointmentCommand, Unit, CheckInAppointmentCommandHandler>()
        .AddCommand<CreateBookingPatientCommand, string, CreateBookingPatientCommandHandler>()
        .AddValidator<CreateBookingPatientCommand, CreateBookingPatientCommandValidator>()
        .AddQuery<GetAppointmentSettingsQuery, Contracts.Appointments.AppointmentSettings, GetAppointmentSettingsQueryHandler>()
        .AddCommand<SaveAppointmentSettingsCommand, Unit, SaveAppointmentSettingsCommandHandler>()
        .AddValidator<SaveAppointmentSettingsCommand, SaveAppointmentSettingsCommandValidator>()
        .AddCommand<AddAppointmentTypeCommand, Contracts.Appointments.CustomAppointmentType, AddAppointmentTypeCommandHandler>()
        .AddValidator<AddAppointmentTypeCommand, AddAppointmentTypeCommandValidator>()
        .AddCommand<RemoveAppointmentTypeCommand, Unit, RemoveAppointmentTypeCommandHandler>()
        .AddValidator<RemoveAppointmentTypeCommand, RemoveAppointmentTypeCommandValidator>()
        .AddCommand<SetCalendarModeCommand, Unit, SetCalendarModeCommandHandler>()
        .AddCommand<SetAppointmentSmsCommand, Unit, SetAppointmentSmsCommandHandler>()
        .AddValidator<SetAppointmentSmsCommand, SetAppointmentSmsCommandValidator>();
}
