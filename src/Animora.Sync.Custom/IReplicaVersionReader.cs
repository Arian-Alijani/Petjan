using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Replica;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Sync.Custom;

/// <summary>
/// Reads the server version the local read replica last observed for an aggregate, so a command can carry
/// <c>ExpectedServerVersion</c> (wire v2 optimistic-concurrency precondition). Absent row ⇒ <see langword="null"/> ⇒ no precondition.
/// </summary>
public interface IReplicaVersionReader
{
    /// <summary>The replica's server version token for (<paramref name="aggregateType"/>, <paramref name="aggregateId"/>), or <see langword="null"/>.</summary>
    Task<string?> ReadServerVersionAsync(string aggregateType, string aggregateId, CancellationToken ct = default);
}

/// <summary>Default <see cref="IReplicaVersionReader"/> over <c>repl_base</c> in the active generation named by <c>repl_checkpoint</c>.</summary>
public sealed class ReplicaVersionReader(IServiceScopeFactory scopes) : IReplicaVersionReader
{
    /// <inheritdoc />
    public async Task<string?> ReadServerVersionAsync(string aggregateType, string aggregateId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aggregateType) || string.IsNullOrWhiteSpace(aggregateId)) return null;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ILocalTenantContext>().TenantId;

        var generations = db.Set<ReplicaCheckpoint>().AsNoTracking()
            .Where(c => c.TenantId == tenant)
            .Select(c => c.Generation);

        return await db.Set<ReplicaBase>().AsNoTracking()
            .Where(b => b.TenantId == tenant && b.Entity == aggregateType && b.EntityId == aggregateId && !b.Deleted
                        && generations.Contains(b.Generation))
            .Select(b => b.Version)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }
}
