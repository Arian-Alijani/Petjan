using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Sync;

/// <summary>
/// Aggregate view of the pending-command queue for the UI sync badge (C07/C08/E06 bind here; nothing in the
/// UI reads the table directly).
/// </summary>
/// <param name="Total">Every unsettled command (not <c>Settled</c>/<c>Reversed</c>).</param>
/// <param name="PendingSettlement">Commands awaiting the server's decision.</param>
/// <param name="Rejected">Commands the server refused.</param>
/// <param name="Conflict">Commands that conflicted with server state.</param>
/// <param name="NeedsAttention">Commands that exhausted retries or need a human.</param>
/// <param name="OldestCreatedUtc">The oldest unsettled command's creation instant, when any exist.</param>
public sealed record PendingCommandSummary(
    int Total,
    int PendingSettlement,
    int Rejected,
    int Conflict,
    int NeedsAttention,
    DateTimeOffset? OldestCreatedUtc);

/// <summary>Returns the pending-command summary for the UI sync seam.</summary>
public sealed record PendingCommandSummaryQuery : IQuery<PendingCommandSummary>;

/// <summary>Handles <see cref="PendingCommandSummaryQuery"/> over the command journal.</summary>
public sealed class PendingCommandSummaryQueryHandler(ICommandJournal journal)
    : IQueryHandler<PendingCommandSummaryQuery, PendingCommandSummary>
{
    public Task<Result<PendingCommandSummary>> HandleAsync(PendingCommandSummaryQuery query, QueryContext context,
        CancellationToken ct) => journal.GetSummaryAsync(ct);
}
