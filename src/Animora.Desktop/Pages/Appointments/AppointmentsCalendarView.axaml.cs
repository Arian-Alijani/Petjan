using Avalonia.Controls;
using Avalonia.Input;

namespace Animora.Desktop.Pages.Appointments;

public partial class AppointmentsCalendarView : UserControl
{
    public AppointmentsCalendarView() => InitializeComponent();

    // Search is two-way bound to the ViewModel's SearchQueryText; debouncing now
    // lives in the ViewModel. Only the UI-specific Escape-to-clear stays here.
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is AppointmentsCalendarViewModel vm)
        {
            vm.ClearSearchCommand.Execute(null);
        }
    }
}
