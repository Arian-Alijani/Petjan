using Animora.Application.Dispatch;
using Xunit;

namespace Animora.Application.Tests;

public sealed class ServerChainTests
{
    [Fact]
    public void ServerChain_HasNoCommandJournal_ClientChainUnchanged()
    {
        Assert.Equal(new[] { "Correlation", "ExceptionMapping", "Validation", "Authorization", "Entitlement",
            "Transaction", "CommandJournal", "Audit" }, Dispatcher.Chain.Select(b => b.Name));
        Assert.Equal(new[] { "Correlation", "ExceptionMapping", "Validation", "Authorization", "Entitlement",
            "Transaction", "Audit" }, Dispatcher.ServerChain.Select(b => b.Name));
        Assert.DoesNotContain(Dispatcher.ServerChain, b => b.Name == "CommandJournal");
    }
}
