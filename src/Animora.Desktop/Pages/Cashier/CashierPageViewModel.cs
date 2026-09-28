using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Cashier;
using Animora.Contracts.Diagnostics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Cashier;

/// <summary>
/// ViewModel for the Cashier (invoice &amp; payment management) screen. Mirrors
/// the reference's page controller (Animora-UI/assets/cashier.js): the overview
/// stats (<c>renderStats</c>), the status-tab counters (<c>renderTabs</c>), the
/// searchable, tab-filtered, state-sorted invoice list (<c>renderList</c>), and
/// the invoice detail panel (<c>renderDetail</c>: lines via <c>renderLines</c>,
/// totals via <c>renderTotals</c>, the inline payment panel via
/// <c>renderPaymentPanel</c>, payment history via <c>renderPaymentHistory</c>
/// and the footer actions via <c>renderActions</c>). All data flows through
/// <see cref="ICashierService"/> (the single backend swap point, R-18) — no
/// sample data or mock branching leaks into this public shape.
/// </summary>
public sealed partial class CashierPageViewModel : PageViewModelBase
{
    private readonly ICashierService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;

    private readonly List<CashierInvoice> _invoices = [];
    private bool _suppressSelects;

    private CashierTab _tab = CashierTab.Open;

    public CashierPageViewModel(ICashierService service, IStoreReadinessGate readiness, IToastService toast)
    {
        _service = service;
        _readiness = readiness;
        _toast = toast;

        Title = "صندوق و مالی";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("صندوق");

        _ = LoadAsync();
    }

    public RangeObservableCollection<CashierStatViewModel> Stats { get; } = [];

    public RangeObservableCollection<CashierTabViewModel> Tabs { get; } = [];

    public RangeObservableCollection<CashierInvoiceRowViewModel> InvoiceRows { get; } = [];

    public RangeObservableCollection<CashierLineViewModel> Lines { get; } = [];

    public RangeObservableCollection<CashierPaymentViewModel> Payments { get; } = [];

    public ObservableCollection<CashierMethodViewModel> Methods { get; } = [];

    public ObservableCollection<CashierPresetViewModel> Presets { get; } = [];

    public RangeObservableCollection<CashierCatalogOptionViewModel> CatalogOptions { get; } = [];

    public ObservableCollection<CashierInsurerOptionViewModel> InsurerOptions { get; } = [];

    public ObservableCollection<CashierDiscountOptionViewModel> DiscountOptions { get; } = [];

    public string SearchPlaceholder => "جستجو در فاکتورها، مالکین یا حیوانات…";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CashierCatalogOptionViewModel? _selectedCatalogOption;

    [ObservableProperty]
    private CashierInsurerOptionViewModel? _selectedInsurerOption;

    [ObservableProperty]
    private CashierDiscountOptionViewModel? _selectedDiscountOption;

    [ObservableProperty]
    private string _discountValueText = "۰";

    // ── Detail state ────────────────────────────────────────────────────

    [ObservableProperty]
    private CashierDetailViewModel? _detail;

    [ObservableProperty]
    private bool _hasDetail;

    /// <summary>The default no-selection detail head text (= the reference's placeholder).</summary>
    public bool ShowDetailPlaceholder => !HasDetail;

    [ObservableProperty]
    private string _payAmountText = string.Empty;

    [ObservableProperty]
    private string _payReferenceText = string.Empty;

    [ObservableProperty]
    private string _payNoteText = string.Empty;

    [ObservableProperty]
    private string _refundAmountText = string.Empty;

    public bool IsEmpty => InvoiceRows.Count == 0;

    public bool ShowPaymentPanel => Detail is { IsEditable: true };

    public string PayAmountWatermark => Detail is null ? "۰" : CashierPresentation.Money(Detail.Detail.Due);

    partial void OnHasDetailChanged(bool value) => OnPropertyChanged(nameof(ShowDetailPlaceholder));

    partial void OnDetailChanged(CashierDetailViewModel? value)
    {
        OnPropertyChanged(nameof(ShowPaymentPanel));
        OnPropertyChanged(nameof(PayAmountWatermark));
    }

    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    partial void OnSearchTextChanged(string value) => _searchDebounce.Debounce(RenderList);

    partial void OnSelectedDiscountOptionChanged(CashierDiscountOptionViewModel? value)
    {
        if (!_suppressSelects && Detail is not null)
        {
            _ = SetDiscount();
        }
    }

