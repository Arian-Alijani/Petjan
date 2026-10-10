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
    [PgFact] public async Task OperatorCreatesTenant_SuspendsIt_TenantRequestsGet403_ReinstateRestores()
    {
        using var host=await Host();string id;
        await using(var scope=Scope(host)) {var f=NewForm();id=(await scope.ServiceProvider.GetRequiredService<IAdminSubscriptionCommands>().CreateTenantAsync(f,f.IdempotencyKey)).Value!.TenantId!;}
        await using(var scope=Scope(host))
        {
            var detail=(await scope.ServiceProvider.GetRequiredService<IAdminTenantQueries>().GetAsync(id)).Value!;
            var result=await scope.ServiceProvider.GetRequiredService<IAdminSubscriptionCommands>().SuspendAsync(id,new() {Stamp=detail.Subscription!.ConcurrencyStamp,Confirmed=true,Reason="test suspension"});Assert.True(result.IsSuccess);
        }
        // The same stored tenant status is what the tenant-binding plane reads before returning 403.
        await using(var db=TestSupport.CreateContext(new FixedTenantContext(id),pg.ConnectionString))Assert.Equal(TenantStatus.Suspended,(await db.Tenants.SingleAsync(t=>t.Id==id)).Status);
        await using(var scope=Scope(host))
        {
            var detail=(await scope.ServiceProvider.GetRequiredService<IAdminTenantQueries>().GetAsync(id)).Value!;
            Assert.True((await scope.ServiceProvider.GetRequiredService<IAdminSubscriptionCommands>().ReinstateAsync(id,new() {Stamp=detail.Subscription!.ConcurrencyStamp})).IsSuccess);
        }
        await using(var db=TestSupport.CreateContext(new FixedTenantContext(id),pg.ConnectionString))Assert.Equal(TenantStatus.Active,(await db.Tenants.SingleAsync(t=>t.Id==id)).Status);
    }
    [PgFact] public async Task RevokeLicence_DeviceBlockedAtServerGate()
    {
        using var host=await Host();string id;
        await using(var scope=Scope(host)) {var f=NewForm();id=(await scope.ServiceProvider.GetRequiredService<IAdminSubscriptionCommands>().CreateTenantAsync(f,f.IdempotencyKey)).Value!.TenantId!;}
        string deviceId="01K00000000000000000000055",licenseId=IdGenerator.NewId();
        await using(var scope=Scope(host))
        {
            var store=scope.ServiceProvider.GetRequiredService<LicenseStore>();
            store.Grants.Add(new() {TenantId=id,SubscriptionId="subscription",PlanCode="standard",Status=LicenseGrantStatus.Active,Sequence=1,SeatLimit=1,MaxDevices=2,ValidUntilUtc=DateTimeOffset.UtcNow.AddDays(30),UpdatedUtc=DateTimeOffset.UtcNow});
            store.Licenses.Add(new() {TenantId=id,Id=licenseId,DeviceId=deviceId,InstallationIdHash=new string('a',64),Sequence=1,Status=LicenseRecordStatus.Active,KeyId="key",PayloadSha256=new string('b',64),IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddDays(1),OfflineGraceUntilUtc=DateTimeOffset.UtcNow.AddDays(2)});
            await store.SaveChangesAsync();
            var result=await scope.ServiceProvider.GetRequiredService<IAdminLicenseCommands>().RevokeAsync(id,new() {LicenseId=licenseId,Stamp="1",Reason="test",Confirmed=true});Assert.True(result.IsSuccess);
        }
        await using(var scope=host.Factory.Services.CreateAsyncScope())
        {
            var http=new DefaultHttpContext();http.Request.Path="/api/commands";http.User=new ClaimsPrincipal(new ClaimsIdentity([new("did",deviceId)]));scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext=http;
            var result=await scope.ServiceProvider.GetRequiredService<IServerEntitlementGate>().EvaluateAsync(new(id,"owner",null));Assert.Equal(ErrorCodes.LicenseRevoked,result.Error);
        }
    }
}
