using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Sync;

/// <summary>A bounded, stable page of unsettled commands for the UI sync list (C07/C08/E06).</summary>
public sealed record UnsettledCommandListQuery(int Page, int PageSize)
    : IQuery<PagedResult<PendingCommandRecord>>;

/// <summary>Handles <see cref="UnsettledCommandListQuery"/> over the command journal.</summary>
public sealed class UnsettledCommandListQueryHandler(ICommandJournal journal)
    : IQueryHandler<UnsettledCommandListQuery, PagedResult<PendingCommandRecord>>
{
    public Task<Result<PagedResult<PendingCommandRecord>>> HandleAsync(UnsettledCommandListQuery query,
        QueryContext context, CancellationToken ct) => journal.GetUnsettledPageAsync(query.Page, query.PageSize, ct);
}
