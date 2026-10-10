using Animora.Application.Ports;
using Animora.Domain.Purchasing;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class SupplierRepository(AnimoraServerDbContext db) : TenantRepository<Supplier>(db), ISupplierRepository
{
    public Task<Supplier?> FindByNameAsync(string name, CancellationToken ct = default) =>
        Context.Set<Supplier>().AsTracking().FirstOrDefaultAsync(s => s.Name == name, ct);

    public new void Add(Supplier supplier) => Context.Set<Supplier>().Add(supplier);
}
