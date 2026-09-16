using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>
/// Small status pill matching Animora-UI's .pill/.pill-* pattern.
/// Bindable Text + Tone (neutral|danger|warn|info|ok).
/// </summary>
public partial class Badge : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<Badge, string?>(nameof(Text));

    public static readonly StyledProperty<Tone> ToneProperty =
        AvaloniaProperty.Register<Badge, Tone>(nameof(Tone));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Tone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public Badge()
    {
        InitializeComponent();
        // Tree-independent content only; tone brushes are resolved once the
        // control is attached to the visual tree (see OnAttachedToVisualTree),
        // because App-level resources are not reachable during construction.
        TextBlock.Text = Text;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            TextBlock.Text = Text;
        }
        else if (change.Property == ToneProperty)
        {
            UpdateToneBrushes();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateToneBrushes();
    }

    private void UpdateToneBrushes()
    {
        var (bg, fg) = Tone switch
        {
            Tone.Danger => ("DangerBgBrush", "DangerBrush"),
            Tone.Warn => ("WarnBgBrush", "WarnBrush"),
            Tone.Info => ("InfoBgBrush", "InfoBrush"),
            Tone.Ok => ("OkBgBrush", "OkBrush"),
            _ => ("Line2Brush", "MutedBrush"),
        };

        Root.Background = ResolveBrush(bg, Brushes.Transparent);
        TextBlock.Foreground = ResolveBrush(fg, Brushes.Black);
    }

    // TryFindResource returns false (never AvaloniaProperty.UnsetValue) when the
    // key is missing, so we never cast UnsetValueType to IBrush.
    private IBrush ResolveBrush(string key, IBrush fallback) =>
        this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : fallback;
}
