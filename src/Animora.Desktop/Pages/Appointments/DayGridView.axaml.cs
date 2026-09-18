using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Day-view sub-view (mirrors the reference's <c>viewDay()</c>): one column per
/// active doctor with the shared time grid and the live "now" line. The grid is
/// built in code by <see cref="AppointmentsGridHost"/> because its column count
/// is data-driven.
/// </summary>
public partial class DayGridView : UserControl
{
    public DayGridView()
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
