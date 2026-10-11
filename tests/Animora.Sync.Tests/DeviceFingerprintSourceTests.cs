using Animora.Client.Services.Identity;
using Animora.Infrastructure.Local.Identity;
using Animora.Server.Licensing.Verification;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G06: the installation id is a one-way hash; raw machine components never leave the machine.</summary>
public sealed class DeviceFingerprintSourceTests
{
    [Fact]
    public async Task RawComponentsNeverLeave_OnlyHashSent()
    {
        using var stack = await OnlineStack.CreateAsync();

        Assert.True((await stack.Client.LoginAsync(FakeIdentityServer.TenantCode, FakeIdentityServer.UserName, FakeIdentityServer.Password)).IsSuccess);

        var expected = DeviceFingerprint.Compute(OnlineStack.RawComponents);
        Assert.Matches("^[a-f0-9]{64}$", expected);
        foreach (var request in stack.Server.Handler.Requests)
        {
            foreach (var raw in OnlineStack.RawComponents)
                Assert.DoesNotContain(raw.Split(':', 2)[1], request.Body, StringComparison.OrdinalIgnoreCase);
        }

        var login = stack.Server.Handler.Requests.First(r => r.Path == "auth/login");
        Assert.Contains(expected, login.Body, StringComparison.Ordinal);
        Assert.Contains(expected, stack.Server.Handler.Requests.First(r => r.Path == "auth/device/register").Body, StringComparison.Ordinal);
        Assert.Contains(expected, stack.Server.Handler.Requests.First(r => r.Path == "licensing/activate").Body, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallationHash_IsStable_AndOrderIndependent()
    {
        var a = InstallationHash.Compute(new Animora.Persistence.Tests.FixedFingerprintSource("a:1", "b:2", "c:3"));
        var b = InstallationHash.Compute(new Animora.Persistence.Tests.FixedFingerprintSource("c:3", "a:1", "b:2"));
        var other = InstallationHash.Compute(new Animora.Persistence.Tests.FixedFingerprintSource("a:1", "b:2", "c:4"));

        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void DefaultSource_CollectsLabelledUniqueNonEmptyComponents_AndNeverThrows()
    {
        var parts = new DeviceFingerprintSource().Collect();

        Assert.NotEmpty(parts);
        Assert.All(parts, p => Assert.Contains(':', p));
        Assert.Equal(parts.Count, parts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Matches("^[a-f0-9]{64}$", DeviceFingerprint.Compute(parts)); // always hashable
    }
}
