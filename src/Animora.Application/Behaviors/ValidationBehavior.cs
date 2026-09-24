using Animora.Application.Dispatch;
using Animora.Application.Validation;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Behaviors;

/// <summary>
/// Stage 2. Runs every registered <see cref="IValidator{T}"/>; any failure short-circuits with
/// <c>VALIDATION_FAILED</c> (failures encoded by <see cref="ValidationFailure.FormatDetail"/>). Never throws.
/// Entries without validators skip this link entirely at registration.
/// </summary>
public sealed class ValidationBehavior : IPipelineBehavior
{
    public string Name => "Validation";

    public PipelineStep<TResult> Wrap<TRequest, TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!descriptor.HasValidators) return next;

        return async (sp, request, context, ct) =>
        {
            List<ValidationFailure>? failures = null;
            foreach (var validator in sp.GetServices<IValidator<TRequest>>())
            {
                var found = await validator.ValidateAsync((TRequest)request, ct).ConfigureAwait(false);
                if (found.Count == 0) continue;
                (failures ??= []).AddRange(found);
            }
            return failures is null
                ? await next(sp, request, context, ct).ConfigureAwait(false)
                : Result<TResult>.Fail(ErrorCodes.ValidationFailed, ValidationFailure.FormatDetail(failures));
        };
    }
}
