using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Domain.Inventory;

/// <summary>
/// A unit of measure aggregate root: the code every <see cref="ValueObjects.Quantity"/> in the
/// product is expressed in, optionally convertible to a base unit of the same dimension
/// (e.g. <c>BOX</c> → 12 <c>EA</c>, <c>KG</c> → 1000 <c>MG</c>).
/// </summary>
/// <remarks>
/// Conversion is all-or-nothing: <see cref="BaseUnitCode"/> and <see cref="ConversionToBase"/> are
/// either both <see langword="null"/> (this unit <i>is</i> a base unit) or both set with a strictly
/// positive factor. The domain never converts silently — a movement, a line and a product all carry
/// their own unit code and mixing units is a validation failure (<c>UNIT_MISMATCH</c>), not an
/// implicit conversion.
/// </remarks>
public sealed class UnitOfMeasure : AggregateRoot
{
    /// <summary>Minimum decimal places a unit may display.</summary>
    public const int MinDecimalPlaces = 0;

    /// <summary>Maximum decimal places a unit may display.</summary>
    public const int MaxDecimalPlaces = 3;

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private UnitOfMeasure()
    {
    }

    /// <summary>Short unit code, e.g. <c>EA</c>, <c>BOX</c>, <c>ML</c>, <c>MG</c>, <c>KG</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Localization key for the display name; the Desktop resolves it to Persian text.</summary>
    public string DisplayKey { get; private set; } = string.Empty;

    /// <summary>How many decimal places quantities in this unit are shown with, in [0, 3].</summary>
    public int DecimalPlaces { get; private set; }

    /// <summary>The base unit of this dimension; <see langword="null"/> when this is itself a base unit.</summary>
    public string? BaseUnitCode { get; private set; }

    /// <summary>
    /// How many base units one of this unit equals; <see langword="null"/> when this is a base unit.
    /// Strictly positive when present.
    /// </summary>
    public decimal? ConversionToBase { get; private set; }

    /// <summary><see langword="true"/> when this unit converts to another unit.</summary>
    public bool IsDerived => BaseUnitCode is not null;

    /// <summary>
    /// Creates a unit of measure. The conversion pair must be consistent
    /// (<c>VALIDATION_FAILED</c>) and the factor strictly positive.
    /// </summary>
    public static Result<UnitOfMeasure> Create(
        string tenantId,
        string code,
        string displayKey,
        int decimalPlaces,
        string? baseUnitCode = null,
        decimal? conversionToBase = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayKey);

        if (decimalPlaces is < MinDecimalPlaces or > MaxDecimalPlaces)
        {
            return Result<UnitOfMeasure>.Fail(
                ErrorCodes.ValidationFailed,
                $"DecimalPlaces {decimalPlaces} must be in [{MinDecimalPlaces}, {MaxDecimalPlaces}].");
        }

        var conversion = ValidateConversion(baseUnitCode, conversionToBase);
        if (conversion.IsFailure)
        {
            return Result<UnitOfMeasure>.Fail(conversion.Error, conversion.Detail);
        }

        return new UnitOfMeasure
        {
            Id = IdGenerator.NewId(),
            TenantId = tenantId,
            Code = code.Trim(),
            DisplayKey = displayKey.Trim(),
            DecimalPlaces = decimalPlaces,
            BaseUnitCode = Clean(baseUnitCode),
            ConversionToBase = conversionToBase,
        };
    }

    /// <summary>Re-points the conversion, keeping the all-or-nothing invariant.</summary>
    public Result SetConversion(string? baseUnitCode, decimal? conversionToBase)
    {
        var conversion = ValidateConversion(baseUnitCode, conversionToBase);
        if (conversion.IsFailure)
        {
            return conversion;
        }

        BaseUnitCode = Clean(baseUnitCode);
        ConversionToBase = conversionToBase;
        return Result.Ok();
    }

    /// <summary>Updates the display key and the displayed precision.</summary>
    public Result SetDisplay(string displayKey, int decimalPlaces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayKey);

        if (decimalPlaces is < MinDecimalPlaces or > MaxDecimalPlaces)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"DecimalPlaces {decimalPlaces} must be in [{MinDecimalPlaces}, {MaxDecimalPlaces}].");
        }

        DisplayKey = displayKey.Trim();
        DecimalPlaces = decimalPlaces;
        return Result.Ok();
    }

    private static Result ValidateConversion(string? baseUnitCode, decimal? conversionToBase)
    {
        var hasBase = !string.IsNullOrWhiteSpace(baseUnitCode);
        if (hasBase != conversionToBase.HasValue)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                "BaseUnitCode and ConversionToBase must both be null or both be set.");
        }

        if (conversionToBase is <= 0m)
        {
            return Result.Fail(
                ErrorCodes.ValidationFailed,
                $"ConversionToBase {conversionToBase} must be greater than zero.");
        }

        return Result.Ok();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
