using Avalonia.Controls;
using Avalonia.Input;

namespace Animora.Desktop.Pages.Cheques;

public partial class ChequesPageView : UserControl
{
    public ChequesPageView() => InitializeComponent();

    // Search is two-way bound (debounced in the ViewModel); only Escape-to-clear is UI-specific.
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ChequesPageViewModel vm)
        {
            vm.ClearSearchCommand.Execute(null);
        }
    }
}
