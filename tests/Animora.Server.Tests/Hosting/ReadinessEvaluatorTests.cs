using Animora.Server.Hosting;
using Animora.Server.Observability;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Server.Tests.Hosting;

/// <summary>BUG-HEALTH-01 / IMP-10: cached, single-flight readiness + latched startup state.</summary>
public sealed class ReadinessEvaluatorTests
{
    private sealed class CountingProbe(string name, bool required = true) : IReadinessProbe
    {
        public int Calls;
        public bool Healthy = true;
        public string Name => name;
        public bool IsRequired => required;

        public async Task<ReadinessResult> CheckAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(20, ct);
            return Healthy ? ReadinessResult.Healthy() : ReadinessResult.Unhealthy();
        }
    }

    private static ReadinessEvaluator Create(int ttlSeconds, params IReadinessProbe[] probes) =>
        new(probes, new ReadinessSnapshot(), Options.Create(new ServerOptions { Health = new HealthSettings { ReadinessCacheSeconds = ttlSeconds } }));

    [Fact]
    public async Task FrequentPolling_RunsOneProbeRoundPerTtl()
    {
        var probe = new CountingProbe("database");
        var evaluator = Create(30, probe);

        for (int i = 0; i < 25; i++)
        {
            Assert.True((await evaluator.EvaluateAsync(CancellationToken.None)).IsReady);
        }

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareASingleEvaluation()
    {
        var probe = new CountingProbe("database");
        var evaluator = Create(30, probe);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => evaluator.EvaluateAsync(CancellationToken.None)));

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task ZeroTtl_DisablesCaching()
    {
        var probe = new CountingProbe("database");
        var evaluator = Create(0, probe);

        await evaluator.EvaluateAsync(CancellationToken.None);
        await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task Report_SeparatesRequiredFailuresFromOptionalDegradation()
    {
        var evaluator = Create(0,
            new CountingProbe("database") { Healthy = false },
            new CountingProbe("workers", required: false) { Healthy = false },
            new CountingProbe("keyring"));

        var report = await evaluator.EvaluateAsync(CancellationToken.None);

        Assert.False(report.IsReady);
        Assert.Equal(["database"], report.Failed);
        Assert.Equal(["workers"], report.Degraded);
    }

    [Fact]
    public async Task Started_LatchesAfterFirstHealthyRound_AndSurvivesLaterFailure()
    {
        var probe = new CountingProbe("database") { Healthy = false };
        var evaluator = Create(0, probe);

        await evaluator.EvaluateAsync(CancellationToken.None);
        Assert.False(evaluator.Started);

        probe.Healthy = true;
        await evaluator.EvaluateAsync(CancellationToken.None);
        Assert.True(evaluator.Started);

        probe.Healthy = false;
        Assert.False((await evaluator.EvaluateAsync(CancellationToken.None)).IsReady);
        Assert.True(evaluator.Started); // startup never regresses; only readiness flaps
    }
}
