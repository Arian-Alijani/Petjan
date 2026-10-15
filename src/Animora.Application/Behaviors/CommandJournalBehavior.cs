using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Dispatch;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Behaviors;

/// <summary>
/// Stage 6 — commands only. Sits <b>between</b> <see cref="TransactionBehavior"/> and the handler, so the
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
            if (previous.Value is { } stored && stored.CommandFingerprint == fingerprint
                && typeof(TResult) == typeof(Unit))
                return Result<TResult>.Ok((TResult)(object)Unit.Value);
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
                commandContext.ExpectedAggregateVersion);

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
            var post = unsettled.Value!.LastOrDefault(c => c.CommandType == CommandTypes.SalesInvoicePost);
            if (post is not null) return post.OperationId;
        }

        var creation = await journal.FindByOperationIdAsync(aggregateId, ct).ConfigureAwait(false);
        return creation.IsSuccess && creation.Value is { } created
               && created.Status != SettlementState.Settled
               && string.Equals(created.AggregateType, command.AggregateTypeName, StringComparison.Ordinal)
            ? created.OperationId
            : null;
    }
}
