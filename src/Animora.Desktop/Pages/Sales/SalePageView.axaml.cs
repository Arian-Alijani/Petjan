using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Animora.Desktop.Pages.Sales;

public partial class SalePageView : UserControl
{
    public SalePageView() => InitializeComponent();

    /// <summary>
    /// Bottom margin of the workspace tab row. On the history tab the row sits
    /// flush against the history toolbar (bottom = 0) so the two surfaces read
    /// as one; on the cashier tab it keeps the usual 14px gap.
    /// </summary>
    public static readonly IValueConverter TabRowMarginConverter =
        new FuncValueConverter<bool, Thickness>(isHistory =>
            isHistory ? new Thickness(2, 0, 2, 0) : new Thickness(2, 0, 2, 14));
}
