using Animora.Application.Ports;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class InvoiceRepository(AnimoraLocalDbContext db) : IInvoiceRepository
{
    public Task<Invoice?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Invoice>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Invoice> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Invoice {id} not found.");

    public void Add(Invoice entity) => db.Set<Invoice>().Add(entity);
    public void AddRange(IEnumerable<Invoice> entities) => db.Set<Invoice>().AddRange(entities);
}
