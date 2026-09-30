using Animora.Application.Abstractions;
using Animora.Domain.Primitives;
using Animora.Domain.Events;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.Sqlite.Repositories;

/// <summary>
/// Atomic unit of work: one transaction covers work → save → event dispatch → handler writes → commit.
/// Domain events are cleared only after commit; on any failure the transaction is rolled back, pending
/// events stay on the aggregates and the change tracker is cleared so a retry reloads fresh state.
/// </summary>
public sealed class LocalUnitOfWork(AnimoraLocalDbContext db, IDomainEventDispatcher dispatcher) : IUnitOfWork
{
    // Audit and other ports may flush inside the ambient transaction. Preserve movement
    // events before those flushes reset Added state; dispatch exactly once at commit.
    private readonly Dictionary<string, StockMovementRecordedEvent> _movementEvents = new(StringComparer.Ordinal);

    public async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        // Inside ExecuteAsync the ambient transaction owns dispatch and commit: only flush.
        if (db.Database.CurrentTransaction is not null)
        {
            CaptureMovementEvents();
            return await StoreAsync(() => db.SaveChangesAsync(ct));
        }
        var outcome = await RunAsync(_ => Task.FromResult(Result<bool>.Ok(true)), ct);
        return outcome.ToResult();
    }

    public Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunAsync(work, ct);
    }

    private async Task<Result<T>> RunAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A unit of work transaction is already active; transactions never nest.");
        IDbContextTransaction? transaction = null;
        var committed = false;
        try
        {
            var step = await StoreAsync(async () => transaction = await db.Database.BeginTransactionAsync(ct));
            if (step.IsFailure) return Fail<T>(step);

            var outcome = await work(ct);
            if (outcome.IsFailure) return outcome;

            var roots = db.ChangeTracker.Entries().Select(e => e.Entity).OfType<IHasDomainEvents>()
                .Where(r => r.DomainEvents.Count != 0).ToArray();
            // Deterministic: occurrence time, then raise order (stable sort).
            // StockMovement is append-only (not an aggregate), so it cannot buffer events itself.
            // Capture each added movement before SaveChanges resets its state. This keeps the
            // stock ledger, COGS, outbox and activity feed in the same commit for every writer.
            CaptureMovementEvents();
            var events = roots.SelectMany(r => r.DomainEvents).Concat(_movementEvents.Values)
                .OrderBy(e => e.OccurredAtUtc).ToArray();

            if ((step = await StoreAsync(() => db.SaveChangesAsync(ct))).IsFailure) return Fail<T>(step);
            foreach (var domainEvent in events)
                if ((step = await dispatcher.DispatchAsync(domainEvent, ct)).IsFailure) return Fail<T>(step);
            if ((step = await StoreAsync(() => db.SaveChangesAsync(ct))).IsFailure) return Fail<T>(step);
            if ((step = await StoreAsync(() => transaction!.CommitAsync(ct))).IsFailure) return Fail<T>(step);

            committed = true;
            foreach (var root in roots) root.ClearDomainEvents();
            return outcome;
        }
        finally
        {
            // Disposing an uncommitted transaction rolls it back (business + handler writes).
            if (transaction is not null) await transaction.DisposeAsync();
            if (!committed) db.ChangeTracker.Clear();
            _movementEvents.Clear();
        }
    }

    private void CaptureMovementEvents()
    {
        foreach (var entry in db.ChangeTracker.Entries<StockMovement>())
        {
            if (entry.State != EntityState.Added) continue;
            var m = entry.Entity;
            if (_movementEvents.ContainsKey(m.Id)) continue;
            _movementEvents.Add(m.Id, new StockMovementRecordedEvent(IdGenerator.NewId(m.OccurredUtc),
                m.TenantId, m.OccurredUtc, m.Id, m.ProductId, m.VariantId, m.Kind, m.Quantity.Value));
        }
    }

    private static Result<T> Fail<T>(Result failure) => Result<T>.Fail(failure.Error, failure.Detail);

    /// <summary>Maps store failures to results; cancellation and programming errors propagate.</summary>
    private static async Task<Result> StoreAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException) { return Result.Fail(ErrorCodes.ConcurrencyConflict); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException)
        {
            return Result.Fail(ErrorCodes.StoreUnavailable);
        }
        catch (SqliteException) { return Result.Fail(ErrorCodes.StoreUnavailable); }
    }
}
