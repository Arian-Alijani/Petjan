using Animora.Application.Abstractions;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class LocalUnitOfWork(AnimoraLocalDbContext db, IDomainEventDispatcher dispatcher) : IUnitOfWork
{
    public async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        // Dispatchers may persist outbox rows: they must share the caller's transaction.
        var roots = db.ChangeTracker.Entries().Select(e => e.Entity).OfType<IHasDomainEvents>()
            .Where(e => e.DomainEvents.Count != 0).ToArray();
        try
        {
            await db.SaveChangesAsync(ct);
            foreach (var root in roots)
                foreach (var domainEvent in root.DomainEvents)
                {
                    var result = await dispatcher.DispatchAsync(domainEvent, ct);
                    if (result.IsFailure) return result;
                }
            await db.SaveChangesAsync(ct);
            foreach (var root in roots) root.ClearDomainEvents();
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException) { return Result.Fail(ErrorCodes.ConcurrencyConflict); }
        catch (SqliteException) { return Result.Fail(ErrorCodes.StoreUnavailable); }
    }

    public async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var outcome = await work(ct);
        if (outcome.IsFailure) { await transaction.RollbackAsync(ct); db.ChangeTracker.Clear(); return outcome; }
        var saved = await SaveAsync(ct);
        if (saved.IsFailure)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Result<T>.Fail(saved.Error, saved.Detail);
        }
        await transaction.CommitAsync(ct);
        return outcome;
    }
}
