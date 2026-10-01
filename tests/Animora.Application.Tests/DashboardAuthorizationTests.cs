using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Dashboard;
using Animora.Application.UseCases.Identity;
using Animora.Client.Services.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Domain.Identity;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>Real local session, dispatcher, stored membership/roles and SQLite; no authorization double.</summary>
public sealed class DashboardAuthorizationTests
{
    [Theory]
    [InlineData(StaffRole.Doctor, UserRole.Doctor)]
    [InlineData(StaffRole.Receptionist, UserRole.Receptionist)]
    [InlineData(StaffRole.Accountant, UserRole.Manager)]
    public async Task RequestedManagerLayout_CannotGrantData(StaffRole role, UserRole expected)
    {
        await using var host = await CreateHostAsync(role);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var snapshot = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.Equal(expected, snapshot.Role);
        if (role != StaffRole.Accountant) Assert.Null(snapshot.RevenuePulse);
        Assert.True(snapshot.DoctorLoads is null || snapshot.DoctorLoads.Count == 0);
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext().Value!;
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new DashboardSnapshotQuery(UserRole.Manager), context);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Workloads);
        if (role != StaffRole.Accountant) Assert.Empty(result.Value.RevenueDays);
    }

    [Fact]
    public async Task StoredGrantAndDeny_OverrideRoleDefaults()
    {
        await using var host = await CreateHostAsync(StaffRole.Receptionist);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var original = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.Null(original.RevenuePulse);
        await ChangeMemberAsync(host, member => member.GrantExtra(PermissionCodes.FinanceRead));
        var granted = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.NotNull(granted.RevenuePulse);
        await ChangeMemberAsync(host, member => member.Deny(PermissionCodes.FinanceRead));
        var denied = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.Null(denied.RevenuePulse);
    }

    [Theory]
    [InlineData("suspend")]
    [InlineData("revoke")]
    [InlineData("lock")]
    [InlineData("expire")]
    [InlineData("signout")]
    public async Task WarmCache_RechecksLiveAuthorization(string change)
    {
        await using var host = await CreateHostAsync(StaffRole.Manager);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var warm = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.NotNull(warm.RevenuePulse);
        Assert.Same(warm, await service.GetSnapshotAsync(UserRole.Manager));
        switch (change)
        {
            case "suspend": await ChangeMemberAsync(host, member => member.Suspend()); break;
            case "revoke": await ChangeMemberAsync(host, member => member.ChangeRole(StaffRole.Receptionist)); break;
            case "lock": host.Cache.Set(host.Cache.Current! with { IsLocked = true }); break;
            case "expire": host.Clock.Advance(TimeSpan.FromDays(7)); break;
            case "signout": host.Cache.Set(null); break;
        }
        await Assert.ThrowsAsync<Animora.Contracts.Dashboard.DashboardQueryException>(() => service.GetSnapshotAsync(UserRole.Manager));
    }

    [Fact]
    public async Task AuthorizedConcurrentCacheHits_RemainCoalescedAndDayScoped()
    {
        var sql = new SqlRecorder();
        await using var host = await CreateHostAsync(StaffRole.Manager, sql);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        sql.Commands.Clear();
        var snapshots = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetSnapshotAsync(UserRole.Doctor)));
        Assert.All(snapshots, x => Assert.Same(snapshots[0], x));
        Assert.Single(sql.Commands, x => x.Contains("DailyFinanceFacts"));
        Assert.Equal(UserRole.Manager, snapshots[0].Role);
        host.Clock.Advance(TimeSpan.FromDays(1));
        // Keep the local test session active while advancing the injected clinic clock.
        host.Cache.Set(host.Cache.Current! with { ExpiresAtUtc = host.Clock.UtcNow.AddHours(12) });
        Assert.NotSame(snapshots[0], await service.GetSnapshotAsync(UserRole.Manager));
    }

    [Fact]
    public async Task UnknownStoredRole_FailsClosed()
    {
        await using var host = await CreateHostAsync(StaffRole.Doctor);
        await ChangeMemberAsync(host, member => member.ChangeRole((StaffRole)999));
        host.Cache.Set(host.Cache.Current! with { Role = (StaffRole)999 });
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        await Assert.ThrowsAsync<Animora.Contracts.Dashboard.DashboardQueryException>(() => service.GetSnapshotAsync(UserRole.Manager));
    }

    [Fact]
    public async Task Receptionist_ReceivesNoFakeFinancialZero_AndForbiddenSourcesAreNotQueried()
    {
        var sql = new SqlRecorder();
        await using var host = await CreateHostAsync(StaffRole.Receptionist, sql);
        sql.Commands.Clear();
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());
        var snapshot = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.DoesNotContain(snapshot.Stats, x => x.IconKey is "IconArrowUpRight" or "IconBox");
        Assert.Null(snapshot.RevenuePulse);
        Assert.Null(snapshot.DoctorLoads);
        Assert.NotEmpty(sql.Commands);
        Assert.DoesNotContain(sql.Commands, x => x.Contains("DailyFinanceFacts") || x.Contains("StockOnHandSnapshots"));
        await ChangeMemberAsync(host, member => member.GrantExtra(PermissionCodes.FinanceRead));
        sql.Commands.Clear();
        var granted = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.Contains(granted.Stats, x => x.IconKey == "IconArrowUpRight");
        Assert.Contains(sql.Commands, x => x.Contains("DailyFinanceFacts"));
        await ChangeMemberAsync(host, member => member.Deny(PermissionCodes.FinanceRead));
        sql.Commands.Clear();
        var denied = await service.GetSnapshotAsync(UserRole.Manager);
        Assert.DoesNotContain(denied.Stats, x => x.IconKey == "IconArrowUpRight");
        Assert.DoesNotContain(sql.Commands, x => x.Contains("DailyFinanceFacts"));
    }

    internal sealed class SqlRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    internal static async Task<LocalIdentityHost> CreateHostAsync(StaffRole role, DbCommandInterceptor? interceptor = null)
    {
        var host = new LocalIdentityHost(interceptor);
        await host.InitializeAsync();
        var setup = await host.SendAnonymousAsync(new CompleteFirstRunSetupCommand("Clinic", "Owner", "owner", "Secret123!"));
        Assert.True(setup.IsSuccess, setup.Detail);
        await ChangeMemberAsync(host, member => member.ChangeRole(role));
        host.Cache.Set(host.Cache.Current! with { Role = role });
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var fact = DailyFinanceFact.Create(Tenant.LocalDefaultId, 20260924);
        fact.Apply(revenue: 12345);
        db.Add(fact);
        await db.SaveChangesAsync();
        return host;
    }

    internal static async Task ChangeMemberAsync(LocalIdentityHost host, Action<StaffMembership> change)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var member = await db.Set<StaffMembership>().AsTracking().SingleAsync(x => x.UserId == host.Cache.Current!.UserId);
        change(member);
        await db.SaveChangesAsync();
    }
}
