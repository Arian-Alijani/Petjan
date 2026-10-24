using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace Animora.Desktop.Controls;

/// <summary>
/// A barcode input: a monospace text box plus a small «اسکن» button. USB barcode scanners are HID
/// keyboard-wedge devices, so scanning simply types the code into whichever box has focus — the button
/// just focuses (and selects) the box so the next scan lands in it, replacing any previous value.
/// The inner box carries the <see cref="InputBoxClass"/> marker class so the central
/// <c>BarcodeScannerService</c> leaves it alone and lets the scan type straight into the field.
/// </summary>
public sealed class BarcodeField : UserControl
{
    /// <summary>Marker class on the inner <see cref="TextBox"/>: scans typed here are real field input.</summary>
    public const string InputBoxClass = "barcode-input";

    /// <summary>Longest barcode accepted (EAN/UPC/Code128 all fit comfortably).</summary>
    private const int MaxBarcodeLength = 64;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<BarcodeField, string?>(
            nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    private readonly TextBox _box;
    private bool _syncing;

    public BarcodeField()
    {
        _box = new TextBox
        {
            MaxLength = MaxBarcodeLength,
            FontFamily = new FontFamily("monospace"),
            PlaceholderText = "بارکد را اسکن یا تایپ کنید",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _box.Classes.Add("inp");
        _box.Classes.Add(InputBoxClass);
        _box.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            _syncing = true;
            try { SetCurrentValue(TextProperty, _box.Text); }
            finally { _syncing = false; }
        };

        var scan = new Button
        {
            Padding = new Thickness(10, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = BuildScanIcon(),
        };
        scan.Classes.Add("outline");
        ToolTip.SetTip(scan, "اسکن بارکد (فوکوس روی فیلد؛ سپس کالا را اسکن کنید)");
        scan.Click += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(_box, 0);
        Grid.SetColumn(scan, 1);
        grid.Children.Add(_box);
        grid.Children.Add(scan);
        Content = grid;
    }

    /// <summary>The barcode text (two-way bindable).</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TextProperty || _syncing) return;

        _syncing = true;
        try { _box.Text = change.GetNewValue<string?>(); }
        finally { _syncing = false; }
    }

    private static Control BuildScanIcon()
    {
        var path = new AvaloniaPath { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        path.Classes.Add("icon");
        var app = global::Avalonia.Application.Current;
        if (app is not null && app.TryFindResource("IconScan", out var geometry) && geometry is Geometry g)
        {
            path.Data = g;
        }

        if (app is not null && app.TryFindResource("InkBrush", out var ink) && ink is IBrush brush)
        {
            path.Stroke = brush;
        }

        return path;
    }
}
