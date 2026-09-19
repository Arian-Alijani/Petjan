using System;
using System.Globalization;
using Animora.Contracts.Dashboard;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Adapts a <see cref="DashboardSpecies"/> row for the "بیماران بر اساس گونه"
/// legend: pre-formatted Persian count/percent strings and a parsed accent
/// brush colour, so the View can bind directly without converters.
/// Mirrors the legend rows built in Animora-UI/assets/dashboard.js, including
/// the hover focus state (<c>.legend-row.on</c>) and the soft share fill
/// (<c>.legend-fill</c>) whose width tracks the species' percentage.
/// </summary>
public sealed partial class DashboardSpeciesViewModel : ObservableObject
{
    // The reference keeps a floor of 9% width so a tiny share is still visible.
    private const double MinFillPercent = 9.0;

    private readonly IBrush _fillBrush;

    [ObservableProperty]
    private bool _isActive;

    public DashboardSpeciesViewModel(int index, DashboardSpecies species, double fraction)
    {
        Index = index;
        Name = species.Name;
        IconKey = ResolveIconKey(species.Name);
        ColorHex = species.ColorHex;
        SwatchBrush = ParseBrush(species.ColorHex);
        CountText = PersianNumberFormatter.FormatGrouped(species.Count);

        // The reference `.legend-fill` gradient:
        //   linear-gradient(to left, var(--c) 0%, color-mix(in srgb, var(--c) 25%, transparent) 100%)
        // built once here so bindings never allocate a new brush per render.
        var solid = ParseColor(species.ColorHex);
        _fillBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(solid, 0),
                new GradientStop(Color.FromArgb(0x40, solid.R, solid.G, solid.B), 1),
            },
        };

        // Round to a whole percent, then render as an integer so the string
        // form is culture-invariant and never carries a decimal point.
        // Casting to long guarantees the int64 ToString(IFormatProvider)
        // overload is selected (avoids the fragile double overload resolution
        // that trips some SDKs with "No overload for 'ToString' takes 1 args").
        var percent = (long)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);
        Percent = percent;
        PercentText = PersianNumberFormatter.ToPersianDigits(
            percent.ToString(CultureInfo.InvariantCulture)) + "٪";

        FillFraction = Math.Max(MinFillPercent, fraction * 100) / 100.0;
    }

    /// <summary>Position in the ring (matches the donut slice).</summary>
    public int Index { get; }

    public string Name { get; }

    /// <summary>Reference animal glyph used alongside this legend label.</summary>
    public string IconKey { get; }

    public string ColorHex { get; }

    public IBrush SwatchBrush { get; }

    public string CountText { get; }

    public string PercentText { get; }

    /// <summary>Whole percent (0..100) of the total.</summary>
    public long Percent { get; }

    /// <summary>Fill fraction (0..1) of the row's soft share background.</summary>
    public double FillFraction { get; }

    /// <summary>The soft share gradient painted behind the legend row.</summary>
    public IBrush FillBrush => _fillBrush;

    private static string ResolveIconKey(string name) => name switch
    {
        "سگ" => "IconDog",
        "گربه" => "IconCat",
        "پرنده" => "IconBird",
        "خرگوش و جوندگان" => "IconRabbit",
        _ => "IconPaw",
    };

    private static Color ParseColor(string hex)
    {
        try
        {
            return Color.Parse(hex);
        }
        catch (FormatException)
        {
            return Colors.Gray;
        }
    }

    private static IBrush ParseBrush(string hex) => new SolidColorBrush(ParseColor(hex));
}
