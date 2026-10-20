using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Behaviors;

/// <summary>
/// Stage 4 — authorization plane only (A-RULE 023). A denial short-circuits with <c>PERMISSION_DENIED</c>
/// (a gate's own <c>Forbidden</c>-category code, e.g. <c>TENANT_MISMATCH</c>, is kept).
/// </summary>
public sealed class AuthorizationBehavior : IPipelineBehavior
{
    public string Name => "Authorization";

    public PipelineStep<TResult> Wrap<TRequest, TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(next);
        return async (sp, request, context, ct) =>
        {
            var gate = sp.GetRequiredService<IAuthorizationGate>();
            var verdict = context is CommandContext c
                ? await gate.EvaluateAsync(request, c, ct).ConfigureAwait(false)
                : await gate.EvaluateAsync(request, (QueryContext)context, ct).ConfigureAwait(false);
            if (verdict.IsSuccess) return await next(sp, request, context, ct).ConfigureAwait(false);
            var code = verdict.Error.Category == ErrorCode.Forbidden ? verdict.Error : ErrorCodes.PermissionDenied;
            return Result<TResult>.Fail(code, verdict.Detail);
        };
    }
}
