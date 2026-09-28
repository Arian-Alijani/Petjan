namespace Animora.Domain.Cashier;

/// <summary>Cash reconciliation thresholds in minor currency units.</summary>
public static class CashVarianceThresholds
{
    public const long RequiresNote = 5_000_000L;
}
