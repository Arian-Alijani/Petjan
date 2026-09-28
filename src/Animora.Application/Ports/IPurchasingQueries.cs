using Animora.Application.UseCases.Purchasing.ReadModels;

namespace Animora.Application.Ports;

/// <summary>Grouped purchasing read projections (C06): the screen snapshot in ≤3 SELECTs.</summary>
public interface IPurchasingQueries
{
    /// <summary>
    /// KPI aggregates (active-order count, month-to-date spend, outstanding supplier debt) plus the
    /// purchase-order list, computed relative to <paramref name="nowUtc"/>.
    /// </summary>
    Task<PurchasingSnapshotReadModel> GetSnapshotAsync(DateTimeOffset nowUtc, CancellationToken ct);
}
