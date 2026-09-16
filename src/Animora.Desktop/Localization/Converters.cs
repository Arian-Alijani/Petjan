using System.Globalization;
using Avalonia.Data.Converters;

namespace Animora.Desktop.Localization;

/// <summary>
/// Binding converter that renders any value with Persian digits via
/// <see cref="PersianNumberFormatter.ToPersianDigits(object?)"/>. When the
/// converter parameter is "grouped" and the value is an integral number, the
/// grouped thousands-separator form (<see cref="PersianNumberFormatter.FormatGrouped"/>)
/// is used instead.
/// </summary>
public sealed class PersianDigitsConverter : IValueConverter
{
    public static readonly PersianDigitsConverter Instance = new();

    public object Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var grouped = parameter is string flag &&
                      flag.Equals("grouped", System.StringComparison.OrdinalIgnoreCase);

        if (grouped && TryGetInt64(value, out var number))
        {
            return PersianNumberFormatter.FormatGrouped(number);
        }

        return PersianNumberFormatter.ToPersianDigits(value);
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        throw new System.NotSupportedException($"{nameof(PersianDigitsConverter)} is one-way only.");
    }

    private static bool TryGetInt64(object value, out long result)
    {
        switch (value)
        {
            case long l: result = l; return true;
            case int i: result = i; return true;
            case short s: result = s; return true;
            case byte b: result = b; return true;
            case sbyte sb: result = sb; return true;
            case uint ui: result = ui; return true;
            case ushort us: result = us; return true;
            default: result = 0; return false;
        }
    }
}

/// <summary>
/// Binding converter that renders a <see cref="System.DateTime"/> (or an existing
/// <see cref="JalaliDate"/>) as a formatted Jalali string. The converter
/// parameter selects the format ("long" default, "medium", "short") — see
/// <see cref="JalaliDate.ToString(string)"/>.
/// </summary>
public sealed class JalaliDateConverter : IValueConverter
{
    public static readonly JalaliDateConverter Instance = new();

    public object Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter as string;

        var jalali = value switch
        {
            JalaliDate jd => jd,
            System.DateTime dt => JalaliDate.FromDateTime(dt),
            System.DateTimeOffset dto => JalaliDate.FromDateTime(dto.LocalDateTime),
            _ => (JalaliDate?)null,
        };

        return jalali is { } result ? result.ToString(format) : string.Empty;
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        throw new System.NotSupportedException($"{nameof(JalaliDateConverter)} is one-way only.");
    }
}
