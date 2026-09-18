using Avalonia.Controls;
using Avalonia.Input;

namespace Animora.Desktop.Pages.Cheques;

public partial class ChequesPageView : UserControl
{
    public ChequesPageView() => InitializeComponent();

    /// <summary>
    /// Mirrors the reference's page keyboard shortcuts (keydown handler in
    /// cheques.js): '/' focuses the search box, 'N' opens the new-cheque action,
    /// Escape closes the open row menu.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ChequesPageViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.CloseRowMenuCommand.Execute(null);
            return;
        }

        var target = e.Source as Control;
        if (target is TextBox)
        {
            return;
        }

        if (e.Key == Key.OemQuestion || e.Key == Key.Divide)
        {
            this.FindControl<TextBox>("SearchBox")?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.N && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
        {
            vm.NewChequeCommand.Execute(null);
            e.Handled = true;
        }
    }
}
