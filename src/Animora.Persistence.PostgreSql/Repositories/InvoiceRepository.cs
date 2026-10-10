using Animora.Application.Ports;
using Animora.Domain.Sales;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class InvoiceRepository(AnimoraServerDbContext db) : TenantRepository<Invoice>(db), IInvoiceRepository
{
    public Task<Invoice?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<Invoice>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<Invoice?>(local) : Context.Set<Invoice>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Invoice> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Invoice {id} not found.");

    public new void Add(Invoice entity) => Context.Set<Invoice>().Add(entity);
    public void AddRange(IEnumerable<Invoice> entities) => Context.Set<Invoice>().AddRange(entities);
}
