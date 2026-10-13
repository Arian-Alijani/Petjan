using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Server.Tests.Resilience;

/// <summary>IMP-05 / IMP-06: bounded admission queues shed with a typed error; authZ store calls have per-operation budgets.</summary>
public sealed class BackpressureTests
{
    [Fact]
    public async Task SyncGate_WaitBudgetExpires_ShedsWithTypedException()
    {
        using var gate = new SemaphoreSyncAdmission(1, TimeSpan.FromMilliseconds(80), maxQueue: 4);
        await using var held = await gate.EnterAsync();

        var ex = await Assert.ThrowsAsync<AdmissionSaturatedException>(async () => { await gate.EnterAsync(); });

        Assert.Equal("wait_timeout", ex.Reason);
        Assert.Equal(AdmissionMetrics.SyncGate, ex.Gate);
    }

    [Fact]
    public async Task SyncGate_FullQueue_RejectsImmediately_WithoutWaiting()
    {
        using var gate = new SemaphoreSyncAdmission(1, TimeSpan.FromSeconds(30), maxQueue: 1);
        await using var held = await gate.EnterAsync();
        Task<IAsyncDisposable> queued = gate.EnterAsync().AsTask(); // takes the only queue slot
        await Task.Delay(50);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<AdmissionSaturatedException>(async () => { await gate.EnterAsync(); });
        watch.Stop();

        Assert.Equal("queue_full", ex.Reason);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "a full queue must shed at once, not wait out the budget");

        await held.DisposeAsync();
        await using var admitted = await queued; // the queued caller is still served once the permit frees
    }

    [Fact]
    public async Task SyncGate_FreePermit_NeverQueues_EvenWithZeroQueue()
    {
        using var gate = new SemaphoreSyncAdmission(2, TimeSpan.FromMilliseconds(50), maxQueue: 0);

        await using var a = await gate.EnterAsync();
        await using var b = await gate.EnterAsync();

        var ex = await Assert.ThrowsAsync<AdmissionSaturatedException>(async () => { await gate.EnterAsync(); });
        Assert.Equal("queue_full", ex.Reason);
    }

    [Fact]
    public async Task SyncGate_ReleasedPermit_IsReusable_AfterShedding()
    {
        using var gate = new SemaphoreSyncAdmission(1, TimeSpan.FromMilliseconds(30), maxQueue: 2);
        IAsyncDisposable held = await gate.EnterAsync();
        await Assert.ThrowsAsync<AdmissionSaturatedException>(async () => { await gate.EnterAsync(); });

        await held.DisposeAsync();

        await using var again = await gate.EnterAsync(); // a shed caller must not leak a permit or queue slot
    }

    [Fact]
    public async Task WorkerGate_StaysUnbounded_AndCallerCancellationStillWorks()
    {
        using var gate = new SemaphoreBackgroundAdmission(1);
        await using var held = await gate.EnterAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await gate.EnterAsync(cts.Token); });
    }

    private static AuthzOperationGuard Guard(int seconds) =>
        new(Options.Create(new ServerOptions { Authz = new AuthzSettings { OperationTimeoutSeconds = seconds } }));

    [Fact]
    public async Task Guard_FastOperation_ReturnsItsValue() =>
        Assert.Equal(42, await Guard(5).RunAsync(_ => Task.FromResult(42), CancellationToken.None));

    [Fact]
    public async Task Guard_SlowOperation_IsCancelledAtTheBudget_AsTypedTimeout()
    {
        var guard = Guard(1);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAsync<AuthzOperationTimeoutException>(() =>
            guard.RunAsync<int>(async token => { await Task.Delay(TimeSpan.FromSeconds(30), token); return 1; }, CancellationToken.None));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "the op must be cut at the budget, not run to the 30 s command timeout");
    }

    [Fact]
    public async Task Guard_ClientAbort_PropagatesAsCancellation_NotAs503()
    {
        var guard = Guard(30);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            guard.RunAsync<int>(async token => { await Task.Delay(TimeSpan.FromSeconds(30), token); return 1; }, cts.Token));
    }

    [Fact]
    public async Task Guard_ValueTaskOverload_Works() =>
        Assert.Equal("ok", await Guard(5).RunAsync(_ => ValueTask.FromResult("ok"), CancellationToken.None));

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void OperationTimeout_IsRangeValidated(int seconds)
    {
        var options = new ServerOptions { DataRootPath = Path.GetTempPath(), Authz = new AuthzSettings { OperationTimeoutSeconds = seconds } };

        var result = new ServerOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Authz:OperationTimeoutSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void MiddlewareMapsAdmissionAndAuthzTimeouts_To503()
    {
        string source = Animora.Server.Tests.Licensing.LicensingSource.Read("src/Animora.Server/Hosting/GlobalExceptionMiddleware.cs");
        Assert.Contains("AdmissionSaturatedException", source, StringComparison.Ordinal);
        Assert.Contains("AuthzOperationTimeoutException", source, StringComparison.Ordinal);
    }
}
