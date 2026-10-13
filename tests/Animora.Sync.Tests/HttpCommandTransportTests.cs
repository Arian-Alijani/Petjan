using Animora.Application.Security;
using Animora.Infrastructure.Local.Storage;
using Animora.Persistence.Tests;
using Animora.Sync.Custom;
using System.Net;
using System.Text.Json;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G05: the HTTP→settlement status map of <c>POST /api/commands</c>.</summary>
public sealed class HttpCommandTransportTests
{
    [Fact]
    public async Task Http200Settlement_ReturnsSettlementResult()
    {
        var env = CommandFixtures.Envelope("OP1");
        var (transport, handler) = CommandFixtures.Transport(_ => FeedFixtures.JsonResponse(CommandFixtures.Accepted("OP1")));

        var result = await transport.SubmitAsync(env);

        Assert.True(result.IsSuccess);
        Assert.Equal(SettlementOutcome.Accepted, result.Value!.Outcome);
        Assert.Equal(["POST /api/commands"], handler.Requests);
    }

    [Fact]
    public async Task SendsContractVersionHeaderAndEnvelopeBody()
    {
        var env = CommandFixtures.Envelope("OP1", payload: "{\"a\":1}");
        string? header = null; string? body = null;
        var (transport, _) = CommandFixtures.Transport(req =>
        {
            header = req.Headers.GetValues(WireContract.HeaderName).Single();
            body = req.Content!.ReadAsStringAsync().Result;
            return FeedFixtures.JsonResponse(CommandFixtures.Accepted("OP1"));
        });

        await transport.SubmitAsync(env);

        Assert.Equal("2", header);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("OP1", doc.RootElement.GetProperty("operationId").GetString());
        Assert.Equal(env.CommandFingerprint, doc.RootElement.GetProperty("commandFingerprint").GetString());
    }

    [Fact]
    public async Task Accepted_RecordsServerIdAndVersion()
    {
        var (transport, _) = CommandFixtures.Transport(_ => FeedFixtures.JsonResponse(CommandFixtures.Accepted("OP1", "SRV-9", "ver-3")));

        var result = await transport.SubmitAsync(CommandFixtures.Envelope("OP1"));

        Assert.Equal("SRV-9", result.Value!.ServerEntityId);
        Assert.Equal("ver-3", result.Value.ServerVersion);
    }

