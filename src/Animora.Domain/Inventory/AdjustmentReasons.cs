namespace Animora.Domain.Inventory;

/// <summary>Stable reason codes recorded on manual stock adjustments.</summary>
public static class AdjustmentReasons
{
    public const string Count = "Count";
    public const string Damage = "Damage";
    public const string Expiry = "Expiry";
    public const string Theft = "Theft";
    public const string Correction = "Correction";

    /// <summary>The first movement of a product created with opening stock.</summary>
    public const string OpeningBalance = "OpeningBalance";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Count, Damage, Expiry, Theft, Correction, OpeningBalance,
    };

    public static bool IsValid(string? reason) => reason is not null && All.Contains(reason);
}
