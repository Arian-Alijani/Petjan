using Animora.Domain.Results;

namespace Animora.Application.Abstractions;

/// <summary>Handles exactly one query type. Read-only; never runs inside a unit of work.</summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, QueryContext context, CancellationToken ct);
}
