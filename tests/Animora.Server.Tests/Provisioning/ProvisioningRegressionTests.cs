using System.Security.Cryptography;
using System.Text;
using Animora.Persistence.PostgreSql.Provisioning;
using Animora.Server.Provisioning;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Animora.Server.Tests.Provisioning;

public sealed class RequestSignatureVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public void ValidSignature_Tampering_UnknownKey_AndSkew()
    {
        var secret = new byte[32]; var body = Encoding.UTF8.GetBytes("{\"name\":\"clinic\"}");
        var timestamp = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(
            RequestSignatureVerifier.CanonicalRequest("POST", "/provisioning/intents", timestamp, body))));
        bool Verify(byte[]? key, byte[] bytes, DateTimeOffset time, string path = "/provisioning/intents") =>
            RequestSignatureVerifier.VerifySignature(key, "POST", path, timestamp, signature, bytes, time);
        Assert.True(Verify(secret, body, Now));
        Assert.True(Verify(secret, body, Now.AddMinutes(5)));
        Assert.False(Verify(secret, body, Now.AddMinutes(5).AddSeconds(1)));
        Assert.False(Verify(secret, body, Now.AddMinutes(-6)));
        Assert.False(Verify(secret, Encoding.UTF8.GetBytes("{}"), Now));
        Assert.False(Verify(null, body, Now));
        Assert.False(Verify(secret, body, Now, "/provisioning/intents/other"));
        Assert.False(RequestSignatureVerifier.VerifySignature(secret, "GET", "/", "99999999999999999", new string('a', 64), [], Now));
    }
    [Fact]
    public void ReplayWindow_Bounded_Expires_AndScopesKeys()
    {
        var window = new ReplayWindow();
        Assert.True(window.TryAccept("key", "AB", Now));
        Assert.False(window.TryAccept("key", "ab", Now));
        Assert.True(window.TryAccept("other", "ab", Now));
        for (var i = 2; i < ReplayWindow.Capacity; i++) Assert.True(window.TryAccept("key", i.ToString(), Now));
        Assert.Equal(ReplayWindow.Capacity, window.Count);
        Assert.False(window.TryAccept("key", "overflow", Now));
        Assert.True(window.TryAccept("key", "ab", Now.AddMinutes(5)));
        Assert.Equal(1, window.Count);
    }
}

public sealed class ProvisioningEndpointsTests
{
    [Theory]
    [InlineData(8192, true)]
    [InlineData(8193, false)]
    public async Task OversizeBody_RejectsKnownAndChunkedLength(int size, bool accepted)
    {
        foreach (var known in new[] { false, true })
        {
            var request = new DefaultHttpContext().Request;
            request.Body = new MemoryStream(new byte[size]);
            if (known) request.ContentLength = size;
            Assert.Equal(accepted, await ProvisioningEndpoints.ReadBodyAsync(request, default) is not null);
        }
    }
}

public sealed class ProvisioningIntentWorkerTests
{
    [Theory]
    [InlineData("TENANT_CODE_TAKEN", true)]
    [InlineData("PLAN_NOT_FOUND", true)]
    [InlineData("VALIDATION_FAILED", true)]
    [InlineData("PROVISIONING_KEY_CONFLICT", true)]
    [InlineData("STORE_UNAVAILABLE", false)]
    public void PermanentFailure_NoRetry(string code, bool permanent) => Assert.Equal(permanent, ProvisioningIntentStore.IsPermanent(code));

    [Fact]
    public void Worker_UsesWebsiteActor_ReplayRecovery_AndBoundedRetries()
    {
        var root = RepoRoot.Find();
        var worker = File.ReadAllText(Path.Combine(root, "src/Animora.Server/Provisioning/ProvisioningIntentWorker.cs"));
        Assert.Contains("new ActorRef(ActorKind.WebsiteIntent, claim.ClientId)", worker);
        Assert.Contains("created.WasReplay", worker);
        Assert.Contains("ProvisioningReplayCredentials", worker);
        Assert.Contains("ServerRetryPolicy.NextDelay", worker);
        var store = File.ReadAllText(Path.Combine(root, "src/Animora.Persistence.PostgreSql/Provisioning/ProvisioningIntentStore.cs"));
        Assert.Contains("claim.Attempts >= 8", store);
        Assert.Contains("SKIP LOCKED", store);
        Assert.Contains("\"ClaimToken\"={claim.ClaimToken}", store);
        Assert.Contains("\"ClaimedUntilUtc\">{now}", store);
    }
}

public sealed class ProvisioningSecretsTests
{
    [Fact]
    public void Secrets_Protected_AndNotInAuditOrLogs()
    {
        var root = RepoRoot.Find();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src/Animora.Server/Provisioning"), "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("LogInformation(\"{Payload", source);
            Assert.DoesNotContain("LogError(ex", source);
        }
        var sourceStore = File.ReadAllText(Path.Combine(root, "src/Animora.Persistence.PostgreSql/Provisioning/WebsiteClientStore.cs"));
        Assert.Contains("pg_advisory_xact_lock", sourceStore);
        Assert.Contains(">= 3", sourceStore);
        Assert.DoesNotContain("new { secret", sourceStore);
    }
}
