using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Animora.Desktop.Pages.Cashier;

/// <summary>
/// Code-behind for the Cashier screen. Only interaction glue lives here (no
/// business logic): the inline line-editor commit (mirrors the reference's
/// per-row input listeners) and the discount-value commit. All data operations
/// are routed to the ViewModel.
/// </summary>
public partial class CashierPageView : UserControl
{
    public CashierPageView() => InitializeComponent();

    // Mirrors the reference's per-line input listeners: on blur of the qty /
    // discount boxes, push the row's edits back through the ViewModel (which
    // recomputes the invoice via the mock).
    private void OnLineFieldCommit(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: CashierLineViewModel line })
        {
            line.SubmitEdit();
        }
    }

    private void OnRemoveLineClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CashierLineViewModel line })
        {
            line.Remove();
        }
    }

    private void OnDiscountCommit(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CashierPageViewModel vm)
        {
            _ = vm.SetDiscountCommand.ExecuteAsync(null);
        }
    }
}
