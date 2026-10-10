using System.Security.Claims;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Admin;
using Animora.Server.Authorization;
using Animora.Server.Tests.Persistence;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Net;
using System.Text.RegularExpressions;
using Animora.Domain.Time;
using Animora.Server.Auth;
using Animora.Server.Hosting;
using Animora.Server.Observability;
using Microsoft.AspNetCore.Mvc.Testing;
namespace Animora.Server.Tests.Admin;

public sealed class AdminEndToEndTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private async Task<AdminHost> Host()
    { await using(var db=TestSupport.CreateContext(connection:pg.ConnectionString))await db.Database.MigrateAsync();return new AdminHost(pg.ConnectionString); }
    private static AsyncServiceScope Scope(AdminHost host)
    {
        var scope=host.Factory.Services.CreateAsyncScope();scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext=AdminAdaptersTests.CookieContext();return scope;
    }
    private static CreateTenantForm NewForm()=>new() { Code="a-"+Guid.NewGuid().ToString("N")[..20],Name="Test clinic",OwnerName="Owner",OwnerUserName="owner",PlanCode="standard",StartTrial=true };
    [PgFact] public async Task CreateTenant_DoubleSubmit_SameIdempotencyKey_OneTenant()
    {
        using var host=await Host();var form=NewForm();MutationReceipt first,second;
        await using(var scope=Scope(host)) {var result=await scope.ServiceProvider.GetRequiredService<IAdminSubscriptionCommands>().CreateTenantAsync(form,form.IdempotencyKey);Assert.True(result.IsSuccess);first=result.Value!;}
        await using(var scope=Scope(host)) {var result=await scope.ServiceProvider.GetRequiredService<IAdminSubscriptionCommands>().CreateTenantAsync(form,form.IdempotencyKey);Assert.True(result.IsSuccess);second=result.Value!;}
        Assert.Equal(first.TenantId,second.TenantId);Assert.NotNull(first.TemporaryPassword);Assert.Null(second.TemporaryPassword);
        await using(var scope=Scope(host)) {var db=scope.ServiceProvider.GetRequiredService<AnimoraOperatorDbContext>();Assert.Equal(1,await db.Tenants.CountAsync(t=>t.Code==form.Code));Assert.All(await db.Audit.Where(a=>a.TargetTenantId==first.TenantId).ToListAsync(),a=>Assert.Equal("operator-from-cookie",a.OperatorId));}
    }
    private async Task<(Device Device, string Token)> RegisterDevice(AdminHost host, string tenantId)
    {
        using var scope = host.Factory.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        await using var db = TestSupport.CreateContext(new FixedTenantContext(tenantId), pg.ConnectionString);
        var owner = await db.Users.SingleAsync(u => u.UserName == "owner");
        var device = Device.Register(tenantId, owner.Id, "HTTP test device", new string('a', 64), "windows", clock);
        device.Approve(); db.Devices.Add(device); await db.SaveChangesAsync();
        string token = scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>()
            .Issue(new(owner.Id, tenantId, device.Id, owner.SecurityStamp, "Manager")).Token;
        return (device, token);
    }
    private static HttpClient TenantClient(AdminHost host, string token)
    {
        var client = host.Factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", token); return client;
    }
    [PgFact] public async Task OperatorCreatesTenant_SuspendsIt_TenantRequestsGet403_ReinstateRestores()
    {
        using var host = await Host(); await host.SeedOperatorAsync();
        using var login = await host.LoginAsync(); Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var form = NewForm();
        string initialHtml = await host.Client.GetStringAsync("/admin/tenants/new");
        form.IdempotencyKey = AdminHost.Hidden(initialHtml, "Form.IdempotencyKey");
        var fields = new Dictionary<string, string>
        {
            ["Form.Code"] = form.Code, ["Form.Name"] = form.Name, ["Form.OwnerName"] = form.OwnerName,
            ["Form.OwnerUserName"] = form.OwnerUserName, ["Form.PlanCode"] = form.PlanCode,
            ["Form.StartTrial"] = "true", ["Form.IdempotencyKey"] = form.IdempotencyKey
        };
        using var created = await host.SubmitAsync("/admin/tenants/new", "create-tenant", fields);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        string html = await created.Content.ReadAsStringAsync(); Assert.Contains("Temporary password (shown once)", html);
        var match = Regex.Match(html, "href=\"/admin/tenants/([0-9A-HJKMNP-TV-Z]{26})\""); Assert.True(match.Success);
        string id = match.Groups[1].Value, route = "/admin/tenants/" + id;
        using var replay = await host.SubmitAsync("/admin/tenants/new", "create-tenant", fields);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.DoesNotContain("Temporary password (shown once)", await replay.Content.ReadAsStringAsync());
        Assert.DoesNotContain("password", created.Headers.Location?.OriginalString ?? "");
        var (_, token) = await RegisterDevice(host, id); using var tenant = TenantClient(host, token);
        using var before = await tenant.GetAsync("/licensing/status"); Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        string stamp = AdminHost.Hidden(await host.Client.GetStringAsync(route), "Form.Stamp");
        using var suspended = await host.SubmitAsync(route, "suspend", new Dictionary<string, string>
            { ["Form.Stamp"] = stamp, ["Form.Confirmed"] = "true", ["Form.Reason"] = "HTTP suspension" });
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        using var blocked = await tenant.GetAsync("/licensing/status"); Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains(ErrorCodes.TenantSuspended.Code, await blocked.Content.ReadAsStringAsync());
        using var stale = await host.SubmitAsync(route, "reinstate", new Dictionary<string, string> { ["Form.Stamp"] = stamp });
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Contains("Someone else changed", await stale.Content.ReadAsStringAsync());
        string current = AdminHost.Hidden(await host.Client.GetStringAsync(route), "Form.Stamp");
        Assert.NotEqual(stamp, current);
        using var reinstated = await host.SubmitAsync(route, "reinstate", new Dictionary<string, string> { ["Form.Stamp"] = current });
        Assert.Equal(HttpStatusCode.OK, reinstated.StatusCode);
        using var after = await tenant.GetAsync("/licensing/status"); Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var scope = host.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AnimoraOperatorDbContext>();
        Assert.Equal(1, await db.Tenants.CountAsync(t => t.Code == form.Code));
        Assert.All(await db.Audit.Where(a => a.TargetTenantId == id && a.ActorKind == Animora.Domain.Subscriptions.ActorKind.Operator).ToListAsync(),
            a => Assert.Equal("01K00000000000000000000099", a.OperatorId));
    }
    [PgFact] public async Task RevokeLicence_DeviceBlockedAtServerGate()
    {
        using var host=await Host();string id;
        await using(var scope=Scope(host)) {var f=NewForm();id=(await scope.ServiceProvider.GetRequiredService<IAdminSubscriptionCommands>().CreateTenantAsync(f,f.IdempotencyKey)).Value!.TenantId!;}
        var (device, token) = await RegisterDevice(host, id);
        string deviceId = device.Id, licenseId = IdGenerator.NewId();
        await using(var scope=Scope(host))
        {
            var store=scope.ServiceProvider.GetRequiredService<LicenseStore>();
            store.Grants.Add(new() {TenantId=id,SubscriptionId="subscription",PlanCode="standard",Status=LicenseGrantStatus.Active,Sequence=1,SeatLimit=1,MaxDevices=2,ValidUntilUtc=DateTimeOffset.UtcNow.AddDays(30),UpdatedUtc=DateTimeOffset.UtcNow});
            store.Licenses.Add(new() {TenantId=id,Id=licenseId,DeviceId=deviceId,InstallationIdHash=new string('a',64),Sequence=1,Status=LicenseRecordStatus.Active,KeyId="key",PayloadSha256=new string('b',64),IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddDays(1),OfflineGraceUntilUtc=DateTimeOffset.UtcNow.AddDays(2)});
            await store.SaveChangesAsync();
        }
        using var tenant = TenantClient(host, token);
        using var available = await tenant.GetAsync("/files/unknown"); Assert.Equal(HttpStatusCode.NotFound, available.StatusCode);
        await using (var scope = Scope(host))
        {
            var result=await scope.ServiceProvider.GetRequiredService<IAdminLicenseCommands>().RevokeAsync(id,new() {LicenseId=licenseId,Stamp="1",Reason="test",Confirmed=true});Assert.True(result.IsSuccess);
        }
        using var denied = await tenant.GetAsync("/files/unknown"); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains(ErrorCodes.LicenseRevoked.Code, await denied.Content.ReadAsStringAsync());
        await using(var scope=host.Factory.Services.CreateAsyncScope())
        {
            var http=new DefaultHttpContext();http.Request.Path="/api/commands";http.User=new ClaimsPrincipal(new ClaimsIdentity([new("did",deviceId)]));scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext=http;
            var result=await scope.ServiceProvider.GetRequiredService<IServerEntitlementGate>().EvaluateAsync(new(id,"owner",null));Assert.Equal(ErrorCodes.LicenseRevoked,result.Error);
        }
    }
}