    [Fact]
    public async Task Status426_SettlesDeferred_ContractUnsupported_NotDropped()
    {
        var (transport, _) = CommandFixtures.Transport(_ =>
        {
            var r = CommandFixtures.Status(HttpStatusCode.UpgradeRequired);
            r.Headers.TryAddWithoutValidation(WireContract.SupportedVersionsHeaderName, "1,2");
            return r;
        });

        var result = await transport.SubmitAsync(CommandFixtures.Envelope("OP1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(SettlementOutcome.Deferred, result.Value!.Outcome);
        Assert.Equal(ErrorCodes.ContractVersionUnsupported.Code, result.Value.ErrorCode);
        Assert.Contains("1,2", result.Value.Detail);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Status401And403_FailsUnauthorized(HttpStatusCode code)
    {
        var (transport, _) = CommandFixtures.Transport(_ => CommandFixtures.Status(code));
        var result = await transport.SubmitAsync(CommandFixtures.Envelope("OP1"));
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.Unauthorized, result.Error);
    }

    [Fact]
    public async Task Status400_FailsValidation()
    {
        var (transport, _) = CommandFixtures.Transport(_ => CommandFixtures.Status(HttpStatusCode.BadRequest));
        var result = await transport.SubmitAsync(CommandFixtures.Envelope("OP1"));
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TransientStatus_SettlesDeferred_StaysPending(HttpStatusCode code)
    {
        var (transport, _) = CommandFixtures.Transport(_ => CommandFixtures.Status(code));
        var result = await transport.SubmitAsync(CommandFixtures.Envelope("OP1"));
        Assert.True(result.IsSuccess);
        Assert.Equal(SettlementOutcome.Deferred, result.Value!.Outcome);
        Assert.Null(result.Value.ErrorCode);
    }

    [Fact]
    public async Task NetworkFault_SettlesDeferred_StaysPending()
    {
        var (transport, _) = CommandFixtures.Transport(_ => throw new HttpRequestException("offline"));
        var result = await transport.SubmitAsync(CommandFixtures.Envelope("OP1"));
        Assert.True(result.IsSuccess);
        Assert.Equal(SettlementOutcome.Deferred, result.Value!.Outcome);
        Assert.Equal("OP1", result.Value.OperationId);
    }

    [Fact]
    public async Task ResponseForAnotherOperation_IsProtocolViolation()
    {
        var (transport, _) = CommandFixtures.Transport(_ => FeedFixtures.JsonResponse(CommandFixtures.Accepted("OTHER")));
        var result = await transport.SubmitAsync(CommandFixtures.Envelope("OP1"));
        Assert.Equal(ErrorCodes.SyncProtocolViolation, result.Error);
    }

    [Fact]
    public async Task IdempotentRetry_SameOperationId_UsesStoredResult()
    {
        // The server stores the first result and replays it for the same (tenant, operationId).
        var stored = CommandFixtures.Accepted("OP1", "SRV-1", "v-1");
        var bodies = new List<string>();
        var (transport, handler) = CommandFixtures.Transport(req =>
        {
            bodies.Add(req.Content!.ReadAsStringAsync().Result);
            return FeedFixtures.JsonResponse(stored);
        });
        var env = CommandFixtures.Envelope("OP1");

        var first = await transport.SubmitAsync(env);
        var retry = await transport.SubmitAsync(env);

        Assert.Equal(first.Value, retry.Value);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(bodies[0], bodies[1]); // byte-identical retry: same OperationId and fingerprint, never re-minted
    }
    [Theory]
    [InlineData(true, "B")]
    [InlineData(false, "A")]
    public async Task TenantIsolation_MismatchedOrSuspendedSessionDefersSingleAndBatchWithoutNetwork(bool active, string sessionTenant)
    {
        var binding = new TenantSessionBinding(new MemorySecretStore());
        Assert.True(binding.Bind("A").IsSuccess);
        if (active) binding.Activate();
        var handler = new FakeFeedHandler(_ => throw new InvalidOperationException("Cross-tenant request must not leave the device."));
        var transport = new HttpCommandTransport(new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://sync.test/") }),
            FeedFixtures.Options(), new FixedClock(FeedFixtures.Now), new FixedSessions(sessionTenant), binding);
        var envelope = CommandFixtures.Envelope("OP1") with { TenantId = "T-LOCAL-DEFAULT" };
        Assert.Equal(SettlementOutcome.Deferred, (await transport.SubmitAsync(envelope)).Value!.Outcome);
        Assert.Equal(SettlementOutcome.Deferred, Assert.Single((await transport.SubmitBatchAsync([envelope])).Value!).Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TenantIsolation_MatchingSessionRebindsWithoutChangingIdempotencyAndRejectsForeignEnvelope()
    {
        var binding = new TenantSessionBinding(new MemorySecretStore());
        binding.Bind("A");
        binding.Activate();
        var bodies = new List<string>();
        var handler = new FakeFeedHandler(req =>
        {
            bodies.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return FeedFixtures.JsonResponse(CommandFixtures.Accepted("OP1"));
        });
        var transport = new HttpCommandTransport(new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://sync.test/") }),
            FeedFixtures.Options(), new FixedClock(FeedFixtures.Now), new FixedSessions("A"), binding);
        var envelope = CommandFixtures.Envelope("OP1") with { TenantId = "T-LOCAL-DEFAULT" };
        Assert.Equal(SettlementOutcome.Accepted, (await transport.SubmitAsync(envelope)).Value!.Outcome);
        Assert.Equal(SettlementOutcome.Accepted, (await transport.SubmitAsync(envelope)).Value!.Outcome);
        Assert.Equal(bodies[0], bodies[1]);
        using var json = JsonDocument.Parse(bodies[0]);
        Assert.Equal("A", json.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal(envelope.CommandFingerprint, json.RootElement.GetProperty("commandFingerprint").GetString());
        Assert.Equal(envelope.PayloadJson, json.RootElement.GetProperty("payloadJson").GetString());
        Assert.Equal(SettlementOutcome.Deferred, (await transport.SubmitAsync(envelope with { TenantId = "B" })).Value!.Outcome);
        Assert.Equal(2, handler.Requests.Count);
    }

    private sealed class FixedSessions(string tenant) : ISessionStore
    {
        public Task<Result<ClientSession?>> ReadAsync(CancellationToken ct = default) => Task.FromResult(Result<ClientSession?>.Ok(
            new ClientSession("access", FeedFixtures.Now.AddHours(1), "refresh", "U", tenant, "D", "Manager", "s")));
        public Task<Result> WriteAsync(ClientSession session, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> ClearAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
