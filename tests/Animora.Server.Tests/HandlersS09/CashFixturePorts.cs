using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Cashier;
using Animora.Domain.Events;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Server.Tests.HandlersS09;

internal sealed class CashSessionRepositoryFake : ICashSessionRepository
{
    public List<CashSession> Sessions { get; } = [];
    public Task<CashSession?> FindAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Sessions.FirstOrDefault(s => s.Id == id));
    public Task<CashSession> RequireAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Sessions.First(s => s.Id == id));
    public void Add(CashSession entity) => Sessions.Add(entity);
    public void AddRange(IEnumerable<CashSession> entities) => Sessions.AddRange(entities);
}

/// <summary>Resolves the newest still-open drawer from the fake repository.</summary>
internal sealed class CashSessionContextFake(CashSessionRepositoryFake repo) : ICashSessionContext
{
    public Task<CashSession?> GetOpenSessionAsync(string tenantId, string userId, CancellationToken ct) =>
        Task.FromResult(repo.Sessions.LastOrDefault(s =>
            s.TenantId == tenantId && s.CashierUserId == userId && s.State == CashSessionState.Open));
}

/// <summary>A cashier-queries stub; close tests never have unsettled payments.</summary>
internal sealed class CashierQueriesFake : ICashierQueries
{
    public bool Unsettled { get; init; }
    public Task<PagedResult<InvoiceSummaryReadModel>> GetInvoicesAsync(CashierInvoiceTab tab, string? search, int page, int pageSize, CancellationToken ct) =>
        Task.FromResult(new PagedResult<InvoiceSummaryReadModel>([], 0, page, pageSize));
    public Task<CashierStatsReadModel> GetStatsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct) =>
        Task.FromResult(new CashierStatsReadModel(0, 0, 0, 0, 0));
    public Task<CashierTabCountsReadModel> GetTabCountsAsync(CancellationToken ct) =>
        Task.FromResult(new CashierTabCountsReadModel(0, 0, 0, 0));
    public Task<OpenCashSessionReadModel?> GetOpenSessionAsync(string userId, CancellationToken ct) =>
        Task.FromResult<OpenCashSessionReadModel?>(null);
    public Task<bool> HasUnsettledPaymentsAsync(string sessionId, CancellationToken ct) =>
        Task.FromResult(Unsettled);
}
