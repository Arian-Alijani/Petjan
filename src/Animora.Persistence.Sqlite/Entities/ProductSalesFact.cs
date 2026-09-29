using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Rebuildable per-product sales for a clinic-local day.</summary>
[LocalOnly]
public sealed class ProductSalesFact
{
    private ProductSalesFact() { }
    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public int LocalDay { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public string VariantId { get; private set; } = string.Empty;
    public decimal QuantitySold { get; private set; }
    public long RevenueMinor { get; private set; }
    public long CogsMinor { get; private set; }

    public static ProductSalesFact Create(string tenantId, int localDay, string productId, string variantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        return new() { Id = IdGenerator.NewId(), TenantId = tenantId, LocalDay = localDay,
            ProductId = productId, VariantId = variantId ?? string.Empty };
    }

    public void Apply(decimal quantity = 0, long revenue = 0, long cogs = 0)
    {
        QuantitySold += quantity;
        RevenueMinor = checked(RevenueMinor + revenue);
        CogsMinor = checked(CogsMinor + cogs);
    }
}
