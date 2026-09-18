using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Animora.Desktop.Pages.Vaccination;

public partial class VaccinationPageView : UserControl
{
    private DispatcherTimer? _searchTimer;

    public VaccinationPageView() => InitializeComponent();

    // Mirrors the reference's debounced search (180ms).
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = (sender as TextBox)?.Text;
        if (DataContext is VaccinationPageViewModel vm && text is not null)
        {
            vm.HasSearchValue = text.Trim().Length > 0;
        }

        _searchTimer?.Stop();
        _searchTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(180) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer!.Stop();
            if (DataContext is VaccinationPageViewModel model)
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
            if (DataContext is VaccinationPageViewModel vm)
            {
                vm.ApplySearch(string.Empty);
            }
        }
    }
}
