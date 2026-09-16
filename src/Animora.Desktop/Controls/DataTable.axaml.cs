using System.Collections;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;

namespace Animora.Desktop.Controls;

/// <summary>
/// Thin composite wrapping Avalonia's DataGrid, styled to match Animora-UI's
/// .tbl look. Only supplies the styling shell — consumers set
/// <see cref="Columns"/> for column definitions and <see cref="ItemsSource"/>
/// for row data.
/// </summary>
public partial class DataTable : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<DataTable, IEnumerable?>(nameof(ItemsSource));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Column definitions, forwarded to the inner DataGrid.</summary>
    public ObservableCollection<DataGridColumn> Columns => Grid.Columns;

    public DataTable()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            Grid.ItemsSource = ItemsSource;
        }
    }
}
