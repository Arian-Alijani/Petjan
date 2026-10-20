using Animora.Contracts.Wire.Errors;
using Animora.Domain.Results;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Authentication;

namespace Animora.Server.Operator;

internal sealed class OperatorContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if(http.Request.Path.StartsWithSegments(ServerRoutes.Admin,StringComparison.OrdinalIgnoreCase) ||
            http.Request.Path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase))
        {
            var cookie=await http.AuthenticateAsync(Animora.Server.Admin.OperatorCookie.Scheme).ConfigureAwait(false);
            bool anonymous=http.Request.Path.Equals("/admin/login",StringComparison.OrdinalIgnoreCase)||
                http.Request.Path.Equals("/admin/account/login",StringComparison.OrdinalIgnoreCase);
            http.User=cookie.Principal??new System.Security.Claims.ClaimsPrincipal();
            if(!cookie.Succeeded)
            { if(!anonymous) { if (http.Request.Path.StartsWithSegments("/_blazor")) http.Response.StatusCode = 401; else http.Response.Redirect("/admin/login"); return; } }
            else
            {
                http.RequestServices.GetRequiredService<OperatorTenantContext>().Authenticate();
                if(http.Items.ContainsKey("AdminPasswordChangeRequired")&&!Animora.Server.Admin.OperatorCookie.PasswordChangePath(http.Request.Path))
                { if (http.Request.Path.StartsWithSegments("/_blazor")) http.Response.StatusCode = 403; else http.Response.Redirect("/admin/account/change-password"); return; }
            }
            await next(http).ConfigureAwait(false); return;
        }
        if (!http.Request.Path.StartsWithSegments(ServerRoutes.Operator, StringComparison.OrdinalIgnoreCase) ||
            http.Request.Path.Equals(ServerRoutes.Operator + "/auth/login", StringComparison.OrdinalIgnoreCase))
        { await next(http).ConfigureAwait(false); return; }
        var authenticated = await http.AuthenticateAsync(OperatorAuth.Scheme).ConfigureAwait(false);
        if (!authenticated.Succeeded || authenticated.Principal is not { } principal)
        {
            http.User = new System.Security.Claims.ClaimsPrincipal();
            await next(http).ConfigureAwait(false); return;
        }
        var id = principal.FindFirst("sub")?.Value;
        var stamp = principal.FindFirst("ost")?.Value;
        var account = await OperatorRequestAccount.GetAsync(http, id).ConfigureAwait(false);
        if (account is null || !account.IsValidFor(stamp))
        { await Failure(http, 401, ErrorCodes.OperatorCredentialsInvalid).ConfigureAwait(false); return; }
        if (account.MustChangePassword && !http.Request.Path.Equals(ServerRoutes.Operator + "/auth/change-password", StringComparison.OrdinalIgnoreCase))
        { await Failure(http, 403, ErrorCodes.PasswordPolicyViolation).ConfigureAwait(false); return; }
        http.User = principal;
        http.RequestServices.GetRequiredService<OperatorTenantContext>().Authenticate();
        await next(http).ConfigureAwait(false);
    }
    private static Task Failure(HttpContext http, int status, ErrorCode error)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new WireApiError(error.Code, null, [], CorrelationHeader.Resolve(http), Category: error.Category), http.RequestAborted);
    }
}
