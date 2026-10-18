using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// The «فاکتور» workspace tab: the invoice register, and nothing else. Where the cashier tab is the working
/// surface for one open invoice, this tab answers a single question fast — «این فاکتور برای کی و از کدام فروش
/// بود؟» — by putting the customer identity first, the invoice number + source next to it, the Jalali date, the
/// patient, the money triplet (total / paid / due) and the state, with edit/delete offered only where the backend
/// allows them (a draft).
/// <para>
/// <b>Data.</b> One bounded read (<see cref="ISalesQueryService.GetInvoicesAsync"/>) feeds the whole tab; the state
/// chips and the search filter that in-memory page, so switching a chip never re-queries. The tab loads lazily on
/// its first activation and refreshes on any sale write (PERFORMANCE.md §12).
/// </para>
/// </summary>
public sealed partial class SaleInvoiceLedgerViewModel : ObservableObject
{
    private readonly ISalesQueryService _service;
    private readonly IHistoryRecordOpener _opener;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onInvoiceChanged;
    private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(160));

    private readonly List<InvoiceSummary> _invoices = [];
    private string _filter = "all";
    private bool _loaded;
    private int _generation;

    public SaleInvoiceLedgerViewModel(ISalesQueryService service, IHistoryRecordOpener opener, IToastService toast,
        IDialogService dialogs, Action? onInvoiceChanged = null)
    {
        _service = service;
        _opener = opener;
        _toast = toast;
        _dialogs = dialogs;
        _onInvoiceChanged = onInvoiceChanged;

        Filters.Add(new SaleInvoiceLedgerFilterViewModel("all", "همه"));
        Filters.Add(new SaleInvoiceLedgerFilterViewModel("open", "باز و در انتظار"));
        Filters.Add(new SaleInvoiceLedgerFilterViewModel("settled", "تسویه‌شده"));
        Filters.Add(new SaleInvoiceLedgerFilterViewModel("closed", "ابطال و عودت"));
        Filters[0].IsActive = true;
    }

    // ── Surface ─────────────────────────────────────────────────────────────

    public RangeObservableCollection<SaleInvoiceLedgerRowViewModel> Rows { get; } = [];

    public ObservableCollection<SaleInvoiceLedgerFilterViewModel> Filters { get; } = [];

    public string SearchPlaceholder => "جستجوی شمارهٔ فاکتور، نام سرپرست، حیوان یا شمارهٔ تماس…";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>«۱۲ فاکتور · ۳ باز» — the register's one-line summary.</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _totalText = "۰";

    [ObservableProperty]
    private string _dueText = "۰";

    [ObservableProperty]
    private string _openCountText = "۰";

    partial void OnSearchTextChanged(string value)
    {
        HasSearchValue = !string.IsNullOrWhiteSpace(value);
        _searchDebounce.Debounce(Render);
    }

    // ── Loading ─────────────────────────────────────────────────────────────

    /// <summary>Loads the register the first time the tab is shown; every later activation re-reads it.</summary>
    public void EnsureLoaded()
    {
        if (_loaded)
        {
            Refresh();
            return;
        }

        _loaded = true;
        _ = ReloadAsync();
    }

    /// <summary>Re-reads the register (a sale/payment elsewhere changed it).</summary>
    public void Refresh()
    {
        if (_loaded)
        {
            _ = ReloadAsync();
        }
    }

    private async Task ReloadAsync()
    {
        var token = ++_generation;
        IsLoading = true;
        try
        {
            var found = await _service.GetInvoicesAsync();
            if (token != _generation)
            {
                return;
            }

            _invoices.Clear();
            _invoices.AddRange(found);
            Render();
        }
        catch (InvalidOperationException ex)
        {
            if (token == _generation)
            {
                _invoices.Clear();
                Render();
                _toast.Show(
                    Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(
                        ex, "بارگذاری فهرست فاکتورها ناموفق بود؛ دوباره تلاش کنید"),
                    Tone.Danger);
            }
        }
        finally
        {
            if (token == _generation)
            {
                IsLoading = false;
            }
        }
    }

    // ── Filtering + rendering ───────────────────────────────────────────────

    [RelayCommand]
    private void SetFilter(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == _filter)
        {
            return;
        }

        _filter = key;
        foreach (var filter in Filters)
        {
            filter.IsActive = filter.Key == key;
        }

        Render();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private void Render()
    {
        var query = PersianText.Normalize(SearchText);

        var matches = _invoices
            .Where(invoice => InFilter(invoice) && Matches(invoice, query))
            .OrderByDescending(invoice => invoice.IssuedAt ?? DateTime.MinValue)
            .ThenByDescending(invoice => invoice.Id, StringComparer.Ordinal)
            .ToList();

        Rows.ReplaceAll(matches.Select(summary => new SaleInvoiceLedgerRowViewModel(
            summary, OpenAsync, EditAsync, DeleteAsync)));

        HasRows = Rows.Count > 0;
        IsEmpty = Rows.Count == 0;

        var openCount = _invoices.Count(i => i.State is InvoiceState.Draft or InvoiceState.Partial);
        SummaryText = _invoices.Count == 0
            ? "بدون فاکتور"
            : $"{SalePresentation.Fa(_invoices.Count)} فاکتور · {SalePresentation.Fa(openCount)} باز";
        TotalText = SalePresentation.Money(_invoices.Sum(i => i.Total));
        DueText = SalePresentation.Money(_invoices.Sum(i => i.Due));
        OpenCountText = SalePresentation.Fa(openCount);

        foreach (var filter in Filters)
        {
            filter.CountText = SalePresentation.Fa(_invoices.Count(InFilterFor(filter.Key)));
        }
    }

    private bool InFilter(InvoiceSummary invoice) => InFilterFor(_filter)(invoice);

    private static Func<InvoiceSummary, bool> InFilterFor(string key) => key switch
    {
        "open" => invoice => invoice.State is InvoiceState.Draft or InvoiceState.Partial,
        "settled" => invoice => invoice.State == InvoiceState.Settled,
        "closed" => invoice => invoice.State is InvoiceState.Void or InvoiceState.Refunded or InvoiceState.Deleted,
        _ => _ => true,
    };

    private static bool Matches(InvoiceSummary invoice, string query) =>
        query.Length == 0 || PersianText.Contains(
            PersianText.Normalize(
                $"{invoice.Number} {invoice.Id} {invoice.OwnerName} {invoice.PetName} {invoice.Phone}"),
            query);

    // ── Row actions ─────────────────────────────────────────────────────────

    /// <summary>Opens the unified invoice sheet (read-only for a posted invoice, editable for a draft).</summary>
    private Task OpenAsync(string invoiceId) =>
        _opener.OpenAsync("Invoice", invoiceId, onChanged: OnChanged);

    /// <summary>«ویرایش» — the same sheet, opened with the intent to change the open invoice.</summary>
    private Task EditAsync(string invoiceId) => OpenAsync(invoiceId);

    /// <summary>
    /// «حذف» — asks first (this cannot be undone), then deletes the draft. The backend accepts a draft only, so
    /// a rejection is reported in plain Persian instead of a raw error code.
    /// </summary>
    private Task DeleteAsync(string invoiceId)
    {
        var row = Rows.FirstOrDefault(r => r.Id == invoiceId);
        _dialogs.ShowModal(new Shared.ConfirmDialogViewModel(
            _dialogs,
            "danger",
            "IconTrash",
            "حذف فاکتور",
            "آیا از حذف فاکتور ",
            $"«{row?.Number ?? invoiceId}»",
            " اطمینان دارید؟",
            "فاکتور و اقلام آن به‌طور کامل حذف می‌شود و قابل بازگشت نیست.",
            "بله، فاکتور را حذف کن",
            "انصراف",
            void () => _ = DeleteConfirmedAsync(invoiceId)), 480);

        return Task.CompletedTask;
    }

    private async Task DeleteConfirmedAsync(string invoiceId)
    {
        try
        {
            await _service.DeleteInvoiceAsync(invoiceId);
            _toast.Show("فاکتور حذف شد", Tone.Ok);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(
                Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(
                    ex, "حذف فاکتور ناموفق بود؛ فقط فاکتور پیش‌نویس قابل حذف است"),
                Tone.Danger);
        }

        OnChanged();
    }

    /// <summary>The sheet changed an invoice: re-read the register and tell the rest of the app.</summary>
    private void OnChanged()
    {
        _ = ReloadAsync();
        _onInvoiceChanged?.Invoke();
    }
}
