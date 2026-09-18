using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Week-view sub-view (mirrors the reference's <c>viewWeek()</c>): seven day
/// columns with the shared time grid, Friday marked closed and the live "now"
/// line on today. Built in code by <see cref="AppointmentsGridHost"/>.
/// </summary>
public partial class WeekGridView : UserControl
{
    public WeekGridView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        if (DataContext is AppointmentsCalendarViewModel vm)
        {
            AppointmentsGridHost.BuildTimeGrid(Host, vm);
        }
    }
}
