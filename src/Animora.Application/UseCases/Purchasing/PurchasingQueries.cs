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
