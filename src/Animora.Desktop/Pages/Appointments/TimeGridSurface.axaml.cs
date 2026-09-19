using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Declarative day/week time grid shared by <see cref="DayGridView"/> and
/// <see cref="WeekGridView"/>. The column/event trees are data-bound in
/// <c>TimeGridSurface.axaml</c> (ItemsControl over
/// <see cref="AppointmentsCalendarViewModel.Columns"/>), so a DataContext change
/// no longer rebuilds the whole grid imperatively.
/// </summary>
public partial class TimeGridSurface : UserControl
{
    public TimeGridSurface() => InitializeComponent();
}
