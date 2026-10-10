using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Operator;
using Animora.Server.Tests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Server.Tests.Subscriptions;

/// <summary>Authored only; all real PostgreSQL execution is env-gated and deferred.</summary>
public sealed class SubscriptionPersistenceTests(PgFixture fixture) : IClassFixture<PgFixture>
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class Scheduler(AnimoraOperatorDbContext db, bool fail = false) : ILicenseIssuanceScheduler
    {
        public int Calls { get; private set; }
        public Task ScheduleAsync(string tenantId, string subscriptionId, CancellationToken ct = default)
        {
            Assert.NotNull(db.Database.CurrentTransaction); Calls++;
            if (fail) throw new InvalidOperationException("injected_scheduler_failure");
            return Task.CompletedTask;
        }
    }
    private async Task Migrate()
    {
        await using var db = new AnimoraServerDbContext(new DbContextOptionsBuilder<AnimoraServerDbContext>()
            .UseNpgsql(fixture.ConnectionString).Options, new NullTenantContext());
        await db.Database.MigrateAsync();
    }
    private AnimoraOperatorDbContext Context() => new(new DbContextOptionsBuilder<AnimoraOperatorDbContext>()
        .UseNpgsql(fixture.ConnectionString).Options);
    private static ProvisioningRequest Request() => ProvisioningEngineTests.Request(IdGenerator.NewId(), "c-" + IdGenerator.NewId().ToLowerInvariant());
    private static ProvisioningEngine Engine(AnimoraOperatorDbContext db, Clock clock, ILicenseIssuanceScheduler? scheduler = null) =>
        new(db, new PasswordHasher<ServerIdentityUser>(), scheduler ?? new NoopLicenseIssuanceScheduler(), clock);

    [PgFact]
    public async Task SameKeySameFingerprint_ReplaysWithoutSecondTenant()
    {
        await Migrate(); var r = Request(); var clock = new Clock();
        await using var db = Context(); var scheduler = new Scheduler(db);
        var engine = Engine(db, clock, scheduler);
        var first = await engine.ProvisionAsync(r, new ActorRef(ActorKind.Operator));
        Assert.True(first.IsSuccess); var created = Assert.IsType<ProvisioningResult>(first.Value);
        Assert.False(created.WasReplay); Assert.NotNull(created.OwnerTemporaryPassword);
        db.ChangeTracker.Clear();
        var replay = await engine.ProvisionAsync(r, new ActorRef(ActorKind.WebsiteIntent));
        Assert.True(replay.IsSuccess); var repeated = Assert.IsType<ProvisioningResult>(replay.Value);
        Assert.True(repeated.WasReplay); Assert.Null(repeated.OwnerTemporaryPassword);
        Assert.Equal(created.TenantId, repeated.TenantId); Assert.Equal(created.OwnerUserId, repeated.OwnerUserId);
        Assert.Equal(1, scheduler.Calls);
        Assert.Equal(1, await db.Tenants.CountAsync(x => x.Code == r.TenantCode));
        Assert.Equal(1, await db.Subscriptions.CountAsync(x => x.TenantId == created.TenantId));
    }

    [PgFact]
    public async Task SameKeyDifferentFingerprint_Conflicts()
    {
        await Migrate(); var r = Request(); await using var db = Context();
        var engine = Engine(db, new Clock()); Assert.True((await engine.ProvisionAsync(r, new ActorRef(ActorKind.Operator))).IsSuccess);
        db.ChangeTracker.Clear();
        var changed = await engine.ProvisionAsync(r with { DisplayName = "Different" }, new ActorRef(ActorKind.Operator));
        Assert.Equal(ErrorCodes.ProvisioningKeyConflict, changed.Error);
        var taken = await engine.ProvisionAsync(r with { IdempotencyKey = IdGenerator.NewId() }, new ActorRef(ActorKind.Operator));
        Assert.Equal(ErrorCodes.TenantCodeTaken, taken.Error);
    }

    [PgFact]
    public async Task CreateTenantWithSubscription_OneTransaction_RollsBackTogether()
    {
        await Migrate(); var r = Request(); await using var db = Context();
        var scheduler = new Scheduler(db, fail: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db, new Clock(), scheduler).ProvisionAsync(r, new ActorRef(ActorKind.Operator)));
        db.ChangeTracker.Clear();
        Assert.False(await db.Tenants.AnyAsync(x => x.Code == r.TenantCode));
        Assert.False(await db.ProvisioningLedger.AnyAsync(x => x.IdempotencyKey == r.IdempotencyKey));
        Assert.Equal(1, scheduler.Calls);
    }

    [PgFact]
    public async Task ConcurrentProvisionSameKey_OneTenant()
    {
        await Migrate(); var r = Request();
        async Task<Result<ProvisioningResult>> Provision()
        {
            await using var db = Context();
            return await Engine(db, new Clock()).ProvisionAsync(r, new ActorRef(ActorKind.Operator));
        }
        var calls = await Task.WhenAll(Provision(), Provision());
        Assert.All(calls, result => Assert.True(result.IsSuccess));
        Assert.Equal(1, calls.Count(x => x.Value is { WasReplay: false }));
        Assert.Equal(calls[0].Value?.TenantId, calls[1].Value?.TenantId);
        await using var check = Context(); Assert.Equal(1, await check.Tenants.CountAsync(x => x.Code == r.TenantCode));
    }

    [PgFact]
    public async Task EventsAndOperatorAudit_ImmutableTriggers()
    {
        await Migrate(); await using var db = Context();
        var result = await Engine(db, new Clock()).ProvisionAsync(Request(), new ActorRef(ActorKind.Operator));
        var value = Assert.IsType<ProvisioningResult>(result.Value);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.\"SubscriptionEvents\" SET \"Type\"='tampered' WHERE \"SubscriptionId\"={value.SubscriptionId}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM public.\"OperatorAudit\" WHERE \"TargetTenantId\"={value.TenantId}"));
    }

    [PgFact]
    public async Task SuspendedSubscription_UpdatesTenantStatus_AndIfMatchConflictDoesNotAppend()
    {
        await Migrate(); await using var db = Context(); var clock = new Clock();
        var created = await Engine(db, clock).ProvisionAsync(Request(), new ActorRef(ActorKind.Operator));
        var value = Assert.IsType<ProvisioningResult>(created.Value); db.ChangeTracker.Clear();
        var sub = await db.Subscriptions.AsNoTracking().SingleAsync(x => x.Id == value.SubscriptionId);
        var service = new SubscriptionService(db, new SeatUsageReader(db), new TenantAdministration(db, clock),
            new NoopLicenseIssuanceScheduler(), clock, Options.Create(new SubscriptionOptions()));
        var bad = await service.MutateAsync(value.TenantId, "stale", new SubscriptionMutation("suspend", Reason: "review"), new ActorRef(ActorKind.Operator));
        Assert.Equal(ErrorCodes.ConcurrencyConflict, bad.Error); db.ChangeTracker.Clear();
        var good = await service.MutateAsync(value.TenantId, sub.ConcurrencyStamp, new SubscriptionMutation("suspend", Reason: "review"), new ActorRef(ActorKind.Operator));
        Assert.True(good.IsSuccess); db.ChangeTracker.Clear();
        Assert.Equal(TenantStatus.Suspended, await db.Tenants.Where(x => x.Id == value.TenantId).Select(x => x.Status).SingleAsync());
        Assert.Equal(2, await db.SubscriptionEvents.CountAsync(x => x.SubscriptionId == sub.Id));
    }

    [PgFact]
    public async Task SweepAndConsumer_Idempotent_OnRedelivery()
    {
        await Migrate(); await using var db = Context(); var clock = new Clock();
        var created = await Engine(db, clock).ProvisionAsync(Request(), new ActorRef(ActorKind.Operator));
        var value = Assert.IsType<ProvisioningResult>(created.Value); db.ChangeTracker.Clear(); clock.UtcNow = clock.UtcNow.AddDays(15);
        var sweep = new SubscriptionSweep(db, clock); await sweep.EnqueueDueAsync(); await sweep.EnqueueDueAsync();
        Assert.Equal(1, await db.Messages.CountAsync(x => x.TenantId == value.TenantId && x.MessageType == OutboxMessageTypes.SubscriptionExpiry));
        var service = new SubscriptionService(db, new SeatUsageReader(db), new TenantAdministration(db, clock),
            new NoopLicenseIssuanceScheduler(), clock, Options.Create(new SubscriptionOptions()));
        for (var i = 0; i < 2; i++)
        {
            db.ChangeTracker.Clear();
            var s = await db.Subscriptions.AsNoTracking().SingleAsync(x => x.Id == value.SubscriptionId);
            Assert.True((await service.MutateAsync(value.TenantId, s.ConcurrencyStamp, new SubscriptionMutation("tick"), new ActorRef(ActorKind.System))).IsSuccess);
        }
        Assert.Equal(2, await db.SubscriptionEvents.CountAsync(x => x.SubscriptionId == value.SubscriptionId));
    }

    [PgFact]
    public async Task Login_WrongPasswordAndUnknownUser_SameError()
    {
        await Migrate(); await using var db = Context(); var clock = new Clock();
        var hasher = new PasswordHasher<OperatorAccount>();
        var account = new OperatorAccount { Id = IdGenerator.NewId(), UserName = IdGenerator.NewId(), SecurityStamp = IdGenerator.NewId(), CreatedUtc = clock.UtcNow };
        account.NormalizedUserName = account.UserName.ToUpperInvariant(); account.PasswordHash = hasher.HashPassword(account, "Correct-password-1");
        db.Accounts.Add(account); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new OperatorAccountService(db, hasher, new OperatorDummyCredential(hasher), clock);
        var bad = await service.LoginAsync(account.UserName, "Wrong-password-1", "test"); db.ChangeTracker.Clear();
        var unknown = await service.LoginAsync("not-found-" + IdGenerator.NewId(), "Wrong-password-1", "test");
        Assert.Equal(ErrorCodes.OperatorCredentialsInvalid, bad.Error); Assert.Equal(bad.Error, unknown.Error); Assert.Equal(bad.Detail, unknown.Detail);
    }
}
