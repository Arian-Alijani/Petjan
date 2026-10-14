using Animora.Application.Abstractions;
using Animora.Application.Behaviors;
using Animora.Contracts.Diagnostics;
using Animora.Domain.Results;

namespace Animora.Application.Dispatch;

/// <summary>
/// Resolves the frozen entry and runs its pre-composed chain. Composition order (outer → inner):
/// Correlation → ExceptionMapping → Validation → Authorization → Entitlement → Transaction (commands only)
/// → CommandJournal (journaled commands only) → Audit (annotated commands only) → Handler. ExceptionMapping is the outermost <i>catch</i>,
/// placed just inside Correlation so mapped failures still carry the correlation prefix.
/// </summary>
public sealed class Dispatcher(HandlerRegistry registry, IServiceProvider services) : IDispatcher
{
    /// <summary>The fixed behavior chain, outer → inner. Never reordered, never composed per call.</summary>
    public static IReadOnlyList<IPipelineBehavior> Chain { get; } =
    [
        new CorrelationBehavior(),
        new ExceptionMappingBehavior(),
        new ValidationBehavior(),
        new AuthorizationBehavior(),
        new EntitlementBehavior(),
        new TransactionBehavior(),
        new CommandJournalBehavior(),
        new AuditBehavior(),
    ];

    /// <summary>
    /// The server-side chain: identical to <see cref="Chain"/> minus <c>CommandJournalBehavior</c> (client-only — needs the
    /// local journal and sequence allocator). Selected through <see cref="ApplicationPipelineProfile.Server"/>.
    /// </summary>
    public static IReadOnlyList<IPipelineBehavior> ServerChain { get; } =
    [
        new CorrelationBehavior(),
        new ExceptionMappingBehavior(),
        new ValidationBehavior(),
        new AuthorizationBehavior(),
        new EntitlementBehavior(),
        new TransactionBehavior(),
        new AuditBehavior(),
    ];

    public Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CommandContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(ctx);
        TraceLog.Debug($"SendAsync {command.GetType().Name} (op={ctx.OperationId}, tenant={ctx.TenantId}, user={ctx.UserId})", "dispatch");
        return Run<TResult>(command, ctx, RequestKind.Command, ct);
    }

    public Task<Result<TResult>> QueryAsync<TResult>(IQuery<TResult> query, QueryContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(ctx);
        TraceLog.Debug($"QueryAsync {query.GetType().Name} (tenant={ctx.TenantId}, user={ctx.UserId}, corr={ctx.CorrelationId})", "dispatch");
        return Run<TResult>(query, ctx, RequestKind.Query, ct);
    }

    private async Task<Result<TResult>> Run<TResult>(object request, object ctx, RequestKind kind, CancellationToken ct)
    {
        var type = request.GetType();
        if (!registry.TryGet(type, out var entry))
        {
            TraceLog.Error($"Dispatch {kind} {type.Name} → NO HANDLER REGISTERED (HANDLER_NOT_REGISTERED).", null, "dispatch");
            return Result<TResult>.Fail(ErrorCodes.HandlerNotRegistered, type.FullName);
        }
        if (entry.Kind != kind || entry.Pipeline is not PipelineStep<TResult> pipeline)
        {
            TraceLog.Error($"Dispatch {kind} {type.Name} → KIND/RESULT MISMATCH (registered as {entry.Kind}/{entry.ResultType.Name}).", null, "dispatch");
            return Result<TResult>.Fail(ErrorCodes.DispatchFailed,
                $"{type.FullName}: registered as {entry.Kind}/{entry.ResultType.Name}.");
        }

        TraceLog.Debug($"Dispatch {kind} {type.Name} → running behaviour chain (Correlation→ExceptionMapping→Validation→Authorization→Entitlement→Transaction→[Journal]→Audit).", "dispatch");
        var result = await pipeline(services, request, ctx, ct).ConfigureAwait(false);
        if (result.IsFailure)
            TraceLog.Warn($"Dispatch {kind} {type.Name} ← FAILED {result.Error} detail='{result.Detail}'", "dispatch");
        else
            TraceLog.Debug($"Dispatch {kind} {type.Name} ← Ok.", "dispatch");
        return result;
    }
}
