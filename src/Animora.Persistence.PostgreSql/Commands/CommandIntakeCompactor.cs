using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Commands;

/// <summary>
/// Retention compaction of the idempotency store (A-RULE 040): once a row is older than the full-record window, its
/// <c>PayloadJson</c> is nulled while the fingerprint, outcome and ids are kept as the receipt. Idempotent SQL; never
/// deletes. Invoked by the S13 maintenance worker (vendor-side, cross-tenant by design), never on a request path.
/// </summary>
public sealed class CommandIntakeCompactor(AnimoraServerDbContext context)
{
    /// <summary>
    /// Nulls the payload of every <c>Settled</c>/<c>Failed</c> row settled-or-received before <paramref name="cutoff"/>.
    /// Returns the number of rows compacted (0 on a repeat run).
    /// </summary>
    public async Task<int> CompactAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        DateTimeOffset when = cutoff.ToUniversalTime();
        short settled = (short)IntakeStatus.Settled;
        short failed = (short)IntakeStatus.Failed;

        return await context.Database.ExecuteSqlAsync(
            $"""
            UPDATE public."CommandIntake" SET "PayloadJson" = NULL
             WHERE "PayloadJson" IS NOT NULL
               AND (("Status" = {settled} AND "SettledUtc" < {when})
                 OR ("Status" = {failed} AND "ReceivedUtc" < {when}))
            """,
            ct).ConfigureAwait(false);
    }
}
