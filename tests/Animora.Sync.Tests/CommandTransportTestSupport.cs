using System.Net;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Sync.Custom;

namespace Animora.Sync.Tests;

/// <summary>Fixtures for the G05 command-transport tests.</summary>
internal static class CommandFixtures
{
    public const string Tenant = "T1";

    public static CommandEnvelope Envelope(string operationId, string? aggregateId = "AGG-1", string payload = "{}",
        string type = "clinic.pet.update", string? dependsOn = null) =>
        CommandEnvelope.Create(operationId, Tenant, "U1", "D1", type, "Pet", aggregateId, payload,
            CommandFingerprint.Compute(type, payload), FeedFixtures.Now, 1, dependsOnOperationId: dependsOn);

    public static SettlementResult Accepted(string operationId, string? serverId = "S-1", string? version = "v-1") =>
        new(operationId, SettlementOutcome.Accepted, null, null, serverId, version, FeedFixtures.Now, FinancialEffect.None, null);

    public static PendingCommandRecord Record(string operationId, SettlementState status = SettlementState.PendingSettlement,
        DateTimeOffset? lastAttempt = null, DateTimeOffset? claimedUntil = null, long sequence = 1, string? aggregateId = "AGG-1") =>
        new("id-" + operationId, operationId, Tenant, "U1", "D1", "clinic.pet.update", "Pet", aggregateId, "{}",
            CommandFingerprint.Compute("clinic.pet.update", "{}"), sequence, status, null, null, null,
            FeedFixtures.Now.AddHours(-1), lastAttempt ?? FeedFixtures.Now.AddHours(-1), 1, null, null, null,
            FinancialEffect.Unknown, null, null, null, null, null, claimedUntil);

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    public static (HttpCommandTransport Transport, FakeFeedHandler Handler) Transport(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeFeedHandler(respond);
        var factory = new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://sync.test/") });
        return (new HttpCommandTransport(factory, FeedFixtures.Options(), new FixedClock(FeedFixtures.Now)), handler);
    }
}

internal sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

/// <summary>In-memory journal: only what the reconciler touches is real; everything else is inert.</summary>
internal sealed class FakeJournal(IEnumerable<PendingCommandRecord> rows) : ICommandJournal
{
    public List<PendingCommandRecord> Rows { get; } = rows.ToList();
    public List<SettlementResult> Applied { get; } = [];

    public Task<Result<PagedResult<PendingCommandRecord>>> GetUnsettledPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var items = Rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return Task.FromResult(Result<PagedResult<PendingCommandRecord>>.Ok(new PagedResult<PendingCommandRecord>(items, Rows.Count, page, pageSize)));
    }

    public Task<Result> ApplySettlementAsync(SettlementResult result, CancellationToken ct = default)
    {
        Applied.Add(result);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result<PendingCommandRecord>> EnqueueAsync(CommandEnvelope envelope, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Result<PendingCommandRecord?>> FindByOperationIdAsync(string operationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Result<IReadOnlyList<PendingCommandRecord>>> ClaimBatchAsync(int max, TimeSpan lease, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Result> ReleaseAsync(string operationId, ErrorCode error, TimeSpan backoff, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Result<IReadOnlyList<PendingCommandRecord>>> GetUnsettledForAggregateAsync(string aggregateType, string aggregateId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Result<PendingCommandSummary>> GetSummaryAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Scripted transport that records every submitted envelope.</summary>
internal sealed class ScriptedTransport(Func<CommandEnvelope, Result<SettlementResult>> respond) : ICommandTransport
{
    public List<CommandEnvelope> Sent { get; } = [];

    public Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default)
    {
        Sent.Add(envelope);
        return Task.FromResult(respond(envelope));
    }
}
