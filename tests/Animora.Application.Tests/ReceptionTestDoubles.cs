using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Reception.ReadModels;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;

namespace Animora.Application.Tests;

/// <summary>
/// In-memory repository doubles shared by the C02 reception + triage use-case tests. They mirror the
/// real repositories' observable contract (find / find-by-key / add) and nothing more, so a handler's
/// row-creation behavior (lazy queue row, owner de-duplication, single visit) is directly countable.
/// </summary>
internal static class ReceptionTestDoubles
{
    public const string TenantId = "T-LOCAL-DEFAULT";

    public static readonly IClock Clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

    public static CommandContext Context => new(
        TenantId, "U-1", "D-1", "OP-1", "COR-1", null, null, null, Clock.UtcNow);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}

internal sealed class FakeQueueRepository : IReceptionQueueRepository
{
    public readonly Dictionary<string, ReceptionQueueEntry> Items = new(StringComparer.Ordinal);

    public Task<ReceptionQueueEntry?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Items.GetValueOrDefault(id));

    public Task<ReceptionQueueEntry> RequireAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Items[id]);

    public Task<ReceptionQueueEntry?> FindByAppointmentAsync(string appointmentId, CancellationToken ct = default) =>
        Task.FromResult(Items.Values.FirstOrDefault(e => e.AppointmentId == appointmentId));

    public Task<ReceptionQueueEntry?> FindByVisitAsync(string visitId, CancellationToken ct = default) =>
        Task.FromResult(Items.Values.FirstOrDefault(e => e.VisitId == visitId));

    public Task<int> CountForDayAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default) =>
        Task.FromResult(Items.Values.Count(e => e.EnqueuedUtc >= startUtc && e.EnqueuedUtc < endUtc));

    public void Add(ReceptionQueueEntry entity) => Items[entity.Id] = entity;

    public void AddRange(IEnumerable<ReceptionQueueEntry> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
}

internal sealed class FakeAppointmentRepository : IAppointmentRepository
{
    public readonly Dictionary<string, Appointment> Items = new(StringComparer.Ordinal);

    public Task<Appointment?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Items.GetValueOrDefault(id));

    public Task<Appointment> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Items[id]);

    public void Add(Appointment entity) => Items[entity.Id] = entity;

    public void AddRange(IEnumerable<Appointment> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
}

internal sealed class FakeOwners : IOwnerRepository
{
    public readonly Dictionary<string, Owner> Items = new(StringComparer.Ordinal);

    public Task<Owner?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Items.GetValueOrDefault(id));

    public Task<Owner> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Items[id]);

    public Task<Owner?> FindByPhoneAsync(string normalizedPhone, CancellationToken ct = default) =>
        Task.FromResult(Items.Values.FirstOrDefault(o => o.PhonePrimary == normalizedPhone));

    public void Add(Owner entity) => Items[entity.Id] = entity;

    public void AddRange(IEnumerable<Owner> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
}

internal sealed class FakePets : IPetRepository
{
    public readonly Dictionary<string, Pet> Items = new(StringComparer.Ordinal);

    public Task<Pet?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Items.GetValueOrDefault(id));

    public Task<Pet> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Items[id]);

    public Task<Pet?> FindByOwnerAndNameAsync(string ownerId, string name, CancellationToken ct = default) =>
        Task.FromResult(Items.Values.FirstOrDefault(p => p.OwnerId == ownerId && p.Name == name));

    public Task<IReadOnlyList<Pet>> ListByOwnerAndNameAsync(string ownerId, string name, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Pet>>(Items.Values.Where(p => p.OwnerId == ownerId && p.Name == name).ToList());

    public void Add(Pet entity) => Items[entity.Id] = entity;

    public void AddRange(IEnumerable<Pet> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
}

internal sealed class FakeVisits : IVisitRepository
{
    public readonly Dictionary<string, Visit> Items = new(StringComparer.Ordinal);

    public Task<Visit?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Items.GetValueOrDefault(id));

    public Task<Visit> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Items[id]);

    public void Add(Visit entity) => Items[entity.Id] = entity;

    public void AddRange(IEnumerable<Visit> entities)
    {
        foreach (var entity in entities) Add(entity);
    }
}

internal sealed class FakeMemberships : IStaffMembershipRepository
{
    public readonly List<StaffMembership> Items = [];

    public Task<StaffMembership?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(m => m.Id == id));

    public Task<StaffMembership?> FindActiveAsync(string tenantId, string userId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(m => m.TenantId == tenantId && m.UserId == userId && m.IsActive));

    public void Add(StaffMembership entity) => Items.Add(entity);
}

/// <summary>Doctor-name lookup used by the encounter draft; the queue projection itself is covered in Persistence.Tests.</summary>
internal sealed class FakeReceptionQueries(params ReceptionDoctorReadModel[] doctors) : IReceptionQueueQueries
{
    public Task<IReadOnlyList<QueueTicketReadModel>> GetQueueAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<QueueTicketReadModel>>([]);

    public Task<IReadOnlyList<ReceptionDoctorReadModel>> GetDoctorsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReceptionDoctorReadModel>>(doctors);
}
