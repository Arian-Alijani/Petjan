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

    /// <summary>
    /// True only when the first binding is true AND the second is false — used
    /// for the compact-rail notification dot, which shows for badged entries
    /// (HasBadge) but only while the sidebar is collapsed (NOT IsExpanded).
    /// </summary>
    public static readonly IMultiValueConverter BadgeDotConverter =
        new FirstTrueRestFalseMultiConverter();

    /// <summary>Inverts a single boolean (visible only when collapsed).</summary>
    public static readonly IValueConverter InverseBoolConverter =
        new FuncValueConverter<bool, bool>(v => !v);

    public SidebarView() => InitializeComponent();

    private sealed class AllTrueMultiConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 0 && values.All(v => v is true);
    }

    private sealed class FirstTrueRestFalseMultiConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count == 2 && values[0] is true && values[1] is false;
    }
}
