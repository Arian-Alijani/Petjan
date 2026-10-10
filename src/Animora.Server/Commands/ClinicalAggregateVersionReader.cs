using Animora.Domain.Inventory;
using Animora.Domain.Purchasing;
using Animora.Domain.Finance;
using Animora.Domain.Sales;
using Animora.Domain.Cashier;
using Animora.Domain.Boarding;
using Animora.Domain.Clinical;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Commands;

/// <summary>Reads server-owned uuid tokens through the current tenant's filtered context.</summary>
internal sealed class ClinicalAggregateVersionReader(AnimoraServerDbContext db) : IAggregateVersionReader
{
    public Task<Result<string?>> TryReadAsync(string aggregateType, string aggregateId, CancellationToken ct = default) =>
        aggregateType switch
        {
            "Product" => ReadAsync<Product>(aggregateId, ct),
            "ProductCategory" => ReadAsync<ProductCategory>(aggregateId, ct),
            "BoardingServiceDef" => ReadAsync<BoardingServiceDef>(aggregateId, ct),
            "PurchaseOrder" => ReadAsync<PurchaseOrder>(aggregateId, ct),
            "GoodsReceipt" => ReadAsync<GoodsReceipt>(aggregateId, ct),
            "Supplier" => ReadAsync<Supplier>(aggregateId, ct),
            "Cheque" => ReadAsync<Cheque>(aggregateId, ct),
            "Expense" => ReadAsync<Expense>(aggregateId, ct),
            "RecurringExpense" => ReadAsync<RecurringExpense>(aggregateId, ct),
            "Invoice" => ReadAsync<Invoice>(aggregateId, ct),
            "Payment" => ReadAsync<Payment>(aggregateId, ct),
            "Insurer" => ReadAsync<Insurer>(aggregateId, ct),
            "CashSession" => ReadAsync<CashSession>(aggregateId, ct),
            "Appointment" => ReadAsync<Appointment>(aggregateId, ct),
            "Room" => ReadAsync<Room>(aggregateId, ct),
            "BoardingStay" => ReadAsync<BoardingStay>(aggregateId, ct),
            "Owner" => ReadAsync<Owner>(aggregateId, ct),
            "Pet" => ReadAsync<Pet>(aggregateId, ct),
            "ReceptionQueueEntry" => ReadAsync<ReceptionQueueEntry>(aggregateId, ct),
            "Visit" => ReadAsync<Visit>(aggregateId, ct),
            "VaccinationRecord" => ReadAsync<VaccinationRecord>(aggregateId, ct),
            "TreatmentPlan" => ReadAsync<TreatmentPlan>(aggregateId, ct),
            "TreatmentOccurrence" => ReadAsync<TreatmentOccurrence>(aggregateId, ct),
            "VaccineCatalogItem" => ReadAsync<VaccineCatalogItem>(aggregateId, ct),
            _ => Task.FromResult(Result<string?>.Ok(null))
        };

    private async Task<Result<string?>> ReadAsync<T>(string id, CancellationToken ct) where T : SyncableEntity =>
        Result<string?>.Ok(await db.Set<T>().AsNoTracking().Where(e => e.Id == id)
            .Select(e => e.Version).FirstOrDefaultAsync(ct).ConfigureAwait(false));
}
