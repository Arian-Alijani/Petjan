using System.Text.Json;
using Animora.Domain.Inventory;
using Animora.Sync.Custom.Replica;
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
    public async Task SoftDeleteUpsert_WithPayload_WhenRowAbsent_InsertsDeleted()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        // A soft-delete travels as an upsert (Op='U', Deleted=true) with the full image; the peer never had the row.
        var softDeleted = new SyncRecord("Owner", "O9", 'U', "v3", FeedFixtures.Now, true, JsonDocument.Parse(OwnerJson).RootElement.Clone());
        Assert.True((await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, softDeleted))).IsSuccess);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.False(await db.Set<Owner>().AnyAsync(x => x.Id == "O9")); // hidden by the soft-delete filter
        Assert.True(await db.Set<Owner>().IgnoreQueryFilters(["SoftDelete"]).AnyAsync(x => x.Id == "O9" && x.Deleted));
    }

    [Fact]
    public async Task SoftDeletedOwner_AppendsExactlyOneDeletedHistoryRow_WithReplicatedActor_AndIsIdempotent()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        Assert.True((await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, Json("Owner", "O1", "v1", OwnerJson)))).IsSuccess);

        const string deletedJson = """{"fullName":"Ali Rezaei","phonePrimary":"09120000000","gender":1,"registeredUtc":"2026-10-01T08:00:00+00:00","isBlocked":false,"deletedByUserId":"U-B","deletedUtc":"2026-10-05T10:30:00+00:00"}""";
        var softDeleted = new SyncRecord("Owner", "O1", 'U', "v2", FeedFixtures.Now, true, JsonDocument.Parse(deletedJson).RootElement.Clone());
        Assert.True((await ApplyAsync(store, Batch("b2", 1, 10, 20), Page("b2", 0, 1, softDeleted))).IsSuccess);
        // Re-applying the same delete (a replayed/retried batch) must not duplicate the history row.
        Assert.True((await ApplyAsync(store, Batch("b3", 1, 20, 30), Page("b3", 0, 1, softDeleted))).IsSuccess);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var rows = await db.Set<ActivityEvent>().AsNoTracking()
            .Where(x => x.EntityType == "Owner" && x.EntityId == "O1" && x.Title == "history.owner.deleted").ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("U-B", row.ActorUserId);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 10, 30, 0, TimeSpan.Zero), row.OccurredUtc);
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
    [Fact]
    public void SupportedEntities_CoversRequiredInventoryClinicalBoardingAndIdentitySurface()
    {
        string[] expected = ["Owner", "Pet", "Appointment", "ReceptionQueueEntry", "UnitOfMeasure", "ProductCategory", "Product",
            "Supplier", "Insurer", "LedgerAccount", "AccountingPeriod", "WeightEntry", "MedicalHistoryEntry", "VaccinationRecord",
            "VaccineCatalogItem", "Visit", "TreatmentPlan", "TreatmentOccurrence", "Room", "BoardingServiceDef", "BoardingStay",
            "RoleDefinition", "StaffMembership", "StockLedgerEntry"];
        Assert.Equal(expected.OrderBy(x => x), ReplicaMaterializer.SupportedEntities.OrderBy(x => x));
    }

    [Fact]
    public async Task ProductAndVariants_FromAnotherClient_MaterializeAndTombstone()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        const string product = """{"sku":"DRUG-1","name":"Antibiotic","categoryId":"CAT-1","kind":0,"unitCode":"PCS","isActive":true,"trackStock":true,"defaultCostPrice":{"minor":100,"currency":"IRT"},"defaultSalePrice":{"minor":150,"currency":"IRT"},"reorderLevel":{"value":2,"unitCode":"PCS"},"variants":[{"variantId":"V1","sku":"DRUG-1-10","attributeName":"Strength","attributeValue":"10mg","costPriceMinor":100,"costPriceCurrency":"IRT","salePriceMinor":150,"salePriceCurrency":"IRT","reorderLevelScaled":2000,"reorderLevelUnit":"PCS","isActive":true}]}""";
        Assert.True((await ApplyAsync(store, Batch("products", 1, 0, 10), Page("products", 0, 1,
            Json("ProductCategory", "CAT-1", "c1", """{"code":"MED","displayName":"Medicine","isActive":true}"""),
            Json("UnitOfMeasure", "UNIT-1", "u1", """{"code":"PCS","displayKey":"unit.pcs","decimalPlaces":0}"""),
            Json("Product", "PR-1", "p1", product)))).IsSuccess);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var row = await db.Set<Product>().SingleAsync(x => x.Id == "PR-1");
            Assert.Equal("Antibiotic", row.Name);
            Assert.Equal("p1", row.Version);
            Assert.Equal(FeedFixtures.Now, row.UpdatedAt);
            Assert.Equal(150, row.DefaultSalePrice.Minor);
            Assert.Single(row.Variants);
            Assert.Equal("V1", row.Variants.Single().VariantId);
            Assert.True(await db.Set<UnitOfMeasure>().AnyAsync(x => x.Id == "UNIT-1"));
        }
        Assert.True((await ApplyAsync(store, Batch("delete-product", 1, 10, 20), Page("delete-product", 0, 1,
            Json("Product", "PR-1", "p2", "{}", 'D')))).IsSuccess);
        using var read = fixture.CreateScope();
        Assert.False(await read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>().Set<Product>().AnyAsync(x => x.Id == "PR-1"));
    }

    [Fact]
    public async Task PulledCompletedVisit_RebuildsTheReadyHandoff_UnderTheServerId_AndNeverDowngradesAnInvoicedOne()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        const string visit = """{"petId":"P1","ownerId":"O1","doctorUserId":"DOC-1","state":2,"reason":0,"startedUtc":"2026-10-12T09:00:00+00:00","completedUtc":"2026-10-12T09:30:00+00:00","diagnosis":"ok","billingHandoffs":[{"id":"H-SERVER-1","visitId":"V1","ownerId":"O1","petId":"P1","doctorId":"DOC-1","completedUtc":"2026-10-12T09:30:00+00:00","linesJson":{"serviceLines":[],"prescriptions":[]},"status":0,"invoiceId":null}]}""";
        Assert.True((await ApplyAsync(store, Batch("v1", 1, 0, 10), Page("v1", 0, 1, Json("Visit", "V1", "v1", visit)))).IsSuccess);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            // AsTracking is REQUIRED: the store sets QueryTrackingBehavior.NoTracking globally, so a plain read returns a
            // detached entity and MarkInvoiced + SaveChanges would silently persist nothing (which is what made this
            // test report a bogus "the pull downgraded it" failure). Production marks the handoff the same way —
            // SalesPostingData.MarkHandoffInvoicedAsync reads with AsTracking for exactly this reason.
            var handoff = await db.Set<VisitBillingHandoff>().AsTracking().SingleAsync(h => h.VisitId == "V1");
            Assert.Equal("H-SERVER-1", handoff.Id);
            Assert.Equal(HandoffStatus.Ready, handoff.Status);
            Assert.Equal("DOC-1", handoff.DoctorId);
            Assert.Contains("ServiceLines", handoff.LinesJson);
            handoff.MarkInvoiced("INV-1");
            await db.SaveChangesAsync();
        }

        // The local invoicing must be durable BEFORE the next pull, otherwise the assertion below cannot tell
        // "the pull downgraded it" apart from "the write never landed".
        using (var verify = fixture.CreateScope())
        {
            var persisted = await verify.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>()
                .Set<VisitBillingHandoff>().SingleAsync(h => h.VisitId == "V1");
            Assert.Equal(HandoffStatus.Invoiced, persisted.Status);
            Assert.Equal("INV-1", persisted.InvoiceId);
        }

        // A later pull of the same (still Ready on the server) image must not resurrect the locally invoiced handoff.
        Assert.True((await ApplyAsync(store, Batch("v2", 1, 10, 20), Page("v2", 0, 1, Json("Visit", "V1", "v2", visit)))).IsSuccess);
        using var read = fixture.CreateScope();
        var after = await read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>().Set<VisitBillingHandoff>().SingleAsync(h => h.VisitId == "V1");
        Assert.Equal(HandoffStatus.Invoiced, after.Status);
        Assert.Equal("INV-1", after.InvoiceId);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task CompletedVisitHistory_UsesCompletionTimeAndDoctor_AndIsIdempotent(int state)
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        var visit = $$"""{"petId":"P1","ownerId":"O1","doctorUserId":"DOC-1","state":{{state}},"startedUtc":"2026-10-12T09:00:00+00:00","completedUtc":"2026-10-12T09:30:00+00:00"}""";
        Assert.True((await ApplyAsync(store, Batch("v1", 1, 0, 10), Page("v1", 0, 1, Json("Visit", "V1", "v1", visit)))).IsSuccess);
        Assert.True((await ApplyAsync(store, Batch("v2", 1, 10, 20), Page("v2", 0, 1, Json("Visit", "V1", "v2", visit)))).IsSuccess);
        using var scope = fixture.CreateScope();
        var row = Assert.Single(await scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>()
            .Set<ActivityEvent>().Where(e => e.EntityId == "V1").ToListAsync());
        Assert.Equal("history.visit.completed", row.Title);
        Assert.Equal("DOC-1", row.ActorUserId);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero), row.OccurredUtc);
    }

    [Fact]
    public async Task DeferredOverlay_SurvivesNewStoreAndStartupCleanup_ThenReplaysOnIdleAfterSettlement()
    {
        using var fixture = await NewFixtureAsync();
        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<Animora.Application.Commands.ICommandJournal>();
            var uow = scope.ServiceProvider.GetRequiredService<Animora.Application.Abstractions.IUnitOfWork>();
            var envelope = Animora.Contracts.Wire.Commands.CommandEnvelope.Create(Animora.Domain.Primitives.IdGenerator.NewId(),
                fixture.TenantId, "U1", "D1", "patients.owner.update", "Owner", "O1", "{}", "fp", DateTimeOffset.UnixEpoch, 1);
            Assert.True((await uow.ExecuteAsync(async ct => await journal.EnqueueAsync(envelope, ct))).IsSuccess);
        }
        Assert.True((await ApplyAsync(NewStore(fixture), Batch("b1", 1, 0, 10), Page("b1", 0, 1,
            Json("Owner", "O1", "v1", OwnerJson)))).IsSuccess);
        var restarted = NewStore(fixture);
        // Begin deletes only abandoned batch staging, never the durable typed retry namespace.
        await using (var aborted = (await restarted.BeginAsync(Animora.Sync.Abstractions.SyncScope.Default, Batch("abort", 1, 10, 20))).Value!)
            await aborted.AbortAsync();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.False(await db.Set<Owner>().AnyAsync());
            Assert.Equal(1, await db.Set<Animora.Persistence.Sqlite.Replica.ReplicaStaging>()
                .CountAsync(x => x.BatchId == ReplicaMaterializer.PendingProjectionBatch));
            await db.Set<PendingCommand>().ExecuteUpdateAsync(u => u.SetProperty(x => x.Status,
                Animora.Domain.Primitives.SettlementState.Settled));
        }
        Assert.True((await restarted.ReadCheckpointAsync(Animora.Sync.Abstractions.SyncScope.Default)).IsSuccess);
        using var read = fixture.CreateScope();
        var after = read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.Equal("Ali Rezaei", (await after.Set<Owner>().SingleAsync()).FullName);
        Assert.Equal(0, await after.Set<Animora.Persistence.Sqlite.Replica.ReplicaStaging>().CountAsync());
        Assert.Equal((Epoch, 10L, 1L), await CheckpointAsync(fixture));
    }

    [Fact]
    public async Task CorrectedImage_SupersedesADeferredBadVersion_AndClearsTheMarker()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        Assert.True((await ApplyAsync(store, Batch("bad", 1, 0, 10), Page("bad", 0, 1,
            Json("Owner", "O1", "bad-v1", "{}")))).IsSuccess);
        Assert.True((await ApplyAsync(store, Batch("fixed", 1, 10, 20), Page("fixed", 0, 1,
            Json("Owner", "O1", "good-v2", OwnerJson)))).IsSuccess);
        using var read = fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.Equal("good-v2", (await db.Set<Owner>().SingleAsync()).Version);
        Assert.Equal(0, await db.Set<Animora.Persistence.Sqlite.Replica.ReplicaStaging>().CountAsync());
    }

    [Fact]
    public async Task ChildConstraintFailure_RollsBackRootAndChildren_WithoutBlockingGoodRows()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        const string broken = """{"sku":"DRUG-1","name":"Drug","categoryId":"CAT","unitCode":"PCS","variants":[{"variantId":"V1","sku":"S1","attributeName":"Dose","attributeValue":"1"},{"variantId":"V1","sku":"S1","attributeName":"Dose","attributeValue":"1"}]}""";
        Assert.True((await ApplyAsync(store, Batch("bad", 1, 0, 10), Page("bad", 0, 1,
            Json("Product", "PR-1", "v1", broken), Json("Owner", "O1", "v1", OwnerJson)))).IsSuccess);
        using var read = fixture.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.False(await db.Set<Product>().AnyAsync());
        Assert.True(await db.Set<Owner>().AnyAsync());
        Assert.Equal(1, await db.Set<Animora.Persistence.Sqlite.Replica.ReplicaStaging>()
            .CountAsync(x => x.BatchId == ReplicaMaterializer.PendingProjectionBatch));
    }

    // Regression: the sign-in mirror creates a StaffMembership under a random local id, while sync pulls the server's
    // membership for the same user under the server id. Both share (TenantId, UserId, Role) — the local UNIQUE business-key
    // index — so the server INSERT used to trip SQLITE_CONSTRAINT and be skipped on every pull forever. The server row must
    // now supersede the mirror row (single identity source), not be deferred.
    [Fact]
    public async Task SyncedStaffMembership_SupersedesTheRandomIdLocalMirror_ForTheSameUserAndRole()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);

        // Seed the local mirror row: same user+role as the server will send, but a DIFFERENT (random) id.
        using (var seed = fixture.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Set<Animora.Domain.Identity.StaffMembership>().Add(
                Animora.Domain.Identity.StaffMembership.Create(fixture.TenantId, "USER-1",
                    Animora.Domain.Identity.StaffRole.Doctor, new FixedClock(FeedFixtures.Now)));
            await db.SaveChangesAsync();
        }

        const string serverMembership =
            """{"userId":"USER-1","role":1,"status":0,"joinedUtc":"2026-10-01T08:00:00+00:00","workStartMinute":480,"workEndMinute":1200,"workDaysMask":63}""";
        var result = await ApplyAsync(store, Batch("sm", 1, 0, 10), Page("sm", 0, 1,
            Json("StaffMembership", "SRV-MEM-1", "v1", serverMembership)));
        Assert.True(result.IsSuccess, result.Detail);

        using var read = fixture.CreateScope();
        var store2 = read.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        // Exactly one membership for the user, carrying the SERVER id — the random mirror row was retired.
        var memberships = await store2.Set<Animora.Domain.Identity.StaffMembership>()
            .Where(x => x.TenantId == fixture.TenantId && x.UserId == "USER-1").ToListAsync();
        Assert.Single(memberships);
        Assert.Equal("SRV-MEM-1", memberships[0].Id);
        // No durable retry was kept: the row projected successfully instead of being skipped on a constraint violation.
        Assert.Equal(0, await store2.Set<Animora.Persistence.Sqlite.Replica.ReplicaStaging>()
            .CountAsync(x => x.BatchId == ReplicaMaterializer.PendingProjectionBatch && x.Entity == "StaffMembership"));
    }

    private sealed class FixedClock(DateTimeOffset now) : Animora.Domain.Time.IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

}
