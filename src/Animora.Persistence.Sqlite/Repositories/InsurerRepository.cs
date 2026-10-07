using Animora.Application.Ports;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class InsurerRepository(AnimoraLocalDbContext db) : IInsurerRepository
{
    public Task<Insurer?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Insurer>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public void Add(Insurer entity) => db.Set<Insurer>().Add(entity);

    public async Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default)
    {
        // The directory is tiny (a handful of rows): compare in memory so Persian text matches exactly and
        // case-insensitively without depending on SQLite's ASCII-only NOCASE collation.
        var target = name.Trim();
        var names = await db.Set<Insurer>().AsNoTracking()
            .Where(i => excludeId == null || i.Id != excludeId)
            .Select(i => i.Name)
            .ToListAsync(ct);
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i].Trim(), target, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
