using Avalonia.Controls;
using Avalonia.Threading;

namespace Animora.Desktop.Pages.Boarding;

public partial class BoardingHistoryTabView : UserControl
{
    private DispatcherTimer? _timer;

    public BoardingHistoryTabView() => InitializeComponent();

    // Mirrors the reference's debounced history search (160ms).
    private void OnHistorySearchChanged(object? sender, TextChangedEventArgs e)
    {
        var text = (sender as TextBox)?.Text;
        if (DataContext is BoardingPageViewModel vm && text is not null)
        {
            vm.HasHistoryQuery = text.Trim().Length > 0;
        }

        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(160) };
        _timer.Tick += (_, _) =>
        {
            _timer!.Stop();
            if (DataContext is BoardingPageViewModel model)
            {
                model.ApplyHistorySearch((sender as TextBox)?.Text);
            }
        };
        _timer.Start();
    }
}
