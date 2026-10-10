using Animora.Application.Ports;
using Animora.Application.UseCases.Sales;
using Animora.Domain.Boarding;
using Animora.Domain.Inventory;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class ServiceRateQueries(AnimoraServerDbContext db) : IServiceRateQueries
{
    private const string ServiceCategoryCode = "SRV";
    private const int MaxRows = 100;

    public async Task<IReadOnlyList<ServiceRateReadModel>> GetAsync(CancellationToken ct)
    {
        var clinic = await db.Set<Product>().AsNoTracking()
            .Where(p => p.Kind == ProductKind.Service)
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Take(MaxRows)
            .Select(p => new ServiceRateReadModel(p.Id, ServiceRateSources.Clinic, p.Name,
                p.DefaultSalePrice.Minor, p.IsActive, null))
            .ToListAsync(ct);
        var boarding = await db.Set<BoardingServiceDef>().AsNoTracking()
            .OrderBy(s => s.Title).ThenBy(s => s.Id).Take(MaxRows)
            .Select(s => new ServiceRateReadModel(s.Id, ServiceRateSources.Boarding, s.Title,
                s.UnitPrice.Minor, s.IsActive, s.Billing.ToString()))
            .ToListAsync(ct);
        return clinic.Concat(boarding).ToArray();
    }

    public async Task<string?> FindServiceCategoryIdAsync(CancellationToken ct)
    {
        var categories = db.Set<ProductCategory>().AsNoTracking().Where(c => c.IsActive);
        var id = await categories.Where(c => c.Code == ServiceCategoryCode).Select(c => c.Id).FirstOrDefaultAsync(ct);
        return id ?? await categories.OrderBy(c => c.SortOrder).Select(c => c.Id).FirstOrDefaultAsync(ct);
    }
}
