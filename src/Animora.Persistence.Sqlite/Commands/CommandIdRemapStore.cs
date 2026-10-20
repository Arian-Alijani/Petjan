using Animora.Application.Commands;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Persistence.Sqlite.Commands;

/// <summary>
/// SYN-01: SQLite backing of <see cref="ICommandIdRemapStore"/>. Singleton: every call opens its own short-lived scope + context from the
/// scoped factory, so the singleton sync service can use it without capturing a scoped dependency. The tenant is always an explicit predicate.
/// </summary>
public sealed class CommandIdRemapStore(IServiceScopeFactory scopes) : ICommandIdRemapStore
{
    public void Upsert(string tenantId, string localId, string serverId, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(localId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        using var scope = scopes.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AnimoraLocalDbContext>>().CreateDbContext();
        // Idempotent: a repeat learn keeps the original CreatedAtUtc and only refreshes the server id.
        db.Database.ExecuteSqlRaw(
            "INSERT INTO CommandIdRemaps (TenantId, LocalId, ServerId, CreatedAtUtc) VALUES ({0}, {1}, {2}, {3}) " +
            "ON CONFLICT (TenantId, LocalId) DO UPDATE SET ServerId = excluded.ServerId;",
            tenantId, localId, serverId, createdAtUtc.UtcTicks);
    }

    public IReadOnlyList<KeyValuePair<string, string>> LoadAll(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        using var scope = scopes.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AnimoraLocalDbContext>>().CreateDbContext();
        var rows = db.Set<Entities.CommandIdRemap>().AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .Select(r => new { r.LocalId, r.ServerId })
            .ToList();
        var result = new List<KeyValuePair<string, string>>(rows.Count);
        for (var i = 0; i < rows.Count; i++) result.Add(new(rows[i].LocalId, rows[i].ServerId));
        return result;
    }
}
