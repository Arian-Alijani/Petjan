using Animora.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Animora.Desktop.Pages.Finance;

public partial class FinancePageView : UserControl
{
    public FinancePageView() => InitializeComponent();

    // Finance donut ring hover → focus/blur the slice under the pointer,
    // mirroring the reference's per-seg mouseenter/mouseleave in
    // Animora-UI/assets/finance-page.js bindDonuts.
    private void OnDonutPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Visual host ||
            host.DataContext is not FinanceDonutViewModel vm ||
            FindRing(host) is not { } ring)
        {
            return;
        }

        var index = ring.SliceIndexAt(e.GetPosition(ring));
        if (index >= 0)
        {
            vm.FocusCommand.Execute(index);
        }
        else
        {
            vm.BlurCommand.Execute(null);
        }
    }

    private void OnDonutPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Visual host && host.DataContext is FinanceDonutViewModel vm)
        {
            vm.BlurCommand.Execute(null);
        }
    }

    private static DonutRing? FindRing(Visual host)
    {
        if (host is DonutRing direct)
        {
            return direct;
        }

        foreach (var child in host.GetVisualChildren())
        {
            if (FindRing(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
