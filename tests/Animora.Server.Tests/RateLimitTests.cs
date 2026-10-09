using System.Net;
using System.Text.Json;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Server.Tests;

public sealed class RateLimitTests
{
    private static Task<WebApplication> StartAsync(int authPermitLimit)
        => TestHostFactory.StartAsync(
            "Production",
            builder =>
            {
                builder.Services.AddOptions<ServerOptions>().Configure(o =>
                {
                    o.DataRootPath = Path.GetTempPath();
                    o.RateLimits.Auth.PermitLimit = authPermitLimit;
                    o.RateLimits.Auth.WindowSeconds = 60;
                });
                builder.Services.AddRateLimiter(ServerRateLimiting.Configure);
            },
            app =>
            {
                app.UseRateLimiter();
                app.MapGet("/limited/auth", () => "ok").RequireRateLimiting(RateLimitPolicies.Auth);
                app.MapGet("/limited/commands", () => "ok").RequireRateLimiting(RateLimitPolicies.Commands);
                app.MapGet("/health/live", () => "live");
            });

    [Fact]
    public async Task Auth_Policy_Returns429_WithRetryAfter_AfterLimit()
    {
        await using var app = await StartAsync(authPermitLimit: 2);
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/limited/auth")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/limited/auth")).StatusCode);

        using var rejected = await client.GetAsync("/limited/auth");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = Assert.Single(rejected.Headers.GetValues("Retry-After"));
        Assert.True(int.Parse(retryAfter, System.Globalization.CultureInfo.InvariantCulture) > 0);
        using var doc = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("RATE_LIMITED", doc.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Policies_AreIndependent_AndUnlistedEndpointsAreNotLimited()
    {
        await using var app = await StartAsync(authPermitLimit: 1);
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/limited/auth")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/limited/auth")).StatusCode);

        // Exhausting `auth` never affects `commands` or an endpoint without a policy.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/limited/commands")).StatusCode);
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
    }

    [Fact]
    public void Defaults_MatchArchitectureWindows()
    {
        var limits = new ServerOptions().RateLimits;

        Assert.Equal(10, limits.ForPolicy(RateLimitPolicies.Auth).PermitLimit);
        Assert.Equal(120, limits.ForPolicy(RateLimitPolicies.Commands).PermitLimit);
        Assert.Equal(60, limits.ForPolicy(RateLimitPolicies.Sync).PermitLimit);
        Assert.Equal(60, limits.ForPolicy(RateLimitPolicies.Files).PermitLimit);
        Assert.Equal(30, limits.ForPolicy(RateLimitPolicies.Operator).PermitLimit);
    }

    [Fact]
    public void Validator_RejectsNonPositiveWindows_AndRelativeDataRoot()
    {
        var options = new ServerOptions { DataRootPath = "relative/path" };
        options.RateLimits.Files.PermitLimit = 0;
        options.RateLimits.Sync.WindowSeconds = -1;

        var result = new ServerOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("absolute", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("RateLimits:files:PermitLimit", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("RateLimits:sync:WindowSeconds", StringComparison.Ordinal));
    }
}
