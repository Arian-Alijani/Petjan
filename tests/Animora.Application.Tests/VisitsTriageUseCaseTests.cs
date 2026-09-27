using System.Reflection;
using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Security;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Reception.ReadModels;
using Animora.Application.UseCases.Visits;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Queries;
using Animora.Persistence.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Animora.Application.Tests.ReceptionTestDoubles;

namespace Animora.Application.Tests;

/// <summary>
/// C02 triage rules: starting a visit from a ticket is idempotent (one <c>Visit</c>, one deterministic
/// <c>OperationId</c> per ticket), it links appointment + queue row + visit, and the doctor rail's booked
/// counts come from one projection over the day's appointments.
/// </summary>
public sealed class VisitsTriageUseCaseTests
{
    private const string DoctorId = "U-DOCTOR";

    private readonly FakeQueueRepository _queue = new();
    private readonly FakeAppointmentRepository _appointments = new();
    private readonly FakeOwners _owners = new();
    private readonly FakePets _pets = new();
    private readonly FakeVisits _visits = new();
    private readonly FakeMemberships _memberships = new();
    private readonly FakeReceptionQueries _queries = new(new ReceptionDoctorReadModel(DoctorId, "دکتر رضایی", string.Empty));

    public VisitsTriageUseCaseTests() =>
        _memberships.Add(StaffMembership.Create(TenantId, DoctorId, StaffRole.Doctor, Clock));

    private StartVisitFromTicketCommandHandler FromTicketHandler() =>
        new(_queue, _appointments, _visits, _pets, _owners, _memberships, _queries, Clock);

    private Appointment BookToday(string? doctorId = DoctorId)
    {
        var owner = Owner.Register(TenantId, "نگار محمدی", "09120000001", Clock);
        var pet = Pet.Register(TenantId, owner.Id, "رکس", PetSpecies.Dog, Clock);
        _owners.Add(owner);
        _pets.Add(pet);
        var appointment = Appointment.Book(TenantId, pet.Id, owner.Id, "CHECKUP", Clock.UtcNow.AddMinutes(15),
            30, AppointmentSource.Phone, "U-1", Clock, doctorId).Value!;
        _appointments.Add(appointment);
        return appointment;
    }

    [Fact]
    public void StartVisitCommands_AreJournaledUnderVisitsVisitStart_AndRequireVisitsWrite()
    {
        Assert.Contains(CommandTypes.VisitsVisitStart, CommandTypes.All);
        foreach (var type in new[] { typeof(StartVisitFromTicketCommand), typeof(StartNewVisitCommand) })
        {
            Assert.Equal(PermissionCodes.VisitsWrite, type.GetCustomAttribute<RequiresPermissionAttribute>()!.PermissionCode);
        }

        Assert.Equal(CommandTypes.VisitsVisitStart, new StartVisitFromTicketCommand("apt:x").CommandTypeName);
    }

    [Fact]
    public void OperationId_ForATicket_IsDeterministic_AndDistinctPerTicket()
    {
        var a = new OperationIdFactory().Deterministic("visit-start:apt:A1");
        var b = new OperationIdFactory().Deterministic("visit-start:apt:A1");
        var other = new OperationIdFactory().Deterministic("visit-start:apt:A2");

        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
        Assert.Equal(26, a.Length);
    }