public sealed class AdminStatusReaderTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private sealed class SecretProbe : IReadinessProbe
    {
        public string Name => "storage-probe";
        public bool IsRequired => false;
        public int Calls;
        public Task<ReadinessResult> CheckAsync(CancellationToken ct)
        { Calls++; throw new IOException("Password=secret-db-password;Host=private-db;Path=/secret/storage"); }
    }
    [PgFact] public async Task DegradedByNameOnly_NoSecrets_AndCountersReadInProcess()
    {
        await using (var db = TestSupport.CreateContext(connection: pg.ConnectionString)) await db.Database.MigrateAsync();
        var probe = new SecretProbe();
        using var host = new AdminHost(pg.ConnectionString, services => services.AddSingleton<IReadinessProbe>(probe));
        await host.SeedOperatorAsync(); using var login = await host.LoginAsync(); Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var snapshot = host.Factory.Services.GetRequiredService<ReadinessSnapshot>(); snapshot.Set(probe.Name, false, false);
        var metrics = host.Factory.Services.GetRequiredService<ServerMetrics>(); metrics.SetQueues(781913, 517, 41); metrics.SetPoolInUse(3);
        using var command = metrics.CommandLease();
        await host.Factory.Services.GetRequiredService<AdminStatusCache>().RefreshOnceAsync();
        using var response = await host.Client.GetAsync("/admin"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains(probe.Name, html); Assert.Contains("degraded", html); Assert.Contains("781913", html); Assert.Contains("517", html);
        Assert.Equal(0, probe.Calls);
        foreach (string secret in new[] { "secret-db-password", "private-db", "/secret/storage", pg.ConnectionString }) Assert.DoesNotContain(secret, html);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
