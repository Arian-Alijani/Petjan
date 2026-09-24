using Animora.Domain.ValueObjects;

namespace Animora.Application.Validation;

/// <summary>
/// Allocation-light guards: each returns <see langword="null"/> on success (no allocation) and a
/// <see cref="ValidationFailure"/> carrying a machine code on failure.
/// </summary>
public static class Rules
{
    public const string RequiredCode = "REQUIRED";
    public const string MaxLengthCode = "MAX_LENGTH";
    public const string RangeCode = "OUT_OF_RANGE";
    public const string PositiveMoneyCode = "MONEY_NOT_POSITIVE";
    public const string NonNegativeQuantityCode = "QUANTITY_NEGATIVE";
    public const string UlidFormatCode = "ULID_FORMAT";
    public const string NotFutureUtcCode = "FUTURE_UTC";
    public const string ValidEnumCode = "ENUM_UNDEFINED";

    private const int UlidLength = 26;

    /// <summary>Fails when <paramref name="value"/> is null, empty or whitespace.</summary>
    public static ValidationFailure? Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? new(field, RequiredCode) : null;

    /// <summary>Fails when <paramref name="value"/> is longer than <paramref name="max"/>; null passes.</summary>
    public static ValidationFailure? MaxLength(string? value, int max, string field) =>
        value is not null && value.Length > max ? new(field, MaxLengthCode) : null;

    /// <summary>Fails when <paramref name="value"/> is outside the inclusive [min, max] range.</summary>
    public static ValidationFailure? Range<T>(T value, T min, T max, string field)
        where T : IComparable<T> =>
        value.CompareTo(min) < 0 || value.CompareTo(max) > 0 ? new(field, RangeCode) : null;

    /// <summary>Fails when the amount is zero or negative.</summary>
    public static ValidationFailure? PositiveMoney(Money value, string field) =>
        value.Minor <= 0 ? new(field, PositiveMoneyCode) : null;

    /// <summary>Fails when the quantity is negative; zero passes.</summary>
    public static ValidationFailure? NonNegativeQuantity(Quantity value, string field) =>
        value.Value < 0m ? new(field, NonNegativeQuantityCode) : null;

    /// <summary>Fails unless <paramref name="value"/> is a 26-char upper-case Crockford base32 ULID.</summary>
    public static ValidationFailure? UlidFormat(string? value, string field) =>
        IsUlid(value) ? null : new(field, UlidFormatCode);

    /// <summary>Fails when <paramref name="value"/> is later than <paramref name="nowUtc"/>.</summary>
    public static ValidationFailure? NotFutureUtc(DateTimeOffset value, DateTimeOffset nowUtc, string field) =>
        value.UtcDateTime > nowUtc.UtcDateTime ? new(field, NotFutureUtcCode) : null;

    /// <summary>Fails when <paramref name="value"/> is not a declared member of <typeparamref name="TEnum"/>.</summary>
    public static ValidationFailure? ValidEnum<TEnum>(TEnum value, string field)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? null : new(field, ValidEnumCode);

    private static bool IsUlid(string? value)
    {
        if (value is null || value.Length != UlidLength) return false;
        if (value[0] > '7') return false; // 48-bit timestamp: the first char carries only 3 bits.
        foreach (var c in value)
        {
            var ok = c is >= '0' and <= '9'
                || (c is >= 'A' and <= 'Z' && c is not ('I' or 'L' or 'O' or 'U'));
            if (!ok) return false;
        }
        return true;
    }
}
