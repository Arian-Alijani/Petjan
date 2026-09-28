using Animora.Application.Abstractions;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Sales.ReadModels;

public sealed record SalesCatalogItemReadModel(string Id, string Title, string Kind, string Category,
    long PriceMinor);

public sealed record SalesCatalogReadModel(IReadOnlyList<SalesCatalogItemReadModel> Items,
    IReadOnlyList<string> Categories, IReadOnlyList<string> RecentIds);

public sealed record InsurerReadModel(string Id, string Name, decimal CoveragePercent, long CeilingMinor);

public sealed record OwnerLookupReadModel(string Id, string Name, string Phone);
