using Animora.Contracts.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Desktop.Pages.Dashboard;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>D28: loading / empty / stale / error are distinct, and a real zero never precedes the first success.</summary>
public sealed class DashboardViewStateTests
{
    private static DashboardSnapshot Empty() => new(UserRole.Manager, "", "", [], "", [], "", []);

    private static DashboardSnapshot WithRow() => new(UserRole.Manager, "", "", [],
        "p", [new DashboardListItem("A", "t", "s", "tag", DashboardTone.Neutral)], "s", []);

    [Fact]
    public void StartupPending_IsInitialLoading_NotZero()
    {
        Assert.Equal(DashboardViewState.InitialLoading, DashboardViewStateResolver.Resolve(false, true, false, null));
        Assert.Equal(DashboardViewState.InitialLoading, DashboardViewStateResolver.Resolve(false, false, false, null));
    }

    [Fact]
    public void SuccessfulEmptyStore_IsEmpty_AndSuccessfulWithRows_IsReady()
    {
        Assert.Equal(DashboardViewState.Empty, DashboardViewStateResolver.Resolve(true, false, false, Empty()));
        Assert.Equal(DashboardViewState.Ready, DashboardViewStateResolver.Resolve(true, false, false, WithRow()));
    }

    [Fact]
    public void RefreshFailureWithData_IsStale_InitialFailureIsError()
    {
        Assert.Equal(DashboardViewState.Stale, DashboardViewStateResolver.Resolve(true, false, true, WithRow()));
        Assert.Equal(DashboardViewState.Error, DashboardViewStateResolver.Resolve(false, false, true, null));
    }

    [Fact]
    public void RefreshInProgressWithData_IsRefreshing_AndRetryAfterErrorIsLoading()
    {
        Assert.Equal(DashboardViewState.Refreshing, DashboardViewStateResolver.Resolve(true, true, false, WithRow()));
        Assert.Equal(DashboardViewState.InitialLoading, DashboardViewStateResolver.Resolve(false, true, true, null));
    }

    [Fact]
    public void EveryNonReadyStateHasUserText_AndReadyHasNone()
    {
        foreach (var s in Enum.GetValues<DashboardViewState>())
            Assert.Equal(s == DashboardViewState.Ready, string.IsNullOrEmpty(DashboardViewStateResolver.Describe(s)));
    }

    [Fact]
    public async Task Coordinator_SignalsSuccess_OnlyAfterPublish_AndNotOnFailure()
    {
        var events = new List<string>();
        var fail = false;
        var c = new DashboardRefreshCoordinator<DashboardSnapshot>(
            (_, _) => fail ? throw new InvalidOperationException() : Task.FromResult(WithRow()),
            s => s, _ => events.Add("publish"), _ => events.Add("fail"), () => events.Add("ok"));
        await c.RefreshAsync(force: true);
        fail = true;
        await c.RefreshAsync(force: true);
        Assert.Equal(["publish", "ok", "fail"], events);
        Assert.NotNull(c.LastUpdated);
    }

    [Fact]
    public void Views_ExposeLoadingAndEmptyTemplates()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "../../../../../src/Animora.Desktop/Pages/Dashboard/");
        var page = File.ReadAllText(Path.Combine(root, "DashboardPageView.axaml"));
        Assert.Contains("IsStateVisible", page);
        Assert.Contains("StateText", page);
        foreach (var f in new[] { "ManagerDashboardView.axaml", "DoctorDashboardView.axaml", "CashierDashboardView.axaml" })
        {
            var x = File.ReadAllText(Path.Combine(root, f));
            Assert.Contains("ShowPrimaryEmpty", x);
            Assert.Contains("ShowSecondaryEmpty", x);
        }
    }
}
