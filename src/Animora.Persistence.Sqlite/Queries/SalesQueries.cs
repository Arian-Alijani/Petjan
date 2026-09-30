using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Boarding;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Bounded sales projections; global tenant filters apply to every domain set.</summary>
public sealed class SalesQueries(AnimoraLocalDbContext db, IOptions<LocalStoreOptions> options) : ISalesQueries
{
    public async Task<IReadOnlyList<ReadyForBillingReadModel>> GetReadyForBillingAsync(CancellationToken ct)
    {
        var visits = from h in db.Set<VisitBillingHandoff>().AsNoTracking()
                     where h.Status == HandoffStatus.Ready
                     join o in db.Set<Owner>().AsNoTracking() on h.OwnerId equals o.Id
                     join p in db.Set<Pet>().AsNoTracking() on h.PetId equals p.Id
                     select new ReadyForBillingReadModel(h.Id, "Visit", h.OwnerId, o.FullName,
                         h.PetId, p.Name, h.DoctorId, h.CompletedUtc, h.LinesJson);
        var stays = from h in db.Set<BoardingBillingHandoff>().AsNoTracking()
                    where h.Status == HandoffStatus.Ready
                    join o in db.Set<Owner>().AsNoTracking() on h.OwnerId equals o.Id
                    join p in db.Set<Pet>().AsNoTracking() on h.PetId equals p.Id
                    select new ReadyForBillingReadModel(h.Id, "Boarding", h.OwnerId, o.FullName,
                        h.PetId, p.Name, h.DoctorId, h.CompletedUtc, h.LinesJson);
        return await visits.Concat(stays).OrderByDescending(h => h.CompletedUtc)
            .ThenBy(h => h.Id).Take(100).ToListAsync(ct);
    }

    private IQueryable<InvoiceSummaryReadModel> ProjectInvoices(IQueryable<Invoice> invoices) =>
        from i in invoices
        join owner in db.Set<Owner>().AsNoTracking() on i.OwnerId equals owner.Id into owners
        from owner in owners.DefaultIfEmpty()
        join pet in db.Set<Pet>().AsNoTracking() on i.PetId equals pet.Id into pets
        from pet in pets.DefaultIfEmpty()
        select new InvoiceSummaryReadModel(i.Id, i.InvoiceNumber, i.Kind, i.State,
            i.OwnerId, owner == null ? i.GuestName ?? "" : owner.FullName,
            pet == null ? "" : pet.Name, owner == null ? "" : owner.PhonePrimary,
            i.IssuedUtc, i.GrandTotal.Minor, i.DueTotal.Minor, i.Settlement);

