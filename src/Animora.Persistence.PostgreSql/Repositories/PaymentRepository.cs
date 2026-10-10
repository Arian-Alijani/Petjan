using Animora.Application.Ports;
using Animora.Domain.Sales;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class PaymentRepository(AnimoraServerDbContext db) : TenantRepository<Payment>(db), IPaymentRepository
{
    public Task<Payment?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Payment>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<Payment?>(local) : db.Set<Payment>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Payment> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Payment {id} not found.");

    public Task<Payment?> FindLatestForInvoiceAsync(string invoiceId, CancellationToken ct = default) =>
        db.Set<Payment>().AsTracking().Where(e => e.InvoiceId == invoiceId && e.Amount.Minor > 0)
            .OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct);

    public new void Add(Payment entity) => db.Set<Payment>().Add(entity);
    public void AddRange(IEnumerable<Payment> entities) => db.Set<Payment>().AddRange(entities);
}
