using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Purchasing.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Purchasing;

/// <summary>
/// The Purchase &amp; Suppliers screen snapshot (C06): the KPI aggregates (active orders,
/// month-to-date spend, outstanding supplier debt) plus the purchase-order list, computed by
/// <see cref="IPurchasingQueries"/> in ≤3 SELECTs.
/// </summary>
[RequiresPermission(PermissionCodes.PurchasingRead)]
public sealed record PurchasingSnapshotQuery : IQuery<PurchasingSnapshotReadModel>;

public sealed class PurchasingSnapshotQueryHandler(IPurchasingQueries queries, IClock clock)
    : IQueryHandler<PurchasingSnapshotQuery, PurchasingSnapshotReadModel>
{
    public async Task<Result<PurchasingSnapshotReadModel>> HandleAsync(
        PurchasingSnapshotQuery query, QueryContext context, CancellationToken ct) =>
        Result<PurchasingSnapshotReadModel>.Ok(await queries.GetSnapshotAsync(clock.UtcNow, ct));
}

/// <summary>Supplier picker: active suppliers matching <paramref name="Search"/> (name / code / phone), by name.</summary>
[RequiresPermission(PermissionCodes.PurchasingRead)]
public sealed record SupplierDirectoryQuery(string? Search = null, int Take = 50)
    : IQuery<IReadOnlyList<SupplierRowReadModel>>;

public sealed class SupplierDirectoryQueryHandler(IPurchaseDirectoryQueries queries)
    : IQueryHandler<SupplierDirectoryQuery, IReadOnlyList<SupplierRowReadModel>>
{
    public async Task<Result<IReadOnlyList<SupplierRowReadModel>>> HandleAsync(
        SupplierDirectoryQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<SupplierRowReadModel>>.Ok(
            await queries.ListSuppliersAsync(query.Search, Math.Clamp(query.Take, 1, 200), ct));
}

/// <summary>
/// The supplier register («تأمین‌کنندگان»): every active supplier with its purchase statistics
/// (receipt count, total purchased, last purchase date, open orders), optionally filtered by name / code / phone.
/// </summary>
[RequiresPermission(PermissionCodes.PurchasingRead)]
public sealed record SupplierRegisterQuery(string? Search = null, int Take = 200)
    : IQuery<IReadOnlyList<SupplierSummaryReadModel>>;

public sealed class SupplierRegisterQueryHandler(IPurchaseDirectoryQueries queries)
    : IQueryHandler<SupplierRegisterQuery, IReadOnlyList<SupplierSummaryReadModel>>
{
    public async Task<Result<IReadOnlyList<SupplierSummaryReadModel>>> HandleAsync(
        SupplierRegisterQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<SupplierSummaryReadModel>>.Ok(
            await queries.ListSupplierSummariesAsync(query.Search, Math.Clamp(query.Take, 1, 500), ct));
}

/// <summary>
/// One supplier's full profile: its register row plus the completed-purchase history (newest first).
/// A successful query with <see langword="null"/> means the id is unknown/inactive — a real answer, not a failure.
/// </summary>
[RequiresPermission(PermissionCodes.PurchasingRead)]
public sealed record SupplierProfileQuery(string SupplierId, int HistoryTake = 100) : IQuery<SupplierProfileReadModel?>;

public sealed class SupplierProfileQueryHandler(IPurchaseDirectoryQueries queries)
    : IQueryHandler<SupplierProfileQuery, SupplierProfileReadModel?>
{
    public async Task<Result<SupplierProfileReadModel?>> HandleAsync(
        SupplierProfileQuery query, QueryContext context, CancellationToken ct)
    {
        var summary = await queries.FindSupplierSummaryAsync(query.SupplierId, ct);
        if (summary is null)
        {
            return Result<SupplierProfileReadModel?>.Ok(null);
        }

        var purchases = await queries.ListSupplierPurchasesAsync(
            query.SupplierId, Math.Clamp(query.HistoryTake, 1, 500), ct);
        return Result<SupplierProfileReadModel?>.Ok(new SupplierProfileReadModel(summary, purchases));
    }
}

/// <summary>The purchases table + KPI counters: recent goods receipts, receipt count and supplier count.</summary>
[RequiresPermission(PermissionCodes.PurchasingRead)]
public sealed record PurchaseReceiptListQuery(int Take = 100) : IQuery<PurchaseReceiptListReadModel>;

public sealed class PurchaseReceiptListQueryHandler(IPurchaseDirectoryQueries queries)
    : IQueryHandler<PurchaseReceiptListQuery, PurchaseReceiptListReadModel>
{
    public async Task<Result<PurchaseReceiptListReadModel>> HandleAsync(
        PurchaseReceiptListQuery query, QueryContext context, CancellationToken ct)
    {
        var rows = await queries.ListReceiptsAsync(Math.Clamp(query.Take, 1, 200), ct);
        var receipts = await queries.CountReceiptsAsync(ct);
        var suppliers = await queries.CountSuppliersAsync(ct);
        return Result<PurchaseReceiptListReadModel>.Ok(new PurchaseReceiptListReadModel(receipts, suppliers, rows));
    }
}
