using System.Collections.ObjectModel;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Purchasing;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Purchasing;

/// <summary>
/// ViewModel for the Purchase &amp; Suppliers screen (خرید و تأمین‌کنندگان).
/// Mirrors the static markup the reference (<c>Animora-UI/purchase.html</c>)
/// renders: the page header with its «فاکتور خرید» action, the three-card KPI
/// stat row (سفارش فعال / خرید این ماه / بدهی تأمین‌کننده) and the
/// purchase-orders table. The reference carries no interactive JS beyond static
/// markup, so the only action is the header's «فاکتور خرید» button — a
/// toast-only stub (no deeper purchase-order flow exists in the reference to
/// mirror). All data flows through <see cref="IPurchasingQueryService"/> (the
/// single backend swap point, R-18) — no sample data or mock branching leaks
/// into this public shape.
/// </summary>
public sealed partial class PurchasingPageViewModel : PageViewModelBase
{
    private readonly IPurchasingQueryService _service;
    private readonly IToastService _toast;
    private readonly IStoreReadinessGate _readiness;

    [ObservableProperty]
    private PurchaseKpiViewModel? _activeOrders;

    [ObservableProperty]
    private PurchaseKpiViewModel? _monthlyPurchases;

    [ObservableProperty]
    private PurchaseKpiViewModel? _supplierDebt;

    public PurchasingPageViewModel(IPurchasingQueryService service, IStoreReadinessGate readiness,
        IToastService toast)
    {
        _service = service;
        _toast = toast;
        _readiness = readiness;

        Title = "خرید و تأمین‌کنندگان";
        Eyebrow = "زنجیره تأمین";
        Breadcrumbs.Add("کسب و کار / خرید");

        UseLoadStatus(ReloadPageAsync);
    }

    /// <summary>The purchase-orders table rows (the reference's tbody).</summary>
    public RangeObservableCollection<PurchaseOrderRowViewModel> Orders { get; } = [];


    // Singleton page: reload on every visit (not only at construction) so data changed elsewhere is never stale.
    // Failures surface in the shell's load-state block, which also offers the manual refresh.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadPageAsync();
    }

    private Task ReloadPageAsync() => TrackLoadAsync(LoadAsync);

    private async Task LoadAsync()
    {
        await _readiness.WaitAsync();
        var snapshot = await _service.GetSnapshotAsync();

        ActiveOrders = new PurchaseKpiViewModel(snapshot.ActiveOrders);
        MonthlyPurchases = new PurchaseKpiViewModel(snapshot.MonthlyPurchases);
        SupplierDebt = new PurchaseKpiViewModel(snapshot.SupplierDebt);

        Orders.ReplaceAll(snapshot.Orders.Select(order => new PurchaseOrderRowViewModel(order)));
    }

    /// <summary>
    /// The header's «فاکتور خرید» action. The reference's page is static markup
    /// with no purchase-invoice flow to mirror, so this is a toast-only stub.
    /// </summary>
    [RelayCommand]
    private void NewPurchaseInvoice() => _toast.Show("فاکتور خرید جدید — نمونه طراحی", Tone.Ok);
}
