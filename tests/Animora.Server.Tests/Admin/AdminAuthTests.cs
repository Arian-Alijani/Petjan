using System.Net;
using Animora.Server.Tests.Persistence;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Animora.Server.Auth;
using Animora.Server.Operator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Text.Json;
namespace Animora.Server.Tests.Admin;

public sealed class AdminAuthTests
{
    [Theory] [InlineData("/admin")] [InlineData("/admin/tenants")] [InlineData("/admin/tenants/new")]
    [InlineData("/admin/plans")] [InlineData("/admin/licensing/keys")] [InlineData("/admin/unknown-route")]
    [InlineData("/admin/tenants/guess")] [InlineData("/admin/tenants/guess/licenses")] [InlineData("/admin/account/change-password")]
    public async Task Anonymous_RedirectsToLogin_ForEveryAdminRoute(string route)
    { using var host=new AdminHost();var response=await host.Client.GetAsync(route);Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Equal("/admin/login",response.Headers.Location?.OriginalString); }
    private static async Task AcceptedByScheme(AdminHost host, string scheme, string token)
    {
        using var scope = host.Factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = "Bearer " + token;
        var result = await context.AuthenticateAsync(scheme);
        Assert.True(result.Succeeded); Assert.Equal(scheme, result.Ticket!.AuthenticationScheme);
    }
    [Fact] public async Task TenantBearerToken_CannotAccessAdmin()
    {
        using var host = new AdminHost();
        string token;
        using (var scope = host.Factory.Services.CreateScope())
            token = scope.ServiceProvider.GetRequiredService<IAccessTokenIssuer>().Issue(new("user", "tenant", "device", "stamp", "Manager")).Token;
        await AcceptedByScheme(host, IdentityConstants.BearerScheme, token);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var response = await host.Client.GetAsync("/admin"); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/login", response.Headers.Location?.OriginalString);
    }
    [Fact] public async Task OperatorApiBearer_CannotAccessAdmin()
    {
        using var host = new AdminHost();
        string token;
        using (var scope = host.Factory.Services.CreateScope())
            token = JsonSerializer.SerializeToElement(OperatorAuth.Issue(new OperatorAccount { Id = "operator", SecurityStamp = "stamp" },
                DateTimeOffset.UtcNow, scope.ServiceProvider.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>())).GetProperty("accessToken").GetString()!;
        await AcceptedByScheme(host, OperatorAuth.Scheme, token);
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var response = await host.Client.GetAsync("/admin"); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/login", response.Headers.Location?.OriginalString);
    }
    [Fact] public async Task LoginPage_IsStaticSSR_WithAntiforgery()
    { using var host=new AdminHost();var html=await host.Client.GetStringAsync("/admin/login");Assert.Contains("Operator sign in",html);Assert.Contains("__RequestVerificationToken",html);Assert.DoesNotContain("blazor.server.js",html);Assert.DoesNotContain("blazor:server",html); }
}
public sealed class AdminAntiforgeryTests
{
    [Fact] public async Task PostWithoutToken_Rejected_ForAnonymousLogin()
    { using var host=new AdminHost();Assert.Equal(HttpStatusCode.BadRequest,(await host.Client.PostAsync("/admin/account/login",new FormUrlEncodedContent(new Dictionary<string,string> { ["userName"]="admin",["password"]="wrong" }))).StatusCode); }
    [Fact] public void GetNeverMutates()
    { var source=File.ReadAllText(Path.Combine(RepoRoot.Find(),"src/Animora.Server/Admin/AdminAccountEndpoints.cs"));Assert.DoesNotContain("MapGet",source);Assert.Contains("MapPost(\"/logout\"",source); }
}
public sealed class AdminCookiePgTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private async Task<AdminHost> Host(bool mustChange=false)
    {
        await using(var db=TestSupport.CreateContext(connection:pg.ConnectionString))await db.Database.MigrateAsync();
        var host=new AdminHost(pg.ConnectionString);await host.SeedOperatorAsync(mustChange);return host;
    }
    [PgFact] public async Task SecurityStampChange_InvalidatesCookieImmediately()
    {
        using var host=await Host();Assert.Equal(HttpStatusCode.Redirect,(await host.LoginAsync()).StatusCode);
        using(var scope=host.Factory.Services.CreateScope()) {var db=scope.ServiceProvider.GetRequiredService<AnimoraOperatorDbContext>();var a=await db.Accounts.SingleAsync();a.SecurityStamp="changed";await db.SaveChangesAsync();}
        Assert.Equal("/admin/login",(await host.Client.GetAsync("/admin/tenants")).Headers.Location?.OriginalString);
    }
    [PgFact] public async Task MustChangePassword_OnlyChangePasswordReachable()
    {
        using var host=await Host(true);Assert.Equal("/admin/account/change-password",(await host.LoginAsync()).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK,(await host.Client.GetAsync("/admin/account/change-password")).StatusCode);
        Assert.Equal("/admin/account/change-password",(await host.Client.GetAsync("/admin/tenants")).Headers.Location?.OriginalString);
    }
    [PgFact] public async Task Login_WrongPasswordAndUnknownUser_SameMessage_AndLockoutAfter5Failures()
    {
        using var host=await Host();var wrong=await host.LoginAsync(password:"bad");var unknown=await host.LoginAsync(user:"unknown",password:"bad");
        Assert.Equal(wrong.Headers.Location,unknown.Headers.Location);
        for(int i=0;i<4;i++)await host.LoginAsync(password:"bad");
        Assert.Equal("/admin/login?error=invalid",(await host.LoginAsync()).Headers.Location?.OriginalString);
        using var scope=host.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AnimoraOperatorDbContext>();Assert.True((await db.Accounts.SingleAsync()).IsLockedOut(DateTimeOffset.UtcNow));
    }
    [PgFact] public async Task Cookie_CannotCallOperatorApi_AndPostsWithoutTokenRejected()
    {
        using var host=await Host();await host.LoginAsync();Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client.GetAsync("/operator/tenants")).StatusCode);
        foreach(string route in new[] { "/admin/tenants/new","/admin/tenants/guess","/admin/tenants/guess/licenses","/admin/plans","/admin/licensing/keys","/admin/account/logout","/admin/account/change-password" })
            Assert.Equal(HttpStatusCode.BadRequest,(await host.Client.PostAsync(route,new StringContent(""))).StatusCode);
    }
    [PgFact] public async Task CookieRevokedByOperatorPasswordChange()
    {
        using var host=await Host();await host.LoginAsync();string token=await host.TokenAsync("/admin/account/change-password");
        var result=await host.Client.PostAsync("/admin/account/change-password",new FormUrlEncodedContent(new Dictionary<string,string> {
            ["__RequestVerificationToken"]=token,["oldPassword"]="Strong-operator-Pass-123!",["newPassword"]="New-operator-Pass-123!" }));
        Assert.Equal("/admin/login",result.Headers.Location?.OriginalString);Assert.Equal(HttpStatusCode.Redirect,(await host.Client.GetAsync("/admin")).StatusCode);
    }
}
