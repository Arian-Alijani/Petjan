using Animora.Domain.Time;
using Animora.Sync.Abstractions;
using Animora.Application.Sync;
using Animora.Sync.Custom;
using Animora.Sync.Custom.Replica;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Facts for <see cref="SyncCustomServiceCollectionExtensions.AddAnimoraHttpSyncEngine"/>.</summary>
public sealed class SyncCustomDiTests
{
    private static ServiceProvider Build(Action<ServerEndpointOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(FeedFixtures.Now));
        services.AddAnimoraSyncEngine();
        services.AddAnimoraHttpSyncEngine(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Configured_HttpEngineWins()
    {
        using var provider = Build(o => o.BaseAddress = "https://sync.example.com");
        var engine = provider.GetRequiredService<IStateSyncEngine>();

        Assert.IsType<HttpStateSyncEngine>(engine);
        Assert.True(engine.IsConfigured);
        Assert.IsType<SqliteSyncApplyStore>(provider.GetRequiredService<ISyncApplyStore>());
    }

    [Fact]
    public void Unconfigured_NullEngineStays()
    {
        using var provider = Build(null);
        Assert.IsType<NullSyncEngine>(provider.GetRequiredService<IStateSyncEngine>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://sync.example.com")]
    public void InvalidAddress_NullEngineStays(string address)
    {
        using var provider = Build(o => o.BaseAddress = address);
        Assert.IsType<NullSyncEngine>(provider.GetRequiredService<IStateSyncEngine>());
    }

    [Fact]
    public void SyncFeedClient_ResolvesFromContainer()
    {
        using var provider = Build(o => o.BaseAddress = "https://sync.example.com");
        Assert.NotNull(provider.GetRequiredService<SyncFeedClient>());
    }

    [Fact]
    public void Configured_ReplacesBufferingAndNullResync()
    {
        // Orchestration's Null default registered first (TryAdd) and last must both end in the real handler.
        foreach (var nullFirst in new[] { true, false })
        {
            var services = new ServiceCollection();
            services.AddSingleton<IClock>(new FixedClock(FeedFixtures.Now));
            services.AddAnimoraSyncEngine();
            if (nullFirst) services.TryAddSingleton<ISyncResyncHandler, NullSyncResyncHandler>();
            services.AddAnimoraHttpSyncEngine(o => o.BaseAddress = "https://sync.example.com");
            if (!nullFirst) services.TryAddSingleton<ISyncResyncHandler, NullSyncResyncHandler>();
            using var provider = services.BuildServiceProvider();

            Assert.IsType<SqliteSyncApplyStore>(provider.GetRequiredService<ISyncApplyStore>());
            Assert.IsType<ShadowResyncHandler>(provider.GetRequiredService<ISyncResyncHandler>());
            Assert.NotNull(provider.GetRequiredService<ConflictResolver>());
        }
    }

    [Fact]
    public void Unconfigured_KeepsNoOpDefaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(FeedFixtures.Now));
        services.AddAnimoraSyncEngine();
        services.TryAddSingleton<ISyncResyncHandler, NullSyncResyncHandler>();
        services.AddAnimoraHttpSyncEngine();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<BufferingSyncApplyStore>(provider.GetRequiredService<ISyncApplyStore>());
        Assert.IsType<NullSyncResyncHandler>(provider.GetRequiredService<ISyncResyncHandler>());
    }
}
