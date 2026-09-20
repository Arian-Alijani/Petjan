using System.Collections.ObjectModel;
using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// ViewModel for the Boarding screen (پانسیون). Mirrors the reference's page
/// controller (Animora-UI/assets/boarding.js): the page-chrome section switcher
/// (مدیریت اقامت / تاریخچه / تنظیمات), the status-tab filter with live counts
/// (<c>tabMatches</c>/<c>renderTabCounts</c>), the search + species filter, the
/// stay-cards grid (<c>renderCards</c>), the real History (<c>renderHistory</c>)
/// and Settings (<c>renderSettingsRooms</c>/<c>renderSettingsSvcs</c>) content,
/// and the detail / transfer / checkout / new-stay modal flows. All data flows
/// through <see cref="IBoardingQueryService"/> (the single backend swap point,
/// R-18) — no sample data or mock branching leaks into this public shape.
/// </summary>
public sealed partial class BoardingPageViewModel : PageViewModelBase
{
    private readonly IBoardingQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    private readonly List<BoardingStay> _stays = [];
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    // Reference page state: S.tab, S.query, S.species, S.section.
    private string _tab = "all";
    private string _searchQuery = string.Empty;
    private BoardingSpecies? _species;

    // Reference history state: histQuery, histSpecies.
    private string _historyQuery = string.Empty;
    private BoardingSpecies? _historySpecies;

    public BoardingPageViewModel(IBoardingQueryService service, IToastService toast, IDialogService dialogs, INavigationService navigation)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _navigation = navigation;

        Title = "پانسیون";
        Eyebrow = "مدیریت کلینیک";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("پانسیون");

        foreach (var (key, label, icon) in new[]
                 {
                     ("board", "مدیریت اقامت", "IconHome"),
                     ("history", "تاریخچه", "IconTimeline"),
                     ("settings", "تنظیمات", "IconTools"),
                 })
        {
            Sections.Add(new BoardingSectionViewModel(key, label, icon));
        }

        foreach (var (key, label) in new[]
                 {
                     ("all", "همه"),
                     ("current", "اقدام فعلی"),
                     ("overdue", "عقب‌افتاده"),
                     ("leaving", "تخلیه امروز"),
                     ("done", "تخلیه‌شده"),
                 })
        {
            Tabs.Add(new BoardingTabViewModel(key, label));
        }

        // Correct the «current» label to the reference's exact text.
        Tabs[1] = new BoardingTabViewModel("current", "اقامت فعلی");

        foreach (var (value, label) in new (BoardingSpecies?, string)[]
                 {
                     (null, "همه گونه‌ها"),
                     (BoardingSpecies.Dog, "سگ"),
                     (BoardingSpecies.Cat, "گربه"),
                     (BoardingSpecies.Bird, "پرنده"),
                     (BoardingSpecies.Rabbit, "خرگوش"),
                 })
        {
            var icon = value is { } species ? BoardingPresentation.SpeciesIconKey(species) : null;
            SpeciesOptions.Add(new BoardingSpeciesOption(value, label, icon));
        }

        SelectedSpeciesOption = SpeciesOptions[0];
        SelectedSpeciesOption.IsActive = true;

