using System.Text.Json;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Animora.Sync.Tests.ReplicaTestSupport;

namespace Animora.Sync.Tests;

/// <summary>TASK-6: an applied batch authored by another user lands in the typed business tables the screens read.</summary>
public sealed class ReplicaMaterializerTests
{
    private static SyncRecord Json(string entity, string id, string version, string payload, char op = 'U') =>
        new(entity, id, op, version, FeedFixtures.Now, op == 'D', op == 'D' ? null : JsonDocument.Parse(payload).RootElement.Clone());

    private const string OwnerJson = """{"fullName":"Ali Rezaei","phonePrimary":"09120000000","gender":1,"registeredUtc":"2026-10-01T08:00:00+00:00","isBlocked":false}""";
    private const string PetJson = """{"ownerId":"O1","name":"Rex","species":1,"gender":0,"isNeutered":true,"status":0,"registeredUtc":"2026-10-01T08:00:00+00:00"}""";
    private const string ApptJson = """{"petId":"P1","ownerId":"O1","appointmentTypeCode":"CHECKUP","startUtc":"2026-10-12T09:00:00+00:00","durationMinutes":30,"state":1,"source":0,"createdByUserId":"U-A","rescheduleCount":0}""";

    [Fact]
    public async Task AppliedBatch_ProjectsOtherUsersRecords_IntoBusinessTables_PreservingServerBookkeeping()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);

        var result = await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1,
            Json("Owner", "O1", "v1", OwnerJson), Json("Pet", "P1", "v1", PetJson), Json("Appointment", "A1", "v7", ApptJson)));
        Assert.True(result.IsSuccess, result.Detail);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = await db.Set<Owner>().SingleAsync(x => x.Id == "O1");
        Assert.Equal("Ali Rezaei", owner.FullName);
        Assert.Equal("v1", owner.Version);
        Assert.Equal(FeedFixtures.Now, owner.UpdatedAt); // server UpdatedAt kept, not re-stamped by the local context
        Assert.False(string.IsNullOrEmpty(owner.SearchKey));
        Assert.Equal("Rex", (await db.Set<Pet>().SingleAsync(x => x.Id == "P1")).Name);
        var appt = await db.Set<Appointment>().SingleAsync(x => x.Id == "A1");
        Assert.Equal("U-A", appt.CreatedByUserId);
        Assert.Equal(AppointmentState.Confirmed, appt.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), appt.StartUtc);
    }

    [Fact]
    public async Task Tombstone_SoftDeletesLocalRow()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        Assert.True((await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, Json("Owner", "O1", "v1", OwnerJson)))).IsSuccess);
        Assert.True((await ApplyAsync(store, Batch("b2", 1, 10, 20), Page("b2", 0, 1, Json("Owner", "O1", "v2", "{}", 'D')))).IsSuccess);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.False(await db.Set<Owner>().AnyAsync(x => x.Id == "O1")); // soft-delete filter hides it
        Assert.True(await db.Set<Owner>().IgnoreQueryFilters(["SoftDelete"]).AnyAsync(x => x.Id == "O1" && x.Deleted));
    }

    [Fact]
    public async Task RecordMissingRequiredField_IsSkipped_AndBatchStillCommits()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);

        var result = await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1,
            Json("Owner", "BAD", "v1", """{"phonePrimary":"1"}"""), Json("Owner", "O1", "v1", OwnerJson)));

        Assert.True(result.IsSuccess, result.Detail);
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.False(await db.Set<Owner>().AnyAsync(x => x.Id == "BAD"));
        Assert.True(await db.Set<Owner>().AnyAsync(x => x.Id == "O1"));
        Assert.Equal((Epoch, 10L, 1L), await CheckpointAsync(fixture));
    }

    [Fact]
    public async Task PendingLocalIntent_IsNotOverwritten_ButServerRowStaysInReplicaBase()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        // EnqueueOverlayAsync posts a pending command on aggregate Invoice/INV-1; use an Owner-targeted one instead.
        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<Animora.Application.Commands.ICommandJournal>();
            var uow = scope.ServiceProvider.GetRequiredService<Animora.Application.Abstractions.IUnitOfWork>();
            var envelope = Animora.Contracts.Wire.Commands.CommandEnvelope.Create(Animora.Domain.Primitives.IdGenerator.NewId(), fixture.TenantId,
                "U1", "D1", "patients.owner.update", "Owner", "O1", "{}", "fp", DateTimeOffset.UnixEpoch, 1);
            Assert.True((await uow.ExecuteAsync(async ct => await journal.EnqueueAsync(envelope, ct))).IsSuccess);
        }

        var result = await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, Json("Owner", "O1", "v1", OwnerJson)));
        Assert.True(result.IsSuccess, result.Detail);

        using var read = fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.False(await db.Set<Owner>().AnyAsync(x => x.Id == "O1")); // local unsettled intent wins
        Assert.Contains(await BaseRowsAsync(fixture), r => r.Entity == "Owner" && r.EntityId == "O1");
    }

    [Fact]
    public async Task PendingCreate_WithNullAggregateId_IsProtectedByOperationId_SoServerEchoDoesNotClobberIt()
    {
        // Regression (ce00111b + tenant-isolation sync): a *create* carries AggregateIdOrNull => null, but its slot-0
        // aggregate id IS the OperationId, and client & server now derive the SAME id. When the server echoes that row
        // back, the overlay must still protect the just-created local row — keyed by OperationId, since AggregateId is
        // null — or it "disappears from the UI while existing on the server".
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        const string ownerId = "00000000000000000000000001"; // = OperationId of the create (slot 0)
        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<Animora.Application.Commands.ICommandJournal>();
            var uow = scope.ServiceProvider.GetRequiredService<Animora.Application.Abstractions.IUnitOfWork>();
            // CreateOwner: AggregateId is null (slot-0 id == OperationId == ownerId).
            var envelope = Animora.Contracts.Wire.Commands.CommandEnvelope.Create(ownerId, fixture.TenantId,
                "U1", "D1", "patients.owner.create", "Owner", null, "{}", "fp", DateTimeOffset.UnixEpoch, 1);
            Assert.True((await uow.ExecuteAsync(async ct => await journal.EnqueueAsync(envelope, ct))).IsSuccess);
        }

        var result = await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, Json("Owner", ownerId, "v1", OwnerJson)));
        Assert.True(result.IsSuccess, result.Detail);

        using var read = fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.False(await db.Set<Owner>().AnyAsync(x => x.Id == ownerId)); // local unsettled create wins; the pull is skipped
        Assert.Contains(await BaseRowsAsync(fixture), r => r.Entity == "Owner" && r.EntityId == ownerId); // server row still in repl_base
    }

    [Fact]
    public async Task SyncedAppointment_AppendsHistoryRow_AttributedToItsAuthor_NotTheLocalUser()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);

        Assert.True((await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, Json("Appointment", "A1", "v1", ApptJson)))).IsSuccess);
        // The same record arriving again (a later pull) must not duplicate the feed row.
        Assert.True((await ApplyAsync(store, Batch("b2", 1, 10, 20), Page("b2", 0, 1, Json("Appointment", "A1", "v2", ApptJson)))).IsSuccess);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var rows = await db.Set<ActivityEvent>().AsNoTracking().Where(x => x.EntityType == "Appointment" && x.EntityId == "A1").ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("U-A", row.ActorUserId);
        Assert.Equal("history.appointment.booked", row.Title);
        Assert.StartsWith("sync:", row.CorrelationId);
    }

    [Fact]
    public async Task SyncedAppointment_WithoutAuthor_IsSkipped_AndGetsNoFabricatedActorRow()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        var noAuthor = ApptJson.Replace("\"createdByUserId\":\"U-A\",", string.Empty);

        Assert.True((await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, Json("Appointment", "A2", "v1", noAuthor)))).IsSuccess);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.False(await db.Set<ActivityEvent>().AnyAsync(x => x.EntityId == "A2"));
    }
}
