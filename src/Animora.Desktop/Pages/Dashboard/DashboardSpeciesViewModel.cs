using System;
using System.Globalization;
using Animora.Contracts.Dashboard;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Adapts a <see cref="DashboardSpecies"/> row for the "بیماران بر اساس گونه"
/// legend: pre-formatted Persian count/percent strings and a parsed accent
/// brush colour, so the View can bind directly without converters.
/// Mirrors the legend rows built in Animora-UI/assets/dashboard.js.
/// </summary>
public sealed class DashboardSpeciesViewModel
{
    public DashboardSpeciesViewModel(DashboardSpecies species, double fraction)
    {
        Name = species.Name;
        ColorHex = species.ColorHex;
        SwatchBrush = ParseBrush(species.ColorHex);
        CountText = PersianNumberFormatter.FormatGrouped(species.Count);
        PercentText = PersianNumberFormatter.ToPersianDigits(
            Math.Round(fraction * 100).ToString(CultureInfo.InvariantCulture)) + "٪";
    }

    public string Name { get; }

    public string ColorHex { get; }

    public IBrush SwatchBrush { get; }

    public string CountText { get; }

    public string PercentText { get; }

    private static IBrush ParseBrush(string hex)
    {
        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
    }
}
