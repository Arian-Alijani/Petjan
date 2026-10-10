using System.Security.Claims;
using Animora.Domain.Results;
using Animora.Server.Admin;
using Animora.Server.Workers;
using Animora.Web.Admin.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Animora.Server.Tests.Admin;
public sealed class AdminStatusCacheTests
{
    private sealed class Source : IAdminStatusSnapshotSource
    {
        public int Calls;
        public Task<AdminStatus> ReadAsync(CancellationToken ct = default)
        { Calls++; return Task.FromResult(new AdminStatus([], [], new(7, 8, 9, 10), new(1, 12), "test", 42, 0, 0)); }
    }
    private sealed class Authentication(ClaimsPrincipal user) : AuthenticationStateProvider
    { public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user)); }
    [Fact] public async Task OneRefreshRegardlessOfPanelCount()
    {
        var source = new Source(); var services = new ServiceCollection(); services.AddSingleton<IAdminStatusSnapshotSource>(source);
        using var container = services.BuildServiceProvider();
        using var cache = new AdminStatusCache(container.GetRequiredService<IServiceScopeFactory>(), new(TimeProvider.System), TimeProvider.System, NullLogger<AdminStatusCache>.Instance);
        await cache.RefreshOnceAsync();
        var user = AdminAdaptersTests.CookieContext().User;
        ((ClaimsIdentity)user.Identity!).AddClaim(new("op_exp", DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString()));
        var reader = new AdminStatusReader(cache, new Authentication(user), TimeProvider.System);
        for (int i = 0; i < 100; i++) Assert.Equal(7, (await reader.GetStatusAsync()).Value!.Counters.CommandsInProgress);
        Assert.Equal(1, source.Calls);
        await cache.RefreshOnceAsync(); Assert.Equal(2, source.Calls);
    }
    [Fact] public async Task CircuitReader_FailsClosedForTenantIdentityAndExpiredCookie()
    {
        var services = new ServiceCollection(); using var container = services.BuildServiceProvider();
        using var cache = new AdminStatusCache(container.GetRequiredService<IServiceScopeFactory>(), new(TimeProvider.System), TimeProvider.System, NullLogger<AdminStatusCache>.Instance);
        var tenant = new ClaimsPrincipal(new ClaimsIdentity([new("sub", "tenant-user"), new("role", "operator")], "Bearer", ClaimTypes.Name, "role"));
        var denied = await new AdminStatusReader(cache, new Authentication(tenant), TimeProvider.System).GetStatusAsync();
        Assert.Equal(ErrorCodes.Forbidden, denied.Error);
        var cookie = AdminAdaptersTests.CookieContext().User;
        ((ClaimsIdentity)cookie.Identity!).AddClaim(new("op_exp", "1"));
        Assert.Equal(ErrorCodes.Forbidden, (await new AdminStatusReader(cache, new Authentication(cookie), TimeProvider.System).GetStatusAsync()).Error);
    }
}
public sealed class LiveStatusPanelTests
{
    [Fact] public void OnlyMountedOnDashboard_Disposes_TimerAndUsesCachedSnapshot()
    {
        string root = Path.Combine(RepoRoot.Find(), "src/Animora.Web.Admin");
        var mounts = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories).Where(f => File.ReadAllText(f).Contains("<LiveStatusPanel", StringComparison.Ordinal)).ToArray();
        Assert.Single(mounts); Assert.Equal("Dashboard.razor", Path.GetFileName(mounts[0]));
        string panel = File.ReadAllText(Path.Combine(root, "Components/LiveStatusPanel.razor"));
        Assert.Contains("IAsyncDisposable", panel); Assert.Contains("_timer?.Dispose()", panel); Assert.Contains("_stop.CancelAsync()", panel);
        Assert.Contains("IAdminStatusReader", panel); Assert.DoesNotContain("DbContext", panel); Assert.Contains("TimeSpan.FromSeconds(10)", panel);
        string reader = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src/Animora.Server/Admin/AdminStatusReader.cs"));
        string cachedReader = reader[reader.IndexOf("internal sealed class AdminStatusReader", StringComparison.Ordinal)..];
        Assert.Contains("cache.Read()", cachedReader); Assert.DoesNotContain("queries.", cachedReader);
    }
}
