using Animora.Domain.Results;

namespace Animora.Application.Abstractions;

/// <summary>Handles exactly one command type. Expected failures are returned, never thrown (CONVENTIONS §4).</summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CommandContext context, CancellationToken ct);
}
