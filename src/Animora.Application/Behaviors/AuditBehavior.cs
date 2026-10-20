using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Dispatch;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Behaviors;

/// <summary>Stage 8 — audits annotated commands from inside the transaction behavior.</summary>
public sealed class AuditBehavior : IPipelineBehavior
{
    public string Name => "Audit";

    public PipelineStep<TResult> Wrap<TRequest, TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind != RequestKind.Command || descriptor.Audit is null) return next;

        var binding = descriptor.Audit;
        return async (sp, request, context, ct) =>
        {
            var result = await next(sp, request, context, ct).ConfigureAwait(false);
            var command = (CommandContext)context;
            var outcome = result.IsSuccess
                ? AuditOutcome.Success
                : result.Error.Category is ErrorCode.Forbidden or ErrorCode.Entitlement
                    ? AuditOutcome.Denied
                    : AuditOutcome.Failed;
            var detail = result.IsFailure ? result.Error.Code : null;
            var record = new AuditRecord(
                command.TenantId,
                command.UserId,
                command.DeviceId,
                sp.GetRequiredService<IClock>().UtcNow,
                binding.Category,
                binding.Action,
                null,
                null,
                command.OperationId,
                command.CorrelationId,
                outcome,
                null,
                null,
                null,
                null,
                detail);

            var written = await sp.GetRequiredService<IAuditWriter>().WriteAsync(record, ct).ConfigureAwait(false);
            return written.IsSuccess
                ? result
                : Result<TResult>.Fail(ErrorCodes.AuditWriteFailed, written.Detail ?? written.Error.Code);
        };
    }
}
