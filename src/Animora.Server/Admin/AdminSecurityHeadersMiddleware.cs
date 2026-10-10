using Animora.Server.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
namespace Animora.Server.Admin;

internal sealed class AdminSecurityHeadersMiddleware(RequestDelegate next)
{
    internal static void Apply(HttpResponse response)
    {
        response.Headers.CacheControl="no-store"; response.Headers["X-Content-Type-Options"]="nosniff";
        response.Headers["X-Frame-Options"]="DENY"; response.Headers["Referrer-Policy"]="no-referrer";
        response.Headers.ContentSecurityPolicy="default-src 'self'; frame-ancestors 'none'; form-action 'self'";
    }
    public async Task InvokeAsync(HttpContext http,IOptions<AdminOptions> options)
    {
        if(!http.Request.Path.StartsWithSegments(ServerRoutes.Admin)) { await next(http).ConfigureAwait(false); return; }
        Apply(http.Response); http.Response.OnStarting(()=>{Apply(http.Response);return Task.CompletedTask;});
        if(options.Value.RequireHttps&&!http.Request.IsHttps) { http.Response.StatusCode=400; return; }
        await next(http).ConfigureAwait(false);
    }
}

internal sealed class AdminAntiforgeryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if(http.Request.Path.StartsWithSegments(ServerRoutes.Admin)&&HttpMethods.IsPost(http.Request.Method))
        {
            try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http).ConfigureAwait(false); }
            catch(AntiforgeryValidationException) { http.Response.StatusCode=400; return; }
        }
        await next(http).ConfigureAwait(false);
    }
}
