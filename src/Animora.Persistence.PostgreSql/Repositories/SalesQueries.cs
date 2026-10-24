using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Boarding;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Clinical;
using Animora.Persistence.PostgreSql.Boarding;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Contracts.Wire.Commands;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

internal sealed class ReadyBillingProjection
{
    public string Id { get; init; } = "";
    public string Source { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string OwnerName { get; init; } = "";
    public string PetId { get; init; } = "";
    public string PetName { get; init; } = "";
    public string DoctorId { get; init; } = "";
    public DateTimeOffset CompletedUtc { get; init; }
    public string LinesJson { get; init; } = "";

    // Scalar member initializers keep both UNION legs translatable before the final record projection.
    // tenantId (optional) lets the handoff leg use its (TenantId, Status, CompletedUtc) index instead of a scan.
    internal static IQueryable<ReadyBillingProjection> Query(AnimoraServerDbContext db, string? tenantId = null)
    {
        // LEFT joins (DefaultIfEmpty) mirror ProjectInvoices: a Ready handoff must stay in the cashier
        // queue even when its owner/pet row is missing (walk-in or later-removed record). An INNER join
        // silently dropped such handoffs, so a completed visit never reached the صندوق.
        var visits = from h in db.Set<VisitBillingHandoff>().AsNoTracking()
            where h.Status == 0 && (tenantId == null || h.TenantId == tenantId)
            join o in db.Set<Owner>().AsNoTracking() on h.OwnerId equals o.Id into owners
            from o in owners.DefaultIfEmpty()
            join p in db.Set<Pet>().AsNoTracking() on h.PetId equals p.Id into petsJoin
            from p in petsJoin.DefaultIfEmpty()
            select new ReadyBillingProjection { Id = h.Id, Source = "Visit", OwnerId = h.OwnerId,
                OwnerName = o == null ? "" : o.FullName, PetId = h.PetId, PetName = p == null ? "" : p.Name,
                DoctorId = h.DoctorId, CompletedUtc = h.CompletedUtc, LinesJson = h.LinesJson };
        var stays = from h in db.Set<BoardingBillingHandoff>().AsNoTracking()
            where h.Status == 0 && (tenantId == null || h.TenantId == tenantId)
            join o in db.Set<Owner>().AsNoTracking() on h.OwnerId equals o.Id into owners
            from o in owners.DefaultIfEmpty()
            join p in db.Set<Pet>().AsNoTracking() on h.PetId equals p.Id into petsJoin
            from p in petsJoin.DefaultIfEmpty()
            select new ReadyBillingProjection { Id = h.Id, Source = "Boarding", OwnerId = h.OwnerId,
                OwnerName = o == null ? "" : o.FullName, PetId = h.PetId, PetName = p == null ? "" : p.Name,
                DoctorId = h.DoctorId, CompletedUtc = h.CompletedUtc, LinesJson = h.LinesJson };
        return visits.Concat(stays);
    }
}

/// <summary>Bounded sales projections; global tenant filters apply to every domain set.</summary>
public sealed class SalesQueries(AnimoraServerDbContext db) : ISalesQueries
{
    public async Task<IReadOnlyList<ReadyForBillingReadModel>> GetReadyForBillingAsync(CancellationToken ct)
    {
        return await ReadyBillingProjection.Query(db).OrderByDescending(h => h.CompletedUtc)
            .ThenBy(h => h.Id).Take(100)
            .Select(h => new ReadyForBillingReadModel(h.Id, h.Source, h.OwnerId, h.OwnerName,
                h.PetId, h.PetName, h.DoctorId, h.CompletedUtc, h.LinesJson)).ToListAsync(ct);
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
            .ToPagedAsync(page, pageSize, ct);
    }

