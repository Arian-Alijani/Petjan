using Animora.Application.Dispatch;
using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Boarding;
using Animora.Contracts.Wire.Commands;
using Animora.Server.Commands;
namespace Animora.Server.Handlers;

internal static class ServerHandlerModuleS08
{
    public static void AddHandlers(ApplicationRegistrationBuilder builder) => builder.AddSchedulingUseCases().AddBoardingUseCases();
    public static void AddPayloads(CommandPayloadCatalogBuilder catalog)
    {
        catalog.Add<AddStayItemCommand>(CommandTypes.BoardingItemAdd, "BoardingStay");
        catalog.Add<RemoveStayItemCommand>(CommandTypes.BoardingItemRemove, "BoardingStay");
        catalog.Add<CreateRoomCommand>(CommandTypes.BoardingRoomCreate, "Room");
        catalog.Add<UpdateRoomCommand>(CommandTypes.BoardingRoomUpdate, "Room");
        catalog.Add<CheckoutStayCommand>(CommandTypes.BoardingStayCheckout, "BoardingStay");
        catalog.Add<CreateBoardingStayCommand>(CommandTypes.BoardingStayCreate, "BoardingStay");
        catalog.Add<RescheduleStayCheckoutCommand>(CommandTypes.BoardingStayReschedule, "BoardingStay");
        catalog.Add<TransferStayRoomCommand>(CommandTypes.BoardingStayTransfer, "BoardingStay");
        catalog.Add<BookAppointmentCommand>(CommandTypes.SchedulingAppointmentBook, "Appointment");
        catalog.Add<CancelAppointmentCommand>(CommandTypes.SchedulingAppointmentCancel, "Appointment");
        catalog.Add<CheckInAppointmentCommand>(CommandTypes.SchedulingAppointmentCheckin, "Appointment");
        catalog.Add<RescheduleAppointmentCommand>(CommandTypes.SchedulingAppointmentMove, "Appointment");
        catalog.Add<CreateBookingPatientCommand>(CommandTypes.SchedulingBookingPatientCreate, "Pet");
    }
}
