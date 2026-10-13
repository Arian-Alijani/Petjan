using Animora.Server.Hosting;
using Xunit;

namespace Animora.Server.Tests.Hosting;

/// <summary>SEC-HSTS-01 / SEC-OPENAPI-01: boot-time contract for TLS termination and OpenAPI exposure.</summary>
public sealed class TlsContractTests
{
    private static ServerOptions Valid() => new() { DataRootPath = Path.GetTempPath() };

    [Fact]
    public void Defaults_AreValid_AppTerminatesTls_AndOpenApiIsOff()
    {
        var options = Valid();

        Assert.True(new ServerOptionsValidator().Validate(null, options).Succeeded);
        Assert.Equal(TlsTermination.App, options.Tls.Termination);
        Assert.False(options.EnableOpenApi);
    }

    [Fact]
    public void ProxyTermination_WithoutForwardedHeaders_FailsBoot_BecauseTheRedirectWouldLoop()
    {
        var options = Valid();
        options.Tls.Termination = TlsTermination.Proxy;

        var result = new ServerOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Tls:Termination", StringComparison.Ordinal));
    }

    [Fact]
    public void ProxyTermination_WithAllowlistedForwardedHeaders_IsValid()
    {
        var options = Valid();
        options.Tls.Termination = TlsTermination.Proxy;
        options.ForwardedHeaders.Enabled = true;
        options.ForwardedHeaders.KnownProxies = ["10.0.0.5"];

        Assert.True(new ServerOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void PublicHttpsPort_MustBeAValidPort(int port)
    {
        var options = Valid();
        options.Tls.PublicHttpsPort = port;

        var result = new ServerOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("PublicHttpsPort", StringComparison.Ordinal));
    }
}
