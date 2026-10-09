using System.Net;
using System.Net.Http.Json;
using Animora.Contracts.Wire.Auth;
using Animora.Server.Signing;
using Xunit;

namespace Animora.Server.Tests.Authorization;

public sealed class KeysEndpointTests
{
    [Fact]
    public async Task ExposesPublicOnly_NeverPrivate()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);

        using HttpResponseMessage response = await host.Client().GetAsync("/auth/keys");
        string raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PublicKeySet set = System.Text.Json.JsonSerializer.Deserialize<PublicKeySet>(raw, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        PublicKeyInfo key = Assert.Single(set.Keys);
        Assert.Equal(world.Signer.Key.KeyId, key.KeyId);
        Assert.Equal(EcdsaSnapshotSigner.AlgorithmName, key.Algorithm);
        Assert.Equal(Convert.ToBase64String(world.Signer.Key.PublicKeyDer), key.PublicKeyDerBase64);

        // No private / protected material, in any encoding, and no private-looking property names.
        Assert.DoesNotContain(Convert.ToBase64String(world.Signer.PrivateKeyPkcs8), raw, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(world.Signer.ProtectedPrivateKey), raw, StringComparison.Ordinal);
        Assert.DoesNotContain("private", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protected", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IsAnonymous_AndNeverTenantBound()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        world.SetTenantStatus(Animora.Domain.Identity.TenantStatus.Suspended);

        // Neither anonymous nor a token of a suspended tenant is stopped by tenant binding on this public route.
        using HttpResponseMessage anonymous = await host.Client().GetAsync("/auth/keys");
        using HttpResponseMessage withToken = await host.Client(host.Token(world.Alice, world.AliceDevice)).GetAsync("/auth/keys");

        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Contains("max-age", string.Join(',', anonymous.Headers.GetValues("Cache-Control")), StringComparison.Ordinal);
    }
}
