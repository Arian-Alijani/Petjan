using Avalonia;
using Avalonia.Controls;
using Animora.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Pages.Vaccination;

public partial class AddVaccineModalView : UserControl
{
    private IDisposable? _stockSubscription;
    private readonly Debouncer _stockRefresh = new(TimeSpan.FromMilliseconds(120));

    public AddVaccineModalView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _stockSubscription?.Dispose();
        _stockSubscription = App.Services?.GetService<IDataChangeNotifier>()?.Subscribe(domain =>
        {
            if (domain is DataDomain.Vaccination or DataDomain.Inventory or DataDomain.Purchasing or DataDomain.Sales)
                _stockRefresh.Debounce(() =>
                {
                    if (DataContext is AddVaccineModalViewModel vm) _ = vm.RefreshStockAsync();
                });
        });
        if (DataContext is AddVaccineModalViewModel vm) _ = vm.RefreshStockAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _stockSubscription?.Dispose();
        _stockSubscription = null;
        _stockRefresh.Cancel();
        if (DataContext is AddVaccineModalViewModel vm) vm.CancelStockRefresh();
        base.OnDetachedFromVisualTree(e);
    }
}
