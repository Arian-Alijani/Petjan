using Animora.Client.Services.Dashboard;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Desktop.Pages.Dashboard;
using Animora.Domain.Identity;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>Cancellation + latest-wins lifecycle (D25/D37). Controlled tasks and barriers only: no sleeps.</summary>
public sealed class DashboardLifecycleTests
{
    private static DashboardSnapshot Snap(string tag) =>
        new(UserRole.Manager, tag, tag, [], tag, [], tag, []);

    private sealed class Probe
    {
        public readonly List<(TaskCompletionSource<DashboardSnapshot> Result, CancellationToken Token, bool Force)> Calls = [];
        public readonly List<string> Applied = [];
        public readonly List<Exception> Failures = [];
        public DashboardRefreshCoordinator<DashboardSnapshot> Coordinator { get; }

        public Probe() => Coordinator = new((ct, force) =>
        {
            var tcs = new TaskCompletionSource<DashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            Calls.Add((tcs, ct, force));
            return tcs.Task;
        }, s => s, s => Applied.Add(s.HeroTitle), Failures.Add);
    }

    [Fact]
    public async Task ReturnDuringLoading_SupersedesOldLoad_OnlyLatestAppliesAndBusyStaysTrueUntilItEnds()
    {
        var p = new Probe();
        var first = p.Coordinator.ActivateAsync();
        var second = p.Coordinator.ActivateAsync();
        Assert.Equal(2, p.Calls.Count);
        Assert.True(p.Calls[0].Token.IsCancellationRequested);
        await first; // the superseded load ends quietly
        Assert.True(p.Coordinator.IsBusy); // old load's finally must not clear the new load's busy flag
        Assert.Empty(p.Applied);
        p.Calls[1].Result.SetResult(Snap("latest"));
        await second;
        Assert.False(p.Coordinator.IsBusy);
        Assert.Equal(["latest"], p.Applied);
        Assert.Empty(p.Failures);
    }

    [Fact]
    public async Task LateResultOfSupersededLoad_IsDiscarded_EvenWhenItCompletesSuccessfully()
    {
        var p = new Probe();
        var first = p.Coordinator.ActivateAsync();
        var oldCall = p.Calls[0];
        var second = p.Coordinator.ActivateAsync();
        oldCall.Result.TrySetResult(Snap("stale")); // loses the race against cancellation or arrives late
        p.Calls[1].Result.SetResult(Snap("fresh"));
        await Task.WhenAll(first, second);
        Assert.Equal(["fresh"], p.Applied);
    }

    [Fact]
    public async Task Deactivate_CancelsInFlight_ResultNeverApplied_NoFailure_AndBusyClears()
    {
        var p = new Probe();
        var load = p.Coordinator.ActivateAsync();
        var states = 0;
        p.Coordinator.StateChanged += () => states++;
        p.Coordinator.Deactivate();
        Assert.True(p.Calls[0].Token.IsCancellationRequested);
        Assert.False(p.Coordinator.IsBusy);
        p.Calls[0].Result.TrySetResult(Snap("after-leave"));
        await load;
        Assert.Empty(p.Applied);
        Assert.Empty(p.Failures);
        Assert.True(states >= 1);
        // Re-entering works and is a fresh generation.
        var again = p.Coordinator.ActivateAsync();
        p.Calls[1].Result.SetResult(Snap("again"));
        await again;
        Assert.Equal(["again"], p.Applied);
    }

    [Fact]
    public async Task ConcurrentRefreshTriggers_ShareOneQuery()
    {
        var p = new Probe();
        var a = p.Coordinator.RefreshAsync();
        var b = p.Coordinator.RefreshAsync();
        var c = p.Coordinator.RefreshAsync();
        Assert.Single(p.Calls);
        p.Calls[0].Result.SetResult(Snap("one"));
        await Task.WhenAll(a, b, c);
        Assert.Equal(["one"], p.Applied);
    }

