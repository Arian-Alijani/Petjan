using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Outbox;

/// <summary>
/// The atomic outbox claim and completion (ARCHITECTURE.md §12.1). The claim is one conditional <c>UPDATE</c>
/// under a lease, so two competing relays never see the same message; an expired lease is reclaimable, so a
/// crashed relay never strands a message (A-RULE 026).
/// </summary>
public sealed class OutboxClaim(AnimoraLocalDbContext db, IClock clock)
{
    /// <summary>Default lease; long enough for a relay step, short enough to recover a crash promptly.</summary>
    public static readonly TimeSpan DefaultLease = TimeSpan.FromSeconds(60);

    /// <summary>Claims up to <paramref name="max"/> eligible messages and returns exactly the rows it won.</summary>
    public async Task<Result<IReadOnlyList<OutboxMessage>>> ClaimAsync(int max,
        TimeSpan? lease = null, CancellationToken ct = default)
    {
        if (max <= 0) return Result<IReadOnlyList<OutboxMessage>>.Ok([]);
        var now = clock.UtcNow;
        var token = IdGenerator.NewId(now);
        try
        {
            await PipelineSql.ClaimOutboxAsync(db, token, now, now + (lease ?? DefaultLease), max, ct)
                .ConfigureAwait(false);
            var rows = await db.Set<OutboxMessage>().AsNoTracking()
                .Where(m => m.ClaimToken == token)
                .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
                .ToListAsync(ct).ConfigureAwait(false);
            IReadOnlyList<OutboxMessage> claimed = rows;
            return Result<IReadOnlyList<OutboxMessage>>.Ok(claimed);
        }
        catch (SqliteException) { return Result<IReadOnlyList<OutboxMessage>>.Fail(ErrorCodes.StoreUnavailable); }
    }

    /// <summary>Marks a claimed message succeeded.</summary>
    public async Task<Result> CompleteAsync(string messageId, CancellationToken ct = default)
    {
        var message = await db.Set<OutboxMessage>().AsTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId, ct).ConfigureAwait(false);
        if (message is null) return Result.Fail(ErrorCodes.NotFound, $"OutboxMessage={messageId}");
        message.MarkSucceeded(clock.UtcNow);
        return await SaveAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a relay failure with backoff. After <see cref="Application.Commands.RetryPolicy.MaxAttempts"/> the
    /// message is dead-lettered with its last error preserved.
    /// </summary>
    public async Task<Result> FailAsync(string messageId, ErrorCode error, TimeSpan backoff, CancellationToken ct = default)
    {
        var message = await db.Set<OutboxMessage>().AsTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId, ct).ConfigureAwait(false);
        if (message is null) return Result.Fail(ErrorCodes.NotFound, $"OutboxMessage={messageId}");
        message.MarkFailed(error.Code, backoff, clock.UtcNow,
            Application.Commands.RetryPolicy.IsExhausted(message.Attempts + 1));
        return await SaveAsync(ct).ConfigureAwait(false);
    }

    private async Task<Result> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException) { return Result.Fail(ErrorCodes.StoreUnavailable); }
        catch (SqliteException) { return Result.Fail(ErrorCodes.StoreUnavailable); }
    }
}
