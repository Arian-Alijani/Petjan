using Animora.Persistence.PostgreSql.Subscriptions;
using Xunit;

namespace Animora.Server.Tests.Subscriptions;

public sealed class ProvisioningEngineTests
{
    internal static ProvisioningRequest Request(string key = "request-1", string code = "clinic-1") =>
        new(key, code, "Clinic", null, null, "owner", "Owner", null, "trial", null, true);

    [Theory]
    [InlineData("admin")]
    [InlineData("operator")]
    [InlineData("api")]
    [InlineData("sync")]
    [InlineData("auth")]
    [InlineData("files")]
    [InlineData("health")]
    [InlineData("UPPER")]
    [InlineData("has space")]
    [InlineData("ab")]
    public void ReservedOrMalformedCode_Rejected(string code) => Assert.True(ProvisioningEngine.Validate(Request(code: code)).IsFailure);

    [Fact]
    public void Fingerprint_ExcludesPasswordAndKey_ButIncludesBusinessFields()
    {
        var r = Request(); var fingerprint = ProvisioningEngine.Fingerprint(r);
        Assert.Equal(fingerprint, ProvisioningEngine.Fingerprint(r with { IdempotencyKey = "another", OwnerInitialPassword = "another-password" }));
        Assert.NotEqual(fingerprint, ProvisioningEngine.Fingerprint(r with { SeatLimitOverride = 5 }));
        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint, ProvisioningEngine.Fingerprint(r with { Timezone = "Asia/Tehran" }));
    }

    [Fact]
    public void TemporaryPassword_MeetsPolicy_AndIsNotReused()
    {
        var a = ProvisioningEngine.GenerateTemporaryPassword(); var b = ProvisioningEngine.GenerateTemporaryPassword();
        Assert.NotEqual(a, b);
        Assert.True(Animora.Application.Security.PasswordPolicy.Validate(a).IsSuccess);
    }
}
