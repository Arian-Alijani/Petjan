using System.Reflection;
using Animora.Application.Security;
using Animora.Application.UseCases.Reception;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Xunit;
using static Animora.Application.Tests.ReceptionTestDoubles;

namespace Animora.Application.Tests;

/// <summary>
/// C02 reception command rules: the queue is DERIVED — an appointment ticket owns no queue row until its
/// first state change, which lazily creates exactly one; a repeated admit is TICKET_STATE_INVALID; a
/// walk-in whose phone matches an existing owner reuses that owner; no-show lives on the appointment.
/// </summary>
public sealed class ReceptionQueueUseCaseTests
{
    private readonly FakeQueueRepository _queue = new();
    private readonly FakeAppointmentRepository _appointments = new();
    private readonly FakeOwners _owners = new();
    private readonly FakePets _pets = new();
    private readonly FakeMemberships _memberships = new();

    private Appointment BookToday()
    {
        var owner = Owner.Register(TenantId, "نگار محمدی", "09120000001", Clock);
        var pet = Pet.Register(TenantId, owner.Id, "رکس", PetSpecies.Dog, Clock);
        _owners.Add(owner);
        _pets.Add(pet);
        var appointment = Appointment.Book(TenantId, pet.Id, owner.Id, "CHECKUP", Clock.UtcNow.AddMinutes(30),
            30, AppointmentSource.Phone, "U-1", Clock).Value!;
        _appointments.Add(appointment);
        return appointment;
    }

    [Fact]
    public void CommandTypes_AreDeclaredInTheCatalog()
    {
        Assert.Contains(CommandTypes.ReceptionTicketAdmit, CommandTypes.All);
        Assert.Contains(CommandTypes.ReceptionTicketEnter, CommandTypes.All);
        Assert.Contains(CommandTypes.ReceptionTicketLeave, CommandTypes.All);
        Assert.Contains(CommandTypes.ReceptionTicketNoShow, CommandTypes.All);
        Assert.Contains(CommandTypes.ReceptionTicketUrgent, CommandTypes.All);
        Assert.Contains(CommandTypes.ReceptionWalkInCreate, CommandTypes.All);
    }

    [Fact]
    public void TicketRef_RoundTripsBothKinds_AndRejectsGarbage()
    {
        Assert.True(TicketRef.TryParse(TicketRef.FromAppointment("A1").Format(), out var apt));
        Assert.Equal((TicketKind.Appointment, "A1"), (apt.Kind, apt.Id));
        Assert.True(TicketRef.TryParse(TicketRef.FromWalkIn("W1").Format(), out var wlk));
        Assert.Equal((TicketKind.WalkIn, "W1"), (wlk.Kind, wlk.Id));
        Assert.False(TicketRef.TryParse("nope", out _));
        Assert.False(TicketRef.TryParse("apt:", out _));
    }

    [Fact]
    public async Task FirstStateChange_OnAppointmentTicket_LazilyCreatesExactlyOneQueueRow()
    {
        var appointment = BookToday();
        var ticketId = TicketRef.FromAppointment(appointment.Id).Format();
        Assert.Empty(_queue.Items); // derived view: nothing persisted before the first touch

        var admitted = await new AdmitTicketCommandHandler(_queue, _appointments, Clock)
            .HandleAsync(new AdmitTicketCommand(ticketId), Context, default);
        var urgent = await new ToggleTicketUrgentCommandHandler(_queue, _appointments, Clock)
            .HandleAsync(new ToggleTicketUrgentCommand(ticketId), Context, default);

        Assert.True(admitted.IsSuccess);
        Assert.True(urgent.IsSuccess);
        var row = Assert.Single(_queue.Items.Values);
        Assert.Equal(appointment.Id, row.AppointmentId);
        Assert.Equal(QueueSource.Appointment, row.Source);
        Assert.Equal(QueueState.Called, row.State);
        Assert.Equal(ReceptionQueueEntry.PriorityEmergency, row.Priority);
        Assert.Equal(AppointmentState.CheckedIn, appointment.State);
    }

    [Fact]
    public async Task SecondAdmit_OfTheSameTicket_IsTicketStateInvalid()
    {
        var ticketId = TicketRef.FromAppointment(BookToday().Id).Format();
        var handler = new AdmitTicketCommandHandler(_queue, _appointments, Clock);

        Assert.True((await handler.HandleAsync(new AdmitTicketCommand(ticketId), Context, default)).IsSuccess);
        var again = await handler.HandleAsync(new AdmitTicketCommand(ticketId), Context, default);

        Assert.True(again.IsFailure);
        Assert.Equal(ErrorCodes.TicketStateInvalid, again.Error);
        Assert.Single(_queue.Items);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("apt:missing")]
    [InlineData("wlk:missing")]
    public async Task UnknownOrUnparsableTicket_IsTicketNotFound(string ticketId)
    {
        var result = await new LeaveQueueCommandHandler(_queue, _appointments, Clock)
            .HandleAsync(new LeaveQueueCommand(ticketId), Context, default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.TicketNotFound, result.Error);
        Assert.Empty(_queue.Items);
    }

