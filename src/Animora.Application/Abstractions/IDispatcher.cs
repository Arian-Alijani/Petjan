using Animora.Domain.Results;

namespace Animora.Application.Abstractions;

/// <summary>
/// Send/query façade over the frozen handler registry. Never throws for expected or infrastructure
/// failures — only <see cref="OperationCanceledException"/> escapes.
/// </summary>
public interface IDispatcher
{
    Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CommandContext ctx, CancellationToken ct = default);

    Task<Result<TResult>> QueryAsync<TResult>(IQuery<TResult> query, QueryContext ctx, CancellationToken ct = default);
}
