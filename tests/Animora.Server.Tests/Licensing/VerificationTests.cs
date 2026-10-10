using System.Text;
using Animora.Application.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Server.Licensing.Issuance;
using Animora.Server.Licensing.Verification;
using Xunit;

namespace Animora.Server.Tests.Licensing;

internal static class LicenseSamples
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    internal static LicensePayload Payload() => new(1, IdGenerator.NewId(), IdGenerator.NewId(), "clinic", IdGenerator.NewId(),
        "standard", 5, 10, new string('a', 64), IdGenerator.NewId(), 1, Now, Now.AddMinutes(-5), Now.AddDays(40), Now.AddDays(54), []);
    internal static (string Token, LicenseKeySet Keys) Sign(LicensePayload p)
    {
        var pair = EcdsaLicenseSigner.GenerateKeyPair(); var id = IdGenerator.NewId();
        try
        {
            using var signer = new EcdsaLicenseSigner(id, pair.PrivateKeyPkcs8);
            var bytes = LicenseCanonicalPayload.Serialize(p);
            var signature = Assert.IsType<byte[]>(signer.Sign(bytes).Value);
            return (LicenseEnvelope.Format(bytes, signature, id), new([new(id, pair.PublicKeyDer, Now.AddDays(-1), Now.AddDays(120))]));
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(pair.PrivateKeyPkcs8); }
    }
}

