using System.Security.Claims;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Server.Admin;
using Animora.Web.Admin.Models;
using Microsoft.AspNetCore.Http;
using Xunit;
namespace Animora.Server.Tests.Admin;

public sealed class AdminAdaptersTests
{
    private sealed class CapturingSubscriptions : ISubscriptionService
    {
        public ActorRef? Actor; public string? Stamp;
        public Task<Result<Subscription>> MutateAsync(string tenantId,string expectedStamp,SubscriptionMutation mutation,ActorRef actor,CancellationToken ct=default)
        { Actor=actor;Stamp=expectedStamp;return Task.FromResult(Result<Subscription>.Fail(ErrorCodes.ConcurrencyConflict)); }
    }
    internal static HttpContext CookieContext(string id="operator-from-cookie")=>new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity([
        new("sub",id),new("ost","stamp"),new("role","operator")],OperatorCookie.Scheme,ClaimTypes.Name,"role")) };
    [Fact] public async Task ActorAlwaysFromCookiePrincipal()
    {
        var context=CookieContext();var service=new CapturingSubscriptions();var adapter=new AdminSubscriptionAdapter(service,null!,null!,new AdminSession(new HttpContextAccessor { HttpContext=context }));
        var result=await adapter.MutateAsync("tenant","extend",new() { Stamp="stale-stamp",Days=1 });
        Assert.Equal("operator-from-cookie",service.Actor!.ActorId);Assert.Equal(ActorKind.Operator,service.Actor.Kind);Assert.Equal("stale-stamp",service.Stamp);Assert.Equal(ErrorCodes.ConcurrencyConflict,result.Error);
    }
    [Fact] public void ApiBearerIdentity_CannotSupplyAdapterActor()
    {
        var context=new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity([new("sub","spoofed")],"Animora.Operator"))};
        Assert.Throws<UnauthorizedAccessException>(()=>new AdminSession(new HttpContextAccessor {HttpContext=context}).OperatorId);
    }
    private sealed class ErrorPage : AdminPageComponent { public void Show()=>Capture(Result<string>.Fail(ErrorCodes.ConcurrencyConflict));public string? Code=>Error; }
    [Fact] public void IfMatchMismatch_Renders412Message()
    { var c=new DefaultHttpContext();var page=new ErrorPage {Http=c};page.Show();Assert.Equal(412,c.Response.StatusCode);Assert.Contains("Someone else changed",Animora.Web.Admin.AdminMessages.For(page.Code!)); }
}
