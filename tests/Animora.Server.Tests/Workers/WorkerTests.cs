using System.Reflection;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Hints;
using Animora.Server.Tests.Resilience;
using Animora.Server.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Animora.Server.Tests.Workers;

public sealed class WorkerHeartbeatTests
{
    [Fact]
    public async Task Stale_ReportsDegradedByNameOnly()
    {
        var time = new MutableTimeProvider(); var heartbeat = new WorkerHeartbeat(time);
        heartbeat.Register("outbox-relay", TimeSpan.FromSeconds(1)); time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(new[] { "outbox-relay" }, heartbeat.StaleNames());
        var probe = new WorkersReadinessProbe(heartbeat);
        Assert.False(probe.IsRequired); Assert.False((await probe.CheckAsync(CancellationToken.None)).IsHealthy);
        heartbeat.Success("outbox-relay"); Assert.Empty(heartbeat.StaleNames());
    }
}
public sealed class WorkerLoopTests
{
    private sealed class ThrowOnceWorker(WorkerHeartbeat heartbeat) : ResilientWorker("test-worker", TimeSpan.FromMilliseconds(2),
        TimeProvider.System, heartbeat, NullLogger.Instance)
    {
        private int _calls;
        public TaskCompletionSource Recovered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task TickAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == 1) throw new InvalidOperationException("first_tick");
            Recovered.TrySetResult(); return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task Exception_DoesNotKillLoop_RateLimitedLog()
    {
        var time = new MutableTimeProvider(); var gate = new WorkerFailureLogGate(time);
        Assert.True(gate.ShouldLog("failure")); Assert.False(gate.ShouldLog("failure"));
        time.Advance(TimeSpan.FromMinutes(1)); Assert.True(gate.ShouldLog("failure"));
        using var worker = new ThrowOnceWorker(new WorkerHeartbeat(TimeProvider.System));
        await worker.StartAsync(CancellationToken.None);
        await worker.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);
    }
}
public sealed class PoolBudgetTests
{
    [Fact]
    public void SumIncludesWorker_Within12_And20()
    {
        var o = new DatabaseOptions(); var validator = new DatabaseOptionsValidator();
        Assert.Equal(12, o.OltpWriteMax + o.OltpReadMax + o.KeyRingMax + o.WorkerMax);
        Assert.True(validator.Validate(null, o).Succeeded);
        o.WorkerMax++; Assert.True(validator.Validate(null, o).Failed);
        o.ProfileGiB = 4; o.WorkerMax = 10; Assert.True(validator.Validate(null, o).Succeeded);
        o.WorkerMax = 11; Assert.True(validator.Validate(null, o).Failed);
        o.WorkerMax = 0; Assert.True(validator.Validate(null, o).Failed);
    }
}
public sealed class HintHubTests
{
    [Fact]
    public void GroupIsServerChosen_ClientCannotJoinOtherTenant()
    {
        Assert.NotEqual(SyncHintHub.Group("A"), SyncHintHub.Group("B"));
        var methods = typeof(SyncHintHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Single(methods); Assert.Equal("OnConnectedAsync", methods[0].Name);
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src/Animora.Server/Hints/SyncHintHub.cs"));
        Assert.Contains("tenant.TenantId", source); Assert.DoesNotContain("Context.GetHttpContext()?.Request.Query", source);
    }
}
