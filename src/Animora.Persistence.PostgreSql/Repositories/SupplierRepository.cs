using Animora.Application.Ports;
using Animora.Domain.Purchasing;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class SupplierRepository(AnimoraServerDbContext db) : TenantRepository<Supplier>(db), ISupplierRepository
{
    public Task<Supplier?> FindByNameAsync(string name, CancellationToken ct = default) =>
        db.Set<Supplier>().AsTracking().FirstOrDefaultAsync(s => s.Name == name, ct);

    public new void Add(Supplier supplier) => db.Set<Supplier>().Add(supplier);
}
