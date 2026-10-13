using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Security;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Server.Authorization;
using Animora.Server.Commands;
using Animora.Server.Tests.Persistence;
using Microsoft.AspNetCore.Http;

namespace Animora.Server.Tests.Commands;

public sealed record TestCreateCommand(string Name) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => "test.create";
    public string AggregateTypeName => "Test";
    public string? AggregateIdOrNull => null;
}

public sealed record TestUpdateCommand(string Id, string Name) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => "test.update";
    public string AggregateTypeName => "Test";
    public string? AggregateIdOrNull => Id;
}

internal sealed class IntakeTestFixture
{
    public FixedClock Clock { get; } = new();
    public FakeIntakeStore Store { get; } = new();
    public FakeDispatcher Dispatcher { get; } = new();
    public FakeAuthorizer Authorizer { get; } = new();
    public FakeAudit Audit { get; } = new();
    public FakeEntitlement Entitlement { get; } = new();
    public FakeVersionReader Versions { get; } = new();
    public FakeOutbox Outbox { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public RequestAuthorizationState State { get; } = new();
    public string TenantId { get; }
    public string UserId { get; } = IdGenerator.NewId();
    public string DeviceId { get; } = IdGenerator.NewId();
    public CommandPayloadCatalog Catalog { get; }
    public RuleSetPolicy Rules { get; init; } = new();

    public IntakeTestFixture(string? tenantId = null)
    {
        TenantId = tenantId ?? IdGenerator.NewId();
        State.Set(new ResolvedMembership(TenantId, StaffRole.Manager, UserId, DeviceId, true),
            new AuthorizationSubject(TenantId, UserId, new HashSet<string>(), StaffRole.Manager, DeviceId, false));
        Catalog = new CommandPayloadCatalogBuilder().Add<TestCreateCommand>("test.create", "Test")
            .Add<TestUpdateCommand>("test.update", "Test").Build();
    }

    public CommandIntakeService CreateService(CommandPayloadCatalog? catalog = null, IDispatcher? dispatcher = null, IUnitOfWork? unitOfWork = null,
        ICommandIntakeStore? store = null, IServerCommandConcurrencyGuard? guard = null, IAggregateVersionReader? versions = null, ICommandEffectIdCollector? effectIds = null, ServerCommandExecutionScope? execution = null,
        Animora.Persistence.PostgreSql.Repositories.IServerFinancialEventProcessor? financialEvents = null) => new(store ?? Store, dispatcher ?? Dispatcher,
        unitOfWork ?? UnitOfWork, Authorizer, Entitlement, new FixedTenantContext(TenantId), State, Audit, catalog ?? Catalog,
        versions ?? Versions, Rules, new IntakeRecoveryService(store ?? Store, [new NoProbe()], Clock), Outbox, Clock,
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, guard, effectIds, execution, financialEvents);

    public CommandEnvelope Envelope(string type = "test.create", string payload = "{\"name\":\"alpha\"}",
        string? operation = null, string? aggregateId = null) => CommandEnvelope.Create(operation ?? IdGenerator.NewId(),
        TenantId, UserId, DeviceId, type, "Test", aggregateId, payload, CommandFingerprint.Compute(type, payload), Clock.UtcNow, 1);

    public CommandIntakeRecord Row(CommandEnvelope envelope, bool expired = false) => CommandIntakeRecord.Begin(
        TenantId, envelope.OperationId, envelope.CommandType, envelope.CommandFingerprint, envelope.PayloadJson,
        envelope.DependsOnOperationId, IdGenerator.NewId(), IdGenerator.NewId(), "test", Clock.UtcNow,
        Clock.UtcNow.AddSeconds(expired ? -1 : 60));
}

internal sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class FakeIntakeStore : ICommandIntakeStore
{
    public Dictionary<string, CommandIntakeRecord> Rows { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, IReadOnlyList<CommandIdMapItem>> Ids { get; } = new(StringComparer.Ordinal);
    public int BeginCalls { get; private set; }
    public int CompleteCalls { get; private set; }
    public bool LoseFence { get; set; }
    public string? ObservedAttempt { get; private set; }
    public string? AcquiredAttempt { get; private set; }
    public string? FailedAttempt { get; private set; }

    public Task<IntakeBeginResult> TryBeginAsync(CommandIntakeRecord record, CancellationToken ct = default)
    {
        BeginCalls++;
        bool added = Rows.TryAdd(record.OperationId, record);
        return Task.FromResult(new IntakeBeginResult(added, added ? null : Rows[record.OperationId]));
    }
    public Task<CommandIntakeRecord?> FindAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(Rows.GetValueOrDefault(operationId));
    public Task<Result> CompleteAsync(IntakeSettlement settlement, CancellationToken ct = default)
    {
        CompleteCalls++;
        CommandIntakeRecord row = Rows[settlement.OperationId];
        if (LoseFence || row.AttemptId != settlement.AttemptId || row.Status != IntakeStatus.InProgress)
            return Task.FromResult(Result.Fail(ErrorCodes.ConcurrencyConflict));
        Set(row, nameof(row.Status), IntakeStatus.Settled);
        Set(row, nameof(row.Outcome), settlement.Outcome);
        Set(row, nameof(row.ErrorCode), settlement.ErrorCode);
        Set(row, nameof(row.Detail), settlement.Detail);
        Set(row, nameof(row.ServerEntityId), settlement.ServerEntityId);
        Set(row, nameof(row.ServerVersion), settlement.ServerVersion);
        Set(row, nameof(row.FinancialEffect), settlement.FinancialEffect);
        Set(row, nameof(row.ResponsibleRole), settlement.ResponsibleRole);
        Set(row, nameof(row.SettledUtc), settlement.SettledUtc);
        Ids[settlement.OperationId] = settlement.Ids;
        return Task.FromResult(Result.Ok());
    }
    public Task<bool> RenewLeaseAsync(string operationId, string attemptId, DateTimeOffset leaseUntilUtc, CancellationToken ct = default)
    {
        bool owned = Rows.TryGetValue(operationId, out var row) && row.AttemptId == attemptId;
        if (owned) Set(row!, nameof(row.LeaseUntilUtc), leaseUntilUtc);
        return Task.FromResult(owned);
    }
    public Task<bool> TryAcquireAttemptAsync(string operationId, string? observedAttemptId, string newAttemptId,
        string leaseOwner, DateTimeOffset nowUtc, DateTimeOffset leaseUntilUtc, CancellationToken ct = default)
    {
        ObservedAttempt = observedAttemptId;
        CommandIntakeRecord row = Rows[operationId];
        bool acquired = !LoseFence && row.AttemptId == observedAttemptId && (row.Status == IntakeStatus.Failed
            || row.Status == IntakeStatus.InProgress && row.LeaseUntilUtc < nowUtc);
        if (acquired)
        {
            AcquiredAttempt = newAttemptId;
            Set(row, nameof(row.AttemptId), newAttemptId);
            Set(row, nameof(row.AttemptCount), (short)(row.AttemptCount + 1));
            Set(row, nameof(row.Status), IntakeStatus.InProgress);
            Set(row, nameof(row.LeaseUntilUtc), leaseUntilUtc);
        }
        return Task.FromResult(acquired);
    }
    public Task<bool> MarkFailedAsync(string operationId, string observedAttemptId, string? detail, CancellationToken ct = default)
    {
        CommandIntakeRecord row = Rows[operationId];
        bool owned = !LoseFence && row.AttemptId == observedAttemptId;
        if (owned)
        {
            FailedAttempt = observedAttemptId;
            Set(row, nameof(row.Status), IntakeStatus.Failed);
            Set(row, nameof(row.Detail), detail);
        }
        return Task.FromResult(owned);
    }
    public Task<IReadOnlyList<CommandIntakeRecord>> ListExpiredLeasesAsync(DateTimeOffset nowUtc, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CommandIntakeRecord>>(Rows.Values.Where(r => r.Status == IntakeStatus.InProgress
            && r.LeaseUntilUtc < nowUtc).Take(Math.Clamp(limit, 1, 50)).ToArray());
    public Task<IReadOnlyList<CommandIdMapItem>> GetIdMapAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(Ids.GetValueOrDefault(operationId) ?? (IReadOnlyList<CommandIdMapItem>)Array.Empty<CommandIdMapItem>());
    public static void Set(CommandIntakeRecord row, string property, object? value) =>
        typeof(CommandIntakeRecord).GetProperty(property)!.SetValue(row, value);
}

internal sealed class FakeDispatcher : IDispatcher
{
    public int Calls { get; private set; }
    public string EntityId { get; } = IdGenerator.NewId();
    public Result Outcome { get; set; } = Result.Ok();
    public CommandContext? LastContext { get; private set; }
    public Task<Result<T>> SendAsync<T>(ICommand<T> command, CommandContext ctx, CancellationToken ct = default)
    {
        Calls++;
        LastContext = ctx;
        return Task.FromResult(Outcome.IsSuccess ? Result<T>.Ok((T)(object)EntityId) : Result<T>.Fail(Outcome.Error, Outcome.Detail));
    }
    public Task<Result<T>> QueryAsync<T>(IQuery<T> query, QueryContext ctx, CancellationToken ct = default) =>
        Task.FromResult(Result<T>.Fail(ErrorCodes.HandlerNotRegistered));
}
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<Result> SaveAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());
    public Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default) => work(ct);
}
internal sealed class FakeAuthorizer : ICommandAuthorizer
{
    public Result Outcome { get; set; } = Result.Ok();
    public int Calls { get; private set; }
    public Task<Result> AuthorizeAsync(string commandType, AuthorizationSubject subject, CancellationToken ct = default)
    { Calls++; return Task.FromResult(Outcome); }
}
internal sealed class FakeEntitlement : IServerEntitlementGate
{
    public Result Outcome { get; set; } = Result.Ok();
    public ValueTask<Result> EvaluateAsync(TenantEntitlementContext context, CancellationToken ct = default) => ValueTask.FromResult(Outcome);
}
internal sealed class FakeAudit : ISecurityAuditWriter
{
    public List<SecurityAuditEntry> Entries { get; } = [];
    /// <summary>When set, every write after this many successful ones fails (simulates a store outage mid-batch).</summary>
    public int? FailAfterWrites { get; set; }
    public Task<Result> WriteAsync(SecurityAuditEntry entry, CancellationToken ct = default)
    {
        if (FailAfterWrites is { } limit && Entries.Count >= limit)
            return Task.FromResult(Result.Fail(ErrorCodes.StoreUnavailable));
        Entries.Add(entry);
        return Task.FromResult(Result.Ok());
    }
}
internal sealed class FakeVersionReader : IAggregateVersionReader
{
    public string? Version { get; set; }
    public Task<Result<string?>> TryReadAsync(string aggregateType, string aggregateId, CancellationToken ct = default) =>
        Task.FromResult(Result<string?>.Ok(Version));
}
internal sealed class FakeOutbox : IServerOutbox
{
    public int Calls { get; private set; }
    public Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct = default)
    { Calls++; return Task.FromResult(Result.Ok()); }
    public Task<Result> EnqueueAsync(string messageType, string tenantId, string payloadJson, string correlationId,
        CancellationToken ct = default)
    { Calls++; return Task.FromResult(Result.Ok()); }
}
