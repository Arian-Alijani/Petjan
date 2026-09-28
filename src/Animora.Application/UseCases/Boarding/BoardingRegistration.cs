using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Boarding.ReadModels;
using Animora.Domain.Boarding;

namespace Animora.Application.UseCases.Boarding;

public static class BoardingRegistration
{
    public static ApplicationRegistrationBuilder AddBoardingUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<BoardingStayListQuery, IReadOnlyList<StayListItemReadModel>, BoardingStayListQueryHandler>()
        .AddQuery<BoardingStayDetailQuery, StayDetailReadModel?, BoardingStayDetailQueryHandler>()
        .AddQuery<BoardingRoomsQuery, IReadOnlyList<RoomOccupancyReadModel>, BoardingRoomsQueryHandler>()
        .AddQuery<BoardingServicesQuery, IReadOnlyList<BoardingServiceDef>, BoardingServicesQueryHandler>()
        .AddQuery<BoardingSelectableItemsQuery, IReadOnlyList<BoardingSelectableReadModel>, BoardingSelectableItemsQueryHandler>()
        .AddQuery<BoardingKnownPetsQuery, IReadOnlyList<BoardingKnownPetReadModel>, BoardingKnownPetsQueryHandler>()
        .AddQuery<BoardingHistoryQuery, BoardingHistoryResult, BoardingHistoryQueryHandler>()
        .AddCommand<CreateBoardingStayCommand, string, CreateBoardingStayCommandHandler>()
        .AddValidator<CreateBoardingStayCommand, CreateBoardingStayCommandValidator>()
        .AddCommand<AddStayItemCommand, Unit, AddStayItemCommandHandler>()
        .AddCommand<RemoveStayItemCommand, Unit, RemoveStayItemCommandHandler>()
        .AddCommand<TransferStayRoomCommand, Unit, TransferStayRoomCommandHandler>()
        .AddCommand<CheckoutStayCommand, Unit, CheckoutStayCommandHandler>();
}
