using Animora.Domain.Results;
using Animora.Server.Commands;
using Xunit;

namespace Animora.Server.Tests.Commands;

public sealed class ReservedCommandTypesTests
{
    [Fact]
    public async Task Reserved_AreRejected_HandlerNotRegistered()
    {
        var f = new IntakeTestFixture();
        Assert.Equal(5, ReservedCommandTypes.All.Count);
        foreach (string type in ReservedCommandTypes.All)
        {
            var result = await f.CreateService().SubmitAsync(f.Envelope(type));
            Assert.Equal(ErrorCodes.HandlerNotRegistered.Code, result.Value!.ErrorCode);
        }
        Assert.Equal(0, f.Dispatcher.Calls);
        Assert.Equal(5, f.Audit.Entries.Count);
    }
}
