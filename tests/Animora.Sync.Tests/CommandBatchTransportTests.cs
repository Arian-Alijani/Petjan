using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G05: <c>POST /api/commands/batch</c>.</summary>
public sealed class CommandBatchTransportTests
{
    [Fact]
    public async Task ResultsInRequestOrder()
    {
        var envelopes = new[] { "OP1", "OP2", "OP3" }.Select(id => CommandFixtures.Envelope(id)).ToArray();
        var (transport, handler) = CommandFixtures.Transport(_ => FeedFixtures.JsonResponse(
            envelopes.Select(e => CommandFixtures.Accepted(e.OperationId)).ToArray()));

        var result = await ((ICommandBatchTransport)transport).SubmitBatchAsync(envelopes);

        Assert.True(result.IsSuccess);
        Assert.Equal(["OP1", "OP2", "OP3"], result.Value!.Select(r => r.OperationId));
        Assert.Equal(["POST /api/commands/batch"], handler.Requests);
    }

    [Fact]
    public async Task ResponseOutOfOrder_IsProtocolViolation()
    {
        var envelopes = new[] { "OP1", "OP2" }.Select(id => CommandFixtures.Envelope(id)).ToArray();
        var (transport, _) = CommandFixtures.Transport(_ => FeedFixtures.JsonResponse(
            new[] { CommandFixtures.Accepted("OP2"), CommandFixtures.Accepted("OP1") }));

        var result = await transport.SubmitBatchAsync(envelopes);

        Assert.Equal(ErrorCodes.SyncProtocolViolation, result.Error);
    }

    [Fact]
    public async Task Over50_FailsBatchTooLarge_WithoutRequest()
    {
        var envelopes = Enumerable.Range(0, 51).Select(i => CommandFixtures.Envelope($"OP{i}")).ToArray();
        var (transport, handler) = CommandFixtures.Transport(_ => throw new InvalidOperationException("must not be called"));

        var result = await transport.SubmitBatchAsync(envelopes);

        Assert.Equal(ErrorCodes.BatchTooLarge, result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExactlyMax_IsSent()
    {
        var envelopes = Enumerable.Range(0, 50).Select(i => CommandFixtures.Envelope($"OP{i}")).ToArray();
        var (transport, _) = CommandFixtures.Transport(_ => FeedFixtures.JsonResponse(
            envelopes.Select(e => CommandFixtures.Accepted(e.OperationId)).ToArray()));

        var result = await transport.SubmitBatchAsync(envelopes);

        Assert.True(result.IsSuccess);
        Assert.Equal(50, result.Value!.Count);
    }

    [Fact]
    public async Task NetworkFault_DefersEveryEnvelope()
    {
        var envelopes = new[] { "OP1", "OP2" }.Select(id => CommandFixtures.Envelope(id)).ToArray();
        var (transport, _) = CommandFixtures.Transport(_ => throw new HttpRequestException("offline"));

        var result = await transport.SubmitBatchAsync(envelopes);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value!, r => Assert.Equal(SettlementOutcome.Deferred, r.Outcome));
        Assert.Equal(["OP1", "OP2"], result.Value!.Select(r => r.OperationId));
    }
}
