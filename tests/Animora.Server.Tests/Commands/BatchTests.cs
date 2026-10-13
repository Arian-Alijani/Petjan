using System.Text;
using System.Text.Json;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Animora.Server.Commands;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Animora.Server.Tests.Commands;

public sealed class BatchTests
{
    [Fact]
    public async Task Over50_Returns_BatchTooLarge()
    {
        var f = new IntakeTestFixture();
        var context = Request(Enumerable.Range(0, 51).Select(_ => f.Envelope()).ToArray());
        var result = await CommandEndpoints.BatchAsync(context, f.CreateService());
        Assert.Equal(413, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(0, f.Store.BeginCalls);
        Assert.Equal(ErrorCodes.BatchTooLarge, CommandEndpoints.ValidateBatch(new CommandEnvelope[51]).Error);
    }

    [Fact]
    public async Task ResultsInRequestOrder_NeverReordered()
    {
        var f = new IntakeTestFixture();
        var envelopes = new[] { f.Envelope(), f.Envelope("unknown.type"), f.Envelope() };
        var result = await CommandEndpoints.BatchAsync(Request(envelopes), f.CreateService());
        var settlements = Assert.IsType<List<SettlementResult>>(((IValueHttpResult)result).Value);
        Assert.Equal(envelopes.Select(e => e.OperationId), settlements.Select(s => s.OperationId));
        Assert.Equal(new[] { SettlementOutcome.Accepted, SettlementOutcome.Rejected, SettlementOutcome.Accepted },
            settlements.Select(s => s.Outcome));
        Assert.Equal(3, f.Store.CompleteCalls);
        Assert.Equal(2, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task MidBatchStoreFailure_KeepsEarlierSettlements_AndDefersTheRest_PerItem()
    {
        var f = new IntakeTestFixture();
        f.Audit.FailAfterWrites = 1; // item 1 settles + audits; item 2's audit write fails
        var envelopes = new[] { f.Envelope(), f.Envelope(), f.Envelope() };

        var result = await CommandEndpoints.BatchAsync(Request(envelopes), f.CreateService());

        // Not a whole-batch HTTP error: a per-item array, in request order.
        var settlements = Assert.IsType<List<SettlementResult>>(((IValueHttpResult)result).Value);
        Assert.Equal(envelopes.Select(e => e.OperationId), settlements.Select(s => s.OperationId));
        Assert.Equal(SettlementOutcome.Accepted, settlements[0].Outcome);
        Assert.Equal(SettlementOutcome.Deferred, settlements[1].Outcome);
        Assert.Equal(SettlementOutcome.Deferred, settlements[2].Outcome);
        Assert.Equal(ErrorCodes.StoreUnavailable.Code, settlements[1].ErrorCode);
        Assert.Equal(1, f.Dispatcher.Calls); // the third item was never attempted
    }

    [Fact]
    public async Task MalformedLaterItem_DoesNotSettleEarlierItems()
    {
        var f = new IntakeTestFixture();
        var result = await CommandEndpoints.BatchAsync(Request([f.Envelope(), f.Envelope() with { OperationId = "bad" }]), f.CreateService());
        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(0, f.Store.BeginCalls);
    }

    private static DefaultHttpContext Request(CommandEnvelope[] envelopes)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelopes));
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        return context;
    }
}
