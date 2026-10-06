using Animora.Contracts.Cheques;
using Animora.Contracts.Diagnostics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Cheques;

/// <summary>«چک‌ها»: server-paged list of received/issued cheques with direction + lifecycle filters, debounced search and the register / detail modals.</summary>
public sealed partial class ChequesPageViewModel : PageViewModelBase
{
    private const int PageSize = 25;
    private readonly IChequesService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IDataChangeNotifier _changes;
    private readonly IStoreReadinessGate _readiness;
    private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(250));
    private int _generation;
    private int _total;
    private string _direction = "all";
    private string _scope = "open";

    public ChequesPageViewModel(IChequesService service, IStoreReadinessGate readiness, IToastService toast,
        IDialogService dialogs, IDataChangeNotifier changes)
    {
        _service = service;
        _readiness = readiness;
        _toast = toast;
        _dialogs = dialogs;
        _changes = changes;

        Title = "چک‌ها";
        Eyebrow = "مدیریت مالی";
        Breadcrumbs.Add("کسب و کار / چک‌ها");

        DirectionChips.AddRange([new("all", "همه"), new("in", "دریافتی"), new("out", "پرداختی")]);
        ScopeChips.AddRange([new("open", "در جریان"), new("attention", "برگشتی"), new("closed", "بسته‌شده"), new("all", "همه")]);
        MarkChips();

        UseLoadStatus(ReloadAsync);
        RefreshOnDataChange(changes, () => _ = ReloadAsync(), DataDomain.Cheques, DataDomain.Sales, DataDomain.Cashier);
    }

    public RangeObservableCollection<ChequeChipViewModel> DirectionChips { get; } = [];
    public RangeObservableCollection<ChequeChipViewModel> ScopeChips { get; } = [];
    public RangeObservableCollection<ChequeRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private string _inboundText = "—";
    [ObservableProperty] private string _inboundFoot = string.Empty;
    [ObservableProperty] private string _outboundText = "—";
    [ObservableProperty] private string _outboundFoot = string.Empty;
    [ObservableProperty] private string _bouncedText = "—";
    [ObservableProperty] private string _bouncedFoot = string.Empty;

    public int PageCount => _total == 0 ? 1 : (int)Math.Ceiling(_total / (double)PageSize);
    public bool ShowPagination => HasRows && PageCount > 1;
    public bool CanGoPrevious => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < PageCount;
    public bool HasSearchValue => SearchText.Length > 0;
    public string PageIndicatorText =>
        $"صفحه {PersianNumberFormatter.ToPersianDigits(CurrentPage)} از {PersianNumberFormatter.ToPersianDigits(PageCount)}";
    public string CountText => PersianNumberFormatter.ToPersianDigits(_total) + " چک";

    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchValue));
        _searchDebounce.Debounce(() =>
        {
            CurrentPage = 1;
            _ = ReloadAsync();
        });
    }

    private Task ReloadAsync() => TrackLoadAsync(LoadAsync);

    private async Task LoadAsync()
    {
        await _readiness.WaitAsync();
        var token = ++_generation;
        var filter = new ChequeListFilter(
            _direction switch { "in" => ChequeDirectionDto.Inbound, "out" => ChequeDirectionDto.Outbound, _ => null },
            _scope switch { "open" => ChequeScopeDto.Open, "attention" => ChequeScopeDto.Attention, "closed" => ChequeScopeDto.Closed, _ => ChequeScopeDto.All },
            SearchText, CurrentPage, PageSize);
        var page = await _service.GetListAsync(filter);
        var summary = await _service.GetSummaryAsync();
        if (token != _generation) return; // a newer query superseded this one

        _total = page.TotalCount;
        var clamped = Math.Clamp(CurrentPage, 1, PageCount);
        if (clamped != CurrentPage)
        {
            CurrentPage = clamped;
            await LoadAsync();
            return;
        }

        Rows.ReplaceAll(page.Items.Select(c => new ChequeRowViewModel(c)));
        HasRows = Rows.Count > 0;
        InboundText = ChequesPresentation.Money(summary.InboundOpenMinor);
        InboundFoot = PersianNumberFormatter.ToPersianDigits(summary.InboundOpenCount) + " چک در جریان";
        OutboundText = ChequesPresentation.Money(summary.OutboundOpenMinor);
        OutboundFoot = PersianNumberFormatter.ToPersianDigits(summary.OutboundOpenCount) + " چک در جریان";
        BouncedText = ChequesPresentation.Money(summary.BouncedMinor);
        BouncedFoot = PersianNumberFormatter.ToPersianDigits(summary.BouncedCount) + " چک نیازمند پیگیری";
        NotifyPaging();
    }

    private void NotifyPaging()
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(ShowPagination));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(PageIndicatorText));
        OnPropertyChanged(nameof(CountText));
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
    }

    private void MarkChips()
    {
        foreach (var c in DirectionChips) c.IsActive = c.Key == _direction;
        foreach (var c in ScopeChips) c.IsActive = c.Key == _scope;
    }

    [RelayCommand]
    private void SetDirection(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == _direction) return;
        _direction = key;
        MarkChips();
        CurrentPage = 1;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void SetScope(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == _scope) return;
        _scope = key;
        MarkChips();
        CurrentPage = 1;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage()
    {
        CurrentPage++;
        _ = ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage()
    {
        CurrentPage--;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void OpenNew() =>
        _dialogs.ShowModal(new ChequeRegisterModalViewModel(_service, _toast, _dialogs, OnChanged), 600);

    [RelayCommand]
    private void OpenDetail(ChequeRowViewModel? row)
    {
        if (row is null) return;
        _dialogs.ShowModal(new ChequeDetailModalViewModel(_service, _toast, _dialogs, row.Id, OnChanged), 600);
    }

    private void OnChanged()
    {
        _changes.Notify(DataDomain.Cheques, DataDomain.Finance, DataDomain.Dashboard);
        _ = ReloadAsync();
    }
}
