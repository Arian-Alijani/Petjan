using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Animora.Sync.Custom;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G05: provisional-id → server-id rewrite and the <c>/ids</c> fetch.</summary>
public sealed class CommandIdRemapTests
{
    private static CommandIdRemapService Service(Func<HttpRequestMessage, HttpResponseMessage>? respond = null, FakeFeedHandler? handler = null)
    {
        handler ??= new FakeFeedHandler(respond ?? (_ => CommandFixtures.Status(System.Net.HttpStatusCode.NotFound)));
        var factory = new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://sync.test/") });
        return new CommandIdRemapService(factory, FeedFixtures.Options());
    }

    [Fact]
    public void RewritesDependentPayload_WithServerId_RecomputesFingerprint()
    {
        var svc = Service();
        svc.Register(CommandFixtures.Tenant, "LOCAL-PET", "SRV-PET");
        var payload = "{\"note\":\"LOCAL-PET stays text\",\"petId\":\"LOCAL-PET\",\"tags\":[\"LOCAL-PET\",\"x\"]}";
        var env = CommandFixtures.Envelope("OP2", aggregateId: "LOCAL-PET", payload: payload, dependsOn: "LOCAL-PET");

        var rewritten = svc.Rewrite(env);

        Assert.Equal("SRV-PET", rewritten.AggregateId);
        Assert.Equal("SRV-PET", rewritten.DependsOnOperationId);
        Assert.Contains("\"petId\":\"SRV-PET\"", rewritten.PayloadJson);
        Assert.Contains("\"SRV-PET\",\"x\"", rewritten.PayloadJson);
        Assert.Contains("LOCAL-PET stays text", rewritten.PayloadJson); // whole-value match only
        Assert.NotEqual(env.CommandFingerprint, rewritten.CommandFingerprint);
        Assert.Equal(CommandFingerprint.Compute(rewritten.CommandType, rewritten.PayloadJson), rewritten.CommandFingerprint);
        Assert.Equal(env.OperationId, rewritten.OperationId); // identity never changes
    }

    [Fact]
    public void NoKnownId_ReturnsSameEnvelope()
    {
        var svc = Service();
        svc.Register(CommandFixtures.Tenant, "LOCAL-PET", "SRV-PET");
        var env = CommandFixtures.Envelope("OP2", aggregateId: "OTHER", payload: "{\"a\":\"b\"}");
        Assert.Same(env, svc.Rewrite(env));
    }

    [Fact]
    public void MappingIsTenantScoped()
    {
        var svc = Service();
        svc.Register("OTHER-TENANT", "LOCAL-PET", "SRV-PET");
        var env = CommandFixtures.Envelope("OP2", aggregateId: "LOCAL-PET");
        Assert.Same(env, svc.Rewrite(env));
    }

    [Fact]
    public void Learn_RegistersPrimaryIdOnAcceptedOnly()
    {
        var svc = Service();
        var creator = CommandFixtures.Record("OP1", aggregateId: "LOCAL-PET");
        svc.Learn(creator, new SettlementResult("OP1", SettlementOutcome.Rejected, "X", null, "SRV", null, FeedFixtures.Now, FinancialEffect.None, null));
        Assert.Null(svc.Resolve(CommandFixtures.Tenant, "LOCAL-PET"));
        svc.Learn(creator, CommandFixtures.Accepted("OP1", "SRV-PET"));
        Assert.Equal("SRV-PET", svc.Resolve(CommandFixtures.Tenant, "LOCAL-PET"));
    }

    [Fact]
    public async Task FetchesAllCreatedIds_NotJustSlot0()
    {
        var handler = new FakeFeedHandler(_ => FeedFixtures.JsonResponse(new CommandIdMapResponse(
        [
            new CommandIdMapItem(1, "Visit", "SRV-VISIT"),
            new CommandIdMapItem(0, "Pet", "SRV-PET"),
            new CommandIdMapItem(2, "Invoice", "SRV-INV"),
        ])));
        var svc = Service(handler: handler);

        var result = await svc.FetchCreatedIdsAsync(CommandFixtures.Tenant, "OP1", "LOCAL-PET");

        Assert.True(result.IsSuccess);
        Assert.Equal(["SRV-PET", "SRV-VISIT", "SRV-INV"], result.Value!.Select(i => i.ServerEntityId));
        Assert.Equal(["GET /api/commands/OP1/ids"], handler.Requests);
        Assert.Equal("SRV-PET", svc.Resolve(CommandFixtures.Tenant, "LOCAL-PET"));
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Unauthorized, "UNAUTHORIZED")]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, "SERVER_BUSY")]
    [InlineData(System.Net.HttpStatusCode.BadGateway, "NETWORK_UNAVAILABLE")]
    public async Task FetchFailures_MapToResult(System.Net.HttpStatusCode status, string code)
    {
        var svc = Service(_ => CommandFixtures.Status(status));
        var result = await svc.FetchCreatedIdsAsync(CommandFixtures.Tenant, "OP1");
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
    }
}
