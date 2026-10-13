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
    [Theory]
    [InlineData("/health/live", SurfaceClass.Health, true, true)]
    [InlineData("/health/ready", SurfaceClass.Health, true, true)]
    [InlineData("/admin/tenants", SurfaceClass.OperatorConsole, true, true)]
    [InlineData("/_blazor/negotiate", SurfaceClass.OperatorConsole, true, true)]
    [InlineData("/operator/tenants", SurfaceClass.Operator, true, true)]
    [InlineData("/provisioning/intents", SurfaceClass.Provisioning, true, true)]
    [InlineData("/auth/login", SurfaceClass.AuthAnonymous, true, true)]
    [InlineData("/auth/refresh", SurfaceClass.AuthAnonymous, true, true)]
    [InlineData("/auth/keys", SurfaceClass.AuthAnonymous, true, true)]
    [InlineData("/licensing/keys", SurfaceClass.LicensingKeys, true, true)]
    [InlineData("/auth/logout", SurfaceClass.AuthSession, false, true)]
    [InlineData("/auth/capabilities", SurfaceClass.AuthSession, false, true)]
    [InlineData("/auth/device/abc/approve", SurfaceClass.AuthSession, false, true)]
    [InlineData("/licensing/activate", SurfaceClass.Licensing, false, true)]
    [InlineData("/api/commands", SurfaceClass.TenantData, false, false)]
    [InlineData("/api/commands/batch", SurfaceClass.TenantData, false, false)]
    [InlineData("/sync/batches", SurfaceClass.TenantData, false, false)]
    [InlineData("/files/abc", SurfaceClass.TenantData, false, false)]
    [InlineData("/licensingx/activate", SurfaceClass.Unclassified, false, false)]
    [InlineData("/authx/login", SurfaceClass.Unclassified, false, false)]
    [InlineData("/", SurfaceClass.Unclassified, false, false)]
    public void Table_ClassifiesEveryPlaneDecision(string path, SurfaceClass expected, bool skipsTenant, bool entitlementExempt)
    {
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
