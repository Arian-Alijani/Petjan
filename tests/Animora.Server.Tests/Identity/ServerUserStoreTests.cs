using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Identity;
using Xunit;

namespace Animora.Server.Tests.Identity;

public sealed class ServerUserStoreTests
{
    [Fact]
    public async Task Lockout_After5Failures_For15Min()
    {
        var h = new AuthHarness();
        var store = new ServerUserStore(h.Repository, h.Clock);
        ServerIdentityUser adapter = ServerUserStore.ToAdapter(h.User);

        for (int i = 1; i <= User.MaxFailedAttempts - 1; i++)
        {
            Assert.Equal(i, await store.IncrementAccessFailedCountAsync(adapter, CancellationToken.None));
            Assert.False(h.User.IsLockedOut(h.Clock.UtcNow));
        }

        Assert.Equal(User.MaxFailedAttempts, await store.IncrementAccessFailedCountAsync(adapter, CancellationToken.None));

        Assert.True(h.User.IsLockedOut(h.Clock.UtcNow));
        Assert.Equal(h.Clock.UtcNow + TimeSpan.FromMinutes(15), adapter.LockoutEnd);
        Assert.Equal(User.LockoutDuration, TimeSpan.FromMinutes(15));

        // Identity resets the counter right after locking; that must never lift the aggregate's lockout.
        await store.ResetAccessFailedCountAsync(adapter, CancellationToken.None);
        Assert.True(h.User.IsLockedOut(h.Clock.UtcNow));

        h.Clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(h.User.IsLockedOut(h.Clock.UtcNow));

        h.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(h.User.IsLockedOut(h.Clock.UtcNow));

        await store.ResetAccessFailedCountAsync(adapter, CancellationToken.None);
        Assert.Equal(0, h.User.FailedAttemptCount);
        Assert.Null(h.User.LockoutEndUtc);
    }

    [Fact]
    public async Task Create_And_Delete_AreNotSupported_AccountsAreProvisionedServerSide()
    {
        var h = new AuthHarness();
        var store = new ServerUserStore(h.Repository, h.Clock);
        ServerIdentityUser adapter = ServerUserStore.ToAdapter(h.User);

        Assert.False((await store.CreateAsync(adapter, CancellationToken.None)).Succeeded);
        Assert.False((await store.DeleteAsync(adapter, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task FindByName_IsCaseInsensitive_AndProjectsAggregate()
    {
        var h = new AuthHarness();
        var store = new ServerUserStore(h.Repository, h.Clock);

        ServerIdentityUser? found = await store.FindByNameAsync("ALICE", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(h.User.Id, found.Id);
        Assert.Equal(h.User.SecurityStamp, found.SecurityStamp);
        Assert.Equal(h.TenantId, found.TenantId);
    }
}
