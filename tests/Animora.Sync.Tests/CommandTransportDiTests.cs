using Animora.Application.Commands;
using Animora.Domain.Time;
using Animora.Sync.Custom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G05: the command transport is replaced only when a server address is configured.</summary>
public sealed class CommandTransportDiTests
{
    private static ServiceCollection Base(bool nullFirst, Action<ServerEndpointOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(FeedFixtures.Now));
        if (nullFirst) services.TryAddSingleton<ICommandTransport, NullCommandTransport>();
        services.AddAnimoraHttpSyncEngine(configure);
        if (!nullFirst) services.TryAddSingleton<ICommandTransport, NullCommandTransport>();
        return services;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Configured_ReplacesNullTransport(bool nullFirst)
    {
        using var provider = Base(nullFirst, o => o.BaseAddress = "https://sync.example.com").BuildServiceProvider();

        var single = provider.GetRequiredService<ICommandTransport>();
        Assert.IsType<HttpCommandTransport>(single);
        Assert.Same(single, provider.GetRequiredService<ICommandBatchTransport>()); // one shared instance
        Assert.NotNull(provider.GetRequiredService<CommandIdRemapService>());
        Assert.IsType<ReplicaVersionReader>(provider.GetRequiredService<IReplicaVersionReader>());
    }

    [Fact]
    public void Unconfigured_KeepsNullTransport()
    {
        using var provider = Base(true, null).BuildServiceProvider();
        Assert.IsType<NullCommandTransport>(provider.GetRequiredService<ICommandTransport>());
        Assert.Null(provider.GetService<ICommandBatchTransport>());
        Assert.Null(provider.GetService<CommandIdRemapService>());
    }
}
