using Animora.Domain.Results;
using Animora.Server.Commands;
using Xunit;

namespace Animora.Server.Tests.Commands;

public sealed class RuleSetPolicyTests
{
    [Fact]
    public async Task BelowSupportedFrom_Rejected()
    {
        var f = new IntakeTestFixture();
        var result = await f.CreateService().SubmitAsync(f.Envelope() with { RuleSetVersion = "0" });
        Assert.Equal(ErrorCodes.RuleSetUnsupported.Code, result.Value!.ErrorCode);
        Assert.Equal(0, f.Dispatcher.Calls);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("2", false)]
    [InlineData("invalid", false)]
    public void SupportedRange_IsExplicit(string? version, bool supported) =>
        Assert.Equal(supported, new RuleSetPolicy().Evaluate(version).IsSuccess);

    [Fact]
    public async Task SupportedVersion_IsRecorded_InReceiptDetail()
    {
        var f = new IntakeTestFixture();
        var result = await f.CreateService().SubmitAsync(f.Envelope());
        Assert.Contains("ruleset=1", result.Value!.Detail);
    }
}