    public Task<PagedResult<InvoiceSummaryReadModel>> GetInvoicesAsync(
        string? search, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Set<Invoice>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var ownerIds = db.Set<Owner>().Where(o => EF.Functions.Like(o.SearchKey, "%" + PersianText.Normalize(term) + "%"))
                .Select(o => o.Id);
            q = q.Where(i => EF.Functions.Like(i.InvoiceNumber, "%" + term + "%") ||
                (i.OwnerId != null && ownerIds.Contains(i.OwnerId)) ||
                (i.GuestName != null && EF.Functions.Like(i.GuestName, "%" + term + "%")));
        }
        return ProjectInvoices(q.OrderByDescending(i => i.IssuedUtc).ThenByDescending(i => i.Id))
            .ToPagedAsync(page, pageSize, options.Value, ct);
    }

    public async Task<InvoiceDetailReadModel?> GetInvoiceAsync(string invoiceId, CancellationToken ct)
    {
        var invoice = db.Set<Invoice>().AsNoTracking().Where(i => i.Id == invoiceId);
        var summary = await ProjectInvoices(invoice).FirstOrDefaultAsync(ct);
        if (summary is null) return null;

        var scalar = await invoice.Select(i => new { i.GuestName, i.InsurerId, i.InsurancePercent,
            Subtotal = i.Subtotal.Minor, Discount = i.DiscountTotal.Minor, Tax = i.TaxTotal.Minor,
            Coverage = i.InsuranceCoverage.Minor, Paid = i.PaidTotal.Minor, i.DiscountKind,
            i.DiscountValue, i.OperationId, i.SettlementErrorCode }).SingleAsync(ct);
        var lines = await invoice.SelectMany(i => i.Lines).Select(l => new InvoiceLineReadModel(
            l.LineId, l.Kind, l.ProductId, l.VariantId, l.CatalogItemId,
            l.Title, l.Quantity.Value, l.UnitPrice.Minor, l.DiscountPercent, l.TaxPercent)).ToListAsync(ct);
        var adjustments = await invoice.SelectMany(i => i.Adjustments)
            .Select(a => new InvoiceAdjustmentReadModel(a.AdjustmentId, a.Kind, a.Amount.Minor,
                a.Reason, a.CreatedUtc)).ToListAsync(ct);
        var payments = await db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == invoiceId)
            .OrderBy(p => p.ReceivedUtc).ThenBy(p => p.Id)
            .Select(p => new InvoicePaymentReadModel(p.Id, p.Method, p.Amount.Minor,
                p.ReceivedUtc, p.Note)).ToListAsync(ct);
        // The post journal entry is the authoritative source for rejected/stuck settlement details.
        var journal = await db.Set<PendingCommand>().AsNoTracking()
            .Where(c => c.AggregateId == invoiceId || (scalar.OperationId != null && c.OperationId == scalar.OperationId))
            .OrderByDescending(c => c.LocalSequence)
            .Select(c => new { c.Status, c.LastErrorCode, c.ResponsibleRole })
            .FirstOrDefaultAsync(ct);
        var settlement = journal is null ? summary.SettlementStatus : journal.Status switch
        {
            Animora.Domain.Primitives.SettlementState.Settled => SettlementStatus.Settled,
            Animora.Domain.Primitives.SettlementState.Rejected => SettlementStatus.Rejected,
            Animora.Domain.Primitives.SettlementState.Conflict => SettlementStatus.Conflict,
            Animora.Domain.Primitives.SettlementState.Reversed => SettlementStatus.Reversed,
            Animora.Domain.Primitives.SettlementState.NeedsAttention => SettlementStatus.NeedsAttention,
            Animora.Domain.Primitives.SettlementState.LocallyPosted => SettlementStatus.LocallyPosted,
            _ => summary.SettlementStatus == SettlementStatus.Draft ? SettlementStatus.Draft : SettlementStatus.PendingSettlement,
        };
        return new InvoiceDetailReadModel(summary, scalar.GuestName, scalar.InsurerId,
            scalar.InsurancePercent, scalar.Subtotal, scalar.Discount, scalar.Tax, scalar.Coverage,
            scalar.Paid, scalar.DiscountKind, scalar.DiscountValue, lines, payments, adjustments,
            settlement, journal?.LastErrorCode ?? scalar.SettlementErrorCode, journal?.ResponsibleRole);
    }

    public async Task<SalesCatalogReadModel> GetCatalogAsync(CancellationToken ct)
    {
        var products = await (from p in db.Set<Product>().AsNoTracking()
            where p.IsActive
            join c in db.Set<ProductCategory>().AsNoTracking().Where(c => c.IsActive) on p.CategoryId equals c.Id
            from v in p.Variants
            where v.IsActive
            orderby p.Name, v.VariantId
            select new SalesCatalogItemReadModel(v.VariantId, p.Name, p.Kind == ProductKind.Service ? "Service" : "Item",
                c.DisplayName, v.SalePrice.Minor)).ToListAsync(ct);
        var boarding = await db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.IsActive)
            .OrderBy(s => s.Title).Take(200)
            .Select(s => new SalesCatalogItemReadModel(s.Id, s.Title, "Service", "Boarding", s.UnitPrice.Minor))
            .ToListAsync(ct);
        var recentIds = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.State != InvoiceState.Draft)
            .OrderByDescending(i => i.IssuedUtc).ThenByDescending(i => i.Id).Take(50)
            .SelectMany(i => i.Lines).Where(l => l.CatalogItemId != null)
            .Select(l => l.CatalogItemId!).Distinct().Take(10).ToListAsync(ct);
        var items = products.Concat(boarding).ToArray();
        return new SalesCatalogReadModel(items, items.Select(i => i.Category).Distinct().ToArray(), recentIds);
    }

    public async Task<IReadOnlyList<InsurerReadModel>> GetInsurersAsync(CancellationToken ct) =>
        await db.Set<Insurer>().AsNoTracking().OrderBy(i => i.Code)
            .Select(i => new InsurerReadModel(i.Id, i.Name, i.CoveragePercent, i.CeilingMinor)).ToListAsync(ct);

    public async Task<SaleKpiReadModel> GetKpisAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var row = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.PostedUtc >= fromUtc && i.PostedUtc < toUtc && i.State != InvoiceState.Void)
            .GroupBy(i => 1).Select(g => new { Count = g.Count(), Revenue = g.Sum(i => i.GrandTotal.Minor) })
            .FirstOrDefaultAsync(ct);
        return row is null ? new SaleKpiReadModel(0, 0, 0) :
            new SaleKpiReadModel(row.Revenue, row.Count, row.Revenue / row.Count);
    }

    public async Task<CashierHistoryPageReadModel> GetHistoryAsync(DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc, string? search, string? kind, string sort, int page, int pageSize,
        CancellationToken ct)
    {
        var payments = from p in db.Set<Payment>().AsNoTracking()
                       join i in db.Set<Invoice>().AsNoTracking() on p.InvoiceId equals i.Id
                       join owner in db.Set<Owner>().AsNoTracking() on i.OwnerId equals owner.Id into owners
                       from owner in owners.DefaultIfEmpty()
                       select new CashierHistoryReadModel(p.Id, i.Id, i.InvoiceNumber, p.ReceivedUtc,
                           p.Amount.Minor < 0 ? "Refund" : "Payment", p.Amount.Minor,
                           owner == null ? i.GuestName ?? "" : owner.FullName);
        var invoices = from i in db.Set<Invoice>().AsNoTracking()
                       join owner in db.Set<Owner>().AsNoTracking() on i.OwnerId equals owner.Id into owners
                       from owner in owners.DefaultIfEmpty()
                       where i.State != InvoiceState.Draft
                       select new CashierHistoryReadModel(i.Id, i.Id, i.InvoiceNumber,
                           i.PostedUtc ?? i.IssuedUtc, i.State == InvoiceState.Void ? "Void" : "Accepted",
                           0L, owner == null ? i.GuestName ?? "" : owner.FullName);
        var rows = payments.Concat(invoices);
        if (fromUtc is { } from) rows = rows.Where(r => r.OccurredUtc >= from);
        if (toUtc is { } to) rows = rows.Where(r => r.OccurredUtc < to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(r => EF.Functions.Like(r.Number, "%" + term + "%") ||
                EF.Functions.Like(r.OwnerName, "%" + term + "%"));
        }
        if (!string.IsNullOrWhiteSpace(kind)) rows = rows.Where(r => r.Kind == kind);
        var totals = await rows.GroupBy(r => 1).Select(g => new
        {
            Inflow = g.Sum(r => r.AmountMinor > 0 ? r.AmountMinor : 0),
            Outflow = g.Sum(r => r.AmountMinor < 0 ? -r.AmountMinor : 0),
        }).FirstOrDefaultAsync(ct);
        var ordered = sort switch
        {
            "Oldest" => rows.OrderBy(r => r.OccurredUtc).ThenBy(r => r.Id),
            "AmountDesc" => rows.OrderByDescending(r => r.AmountMinor).ThenByDescending(r => r.Id),
            "AmountAsc" => rows.OrderBy(r => r.AmountMinor).ThenBy(r => r.Id),
            "Owner" => rows.OrderBy(r => r.OwnerName).ThenByDescending(r => r.OccurredUtc).ThenBy(r => r.Id),
            _ => rows.OrderByDescending(r => r.OccurredUtc).ThenByDescending(r => r.Id),
        };
        return new CashierHistoryPageReadModel(
            await ordered.ToPagedAsync(page, pageSize, options.Value, ct),
            totals?.Inflow ?? 0, totals?.Outflow ?? 0);
    }

    public async Task<IReadOnlyList<OwnerLookupReadModel>> FindOwnersAsync(string? search, CancellationToken ct)
    {
        var q = db.Set<Owner>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PersianText.Normalize(search);
            q = q.Where(o => EF.Functions.Like(o.SearchKey, "%" + term + "%"));
        }
        return await q.OrderBy(o => o.FullName).ThenBy(o => o.Id).Take(100)
            .Select(o => new OwnerLookupReadModel(o.Id, o.FullName, o.PhonePrimary)).ToListAsync(ct);
    }
}
