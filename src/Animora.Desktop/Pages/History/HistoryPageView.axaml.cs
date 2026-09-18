using Avalonia.Controls;
using Avalonia.Input;

namespace Animora.Desktop.Pages.History;

public partial class HistoryPageView : UserControl
{
    public HistoryPageView() => InitializeComponent();

    /// <summary>
    /// Mirrors the reference's search field behaviour (history.js): typing in
    /// the search box triggers the filter, Enter re-applies immediately.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not HistoryPageViewModel vm)
        {
            return;
        }

        var target = e.Source as Control;
        if (target is TextBox && e.Key == Key.Enter)
        {
            vm.ApplySearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (this.FindControl<TextBox>("SearchBox") is { } box)
        {
            box.TextChanged += (_, _) =>
            {
                if (DataContext is HistoryPageViewModel vm)
                {
                    vm.SearchText = box.Text ?? string.Empty;
                    vm.HasSearchValue = !string.IsNullOrEmpty(vm.SearchText);
                    vm.ApplySearchCommand.Execute(null);
                }
            };
        }
    }
}
