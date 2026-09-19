using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Month-view sub-view (mirrors the reference's <c>viewMonth()</c>): a 7-column
/// Jalali month grid with per-day chips, now declared entirely in
/// <c>MonthGridView.axaml</c> (data-bound over <see cref="AppointmentsCalendarViewModel.MonthCells"/>).
/// </summary>
public partial class MonthGridView : UserControl
{
    public MonthGridView() => InitializeComponent();
}
