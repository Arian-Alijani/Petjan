using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Facts for <see cref="SyncAbstractionsServiceCollectionExtensions.AddAnimoraSyncEngine"/>.</summary>
public sealed class SyncDiTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class FakeEngine : IStateSyncEngine
    {
        public bool IsConfigured => true;

        public Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default) =>
            Task.FromResult(Result<SyncResult>.Ok(SyncResult.UpToDate(1, DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public void AddAnimoraSyncEngine_RegistersNullEngine_AndRealRegistrationWins()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock, FakeClock>();
        services.AddAnimoraSyncEngine();
        using (var provider = services.BuildServiceProvider())
            Assert.IsType<NullSyncEngine>(provider.GetRequiredService<IStateSyncEngine>());

        services.AddSingleton<IStateSyncEngine, FakeEngine>();
        using var overridden = services.BuildServiceProvider();
        Assert.IsType<FakeEngine>(overridden.GetRequiredService<IStateSyncEngine>());
    }
}
