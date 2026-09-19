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
}
