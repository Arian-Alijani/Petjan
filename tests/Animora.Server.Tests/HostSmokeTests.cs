using System.Net;
using System.Text.Json;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    private sealed class HealthyProbe(string name) : IReadinessProbe
    {
        public string Name { get; } = name;

        public Task<ReadinessResult> CheckAsync(CancellationToken ct) => Task.FromResult(ReadinessResult.Healthy());
    }

    // stubDatabaseProbes: replace the PostgreSQL-backed probes (S02) with healthy stubs so these
    // tests keep isolating the data-root probe without needing a database.
    private WebApplicationFactory<Program> CreateFactory(string dataRoot, bool stubDatabaseProbes = true)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Server:DataRootPath"] = dataRoot,
                }));

            if (stubDatabaseProbes)
            {
                builder.ConfigureServices(services =>
                {
                    string[] dbProbes = ["database", "keyring", "schema"];
                    var real = services
                        .Where(d => d.ServiceType == typeof(IReadinessProbe)
                                    && (d.ImplementationType == typeof(DatabaseProbe)
                                        || d.ImplementationType == typeof(KeyRingProbe)
                                        || d.ImplementationType == typeof(SchemaProbe)))
                        .ToList();
                    foreach (var descriptor in real)
                    {
                        services.Remove(descriptor);
                    }

                    foreach (var name in dbProbes)
                    {
                        services.AddSingleton<IReadinessProbe>(new HealthyProbe(name));
                    }
                });
            }
        });

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
        var failed = doc.RootElement.GetProperty("failed").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new List<string?> { "data-root" }, failed);
        Assert.DoesNotContain(missing, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ready_Returns503_NamingDatabaseProbes_WhenPostgreSqlIsAbsent()
    {
        // No Database:ConnectionString anywhere: the host must still boot (probes degrade, never throw).
        using var factory = CreateFactory(_dataRoot, stubDatabaseProbes: false);
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        var failed = doc.RootElement.GetProperty("failed").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new List<string?> { "database", "keyring", "schema" }, failed);
    }
}
