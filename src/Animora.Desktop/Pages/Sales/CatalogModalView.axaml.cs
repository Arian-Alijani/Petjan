using Avalonia;
using Avalonia.Controls;
using Animora.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Pages.Sales;

public partial class CatalogModalView : UserControl
{
    private IDisposable? _stockSubscription;
    private readonly Debouncer _stockRefresh = new(TimeSpan.FromMilliseconds(120));

    // Shared keyboard-wedge scanner: while this drawer is open AND its VM accepts barcodes (Sales/Cashier add
    // mode), a scan adds the matching good straight into the open invoice — so the scanner keeps working even
    // with the «افزودن اقلام» drawer open (it is otherwise suppressed while an overlay is up).
    private BarcodeScannerService? _scanner;

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

        if (DataContext is CatalogModalViewModel { SupportsBarcode: true })
        {
            _scanner = App.Services?.GetService<BarcodeScannerService>();
            if (_scanner is not null)
            {
                _scanner.BarcodeScanned -= OnBarcodeScanned;
                _scanner.BarcodeScanned += OnBarcodeScanned;
            }
        }
    }

    private void OnBarcodeScanned(string barcode)
    {
        if (DataContext is CatalogModalViewModel vm)
        {
            _ = vm.TryAddByBarcodeAsync(barcode);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _stockSubscription?.Dispose();
        _stockSubscription = null;
        _stockRefresh.Cancel();
        if (_scanner is not null)
        {
            _scanner.BarcodeScanned -= OnBarcodeScanned;
            _scanner = null;
        }

        if (DataContext is CatalogModalViewModel vm) vm.CancelPendingRefresh();
        base.OnDetachedFromVisualTree(e);
    }
}
