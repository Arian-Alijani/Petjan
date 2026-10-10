using Animora.Client.Services.Sync;
using Animora.Domain.Time;
using Animora.Persistence.Tests;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests.Sync;

public sealed class SyncOrchestrationDiTests
{
    [Fact]
    public async Task RegistersSchedulerAndNullResyncHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FakeClock(DateTimeOffset.UnixEpoch));
        services.AddAnimoraSyncEngine();
        services.AddScoped<Animora.Application.Ports.ISyncCursorStore>(_ => null!);
        services.AddAnimoraSyncOrchestration(o => o.Interval = TimeSpan.FromMinutes(10));

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<NullSyncResyncHandler>(provider.GetRequiredService<ISyncResyncHandler>());
        Assert.Equal(TimeSpan.FromMinutes(10), provider.GetRequiredService<SyncScheduleOptions>().Interval);
        Assert.NotNull(provider.GetRequiredService<SyncOrchestrator>());
        Assert.NotNull(provider.GetRequiredService<SyncSchedulerHostedService>());
    }

    [Fact]
    public async Task ExistingResyncHandler_IsNotOverwrittenByDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FakeClock(DateTimeOffset.UnixEpoch));
        services.AddAnimoraSyncEngine();
        services.AddSingleton<ISyncResyncHandler, RecordingResyncHandler>();
        services.AddAnimoraSyncOrchestration();

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<RecordingResyncHandler>(provider.GetRequiredService<ISyncResyncHandler>());
    }
}
