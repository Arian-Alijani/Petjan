using System.Text;
using Animora.Server.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Animora.Server.Tests;

public sealed class SecretGuardTests
{
    private static IConfigurationRoot FromJson(string json)
        => new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

    [Fact]
    public void Throws_ForLiteralConnectionStringInJson()
    {
        var config = FromJson("""
            { "ConnectionStrings": { "Main": "Host=db;Database=a;Username=u;Password=hunter2" },
              "Db": { "ConnectionString": "Host=db;Password=hunter2;" } }
            """);

        var ex = Assert.Throws<SecretInConfigurationException>(
            () => SecretGuard.ThrowIfSecretsInJsonFiles(config));

        Assert.DoesNotContain("hunter2", ex.Message, StringComparison.Ordinal);
        Assert.Equal("Db:ConnectionString", ex.KeyPath);
    }

    [Fact]
    public void Throws_ForNonEmptySecretNamedKeyInJson()
    {
        var config = FromJson("""{ "Auth": { "ApiKey": "abc123" } }""");

        var ex = Assert.Throws<SecretInConfigurationException>(
            () => SecretGuard.ThrowIfSecretsInJsonFiles(config));

        Assert.Equal("Auth:ApiKey", ex.KeyPath);
        Assert.DoesNotContain("abc123", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Allows_NonSecretJsonAndEmptyPlaceholders()
    {
        var config = FromJson("""
            { "Server": { "EnableOpenApi": false, "RateLimits": { "auth": { "PermitLimit": 10 } } },
              "Auth": { "Password": "" } }
            """);

        SecretGuard.ThrowIfSecretsInJsonFiles(config);
    }

    [Fact]
    public void Allows_EnvironmentProvider()
    {
        // In-memory stands in for the environment-variable / user-secrets providers:
        // only JSON providers are scanned.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Main"] = "Host=db;Database=a;Username=u;Password=hunter2",
                ["Auth:Secret"] = "s3cret",
            })
            .Build();

        SecretGuard.ThrowIfSecretsInJsonFiles(config);
    }

    [Fact]
    public void ShippedAppsettings_ContainNoSecrets()
    {
        var root = RepoRoot.Find();
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "Animora.Server", "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(root, "src", "Animora.Server", "appsettings.Development.json"), optional: false)
            .Build();

        SecretGuard.ThrowIfSecretsInJsonFiles(config);
    }
}
