using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Dispatch;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Behaviors;

/// <summary>
/// Stage 1. Begins (or propagates) the context's <c>CorrelationId</c> for the whole call and prefixes every
/// returned failure's <c>Detail</c> with <c>[corr:&lt;id&gt;]</c>.
/// </summary>
public sealed class CorrelationBehavior : IPipelineBehavior
{
    public string Name => "Correlation";

    public PipelineStep<TResult> Wrap<TRequest, TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(next);
        return async (sp, request, context, ct) =>
        {
            var correlation = sp.GetRequiredService<ICorrelationContext>();
            // Seed the FULL ambient correlation (identity included) from the context, not just the id.
            // The acting user flows into the activity-feed writer so locally-authored history rows carry the
            // real actor instead of falling back to the «سیستم» placeholder.
            using var scope = correlation.Begin(AmbientFor(context));
            var result = await next(sp, request, context, ct).ConfigureAwait(false);
            return result.IsSuccess ? result : Result<TResult>.Fail(result.Error, Prefix(correlation.CurrentId, result.Detail));
        };
    }

    /// <summary>The <c>[corr:&lt;id&gt;]</c> prefix convention shared by every failure <c>Detail</c>.</summary>
    public static string Prefix(string? correlationId, string? detail) =>
        string.IsNullOrEmpty(detail) ? $"[corr:{correlationId}]" : $"[corr:{correlationId}] {detail}";

    /// <summary>
    /// Projects the dispatch context onto the ambient correlation, carrying the acting identity so the activity
    /// feed can stamp the real actor. <see cref="QueryContext"/> has no device/operation, so those stay blank.
    /// </summary>
    private static AmbientCorrelation AmbientFor(object context) => context switch
    {
        CommandContext c => new AmbientCorrelation(
            string.IsNullOrWhiteSpace(c.CorrelationId) ? IdGenerator.NewId() : c.CorrelationId,
            c.TenantId, c.UserId, c.DeviceId, c.OperationId),
        QueryContext q => new AmbientCorrelation(
            string.IsNullOrWhiteSpace(q.CorrelationId) ? IdGenerator.NewId() : q.CorrelationId,
            q.TenantId, q.UserId, string.Empty, null),
        _ => new AmbientCorrelation(IdGenerator.NewId(), string.Empty, string.Empty, string.Empty, null),
    };
}
