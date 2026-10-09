using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Animora.Contracts.Wire.Auth;
using Animora.Server.Auth;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Server.Tests.Identity;

/// <summary>HTTP-level behaviour of /auth/* over the real pipeline with the persistence seams replaced by fakes.</summary>
public sealed class AuthEndpointsTests : IDisposable
{
    private readonly string _dataRoot = Directory.CreateTempSubdirectory("animora-auth-test-").FullName;

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

    private WebApplicationFactory<Program> CreateFactory(AuthHarness harness, int authPermitLimit = 10)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Server:DataRootPath"] = _dataRoot,
                    ["Server:RateLimits:Auth:PermitLimit"] = authPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Server:RateLimits:Auth:WindowSeconds"] = "60",
                }));

            builder.ConfigureServices(services =>
            {
                // The real handlers run; only the persistence-backed LoginService is swapped for the in-memory one.
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(LoginService)).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddScoped(_ => harness.NewRequestScope());
            });
        });

    [Fact]
    public async Task Login_RateLimited_Returns429()
    {
        var harness = new AuthHarness();
        await using var factory = CreateFactory(harness, authPermitLimit: 3);
        using var client = factory.CreateClient();

        for (int i = 0; i < 3; i++)
        {
            using var ok = await client.PostAsJsonAsync("/auth/login", harness.Login(password: "Wrong-pass-1"));
            Assert.Equal(HttpStatusCode.Unauthorized, ok.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/auth/login", harness.Login());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        using var doc = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        Assert.Equal("RATE_LIMITED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Failure_Returns_WireApiError_WithCorrelationId()
    {
        var harness = new AuthHarness();
        await using var factory = CreateFactory(harness);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(harness.Login(password: "Wrong-pass-1")),
        };
        request.Headers.Add("X-Correlation-Id", "corr-abc-123");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("corr-abc-123", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CREDENTIALS_INVALID", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("corr-abc-123", doc.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("detail").ValueKind);
        Assert.Equal(0, doc.RootElement.GetProperty("fields").GetArrayLength());
    }

    [Fact]
    public async Task Login_Success_Returns200_NoStore_AndTokens()
    {
        var harness = new AuthHarness();
        await using var factory = CreateFactory(harness);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/auth/login", harness.Login());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body.AccessToken));
        Assert.False(string.IsNullOrEmpty(body.RefreshToken));
        Assert.Equal(harness.TenantId, body.TenantId);
    }

    [Theory]
    [InlineData("/auth/logout")]
    [InlineData("/auth/device/register")]
    public async Task AuthenticatedEndpoints_WithoutBearer_Return401_SessionExpired(string path)
    {
        var harness = new AuthHarness();
        await using var factory = CreateFactory(harness);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(path, new DeviceRegisterRequest("PC", "hash", "windows"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SESSION_EXPIRED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_WithGarbageToken_Returns401()
    {
        var harness = new AuthHarness();
        await using var factory = CreateFactory(harness);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest("garbage"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("USER_LOCKED_OUT", 423)]
    [InlineData("DEVICE_REVOKED", 403)]
    [InlineData("USER_DISABLED", 403)]
    [InlineData("TENANT_SUSPENDED", 403)]
    [InlineData("CREDENTIALS_INVALID", 401)]
    [InlineData("SESSION_EXPIRED", 401)]
    [InlineData("VALIDATION_FAILED", 400)]
    public void StatusMapping_MatchesContract(string code, int status)
    {
        var error = new Animora.Domain.Results.ErrorCode(code, Animora.Domain.Results.ErrorCode.Forbidden);

        Assert.Equal(status, AuthEndpoints.StatusFor(error));
    }
}