public sealed class LicenseEnvelopeTests
{
    [Fact]
    public void RoundTrip_AndStrictParsing()
    {
        var signed = LicenseSamples.Sign(LicenseSamples.Payload());
        Assert.True(LicenseEnvelope.Parse(signed.Token).IsSuccess);
        foreach (var token in new[] { "", "x.y.z", signed.Token + ".extra", new string('a', 4097), signed.Token.Replace(".", "=.") })
            Assert.Equal(ErrorCodes.LicenseInvalid, LicenseEnvelope.Parse(token).Error);
        Assert.False(Base64Url.TryDecode("AB", out _));
        Assert.False(Base64Url.TryDecode("AA=", out _));
    }
}
public sealed class LicenseVerifierTests
{
    [Fact]
    public void Signature_Tampering_UnknownKey_Rotation_AndFutureVersion()
    {
        var p = LicenseSamples.Payload(); var signed = LicenseSamples.Sign(p);
        var parsed = Assert.IsType<ParsedLicense>(LicenseEnvelope.Parse(signed.Token).Value);
        Assert.True(LicenseVerifier.Verify(parsed, signed.Keys).IsSuccess);
        Assert.Equal(ErrorCodes.LicenseKeyUnknown, LicenseVerifier.Verify(parsed, new LicenseKeySet([])).Error);
        var tampered = parsed.PayloadBytes.ToArray(); tampered[0] ^= 1;
        Assert.Equal(ErrorCodes.LicenseInvalid, LicenseVerifier.Verify(parsed with { PayloadBytes = tampered }, signed.Keys).Error);
        var oldKey = Assert.IsType<LicensePublicKey>(signed.Keys.Find(parsed.KeyId));
        Assert.True(LicenseVerifier.Verify(parsed, new LicenseKeySet([oldKey with { NotAfterUtc = p.IssuedUtc }])).IsSuccess);
        Assert.True(LicenseVerifier.Verify(parsed, new LicenseKeySet([oldKey with { NotAfterUtc = p.IssuedUtc.AddTicks(-1) }])).IsFailure);
        var future = LicenseSamples.Sign(p with { V = 2 });
        Assert.True(LicenseVerifier.Verify(Assert.IsType<ParsedLicense>(LicenseEnvelope.Parse(future.Token).Value), future.Keys).IsFailure);
    }
}
public sealed class LicensePolicyTests
{
    [Theory]
    [InlineData(0, LicenseState.Valid)]
    [InlineData(40, LicenseState.Valid)]
    [InlineData(41, LicenseState.Grace)]
    [InlineData(54, LicenseState.Grace)]
    [InlineData(55, LicenseState.ReadOnly)]
    [InlineData(114, LicenseState.ReadOnly)]
    [InlineData(115, LicenseState.Locked)]
    public void Matrix_ValidGraceReadOnlyLocked_Table(int days, LicenseState state)
    {
        var p = LicenseSamples.Payload(); var now = LicenseSamples.Now.AddDays(days);
        Assert.Equal(state, LicensePolicy.Evaluate(p, now, new(now), p.InstallationIdHash).State);
    }
    [Fact]
    public void WrongInstallation_ClockRollback_AndVerificationFailure_FailClosed()
    {
        var p = LicenseSamples.Payload(); var now = LicenseSamples.Now;
        Assert.Equal(LicenseState.Locked, LicensePolicy.Evaluate(p, now, new(now), new string('b', 64)).State);
        var water = new HighWaterClock(now.AddDays(2));
        var rollback = LicensePolicy.Evaluate(p, now, water, p.InstallationIdHash);
        Assert.Equal(LicenseState.ReadOnly, rollback.State); Assert.True(rollback.ClockRollbackDetected);
        Assert.Equal(LicenseState.Grace, LicensePolicy.Evaluate(p, now, new(now.AddDays(41)), p.InstallationIdHash).State);
        Assert.Equal(LicenseState.Locked, LicensePolicy.FromVerificationFailure(ErrorCodes.LicenseInvalid).State);
        Assert.Equal(water, water.Advance(now)); Assert.Equal(now.AddDays(3), water.Advance(now.AddDays(3)).HighWaterUtc);
    }
}
public sealed class CapabilityGateTests
{
    [Fact]
    public void RiskByState_Table()
    {
        foreach (var risk in Enum.GetValues<RiskLevel>())
        {
            Assert.True(CapabilityGate.IsAllowed(LicenseState.Valid, risk));
            Assert.Equal(risk != RiskLevel.Critical, CapabilityGate.IsAllowed(LicenseState.Grace, risk));
            Assert.False(CapabilityGate.IsAllowed(LicenseState.ReadOnly, risk));
            Assert.True(CapabilityGate.IsAllowed(LicenseState.ReadOnly, risk, isRead: true));
            Assert.False(CapabilityGate.IsAllowed(LicenseState.Locked, risk, isRead: true));
        }
    }
}
public sealed class DeviceFingerprintTests
{
    [Fact]
    public void StableOrderIndependent_RawNeverExposed_RejectsAmbiguousComponents()
    {
        var hash = DeviceFingerprint.Compute(["machine-a", "serial-b"]);
        Assert.Equal(hash, DeviceFingerprint.Compute([" SERIAL-B ", "MACHINE-A"]));
        Assert.Matches("^[a-f0-9]{64}$", hash); Assert.DoesNotContain("machine", hash);
        Assert.Throws<ArgumentException>(() => DeviceFingerprint.Compute([]));
        Assert.Throws<ArgumentException>(() => DeviceFingerprint.Compute(["a", " A "]));
        Assert.Throws<ArgumentException>(() => DeviceFingerprint.Compute(["a\u001fb"]));
    }
}
public sealed class SealedEntitlementMergeTests
{
    [Fact]
    public void NeverLowersHighWaterOrSequence_AndScopeMismatchFails()
    {
        var p = LicenseSamples.Payload(); var signed = LicenseSamples.Sign(p);
        var existing = new SealedEntitlement(signed.Token, 200, 2, p.TenantId, p.InstallationIdHash, 200);
        Assert.True(SealedEntitlementMerge.Merge(existing, existing with { HighestSequenceSeen = 1 }).IsFailure);
        var merge = Assert.IsType<SealedEntitlement>(SealedEntitlementMerge.Merge(existing, existing with { HighWaterUnixMs = 100, WrittenUnixMs = 100 }).Value);
        Assert.Equal(200, merge.HighWaterUnixMs); Assert.Equal(200, merge.WrittenUnixMs);
        Assert.True(SealedEntitlementMerge.Merge(existing, existing with { TenantId = "other" }).IsFailure);
    }
}
public sealed class LicensePayloadTests
{
    [Fact]
    public void CanonicalJson_ParityWithCommandFingerprintCanonicalizer()
    {
        var p = LicenseSamples.Payload();
        Assert.Equal(CommandFingerprint.CanonicalJson(LicenseCanonicalPayload.Fields(p)), Encoding.UTF8.GetString(LicenseCanonicalPayload.Serialize(p)));
        Assert.Contains("2026-10-10T00:00:00.0000000", Encoding.UTF8.GetString(LicenseCanonicalPayload.Serialize(p)));
    }
}
