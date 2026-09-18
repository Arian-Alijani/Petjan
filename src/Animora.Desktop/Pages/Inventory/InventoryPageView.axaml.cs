using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Animora.Desktop.Pages.Inventory;

public partial class InventoryPageView : UserControl
{
    private DispatcherTimer? _searchTimer;

    public InventoryPageView() => InitializeComponent();

    // Mirrors the reference's product search re-render on input (inventory.js
    // binds `input` with no debounce; we debounce briefly to keep typing smooth).
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = (sender as TextBox)?.Text;
        if (DataContext is InventoryPageViewModel vm && text is not null)
        {
            vm.HasSearchValue = text.Trim().Length > 0;
        }

        _searchTimer?.Stop();
        _searchTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(160) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer!.Stop();
            if (DataContext is InventoryPageViewModel model)
            {
                model.ApplySearch((sender as TextBox)?.Text);
            }
        };
        _searchTimer.Start();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is TextBox box)
        {
            box.Text = string.Empty;
            if (DataContext is InventoryPageViewModel vm)
            {
                vm.HasSearchValue = false;
                vm.ApplySearch(string.Empty);
            }
        }
    }
}
