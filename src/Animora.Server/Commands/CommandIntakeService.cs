using System.Globalization;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Repositories;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Authorization;
using Animora.Server.Hosting;

namespace Animora.Server.Commands;

/// <summary>Tenant-scoped, fenced command intake. Transport failures are Result failures; settlements are HTTP 200.</summary>
internal sealed class CommandIntakeService(
    ICommandIntakeStore store,
    IDispatcher dispatcher,
    IUnitOfWork unitOfWork,
    ICommandAuthorizer authorizer,
    IServerEntitlementGate entitlement,
    ITenantContext tenantContext,
    IRequestAuthorizationState authorization,
    ISecurityAuditWriter audit,
    CommandPayloadCatalog catalog,
    IAggregateVersionReader versions,
    RuleSetPolicy rules,
    IntakeRecoveryService recovery,
    IServerOutbox outbox,
    IClock clock,
    IHttpContextAccessor http,
    IServerCommandConcurrencyGuard? concurrency = null,
    ICommandEffectIdCollector? effectIds = null,
    ServerCommandExecutionScope? execution = null,
    IServerFinancialEventProcessor? financialEvents = null)
{
    public async Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default)
    {
        var metrics=http.HttpContext?.RequestServices?.GetService<Animora.Server.Observability.ServerMetrics>();
        using var metricLease=metrics?.CommandLease();
        if(http.HttpContext is { } request) request.Items["OperationId"]=envelope.OperationId;
        Result shape = CommandEnvelopeValidator.Validate(envelope);
        if (shape.IsFailure) return Result<SettlementResult>.Fail(shape.Error, shape.Detail);
        ResolvedMembership? caller = authorization.Membership;
        if (caller is null || authorization.Subject is null || caller.TenantId != tenantContext.TenantId)
            return Result<SettlementResult>.Fail(ErrorCodes.SessionExpired);
        DateTimeOffset now = clock.UtcNow;
        string correlation = http.HttpContext?.Response.Headers[CorrelationHeader.Name].ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(correlation)) correlation = IdGenerator.NewId(now);
        // Identity in the envelope is evidence to compare, never the authority used by a handler or store.
        if (envelope.TenantId != caller.TenantId || envelope.UserId != caller.UserId || envelope.DeviceId != caller.DeviceId)
            return await ObserveAsync(envelope, Result.Fail(ErrorCodes.TenantMismatch), caller, correlation,
                "command.tenant_mismatch", ct).ConfigureAwait(false);
        string fingerprint = CommandFingerprint.Compute(envelope.CommandType, envelope.PayloadJson);
        if (!string.Equals(fingerprint, envelope.CommandFingerprint, StringComparison.Ordinal))
            return await ObserveAsync(envelope, Result.Fail(ErrorCodes.IdempotencyFingerprintMismatch), caller, correlation,
                "command.fingerprint_mismatch", ct).ConfigureAwait(false);

        string attempt = IdGenerator.NewId(now);
        var row = CommandIntakeRecord.Begin(caller.TenantId, envelope.OperationId, envelope.CommandType, fingerprint,
            envelope.PayloadJson, envelope.DependsOnOperationId, correlation, attempt, IntakeRecoveryService.LeaseOwner,
            now, now + IntakeRecoveryService.LeaseDuration);
        IntakeBeginResult begin = await store.TryBeginAsync(row, ct).ConfigureAwait(false);
        if (!begin.Inserted)
        {
            CommandIntakeRecord? existing = begin.Existing;
            if (existing is null)
                return await DeferAsync(envelope, ErrorCodes.CommandInProgress, caller, correlation, ct).ConfigureAwait(false);
            if (existing.Fingerprint != fingerprint)
                return await ObserveAsync(envelope, Result.Fail(ErrorCodes.IdempotencyFingerprintMismatch), caller, correlation,
                    "command.fingerprint_mismatch", ct).ConfigureAwait(false);
            if (existing.Status == IntakeStatus.Settled)
            {
                SettlementResult stored = SettlementMapper.Stored(existing);
                if (stored.Outcome != SettlementOutcome.Accepted)
                {
                    Result observed = await AuditAsync(stored, caller, correlation, "command.replay", ct).ConfigureAwait(false);
                    if (observed.IsFailure) return Result<SettlementResult>.Fail(observed.Error, observed.Detail);
                }
                return Result<SettlementResult>.Ok(stored);
            }
            Result<string> takeover = await recovery.RecoverAsync(existing, ct).ConfigureAwait(false);
            if (takeover.IsFailure)
                return await DeferAsync(envelope, takeover.Error, caller, correlation, ct).ConfigureAwait(false);
            attempt = takeover.Value!;
        }

        // Persist terminal settlement, ids and outbox in the SAME outer transaction as the effect.
        // The Application Transaction behavior joins through CommandPlaneUnitOfWork savepoints.
        Result<SettlementResult> committed = await unitOfWork.ExecuteAsync(async token =>
        {
            // A retry of a transient transaction starts with a fresh tracker and re-evaluates all preconditions.
            if (envelope.DependsOnOperationId is not null)
            {
                CommandIntakeRecord? dependency = await store.FindAsync(envelope.DependsOnOperationId, token).ConfigureAwait(false);
                if (dependency is { Status: IntakeStatus.Settled, Outcome: (short)SettlementOutcome.Rejected }
                    || dependency is { Status: IntakeStatus.Settled, Outcome: (short)SettlementOutcome.Conflict })
                    return await CompleteAsync(envelope, attempt, Result.Fail(ErrorCodes.DependencyRejected), caller,
                        correlation, null, token).ConfigureAwait(false);
                if (dependency is not { Status: IntakeStatus.Settled, Outcome: (short)SettlementOutcome.Accepted })
                {
                    // Deferred is NOT terminal: the same operation must be able to retry once its dependency settles.
                    await store.RenewLeaseAsync(envelope.OperationId, attempt, clock.UtcNow.AddTicks(-1), token).ConfigureAwait(false);
                    return await DeferAsync(envelope, ErrorCodes.DependencyUnsettled, caller, correlation, token).ConfigureAwait(false);
                }
            }
            Result<int> ruleVersion = rules.Evaluate(envelope.RuleSetVersion);
            if (ruleVersion.IsFailure)
                return await CompleteAsync(envelope, attempt, ruleVersion.ToResult(), caller, correlation, null, token).ConfigureAwait(false);
            string ruleDetail = "ruleset=" + ruleVersion.Value.ToString(CultureInfo.InvariantCulture);

            // Unknown/reserved types have no executable handler or permission requirement (fail closed).
            Result<ResolvedCommandPayload> payload = catalog.Resolve(envelope);
            if (payload.IsFailure && payload.Error == ErrorCodes.HandlerNotRegistered)
                return await CompleteAsync(envelope, attempt, payload.ToResult(), caller, correlation, ruleDetail, token).ConfigureAwait(false);
            Result permission = await authorizer.AuthorizeAsync(envelope.CommandType, authorization.Subject!, token).ConfigureAwait(false);
            if (permission.IsFailure)
                return await CompleteAsync(envelope, attempt, permission, caller, correlation, ruleDetail, token).ConfigureAwait(false);
            Result entitled = await entitlement.EvaluateAsync(new TenantEntitlementContext(caller.TenantId, caller.UserId,
                envelope.CommandType), token).ConfigureAwait(false);
            if (entitled.IsFailure)
                return await CompleteAsync(envelope, attempt, Result.Fail(ErrorCodes.EntitlementRequired, entitled.Detail),
                    caller, correlation, ruleDetail, token).ConfigureAwait(false);
            if (payload.IsFailure)
                return await CompleteAsync(envelope, attempt, payload.ToResult(), caller, correlation, ruleDetail, token).ConfigureAwait(false);
            if (envelope.ExpectedServerVersion is not null && envelope.AggregateId is not null)
            {
                Result<string?> current = await versions.TryReadAsync(envelope.AggregateType, envelope.AggregateId, token).ConfigureAwait(false);
                if (current.IsFailure)
                    return await CompleteAsync(envelope, attempt, current.ToResult(), caller, correlation, ruleDetail, token).ConfigureAwait(false);
                if (current.Value is not null && current.Value != envelope.ExpectedServerVersion)
                    return await CompleteAsync(envelope, attempt, Result.Fail(ErrorCodes.ConcurrencyConflict), caller,
                        correlation, ruleDetail, token).ConfigureAwait(false);
            }
            if (execution is not null)
            {
                execution.OperationId = envelope.OperationId;
                execution.UserId = caller.UserId;
            }
            if (concurrency is not null)
            {
                Result fence = await concurrency.PrepareAsync(payload.Value!.Command, token).ConfigureAwait(false);
                if (fence.IsFailure)
                    return await CompleteAsync(envelope, attempt, fence, caller, correlation, ruleDetail, token).ConfigureAwait(false);
            }
            // Numeric v1 hints, client local sequence and timestamps never participate in concurrency/ordering.
            var context = new CommandContext(caller.TenantId, caller.UserId, caller.DeviceId, envelope.OperationId,
                correlation, envelope.CausationId, envelope.DependsOnOperationId, null, clock.UtcNow);
            ResolvedCommandPayload resolved = payload.Value!;
            CommandDispatchResult? dispatched = null;
            CapturedCommandEffectIds? generated = null;
            Result<object?> effect = await unitOfWork.ExecuteAsync(async handlerToken =>
            {
                dispatched = await resolved.Candidate.Dispatch(resolved.Command, dispatcher, context, handlerToken).ConfigureAwait(false);
                if (dispatched.Result.IsFailure)
                    return Result<object?>.Fail(dispatched.Result.Error, dispatched.Result.Detail);
                if (financialEvents is not null)
                {
                    var financial = await financialEvents.ProcessAsync(context, handlerToken).ConfigureAwait(false);
                    if (financial.IsFailure) return Result<object?>.Fail(financial.Error, financial.Detail);
                }
                generated = effectIds?.Capture(envelope, dispatched.Value);
                Result saved = await unitOfWork.SaveAsync(handlerToken).ConfigureAwait(false);
                return saved.IsSuccess ? Result<object?>.Ok(dispatched.Value) : Result<object?>.Fail(saved.Error, saved.Detail);
            }, token).ConfigureAwait(false);
            if (effect.IsFailure)
                return await CompleteAsync(envelope, attempt, effect.ToResult(), caller, correlation, ruleDetail, token).ConfigureAwait(false);
            string? target = generated?.PrimaryEntityId ?? CommandEffectIdentity.Primary(envelope, effect.Value);
            IReadOnlyList<CommandIdMapItem> ids = effect.Value is ICommandEffectMetadata metadata ? metadata.CreatedIds
                : generated?.CreatedIds ?? (envelope.AggregateId is null && target is not null
                    ? [new CommandIdMapItem(0, envelope.AggregateType, target)] : []);
            string? primaryId = ids.FirstOrDefault(i => i.Slot == 0)?.ServerEntityId ?? target;
            string? version = (effect.Value as ICommandEffectMetadata)?.ServerVersion;
            if (version is null && (primaryId ?? envelope.AggregateId) is { } targetId)
            {
                Result<string?> applied = await versions.TryReadAsync(envelope.AggregateType, targetId, token).ConfigureAwait(false);
                if (applied.IsFailure) return Result<SettlementResult>.Fail(applied.Error, applied.Detail);
                version = applied.Value;
            }
            if (version is not null && (!Guid.TryParseExact(version, "D", out var parsed) || parsed.ToString("D") != version))
                return Result<SettlementResult>.Fail(ErrorCodes.DispatchFailed, "noncanonical server version");
            SettlementResult settlement = SettlementMapper.Map(envelope.OperationId, Result.Ok(), clock.UtcNow,
                dispatched!.FinancialEffect, primaryId ?? envelope.AggregateId, version, ruleDetail);
            Result receipt = await StoreSettlementAsync(settlement, attempt, ids, token).ConfigureAwait(false);
            if (receipt.IsFailure) return Result<SettlementResult>.Fail(receipt.Error, receipt.Detail);
            Result appended = await outbox.AppendAsync(envelope, settlement, token).ConfigureAwait(false);
            if (appended.IsFailure) return Result<SettlementResult>.Fail(appended.Error, appended.Detail);
            Result audited = await AuditAsync(settlement, caller, correlation, "command.settled", token).ConfigureAwait(false);
            return audited.IsSuccess ? Result<SettlementResult>.Ok(settlement) : Result<SettlementResult>.Fail(audited.Error, audited.Detail);
        }, ct).ConfigureAwait(false);

        if (committed.IsSuccess)
        {
            if (committed.Value!.Outcome != SettlementOutcome.Deferred)
                metrics?.CommandsSettled.Add(1,
                    new KeyValuePair<string, object?>("outcome", committed.Value.Outcome.ToString()),
                    new KeyValuePair<string, object?>("commandType", catalog.Entries.ContainsKey(envelope.CommandType)
                        ? envelope.CommandType : "unknown"));
            return committed;
        }
        // No effect/receipt committed on a transactional failure. Keep it recoverable, never invent an acceptance.
        return await DeferAsync(envelope, committed.Error, caller, correlation, ct).ConfigureAwait(false);
    }

    private async Task<Result<SettlementResult>> CompleteAsync(CommandEnvelope envelope, string attempt, Result result,
        ResolvedMembership caller, string correlation, string? ruleDetail, CancellationToken ct)
    {
        SettlementResult settlement = SettlementMapper.Map(envelope.OperationId, result, clock.UtcNow, ruleDetail: ruleDetail);
        Result receipt = await StoreSettlementAsync(settlement, attempt, [], ct).ConfigureAwait(false);
        if (receipt.IsFailure) return Result<SettlementResult>.Fail(receipt.Error, receipt.Detail);
        Result audited = await AuditAsync(settlement, caller, correlation, "command.settled", ct).ConfigureAwait(false);
        return audited.IsSuccess ? Result<SettlementResult>.Ok(settlement) : Result<SettlementResult>.Fail(audited.Error, audited.Detail);
    }

    private Task<Result> StoreSettlementAsync(SettlementResult settlement, string attempt,
        IReadOnlyList<CommandIdMapItem> ids, CancellationToken ct) => store.CompleteAsync(new IntakeSettlement(
            settlement.OperationId, attempt, (short)settlement.Outcome, settlement.ErrorCode, settlement.Detail,
            settlement.ServerEntityId, settlement.ServerVersion is null ? null : Guid.ParseExact(settlement.ServerVersion, "D"),
            (short)settlement.FinancialEffect, settlement.ResponsibleRole, settlement.SettledAtUtc, ids), ct);

    private async Task<Result<SettlementResult>> ObserveAsync(CommandEnvelope envelope, Result result, ResolvedMembership caller,
        string correlation, string action, CancellationToken ct)
    {
        SettlementResult settlement = SettlementMapper.Map(envelope.OperationId, result, clock.UtcNow);
        Result audited = await AuditAsync(settlement, caller, correlation, action, ct).ConfigureAwait(false);
        return audited.IsSuccess ? Result<SettlementResult>.Ok(settlement) : Result<SettlementResult>.Fail(audited.Error, audited.Detail);
    }

    private async Task<Result<SettlementResult>> DeferAsync(CommandEnvelope envelope, ErrorCode code,
        ResolvedMembership caller, string correlation, CancellationToken ct)
    {
        var settlement = new SettlementResult(envelope.OperationId, SettlementOutcome.Deferred, code.Code, null, null,
            null, clock.UtcNow, FinancialEffect.Unknown, ErrorResponsibility.For(code.Code));
        Result audited = await AuditAsync(settlement, caller, correlation, "command.deferred", ct).ConfigureAwait(false);
        return audited.IsSuccess ? Result<SettlementResult>.Ok(settlement) : Result<SettlementResult>.Fail(audited.Error, audited.Detail);
    }

    private Task<Result> AuditAsync(SettlementResult settlement, ResolvedMembership caller, string correlation,
        string action, CancellationToken ct) => audit.WriteAsync(SecurityAuditEntry.Create(
            IdGenerator.NewId(clock.UtcNow), caller.TenantId, clock.UtcNow,
            settlement.ErrorCode == ErrorCodes.IdempotencyFingerprintMismatch.Code ? "integrity"
                : settlement.FinancialEffect == FinancialEffect.Applied ? "financial" : "security",
            action, settlement.Outcome.ToString(), correlation, caller.UserId, caller.DeviceId,
            SecurityAuditDetail.Create(("operationId", settlement.OperationId), ("code", settlement.ErrorCode))), ct);
}
