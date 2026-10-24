using Animora.Application.Abstractions;
using Animora.Application.Activity;
using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Boarding;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SettlementState = Animora.Domain.Primitives.SettlementState;

namespace Animora.Persistence.Sqlite.Queries;

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
    internal static IQueryable<ReadyBillingProjection> Query(AnimoraLocalDbContext db, string? tenantId = null)
    {
        // LEFT joins (DefaultIfEmpty) mirror ProjectInvoices: a Ready handoff must stay in the cashier
        // queue even when its owner/pet row is missing (walk-in or later-removed record). An INNER join
        // silently dropped such handoffs, so a completed visit never reached the صندوق.
        var visits = from h in db.Set<VisitBillingHandoff>().AsNoTracking()
            where h.Status == HandoffStatus.Ready && (tenantId == null || h.TenantId == tenantId)
            join o in db.Set<Owner>().AsNoTracking() on h.OwnerId equals o.Id into owners
            from o in owners.DefaultIfEmpty()
            join p in db.Set<Pet>().AsNoTracking() on h.PetId equals p.Id into petsJoin
            from p in petsJoin.DefaultIfEmpty()
            select new ReadyBillingProjection { Id = h.Id, Source = "Visit", OwnerId = h.OwnerId,
                OwnerName = o == null ? "" : o.FullName, PetId = h.PetId, PetName = p == null ? "" : p.Name,
                DoctorId = h.DoctorId, CompletedUtc = h.CompletedUtc, LinesJson = h.LinesJson };
        var stays = from h in db.Set<BoardingBillingHandoff>().AsNoTracking()
            where h.Status == HandoffStatus.Ready && (tenantId == null || h.TenantId == tenantId)
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

// Scalar projections keep UNION ALL below the final read-model constructors (EF's set-operation rule).
// The cache is display-only: no authoritative row is ever inserted into a command-owned table.
internal sealed class FinanceInvoiceProjection
{
    public string Id { get; init; } = "";
    public string Number { get; init; } = "";
    public InvoiceKind Kind { get; init; }
    public InvoiceState State { get; init; }
    public string? OwnerId { get; init; }
    public string OwnerName { get; init; } = "";
    public string PetName { get; init; } = "";
    public string Phone { get; init; } = "";
    public DateTimeOffset IssuedUtc { get; init; }
    public DateTimeOffset? PostedUtc { get; init; }
    public long TotalMinor { get; init; }
    public long PaidMinor { get; init; }
    public long DueMinor { get; init; }
    public SettlementStatus Settlement { get; init; }
    public bool ServerOnly { get; init; }
    public bool FromPeer { get; init; }
    public long SubtotalMinor { get; init; }
    public long DiscountMinor { get; init; }
    public DateTimeOffset? SettledUtc { get; init; }
    public string? DetailJson { get; init; }
    public string? CreatedByUserId { get; init; }

    internal static IQueryable<FinanceInvoiceProjection> Query(AnimoraLocalDbContext db)
    {
        // A refusal/provisional mutation must stay visible even when the cache has an older accepted copy.
        var protectedIds = db.Set<Invoice>().Where(i => i.Settlement != SettlementStatus.Settled
            || db.Set<PendingCommand>().Any(c => c.AggregateId == i.Id && c.AggregateType == "Invoice"
                && c.Status != SettlementState.Settled && c.Status != SettlementState.Reversed)).Select(i => i.Id);
        var local = from i in db.Set<Invoice>().AsNoTracking()
            where protectedIds.Contains(i.Id) || !db.Set<PeerInvoice>().Any(p => p.TenantId == db.CurrentTenantId && p.Id == i.Id)
            join o in db.Set<Owner>() on i.OwnerId equals o.Id into owners
            from o in owners.DefaultIfEmpty()
            join p in db.Set<Pet>() on i.PetId equals p.Id into pets
            from p in pets.DefaultIfEmpty()
            select new FinanceInvoiceProjection { Id = i.Id, Number = i.InvoiceNumber, Kind = i.Kind,
                State = i.State, OwnerId = i.OwnerId, OwnerName = o == null ? i.GuestName ?? "" : o.FullName,
                PetName = p == null ? "" : p.Name, Phone = o == null ? "" : o.PhonePrimary,
                IssuedUtc = i.IssuedUtc, PostedUtc = i.PostedUtc, TotalMinor = i.GrandTotal.Minor,
                PaidMinor = i.PaidTotal.Minor, DueMinor = i.DueTotal.Minor, Settlement = i.Settlement, ServerOnly = false,
                FromPeer = false, SubtotalMinor = i.Subtotal.Minor, DiscountMinor = i.DiscountTotal.Minor,
                SettledUtc = i.SettledUtc, DetailJson = null, CreatedByUserId = i.CreatedByUserId };
        var peer = from i in db.Set<PeerInvoice>().AsNoTracking()
            where i.TenantId == db.CurrentTenantId && !protectedIds.Contains(i.Id)
            join o in db.Set<Owner>() on i.OwnerId equals o.Id into owners
            from o in owners.DefaultIfEmpty()
            select new FinanceInvoiceProjection { Id = i.Id, Number = i.Number, Kind = i.Kind,
                State = i.State, OwnerId = i.OwnerId, OwnerName = i.PartyName, PetName = i.PetName,
                Phone = o == null ? "" : o.PhonePrimary, IssuedUtc = i.IssuedUtc, PostedUtc = i.PostedUtc,
                TotalMinor = i.GrandTotalMinor, PaidMinor = i.PaidTotalMinor, DueMinor = i.DueTotalMinor,
                Settlement = SettlementStatus.Settled, ServerOnly = !db.Set<Invoice>().Any(l => l.Id == i.Id),
                FromPeer = true, SubtotalMinor = i.GrandTotalMinor, DiscountMinor = 0, DetailJson = i.DetailJson,
                CreatedByUserId = null,
                SettledUtc = i.State == InvoiceState.Paid
                    ? db.Set<PeerPayment>().Where(p => p.TenantId == db.CurrentTenantId && p.InvoiceId == i.Id
                        && p.AmountMinor > 0 && p.Method != PaymentMethod.Credit)
                        .OrderByDescending(p => p.ReceivedUtc).Select(p => (DateTimeOffset?)p.ReceivedUtc).FirstOrDefault()
                    : null };
        return local.Concat(peer);
    }

    internal static IQueryable<InvoiceSummaryReadModel> Summaries(IQueryable<FinanceInvoiceProjection> q) =>
        q.Select(i => new InvoiceSummaryReadModel(i.Id, i.Number, i.Kind, i.State, i.OwnerId, i.OwnerName,
            i.PetName, i.Phone, i.IssuedUtc, i.TotalMinor, i.DueMinor, i.Settlement));
}

internal sealed class FinancePaymentProjection
{
    public string Id { get; init; } = "";
    public string InvoiceId { get; init; } = "";
    public PaymentMethod Method { get; init; }
    public long AmountMinor { get; init; }
    public DateTimeOffset ReceivedUtc { get; init; }
    public string? Note { get; init; }
    public string? ReceivedByUserId { get; init; }

    internal static IQueryable<FinancePaymentProjection> Query(AnimoraLocalDbContext db) =>
        db.Set<Payment>().AsNoTracking().Where(p => !db.Set<PeerPayment>().Any(s => s.TenantId == db.CurrentTenantId && s.Id == p.Id))
            .Select(p => new FinancePaymentProjection { Id = p.Id, InvoiceId = p.InvoiceId, Method = p.Method,
                AmountMinor = p.Amount.Minor, ReceivedUtc = p.ReceivedUtc, Note = p.Note,
                ReceivedByUserId = p.ReceivedByUserId })
        .Concat(db.Set<PeerPayment>().AsNoTracking().Where(p => p.TenantId == db.CurrentTenantId).Select(p => new FinancePaymentProjection {
            Id = p.Id, InvoiceId = p.InvoiceId, Method = p.Method, AmountMinor = p.AmountMinor,
            ReceivedUtc = p.ReceivedUtc, Note = null, ReceivedByUserId = null }));
}

/// <summary>Bounded sales projections; global tenant filters apply to every domain set.</summary>
public sealed class SalesQueries(AnimoraLocalDbContext db, IOptions<LocalStoreOptions> options) : ISalesQueries
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

    public async Task<PagedResult<InvoiceSummaryReadModel>> GetInvoicesAsync(
        string? search, int page, int pageSize, CancellationToken ct)
    {
        var q = FinanceInvoiceProjection.Query(db);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var ownerIds = db.Set<Owner>().Where(o => EF.Functions.Like(o.SearchKey, "%" + PersianText.Normalize(term) + "%"))
                .Select(o => o.Id);
            q = q.Where(i => EF.Functions.Like(i.Number, "%" + term + "%") ||
                (i.OwnerId != null && ownerIds.Contains(i.OwnerId)) || EF.Functions.Like(i.OwnerName, "%" + term + "%"));
        }
        var paged = await FinanceInvoiceProjection.Summaries(q.OrderByDescending(i => i.IssuedUtc).ThenByDescending(i => i.Id))
            .ToPagedAsync(page, pageSize, options.Value, ct);
        return paged with { Items = await WithCreatorsAsync(paged.Items, ct) };
    }

    public async Task<IReadOnlyList<InvoiceSummaryReadModel>> GetOwnerInvoicesAsync(
        string ownerId, int take, CancellationToken ct)
    {
        var rows = await FinanceInvoiceProjection.Summaries(FinanceInvoiceProjection.Query(db).Where(i => i.OwnerId == ownerId)
                .OrderByDescending(i => i.IssuedUtc).ThenByDescending(i => i.Id).Take(Math.Clamp(take, 1, 200)))
            .ToListAsync(ct);
        return await WithCreatorsAsync(rows, ct);
    }

    public async Task<InvoiceDetailReadModel?> GetInvoiceAsync(string invoiceId, CancellationToken ct)
    {
        var invoice = db.Set<Invoice>().AsNoTracking().Where(i => i.Id == invoiceId);
        var summary = await FinanceInvoiceProjection.Summaries(FinanceInvoiceProjection.Query(db).Where(i => i.Id == invoiceId))
            .FirstOrDefaultAsync(ct);
        if (summary is null) return null;
        summary = (await WithCreatorsAsync([summary], ct))[0];

        if (!await invoice.AnyAsync(ct))
        {
            var peer = await db.Set<PeerInvoice>().SingleAsync(i => i.TenantId == db.CurrentTenantId && i.Id == invoiceId, ct);
            var receipts = await FinancePaymentProjection.Query(db).Where(p => p.InvoiceId == invoiceId)
                .OrderBy(p => p.ReceivedUtc).ThenBy(p => p.Id)
                .Select(p => new InvoicePaymentReadModel(p.Id, p.Method, p.AmountMinor, p.ReceivedUtc, p.Note)).ToListAsync(ct);
            var detail = peer.DetailJson is null ? null : System.Text.Json.JsonSerializer
                .Deserialize<Animora.Contracts.Wire.Sync.FinanceInvoiceDetail>(peer.DetailJson);
            return new InvoiceDetailReadModel(summary, detail?.GuestName, detail?.InsurerId,
                detail?.InsurancePercent ?? 0, detail?.SubtotalMinor ?? 0, detail?.DiscountMinor ?? 0,
                detail?.TaxMinor ?? 0, detail?.InsuranceMinor ?? 0, peer.PaidTotalMinor,
                (DiscountKind)(detail?.DiscountKind ?? 0), detail?.DiscountValue ?? 0,
                detail?.Lines.Select(l => new InvoiceLineReadModel(l.LineId, (InvoiceLineKind)l.Kind,
                    l.ProductId, l.VariantId, l.CatalogItemId, l.Title, l.Quantity, l.UnitPriceMinor,
                    l.DiscountPercent, l.TaxPercent)).ToArray() ?? [], receipts,
                detail?.Adjustments.Select(a => new InvoiceAdjustmentReadModel(a.Id, (AdjustmentKind)a.Kind,
                    a.AmountMinor, a.Reason, a.CreatedUtc)).ToArray() ?? [],
                SettlementStatus.Settled, null, null, IsReadOnly: true);
        }
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
        var payments = await FinancePaymentProjection.Query(db).Where(p => p.InvoiceId == invoiceId)
            .OrderBy(p => p.ReceivedUtc).ThenBy(p => p.Id)
            .Select(p => new InvoicePaymentReadModel(p.Id, p.Method, p.AmountMinor,
                p.ReceivedUtc, p.Note)).ToListAsync(ct);
        // The settlement verdict belongs to the commands that create or commit this invoice's money/stock; a line
        // edit, a discount or an owner assignment never carries it, and treating them as candidates let a later
        // accepted housekeeping command report "settled" for an invoice whose post had been refused.
        // D15: the single newest command is not enough either. A terminal refusal (Rejected/Conflict/NeedsAttention)
        // is superseded only by a LATER Settled command of the SAME command type — i.e. the same operation was
        // retried and accepted. Among the surviving refusals the EARLIEST wins, so the operator sees the root cause
        // (e.g. STOCK_WOULD_GO_NEGATIVE) instead of a downstream symptom (INVOICE_INVALID_TRANSITION) or a later
        // pending command that merely masks it.
        var settlementCommandTypes = new[]
        {
            CommandTypes.SalesInvoiceCreate, CommandTypes.SalesInvoicePost, CommandTypes.SalesInvoiceVoid,
            CommandTypes.SalesInvoiceRefund, CommandTypes.SalesPaymentRegister, CommandTypes.SalesInvoiceLineReturn,
            CommandTypes.SalesAdjustmentAdd, CommandTypes.SalesInvoiceDelete,
        };
        var journal = await db.Set<PendingCommand>().AsNoTracking()
            .Where(c => (c.AggregateId == invoiceId || (scalar.OperationId != null && c.OperationId == scalar.OperationId))
                && settlementCommandTypes.Contains(c.CommandType))
            .OrderBy(c => c.LocalSequence)
            .Take(100)
            .Select(c => new { c.CommandType, c.Status, c.LastErrorCode, c.ResponsibleRole, c.LocalSequence })
            .ToListAsync(ct);
        var verdict = journal.FirstOrDefault(r => IsTerminalRefusal(r.Status)
                && !journal.Any(later => later.LocalSequence > r.LocalSequence
                    && later.Status == SettlementState.Settled
                    && string.Equals(later.CommandType, r.CommandType, StringComparison.Ordinal)))
            ?? journal.LastOrDefault();
        var settlement = verdict is null ? summary.SettlementStatus : verdict.Status switch
        {
            SettlementState.Settled => SettlementStatus.Settled,
            SettlementState.Rejected => SettlementStatus.Rejected,
            SettlementState.Conflict => SettlementStatus.Conflict,
            SettlementState.Reversed => SettlementStatus.Reversed,
            SettlementState.NeedsAttention => SettlementStatus.NeedsAttention,
            SettlementState.LocallyPosted => SettlementStatus.LocallyPosted,
            _ => summary.SettlementStatus == SettlementStatus.Draft ? SettlementStatus.Draft : SettlementStatus.PendingSettlement,
        };
        var authoritativePaid = summary.SettlementStatus == SettlementStatus.Settled
            ? await db.Set<PeerInvoice>().Where(i => i.TenantId == db.CurrentTenantId && i.Id == invoiceId)
                .Select(i => (long?)i.PaidTotalMinor).SingleOrDefaultAsync(ct) : null;
        return new InvoiceDetailReadModel(summary, scalar.GuestName, scalar.InsurerId,
            scalar.InsurancePercent, scalar.Subtotal, scalar.Discount, scalar.Tax, scalar.Coverage,
            authoritativePaid ?? scalar.Paid, scalar.DiscountKind, scalar.DiscountValue, lines, payments, adjustments,
            settlement, verdict?.LastErrorCode ?? scalar.SettlementErrorCode, verdict?.ResponsibleRole);
    }

    /// <summary>
    /// Fills the creator's display name and role for one page of invoices. The creator id is already persisted on
    /// the invoice, so only the labels are missing: one bounded read over the local invoices joined to their user,
    /// then one over the staff memberships — never a correlated join inside the paged projection. Peer-cache
    /// invoices carry no creator id and keep the empty defaults.
    /// </summary>
    private async Task<IReadOnlyList<InvoiceSummaryReadModel>> WithCreatorsAsync(
        IReadOnlyList<InvoiceSummaryReadModel> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return rows;
        var ids = rows.Select(r => r.Id).ToArray();
        var creators = await (from i in db.Set<Invoice>().AsNoTracking()
            where ids.Contains(i.Id) && i.CreatedByUserId != ""
            join u in db.Set<User>().AsNoTracking() on i.CreatedByUserId equals u.Id
            select new { i.Id, i.CreatedByUserId, u.DisplayName }).ToListAsync(ct);
        if (creators.Count == 0) return rows;
        var roles = await RolesAsync(creators.Select(c => c.CreatedByUserId), ct);
        var byInvoice = new Dictionary<string, (string Name, string? Role)>(StringComparer.Ordinal);
        foreach (var creator in creators)
            byInvoice[creator.Id] = (creator.DisplayName, RoleOf(roles, creator.CreatedByUserId));
        var result = new InvoiceSummaryReadModel[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            result[i] = byInvoice.TryGetValue(rows[i].Id, out var creator)
                ? rows[i] with { CreatedByName = creator.Name, CreatedByRole = creator.Role }
                : rows[i];
        }
        return result;
    }

    /// <summary>Fills the actor's display name and role for one page of the cashier ledger (two bounded reads).</summary>
    private async Task<PagedResult<CashierHistoryReadModel>> WithActorsAsync(
        PagedResult<CashierHistoryReadModel> page, CancellationToken ct)
    {
        var userIds = page.Items.Select(r => r.ActorUserId).Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();
        if (userIds.Length == 0) return page;
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct);
        var roles = await RolesAsync(userIds, ct);
        var byUser = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names) byUser[name.Id] = name.DisplayName;
        var items = new CashierHistoryReadModel[page.Items.Count];
        for (var i = 0; i < items.Length; i++)
        {
            var row = page.Items[i];
            items[i] = row.ActorUserId is { Length: > 0 } id && byUser.TryGetValue(id, out var name)
                ? row with { ActorName = name, ActorRole = RoleOf(roles, id) }
                : row;
        }
        return page with { Items = items };
    }

    /// <summary>The role each of the given users holds in this clinic; an active membership wins over a stale one.</summary>
    private async Task<Dictionary<string, string>> RolesAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct(StringComparer.Ordinal).ToArray();
        var memberships = await db.Set<StaffMembership>().AsNoTracking().Where(m => ids.Contains(m.UserId))
            .Select(m => new { m.UserId, m.Role, m.Status }).ToListAsync(ct);
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var membership in memberships.OrderByDescending(m => m.Status))
            roles[membership.UserId] = membership.Role.ToString();
        return roles;
    }

    private static string? RoleOf(IReadOnlyDictionary<string, string> roles, string userId) =>
        roles.TryGetValue(userId, out var role) ? role : null;

    /// <summary>A settlement that can never become accepted on its own — the states a verdict must surface.</summary>
    private static bool IsTerminalRefusal(SettlementState status) =>
        status is SettlementState.Rejected or SettlementState.Conflict or SettlementState.NeedsAttention;

    public async Task<SalesCatalogItemReadModel?> FindCatalogItemByBarcodeAsync(string barcode, CancellationToken ct)
    {
        // Same projection as a GetCatalogAsync variant row (id = VariantId, on-hand from the snapshot), narrowed to
        // the one active variant of an active, non-service product that carries the scanned barcode.
        return await (from p in db.Set<Product>().AsNoTracking()
            where p.IsActive
            join c in db.Set<ProductCategory>().AsNoTracking() on p.CategoryId equals c.Id into categories
            from c in categories.DefaultIfEmpty()
            from v in p.Variants
            where v.IsActive && v.Barcode == barcode
            orderby v.VariantId
            select new SalesCatalogItemReadModel(v.VariantId, p.Name, p.Kind == ProductKind.Service ? "Service" : "Item",
                c == null ? "سایر" : c.DisplayName, v.SalePrice.Minor,
                p.TrackStock && p.Kind != ProductKind.Service
                    ? (db.Set<StockOnHandSnapshot>().Where(s => s.ProductId == p.Id && s.VariantId == v.VariantId)
                        .Select(s => (long?)s.OnHandScaled).FirstOrDefault() ?? 0L) : (long?)null))
            .FirstOrDefaultAsync(ct);
    }

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
                c == null ? "سایر" : c.DisplayName, v.SalePrice.Minor,
                p.TrackStock && p.Kind != ProductKind.Service
                    ? (db.Set<StockOnHandSnapshot>().Where(s => s.ProductId == p.Id && s.VariantId == v.VariantId)
                        .Select(s => (long?)s.OnHandScaled).FirstOrDefault() ?? 0L) : (long?)null))
            .Take(1000).ToListAsync(ct);
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
                c == null ? "سایر" : c.DisplayName, p.DefaultSalePrice.Minor,
                p.TrackStock && p.Kind != ProductKind.Service ? 0L : (long?)null)).Take(500).ToListAsync(ct);
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
        var items = products.Concat(boarding).ToArray();
        return new SalesCatalogReadModel(items, items.Select(i => i.Category).Distinct().ToArray(), recentIds);
    }

    public async Task<IReadOnlyList<InsurerReadModel>> GetInsurersAsync(CancellationToken ct) =>
        await db.Set<Insurer>().AsNoTracking().OrderBy(i => i.Code)
            .Select(i => new InsurerReadModel(i.Id, i.Name, i.CoveragePercent, i.CeilingMinor)).ToListAsync(ct);

    public async Task<SaleKpiReadModel> GetKpisAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var row = await FinanceInvoiceProjection.Query(db)
            .Where(i => i.PostedUtc >= fromUtc && i.PostedUtc < toUtc && i.State != InvoiceState.Void)
            .GroupBy(i => 1).Select(g => new { Count = g.Count(), Revenue = g.Sum(i => i.TotalMinor) })
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
        var displayInvoices = FinanceInvoiceProjection.Query(db);
        var payments = from p in FinancePaymentProjection.Query(db)
                       join i in displayInvoices on p.InvoiceId equals i.Id
                       select new
                       {
                           Id = p.Id, InvoiceId = i.Id, Number = i.Number, OccurredUtc = p.ReceivedUtc,
                           Kind = p.AmountMinor < 0 ? "Refund" : "Payment", AmountMinor = p.AmountMinor,
                           OwnerName = i.OwnerName, State = i.State,
                           // The payment's recorder is the actor of a money row; a peer-cached payment has no
                           // recorder column, so the invoice's creator stands in for it.
                           ActorUserId = p.ReceivedByUserId ?? i.CreatedByUserId,
                       };
        var invoices = from i in displayInvoices
                       where i.PostedUtc != null
                       select new
                       {
                           Id = i.Id, InvoiceId = i.Id, Number = i.Number,
                           OccurredUtc = i.PostedUtc ?? i.IssuedUtc,
                           Kind = "Accepted",
                           AmountMinor = 0L, OwnerName = i.OwnerName, State = InvoiceState.Posted,
                           ActorUserId = i.CreatedByUserId,
                       };
        // An acceptance never changes kind/time when the invoice is later voided. The cancellation is a separate
        // persisted event, emitted from local InvoiceVoidedEvent or the server's recorded credit-note time.
        var voids = from e in db.Set<ActivityEvent>().AsNoTracking()
                    where e.TenantId == db.CurrentTenantId && e.EntityType == "Invoice" && e.Title == ActivityEventKeys.InvoiceVoided
                    join i in displayInvoices on e.EntityId equals i.Id
                    select new { Id = e.Id, InvoiceId = i.Id, Number = i.Number, OccurredUtc = e.OccurredUtc,
                        Kind = "Void", AmountMinor = 0L, OwnerName = i.OwnerName, State = InvoiceState.Void,
                        ActorUserId = i.CreatedByUserId };
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
            r.Id, r.InvoiceId, r.Number, r.OccurredUtc, r.Kind, r.AmountMinor, r.OwnerName, r.State, r.ActorUserId));
        var paged = await projected.ToPagedAsync(page, pageSize, options.Value, ct);
        return new CashierHistoryPageReadModel(
            await WithActorsAsync(paged, ct),
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

/// <summary>
/// Analytics over the same local/server selection as sales. Never copies peer money into command-owned tables,
/// never replays domain events, and never counts both images of an invoice. Costs/expenses remain local facts.
/// </summary>
internal static class FinanceAnalytics
{
    internal sealed class Day
    {
        public long Revenue, Discount, Refund, Cogs, Expense;
        public int Invoices, Paid;
    }

    internal sealed record Line(string? ProductId, string? VariantId, string Title, string Source,
        decimal Quantity, long Gross, long Net);

    internal static IQueryable<FinanceInvoiceProjection> Invoices(AnimoraLocalDbContext db) =>
        FinanceInvoiceProjection.Query(db).Where(i => i.PostedUtc != null
            && i.State != InvoiceState.Draft && i.State != InvoiceState.Void);

    internal static (DateTimeOffset From, DateTimeOffset To) Bounds(int fromDay, int toDay, TimeZoneInfo zone) =>
        (new DateTimeOffset(Animora.Application.Time.ClinicCalendar.ToUtcTicks(Parse(fromDay), 0, zone), TimeSpan.Zero),
         new DateTimeOffset(Animora.Application.Time.ClinicCalendar.ToUtcTicks(Parse(toDay).AddDays(1), 0, zone), TimeSpan.Zero));

    internal static async Task<Dictionary<int, Day>> DaysAsync(AnimoraLocalDbContext db,
        int fromDay, int toDay, TimeZoneInfo zone, CancellationToken ct)
    {
        var (from, to) = Bounds(fromDay, toDay, zone);
        var days = new Dictionary<int, Day>();
        Day For(DateTimeOffset at)
        {
            var date = Animora.Application.Time.ClinicCalendar.LocalDay(at.UtcTicks, zone);
            var key = date.Year * 10000 + date.Month * 100 + date.Day;
            if (!days.TryGetValue(key, out var day)) days[key] = day = new Day();
            return day;
        }
        var costs = await db.Set<DailyFinanceFact>().AsNoTracking()
            .Where(f => f.TenantId == db.CurrentTenantId && f.LocalDay >= fromDay && f.LocalDay <= toDay)
            .Select(f => new { f.LocalDay, f.CogsMinor, f.ExpenseMinor }).ToListAsync(ct);
        foreach (var cost in costs)
            days[cost.LocalDay] = new Day { Cogs = cost.CogsMinor, Expense = cost.ExpenseMinor };

        var invoices = await Invoices(db).Where(i => (i.PostedUtc >= from && i.PostedUtc < to)
            || (i.State == InvoiceState.Paid && i.SettledUtc >= from && i.SettledUtc < to)).ToListAsync(ct);
        foreach (var invoice in invoices)
        {
            if (invoice.PostedUtc is { } posted && posted >= from && posted < to)
            {
                var day = For(posted);
                var detail = Detail(invoice);
                day.Revenue += detail?.SubtotalMinor ?? invoice.SubtotalMinor;
                day.Discount += detail?.DiscountMinor ?? invoice.DiscountMinor;
                day.Invoices++;
            }
            if (invoice.State == InvoiceState.Paid && invoice.SettledUtc is { } settled && settled >= from && settled < to)
                For(settled).Paid++;
        }
        var refunds = await FinancePaymentProjection.Query(db)
            .Where(p => p.AmountMinor < 0 && p.ReceivedUtc >= from && p.ReceivedUtc < to)
            .Select(p => new { p.ReceivedUtc, p.AmountMinor }).ToListAsync(ct);
        foreach (var refund in refunds) For(refund.ReceivedUtc).Refund += Math.Abs(refund.AmountMinor);
        return days;
    }

    internal static async Task<IReadOnlyList<Line>> LinesAsync(AnimoraLocalDbContext db,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var invoices = Invoices(db).Where(i => i.PostedUtc >= from && i.PostedUtc < to);
        var localIds = invoices.Where(i => !i.FromPeer).Select(i => i.Id);
        var local = await db.Set<Invoice>().AsNoTracking().Where(i => localIds.Contains(i.Id))
            .SelectMany(i => i.Lines, (i, l) => new { i.Kind, l.ProductId, l.VariantId, l.Title,
                LineKind = l.Kind, l.Source, l.Quantity.Value, Unit = l.UnitPrice.Minor, l.DiscountPercent })
            .ToListAsync(ct);
        var lines = new List<Line>();
        foreach (var l in local)
            lines.Add(ToLine(l.ProductId, l.VariantId, l.Title, l.LineKind,
                l.Source == InvoiceLineSource.Stay, l.Value, l.Unit, l.DiscountPercent, l.Kind));
        var peers = await invoices.Where(i => i.FromPeer).ToListAsync(ct);
        foreach (var invoice in peers)
        {
            var detail = Detail(invoice);
            // A legacy summary cannot supply product quantities or charged-line classification. Keep the known
            // total in 'other' rather than inventing catalogue lines or silently dropping its revenue.
            if (detail is null || detail.Lines.Count == 0)
            {
                lines.Add(new Line(null, null, "", "other", 0, invoice.TotalMinor, invoice.TotalMinor));
                continue;
            }
            foreach (var l in detail.Lines)
                lines.Add(ToLine(l.ProductId, l.VariantId, l.Title, (InvoiceLineKind)l.Kind,
                    false, l.Quantity, l.UnitPriceMinor, l.DiscountPercent, invoice.Kind));
        }
        return lines;
    }

    private static Line ToLine(string? product, string? variant, string title, InvoiceLineKind kind,
        bool stay, decimal quantity, long unit, decimal discount, InvoiceKind invoiceKind)
    {
        var sign = invoiceKind == InvoiceKind.SalesReturn ? -1 : 1;
        var gross = quantity * unit;
        var net = (long)decimal.Round(gross - gross * discount / 100m, 0, MidpointRounding.AwayFromZero);
        var source = kind switch
        {
            InvoiceLineKind.Product => "product", InvoiceLineKind.Boarding => "boarding",
            InvoiceLineKind.Service when stay => "boarding", InvoiceLineKind.Service => "visit", _ => "other",
        };
        return new Line(product, variant, title, source, sign * quantity,
            sign * (long)decimal.Round(gross, 0, MidpointRounding.AwayFromZero), sign * net);
    }

    private static Animora.Contracts.Wire.Sync.FinanceInvoiceDetail? Detail(FinanceInvoiceProjection invoice)
    {
        if (!invoice.FromPeer || invoice.DetailJson is null) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<Animora.Contracts.Wire.Sync.FinanceInvoiceDetail>(invoice.DetailJson); }
        catch (System.Text.Json.JsonException) { return null; } // Older cache payload: summary remains usable.
    }

    private static DateOnly Parse(int day) => new(day / 10000, day / 100 % 100, day % 100);
}
