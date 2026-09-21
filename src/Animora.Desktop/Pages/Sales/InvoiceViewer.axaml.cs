using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Code-behind for the center-screen invoice viewer. Only interaction glue lives
/// here (no business logic): the inline line-editor commit (quantity/discount on
/// blur) and the remove-line click, mirrored from the Cashier screen. Every
/// operation is routed to <see cref="InvoiceViewerViewModel"/>, which persists it
/// through the service (recording the change in the audit trail).
/// </summary>
public partial class InvoiceViewerView : UserControl
{
    public InvoiceViewerView() => InitializeComponent();

    private void OnLineFieldCommit(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: InvoiceViewerLineViewModel line })
        {
            line.SubmitEdit();
        }
    }

    private void OnRemoveLineClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InvoiceViewerLineViewModel line })
        {
            line.Remove();
        }
    }
}
