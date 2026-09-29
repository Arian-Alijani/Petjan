using Animora.Application.Ports;
using Animora.Domain.Purchasing;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class PurchasingRepository(AnimoraLocalDbContext db) : IPurchasingRepository
{
    public Task<PurchaseOrder?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<PurchaseOrder>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<PurchaseOrder> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"PurchaseOrder {id} not found.");

    public void Add(PurchaseOrder entity) => db.Set<PurchaseOrder>().Add(entity);
    public void AddRange(IEnumerable<PurchaseOrder> entities) => db.Set<PurchaseOrder>().AddRange(entities);

    public void AddReceipt(GoodsReceipt receipt) => db.Set<GoodsReceipt>().Add(receipt);

    public Task<GoodsReceipt?> FindReceiptAsync(string id, CancellationToken ct = default) =>
        db.Set<GoodsReceipt>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
}
