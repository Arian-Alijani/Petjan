using System.Net;
using System.Text.Json;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Animora.Server.Tests;

public sealed class GlobalExceptionMiddlewareTests
{
    private static Task<WebApplication> StartAsync(string environment)
        => TestHostFactory.StartAsync(
            environment,
            _ => { },
            app =>
            {
                app.UseMiddleware<GlobalExceptionMiddleware>();
                app.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException("kaboom-internal-detail")));
                app.MapGet("/ok", () => "fine");
            });

    [Fact]
    public async Task UnhandledException_Returns500WireApiError_NoStack_InProduction()
    {
        await using var app = await StartAsync("Production");
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/boom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("UNEXPECTED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("detail").ValueKind);
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain("kaboom-internal-detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledException_ExposesMessage_InDevelopmentOnly()
    {
        await using var app = await StartAsync("Development");
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/boom");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("kaboom-internal-detail", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Echoes_CorrelationId()
    {
        await using var app = await StartAsync("Production");
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Add("X-Correlation-Id", "abc-123_XYZ");
        using var response = await client.SendAsync(request);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("abc-123_XYZ", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
        Assert.Equal("abc-123_XYZ", doc.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("bad/char")]
    public async Task InvalidInboundCorrelationId_IsReplacedWithGeneratedUlid(string inbound)
    {
        await using var app = await StartAsync("Production");
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/ok");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", inbound);
        using var response = await client.SendAsync(request);

        var echoed = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        Assert.NotEqual(inbound, echoed);
        Assert.Equal(26, echoed.Length);
    }

    [Fact]
    public async Task OverlongInboundCorrelationId_IsReplaced()
    {
        await using var app = await StartAsync("Production");
        using var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/ok");
        request.Headers.Add("X-Correlation-Id", new string('a', 65));
        using var response = await client.SendAsync(request);

        Assert.Equal(26, Assert.Single(response.Headers.GetValues("X-Correlation-Id")).Length);
    }
}
