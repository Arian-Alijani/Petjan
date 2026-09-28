using Animora.Application.Abstractions;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Cashier;

namespace Animora.Application.Ports;

public enum CashierInvoiceTab { Open, Settled, Void, All }
public sealed record CashierStatsReadModel(long CollectedMinor, long OutstandingMinor, long RefundedMinor, long SessionVarianceMinor, int InvoiceCount);
public sealed record CashierTabCountsReadModel(int Open, int Settled, int Void, int All);
public sealed record OpenCashSessionReadModel(string Id, string SessionNumber, long OpeningMinor, long ExpectedMinor, DateTimeOffset OpenedUtc);

/// <summary>Tenant-filtered cashier projections; aggregate loading is reserved for writes.</summary>
public interface ICashierQueries
{
    Task<PagedResult<InvoiceSummaryReadModel>> GetInvoicesAsync(CashierInvoiceTab tab, int page, int pageSize, CancellationToken ct);
    Task<CashierStatsReadModel> GetStatsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
    Task<CashierTabCountsReadModel> GetTabCountsAsync(CancellationToken ct);
    Task<OpenCashSessionReadModel?> GetOpenSessionAsync(string userId, CancellationToken ct);
    Task<bool> HasUnsettledPaymentsAsync(string sessionId, CancellationToken ct);
}
