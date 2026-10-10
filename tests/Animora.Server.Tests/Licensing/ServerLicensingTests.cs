using System.Reflection;
using Animora.Contracts.Wire.Licensing;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Persistence.PostgreSql.Security;
using Animora.Server.Licensing;
using Animora.Server.Licensing.Issuance;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Animora.Server.Tests.Licensing;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal static class LicensingSource
{
    internal static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot.Find(), relative));
    internal static IEnumerable<string> All(params string[] roots) => roots
        .SelectMany(r => Directory.EnumerateFiles(Path.Combine(RepoRoot.Find(), r), "*.cs", SearchOption.AllDirectories))
        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
}

public sealed class ServerEntitlementGateTests
{
    [Theory]
    [InlineData("/health/live", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/auth/login", true)]
    [InlineData("/licensing/activate", true)]
    [InlineData("/licensing/keys", true)]
    [InlineData("/operator/tenants", true)]
    [InlineData("/provisioning/intents", true)]
    [InlineData("/admin/anything", true)]
    [InlineData("/api/commands", false)]
    [InlineData("/sync/pull", false)]
    [InlineData("/licensingx/activate", false)]
    [InlineData("/", false)]
    public void ExemptRoutes_Table(string path, bool exempt) =>
        Assert.Equal(exempt, ServerEntitlementGate.IsExempt(new PathString(path)));

    [Fact]
    public void PermissionPlane_RunsBeforeEntitlementPlane_AndGateIsNotCachedAcrossRequests()
    {
        var program = LicensingSource.Read("src/Animora.Server/Program.cs");
        var authorization = program.IndexOf("UseAuthorization()", StringComparison.Ordinal);
        var entitlement = program.IndexOf("UseMiddleware<EntitlementMiddleware>", StringComparison.Ordinal);
        Assert.True(authorization >= 0 && entitlement > authorization);
        var gate = LicensingSource.Read("src/Animora.Server/Licensing/ServerEntitlementGate.cs");
        Assert.DoesNotContain("IMemoryCache", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly Dictionary", gate, StringComparison.Ordinal);
        Assert.Contains("AnimoraEntitlementReadContext", gate, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void RealGate_ReplacesAllowAll_AndReadContextIsOnlyUsedByLicensing()
    {
        Assert.Contains("services.Replace(ServiceDescriptor.Scoped<IServerEntitlementGate, ServerEntitlementGate>())",
            LicensingSource.Read("src/Animora.Server/Licensing/LicensingServiceCollectionExtensions.cs"), StringComparison.Ordinal);
        foreach (var file in LicensingSource.All("src/Animora.Server", "src/Animora.Persistence.PostgreSql"))
        {
            if (!File.ReadAllText(file).Contains("AnimoraEntitlementReadContext", StringComparison.Ordinal)) continue;
            var normalized = file.Replace('\\', '/');
            Assert.True(normalized.Contains("/Licensing/", StringComparison.Ordinal) || normalized.EndsWith("AnimoraEntitlementReadContext.cs", StringComparison.Ordinal)
                || normalized.Contains("/Configurations/", StringComparison.Ordinal), normalized);
        }
    }
}

public sealed class LicenseIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BoundToDeviceHash_FromStore_NotBody()
    {
        // The wire request carries only the installation hash and the sequence: no tenant/device/user identifiers.
        var props = typeof(LicenseRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order().ToArray();
        Assert.Equal(["CurrentSequence", "InstallationIdHash"], props);
        var source = LicensingSource.Read("src/Animora.Persistence.PostgreSql/Licensing/LicenseIssuer.cs");
        Assert.Contains("device.InstallationIdHash, request.InstallationIdHash", source, StringComparison.Ordinal);
        Assert.Contains("LicenseDeviceMismatch", source, StringComparison.Ordinal);
        Assert.Contains("license.device_mismatch", source, StringComparison.Ordinal);
        Assert.Contains("DeviceStatus.Active", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SupersedesPrevious_SequenceMonotonic_InSource()
    {
        var source = LicensingSource.Read("src/Animora.Persistence.PostgreSql/Licensing/LicenseIssuer.cs");
        Assert.Contains("LicenseRecordStatus.Superseded", source, StringComparison.Ordinal);
        Assert.Contains("(previous?.Sequence ?? 0) + 1", source, StringComparison.Ordinal);
        Assert.Contains("request.CurrentSequence > (previous?.Sequence ?? 0)", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(100, 40)]
    [InlineData(40, 40)]
    [InlineData(10, 10)]
    [InlineData(1, 1)]
    public void TtlCappedAt40Days_AndGrantValidUntil(int grantDays, int expectedDays) =>
        Assert.Equal(Now.AddDays(expectedDays), LicenseIssuer.Expiry(Now, Now.AddDays(grantDays), new LicensingOptions().MaxIssueTtlDays));

    [Fact]
    public void Options_DefaultsAndBounds()
    {
        var o = new LicensingOptions();
        Assert.True(o.IsValid());
        Assert.Equal((2, 40, 14, 60), (o.DevicesPerSeat, o.MaxIssueTtlDays, o.OfflineGraceDays, o.ReadOnlyWindowDays));
        Assert.False(new LicensingOptions { MaxIssueTtlDays = 41 }.IsValid());
        Assert.False(new LicensingOptions { DevicesPerSeat = 0 }.IsValid());
    }

    [Fact]
    public void DeviceLimit_RevokedGrant_AndPreconditionOrder_InSource()
    {
        var s = LicensingSource.Read("src/Animora.Persistence.PostgreSql/Licensing/LicenseIssuer.cs");
        var notGranted = s.IndexOf("LicenseNotGranted", StringComparison.Ordinal);
        var revoked = s.IndexOf("LicenseRevoked", StringComparison.Ordinal);
        var device = s.IndexOf("DeviceRevoked", StringComparison.Ordinal);
        var limit = s.IndexOf("LicenseDeviceLimitReached", StringComparison.Ordinal);
        Assert.True(notGranted >= 0 && notGranted < revoked && revoked < device && device < limit);
        Assert.Contains("grant.MaxDevices", s, StringComparison.Ordinal);
    }
}

public sealed class LicenseGrantProjectorTests
{
    [Theory]
    [InlineData(SubscriptionStatus.Trial, LicenseGrantStatus.Active)]
    [InlineData(SubscriptionStatus.Active, LicenseGrantStatus.Active)]
    [InlineData(SubscriptionStatus.GracePeriod, LicenseGrantStatus.Active)]
    [InlineData(SubscriptionStatus.Suspended, LicenseGrantStatus.Suspended)]
    [InlineData(SubscriptionStatus.Expired, LicenseGrantStatus.Suspended)]
    [InlineData(SubscriptionStatus.Cancelled, LicenseGrantStatus.Revoked)]
    public void StatusMap_SuspendedExpiredCancelled(SubscriptionStatus input, LicenseGrantStatus expected) =>
        Assert.Equal(expected, LicenseGrant.MapStatus(input));

    [Fact]
    public void StatusMap_CoversEveryStatus() =>
        Assert.All(Enum.GetValues<SubscriptionStatus>(), s => Assert.True(Enum.IsDefined(LicenseGrant.MapStatus(s))));

    [Fact]
    public void OlderSourceEvent_NeverOverwritesNewer_InUpsert()
    {
        var s = LicensingSource.Read("src/Animora.Persistence.PostgreSql/Licensing/LicenseGrantProjector.cs");
        Assert.Contains("ON CONFLICT (\"TenantId\") DO UPDATE", s, StringComparison.Ordinal);
        Assert.Contains("\"LicenseGrants\".\"SourceEventSequence\" < EXCLUDED.\"SourceEventSequence\"", s, StringComparison.Ordinal);
        Assert.Contains("return Result.Ok(); // Deleted tenant", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Consumers_AreIdempotentProjections_ForIssueAndRecheck()
    {
        foreach (var file in new[] { "LicenseIssueConsumer.cs", "LicenseRecheckConsumer.cs" })
            Assert.Contains("projector.ProjectAsync(message.TenantId", LicensingSource.Read("src/Animora.Server/Licensing/" + file), StringComparison.Ordinal);
    }
}

public sealed class LicenseKeyTests
{
    [Fact]
    public void Rotation_KeepsOldKeyVerifiable120Days()
    {
        Assert.Equal(120, LicenseKeyManager.RetentionDays(new LicensingOptions()));
        var clock = new FixedClock(new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));
        var key = SigningKey.Create(SigningKey.Purposes.License, EcdsaLicenseSigner.Algorithm, [1], [2], clock);
        Assert.True(key.IsUsable(clock.UtcNow));
        key.RetireLicense(clock, clock.UtcNow.AddDays(120));
        Assert.False(key.IsUsable(clock.UtcNow));
        Assert.Equal(clock.UtcNow.AddDays(120), key.NotAfterUtc);
        Assert.Throws<ArgumentException>(() => SigningKey.Create(SigningKey.Purposes.Snapshot, "x", [1], [2], clock).RetireLicense(clock, clock.UtcNow.AddDays(1)));
    }

    [Fact]
    public void Bootstrap_IsSerialisedAndIdempotent_InSource()
    {
        var s = LicensingSource.Read("src/Animora.Persistence.PostgreSql/Licensing/LicenseKeyManager.cs");
        Assert.Contains("pg_advisory_xact_lock", s, StringComparison.Ordinal);
        Assert.Contains("if (!rotate && active.FirstOrDefault(k => k.IsUsable(clock.UtcNow)) is { } existing)", s, StringComparison.Ordinal);
        Assert.Contains("SigningKey.Purposes.License", s, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedKeyPair_SignsAndRoundTripsThroughSigner()
    {
        var (publicKey, privateKey) = EcdsaLicenseSigner.GenerateKeyPair();
        using var signer = new EcdsaLicenseSigner("K1", privateKey);
        var signature = Assert.IsType<byte[]>(signer.Sign("payload"u8).Value);
        Assert.Equal(64, signature.Length);
        Assert.NotEmpty(publicKey);
    }
}

public sealed class LicenseSecretsTests
{
    [Fact]
    public void TokenAndPrivateKey_NeverLoggedOrAudited()
    {
        var files = LicensingSource.All("src/Animora.Server/Licensing", "src/Animora.Persistence.PostgreSql/Licensing").ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            foreach (var line in File.ReadAllLines(file))
            {
                var isLogOrAudit = line.Contains("logger.", StringComparison.Ordinal) || line.Contains("Audit(", StringComparison.Ordinal)
                    || line.Contains("OperatorAudit.Create", StringComparison.Ordinal);
                if (!isLogOrAudit) continue;
                Assert.DoesNotContain("Token", line, StringComparison.Ordinal);
                Assert.DoesNotContain("PrivateKey", line, StringComparison.Ordinal);
                Assert.DoesNotContain("signature", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void LicenseRecord_PersistsDigestOnly_NeverToken()
    {
        var names = typeof(LicenseRecord).GetProperties().Select(p => p.Name).ToArray();
        Assert.Contains("PayloadSha256", names);
        Assert.DoesNotContain(names, n => n.Contains("Token", StringComparison.OrdinalIgnoreCase) || n.Contains("Signature", StringComparison.OrdinalIgnoreCase));
    }
}
