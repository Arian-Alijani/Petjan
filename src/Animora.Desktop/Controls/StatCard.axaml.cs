using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>
/// Composite KPI card: icon tile, label, large value, optional second info line (<see cref="Secondary"/>),
/// optional delta chip (+12%/-4%, green/red), optional <see cref="Spark"/> sparkline and — when a
/// <see cref="Command"/> is bound — a click/Enter/Space target with a chevron and hand cursor.
/// Cards without Spark/Secondary/Command (every other page) render as a plain KPI card.
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

    public static readonly StyledProperty<string?> SecondaryProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Secondary));

    /// <summary>Sparkline series normalized to 0..1, oldest first; null/&lt;2 points hides the chart.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> SparkProperty =
        AvaloniaProperty.Register<StatCard, IReadOnlyList<double>?>(nameof(Spark));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<StatCard, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<StatCard, object?>(nameof(CommandParameter));

    private ICommand? _subscribedCommand;
    private bool _pressed;

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

    public string? Secondary
    {
        get => GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    public IReadOnlyList<double>? Spark
    {
        get => GetValue(SparkProperty);
        set => SetValue(SparkProperty, value);
    }

    /// <summary>Executed with <see cref="CommandParameter"/> on click / Enter / Space. Null = a non-interactive card.</summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
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
            || change.Property == SecondaryProperty
            || change.Property == SparkProperty
            || change.Property == DeltaProperty)
        {
            UpdateContent();
        }
        else if (change.Property == CommandProperty)
        {
            UpdateCommand();
        }
        else if (change.Property == CommandParameterProperty)
        {
            UpdateInteractivity();
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
        UpdateSparkBrush();
        UpdateCommand();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // A card in a recycled/removed item must not stay rooted by the page's long-lived command.
        if (_subscribedCommand is not null) _subscribedCommand.CanExecuteChanged -= OnCanExecuteChanged;
        _subscribedCommand = null;
    }

    // ── click / keyboard activation ────────────────────────────────────────────────
    private bool IsInteractive => Command is not null;

    private bool CanInvoke() => Command is { } command && command.CanExecute(CommandParameter);

    private void UpdateCommand()
    {
        if (_subscribedCommand is not null) _subscribedCommand.CanExecuteChanged -= OnCanExecuteChanged;
        _subscribedCommand = VisualRoot is not null ? Command : null;
        if (_subscribedCommand is not null) _subscribedCommand.CanExecuteChanged += OnCanExecuteChanged;
        UpdateInteractivity();
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e) => UpdateInteractivity();

    private void UpdateInteractivity()
    {
        var interactive = IsInteractive && CanInvoke();
        Focusable = interactive;
        IsTabStop = interactive;
        Cursor = interactive ? new Cursor(StandardCursorType.Hand) : null;
        ChevronPath.IsVisible = interactive;
        Root.Classes.Set("clickable", interactive);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressed = IsInteractive && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var wasPressed = _pressed;
        _pressed = false;
        if (!wasPressed || e.InitialPressMouseButton != MouseButton.Left) return;
        // Only a release that ends inside the card is a click (dragging off cancels it).
        var position = e.GetPosition(this);
        if (new Rect(Bounds.Size).Contains(position)) Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || (e.Key != Key.Enter && e.Key != Key.Space)) return;
        if (Invoke()) e.Handled = true;
    }

    private bool Invoke()
    {
        if (Command is not { } command || !command.CanExecute(CommandParameter)) return false;
        command.Execute(CommandParameter);
        return true;
    }

    // Spark colour matches the card's icon tone, so the line in every KPI box reads as the same
    // colour as its icon (not a single global green). Resolved from the tree, not hard-coded.
    private void UpdateSparkBrush() => SparkChart.Stroke = ResolveBrush(ToneStrokeKey(IconTone), Brushes.SeaGreen);

    // The accent-stroke resource key for a tone (shared by the icon glyph and the sparkline).
    private static string ToneStrokeKey(Tone tone) => tone switch
    {
        Tone.Danger => "DangerBrush",
        Tone.Warn => "WarnBrush",
        Tone.Info => "InfoBrush",
        Tone.Ok => "OkBrush",
        Tone.Lilac => "LilacBrush",
        Tone.Peach => "PeachBrush",
        _ => "Brand600Brush",
    };

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
        // Keep the card's sparkline in step with the icon colour.
        UpdateSparkBrush();
    }

    private void UpdateContent()
    {
        IconPath.Data = Icon;
        ValueText.Text = Value;
        LabelText.Text = Label;

        var hasFoot = !string.IsNullOrEmpty(Foot);
        FootText.IsVisible = hasFoot;
        FootText.Text = Foot;

        var hasSecondary = !string.IsNullOrEmpty(Secondary);
        SecondaryText.IsVisible = hasSecondary;
        SecondaryText.Text = Secondary;

        var spark = Spark;
        SparkChart.Points = spark;
        SparkChart.IsVisible = spark is { Count: >= 2 };

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
