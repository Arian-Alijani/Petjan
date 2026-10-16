using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Controls;

/// <summary>
/// Single transient toast entry rendered by <see cref="ToastHost"/>.
/// Carries fully-resolved visual tokens (accent colour, soft icon-chip
/// background, icon geometry) so the <c>ToastHost</c> template stays declarative,
/// plus an observable <see cref="IsOpen"/> flag that drives the enter/exit
/// transition (fade + slide) — the item is added closed, flipped open for the
/// enter tween, then flipped closed again so it animates out before removal.
/// </summary>
public sealed partial class ToastMessage : ObservableObject
{
    public required string Text { get; init; }

    public Tone Tone { get; init; } = Tone.Ok;

    /// <summary>Glyph shown inside the tinted icon chip (tone-specific).</summary>
    public Geometry? Icon { get; init; }

    /// <summary>Strong tone colour: accent bar, icon stroke, icon-chip border.</summary>
    public IBrush AccentBrush { get; init; } = Brushes.Transparent;

    /// <summary>Soft tinted tone colour behind the icon chip.</summary>
    public IBrush IconBackground { get; init; } = Brushes.Transparent;

    /// <summary>
    /// Drives the show/hide transition. Starts false (off-screen, transparent),
    /// is set true one frame later to glide in, and set false again on dismissal
    /// so the card fades + slides out before it leaves the collection.
    /// </summary>
    [ObservableProperty]
    private bool _isOpen;
}
