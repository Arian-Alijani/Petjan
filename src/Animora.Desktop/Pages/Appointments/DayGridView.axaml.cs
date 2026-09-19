using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Day-view sub-view (mirrors the reference's <c>viewDay()</c>): one column per
/// active doctor with the shared time grid and the live "now" line. The grid is
/// now fully data-bound inside <see cref="TimeGridSurface"/> — this view only
/// hosts it; a DataContext change propagates to the bound columns without any
/// imperative rebuild.
/// </summary>
public partial class DayGridView : UserControl
{
    public DayGridView() => InitializeComponent();
}