        _ = LoadAsync();
    }

    /// <summary>board | history | settings — the reference's <c>#bd-main-seg</c> keys.</summary>
    [ObservableProperty]
    private string _activeSection = "board";

    public bool IsBoardSection => ActiveSection == "board";
    public bool IsHistorySection => ActiveSection == "history";
    public bool IsSettingsSection => ActiveSection == "settings";

    /// <summary>Tier-2 board filters (status tabs + search + actions) are only on مدیریت اقامت.</summary>
    public bool ShowBoardTools => ActiveSection == "board";

    public ObservableCollection<BoardingSectionViewModel> Sections { get; } = [];
    public ObservableCollection<BoardingTabViewModel> Tabs { get; } = [];
    public ObservableCollection<BoardingSpeciesOption> SpeciesOptions { get; } = [];
    public RangeObservableCollection<BoardingStayCardViewModel> FilteredStays { get; } = [];

    /// <summary>
    /// <see cref="FilteredStays"/> chunked into rows of three cards each. The
    /// stay grid binds to this so a single-column virtualized ListBox still
    /// renders three cards per visible row (see <see cref="BoardingStayRowViewModel"/>).
    /// </summary>
    public RangeObservableCollection<BoardingStayRowViewModel> StayRows { get; } = [];

    [ObservableProperty]
    private BoardingSpeciesOption _selectedSpeciesOption;

    public string SpeciesLabelText => SelectedSpeciesOption.Label;

    [ObservableProperty]
    private bool _isSpeciesDropdownOpen;

    [ObservableProperty]
    private string _searchQueryText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _hasStays;

    // ── History tab ─────────────────────────────────────────────────────────
    public RangeObservableCollection<BoardingHistoryCardViewModel> HistoryCards { get; } = [];

    [ObservableProperty]
    private string _historyQueryText = string.Empty;

    [ObservableProperty]
    private bool _hasHistoryQuery;

    [ObservableProperty]
    private int _historySpeciesIndex;

    [ObservableProperty]
    private string _historyCountText = string.Empty;

    [ObservableProperty]
    private bool _hasHistoryCards;

    // ── Settings tab ────────────────────────────────────────────────────────
    public ObservableCollection<BoardingRateRowViewModel> RateRows { get; } = [];
    public RangeObservableCollection<BoardingRoomTileViewModel> RoomTiles { get; } = [];
    public RangeObservableCollection<BoardingServiceRowViewModel> ServiceRows { get; } = [];
    public ObservableCollection<BoardingSettingToggleViewModel> SettingToggles { get; } = [];

    partial void OnActiveSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsBoardSection));
        OnPropertyChanged(nameof(IsHistorySection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(ShowBoardTools));
    }

    partial void OnSelectedSpeciesOptionChanged(BoardingSpeciesOption value)
    {
        _species = value.Value;
        OnPropertyChanged(nameof(SpeciesLabelText));
        RefreshCards();
    }

    partial void OnHistorySpeciesIndexChanged(int value)
    {
        _historySpecies = value switch
        {
            1 => BoardingSpecies.Dog,
            2 => BoardingSpecies.Cat,
            3 => BoardingSpecies.Bird,
            4 => BoardingSpecies.Rabbit,
            _ => null,
        };
        RefreshHistory();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        await ReloadStaysAsync();
        IsBusy = false;

        RefreshTabCounts();
        RefreshCards();
        RefreshHistory();
        BuildSettings();
    }

    private async Task ReloadStaysAsync()
    {
        _stays.Clear();
        _stays.AddRange(await _service.GetStaysAsync());
    }

    private void RefreshTabCounts()
    {
        foreach (var tab in Tabs)
        {
            var count = _stays.Count(stay => TabMatches(stay, tab.Key));
            tab.CountText = PersianNumberFormatter.ToPersianDigits(count);
            tab.IsActive = tab.Key == _tab;
        }
    }

    private bool TabMatches(BoardingStay stay, string tab)
    {
        var overdue = BoardingStayCardViewModel.IsStayOverdue(stay, _today);
        var leaving = BoardingStayCardViewModel.IsStayLeaving(stay, _today);

        return tab switch
        {
            "all" => true,
            "current" => stay.Status == BoardingStatus.Active && !leaving && !overdue,
            "overdue" => overdue,
            "leaving" => leaving && !overdue,
            "done" => stay.Status == BoardingStatus.Done,
            _ => true,
        };
    }

    private List<BoardingStay> Filtered()
    {
        var query = PersianText.Normalize(_searchQuery);

        return _stays
            .Where(stay =>
            {
                if (!TabMatches(stay, _tab)) return false;
                if (_species is { } species && stay.Species != species) return false;
                if (query.Length > 0)
                {
                    var hay = PersianText.Normalize(
                        $"{stay.PetName} {stay.OwnerName} {stay.OwnerPhone} {stay.Breed} {stay.Room}");
                    if (!hay.Contains(query, StringComparison.Ordinal)) return false;
                }
                return true;
            })
            .OrderBy(stay => BoardingStayCardViewModel.IsStayOverdue(stay, _today) ? 0 : 1)
            .ThenBy(stay => stay.Status == BoardingStatus.Done ? 1 : 0)
            .ThenBy(stay => stay.CheckOut)
            .ToList();
    }

    private void RefreshCards()
    {
        var rows = Filtered();
        var cards = rows.Select(stay => new BoardingStayCardViewModel(stay, _today)).ToList();
        FilteredStays.ReplaceAll(cards);

        var grouped = new List<BoardingStayRowViewModel>();
        for (var i = 0; i < cards.Count; i += 3)
        {
            grouped.Add(new BoardingStayRowViewModel(cards.GetRange(i, Math.Min(3, cards.Count - i))));
        }
        StayRows.ReplaceAll(grouped);

        HasStays = rows.Count > 0;
    }

    private void RefreshHistory()
    {
        var query = PersianText.Normalize(_historyQuery);

        var rows = _stays
            .Where(stay =>
            {
                if (_historySpecies is { } species && stay.Species != species) return false;
                if (query.Length > 0)
                {
                    var hay = PersianText.Normalize(
                        $"{stay.PetName} {stay.OwnerName} {stay.Breed} {stay.Room}");
                    if (!hay.Contains(query, StringComparison.Ordinal)) return false;
                }
                return true;
            })
            .OrderByDescending(stay => stay.CheckIn)
            .ToList();

        HistoryCountText = $"{PersianNumberFormatter.ToPersianDigits(rows.Count)} اقامت";

        HistoryCards.ReplaceAll(rows.Select(stay =>
        {
            var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
            var elapsed = Math.Min(Math.Max(0, _today.DayNumber - stay.CheckIn.DayNumber), nights);
            var percent = Math.Min(100, (int)Math.Round(elapsed / (double)nights * 100));
            var overdue = BoardingStayCardViewModel.IsStayOverdue(stay, _today);
            var remaining = stay.Status == BoardingStatus.Done ? 0 : Math.Max(0, stay.CheckOut.DayNumber - _today.DayNumber);
            var status = BoardingStayCardViewModel.StatusMeta(stay, overdue, remaining);
            var total = (long)nights * stay.DailyRate + stay.ExtraItemsCost;
            return new BoardingHistoryCardViewModel(
                stay, nights, percent, status.Label, status.Tone, BoardingPresentation.Money(total));
        }));

        HasHistoryCards = rows.Count > 0;
    }

    private void BuildSettings()
    {
        if (RateRows.Count == 0)
        {
            RateRows.Add(new BoardingRateRowViewModel("IconDog", "سگ — اتاق معمولی", "بر اساس اندازه — متوسط", "۸۰۰٬۰۰۰"));
            RateRows.Add(new BoardingRateRowViewModel("IconDog", "سگ — سوئیت VIP", "فضای بزرگ‌تر", "۱٬۲۰۰٬۰۰۰"));
            RateRows.Add(new BoardingRateRowViewModel("IconCat", "گربه — اتاق معمولی", "اتاق استاندارد", "۵۵۰٬۰۰۰"));
            RateRows.Add(new BoardingRateRowViewModel("IconBird", "پرنده", "قفس اختصاصی", "۳۲۰٬۰۰۰"));
            RateRows.Add(new BoardingRateRowViewModel("IconRabbit", "خرگوش / جونده", "قفس کوچک", "۳۸۰٬۰۰۰"));

            SettingToggles.Add(new BoardingSettingToggleViewModel("پیامک پذیرش", "پیامک تأیید پذیرش", "پس از ثبت اقامت به مالک ارسال شود", true));
            SettingToggles.Add(new BoardingSettingToggleViewModel("یادآور تخلیه", "یادآور یک روز قبل از تخلیه", "پیامک به مالک برای آمادگی تحویل", true));
            SettingToggles.Add(new BoardingSettingToggleViewModel("انتقال خودکار", "انتقال خودکار فاکتور به صندوق", "پس از تخلیه فاکتور آماده ارسال به صندوق", false));
            SettingToggles.Add(new BoardingSettingToggleViewModel("هشدار ظرفیت", "هشدار ظرفیت پر", "اعلان هنگام تکمیل ظرفیت پانسیون", true));
        }

        _ = RefreshRoomsAsync();
    }

    private async Task RefreshRoomsAsync()
    {
        RoomTiles.ReplaceAll(
            (await _service.GetRoomsAsync()).Select(room => new BoardingRoomTileViewModel(room)));

        ServiceRows.ReplaceAll(
            (await _service.GetServicesAsync()).Select(service => new BoardingServiceRowViewModel(service)));
    }

    private async Task RefreshAfterChangeAsync()
    {
        await ReloadStaysAsync();
        RefreshTabCounts();
        RefreshCards();
        RefreshHistory();
        await RefreshRoomsAsync();
    }

    [RelayCommand]
    private void SetSection(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        ActiveSection = key;
        IsSpeciesDropdownOpen = false;
    }

    [RelayCommand]
    private void SetTab(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _tab = key;
        RefreshTabCounts();
        RefreshCards();
    }

    [RelayCommand]
    private void ToggleSpecies() => IsSpeciesDropdownOpen = !IsSpeciesDropdownOpen;

    [RelayCommand]
    private void SetSpecies(BoardingSpeciesOption? option)
    {
        if (option is null) return;
        SelectedSpeciesOption = option;
        IsSpeciesDropdownOpen = false;
    }

    public void ApplySearch(string? text)
    {
        _searchQuery = text?.Trim() ?? string.Empty;
        RefreshCards();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQueryText = string.Empty;
        HasSearchValue = false;
        ApplySearch(string.Empty);
    }

    public void ApplyHistorySearch(string? text)
    {
        _historyQuery = text?.Trim() ?? string.Empty;
        RefreshHistory();
    }

    [RelayCommand]
    private void ClearHistorySearch()
    {
        HistoryQueryText = string.Empty;
        HasHistoryQuery = false;
        ApplyHistorySearch(string.Empty);
    }

    // ── modal flows ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenStayDetail(BoardingStayCardViewModel? card)
    {
        if (card is null) return;
        ShowDetail(card.Stay.Id);
    }

    [RelayCommand]
    private void OpenHistoryDetail(BoardingHistoryCardViewModel? card)
    {
        if (card is null) return;
        ShowDetail(card.Stay.Id);
    }

    private void ShowDetail(string stayId) =>
        _dialogs.ShowModal(
            new StayDetailModalViewModel(
                _service, _toast, _dialogs, stayId,
                onChanged: () => _ = RefreshAfterChangeAsync(),
                onTransfer: stay => ShowTransfer(stay),
                onCheckout: stay => ShowCheckout(stay),
                onGotoCashier: GotoCashier),
            maxWidth: 680);

    /// <summary>
    /// Reference (boarding.js <c>goto-cashier</c>): close the detail modal and
    /// navigate to the cashier/treasury screen. Phase 29 wires this to the real
    /// <see cref="Pages.Cashier.CashierPageViewModel"/> (the reference's target
    /// is <c>cashier.html</c>) instead of the old placeholder toast.
    /// </summary>
    private void GotoCashier()
    {
        _dialogs.Close();
        _navigation.NavigateTo<Animora.Desktop.Pages.Cashier.CashierPageViewModel>();
    }

    private void ShowTransfer(BoardingStay stay)
    {
        _ = ShowTransferAsync(stay);
    }

    private async Task ShowTransferAsync(BoardingStay stay)
    {
        var detail = await _service.GetDetailAsync(stay.Id);
        var items = detail?.Items ?? [];
        _dialogs.ShowModal(
            new TransferModalViewModel(_service, _toast, _dialogs, stay, items,
                onTransferred: () => _ = RefreshAfterChangeAsync()),
            maxWidth: 580);
    }

    private void ShowCheckout(BoardingStay stay) =>
        _dialogs.ShowModal(
            new CheckoutModalViewModel(_service, _toast, _dialogs, stay,
                onCheckedOut: () => _ = RefreshAfterChangeAsync()),
            maxWidth: 520);

    [RelayCommand]
    private void OpenNewStay() =>
        _dialogs.ShowModal(
            new NewStayModalViewModel(_service, _toast, _dialogs,
                onCreated: () => _ = RefreshAfterChangeAsync()),
            maxWidth: 580);

    // ── settings actions (reference bdSettingsSave/bdAddRoom/bdAddService/bdEditSvc) ──

    [RelayCommand]
    private void SaveRates() => _toast.Show("نرخ‌های شبانه ذخیره شد (نمونه طراحی)");

    [RelayCommand]
    private void SaveGeneral() => _toast.Show("تنظیمات عمومی ذخیره شد (نمونه طراحی)");

    [RelayCommand]
    private void AddRoom() => _toast.Show("افزودن اتاق (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void AddService() => _toast.Show("افزودن خدمت (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void EditService(BoardingServiceRowViewModel? service)
    {
        if (service is null) return;
        _toast.Show($"ویرایش «{service.Title}» (نمونه طراحی)", Tone.Info);
    }
}
