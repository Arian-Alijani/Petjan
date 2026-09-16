using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>Single transient toast entry rendered by <see cref="ToastHost"/>.</summary>
public sealed class ToastMessage
{
    public required string Text { get; init; }

    public Tone Tone { get; init; } = Tone.Ok;

    public Geometry? Icon { get; init; }
}
