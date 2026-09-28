using Animora.Application.Abstractions;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Sales.ReadModels;

public sealed record SaleKpiReadModel(long RevenueMinor, int Count, long AverageMinor);