    [Fact]
    public async Task StartFromTicket_Twice_CreatesOneInProgressVisit_AndReturnsItBothTimes()
    {
        var appointment = BookToday();
        var ticketId = TicketRef.FromAppointment(appointment.Id).Format();
        var handler = FromTicketHandler();

        var first = await handler.HandleAsync(new StartVisitFromTicketCommand(ticketId), Context, default);
        var second = await handler.HandleAsync(new StartVisitFromTicketCommand(ticketId), Context, default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var visit = Assert.Single(_visits.Items.Values);
        Assert.Equal(VisitState.InProgress, visit.State);
        Assert.Equal(first.Value!.VisitId, second.Value!.VisitId);
        Assert.Equal(visit.Id, first.Value.VisitId);
        Assert.Equal(ticketId, first.Value.TicketId);
        Assert.Equal("دکتر رضایی", first.Value.DoctorName);

        var row = Assert.Single(_queue.Items.Values);
        Assert.Equal((QueueState.InVisit, visit.Id), (row.State, row.VisitId));
        Assert.Equal((AppointmentState.InVisit, visit.Id), (appointment.State, appointment.VisitId));
        Assert.Equal(appointment.Id, visit.AppointmentId);
    }

    [Fact]
    public async Task StartFromTicket_WithoutAnActiveDoctor_IsDoctorNotAvailable()
    {
        var ticketId = TicketRef.FromAppointment(BookToday(doctorId: null).Id).Format();

        var result = await FromTicketHandler().HandleAsync(new StartVisitFromTicketCommand(ticketId), Context, default);

        Assert.Equal(ErrorCodes.DoctorNotAvailable, result.Error);
        Assert.Empty(_visits.Items);
    }

    [Fact]
    public async Task StartFromTicket_OnANoShowAppointment_IsTicketStateInvalid()
    {
        var appointment = BookToday();
        appointment.MarkNoShow(Clock);

        var result = await FromTicketHandler().HandleAsync(
            new StartVisitFromTicketCommand(TicketRef.FromAppointment(appointment.Id).Format()), Context, default);

        Assert.Equal(ErrorCodes.TicketStateInvalid, result.Error);
        Assert.Empty(_visits.Items);
    }

    [Fact]
    public async Task StartNew_CreatesWalkInRowInVisit_AndReusesTheOwnerByPhone()
    {
        var owner = Owner.Register(TenantId, "نگار محمدی", "09120000001", Clock);
        _owners.Add(owner);
        var handler = new StartNewVisitCommandHandler(_queue, _visits, _pets, _owners, _memberships, _queries, Clock);

        var result = await handler.HandleAsync(new StartNewVisitCommand(DoctorId, "میلو", "Cat", "نگار",
            "0912 000 0001", "VACCINE", IsUrgent: false, "سرفه"), Context, default);

        Assert.True(result.IsSuccess);
        Assert.Single(_owners.Items);
        var row = Assert.Single(_queue.Items.Values);
        var visit = Assert.Single(_visits.Items.Values);
        Assert.Equal((QueueSource.WalkIn, QueueState.InVisit, visit.Id), (row.Source, row.State, row.VisitId));
        Assert.Equal(TicketRef.FromWalkIn(row.Id).Format(), result.Value!.TicketId);
        Assert.Equal("VACCINE", result.Value.TypeId);
        Assert.True(result.Value.IsSynthetic);
    }

    [Fact]
    public async Task DoctorRail_BookedCounts_MatchTheDaysAppointments()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var busy = User.Create(fixture.TenantId, "doctor-busy", "دکتر الف", fixture.Clock);
        var idle = User.Create(fixture.TenantId, "doctor-idle", "دکتر ب", fixture.Clock);
        db.AddRange(busy, idle,
            StaffMembership.Create(fixture.TenantId, busy.Id, StaffRole.Doctor, fixture.Clock),
            StaffMembership.Create(fixture.TenantId, idle.Id, StaffRole.Doctor, fixture.Clock));
        var owner = Owner.Register(fixture.TenantId, "مالک", "09120000002", fixture.Clock);
        var pet = Pet.Register(fixture.TenantId, owner.Id, "پت", PetSpecies.Dog, fixture.Clock);
        db.AddRange(owner, pet);
        var now = fixture.Clock.UtcNow;
        Appointment Book(DateTimeOffset at, string doctor) => Appointment.Book(fixture.TenantId, pet.Id, owner.Id,
            "CHECKUP", at, 30, AppointmentSource.Phone, doctor, fixture.Clock, doctor).Value!;
        var cancelled = Book(now.AddHours(3), busy.Id);
        cancelled.Cancel("owner called", fixture.Clock);
        db.AddRange(Book(now, busy.Id), Book(now.AddHours(1), busy.Id), cancelled,
            Book(now.AddDays(1), busy.Id), Book(now.AddHours(2), idle.Id));
        await db.SaveChangesAsync();

        var result = await new TriageDoctorRailQueryHandler(new TriageQueries(db)).HandleAsync(
            new TriageDoctorRailQuery(DateOnly.FromDateTime(now.UtcDateTime)),
            new QueryContext(fixture.TenantId, "U-1", "COR-1"), default);

        Assert.True(result.IsSuccess);
        var counts = result.Value!.ToDictionary(d => d.Id, d => d.BookedCount);
        Assert.Equal(2, counts[busy.Id]); // tomorrow's and the cancelled one are excluded
        Assert.Equal(1, counts[idle.Id]);
    }
}
