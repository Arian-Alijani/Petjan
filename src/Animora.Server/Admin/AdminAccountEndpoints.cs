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
        // Language switch is available before sign-in too (the login page shares the layout); it only sets a cookie.
        group.MapPost("/lang",SetLanguage).AllowAnonymous().WithOrder(-1);
        group.MapPost("/logout",Logout).RequireAuthorization(OperatorCookie.Policy);
        // The change-password Razor page shares this route (it renders on GET); POST must win over the component
        // endpoint, which accepts every verb, or the request is rejected as an ambiguous match (HTTP 500).
        group.MapPost("/change-password",ChangePassword).RequireAuthorization(OperatorCookie.Policy).WithOrder(-1);
    }
    private static async Task<IResult> SetLanguage(HttpContext http,IAntiforgery antiforgery,CancellationToken ct)
    {
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
        var form=await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        string lang=Animora.Web.Admin.Localization.AdminCulture.Normalize(form["lang"].ToString());
        http.Response.Cookies.Append(Animora.Web.Admin.Localization.AdminCulture.CookieName,lang,new CookieOptions
            { HttpOnly=true,Secure=true,SameSite=SameSiteMode.Lax,Path="/",MaxAge=TimeSpan.FromDays(365),IsEssential=true });
        return Results.Redirect(SafeReturnUrl(form["returnUrl"].ToString()));
    }
    /// <summary>Only same-site <c>/admin…</c> paths are honoured, so the switch can never become an open redirect.</summary>
    internal static string SafeReturnUrl(string? value)=>
        !string.IsNullOrEmpty(value)&&value.Length<=2048&&value.StartsWith("/admin",StringComparison.Ordinal)
        &&(value.Length==6||value[6] is '/' or '?' or '#')&&!value.Contains('\\')&&!value.Contains('\r')&&!value.Contains('\n')?value:"/admin";
    private static async Task<IResult> Login(HttpContext http,OperatorAccountService service,IAntiforgery antiforgery,TimeProvider time,CancellationToken ct)
    {
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
        var form=await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        var result=await service.LoginAsync(form["userName"].ToString(),form["password"].ToString(),CorrelationHeader.Resolve(http),ct).ConfigureAwait(false);
        if(result.IsFailure) return Results.Redirect("/admin/login?error=invalid");
        var account=result.Value!;
        var now = time.GetUtcNow();
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new("sub",account.Id),new("ost",account.SecurityStamp),
            new("role","operator"),new("op_exp", now.AddMinutes(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),new(ClaimTypes.Name,account.UserName)],OperatorCookie.Scheme,ClaimTypes.Name,"role"));
        await http.SignInAsync(OperatorCookie.Scheme,principal,new AuthenticationProperties
            { IssuedUtc=now,ExpiresUtc=now.AddMinutes(30),IsPersistent=false,AllowRefresh=false }).ConfigureAwait(false);
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
