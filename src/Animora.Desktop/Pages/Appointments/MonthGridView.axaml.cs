using Avalonia.Controls;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Month-view sub-view (mirrors the reference's <c>viewMonth()</c>): a 7-column
/// Jalali month grid with per-day chips. Built in code by
/// <see cref="AppointmentsGridHost"/>.
/// </summary>
public partial class MonthGridView : UserControl
{
    public MonthGridView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        if (DataContext is AppointmentsCalendarViewModel vm)
        {
            AppointmentsGridHost.BuildMonthGrid(Host, vm);
        }
    }
}