    public async Task<IReadOnlyList<InvoiceSummaryReadModel>> GetOwnerInvoicesAsync(
        string ownerId, int take, CancellationToken ct) =>
        await ProjectInvoices(db.Set<Invoice>().AsNoTracking().Where(i => i.OwnerId == ownerId)
                .OrderByDescending(i => i.IssuedUtc).ThenByDescending(i => i.Id).Take(PageRules.ClampPageSize(take)))
            .ToListAsync(ct);

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
        var journal = await db.CommandIntake.AsNoTracking()
            .Where(c => c.ServerEntityId == invoiceId || (scalar.OperationId != null && c.OperationId == scalar.OperationId))
            .OrderByDescending(c => c.ReceivedUtc).ThenByDescending(c => c.OperationId)
            .Select(c => new { c.Status, c.Outcome, c.ErrorCode, c.ResponsibleRole }).FirstOrDefaultAsync(ct);
        var settlement = journal is null ? summary.SettlementStatus : journal.Status != IntakeStatus.Settled
            ? SettlementStatus.PendingSettlement : journal.Outcome == (short)SettlementOutcome.Accepted
            ? SettlementStatus.Settled : journal.Outcome == (short)SettlementOutcome.Conflict
            ? SettlementStatus.Conflict : SettlementStatus.Rejected;
        return new InvoiceDetailReadModel(summary, scalar.GuestName, scalar.InsurerId,
            scalar.InsurancePercent, scalar.Subtotal, scalar.Discount, scalar.Tax, scalar.Coverage,
            scalar.Paid, scalar.DiscountKind, scalar.DiscountValue, lines, payments, adjustments,
            settlement, journal?.ErrorCode ?? scalar.SettlementErrorCode, journal?.ResponsibleRole);
    }

    public async Task<SalesCatalogItemReadModel?> FindCatalogItemByBarcodeAsync(string barcode, CancellationToken ct) =>
        await (from p in db.Set<Product>().AsNoTracking()
            where p.IsActive
            join c in db.Set<ProductCategory>().AsNoTracking() on p.CategoryId equals c.Id into categories
            from c in categories.DefaultIfEmpty()
            from v in p.Variants
            where v.IsActive && v.Barcode == barcode
            orderby v.VariantId
            select new SalesCatalogItemReadModel(v.VariantId, p.Name, p.Kind == ProductKind.Service ? "Service" : "Item",
                c == null ? "سایر" : c.DisplayName, v.SalePrice.Minor))
            .FirstOrDefaultAsync(ct);

