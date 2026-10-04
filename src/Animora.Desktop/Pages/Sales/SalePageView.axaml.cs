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
        new FuncValueConverter<bool, Thickness>(isFlush =>
            isFlush ? new Thickness(0) : new Thickness(0, 0, 0, 14));

    /// <summary>
    /// Corner radius of the workspace tab row. On the history tab the row's
    /// bottom corners go flat (0) so it fuses seamlessly with the flat top
    /// corners of the history toolbar directly beneath it (see
    /// SaleHistoryView.axaml); on the cashier tab it keeps the normal fully
    /// rounded pill look, matching the Patients/Owners segmented switch.
    /// </summary>
    public static readonly IValueConverter TabRowCornerConverter =
        new FuncValueConverter<bool, CornerRadius>(isFlush =>
            isFlush ? new CornerRadius(12, 12, 0, 0) : new CornerRadius(12));
}
