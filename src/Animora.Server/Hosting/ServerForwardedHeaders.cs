using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Animora.Server.Hosting;

/// <summary>
/// SEC-FWD-01: wires <c>UseForwardedHeaders</c> early in the pipeline with an explicit
/// trusted-proxy allowlist. Without a configured allowlist (<see cref="ForwardedHeadersSettings.Enabled"/>
/// = false, the default) NO forwarded header is honored, so the client IP used for rate limiting /
/// auditing is the real connection peer and <c>X-Forwarded-For</c> cannot be spoofed.
/// </summary>
internal static class ServerForwardedHeaders
{
    /// <summary>Applies forwarded-header processing when enabled by configuration.</summary>
    public static void Use(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var settings = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value.ForwardedHeaders;
        if (!settings.Enabled)
        {
            return; // Fail-safe: trust no proxy; keep the raw connection peer.
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = Math.Max(1, settings.ForwardLimit),
        };

        // Start from an empty allowlist: ASP.NET defaults trust loopback only; we want an explicit set.
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var proxy in settings.KnownProxies)
        {
            if (IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }

        foreach (var network in settings.KnownNetworks)
        {
            var parts = network.Split('/', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2
                && IPAddress.TryParse(parts[0], out var prefix)
                && int.TryParse(parts[1], out var length))
            {
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
            }
        }

        app.UseForwardedHeaders(options);
    }
}
