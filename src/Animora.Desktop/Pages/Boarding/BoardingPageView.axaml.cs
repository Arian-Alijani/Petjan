using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Animora.Desktop.Pages.Boarding;

public partial class BoardingPageView : UserControl
{
    private DispatcherTimer? _searchTimer;

    public BoardingPageView() => InitializeComponent();

    // Mirrors the reference's debounced board search (160ms).
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = (sender as TextBox)?.Text;
        if (DataContext is BoardingPageViewModel vm && text is not null)
        {
            vm.HasSearchValue = text.Trim().Length > 0;
        }

        _searchTimer?.Stop();
        _searchTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(160) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer!.Stop();
            if (DataContext is BoardingPageViewModel model)
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
            if (DataContext is BoardingPageViewModel vm)
            {
                vm.HasSearchValue = false;
                vm.ApplySearch(string.Empty);
            }
        }
    }
}
