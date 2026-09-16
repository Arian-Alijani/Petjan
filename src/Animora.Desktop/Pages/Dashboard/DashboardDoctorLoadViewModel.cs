using System;
using Animora.Contracts.Dashboard;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Adapts a <see cref="DashboardDoctorLoad"/> row for the "بار کاری پزشکان
/// امروز" card: parsed accent + soft tint brushes, a Persian "done/total"
/// caption, and grid-star weights driving the proportional progress bar
/// (mirrors the db-docload rows in Animora-UI/assets/dashboard-views.js).
/// </summary>
public sealed class DashboardDoctorLoadViewModel
{
    public DashboardDoctorLoadViewModel(DashboardDoctorLoad load)
    {
        Initial = load.Initial;
        Name = load.Name;
        Specialty = load.Specialty;

        var accent = ParseColor(load.ColorHex);
        AccentBrush = new SolidColorBrush(accent);
        // Soft ~10% tint of the accent for the avatar tile (matches the
        // reference's `${c}1a` background).
        TintBrush = new SolidColorBrush(Color.FromArgb(0x1A, accent.R, accent.G, accent.B));

        // GridLength star weights: a tiny epsilon keeps both columns valid when
        // a doctor is at 0% or 100%.
        var ratio = Math.Clamp(load.Ratio, 0.0, 1.0);
        DoneStar = new Avalonia.Controls.GridLength(Math.Max(ratio, 0.0001), Avalonia.Controls.GridUnitType.Star);
        RemainStar = new Avalonia.Controls.GridLength(Math.Max(1 - ratio, 0.0001), Avalonia.Controls.GridUnitType.Star);

        CountText =
            PersianNumberFormatter.ToPersianDigits(load.Done) + " / " +
            PersianNumberFormatter.ToPersianDigits(load.Total);
    }

    public string Initial { get; }

    public string Name { get; }

    public string Specialty { get; }

    public IBrush AccentBrush { get; }

    public IBrush TintBrush { get; }

    public Avalonia.Controls.GridLength DoneStar { get; }

    public Avalonia.Controls.GridLength RemainStar { get; }

    public string CountText { get; }

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
}
