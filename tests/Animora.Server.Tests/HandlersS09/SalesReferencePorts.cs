using Animora.Application.Ports;
using Animora.Application.UseCases.Sales;
using Animora.Domain.Sales;
using Animora.Domain.Inventory;
using Animora.Domain.Boarding;
namespace Animora.Server.Tests.HandlersS09;
internal sealed class FixtureInsurerRepository : IInsurerRepository
{
    public List<Insurer> Rows { get; } = [];
    public Task<Insurer?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(e => e.Id == id && !e.Deleted));
    public void Add(Insurer entity) => Rows.Add(entity);
    public Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default) => Task.FromResult(Rows.Any(e => !e.Deleted && e.Id != excludeId && e.Name == name));
}
internal sealed class FixtureProductRepository : IProductRepository
{
    public List<Product> Rows { get; } = [];
    public Task<Product?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(e => e.Id == id && !e.Deleted));
    public void Add(Product entity) => Rows.Add(entity);
    public Task<Product> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.Single(e => e.Id == id));
    public void AddRange(IEnumerable<Product> entities) => Rows.AddRange(entities);
}
internal sealed class FixtureBoardingServiceDefRepository : IBoardingServiceRepository
{
    public List<BoardingServiceDef> Rows { get; } = [];
    public Task<BoardingServiceDef?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(e => e.Id == id && !e.Deleted));
    public void Add(BoardingServiceDef entity) => Rows.Add(entity);
}
internal sealed class FixtureSalesDraft(FixtureInsurerRepository insurers) : ISalesDraftData, IServiceRateQueries
{
    public string VisitHandoffId { get; } = "visit-handoff";
    public string StayHandoffId { get; } = "stay-handoff";
    public Task<SalesHandoff?> FindHandoffAsync(string source, string id, CancellationToken ct) => Task.FromResult<SalesHandoff?>(
        source == "Visit" && id == VisitHandoffId ? new("visit", "owner", "pet", "{}", false, true) :
        source == "Boarding" && id == StayHandoffId ? new("stay", "owner", "pet", "{\"TotalMinor\":100}", false, true) : null);
    public Task<Insurer?> FindInsurerAsync(string id, CancellationToken ct) => insurers.FindAsync(id, ct);
    public Task<SalesCatalogEntry?> FindCatalogEntryAsync(string id, CancellationToken ct) => Task.FromResult<SalesCatalogEntry?>(
        id == "catalog" ? new("catalog", null, null, "Service", InvoiceLineKind.Service, 100, "PCS") : null);
    public Task<string?> FindServiceCategoryIdAsync(CancellationToken ct) => Task.FromResult<string?>("category");
    public Task<IReadOnlyList<ServiceRateReadModel>> GetAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ServiceRateReadModel>>([]);
}
