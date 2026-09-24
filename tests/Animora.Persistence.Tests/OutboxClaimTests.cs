using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.Commands;
using Animora.Application.Outbox;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// B02 acceptance: concurrent claims never overlap; a message dead-letters after the max attempts with its
/// last error preserved; the outbox enqueue rides the caller's transaction (no SaveChanges).
/// </summary>
public sealed class OutboxClaimTests
{
    [Fact]
    public async Task Enqueue_DoesNotSave_UntilTheUnitOfWorkCommits()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
            Assert.True((await outbox.EnqueueAsync("TestEvent", new { Value = 1 })).IsSuccess);
            // Nothing committed yet.
            using var fresh = fixture.CreateContext(fixture.TenantId);
            Assert.Equal(0, await fresh.Set<OutboxMessage>().CountAsync());

            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(1, await db.Set<OutboxMessage>().CountAsync());
        }
    }

    [Fact]
    public async Task ConcurrentClaims_NeverOverlap()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedMessagesAsync(fixture, count: 4);

        using var scopeA = fixture.CreateScope();
        using var scopeB = fixture.CreateScope();
        var a = await scopeA.ServiceProvider.GetRequiredService<OutboxClaim>().ClaimAsync(2);
        var b = await scopeB.ServiceProvider.GetRequiredService<OutboxClaim>().ClaimAsync(2);

        Assert.True(a.IsSuccess && b.IsSuccess);
        var idsA = a.Value!.Select(m => m.Id).ToHashSet();
        var idsB = b.Value!.Select(m => m.Id).ToHashSet();
        Assert.Equal(2, idsA.Count);
        Assert.Equal(2, idsB.Count);
        Assert.Empty(idsA.Intersect(idsB));
    }

    [Fact]
    public async Task DeadLettersAfterMaxAttempts_WithLastErrorPreserved()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedMessagesAsync(fixture, count: 1);

        using var scope = fixture.CreateScope();
        var claim = scope.ServiceProvider.GetRequiredService<OutboxClaim>();
        var claimed = (await claim.ClaimAsync(1)).Value!.Single();

        // Drive the message to exhaustion one attempt at a time.
        for (var i = 0; i < RetryPolicy.MaxAttempts; i++)
            Assert.True((await claim.FailAsync(claimed.Id, ErrorCodes.StoreUnavailable, TimeSpan.Zero)).IsSuccess);

        using var check = fixture.CreateContext(fixture.TenantId);
        var row = await check.Set<OutboxMessage>().AsNoTracking().SingleAsync();
        Assert.Equal(OutboxStatus.DeadLettered, row.Status);
        Assert.Equal(ErrorCodes.StoreUnavailable.Code, row.LastErrorCode);
        Assert.Equal(RetryPolicy.MaxAttempts, row.Attempts);
    }

    [Fact]
    public async Task Complete_MarksSucceeded()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedMessagesAsync(fixture, count: 1);

        using var scope = fixture.CreateScope();
        var claim = scope.ServiceProvider.GetRequiredService<OutboxClaim>();
        var claimed = (await claim.ClaimAsync(1)).Value!.Single();
        Assert.True((await claim.CompleteAsync(claimed.Id)).IsSuccess);

        using var check = fixture.CreateContext(fixture.TenantId);
        Assert.Equal(OutboxStatus.Succeeded, (await check.Set<OutboxMessage>().AsNoTracking().SingleAsync()).Status);
    }

    private static async Task SeedMessagesAsync(LocalStoreTestFixture fixture, int count)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        for (var i = 0; i < count; i++)
            db.Add(OutboxMessage.Create("TestEvent", $"{{\"n\":{i}}}", null, fixture.Clock.UtcNow.AddSeconds(i)));
        await db.SaveChangesAsync();
    }
}
