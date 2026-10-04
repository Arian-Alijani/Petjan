using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// The «نرخ خدمات» tab: lists every clinic and boarding service with its rate and lets the manager add a
/// service, change a rate or title, and switch a service on/off. All data flows through
/// <see cref="IServiceRateService"/>; the list is small (tens of rows) so search/filter run in memory over
/// the loaded snapshot. Loads lazily on first activation (PERFORMANCE.md §12) and after every write.
/// </summary>
public sealed partial class ServiceRatesViewModel : ObservableObject
{
    private readonly IServiceRateService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IDataChangeNotifier _changes;
    private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(160));
    private List<ServiceRate> _all = [];
    private string _filter = "all";
    private bool _loaded;
    private int _generation;

    public ServiceRatesViewModel(IServiceRateService service, IToastService toast, IDialogService dialogs,
        IDataChangeNotifier changes)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _changes = changes;
        Filters =
        [
            new ServiceRateFilterViewModel("all", "همه"),
            new ServiceRateFilterViewModel("clinic", "خدمات کلینیک"),
            new ServiceRateFilterViewModel("boarding", "خدمات پانسیون"),
        ];
        Filters[0].IsActive = true;
    }

    public IReadOnlyList<ServiceRateFilterViewModel> Filters { get; }

    public RangeObservableCollection<ServiceRateRowViewModel> Rows { get; } = [];

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

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public string SearchPlaceholder => "جستجو در نام خدمت…";

    /// <summary>Loads the list the first time the tab becomes visible.</summary>
    public void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _ = ReloadAsync();
    }

    /// <summary>Re-reads the catalogue (also called when another page changed services).</summary>
    public void Refresh()
    {
        if (_loaded)
        {
            _ = ReloadAsync();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        HasSearchValue = !string.IsNullOrWhiteSpace(value);
        _searchDebounce.Debounce(ApplyFilter);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void SetFilter(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == _filter)
        {
            return;
        }

        _filter = key;
        foreach (var chip in Filters)
        {
            chip.IsActive = chip.Key == key;
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void AddService()
    {
        var modal = new ServiceRateEditModalViewModel(_service, _toast, _dialogs, existing: null, OnSaved);
        _dialogs.ShowModal(modal, 520);
    }

    internal void Edit(ServiceRate rate)
    {
        var modal = new ServiceRateEditModalViewModel(_service, _toast, _dialogs, rate, OnSaved);
        _dialogs.ShowModal(modal, 520);
    }

    internal async Task ToggleActiveAsync(ServiceRate rate)
    {
        try
        {
            await _service.UpdateAsync(rate with { IsActive = !rate.IsActive });
            OnSaved();
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "تغییر وضعیت خدمت ناموفق بود"), Tone.Danger);
        }
    }

    private void OnSaved()
    {
        _changes.Notify(DataDomain.Sales, DataDomain.Boarding, DataDomain.Visits, DataDomain.Cashier);
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var token = ++_generation;
        IsLoading = true;
        try
        {
            var rows = await _service.GetAsync();
            if (token != _generation)
            {
                return; // a newer reload superseded this one
            }

            _all = rows.ToList();
            ApplyFilter();
        }
        catch (InvalidOperationException ex)
        {
            if (token == _generation)
            {
                _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "بارگذاری نرخ خدمات ناموفق بود"), Tone.Danger);
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

    private void ApplyFilter()
    {
        var term = SearchText.Trim();
        var visible = _all.Where(r => _filter switch
            {
                "clinic" => r.Source == ServiceRateSource.Clinic,
                "boarding" => r.Source == ServiceRateSource.Boarding,
                _ => true,
            })
            .Where(r => term.Length == 0 || r.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(r => new ServiceRateRowViewModel(r, this))
            .ToList();

        Rows.ReplaceAll(visible);
        HasRows = visible.Count > 0;
        IsEmpty = !HasRows;
        SummaryText = $"{SalePresentation.Fa(visible.Count)} خدمت · {SalePresentation.Fa(visible.Count(v => v.IsActive))} فعال";
    }
}

/// <summary>A source filter chip («همه» / «خدمات کلینیک» / «خدمات پانسیون»).</summary>
public sealed partial class ServiceRateFilterViewModel(string key, string label) : ObservableObject
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>One service row: title, source badge, billing basis, rate, active toggle and edit button.</summary>
public sealed class ServiceRateRowViewModel
{
    private readonly ServiceRatesViewModel _owner;

    public ServiceRateRowViewModel(ServiceRate rate, ServiceRatesViewModel owner)
    {
        Rate = rate;
        _owner = owner;
        EditCommand = new RelayCommand(() => _owner.Edit(Rate));
        ToggleCommand = new AsyncRelayCommand(() => _owner.ToggleActiveAsync(Rate));
    }

    public ServiceRate Rate { get; }

    public string Title => Rate.Title;

    public bool IsActive => Rate.IsActive;

    public bool IsInactive => !Rate.IsActive;

    public string SourceText => Rate.Source == ServiceRateSource.Boarding ? "پانسیون" : "کلینیک";

    public bool IsBoarding => Rate.Source == ServiceRateSource.Boarding;

    public string BillingText => Rate.Billing switch
    {
        ServiceRateBilling.PerStay => "هر اقامت",
        ServiceRateBilling.PerDay => "روزانه",
        ServiceRateBilling.PerUse => "هر بار",
        _ => string.Empty,
    };

    public string PriceText => SalePresentation.Money(Rate.Price) + " تومان";

    public string ToggleText => Rate.IsActive ? "غیرفعال‌سازی" : "فعال‌سازی";

    public RelayCommand EditCommand { get; }

    public AsyncRelayCommand ToggleCommand { get; }
}
