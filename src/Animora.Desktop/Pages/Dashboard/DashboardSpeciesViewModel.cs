using System;
using System.Globalization;
using Animora.Contracts.Dashboard;
using Animora.Desktop.Controls;
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

    [ObservableProperty]
    private bool _isActive;

    public DashboardSpeciesViewModel(int index, DashboardSpecies species, double fraction)
    {
        Index = index;
        // Stable key -> presentation palette; the legacy Name/ColorHex only matter for an unkeyed species.
        var look = SpeciesPresentation.For(species.Key, species.Name);
        Name = string.IsNullOrEmpty(species.Key) ? species.Name : look.Name;
        IconKey = look.IconKey;
        ColorHex = string.IsNullOrEmpty(species.Key) ? species.ColorHex : look.ColorHex;
        // Shared process-wide (PERFORMANCE.md §10): the swatch is a fixed palette
        // colour, so bindings never allocate a new solid brush per render.
        SwatchBrush = BrushCache.Solid(ColorHex);
        CountText = PersianNumberFormatter.FormatGrouped(species.Count);

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
}
