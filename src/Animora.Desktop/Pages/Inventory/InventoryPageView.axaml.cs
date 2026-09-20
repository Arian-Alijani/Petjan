using Avalonia.Controls;
using Avalonia.Input;

namespace Animora.Desktop.Pages.Inventory;

public partial class InventoryPageView : UserControl
{
    public InventoryPageView() => InitializeComponent();

    // Search is two-way bound to the ViewModel's SearchText; debouncing now
    // lives in the ViewModel. Only the UI-specific Escape-to-clear stays here.
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is InventoryPageViewModel vm)
        {
            vm.ClearSearchCommand.Execute(null);
        }
    }
}
