using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Dispatch;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Behaviors;

/// <summary>
/// Stage 7 — commands only. Sits <b>between</b> <see cref="TransactionBehavior"/> and the handler, so the
/// enqueue and the business write commit in one transaction (ARCHITECTURE.md §12.1). It is active only for a
/// command implementing <see cref="IJournaledCommand"/>; every other command (UI prefs, profile settings)
/// skips this link at registration and never touches the journal (CONVENTIONS §6 item 1).
/// </summary>
public sealed class CommandJournalBehavior : IPipelineBehavior
{
    public string Name => "CommandJournal";

    public PipelineStep<TResult> Wrap<TRequest, TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind == RequestKind.Query || !typeof(IJournaledCommand).IsAssignableFrom(typeof(TRequest)))
            return next;

        return async (sp, request, context, ct) =>
        {
            var command = (IJournaledCommand)request;
            var commandContext = (CommandContext)context;
            var journal = sp.GetRequiredService<ICommandJournal>();
            var sequences = sp.GetRequiredService<ILocalSequenceAllocator>();

            var payloadJson = CommandFingerprint.CanonicalJson(request);
            var fingerprint = CommandFingerprint.Compute(command.CommandTypeName, payloadJson);
            // A replay must never re-enter the handler: posting would consume stock twice (or
            // fail after the first posting), even though EnqueueAsync deduplicates the row.
            // Unit commands have a canonical stored success outcome; other result shapes need
            // an explicit durable response before they can be safely reconstructed.
            var previous = await journal.FindByOperationIdAsync(commandContext.OperationId, ct).ConfigureAwait(false);
            if (previous.IsFailure) return Result<TResult>.Fail(previous.Error, previous.Detail);
            if (previous.Value is { } stored && stored.CommandFingerprint == fingerprint)
            {
                // Unit: canonical stored success — reconstruct it directly.
                if (typeof(TResult) == typeof(Unit))
                    return Result<TResult>.Ok((TResult)(object)Unit.Value);

                // Non-Unit (42/43 journaled commands return the new aggregate id as string): the create
                // id is deterministic — IdGenerator.ForOperation(operationId, slot 0) == operationId —
                // so a create-style command (no inbound aggregate id) can be replayed WITHOUT re-entering
                // the handler, which would otherwise double-post the ledger (finance.expense.*). Prefer
                // the server-assigned id once settled, else the deterministic client id.
                if (typeof(TResult) == typeof(string) && command.AggregateIdOrNull is null)
                {
                    var replayId = !string.IsNullOrEmpty(stored.ServerEntityId)
                        ? stored.ServerEntityId!
                        : IdGenerator.ForOperation(commandContext.OperationId, 0, commandContext.CreatedAtUtc);
                    return Result<TResult>.Ok((TResult)(object)replayId);
                }
                // Other result shapes have no durable stored response yet (would need a journal result
                // payload column / migration); fall through and let the normal idempotent enqueue path
                // run — EnqueueAsync still deduplicates the row.
            }
            var dependsOn = commandContext.DependsOnOperationId
                ?? await PrerequisiteAsync(journal, command, ct).ConfigureAwait(false);
            var sequence = await sequences.NextAsync(ct).ConfigureAwait(false);
            if (sequence.IsFailure) return Result<TResult>.Fail(sequence.Error, sequence.Detail);

            var envelope = CommandEnvelope.Create(
                commandContext.OperationId,
                commandContext.TenantId,
                commandContext.UserId,
                commandContext.DeviceId,
                command.CommandTypeName,
                command.AggregateTypeName,
                command.AggregateIdOrNull,
                payloadJson,
                fingerprint,
                commandContext.CreatedAtUtc,
                sequence.Value,
                commandContext.CausationId,
                dependsOn,
                commandContext.ExpectedAggregateVersion,
                // CW-01: no canonical per-aggregate server version is known at authoring time (the command is
                // being created). It is threaded on re-submit from the stored PendingCommandRecord.ServerVersion.
                expectedServerVersion: null,
                // CW-02/CW-05: always stamp the active client rule-set version so the server can precondition it.
                ruleSetVersion: WireContract.RuleSetVersionString);

            var enqueued = await journal.EnqueueAsync(envelope, ct).ConfigureAwait(false);
            if (enqueued.IsFailure) return Result<TResult>.Fail(enqueued.Error, enqueued.Detail);

            return await next(sp, request, context, ct).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// D06: a follow-up on an aggregate whose creating or posting command has not settled yet must not upload
    /// ahead of it (a rejected Start/Post would otherwise be followed by doomed Rx/payment commands). The
    /// prerequisite is the newest unsettled <c>sales.invoice.post</c> on the aggregate, else the aggregate's
    /// own creating operation (root ids are minted from the creating operation id, slot 0). Only a locally
    /// journaled, not-yet-settled prerequisite is declared — a pulled peer aggregate or an already-settled
    /// creation adds no dependency, so nothing can be deferred on a command the server never received.
    /// </summary>
    private static async Task<string?> PrerequisiteAsync(ICommandJournal journal, IJournaledCommand command,
        CancellationToken ct)
    {
        if (command.AggregateIdOrNull is not { Length: > 0 } aggregateId) return null;

        var unsettled = await journal.GetUnsettledForAggregateAsync(command.AggregateTypeName, aggregateId, ct)
            .ConfigureAwait(false);
        if (unsettled.IsSuccess && command.CommandTypeName != CommandTypes.SalesInvoicePost)
        {
            // Reverse indexed scan for the newest matching post — no enumerator/closure alloc (PERFORMANCE.md §2).
            var list = unsettled.Value!;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].CommandType == CommandTypes.SalesInvoicePost)
                {
                    return list[i].OperationId;
                }
            }
        }

        var creation = await journal.FindByOperationIdAsync(aggregateId, ct).ConfigureAwait(false);
        return creation.IsSuccess && creation.Value is { } created
               && created.Status != SettlementState.Settled
               && string.Equals(created.AggregateType, command.AggregateTypeName, StringComparison.Ordinal)
            ? created.OperationId
            : null;
    }
}
