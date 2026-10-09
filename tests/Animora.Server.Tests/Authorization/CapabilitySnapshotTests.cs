using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Identity;
using Animora.Server.Auth;
using Animora.Server.Authorization;
using Animora.Server.Signing;
using Xunit;

namespace Animora.Server.Tests.Authorization;

/// <summary>Offline capability snapshots: Critical codes omitted, bounded lifetime, verifiable detached signature.</summary>
public sealed class CapabilitySnapshotTests
{
    private static readonly DateTimeOffset Issued = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private static ResolvedMembership Actor(StaffRole role = StaffRole.Manager) =>
        new("tenant", role, "user", "device", DeviceActive: true);

    [Fact]
    public void CriticalPermissions_Omitted()
    {
        CapabilitySnapshot snapshot = DeviceAdminService.BuildSnapshot(
            Actor(),
            "stamp",
            [PermissionCodes.PatientsRead, PermissionCodes.SalesPost, PermissionCodes.DevicesManage, PermissionCodes.SalesVoid, "not.in.catalog"],
            Issued);

        Assert.Contains(PermissionCodes.PatientsRead, snapshot.CapabilityExpiry.Keys);
        Assert.Contains(PermissionCodes.SalesPost, snapshot.CapabilityExpiry.Keys);
        Assert.DoesNotContain(PermissionCodes.DevicesManage, snapshot.CapabilityExpiry.Keys);
        Assert.DoesNotContain(PermissionCodes.SalesVoid, snapshot.CapabilityExpiry.Keys);
        Assert.DoesNotContain("not.in.catalog", snapshot.CapabilityExpiry.Keys);
        Assert.All(snapshot.CapabilityExpiry.Keys, code => Assert.NotEqual(RiskLevel.Critical, PermissionCodes.RiskOf(code)));
    }

    [Fact]
    public void SnapshotExpiry_IsEarliestCapabilityExpiry_CappedAt30Days()
    {
        CapabilitySnapshot withHigh = DeviceAdminService.BuildSnapshot(Actor(), "s", [PermissionCodes.PatientsRead, PermissionCodes.SalesPost], Issued);
        CapabilitySnapshot lowOnly = DeviceAdminService.BuildSnapshot(Actor(), "s", [PermissionCodes.PatientsRead], Issued);
        CapabilitySnapshot empty = DeviceAdminService.BuildSnapshot(Actor(), "s", [PermissionCodes.DevicesManage], Issued);

        Assert.Equal(withHigh.CapabilityExpiry.Values.Min(), withHigh.SnapshotExpiryUtc);
        Assert.True(withHigh.SnapshotExpiryUtc <= Issued + DeviceAdminService.MaxSnapshotLifetime);
        Assert.True(lowOnly.SnapshotExpiryUtc <= Issued + DeviceAdminService.MaxSnapshotLifetime);
        Assert.Equal(Issued + DeviceAdminService.MaxSnapshotLifetime, empty.SnapshotExpiryUtc);
        Assert.Empty(empty.CapabilityExpiry);
    }

    [Fact]
    public void Canonical_IsDeterministic_SortedAndSignatureFieldIsExcluded()
    {
        CapabilitySnapshot a = DeviceAdminService.BuildSnapshot(Actor(), "s", [PermissionCodes.SalesRead, PermissionCodes.PatientsRead], Issued);
        CapabilitySnapshot b = DeviceAdminService.BuildSnapshot(Actor(), "s", [PermissionCodes.PatientsRead, PermissionCodes.SalesRead], Issued);

        string json = CapabilitySnapshotCanonicalizer.CanonicalizeToString(a);

        Assert.Equal(json, CapabilitySnapshotCanonicalizer.CanonicalizeToString(b));
        Assert.DoesNotContain("signature", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(json.IndexOf(PermissionCodes.PatientsRead, StringComparison.Ordinal) < json.IndexOf(PermissionCodes.SalesRead, StringComparison.Ordinal));
    }

    [Fact]
    public void SignatureVerifiesWithPublicKey_AndTamperFails()
    {
        (byte[] publicKey, byte[] pkcs8) = EcdsaSnapshotSigner.GenerateKeyPair();
        using EcdsaSnapshotSigner signer = EcdsaSnapshotSigner.FromPkcs8("KEY", pkcs8);
        byte[] payload = CapabilitySnapshotCanonicalizer.Canonicalize(
            DeviceAdminService.BuildSnapshot(Actor(), "s", [PermissionCodes.PatientsRead], Issued));

        string signature = signer.Sign(payload).Value!;

        Assert.True(EcdsaSnapshotSigner.VerifyWithPublicKey(publicKey, payload, signature));
        Assert.True(signer.Verify(payload, signature));

        byte[] tampered = (byte[])payload.Clone();
        tampered[10] ^= 0x01;
        Assert.False(EcdsaSnapshotSigner.VerifyWithPublicKey(publicKey, tampered, signature));
        Assert.False(EcdsaSnapshotSigner.VerifyWithPublicKey(publicKey, payload, "AAAA"));
        Assert.False(EcdsaSnapshotSigner.VerifyWithPublicKey(publicKey, payload, string.Empty));

        (byte[] otherPublic, _) = EcdsaSnapshotSigner.GenerateKeyPair();
        Assert.False(EcdsaSnapshotSigner.VerifyWithPublicKey(otherPublic, payload, signature));
    }

    [Fact]
    public async Task Endpoint_IssuesSignedSnapshot_VerifiableWithPublishedKey_NoCriticalCodes()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        using HttpClient client = host.Client(host.Token(world.Alice, world.AliceDevice));

        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CapabilitySnapshotResponse snapshot = (await response.Content.ReadFromJsonAsync<CapabilitySnapshotResponse>())!;

        using HttpResponseMessage keysResponse = await host.Client().GetAsync("/auth/keys");
        PublicKeySet keys = (await keysResponse.Content.ReadFromJsonAsync<PublicKeySet>())!;
        PublicKeyInfo key = Assert.Single(keys.Keys, k => k.KeyId == snapshot.KeyId);

        byte[] payload = Encoding.UTF8.GetBytes(snapshot.PayloadJson);
        byte[] publicKey = Convert.FromBase64String(key.PublicKeyDerBase64);
        Assert.True(EcdsaSnapshotSigner.VerifyWithPublicKey(publicKey, payload, snapshot.Signature));
        Assert.False(EcdsaSnapshotSigner.VerifyWithPublicKey(publicKey, Encoding.UTF8.GetBytes(snapshot.PayloadJson + " "), snapshot.Signature));

        using JsonDocument doc = JsonDocument.Parse(snapshot.PayloadJson);
        JsonElement caps = doc.RootElement.GetProperty("capabilityExpiry");
        Assert.True(caps.TryGetProperty(PermissionCodes.PatientsRead, out _));
        Assert.False(caps.TryGetProperty(PermissionCodes.DevicesManage, out _));
        Assert.Equal(world.Alice.Id, doc.RootElement.GetProperty("userId").GetString());
        Assert.Equal(world.AliceDevice.Id, doc.RootElement.GetProperty("deviceId").GetString());
        Assert.Contains(world.Audit.Entries, e => e.Action == DeviceAdminService.CapabilitiesIssuedAction);
    }

    [Fact]
    public async Task PendingDevice_CannotRequestCapabilities()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        Device pending = world.AddDevice(world.Alice, approve: false);
        using HttpClient client = host.Client(host.Token(world.Alice, pending));

        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
