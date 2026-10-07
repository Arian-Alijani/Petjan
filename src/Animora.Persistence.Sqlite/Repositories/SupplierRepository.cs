using Animora.Application.Ports;
using Animora.Domain.Purchasing;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class SupplierRepository(AnimoraLocalDbContext db) : ISupplierRepository
{
    public Task<Supplier?> FindByNameAsync(string name, CancellationToken ct = default) =>
        db.Set<Supplier>().AsTracking().FirstOrDefaultAsync(s => s.Name == name, ct);

    public void Add(Supplier supplier) => db.Set<Supplier>().Add(supplier);
}
