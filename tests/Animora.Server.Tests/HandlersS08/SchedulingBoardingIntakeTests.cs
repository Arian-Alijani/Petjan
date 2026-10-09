using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Boarding;
using Animora.Contracts.Wire.Commands;
using Xunit;
using Animora.Domain.Results;
namespace Animora.Server.Tests.HandlersS08;

public sealed class SchedulingBoardingIntakeTests
{
    [Fact] public Task Settles_AddStayItemCommand() => SettlesAsync(typeof(AddStayItemCommand));
    [Fact] public Task Settles_RemoveStayItemCommand() => SettlesAsync(typeof(RemoveStayItemCommand));
    [Fact] public Task Settles_CreateRoomCommand() => SettlesAsync(typeof(CreateRoomCommand));
    [Fact] public Task Settles_UpdateRoomCommand() => SettlesAsync(typeof(UpdateRoomCommand));
    [Fact] public Task Settles_CheckoutStayCommand() => SettlesAsync(typeof(CheckoutStayCommand));
    [Fact] public Task Settles_CreateBoardingStayCommand() => SettlesAsync(typeof(CreateBoardingStayCommand));
    [Fact] public Task Settles_RescheduleStayCheckoutCommand() => SettlesAsync(typeof(RescheduleStayCheckoutCommand));
    [Fact] public Task Settles_TransferStayRoomCommand() => SettlesAsync(typeof(TransferStayRoomCommand));
    [Fact] public Task Settles_BookAppointmentCommand() => SettlesAsync(typeof(BookAppointmentCommand));
    [Fact] public Task Settles_CancelAppointmentCommand() => SettlesAsync(typeof(CancelAppointmentCommand));
    [Fact] public Task Settles_CheckInAppointmentCommand() => SettlesAsync(typeof(CheckInAppointmentCommand));
    [Fact] public Task Settles_RescheduleAppointmentCommand() => SettlesAsync(typeof(RescheduleAppointmentCommand));
    [Fact] public Task Settles_CreateBookingPatientCommand() => SettlesAsync(typeof(CreateBookingPatientCommand));

    private static async Task SettlesAsync(Type type)
    {
        using var f = new SchedulingBoardingHandlerFixture();
        var service = f.Clinical.Intake.CreateService(f.Catalog, f.Clinical.Dispatcher);
        var envelope = f.Clinical.Envelope(f.Command(type, false));
        var first = await service.SubmitAsync(envelope);
        Assert.Equal(SettlementOutcome.Accepted, first.Value!.Outcome);
        Assert.Equal(first.Value, (await service.SubmitAsync(envelope)).Value);
        Assert.Equal(1, f.Clinical.Dispatcher.Calls);
        Assert.Equal(FinancialEffect.None, first.Value.FinancialEffect);
        using var rejected = new SchedulingBoardingHandlerFixture();
        var fail = await rejected.Clinical.Intake.CreateService(rejected.Catalog, rejected.Clinical.Dispatcher)
            .SubmitAsync(rejected.Clinical.Envelope(rejected.Command(type, true)));
        Assert.Equal(SettlementOutcome.Rejected, fail.Value!.Outcome);
        var expected = type.Name switch
        {
            nameof(BookAppointmentCommand) => ErrorCodes.DoctorOutsideWorkingHours,
            nameof(RescheduleAppointmentCommand) or nameof(CancelAppointmentCommand) or nameof(CheckInAppointmentCommand) => ErrorCodes.AppointmentNotFound,
            nameof(CreateBookingPatientCommand) or nameof(CreateRoomCommand) => ErrorCodes.ValidationFailed,
            nameof(CreateBoardingStayCommand) or nameof(RescheduleStayCheckoutCommand) => ErrorCodes.StayDatesInvalid,
            nameof(AddStayItemCommand) or nameof(CheckoutStayCommand) => ErrorCodes.StayNotFound,
            nameof(RemoveStayItemCommand) => ErrorCodes.StayItemIndexStale,
            nameof(TransferStayRoomCommand) => ErrorCodes.RoomUnavailable,
            nameof(UpdateRoomCommand) => ErrorCodes.RoomNotFound,
            _ => throw new InvalidOperationException(type.Name)
        };
        Assert.Equal(expected.Code, fail.Value.ErrorCode);
        Assert.NotEqual("HANDLER_NOT_REGISTERED", fail.Value.ErrorCode);
    }
}
