using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Behaviors;

/// <summary>
/// Stage 4 — entitlement plane only (A-RULE 023), evaluated after authorization. A refusal short-circuits
/// with <c>ENTITLEMENT_REQUIRED</c> (a gate's own <c>Entitlement</c>-category code, e.g.
/// <c>OFFLINE_CAPABILITY_EXPIRED</c>, is kept).
/// </summary>
public sealed class EntitlementBehavior : IPipelineBehavior
{
    public string Name => "Entitlement";

    public PipelineStep<TResult> Wrap<TRequest, TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(next);
        return async (sp, request, context, ct) =>
        {
            var gate = sp.GetRequiredService<IEntitlementGate>();
            var verdict = context is CommandContext c
                ? await gate.EvaluateAsync(request, c, ct).ConfigureAwait(false)
                : await gate.EvaluateAsync(request, (QueryContext)context, ct).ConfigureAwait(false);
            if (verdict.IsSuccess) return await next(sp, request, context, ct).ConfigureAwait(false);
            var code = verdict.Error.Category == ErrorCode.Entitlement ? verdict.Error : ErrorCodes.EntitlementRequired;
            return Result<TResult>.Fail(code, verdict.Detail);
        };
    }
}