    [Fact]
    public async Task ForcedRefreshDuringLoad_IsRetained_AsExactlyOneForcedRerun()
    {
        var p = new Probe();
        var load = p.Coordinator.RefreshAsync();
        var forced1 = p.Coordinator.RefreshAsync(force: true);
        var forced2 = p.Coordinator.RefreshAsync(force: true);
        Assert.Single(p.Calls);
        p.Calls[0].Result.SetResult(Snap("first"));
        await Task.Yield();
        SpinWait.SpinUntil(() => p.Calls.Count == 2, TimeSpan.FromSeconds(5));
        Assert.True(p.Calls[1].Force);
        p.Calls[1].Result.SetResult(Snap("forced"));
        await Task.WhenAll(load, forced1, forced2);
        Assert.Equal(["first", "forced"], p.Applied);
        Assert.Equal(2, p.Calls.Count);
    }

    [Fact]
    public async Task CacheHit_SameSnapshotInstance_IsNotRepublished()
    {
        var p = new Probe();
        var shared = Snap("same");
        var one = p.Coordinator.RefreshAsync();
        p.Calls[0].Result.SetResult(shared);
        await one;
        var two = p.Coordinator.RefreshAsync();
        p.Calls[1].Result.SetResult(shared);
        await two;
        Assert.Equal(["same"], p.Applied);
    }

    [Fact]
    public async Task NonCancellationFailure_ReportsOnlyForCurrentGeneration()
    {
        var p = new Probe();
        var load = p.Coordinator.ActivateAsync();
        p.Calls[0].Result.SetException(new InvalidOperationException("SESSION_EXPIRED"));
        await load;
        Assert.Single(p.Failures);
        Assert.False(p.Coordinator.IsBusy);
        var stale = p.Coordinator.ActivateAsync();
        var current = p.Coordinator.ActivateAsync();
        p.Calls[1].Result.TrySetException(new InvalidOperationException("late"));
        p.Calls[2].Result.SetResult(Snap("ok"));
        await Task.WhenAll(stale, current);
        Assert.Single(p.Failures); // the superseded load's late failure is ignored
        Assert.Equal(["ok"], p.Applied);
    }

    [Fact]
    public async Task IdentityReset_DropsLateResponseOfPreviousIdentity_AndNextLoadAlwaysPublishes()
    {
        var p = new Probe();
        var doctor = p.Coordinator.ActivateAsync();
        p.Coordinator.Reset(); // the signed-in identity changed while the doctor's load was in flight
        Assert.False(p.Coordinator.IsBusy);
        p.Calls[0].Result.TrySetResult(Snap("doctor-data"));
        await doctor;
        Assert.Empty(p.Applied);
        Assert.Null(p.Coordinator.Current);
        var receptionist = p.Coordinator.ActivateAsync();
        p.Calls[1].Result.SetResult(Snap("receptionist-data"));
        await receptionist;
        Assert.Equal(["receptionist-data"], p.Applied);
    }

    [Fact]
    public async Task IdentityReset_ForgetsPublishedSnapshot_SoAnIdenticalInstanceIsPublishedAgain()
    {
        var p = new Probe();
        var shared = Snap("shared");
        var first = p.Coordinator.RefreshAsync();
        p.Calls[0].Result.SetResult(shared);
        await first;
        p.Coordinator.Reset();
        var second = p.Coordinator.RefreshAsync();
        p.Calls[1].Result.SetResult(shared);
        await second;
        Assert.Equal(["shared", "shared"], p.Applied);
    }

    [Fact]
    public async Task ActiveTicks_AreMinuteBounded_Coalesced_AndStopOnExit()
    {
        var p = new Probe();
        var activation = p.Coordinator.ActivateAsync();
        p.Calls[0].Result.SetResult(Snap("initial"));
        await activation;
        var midnight = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var tick = p.Coordinator.TickAsync(midnight);
        var repeated = p.Coordinator.TickAsync(midnight.AddSeconds(5));
        Assert.Equal(2, p.Calls.Count);
        Assert.True(p.Calls[1].Force);
        p.Calls[1].Result.SetResult(Snap("next-day"));
        await Task.WhenAll(tick, repeated);
        p.Coordinator.Deactivate();
        await p.Coordinator.TickAsync(midnight.AddHours(2));
        Assert.Equal(2, p.Calls.Count);
    }

