using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Server.Tests;

/// <summary>
/// Builds a minimal in-memory host around individual server building blocks
/// (middleware / rate limiter) so they can be probed through endpoints that the
/// real host does not expose yet.
/// </summary>
internal static class TestHostFactory
{
    public static async Task<WebApplication> StartAsync(
        string environment,
        Action<WebApplicationBuilder> configureServices,
        Action<WebApplication> configurePipeline,
        IDictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        configureServices(builder);
        var app = builder.Build();
        configurePipeline(app);
        await app.StartAsync();
        return app;
    }
}
