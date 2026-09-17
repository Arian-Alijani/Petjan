using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace Animora.Desktop.Shell;

public partial class SidebarView : UserControl
{
    /// <summary>
    /// Aligns the icon/label row: right (RTL leading edge) when the sidebar is
    /// expanded, centered when collapsed so the lone icon sits in the middle of
    /// the compact rail.
    /// </summary>
    public static readonly IValueConverter RailAlignmentConverter =
        new FuncValueConverter<bool, HorizontalAlignment>(
            expanded => expanded ? HorizontalAlignment.Right : HorizontalAlignment.Center);

    /// <summary>Logical AND over several boolean bindings (all must be true).</summary>
    public static readonly IMultiValueConverter AllTrueConverter =
        new AllTrueMultiConverter();

    public SidebarView() => InitializeComponent();

    private sealed class AllTrueMultiConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 0 && values.All(v => v is true);
    }
}
