using Animora.Application.Abstractions;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Commands;

/// <summary>
/// Intake owns the outer transaction. The unchanged Application Transaction behavior joins it with
/// savepoints, rolling back failed handler effects before a rejection receipt can be committed.
/// </summary>
internal sealed class CommandPlaneUnitOfWork(ServerUnitOfWork inner, AnimoraServerDbContext context) : IUnitOfWork
{
    private int _depth;
    public Task<Result> SaveAsync(CancellationToken ct = default) => inner.SaveAsync(ct);

    public async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default)
    {
        if (_depth == 0)
        {
            try
            {
                _depth++;
                return await inner.ExecuteAsync(work, ct).ConfigureAwait(false);
            }
            finally
            {
                _depth--;
                // EF state must not survive rollback, a retry, or another item in the same HTTP batch.
                context.ChangeTracker.Clear();
            }
        }
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Command intake requires an active transaction.");
        string savepoint = $"intake_{_depth}";
        await transaction.CreateSavepointAsync(savepoint, ct).ConfigureAwait(false);
        try
        {
            _depth++;
            Result<T> result = await work(ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                await transaction.RollbackToSavepointAsync(savepoint, ct).ConfigureAwait(false);
                context.ChangeTracker.Clear();
            }
            await transaction.ReleaseSavepointAsync(savepoint, ct).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, ct).ConfigureAwait(false);
            context.ChangeTracker.Clear();
            throw;
        }
        finally { _depth--; }
    }
}
