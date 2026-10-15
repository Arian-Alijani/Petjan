using Avalonia;
using Avalonia.Controls;
using Animora.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Pages.Sales;

public partial class CatalogModalView : UserControl
{
    private IDisposable? _stockSubscription;
    private readonly Debouncer _stockRefresh = new(TimeSpan.FromMilliseconds(120));

    public CatalogModalView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _stockSubscription?.Dispose();
        _stockSubscription = App.Services?.GetService<IDataChangeNotifier>()?.Subscribe(domain =>
        {
            if (domain is DataDomain.Inventory or DataDomain.Purchasing or DataDomain.Sales or DataDomain.Vaccination)
                _stockRefresh.Debounce(() =>
                {
                    if (DataContext is CatalogModalViewModel vm) _ = vm.RefreshStockAsync();
                });
        });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _stockSubscription?.Dispose();
        _stockSubscription = null;
        _stockRefresh.Cancel();
        if (DataContext is CatalogModalViewModel vm) vm.CancelPendingRefresh();
        base.OnDetachedFromVisualTree(e);
    }
}
