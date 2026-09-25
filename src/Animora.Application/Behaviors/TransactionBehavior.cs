using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Dispatch;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Behaviors;

/// <summary>
/// Stage 5 — commands only: wraps the handler in <see cref="IUnitOfWork.ExecuteAsync{T}"/>. Query entries
/// skip this link at registration (they never open a transaction). A command with no
/// <see cref="IUnitOfWork"/> registered fails closed with <c>DISPATCH_FAILED</c>. A command marked
/// <see cref="ISelfTransactedCommand"/> bypasses this link (its handler saves explicitly).
/// </summary>
public sealed class TransactionBehavior : IPipelineBehavior
{
    public string Name => "Transaction";

    public PipelineStep<TResult> Wrap<TRequest, TResult>(PipelineStep<TResult> next, PipelineDescriptor descriptor)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind == RequestKind.Query || typeof(ISelfTransactedCommand).IsAssignableFrom(typeof(TRequest)))
            return next;

        return (sp, request, context, ct) =>
        {
            var uow = sp.GetService<IUnitOfWork>();
            return uow is null
                ? Task.FromResult(Result<TResult>.Fail(ErrorCodes.DispatchFailed, "No IUnitOfWork registered for commands."))
                : uow.ExecuteAsync(token => next(sp, request, context, token), ct);
        };
    }
}
