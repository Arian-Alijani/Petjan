using Animora.Domain.Primitives;

namespace Animora.Domain.Sales;

/// <summary>A tenant-scoped insurer reference used to calculate invoice coverage.</summary>
public sealed class Insurer : AggregateRoot
{
    private Insurer()
    {
    }

    /// <summary>Stable insurer code within the tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Default covered percentage, from zero through one hundred.</summary>
    public decimal CoveragePercent { get; private set; }

    /// <summary>Maximum covered amount in minor currency units; zero means no ceiling.</summary>
    public long CeilingMinor { get; private set; }

    /// <summary>Creates an insurer reference row.</summary>
    public static Insurer Create(
        string tenantId,
        string code,
        string name,
        decimal coveragePercent,
        long ceilingMinor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(coveragePercent, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(coveragePercent, 100m);
        ArgumentOutOfRangeException.ThrowIfNegative(ceilingMinor);

        return new Insurer
        {
            Id = IdGenerator.NewId(),
            TenantId = tenantId,
            Code = code.Trim(),
            Name = name.Trim(),
            CoveragePercent = coveragePercent,
            CeilingMinor = ceilingMinor,
        };
    }

    /// <summary>
    /// Changes the display name, the covered percentage and the ceiling. Invoices that already applied this
    /// insurer keep the percentage they were given (it is copied onto the invoice), so history is unaffected.
    /// </summary>
    public void Update(string name, decimal coveragePercent, long ceilingMinor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(coveragePercent, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(coveragePercent, 100m);
        ArgumentOutOfRangeException.ThrowIfNegative(ceilingMinor);

        Name = name.Trim();
        CoveragePercent = coveragePercent;
        CeilingMinor = ceilingMinor;
    }
}
