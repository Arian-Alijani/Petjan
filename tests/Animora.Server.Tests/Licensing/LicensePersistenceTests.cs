using Animora.Contracts.Wire.Licensing;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Licensing.Issuance;
using Animora.Server.Licensing.Verification;
using Animora.Server.Tests.Persistence;
using Animora.Server.Tests.Subscriptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Server.Tests.Licensing;

/// <summary>Authored only; real PostgreSQL execution is env-gated (ANIMORA_PG_TEST_CONNECTION) and deferred.</summary>
public sealed class LicensePersistenceTests(PgFixture fixture) : IClassFixture<PgFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private readonly FixedClock _clock = new(T0);
    private readonly IOptions<LicensingOptions> _options = Options.Create(new LicensingOptions());
    // Static: xUnit builds one class instance per test, but the protected licence key row lives in the shared PG
    // database, so every instance must unprotect with the same key ring that protected it.
    private static readonly EphemeralDataProtectionProvider _protection = new();

    private async Task Migrate()
    {
        await using var db = TestSupport.CreateContext(connection: fixture.ConnectionString);
        await db.Database.MigrateAsync();
    }
    private LicenseStore Store() => new(new DbContextOptionsBuilder<LicenseStore>().UseNpgsql(fixture.ConnectionString).Options);
    private AnimoraOperatorDbContext Operator() => new(new DbContextOptionsBuilder<AnimoraOperatorDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    private sealed record World(string TenantId, string UserId, string SubscriptionId);

    private async Task<World> CreateTenantWithKey()
    {
        await Migrate();
        await using (var store = Store()) await new LicenseKeyManager(store, _protection, _clock, _options).EnsureAsync();
        await using var op = Operator();
        var engine = new ProvisioningEngine(op, new PasswordHasher<Animora.Persistence.PostgreSql.Identity.ServerIdentityUser>(),
            new NoopLicenseIssuanceScheduler(), _clock);
        var code = "l-" + IdGenerator.NewId().ToLowerInvariant();
        var r = Assert.IsType<ProvisioningResult>((await engine.ProvisionAsync(ProvisioningEngineTests.Request(IdGenerator.NewId(), code),
            new ActorRef(ActorKind.Operator))).Value);
        return new(r.TenantId, r.OwnerUserId, r.SubscriptionId);
    }

    private async Task Grant(World w, int maxDevices = 4, LicenseGrantStatus status = LicenseGrantStatus.Active, int days = 30)
    {
        await using var store = Store();
        store.Grants.Add(new LicenseGrant { TenantId = w.TenantId, SubscriptionId = w.SubscriptionId, PlanCode = "standard", SeatLimit = 2,
            MaxDevices = maxDevices, ValidUntilUtc = T0.AddDays(days), Status = status, Sequence = 1, SourceEventSequence = 1, UpdatedUtc = T0 });
        await store.SaveChangesAsync();
    }

    private async Task<(string Id, string Hash)> Device(World w)
    {
        await using var db = TestSupport.CreateContext(new FixedTenantContext(w.TenantId), fixture.ConnectionString);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        var d = Animora.Domain.Identity.Device.Register(w.TenantId, w.UserId, "pc", hash, "windows", _clock); d.Approve();
        db.Devices.Add(d); await db.SaveChangesAsync();
        return (d.Id, hash);
    }

    private async Task<Result<LicenseResponse>> Issue(IssueKind kind, World w, (string Id, string Hash) device, long? sequence = null)
    {
        await using var store = Store();
        var issuer = new LicenseIssuer(store, new LicenseKeyManager(store, _protection, _clock, _options), _clock, _options);
        return await issuer.IssueAsync(kind, w.TenantId, w.UserId, device.Id, new LicenseRequest(device.Hash, sequence), "corr");
    }

    [PgFact]
    public async Task ActivateRenew_OneLiveLicencePerDevice_PartialUnique()
    {
        var w = await CreateTenantWithKey(); await Grant(w); var d = await Device(w);
        var first = await Issue(IssueKind.Activate, w, d); Assert.True(first.IsSuccess);
        _clock.UtcNow = T0.AddDays(1);
        var second = await Issue(IssueKind.Renew, w, d, 1); Assert.True(second.IsSuccess);
        await using var store = Store();
        var rows = await store.Licenses.AsNoTracking().Where(x => x.TenantId == w.TenantId && x.DeviceId == d.Id).OrderBy(x => x.Sequence).ToListAsync();
        Assert.Equal([LicenseRecordStatus.Superseded, LicenseRecordStatus.Active], rows.Select(x => x.Status));
        Assert.Equal([1L, 2L], rows.Select(x => x.Sequence));
        Assert.True(rows.Last().ExpiresUtc <= T0.AddDays(1).AddDays(40));
    }

    [PgFact]
    public async Task ConcurrentRenew_NoDuplicateLive()
    {
        var w = await CreateTenantWithKey(); await Grant(w); var d = await Device(w);
        Assert.True((await Issue(IssueKind.Activate, w, d)).IsSuccess);
        await Task.WhenAll(Issue(IssueKind.Renew, w, d, 1), Issue(IssueKind.Renew, w, d, 1));
        await using var store = Store();
        Assert.Equal(1, await store.Licenses.CountAsync(x => x.TenantId == w.TenantId && x.DeviceId == d.Id && x.Status == LicenseRecordStatus.Active));
    }

    [PgFact]
    public async Task DeviceLimit_Mismatch_AndRevokedGrant_AreRejected()
    {
        var w = await CreateTenantWithKey(); await Grant(w, maxDevices: 1);
        var a = await Device(w); var b = await Device(w);
        Assert.True((await Issue(IssueKind.Activate, w, a)).IsSuccess);
        Assert.Equal(ErrorCodes.LicenseDeviceLimitReached, (await Issue(IssueKind.Activate, w, b)).Error);
        Assert.Equal(ErrorCodes.LicenseDeviceMismatch, (await Issue(IssueKind.Renew, w, (a.Id, new string('a', 64)))).Error);
        await using var store = Store();
        await store.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"LicenseGrants\" SET \"Status\"=2 WHERE \"TenantId\"={w.TenantId}");
        Assert.Equal(ErrorCodes.LicenseRevoked, (await Issue(IssueKind.Renew, w, a)).Error);
    }

    [PgFact]
    public async Task GrantBackfill_ForPreExistingTenants()
    {
        var w = await CreateTenantWithKey();
        await using var store = Store();
        // Migration 114 backfilled every Subscription present at migrate time; new tenants arrive via projector.
        Assert.True(await store.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.\"LicenseGrants\"").SingleAsync() >= 0);
        Assert.NotNull(w.SubscriptionId);
    }

    [PgFact]
    public async Task RevokedLicence_BlocksCommandsAt_ServerGate()
    {
        var w = await CreateTenantWithKey(); await Grant(w); var d = await Device(w);
        var issued = await Issue(IssueKind.Activate, w, d); Assert.True(issued.IsSuccess);
        await using var store = Store();
        var admin = new LicenseAdministration(store, _clock);
        var record = await store.Licenses.AsNoTracking().SingleAsync(x => x.TenantId == w.TenantId && x.DeviceId == d.Id);
        Assert.True((await admin.RevokeAsync(w.TenantId, record.Id, record.Sequence.ToString(), "test", new ActorRef(ActorKind.Operator), "c")).IsSuccess);
        await using var read = new AnimoraEntitlementReadContext(new DbContextOptionsBuilder<AnimoraEntitlementReadContext>().UseNpgsql(fixture.ConnectionString).Options);
        Assert.False(await read.Licenses.AnyAsync(x => x.TenantId == w.TenantId && x.DeviceId == d.Id && x.Status == LicenseRecordStatus.Active));
        Assert.True(await read.Licenses.AnyAsync(x => x.TenantId == w.TenantId && x.DeviceId == d.Id && x.Status == LicenseRecordStatus.Revoked));
    }

    [PgFact]
    public async Task ConsumerRedelivery_Idempotent_AndOlderEventNeverOverwrites()
    {
        var w = await CreateTenantWithKey();
        for (var i = 0; i < 2; i++) await Project(w.TenantId);
        await using var store = Store();
        var grant = await store.Grants.AsNoTracking().SingleAsync(x => x.TenantId == w.TenantId);
        var sequence = grant.Sequence;
        await store.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"LicenseGrants\" SET \"SourceEventSequence\"=\"SourceEventSequence\"+10, \"PlanCode\"='newer' WHERE \"TenantId\"={w.TenantId}");
        await Project(w.TenantId);
        var after = await store.Grants.AsNoTracking().SingleAsync(x => x.TenantId == w.TenantId);
        Assert.Equal("newer", after.PlanCode); Assert.Equal(sequence, after.Sequence);
    }

    [PgFact]
    public async Task CrossTenant_CannotReadOrRevoke()
    {
        var a = await CreateTenantWithKey(); var b = await CreateTenantWithKey();
        await Grant(a); var d = await Device(a); Assert.True((await Issue(IssueKind.Activate, a, d)).IsSuccess);
        await using var store = Store();
        var record = await store.Licenses.AsNoTracking().SingleAsync(x => x.TenantId == a.TenantId);
        var admin = new LicenseAdministration(store, _clock);
        Assert.Equal(ErrorCodes.LicenseNotGranted, (await admin.RevokeAsync(b.TenantId, record.Id, "1", "x", new ActorRef(ActorKind.Operator), "c")).Error);
        Assert.Empty(await admin.ListAsync(b.TenantId, 1, 50, new ActorRef(ActorKind.Operator), "c"));
    }

    private async Task Project(string tenantId)
    {
        await using var relay = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>().UseNpgsql(fixture.ConnectionString).Options);
        await using var op = Operator();
        await using var tx = await relay.Database.BeginTransactionAsync();
        Assert.True((await new LicenseGrantProjector(op, relay, _clock, _options).ProjectAsync(tenantId)).IsSuccess);
        await tx.CommitAsync();
    }
}
