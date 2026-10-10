using Animora.Application.Ports;
using Animora.Domain.Purchasing;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class PurchasingRepository(AnimoraServerDbContext db) : TenantRepository<PurchaseOrder>(db), IPurchasingRepository
{
    public Task<PurchaseOrder?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<PurchaseOrder>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<PurchaseOrder?>(local) : Context.Set<PurchaseOrder>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<PurchaseOrder> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"PurchaseOrder {id} not found.");

    public new void Add(PurchaseOrder entity) => Context.Set<PurchaseOrder>().Add(entity);
    public void AddRange(IEnumerable<PurchaseOrder> entities) => Context.Set<PurchaseOrder>().AddRange(entities);

    public void AddReceipt(GoodsReceipt receipt) => Context.Set<GoodsReceipt>().Add(receipt);

    public Task<GoodsReceipt?> FindReceiptAsync(string id, CancellationToken ct = default) =>
        Context.Set<GoodsReceipt>().Local.FirstOrDefault(e => e.Id == id) is { } local
            ? Task.FromResult<GoodsReceipt?>(local) : Context.Set<GoodsReceipt>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
}
