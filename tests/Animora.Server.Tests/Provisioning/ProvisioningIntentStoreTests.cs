using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Provisioning;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Animora.Server.Tests.Provisioning;

public sealed class ProvisioningIntentStoreTests(PgFixture fixture) : IClassFixture<PgFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private ProvisioningDbContext Context() => new(new DbContextOptionsBuilder<ProvisioningDbContext>().UseNpgsql(fixture.ConnectionString).Options);
    private async Task Migrate()
    {
        await using var db = new AnimoraServerDbContext(new DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(fixture.ConnectionString).Options, new NullTenantContext());
        await db.Database.MigrateAsync();
    }
    private async Task<ProvisioningIntent> Insert(ProvisioningDbContext db)
    {
        var client = new WebsiteClient { Id = IdGenerator.NewId(), KeyId = IdGenerator.NewId(), Label = "Regression", ProtectedSecret = [1], CreatedUtc = Now };
        db.Clients.Add(client); await db.SaveChangesAsync();
        var row = new ProvisioningIntent { Id = IdGenerator.NewId(), IdempotencyKey = IdGenerator.NewId(), ClientId = client.Id,
            RequestFingerprint = new string('a', 64), PayloadProtected = [1], NextAttemptUtc = Now, ReceivedUtc = Now };
        Assert.True((await new ProvisioningIntentStore(db).InsertAsync(row)).IsSuccess); return row;
    }
    [PgFact]
    public async Task DuplicateFingerprint_Conflict_AndClientIsolation()
    {
        await Migrate(); await using var db = Context(); var row = await Insert(db); var store = new ProvisioningIntentStore(db);
        Assert.Equal(row.Id, (await store.InsertAsync(row)).Value?.IntentId);
        row.RequestFingerprint = new string('b', 64);
        Assert.Equal(ErrorCodes.ProvisioningKeyConflict, (await store.InsertAsync(row)).Error);
        Assert.Equal(ErrorCodes.ProvisioningIntentNotFound, (await store.PollAsync(row.IdempotencyKey, "other", Now)).Error);
    }
    [PgFact]
    public async Task PasswordReturnedOnce_ExpiryAndRetentionWipe()
    {
        await Migrate(); await using var db = Context(); var row = await Insert(db);
        await db.Intents.Where(x => x.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ProvisioningIntentStatus.Completed)
            .SetProperty(x => x.ProtectedTempPassword, new byte[] { 42 }).SetProperty(x => x.TempPasswordExpiresUtc, Now.AddHours(72)));
        var store = new ProvisioningIntentStore(db);
        Assert.NotNull((await store.PollAsync(row.IdempotencyKey, row.ClientId, Now)).Value?.ProtectedTempPassword);
        Assert.Null((await store.PollAsync(row.IdempotencyKey, row.ClientId, Now)).Value?.ProtectedTempPassword);
        await store.RetainAsync(Now.AddDays(8));
        Assert.Null(await db.Intents.Where(x => x.Id == row.Id).Select(x => x.PayloadProtected).SingleAsync());
    }
    [PgFact]
    public async Task ExpiredLease_Reclaimable_AndStaleCompletionRejected()
    {
        await Migrate(); await using var db = Context(); var row = await Insert(db); var store = new ProvisioningIntentStore(db);
        var claims = await store.ClaimAsync(Now, 5); var first = Assert.Single(claims.Where(x => x.Id == row.Id));
        await using var secondDb = Context(); var secondStore = new ProvisioningIntentStore(secondDb);
        Assert.DoesNotContain(await secondStore.ClaimAsync(Now, 5), x => x.Id == row.Id);
        var second = Assert.Single((await secondStore.ClaimAsync(Now.AddSeconds(61), 5)).Where(x => x.Id == row.Id));
        Assert.NotEqual(first.ClaimToken, second.ClaimToken);
        Assert.False(await store.CompleteAsync(first, "{}", null, Now.AddSeconds(61)));
        Assert.True(await secondStore.CompleteAsync(second, "{}", null, Now.AddSeconds(61)));
    }
}
