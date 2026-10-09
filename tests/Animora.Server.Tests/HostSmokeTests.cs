using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Animora.Server.Tests;

public sealed class HostSmokeTests : IDisposable
{
    private readonly string _dataRoot = Directory.CreateTempSubdirectory("animora-server-test-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(string dataRoot)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Server:DataRootPath"] = dataRoot,
                })));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Live_Returns200_WithoutDependencies()
    {
        // DataRoot deliberately points at a missing directory: liveness must not care.
        using var factory = CreateFactory(Path.Combine(_dataRoot, "does-not-exist"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("live", (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_Returns200_WhenDataRootWritable()
    {
        using var factory = CreateFactory(_dataRoot);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ready", (await ReadJsonAsync(response)).GetProperty("status").GetString());
        Assert.Empty(Directory.GetFiles(_dataRoot, ".readiness-*"));
    }

    [Fact]
    public async Task Ready_Returns503_NamesFailedProbeOnly_WhenDataRootMissing()
    {
        var missing = Path.Combine(_dataRoot, "does-not-exist");
        using var factory = CreateFactory(missing);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("not-ready", doc.RootElement.GetProperty("status").GetString());
        var failed = doc.RootElement.GetProperty("failed").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(["data-root"], failed);
        Assert.DoesNotContain(missing, body, StringComparison.Ordinal);
    }
}
