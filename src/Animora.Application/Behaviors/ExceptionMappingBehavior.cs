using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Animora.Application.Behaviors;

/// <summary>
/// Stage 2 — the outermost catch (sits directly inside Correlation so mapped failures keep their prefix).
/// Maps via <see cref="ErrorMapping.FromException"/>; <see cref="OperationCanceledException"/> is rethrown.
/// The exception is logged once; <c>Detail</c> carries only the exception type name — never a stack trace.
/// The only broad <c>catch (Exception)</c> in the Application layer.
/// </summary>
public sealed class ExceptionMappingBehavior : IPipelineBehavior
{
    public string Name => "ExceptionMapping";

    public PipelineStep<TResult> Wrap<TRequest, TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(descriptor);
        var requestName = descriptor.RequestType.Name;

        return async (sp, request, context, ct) =>
        {
            try
            {
                return await next(sp, request, context, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // The single sanctioned broad catch: defects become Result failures (CONVENTIONS §4).
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var code = ErrorMapping.FromException(ex);
                var logger = (ILogger?)sp.GetService<ILogger<ExceptionMappingBehavior>>() ?? NullLogger.Instance;
#pragma warning disable CA1848, CA2254 // Failure path only; a single structured log per defect.
                logger.LogError(ex, "Dispatch of {Request} failed with {ErrorCode}.", requestName, code.Code);
#pragma warning restore CA1848, CA2254
                return Result<TResult>.Fail(code, $"{requestName}: {ex.GetType().Name}");
            }
        };
    }
}
