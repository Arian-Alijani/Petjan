using System.Diagnostics.Metrics;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Observability;
using Xunit;

namespace Animora.Server.Tests.Observability;

/// <summary>IMP-04: the six scaling signals are observable through the server/admission meters.</summary>
public sealed class ScalingSignalsTests
{
    private static List<(string Name, double Value, string? Tag)> Collect(params string[] meters)
    {
        var seen = new List<(string, double, string?)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (meters.Contains(instrument.Meter.Name, StringComparer.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((i, v, tags, _) => seen.Add((i.Name, v, Tag(tags))));
        listener.SetMeasurementEventCallback<int>((i, v, tags, _) => seen.Add((i.Name, v, Tag(tags))));
        listener.SetMeasurementEventCallback<long>((i, v, tags, _) => seen.Add((i.Name, v, Tag(tags))));
        listener.Start();
        listener.RecordObservableInstruments();
        return seen;
    }

    private static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags) => tags.Length == 0 ? null : tags[0].Value?.ToString();

    [Fact]
    public async Task AdmissionGates_ReportWaitTimeAndOccupancy_PerGateLabel()
    {
        using var worker = new SemaphoreBackgroundAdmission(1);
        using var sync = new SemaphoreSyncAdmission(2);
        var recorded = new List<(string Gate, double Seconds)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (i, l) =>
            {
                if (i.Meter.Name == AdmissionMetrics.MeterName && i.Name == "animora.admission.wait.seconds")
                {
                    l.EnableMeasurementEvents(i);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((i, v, tags, _) => recorded.Add((tags[0].Value!.ToString()!, v)));
        listener.Start();

        await using (await worker.EnterAsync())
        await using (await sync.EnterAsync())
        {
            var inUse = Collect(AdmissionMetrics.MeterName).Where(m => m.Name == "animora.admission.in_use").ToList();
            Assert.Contains(inUse, m => m.Tag == AdmissionMetrics.WorkerGate && m.Value >= 1);
            Assert.Contains(inUse, m => m.Tag == AdmissionMetrics.SyncGate && m.Value >= 1);
        }

        Assert.Contains(recorded, r => r.Gate == AdmissionMetrics.WorkerGate);
        Assert.Contains(recorded, r => r.Gate == AdmissionMetrics.SyncGate);
    }

    [Fact]
    public async Task AdmissionGate_BlockedCaller_ShowsAsWaiting()
    {
        using var gate = new SemaphoreBackgroundAdmission(1);
        await using var held = await gate.EnterAsync();
        var waiter = gate.EnterAsync().AsTask();
        await Task.Delay(50);

        var waiting = Collect(AdmissionMetrics.MeterName).Where(m => m.Name == "animora.admission.waiting" && m.Tag == AdmissionMetrics.WorkerGate);
        Assert.Contains(waiting, m => m.Value >= 1);

        await held.DisposeAsync();
        await using var second = await waiter;
    }

    [Fact]
    public void BacklogGauges_OutboxLagAndIntakeRecovery_AreExposed()
    {
        using var metrics = new ServerMetrics();
        metrics.SetBacklogs(12.5, 7);

        var m = Collect(ServerMetrics.MeterName);

        Assert.Contains(m, x => x.Name == "animora.outbox.lag.seconds" && x.Value == 12.5);
        Assert.Contains(m, x => x.Name == "animora.intake.recovery.backlog" && x.Value == 7);
    }

    [Fact]
    public void PoolSaturation_IsUsedOverMax_PerSource()
    {
        using var metrics = new ServerMetrics();
        metrics.ConfigurePoolLimits(
            new Dictionary<string, int> { ["Animora.Server/oltp-write"] = 6, ["Animora.Server/worker"] = 2 },
            () => new Dictionary<string, int> { ["Animora.Server/oltp-write"] = 3, ["Animora.Server/worker"] = 5 });

        var sat = Collect(ServerMetrics.MeterName).Where(x => x.Name == "animora.pg.pool.saturation").ToList();

        Assert.Contains(sat, x => x.Tag == "Animora.Server/oltp-write" && Math.Abs(x.Value - 0.5) < 1e-9);
        Assert.Contains(sat, x => x.Tag == "Animora.Server/worker" && x.Value == 1.0); // clamped to 1
    }

    [Fact]
    public void AuthzQueries_CountAndLatency_Accumulate()
    {
        using var metrics = new ServerMetrics();
        metrics.RecordAuthzQuery(TimeSpan.FromMilliseconds(200));
        metrics.RecordAuthzQuery(TimeSpan.FromMilliseconds(300));

        var m = Collect(ServerMetrics.MeterName);

        Assert.Equal(2, metrics.AuthzQueries);
        Assert.Contains(m, x => x.Name == "animora.authz.queries" && x.Value == 2);
        Assert.Contains(m, x => x.Name == "animora.authz.duration.seconds.total" && Math.Abs(x.Value - 0.5) < 1e-9);
    }

    [Fact]
    public void ReportingBudget_WaitInUseAndTimeouts_AreExposed()
    {
        using var metrics = new ServerMetrics();
        using var budget = new QueryBudget(new NoSessions(), reportingMax: 3);
        metrics.ObserveReportingBudget(budget);

        var stats = Collect(ServerMetrics.MeterName).Where(x => x.Name == "animora.reporting.budget").Select(x => x.Tag).ToList();

        Assert.Contains("in_use", stats);
        Assert.Contains("max", stats);
        Assert.Contains("wait_seconds_total", stats);
        Assert.Contains("timeouts_total", stats);
    }

    private sealed class NoSessions : Animora.Persistence.PostgreSql.Options.IReportingQuerySessionFactory
    {
        public Task<Animora.Persistence.PostgreSql.Options.ReportingQuerySession> OpenAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
