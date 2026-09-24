using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Dispatch;
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
            using var scope = correlation.Begin(CorrelationIdOf(context));
            var result = await next(sp, request, context, ct).ConfigureAwait(false);
            return result.IsSuccess ? result : Result<TResult>.Fail(result.Error, Prefix(correlation.CurrentId, result.Detail));
        };
    }

    /// <summary>The <c>[corr:&lt;id&gt;]</c> prefix convention shared by every failure <c>Detail</c>.</summary>
    public static string Prefix(string? correlationId, string? detail) =>
        string.IsNullOrEmpty(detail) ? $"[corr:{correlationId}]" : $"[corr:{correlationId}] {detail}";

    private static string? CorrelationIdOf(object context) => context switch
    {
        CommandContext c => c.CorrelationId,
        QueryContext q => q.CorrelationId,
        _ => null,
    };
}
