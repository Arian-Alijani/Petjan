using System;
using System.Globalization;
using Animora.Contracts.Dashboard;
using Animora.Desktop.Controls;
using Avalonia.Data.Converters;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Single source of truth for translating the tone-agnostic Contracts
/// <see cref="DashboardTone"/> into the Desktop control library's
/// <see cref="Tone"/> enum (used by <see cref="Badge"/> and
/// <see cref="StatCard"/>). Keeps the two enums decoupled while ensuring the
/// mapping is defined in exactly one place.
/// </summary>
public static class ToneMap
{
    public static Tone From(DashboardTone tone) => tone switch
    {
        DashboardTone.Danger => Tone.Danger,
        DashboardTone.Warn => Tone.Warn,
        DashboardTone.Info => Tone.Info,
        DashboardTone.Ok => Tone.Ok,
        DashboardTone.Brand => Tone.Brand,
        DashboardTone.Lilac => Tone.Lilac,
        DashboardTone.Peach => Tone.Peach,
        _ => Tone.Neutral,
    };
}

/// <summary>
/// XAML value converter wrapping <see cref="ToneMap.From"/>, so the KPI
/// StatCards can bind their <c>IconTone</c> directly to a Contracts
/// <see cref="DashboardTone"/> without an intermediate ViewModel.
/// </summary>
public sealed class ToneConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DashboardTone tone ? ToneMap.From(tone) : Tone.Neutral;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
