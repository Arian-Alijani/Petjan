using Avalonia;
using Avalonia.Controls;

namespace Animora.Desktop.Pages.Inventory;

public partial class PurchaseInvoiceModalView : UserControl
{
    public PurchaseInvoiceModalView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is PurchaseInvoiceModalViewModel vm) vm.ObserveStockChanges();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is PurchaseInvoiceModalViewModel vm) vm.StopObservingStockChanges();
        base.OnDetachedFromVisualTree(e);
    }
}
