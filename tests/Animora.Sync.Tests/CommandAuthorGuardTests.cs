using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Animora.Application.Commands;
using Animora.Application.Security;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Sync.Custom;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>
/// D18: after a same-tenant user switch on one device (A queues work, signs out, B signs in — one local database,
/// one tenant), the next sync pass claimed A's commands and stamped B's session identity onto them, so the server
/// authorized and recorded A's work as B. A queued command may only be submitted as its own author.
/// </summary>
public sealed class CommandAuthorGuardTests
{
    private const string TenantId = "01TENANT0000000000000000AA";
    private const string DeviceId = "01DEVICE0000000000000000AA";

    private sealed class MemorySessions(ClientSession? session) : ISessionStore
    {
        public ClientSession? Session { get; set; } = session;
        public Task<Result<ClientSession?>> ReadAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<ClientSession?>.Ok(Session));
        public Task<Result> WriteAsync(ClientSession s, CancellationToken ct = default)
        {
            Session = s;
            return Task.FromResult(Result.Ok());
        }
        public Task<Result> ClearAsync(CancellationToken ct = default)
        {
            Session = null;
            return Task.FromResult(Result.Ok());
        }
    }

    private static ClientSession SessionFor(string userId) => new(
        "access-token", FeedFixtures.Now.AddHours(1), "refresh-token", userId, TenantId, DeviceId, "Doctor", "stamp");

    /// <summary>An envelope queued by <paramref name="userId"/> in <paramref name="tenantId"/>.</summary>
    private static CommandEnvelope Envelope(string operationId, string tenantId, string userId) =>
        CommandEnvelope.Create(operationId, tenantId, userId, DeviceId, "clinic.pet.update", "Pet", "AGG-1", "{}",
            CommandFingerprint.Compute("clinic.pet.update", "{}"), FeedFixtures.Now, 1);

    private static (HttpCommandTransport Transport, FakeFeedHandler Handler) Build(
        ISessionStore sessions, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeFeedHandler(respond);
        var factory = new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://sync.test/") });
        return (new HttpCommandTransport(factory, FeedFixtures.Options(), new FixedClock(FeedFixtures.Now), sessions), handler);
    }

    [Fact]
    public async Task AnotherUsersQueuedCommand_IsHeld_AndNeverSent()
    {
        var (transport, handler) = Build(new MemorySessions(SessionFor("U-B")),
            _ => throw new InvalidOperationException("a foreign command must never reach the server"));

        var result = await transport.SubmitAsync(Envelope("OP-A", TenantId, "U-A"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.IdentityMismatch, result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OwnQueuedCommand_IsStillSubmitted()
    {
        var (transport, handler) = Build(new MemorySessions(SessionFor("U-B")),
            _ => FeedFixtures.JsonResponse(CommandFixtures.Accepted("OP-B")));

        var result = await transport.SubmitAsync(Envelope("OP-B", TenantId, "U-B"));

        Assert.True(result.IsSuccess);
        Assert.Equal(SettlementOutcome.Accepted, result.Value!.Outcome);
        Assert.Equal(["POST /api/commands"], handler.Requests);
    }

    /// <summary>The documented purpose of rebinding: a PRE-BINDING envelope (placeholder tenant) is upgraded.</summary>
    [Fact]
    public async Task PreBindingEnvelope_IsStillReboundToTheSession()
    {
        string? body = null;
        var (transport, _) = Build(new MemorySessions(SessionFor("U-B")), request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return FeedFixtures.JsonResponse(CommandFixtures.Accepted("OP-LOCAL"));
        });

        var result = await transport.SubmitAsync(Envelope("OP-LOCAL", Tenant.LocalDefaultId, "U-LOCAL"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(body);
        Assert.Contains(TenantId, body);
        Assert.Contains("U-B", body);
        Assert.DoesNotContain(Tenant.LocalDefaultId, body);
    }

    [Fact]
    public async Task BatchWithAForeignMember_IsRefusedAsAWhole()
    {
        var (transport, handler) = Build(new MemorySessions(SessionFor("U-B")),
            _ => throw new InvalidOperationException("a mixed batch must never reach the server"));

        var result = await transport.SubmitBatchAsync(
            [Envelope("OP-B", TenantId, "U-B"), Envelope("OP-A", TenantId, "U-A")]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.IdentityMismatch, result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NoStoredSession_LeavesTheEnvelopeAlone()
    {
        var (transport, handler) = Build(new MemorySessions(null),
            _ => FeedFixtures.JsonResponse(CommandFixtures.Accepted("OP-A")));

        var result = await transport.SubmitAsync(Envelope("OP-A", TenantId, "U-A"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["POST /api/commands"], handler.Requests);
    }
}
