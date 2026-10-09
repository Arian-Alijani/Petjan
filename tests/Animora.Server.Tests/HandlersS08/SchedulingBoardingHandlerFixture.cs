using System.Reflection;
using Animora.Application;
using Animora.Application.Ports;
using Animora.Application.UseCases.Boarding;
using Animora.Application.UseCases.Boarding.ReadModels;
using Animora.Application.UseCases.Scheduling;
using Animora.Domain.Boarding;
using Animora.Domain.Primitives;
using Animora.Domain.ValueObjects;
using Animora.Server.Commands;
using Animora.Server.Handlers;
using Animora.Server.Tests.HandlersS07;
using Microsoft.Extensions.DependencyInjection;
namespace Animora.Server.Tests.HandlersS08;

internal sealed class SchedulingBoardingHandlerFixture : IDisposable
{
    public ClinicalHandlerFixture Clinical { get; }
    public CommandPayloadCatalog Catalog { get; }
    public Room Source { get; }
    public Room Target { get; }
    public BoardingStay Stay { get; }
    public BoardingQueriesFake BoardingQueries { get; }
    public BookingQueriesFake BookingQueries { get; } = new();
    public SchedulingBoardingHandlerFixture()
    {
        // Seed first; registration factory closes over the ports, not a second fake intake container.
        Source = Room.Create("fixture", "SRC", "Source", RoomType.Standard, 2).Value!;
        Target = Room.Create("fixture", "DST", "Target", RoomType.Standard, 2).Value!;
        var clock = new Animora.Server.Tests.Commands.FixedClock();
        Stay = BoardingStay.Reserve("fixture", "pet", "owner", Source.Id, clock.UtcNow.AddDays(-2), clock.UtcNow.AddDays(1), Money.Irt(100)).Value!;
        Stay.Activate(clock);
        Stay.AddItem(new StayItem("item", StayItemKind.Custom, null, "Item", new Quantity(1, "PCS"), Money.Irt(10), clock.UtcNow, "user"));
        BoardingQueries = new BoardingQueriesFake(Source, Target) { ActiveStay = Stay,
            Selectable = new BoardingSelectableReadModel("service", "Service", 10, "BoardingService") };
        var repository = new BoardingRepositoryFake(Stay);
        Clinical = new ClinicalHandlerFixture(services =>
        {
            services.AddSingleton<IBoardingRepository>(repository);
            services.AddSingleton<IBoardingQueries>(BoardingQueries);
            services.AddSingleton<IBoardingBillingHandoffWriter>(new BoardingHandoffFake());
            services.AddSingleton<IRoomRepository>(new RoomFixtureRepository(Source, Target));
            services.AddSingleton<IAppointmentQueries>(BookingQueries);
            services.AddAnimoraApplication(ServerHandlerModuleS08.AddHandlers, ApplicationPipelineProfile.Server, registerQueries: false);
        });
        BoardingQueries.KnownPet = new BoardingKnownPetReadModel(Clinical.Pet.Id, Clinical.Pet.Name, "Dog", "", "Owner", "09120000000", Clinical.Owner.Id);
        var builder = new CommandPayloadCatalogBuilder(); ServerHandlerModuleS08.AddPayloads(builder); Catalog = builder.Build();
    }
    public object Command(Type type, bool reject)
    {
        return type.Name switch
        {
            nameof(BookAppointmentCommand) => new BookAppointmentCommand(new DateOnly(2026, 10, 9), reject ? 60 : 600, 30, "CHECKUP", Clinical.DoctorId, Clinical.Pet.Id, Clinical.Owner.Id, null),
            nameof(RescheduleAppointmentCommand) => new RescheduleAppointmentCommand(reject ? IdGenerator.NewId() : Clinical.Appointment.Id, new DateOnly(2026, 10, 9), 600, 30),
            nameof(CancelAppointmentCommand) => new CancelAppointmentCommand(reject ? IdGenerator.NewId() : Clinical.Appointment.Id, "Cancelled"),
            nameof(CheckInAppointmentCommand) => new CheckInAppointmentCommand(reject ? IdGenerator.NewId() : Clinical.Appointment.Id),
            nameof(CreateBookingPatientCommand) => new CreateBookingPatientCommand(reject ? "" : "Pet", "Dog", "Owner", "09120000001"),
            nameof(CreateBoardingStayCommand) => new CreateBoardingStayCommand(Clinical.Pet.Id, Target.Id,
                new DateOnly(2026, 10, 9), new DateOnly(2026, 10, reject ? 8 : 11), 100, null),
            nameof(AddStayItemCommand) => new AddStayItemCommand(reject ? IdGenerator.NewId() : Stay.Id, "service", 1),
            nameof(RemoveStayItemCommand) => new RemoveStayItemCommand(Stay.Id, 0, reject ? "stale" : "item"),
            nameof(TransferStayRoomCommand) => new TransferStayRoomCommand(Stay.Id, reject ? Source.Id : Target.Id),
            nameof(RescheduleStayCheckoutCommand) => new RescheduleStayCheckoutCommand(Stay.Id, new DateOnly(2026, 10, reject ? 1 : 12)),
            nameof(CheckoutStayCommand) => new CheckoutStayCommand(reject ? IdGenerator.NewId() : Stay.Id),
            nameof(CreateRoomCommand) => new CreateRoomCommand(reject ? "" : "NEW", "New room", RoomType.Standard, 1, null),
            nameof(UpdateRoomCommand) => new UpdateRoomCommand(reject ? IdGenerator.NewId() : Source.Id, "Renamed", 2, RoomStatus.Available, null),
            _ => throw new InvalidOperationException(type.Name)
        };
    }
    public void Dispose() => Clinical.Dispose();
}
internal sealed class RoomFixtureRepository(params Room[] initial) : IRoomRepository
{
    private readonly List<Room> _rooms = [.. initial];
    public Task<Room?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(_rooms.FirstOrDefault(r => r.Id == id));
    public Task<bool> CodeExistsAsync(string code, string? except, CancellationToken ct = default) => Task.FromResult(_rooms.Any(r => r.Code == code && r.Id != except));
    public void Add(Room room) => _rooms.Add(room);
}
