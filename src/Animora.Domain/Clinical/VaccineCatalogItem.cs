using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Domain.Clinical;

/// <summary>
/// A vaccine product the clinic added to its own catalog (the «انبار واکسن» tab). The built-in protocols
/// in <see cref="VaccineSchedule"/> stay read-only; this aggregate holds only the clinic's additions, and
/// carries the stock levels the stock bar renders.
/// </summary>
public sealed class VaccineCatalogItem : AggregateRoot
{
    /// <summary>Longest accepted code.</summary>
    public const int MaxCodeLength = 32;

    /// <summary>Longest accepted display name.</summary>
    public const int MaxNameLength = 120;

    /// <summary>Longest accepted "protects against" text.</summary>
    public const int MaxProtectsLength = 400;

    /// <summary>Upper bound for doses in a series and for stock figures.</summary>
    public const int MaxCount = 100_000;

    /// <summary>Upper bound for the booster interval (ten years).</summary>
    public const int MaxBoosterDays = 3650;

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private VaccineCatalogItem()
    {
    }

    /// <summary>Upper-case protocol code, unique per tenant and distinct from every built-in code.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The diseases it protects against.</summary>
    public string? Protects { get; private set; }

    /// <summary>Doses in the primary series.</summary>
    public int TotalDoses { get; private set; }

    /// <summary>Days until the booster.</summary>
    public int BoosterDays { get; private set; }

    /// <summary>Units on hand.</summary>
    public int Stock { get; private set; }

    /// <summary>Reorder threshold.</summary>
    public int MinStock { get; private set; }

    /// <summary><see langword="false"/> hides the item from pickers without deleting it.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates a catalog item; the code may not collide with a built-in protocol.</summary>
    public static Result<VaccineCatalogItem> Create(
        string tenantId,
        string code,
        string name,
        DateTimeOffset createdUtc,
        string? protects = null,
        int totalDoses = 1,
        int boosterDays = VaccineSchedule.AnnualBoosterDays,
        int stock = 0,
        int minStock = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var invalid = Validate(code, name, protects, totalDoses, boosterDays, stock, minStock);
        if (invalid is not null)
        {
            return Result<VaccineCatalogItem>.Fail(ErrorCodes.ValidationFailed, invalid);
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (VaccineSchedule.All.ContainsKey(normalized))
        {
            return Result<VaccineCatalogItem>.Fail(ErrorCodes.VaccineCatalogCodeTaken, $"'{normalized}' is a built-in vaccine.");
        }

        return Result<VaccineCatalogItem>.Ok(new VaccineCatalogItem
        {
            Id = IdGenerator.NewId(createdUtc),
            TenantId = tenantId,
            Code = normalized,
            Name = name.Trim(),
            Protects = string.IsNullOrWhiteSpace(protects) ? null : protects.Trim(),
            TotalDoses = totalDoses,
            BoosterDays = boosterDays,
            Stock = stock,
            MinStock = minStock,
            IsActive = true,
        });
    }

    private static string? Validate(string? code, string? name, string? protects, int totalDoses, int boosterDays, int stock, int minStock)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > MaxCodeLength)
        {
            return $"A vaccine code of 1..{MaxCodeLength} characters is required.";
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return $"A vaccine name of 1..{MaxNameLength} characters is required.";
        }

        if ((protects?.Trim().Length ?? 0) > MaxProtectsLength)
        {
            return $"'Protects' exceeds {MaxProtectsLength} characters.";
        }

        if (totalDoses is < 1 or > MaxCount || boosterDays is < 1 or > MaxBoosterDays
            || stock is < 0 or > MaxCount || minStock is < 0 or > MaxCount)
        {
            return "Doses, booster interval or stock figures are out of range.";
        }

        return null;
    }
}
