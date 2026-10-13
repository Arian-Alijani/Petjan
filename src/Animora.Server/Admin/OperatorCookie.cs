using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
namespace Animora.Server.Admin;

internal sealed class AdminOptions
{
    public int SessionMinutes { get; set; }=30;
    public bool RequireHttps { get; set; }=true;
}
internal static class OperatorCookie
{
    public const string Scheme="Animora.Operator.Cookie";
    public const string Policy="AdminOnly";
    public const string Name="__Host-animora-op";
    public static bool PasswordChangePath(PathString path)=>path.Equals("/admin/account/change-password",StringComparison.OrdinalIgnoreCase)||path.Equals("/admin/account/logout",StringComparison.OrdinalIgnoreCase);
    public static void Configure(CookieAuthenticationOptions options)
    {
        options.Cookie.Name=Name; options.Cookie.Path="/"; options.Cookie.SecurePolicy=CookieSecurePolicy.Always;
        options.Cookie.HttpOnly=true; options.Cookie.SameSite=SameSiteMode.Strict;
        options.ExpireTimeSpan=TimeSpan.FromMinutes(30); options.SlidingExpiration=false; options.LoginPath="/admin/login";
        options.Events=new CookieAuthenticationEvents
        {
            OnRedirectToLogin=c=>{c.Response.Redirect("/admin/login");return Task.CompletedTask;},
            OnRedirectToAccessDenied=c=>{c.Response.Redirect("/admin/login");return Task.CompletedTask;},
            OnValidatePrincipal=async c=>
            {
                var id=c.Principal?.FindFirstValue("sub"); var stamp=c.Principal?.FindFirstValue("ost");
                // BUG-OPLOCK-01: one operator-account read per request, shared with every other auth pass.
                var account=await Animora.Server.Operator.OperatorRequestAccount.GetAsync(c.HttpContext,id).ConfigureAwait(false);
                if(account is null||!account.IsValidFor(stamp))
                { c.RejectPrincipal(); await c.HttpContext.SignOutAsync(Scheme).ConfigureAwait(false); return; }
                if(account.MustChangePassword) c.HttpContext.Items["AdminPasswordChangeRequired"]=true;
            }
        };
    }
}