    [Fact]
    public async Task ManualRefresh_BypassesWarmDataCache_ButStillAuthorizes()
    {
        var sql = new DashboardAuthorizationTests.SqlRecorder();
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Manager, sql);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var initial = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.Same(initial, await service.GetSnapshotAsync(UserRole.Manager));
        Assert.NotSame(initial, await service.RefreshSnapshotAsync(UserRole.Manager));
    }

    [Theory]
    [InlineData("SESSION_EXPIRED", DashboardFailureKind.SessionExpired, false)]
    [InlineData("PERMISSION_DENIED", DashboardFailureKind.PermissionDenied, false)]
    [InlineData("STORE_UNAVAILABLE", DashboardFailureKind.StoreUnavailable, true)]
    [InlineData("UNEXPECTED", DashboardFailureKind.Unexpected, false)]
    [InlineData("private owner/financial detail", DashboardFailureKind.Unexpected, false)]
    public void Failures_AreTypedSafe_AndOnlyRecoverableOnesOfferRetry(string code, DashboardFailureKind kind, bool retry)
    {
        var error = new DashboardQueryException(DashboardQueryException.Classify(code), "opaque-correlation");
        Assert.Equal(kind, error.Kind);
        Assert.Equal(retry, error.CanRetry);
        Assert.Equal("opaque-correlation", error.CorrelationId);
        Assert.DoesNotContain(code, error.UserMessage);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task ExpiredSession_ServiceEmitsSafeTypedFailureWithCorrelation()
    {
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Manager);
        host.Cache.Set(null);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var error = await Assert.ThrowsAsync<DashboardQueryException>(() => service.GetSnapshotAsync(UserRole.Manager));
        Assert.Equal(DashboardFailureKind.SessionExpired, error.Kind);
        Assert.NotEmpty(error.CorrelationId);
        Assert.False(error.CanRetry);
    }

    // ---- service: token reaches the semaphore and the database --------------------------------------------

    private sealed class Gatekeeper : DbCommandInterceptor
    {
        private readonly ManualResetEventSlim _release = new(true);
        public readonly ManualResetEventSlim Entered = new(false);
        public void Arm() { Entered.Reset(); _release.Reset(); }
        public void Open() => _release.Set();
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result)
        {
            Entered.Set();
            _release.Wait(TimeSpan.FromSeconds(20));
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Entered.Set();
            _release.Wait(TimeSpan.FromSeconds(20));
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Service_PreCancelledToken_ThrowsOce_WithoutReadingTheStore()
    {
        var sql = new DashboardAuthorizationTests.SqlRecorder();
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Manager, sql);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        sql.Commands.Clear();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetSnapshotAsync(UserRole.Manager, cts.Token));
        Assert.Empty(sql.Commands);
    }

    [Fact]
    public async Task Service_CancelWhileWaitingForGate_DoesNotOverReleaseTheSemaphore()
    {
        var gate = new Gatekeeper();
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Manager, gate);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        gate.Arm();
        var running = service.GetSnapshotAsync(UserRole.Manager);
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(20)));
        using var cts = new CancellationTokenSource();
        var waiting = service.GetSnapshotAsync(UserRole.Doctor, cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        gate.Open();
        Assert.NotNull(await running);
        // If the cancelled waiter had released a slot it never took, two loads would now overlap. Prove they serialize:
        gate.Arm();
        var a = service.GetSnapshotAsync(UserRole.Manager, default);
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(20)));
        var b = service.GetSnapshotAsync(UserRole.Manager, default);
        // b is blocked behind a's semaphore, so it cannot have produced a result yet.
        Assert.False(b.IsCompleted);
        gate.Open();
        Assert.NotNull(await a);
        Assert.NotNull(await b);
    }

    [Fact]
    public async Task Service_CancelDuringQuery_ThrowsOce_NotAFailureOutcome()
    {
        var gate = new Gatekeeper();
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Manager, gate);
        // Different day-key content so the call below cannot be a cache hit.
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        gate.Arm();
        using var cts = new CancellationTokenSource();
        var call = service.GetSnapshotAsync(UserRole.Manager, cts.Token);
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(20)));
        await cts.CancelAsync();
        gate.Open();
        var ex = await Record.ExceptionAsync(() => call);
        Assert.True(ex is OperationCanceledException, ex?.ToString() ?? "completed without cancellation");
    }
}
