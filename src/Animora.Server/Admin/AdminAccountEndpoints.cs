using System.Security.Claims;
using Animora.Server.Hosting;
using Animora.Server.Operator;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
namespace Animora.Server.Admin;

internal static class AdminAccountEndpoints
{
    public static void MapAdminAccountEndpoints(this WebApplication app)
    {
        var group=app.MapGroup("/admin/account").RequireRateLimiting(RateLimitPolicies.Operator);
        group.MapPost("/login",Login).AllowAnonymous();
        group.MapPost("/logout",Logout).RequireAuthorization(OperatorCookie.Policy);
        group.MapPost("/change-password",ChangePassword).RequireAuthorization(OperatorCookie.Policy);
    }
    private static async Task<IResult> Login(HttpContext http,OperatorAccountService service,IAntiforgery antiforgery,CancellationToken ct)
    {
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
        var form=await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        var result=await service.LoginAsync(form["userName"].ToString(),form["password"].ToString(),CorrelationHeader.Resolve(http),ct).ConfigureAwait(false);
        if(result.IsFailure) return Results.Redirect("/admin/login?error=invalid");
        var account=result.Value!;
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new("sub",account.Id),new("ost",account.SecurityStamp),
            new("role","operator"),new(ClaimTypes.Name,account.UserName)],OperatorCookie.Scheme,ClaimTypes.Name,"role"));
        await http.SignInAsync(OperatorCookie.Scheme,principal,new AuthenticationProperties
            { IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(30),IsPersistent=false,AllowRefresh=false }).ConfigureAwait(false);
        return Results.Redirect(account.MustChangePassword?"/admin/account/change-password":"/admin");
    }
    private static async Task<IResult> Logout(HttpContext http,OperatorAccountService service,IAntiforgery antiforgery,CancellationToken ct)
    {
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
        await service.LogoutAsync(http.User.FindFirstValue("sub")!,CorrelationHeader.Resolve(http),ct).ConfigureAwait(false);
        await http.SignOutAsync(OperatorCookie.Scheme).ConfigureAwait(false); return Results.Redirect("/admin/login");
    }
    private static async Task<IResult> ChangePassword(HttpContext http,OperatorAccountService service,IAntiforgery antiforgery,CancellationToken ct)
    {
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false); var form=await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        var result=await service.ChangePasswordAsync(http.User.FindFirstValue("sub")!,form["oldPassword"].ToString(),form["newPassword"].ToString(),
            http.User.FindFirstValue("ost")!,CorrelationHeader.Resolve(http),ct).ConfigureAwait(false);
        if(result.IsFailure) return Results.Redirect("/admin/account/change-password?error=invalid");
        await http.SignOutAsync(OperatorCookie.Scheme).ConfigureAwait(false); return Results.Redirect("/admin/login");
    }
}
