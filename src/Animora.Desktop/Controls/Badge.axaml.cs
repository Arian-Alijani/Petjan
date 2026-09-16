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
        UpdateAppearance();
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
            UpdateAppearance();
        }
    }

    private void UpdateAppearance()
    {
        TextBlock.Text = Text;

        var (bg, fg) = Tone switch
        {
            Tone.Danger => ("DangerBgBrush", "DangerBrush"),
            Tone.Warn => ("WarnBgBrush", "WarnBrush"),
            Tone.Info => ("InfoBgBrush", "InfoBrush"),
            Tone.Ok => ("OkBgBrush", "OkBrush"),
            _ => ("Line2Brush", "MutedBrush"),
        };

        Root.Background = (IBrush?)this.FindResource(bg) ?? Brushes.Transparent;
        TextBlock.Foreground = (IBrush?)this.FindResource(fg) ?? Brushes.Black;
    }
}