    [Fact]
    public async Task EnterVisit_WithoutAStartedVisit_IsTicketStateInvalid()
    {
        var ticketId = TicketRef.FromAppointment(BookToday().Id).Format();

        var result = await new EnterVisitCommandHandler(_queue, _appointments, Clock)
            .HandleAsync(new EnterVisitCommand(ticketId), Context, default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.TicketStateInvalid, result.Error);
    }

    [Fact]
    public async Task MarkNoShow_OnUntouchedAppointment_MovesTheAppointment_AndCreatesNoQueueRow()
    {
        var appointment = BookToday();

        var result = await new MarkNoShowCommandHandler(_queue, _appointments, Clock)
            .HandleAsync(new MarkNoShowCommand(TicketRef.FromAppointment(appointment.Id).Format()), Context, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentState.NoShow, appointment.State);
        Assert.Empty(_queue.Items);
    }

    [Fact]
    public async Task WalkIn_WhosePhoneMatchesAnExistingOwner_ReusesThatOwnerAndPet()
    {
        var owner = Owner.Register(TenantId, "نگار محمدی", "09120000001", Clock);
        var pet = Pet.Register(TenantId, owner.Id, "رکس", PetSpecies.Dog, Clock);
        _owners.Add(owner);
        _pets.Add(pet);
        var handler = new CreateWalkInCommandHandler(_owners, _pets, _queue, _memberships, Clock);

        var result = await handler.HandleAsync(
            new CreateWalkInCommand("رکس", "نگار", "0912 000 0001", null, IsUrgent: true), Context, default);

        Assert.True(result.IsSuccess);
        Assert.Single(_owners.Items);
        Assert.Single(_pets.Items);
        var row = Assert.Single(_queue.Items.Values);
        Assert.Equal(TicketRef.FromWalkIn(row.Id).Format(), result.Value);
        Assert.Equal((owner.Id, pet.Id), (row.OwnerId, row.PetId));
        Assert.Equal(QueueSource.WalkIn, row.Source);
        Assert.Null(row.AppointmentId);
        Assert.Equal(ReceptionQueueEntry.PriorityEmergency, row.Priority);
    }

    [Fact]
    public async Task WalkIn_WithoutPhone_IsWalkInPhoneRequired_AndValidatorFlagsIt()
    {
        var command = new CreateWalkInCommand("رکس", "نگار", " ", null, false);

        var result = await new CreateWalkInCommandHandler(_owners, _pets, _queue, _memberships, Clock)
            .HandleAsync(command, Context, default);
        var failures = await new CreateWalkInCommandValidator().ValidateAsync(command, default);

        Assert.Equal(ErrorCodes.WalkInPhoneRequired, result.Error);
        Assert.Contains(failures, f => f.Field == nameof(CreateWalkInCommand.Phone) && f.Code == "REQUIRED");
        Assert.Empty(_queue.Items);
    }

    [Fact]
    public async Task WalkIn_AddressedToANonDoctor_IsDoctorNotAvailable()
    {
        _memberships.Add(StaffMembership.Create(TenantId, "U-RECEPTION", StaffRole.Receptionist, Clock));

        var result = await new CreateWalkInCommandHandler(_owners, _pets, _queue, _memberships, Clock)
            .HandleAsync(new CreateWalkInCommand("رکس", "نگار", "09120000009", "U-RECEPTION", false), Context, default);

        Assert.Equal(ErrorCodes.DoctorNotAvailable, result.Error);
        Assert.Empty(_owners.Items);
    }

    [Theory]
    [InlineData(typeof(AdmitTicketCommand))]
    [InlineData(typeof(EnterVisitCommand))]
    [InlineData(typeof(LeaveQueueCommand))]
    [InlineData(typeof(MarkNoShowCommand))]
    [InlineData(typeof(ToggleTicketUrgentCommand))]
    [InlineData(typeof(CreateWalkInCommand))]
    public void ReceptionCommands_RequireTheReceptionManagePermission(Type command)
    {
        var attribute = command.GetCustomAttribute<RequiresPermissionAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(PermissionCodes.ReceptionManage, attribute!.PermissionCode);
    }
}
