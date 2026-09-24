using Animora.Application.Commands;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.Data.Sqlite;

namespace Animora.Persistence.Sqlite.Commands;

/// <summary>
/// The DB-backed <see cref="ILocalSequenceAllocator"/>: one atomic upsert-increment per call, so two concurrent
/// enqueues never share a <c>LocalSequence</c>. Inside a unit of work it joins the ambient transaction.
/// </summary>
public sealed class LocalSequenceAllocator(AnimoraLocalDbContext db) : ILocalSequenceAllocator
{
    public async Task<Result<long>> NextAsync(CancellationToken ct = default)
    {
        try
        {
            var value = await PipelineSql.NextSequenceAsync(db, LocalSequenceCounter.DefaultName, ct)
                .ConfigureAwait(false);
            return Result<long>.Ok(value);
        }
        catch (SqliteException) { return Result<long>.Fail(ErrorCodes.StoreUnavailable); }
    }
}
