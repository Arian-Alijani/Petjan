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
    private void OnLegendEnter(object? sender, PointerEventArgs e) => FocusRow(sender);

    private void OnLegendLeave(object? sender, PointerEventArgs e) => BlurAll();

    private void FocusRow(object? sender)
    {
        if (sender is Control { DataContext: DashboardSpeciesViewModel row } &&
            DataContext is DashboardPageViewModel vm)
        {
            vm.FocusSpeciesCommand.Execute(row.Index);
        }
    }

    private void BlurAll()
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

    private void OnDonutPointerExited(object? sender, PointerEventArgs e) => BlurAll();

    // Keyboard parity with hover: focusing the ring/legend shows the same readout, leaving it (or Escape) restores it.
    private void OnLegendGotFocus(object? sender, RoutedEventArgs e) => FocusRow(sender);

    private void OnLegendLostFocus(object? sender, RoutedEventArgs e) => BlurAll();

    private void OnDonutGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardPageViewModel { Species.Count: > 0 } vm && vm.SpeciesActiveIndex < 0)
            vm.FocusSpeciesCommand.Execute(0);
    }

    private void OnDonutLostFocus(object? sender, RoutedEventArgs e) => BlurAll();

    private void OnSpeciesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not DashboardPageViewModel vm) return;
        vm.BlurSpeciesCommand.Execute(null);
        e.Handled = true;
    }
}
