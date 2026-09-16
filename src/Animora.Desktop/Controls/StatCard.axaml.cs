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

    public static readonly StyledProperty<string?> DeltaProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Delta));

    public static readonly StyledProperty<bool> IsDeltaPositiveProperty =
        AvaloniaProperty.Register<StatCard, bool>(nameof(IsDeltaPositive), true);

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

    public StatCard()
    {
        InitializeComponent();
        UpdateAll();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty
            || change.Property == ValueProperty
            || change.Property == LabelProperty
            || change.Property == DeltaProperty
            || change.Property == IsDeltaPositiveProperty)
        {
            UpdateAll();
        }
    }

    private void UpdateAll()
    {
        IconPath.Data = Icon;
        ValueText.Text = Value;
        LabelText.Text = Label;

        var hasDelta = !string.IsNullOrEmpty(Delta);
        DeltaChip.IsVisible = hasDelta;
        DeltaText.Text = Delta;

        var (bg, fg) = IsDeltaPositive ? ("OkBgBrush", "OkBrush") : ("DangerBgBrush", "DangerBrush");
        DeltaChip.Background = (IBrush?)this.FindResource(bg) ?? Brushes.Transparent;
        DeltaText.Foreground = (IBrush?)this.FindResource(fg) ?? Brushes.Black;
    }
}
