using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Animora.Desktop.Pages.Dashboard;

public partial class ManagerDashboardView : UserControl
{
    public ManagerDashboardView() => InitializeComponent();

    // Species legend hover → focus/blur the donut, mirroring the reference's
    // per-row mouseenter/mouseleave wiring in Animora-UI/assets/dashboard.js.
    private void OnLegendEnter(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: DashboardSpeciesViewModel row } &&
            DataContext is DashboardPageViewModel vm)
        {
            vm.FocusSpeciesCommand.Execute(row.Index);
        }
    }

    private void OnLegendLeave(object? sender, PointerEventArgs e)
    {
        if (DataContext is DashboardPageViewModel vm)
        {
            vm.BlurSpeciesCommand.Execute(null);
        }
    }

    // Ring hover → focus the slice under the pointer (the reference binds the
    // same mouseenter/mouseleave on both the .seg circles and the legend rows).
    private void OnDonutPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Visual host || DataContext is not DashboardPageViewModel vm)
        {
            return;
        }

        var p = e.GetPosition(DonutRing);
        var index = DonutRing.SliceIndexAt(p);
        if (index >= 0)
        {
            vm.FocusSpeciesCommand.Execute(index);
        }
        else
        {
            vm.BlurSpeciesCommand.Execute(null);
        }
    }

    private void OnDonutPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is DashboardPageViewModel vm)
        {
            vm.BlurSpeciesCommand.Execute(null);
        }
    }

    // Clicking the ring drills into the patients directory (reference: the seg
    // click handler calls navigateTo('patients.html')).
    private void OnDonutPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is DashboardPageViewModel vm)
        {
            vm.OpenSpeciesPatientsCommand.Execute(null);
        }
    }
}