    public async Task<SalesCatalogReadModel> GetCatalogAsync(CancellationToken ct)
    {
        // Per-variant rows for products that have at least one active variant (the variant id is what an
        // invoice line sells)...
        var variantRows = await (from p in db.Set<Product>().AsNoTracking()
            where p.IsActive
            join c in db.Set<ProductCategory>().AsNoTracking() on p.CategoryId equals c.Id into categories
            from c in categories.DefaultIfEmpty()
            from v in p.Variants
            where v.IsActive
            // Services first (like the visit catalog) so a large goods catalogue can never push a freshly added
            // service out of the bounded window; every active product/variant is otherwise listed by name.
            orderby p.Kind == ProductKind.Service descending, p.Name, v.VariantId
            select new SalesCatalogItemReadModel(v.VariantId, p.Name, p.Kind == ProductKind.Service ? "Service" : "Item",
                c == null ? "سایر" : c.DisplayName, v.SalePrice.Minor)).Take(200).ToListAsync(ct);
        // ...plus ONE product-level fallback row for every active product without an active variant (typically
        // services created from the rate editor, or items whose variants were all deactivated). Without it
        // those products silently vanished from the drawer. The row is keyed by the product id, which
        // ISalesDraftData.FindCatalogEntryAsync resolves to the product's default sale price.
        var variantless = await (from p in db.Set<Product>().AsNoTracking()
            where p.IsActive && !p.Variants.Any(v => v.IsActive)
            join c in db.Set<ProductCategory>().AsNoTracking() on p.CategoryId equals c.Id into categories
            from c in categories.DefaultIfEmpty()
            orderby p.Kind == ProductKind.Service descending, p.Name, p.Id
            select new SalesCatalogItemReadModel(p.Id, p.Name, p.Kind == ProductKind.Service ? "Service" : "Item",
                c == null ? "سایر" : c.DisplayName, p.DefaultSalePrice.Minor)).Take(200).ToListAsync(ct);
        var products = variantRows.Concat(variantless).ToList();
        var boarding = await db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.IsActive)
            .OrderBy(s => s.Title).Take(200)
            .Select(s => new SalesCatalogItemReadModel(s.Id, s.Title, "Service", "Boarding", s.UnitPrice.Minor))
            .ToListAsync(ct);
        var recentIds = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.State != InvoiceState.Draft)
            .OrderByDescending(i => i.IssuedUtc).ThenByDescending(i => i.Id).Take(50)
            .SelectMany(i => i.Lines).Where(l => l.CatalogItemId != null)
            .Select(l => l.CatalogItemId!).Distinct().Take(10).ToListAsync(ct);
        var items = products.Concat(boarding).Take(PageRules.MaxPageSize).ToArray();
        return new SalesCatalogReadModel(items, items.Select(i => i.Category).Distinct().ToArray(), recentIds);
    }

    public async Task<IReadOnlyList<InsurerReadModel>> GetInsurersAsync(CancellationToken ct) =>
        await db.Set<Insurer>().AsNoTracking().OrderBy(i => i.Code).Take(PageRules.MaxPageSize)
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
        // Both legs project into the SAME flat, fully server-translatable anonymous shape (plain columns
        // + CASE/COALESCE scalars) so EF Core can emit the set operation as a single UNION ALL. Projecting
        // a record constructor — or mismatched client-shaped expressions — in a Concat leg makes EF throw
        // "Unable to translate set operation after client projection has been applied" (EF Core 10), which
        // previously surfaced as an empty «تاریخچه صندوق» tab even though the invoice was posted.
        var payments = from p in db.Set<Payment>().AsNoTracking()
                       join i in db.Set<Invoice>().AsNoTracking() on p.InvoiceId equals i.Id
                       join owner in db.Set<Owner>().AsNoTracking() on i.OwnerId equals owner.Id into owners
                       from owner in owners.DefaultIfEmpty()
                       select new
                       {
                           Id = p.Id,
                           InvoiceId = i.Id,
                           Number = i.InvoiceNumber,
                           OccurredUtc = p.ReceivedUtc,
                           Kind = p.Amount.Minor < 0 ? "Refund" : "Payment",
                           AmountMinor = p.Amount.Minor,
                           OwnerName = owner == null ? i.GuestName ?? "" : owner.FullName,
                           State = i.State,
                       };
        var invoices = from i in db.Set<Invoice>().AsNoTracking()
                       join owner in db.Set<Owner>().AsNoTracking() on i.OwnerId equals owner.Id into owners
                       from owner in owners.DefaultIfEmpty()
                       where i.PostedUtc != null
                       select new
                       {
                           Id = i.Id,
                           InvoiceId = i.Id,
                           Number = i.InvoiceNumber,
                           OccurredUtc = i.PostedUtc ?? i.IssuedUtc,
                           Kind = "Accepted",
                           AmountMinor = 0L,
                           OwnerName = owner == null ? i.GuestName ?? "" : owner.FullName,
                           State = InvoiceState.Posted,
                       };
        var voids = from v in FinanceVoidProjection.Query(db)
                    where v.OccurredUtc != null
                    join i in db.Set<Invoice>().AsNoTracking() on v.InvoiceId equals i.Id
                    join owner in db.Set<Owner>() on i.OwnerId equals owner.Id into owners
                    from owner in owners.DefaultIfEmpty()
                    select new { Id = "void:" + i.Id, InvoiceId = i.Id, Number = i.InvoiceNumber,
                        OccurredUtc = v.OccurredUtc!.Value, Kind = "Void", AmountMinor = 0L,
                        OwnerName = owner == null ? i.GuestName ?? "" : owner.FullName, State = InvoiceState.Void };
        var rows = payments.Concat(invoices).Concat(voids);
        if (fromUtc is { } from) rows = rows.Where(r => r.OccurredUtc >= from);
        if (toUtc is { } to) rows = rows.Where(r => r.OccurredUtc < to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(r => EF.Functions.Like(r.Number, "%" + term + "%") ||
                EF.Functions.Like(r.OwnerName, "%" + term + "%"));
        }
        if (!string.IsNullOrWhiteSpace(kind))
        {
            // `kind` is one projection kind or a comma-separated set (multi-select chips).
            var kinds = kind.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            rows = kinds.Length == 1 ? rows.Where(r => r.Kind == kinds[0]) : rows.Where(r => kinds.Contains(r.Kind));
        }
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
        // The read model is constructed only after the set operation, ordering and paging window are
        // applied — i.e. the final (translatable) projection sits on top of the UNION, never inside a leg.
        var projected = ordered.Select(r => new CashierHistoryReadModel(
            r.Id, r.InvoiceId, r.Number, r.OccurredUtc, r.Kind, r.AmountMinor, r.OwnerName, r.State));
        return new CashierHistoryPageReadModel(
            await projected.ToPagedAsync(page, pageSize, ct),
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