    partial void OnSelectedInsurerOptionChanged(CashierInsurerOptionViewModel? value)
    {
        if (!_suppressSelects && Detail is not null)
        {
            _ = SetInsurance(value?.Id);
        }
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowPaymentPanel));
        OnPropertyChanged(nameof(ShowDetailPlaceholder));
    }

    // ── Loading ─────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        IsBusy = true;
        await _readiness.WaitAsync();

        BuildMethods();
        BuildPresets();
        BuildDiscountOptions();

        CatalogOptions.ReplaceAll(
            (await _service.GetCatalogItemsAsync()).Select(item => new CashierCatalogOptionViewModel(item)));

        InsurerOptions.Clear();
        InsurerOptions.Add(new CashierInsurerOptionViewModel(new CashierInsurer(string.Empty, string.Empty, 0), true));
        foreach (var insurer in await _service.GetInsurersAsync())
        {
            InsurerOptions.Add(new CashierInsurerOptionViewModel(insurer, false));
        }

        await ReloadAsync();
        IsBusy = false;
    }

    private async Task ReloadAsync()
    {
        Stats.Clear();
        foreach (var stat in await _service.GetStatsAsync())
        {
            Stats.Add(new CashierStatViewModel(stat));
        }

        Tabs.Clear();
        foreach (var count in await _service.GetTabCountsAsync())
        {
            Tabs.Add(new CashierTabViewModel(count));
        }

        _invoices.Clear();
        _invoices.AddRange(await _service.GetInvoicesAsync());

        RenderList();
    }

    private void BuildMethods()
    {
        Methods.Clear();
        Methods.Add(new CashierMethodViewModel(CashierPaymentMethod.Cash, "نقدی", "IconWallet"));
        Methods.Add(new CashierMethodViewModel(CashierPaymentMethod.Card, "کارت", "IconCreditCard"));
        Methods.Add(new CashierMethodViewModel(CashierPaymentMethod.Cheque, "چک", "IconCheque"));
        Methods.Add(new CashierMethodViewModel(CashierPaymentMethod.Credit, "نسیه", "IconHourglass"));
        Methods.Add(new CashierMethodViewModel(CashierPaymentMethod.Mixed, "ترکیبی", "IconLayers"));
        SetMethod(CashierPaymentMethod.Cash);
    }

    private void BuildPresets()
    {
        Presets.Clear();
        foreach (var preset in new long[] { 50_000, 100_000, 200_000, 500_000, 1_000_000 })
        {
            Presets.Add(new CashierPresetViewModel(CashierPresentation.Money(preset), preset, false));
        }
    }

    private void BuildDiscountOptions()
    {
        DiscountOptions.Clear();
        DiscountOptions.Add(new CashierDiscountOptionViewModel(CashierDiscountKind.Percent, "درصد (%)"));
        DiscountOptions.Add(new CashierDiscountOptionViewModel(CashierDiscountKind.Amount, "مبلغ (ریال)"));
    }

    // ── Tabs / list ─────────────────────────────────────────────────────

    [RelayCommand]
    private void SetTab(string? key)
    {
        if (string.IsNullOrEmpty(key) || !System.Enum.TryParse<CashierTab>(key, true, out var tab))
        {
            return;
        }

        _tab = tab;
        SearchText = string.Empty;
        ShowEmptyDetail();
        RenderList();
    }

    private bool TabMatches(CashierInvoice inv) => _tab switch
    {
        CashierTab.Open => inv.State is CashierInvoiceState.Draft or CashierInvoiceState.Partial,
        CashierTab.Settled => inv.State == CashierInvoiceState.Settled,
        CashierTab.Void => inv.State is CashierInvoiceState.Void or CashierInvoiceState.Refunded,
        _ => true,
    };

    private void RenderList()
    {
        foreach (var tab in Tabs)
        {
            tab.IsActive = tab.Tab == _tab;
        }

        var rows = _invoices.Where(TabMatches);

        var q = PersianText.Normalize(SearchText);
        if (q.Length > 0)
        {
            rows = rows.Where(i => PersianText.Contains(
                PersianText.Normalize($"{i.Id} {i.OwnerName} {i.PetName}"), q));
        }

        var sorted = rows
            .OrderBy(i => StateOrder(i.State))
            .ThenByDescending(i => i.Id, System.StringComparer.Ordinal)
            .ToList();

        InvoiceRows.ReplaceAll(sorted.Select(inv => new CashierInvoiceRowViewModel(inv)
        {
            IsActive = Detail is not null && Detail.Id == inv.Id,
        }));

        RaiseDerived();
    }

    private static int StateOrder(CashierInvoiceState state) => state switch
    {
        CashierInvoiceState.Draft => 0,
        CashierInvoiceState.Partial => 1,
        CashierInvoiceState.Settled => 2,
        CashierInvoiceState.Void => 3,
        _ => 4,
    };

    // ── Selection ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SelectInvoice(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        var detail = await _service.GetDetailAsync(id);
        if (detail is null)
        {
            return;
        }

        ResetPayment();
        ShowDetail(detail);
    }

    private void ResetPayment()
    {
        PayAmountText = string.Empty;
        PayReferenceText = string.Empty;
        PayNoteText = string.Empty;
        RefundAmountText = string.Empty;
        SetMethod(CashierPaymentMethod.Cash);
    }

    private void ShowDetail(CashierInvoiceDetail detail)
    {
        Detail = new CashierDetailViewModel(detail);
        HasDetail = true;

        DiscountValueText = CashierPresentation.Fa(detail.DiscountValue);
        _suppressSelects = true;
        SelectedDiscountOption = DiscountOptions.FirstOrDefault(o => o.Kind == detail.DiscountKind)
            ?? DiscountOptions.FirstOrDefault();
        SelectedInsurerOption = InsurerOptions.FirstOrDefault(o => !o.IsNone && o.Id == detail.InsuranceOrgId)
            ?? InsurerOptions.FirstOrDefault();
        _suppressSelects = false;

        BuildLines(detail);
        BuildPayments(detail);
        RenderList();
        RaiseDerived();
    }

    private void ShowEmptyDetail()
    {
        Detail = null;
        HasDetail = false;
        Lines.Clear();
        Payments.Clear();
        RaiseDerived();
    }

    private void BuildLines(CashierInvoiceDetail detail)
    {
        // Batched rebuild (PERFORMANCE.md §5): a single Reset, not one
        // notification per invoice line.
        Lines.ReplaceAll(detail.Lines.Select(line =>
            new CashierLineViewModel(line, detail.IsEditable, OnLineEdited, OnLineRemoved)));
    }

    private void BuildPayments(CashierInvoiceDetail detail)
    {
        // Batched rebuild (PERFORMANCE.md §5): a single Reset, not one
        // notification per payment.
        Payments.ReplaceAll(detail.Payments.Select(payment =>
            new CashierPaymentViewModel(payment)));
    }

    private void OnLineEdited(int index, int quantity, int discountPercent) =>
        _ = RunMutationAsync(() =>
            _service.UpdateLineAsync(Detail!.Id, index, quantity, discountPercent));

    private void OnLineRemoved(int index) =>
        _ = RunMutationAsync(() => _service.RemoveLineAsync(Detail!.Id, index));

    // ── Payment panel ───────────────────────────────────────────────────

    [RelayCommand]
    private void PickMethod(CashierPaymentMethod method) => SetMethod(method);

    private void SetMethod(CashierPaymentMethod method)
    {
        foreach (var m in Methods)
        {
            m.IsOn = m.Method == method;
        }
    }

    private CashierPaymentMethod ActiveMethod =>
        Methods.FirstOrDefault(m => m.IsOn)?.Method ?? CashierPaymentMethod.Cash;

    [RelayCommand]
    private void UsePreset(long value)
    {
        PayAmountText = CashierPresentation.Fa(value);
    }

    [RelayCommand]
    private void UseFullAmount()
    {
        if (Detail is not null)
        {
            PayAmountText = CashierPresentation.Fa(Detail.Detail.Due);
        }
    }

    // ── Footer actions ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task Recalculate()
    {
        if (Detail is null)
        {
            return;
        }

        var updated = await _service.RecalculateAsync(Detail.Id);
        ShowDetail(updated);
        _toast.Show("فاکتور باز محاسبه شد", Tone.Ok);
    }

    [RelayCommand]
    private async Task Pay()
    {
        if (Detail is null)
        {
            return;
        }

        var amount = ParseLong(PayAmountText);
        if (amount <= 0)
        {
            amount = Detail.Detail.Due;
        }

        var id = Detail.Id;
        var request = new CashierPayRequest(ActiveMethod, amount, PayReferenceText, PayNoteText);
        var result = await _service.PayAsync(id, request);

        if (!result.Ok)
        {
            _toast.Show(result.Error ?? "خطا در ثبت پرداخت", Tone.Danger);
            return;
        }

        var verb = result.Invoice?.State == CashierInvoiceState.Settled ? "تسویه شد" : "پرداخت ثبت شد";
        _toast.Show($"{verb} · {CashierPresentation.Money(amount)}", Tone.Ok);

        ResetPayment();
        await RefreshAsync(id);
    }

    [RelayCommand]
    private async Task Void()
    {
        if (Detail is null)
        {
            return;
        }

        var id = Detail.Id;
        var result = await _service.VoidAsync(id);
        if (!result.Ok)
        {
            _toast.Show(result.Error ?? "خطا در ابطال", Tone.Danger);
            return;
        }

        _toast.Show("فاکتور ابطال شد", Tone.Ok);
        await RefreshAsync(id);
    }

    [RelayCommand]
    private async Task Refund()
    {
        if (Detail is null)
        {
            return;
        }

        var id = Detail.Id;
        var amount = ParseLong(RefundAmountText);
        if (amount <= 0)
        {
            amount = Detail.Detail.Paid;
        }

        var result = await _service.RefundAsync(id, new CashierRefundRequest(amount));
        if (!result.Ok)
        {
            _toast.Show(result.Error ?? "خطا در عودت", Tone.Danger);
            return;
        }

        _toast.Show($"عودت ثبت شد · {CashierPresentation.Money(amount)}", Tone.Ok);
        RefundAmountText = string.Empty;
        await RefreshAsync(id);
    }

    [RelayCommand]
    private void Print() => _toast.Show("گزینهٔ چاپ در نسخهٔ بعدی فعال می‌شود (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void PrintInvoice() => _toast.Show("گزینهٔ چاپ در نسخهٔ بعدی فعال می‌شود (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void Export() => _toast.Show("خروجی اکسل به‌زودی (نمونه طراحی)", Tone.Info);

    // ── Inline add-line / discount / insurance ──────────────────────────

    [RelayCommand]
    private async Task AddLine()
    {
        if (Detail is null || SelectedCatalogOption is null)
        {
            return;
        }

        await RunMutationAsync(() => _service.AddLineAsync(Detail.Id, SelectedCatalogOption.Id));
        SelectedCatalogOption = null;
    }

    [RelayCommand]
    private async Task SetDiscount()
    {
        if (Detail is null)
        {
            return;
        }

        var kind = SelectedDiscountOption?.Kind ?? CashierDiscountKind.Percent;
        var value = (int)ParseLong(DiscountValueText);
        await RunMutationAsync(() => _service.SetDiscountAsync(Detail.Id, kind, value));
    }

    [RelayCommand]
    private async Task SetInsurance(string? insurerId)
    {
        if (Detail is null)
        {
            return;
        }

        await RunMutationAsync(() => _service.SetInsuranceAsync(Detail.Id, insurerId));
    }

    private async Task RunMutationAsync(System.Func<Task<CashierInvoiceDetail>> mutation)
    {
        if (Detail is null)
        {
            return;
        }

        var updated = await mutation();
        ShowDetail(updated);
        await ReloadStatsAsync();
    }

    private async Task RefreshAsync(string currentId)
    {
        await ReloadStatsAsync();
        var detail = await _service.GetDetailAsync(currentId);
        if (detail is not null)
        {
            ShowDetail(detail);
        }
        else
        {
            ShowEmptyDetail();
        }
    }

    private async Task ReloadStatsAsync()
    {
        Stats.ReplaceAll(
            (await _service.GetStatsAsync()).Select(stat => new CashierStatViewModel(stat)));

        Tabs.ReplaceAll(
            (await _service.GetTabCountsAsync()).Select(count => new CashierTabViewModel(count)));

        _invoices.Clear();
        _invoices.AddRange(await _service.GetInvoicesAsync());
    }

    private static long ParseLong(string? text)
    {
        var ascii = PersianNumberFormatter.ToAsciiDigits(text).Replace("٬", string.Empty).Replace(",", string.Empty).Trim();
        return long.TryParse(ascii, out var v) ? v : 0;
    }
}
