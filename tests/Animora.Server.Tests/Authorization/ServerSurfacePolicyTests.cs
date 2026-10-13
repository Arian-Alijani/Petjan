using Animora.Server.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Server.Tests.Authorization;

/// <summary>CONS-GATE-01: one route-class table feeds both the tenant-binding and entitlement planes.</summary>
public sealed class ServerSurfacePolicyTests
{
    // NOTE: the expected class is passed as its enum NAME (string), not the SurfaceClass value,
    // because SurfaceClass is `internal` and a `public` [Theory] method cannot take an internal
    // parameter type (CS0051). The name is parsed back to the internal enum inside the body.
    [Theory]
    [InlineData("/health/live", nameof(SurfaceClass.Health), true, true)]
    [InlineData("/health/ready", nameof(SurfaceClass.Health), true, true)]
    [InlineData("/admin/tenants", nameof(SurfaceClass.OperatorConsole), true, true)]
    [InlineData("/_blazor/negotiate", nameof(SurfaceClass.OperatorConsole), true, true)]
    [InlineData("/operator/tenants", nameof(SurfaceClass.Operator), true, true)]
    [InlineData("/provisioning/intents", nameof(SurfaceClass.Provisioning), true, true)]
    [InlineData("/auth/login", nameof(SurfaceClass.AuthAnonymous), true, true)]
    [InlineData("/auth/refresh", nameof(SurfaceClass.AuthAnonymous), true, true)]
    [InlineData("/auth/keys", nameof(SurfaceClass.AuthAnonymous), true, true)]
    [InlineData("/licensing/keys", nameof(SurfaceClass.LicensingKeys), true, true)]
    [InlineData("/auth/logout", nameof(SurfaceClass.AuthSession), false, true)]
    [InlineData("/auth/capabilities", nameof(SurfaceClass.AuthSession), false, true)]
    [InlineData("/auth/device/abc/approve", nameof(SurfaceClass.AuthSession), false, true)]
    [InlineData("/licensing/activate", nameof(SurfaceClass.Licensing), false, true)]
    [InlineData("/api/commands", nameof(SurfaceClass.TenantData), false, false)]
    [InlineData("/api/commands/batch", nameof(SurfaceClass.TenantData), false, false)]
    [InlineData("/sync/batches", nameof(SurfaceClass.TenantData), false, false)]
    [InlineData("/files/abc", nameof(SurfaceClass.TenantData), false, false)]
    [InlineData("/licensingx/activate", nameof(SurfaceClass.Unclassified), false, false)]
    [InlineData("/authx/login", nameof(SurfaceClass.Unclassified), false, false)]
    [InlineData("/", nameof(SurfaceClass.Unclassified), false, false)]
    public void Table_ClassifiesEveryPlaneDecision(string path, string expectedClass, bool skipsTenant, bool entitlementExempt)
    {
        var expected = Enum.Parse<SurfaceClass>(expectedClass);
        var p = new PathString(path);
        Assert.Equal(expected, ServerSurfacePolicy.Classify(p));
        Assert.Equal(skipsTenant, ServerSurfacePolicy.SkipsTenantBinding(p));
        Assert.Equal(entitlementExempt, ServerSurfacePolicy.IsEntitlementExempt(p));
    }

    [Fact]
    public void Matching_IsCaseInsensitive() =>
        Assert.Equal(SurfaceClass.AuthAnonymous, ServerSurfacePolicy.Classify(new PathString("/AUTH/Login")));

    [Fact]
    public void EveryClassSkippingTenantBinding_IsAlsoEntitlementExempt()
    {
        // A route that never gets a membership can never be entitlement-evaluated; the tables must not contradict.
        foreach (SurfaceClass c in Enum.GetValues<SurfaceClass>())
        {
            if (ServerSurfacePolicy.SkipsTenantBinding(c))
            {
                Assert.True(ServerSurfacePolicy.IsEntitlementExempt(c), c.ToString());
            }
        }
    }

    [Fact]
    public async Task EveryMappedAuthorizedEndpoint_HasADefinedClassification()
    {
        await using var host = new AuthzHost(new AuthzWorld());
        var sources = host.Services.GetServices<EndpointDataSource>();
        var endpoints = sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(endpoints);

        foreach (RouteEndpoint endpoint in endpoints)
        {
            string raw = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            // Strip route parameters so the template is a representative concrete path.
            string path = System.Text.RegularExpressions.Regex.Replace(raw, @"\{[^}]*\}", "x");
            bool protectedEndpoint = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            if (ServerSurfacePolicy.Classify(new PathString(path)) == SurfaceClass.Unclassified)
            {
                // Unclassified fails closed (tenant + entitlement), which is only acceptable for endpoints that
                // carry no authorization of their own (static assets, framework files).
                Assert.False(protectedEndpoint, $"Authorized endpoint '{raw}' has no ServerSurfacePolicy class.");
            }
        }
    }
}
