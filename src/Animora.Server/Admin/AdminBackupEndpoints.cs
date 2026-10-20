using Animora.Server.Hosting;
using Animora.Server.Operator;
using Microsoft.AspNetCore.Antiforgery;
namespace Animora.Server.Admin;

internal static class AdminBackupEndpoints
{
    public static void MapAdminBackupEndpoints(this WebApplication app)
    {
        // POST (not GET): the download is a sensitive action and must carry the antiforgery token like every other operator mutation.
        // The /admin/backup page itself (GET) is the Razor component; only the exact sub-path below is an endpoint.
        app.MapPost("/admin/backup/download",Download).RequireAuthorization(OperatorCookie.Policy).RequireRateLimiting(RateLimitPolicies.Operator).WithOrder(-1);
        // BAK-02: per-tenant export download — same POST + antiforgery + operator policy + rate limit, own name whitelist.
        app.MapPost("/admin/backup/tenant/download",DownloadTenant).RequireAuthorization(OperatorCookie.Policy).RequireRateLimiting(RateLimitPolicies.Operator).WithOrder(-1);
    }
    private static async Task<IResult> DownloadTenant(HttpContext http,AdminBackupAdapter backups,IAntiforgery antiforgery,CancellationToken ct)
    {
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
        var form=await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        var resolved=await backups.ResolveTenantDownloadAsync(form["file"].ToString(),ct).ConfigureAwait(false);
        if(resolved.IsFailure)return Results.NotFound();
        var path=resolved.Value!;
        http.Response.Headers.CacheControl="no-store";
        return Results.File(path,"application/gzip",Path.GetFileName(path));
    }
    private static async Task<IResult> Download(HttpContext http,AdminBackupAdapter backups,IAntiforgery antiforgery,CancellationToken ct)
    {
        await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
        var form=await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        var resolved=await backups.ResolveDownloadAsync(form["file"].ToString(),ct).ConfigureAwait(false);
        if(resolved.IsFailure)return Results.NotFound();
        var path=resolved.Value!;
        http.Response.Headers.CacheControl="no-store";
        return Results.File(path,"application/octet-stream",Path.GetFileName(path));
    }
}
