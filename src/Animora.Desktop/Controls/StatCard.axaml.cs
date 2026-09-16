using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>
/// Composite KPI card matching Animora-UI's .stat pattern: icon tile,
/// large value, label, optional delta chip (+12%/-4%, green/red).
/// </summary>
public partial class StatCard : UserControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<StatCard, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Value));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Label));

    public static readonly StyledProperty<string?> FootProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Foot));

    public static readonly StyledProperty<string?> DeltaProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Delta));

    public static readonly StyledProperty<bool> IsDeltaPositiveProperty =
        AvaloniaProperty.Register<StatCard, bool>(nameof(IsDeltaPositive), true);

    public static readonly StyledProperty<Tone> IconToneProperty =
        AvaloniaProperty.Register<StatCard, Tone>(nameof(IconTone), Tone.Brand);

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Foot
    {
        get => GetValue(FootProperty);
        set => SetValue(FootProperty, value);
    }

    public string? Delta
    {
        get => GetValue(DeltaProperty);
        set => SetValue(DeltaProperty, value);
    }

    public bool IsDeltaPositive
    {
        get => GetValue(IsDeltaPositiveProperty);
        set => SetValue(IsDeltaPositiveProperty, value);
    }

    public Tone IconTone
    {
        get => GetValue(IconToneProperty);
        set => SetValue(IconToneProperty, value);
    }

    public StatCard()
    {
        InitializeComponent();
        // Only the tree-independent content is applied here. Resource-backed
        // brushes are resolved in OnAttachedToVisualTree: during construction
        // the control is not yet in the visual/logical tree, so App-level
        // resources are unreachable.
        UpdateContent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty
            || change.Property == ValueProperty
            || change.Property == LabelProperty
            || change.Property == FootProperty
            || change.Property == DeltaProperty)
        {
            UpdateContent();
        }
        else if (change.Property == IsDeltaPositiveProperty)
        {
            UpdateDeltaBrushes();
        }
        else if (change.Property == IconToneProperty)
        {
            UpdateIconTone();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Resources are only reliably resolvable once attached to the tree.
        UpdateDeltaBrushes();
        UpdateIconTone();
    }

    // Tints the icon tile (bg + border + stroke) per tone, mirroring the
    // reference .stat-icon.<tone> palette (teal/lilac/info/danger).
    private void UpdateIconTone()
    {
        var (bg, border, stroke) = IconTone switch
        {
            Tone.Danger => ("DangerBgBrush", "DangerBrush", "DangerBrush"),
            Tone.Warn => ("WarnBgBrush", "WarnBrush", "WarnBrush"),
            Tone.Info => ("InfoBgBrush", "InfoBrush", "InfoBrush"),
            Tone.Ok => ("OkBgBrush", "OkBrush", "OkBrush"),
            Tone.Lilac => ("LilacBgBrush", "LilacBrush", "LilacBrush"),
            Tone.Peach => ("PeachBgBrush", "PeachBrush", "PeachBrush"),
            _ => ("Brand50Brush", "Brand100Brush", "Brand600Brush"),
        };

        IconTile.Background = ResolveBrush(bg, Brushes.Transparent);
        IconTile.BorderBrush = ResolveBrush(border, Brushes.Transparent);
        IconPath.Stroke = ResolveBrush(stroke, Brushes.Black);
    }

    private void UpdateContent()
    {
        IconPath.Data = Icon;
        ValueText.Text = Value;
        LabelText.Text = Label;

        var hasFoot = !string.IsNullOrEmpty(Foot);
        FootText.IsVisible = hasFoot;
        FootText.Text = Foot;

        var hasDelta = !string.IsNullOrEmpty(Delta);
        DeltaChip.IsVisible = hasDelta;
        DeltaText.Text = Delta;
    }

    private void UpdateDeltaBrushes()
    {
        var (bg, fg) = IsDeltaPositive
            ? ("OkBgBrush", "OkBrush")
            : ("DangerBgBrush", "DangerBrush");

        DeltaChip.Background = ResolveBrush(bg, Brushes.Transparent);
        DeltaText.Foreground = ResolveBrush(fg, Brushes.Black);
    }

    // TryFindResource returns false (and never AvaloniaProperty.UnsetValue) when
    // the key is missing, so we avoid casting UnsetValueType to IBrush. We also
    // guard the value type instead of a blind cast.
    private IBrush ResolveBrush(string key, IBrush fallback) =>
        this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : fallback;
}
