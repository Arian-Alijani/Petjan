using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Animora.Desktop.Pages.Appointments;

public partial class AppointmentsCalendarView : UserControl
{
    // Below this page width the 420px search no longer fits next to the four tabs.
    private const double CompactBreakpoint = 1000;
    private bool? _compact;

    public AppointmentsCalendarView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Reflow();
    }

    // Layout only: on narrow windows the search field drops under the tabs and stretches to the
    // full toolbar width instead of squeezing the tabs; wide windows keep the single-row layout.
    private void Reflow()
    {
        var compact = Bounds.Width < CompactBreakpoint;
        if (_compact == compact)
        {
            return;
        }

        _compact = compact;
        if (compact)
        {
            Tier1.ColumnDefinitions = new ColumnDefinitions("*");
            Tier1.RowDefinitions = new RowDefinitions("Auto,Auto");
            Tier1.RowSpacing = 10;
            Grid.SetColumn(SearchHost, 0);
            Grid.SetRow(SearchHost, 1);
            SearchHost.Width = double.NaN;
            SearchHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            Tier1.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            Tier1.RowDefinitions = new RowDefinitions();
            Tier1.RowSpacing = 0;
            Grid.SetColumn(SearchHost, 1);
            Grid.SetRow(SearchHost, 0);
            SearchHost.Width = 420;
            SearchHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

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
