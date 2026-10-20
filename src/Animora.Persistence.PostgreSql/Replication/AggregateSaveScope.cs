using System.Data.Common;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

// EF1003 suppressed deliberately: SET CONSTRAINTS cannot take bind parameters (identifiers), and _constraints
// is built ONLY from trigger/constraint names derived from the EF model metadata - never from user input.
#pragma warning disable EF1003

namespace Animora.Persistence.PostgreSql.Replication;

/// <summary>
/// Child journal triggers touch the root Version. Defer just those triggers until EF has checked
/// every root's original token, then flush them and read the final tokens BEFORE settlement/commit.
/// Raw SQL retains immediate capture; the feed-sequence trigger remains deferred until commit.
/// </summary>
internal sealed class AggregateSaveScope : IDisposable, IAsyncDisposable
{
    private readonly AnimoraServerDbContext _db;
    private readonly EntityEntry<SyncableEntity>[] _roots;
    private readonly string _constraints;
    private readonly string _savepoint = "aggregate_" + Guid.NewGuid().ToString("N");
    private IDbContextTransaction? _transaction;
    private bool _ownsTransaction;
    private bool _hasSavepoint;
    private bool _completed;

    private AggregateSaveScope(AnimoraServerDbContext db, EntityEntry<SyncableEntity>[] roots, string constraints)
    {
        _db = db;
        _roots = roots;
        _constraints = constraints;
    }

    public static AggregateSaveScope? Create(AnimoraServerDbContext db)
    {
        // Entries() performs normal EF change detection, including replacement of immutable children.
        var changes = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var affected = new HashSet<(Type Type, string Id)>();
        var constraints = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in SyncContractCatalog.ProjectionOrder)
        foreach (var child in descriptor.Children)
        foreach (var entry in changes.Where(e => e.Metadata.GetTableName() == child.Table))
        {
            constraints.Add("public." + JournalSqlTemplates.Quote("trg_" + child.Table + "_journal"));
            var owner = entry.Property(child.RootIdColumn);
            if (entry.State != EntityState.Deleted && owner.CurrentValue is string current)
                affected.Add((descriptor.Entity, current));
            if (entry.State != EntityState.Added && owner.OriginalValue is string original)
                affected.Add((descriptor.Entity, original));
        }
        if (constraints.Count == 0) return null;

        var roots = db.ChangeTracker.Entries<SyncableEntity>()
            .Where(e => e.State != EntityState.Deleted && affected.Contains((e.Metadata.ClrType, e.Entity.Id))).ToArray();
        foreach (var entry in roots)
        {
            // A child-only edit must also check the aggregate's optimistic token. Do not replace
            // its original Version with a freshly loaded value (that would hide real conflicts).
            if (entry.State == EntityState.Unchanged)
                entry.Property(e => e.UpdatedAt).IsModified = true;
        }
        return new AggregateSaveScope(db, roots, string.Join(", ", constraints));
    }

    public void Begin()
    {
        _ownsTransaction = _db.Database.CurrentTransaction is null;
        _transaction = _db.Database.CurrentTransaction ?? _db.Database.BeginTransaction();
        if (!_ownsTransaction)
        {
            _transaction.CreateSavepoint(_savepoint);
            _hasSavepoint = true;
        }
        _db.Database.ExecuteSqlRaw("SET CONSTRAINTS " + _constraints + " DEFERRED");
    }

    public async Task BeginAsync(CancellationToken ct)
    {
        _ownsTransaction = _db.Database.CurrentTransaction is null;
        _transaction = _db.Database.CurrentTransaction
            ?? await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!_ownsTransaction)
        {
            await _transaction.CreateSavepointAsync(_savepoint, ct).ConfigureAwait(false);
            _hasSavepoint = true;
        }
        await _db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS " + _constraints + " DEFERRED", ct).ConfigureAwait(false);
    }

    public void Complete()
    {
        _db.Database.ExecuteSqlRaw("SET CONSTRAINTS " + _constraints + " IMMEDIATE");
        foreach (var root in _roots)
        {
            using var command = VersionCommand(root);
            RefreshVersion(root, command.ExecuteScalar());
        }
        if (_ownsTransaction) _transaction!.Commit();
        else _transaction!.ReleaseSavepoint(_savepoint);
        _completed = true;
    }

    public async Task CompleteAsync(CancellationToken ct)
    {
        // Flush only child capture, NOT ALL constraints: assigning feed sequences here would
        // move the feed-state serialization point away from the outer transaction's commit.
        await _db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS " + _constraints + " IMMEDIATE", ct).ConfigureAwait(false);
        foreach (var root in _roots)
        {
            await using var command = VersionCommand(root);
            RefreshVersion(root, await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }
        if (_ownsTransaction) await _transaction!.CommitAsync(ct).ConfigureAwait(false);
        else await _transaction!.ReleaseSavepointAsync(_savepoint, ct).ConfigureAwait(false);
        _completed = true;
    }

    private DbCommand VersionCommand(EntityEntry<SyncableEntity> root)
    {
        // GetDatabaseValues/Reload internally use unnamed IgnoreQueryFilters(), which our
        // tenant-isolation interceptor correctly bans. Read ONLY the token with explicit
        // tenant + primary-key predicates, including soft-deleted roots without disabling RLS.
        var command = _db.Database.GetDbConnection().CreateCommand();
        command.Transaction = _transaction!.GetDbTransaction();
        command.CommandTimeout = _db.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"SELECT \"Version\" FROM public.{JournalSqlTemplates.Quote(root.Metadata.GetTableName()!)} " +
            "WHERE \"Id\" = @id AND \"TenantId\" = @tenant";
        var id = command.CreateParameter(); id.ParameterName = "id"; id.Value = root.Entity.Id;
        var tenant = command.CreateParameter(); tenant.ParameterName = "tenant"; tenant.Value = root.Entity.TenantId;
        command.Parameters.Add(id);
        command.Parameters.Add(tenant);
        return command;
    }

    private static void RefreshVersion(EntityEntry<SyncableEntity> root, object? value)
    {
        if (value is not Guid version) throw new DbUpdateConcurrencyException("Aggregate disappeared while saving its children.");
        // AcceptAllChanges (when requested by the caller) updates OriginalValue only after the
        // entire save succeeds. SaveChanges(false) must preserve original snapshots and states.
        root.Property(e => e.Version).CurrentValue = version.ToString("D");
    }

    public void Dispose()
    {
        try
        {
            if (!_completed && _transaction is not null)
            {
                if (_ownsTransaction) _transaction.Rollback();
                else if (_hasSavepoint)
                {
                    _transaction.RollbackToSavepoint(_savepoint);
                    _transaction.ReleaseSavepoint(_savepoint);
                }
            }
        }
        finally
        {
            if (_ownsTransaction) _transaction?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_completed && _transaction is not null)
            {
                // Cleanup must also work after request cancellation. Rolling back the savepoint
                // restores constraint modes as well as root/child/audit rows and pending events.
                if (_ownsTransaction) await _transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                else if (_hasSavepoint)
                {
                    await _transaction.RollbackToSavepointAsync(_savepoint, CancellationToken.None).ConfigureAwait(false);
                    await _transaction.ReleaseSavepointAsync(_savepoint, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (_ownsTransaction && _transaction is not null) await _transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}
