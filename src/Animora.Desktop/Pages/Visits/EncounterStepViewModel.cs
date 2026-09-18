using Animora.Desktop.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// One stage chip in the wizard's stepper header, mirroring the reference's
/// <c>renderStepper</c> per-step markup (<c>.wz-step</c>: numbered square that
/// turns brand-filled when active and shows a check glyph once passed, plus the
/// short label). <c>is-active</c>/<c>is-done</c> drive the exact CSS states.
/// </summary>
public sealed partial class EncounterStepViewModel : ObservableObject
{
    public EncounterStepViewModel(int index, string shortLabel)
    {
        Index = index;
        ShortLabel = shortLabel;
        NumberText = Animora.Desktop.Localization.PersianNumberFormatter.ToPersianDigits(index + 1);
    }

    public int Index { get; }

    public string ShortLabel { get; }

    public string NumberText { get; }

    /// <summary>
    /// Whether this step draws the 28×2 connector line before it — every step
    /// except the first (reference <c>.wz-step + .wz-step::before</c>).
    /// </summary>
    public bool ShowConnector => Index > 0;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isDone;
}
