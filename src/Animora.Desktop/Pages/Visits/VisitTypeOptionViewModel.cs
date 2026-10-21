using Animora.Contracts.Visits;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// Adapts a <see cref="VisitTypeInfo"/> to one option in the new-visit form's
/// type grid, mirroring the reference's <c>typeBlockHtml</c> option (accent
/// icon tile + label + default-duration caption).
/// </summary>
public sealed partial class VisitTypeOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public VisitTypeOptionViewModel(VisitTypeInfo type)
    {
        Type = type;
        Label = type.Label;
        IconKey = type.IconKey;
        Accent = VisitsPresentation.Fill(type.Accent);
        AccentSoft = VisitsPresentation.FillAlpha(type.Accent, 0x22);
        DurationText = $"پیش‌فرض {PersianNumberFormatter.ToPersianDigits(type.DefaultDurationMinutes)} دقیقه";
    }

    public VisitTypeInfo Type { get; }
    public string Label { get; }
    public string IconKey { get; }
    public IBrush Accent { get; }
    public IBrush AccentSoft { get; }
    public string DurationText { get; }

    /// <summary>
    /// Icon tile brushes: the active option uses a solid accent tile with white
    /// glyph, the inactive one a 22-alpha accent wash with the accent glyph —
    /// mirroring the reference's inline <c>background: ${t.ac}22; color: ${t.ac}</c>.
    /// </summary>
    public IBrush IconTileBackground => IsSelected
        ? VisitsPresentation.Fill("#4a6cf7")
        : AccentSoft;

    public IBrush IconGlyph => IsSelected ? Brushes.White : Accent;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IconTileBackground));
        OnPropertyChanged(nameof(IconGlyph));
    }
}
