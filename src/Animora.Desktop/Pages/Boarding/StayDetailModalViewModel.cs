using System.Collections.ObjectModel;
using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// Backs the Boarding stay-detail modal (Animora-UI/assets/boarding.js
/// <c>_openDetailFor</c>/<c>_renderDetailActions</c>): the toned header with
/// status/pay/transferred pills, the entry-info strip (dates/room/rate), the
/// contacts chips, the scrollable body (stay-days timeline, care note, consumed
/// items with their add row, and the bill totals) and the status-specific
/// footer actions (finished vs. active, with the transfer / view-in-cashier
/// variant). Every value is derived the same way the reference derives it;
/// mutations flow through <see cref="IBoardingQueryService"/> (R-18).
/// </summary>
public sealed partial class StayDetailModalViewModel : ObservableObject
{
    private readonly IBoardingQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IPrintService _print;
    private readonly Action? _onChanged;
    private readonly Action<BoardingStay>? _onCheckout;
    private readonly Action? _onGotoCashier;
    private readonly Action? _onReopen;

    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    public StayDetailModalViewModel(
        IBoardingQueryService service,
        IToastService toast,
        IDialogService dialogs,
        IPrintService print,
        string stayId,
        Action? onChanged = null,
        Action<BoardingStay>? onCheckout = null,
        Action? onGotoCashier = null,
        Action? onReopen = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _print = print;
        _onChanged = onChanged;
        _onCheckout = onCheckout;
        _onGotoCashier = onGotoCashier;
        _onReopen = onReopen;
        StayId = stayId;

        _ = ModalLoadGuard.RunAsync(_toast, _dialogs, "بارگذاری جزئیات اقامت ناموفق بود؛ دوباره تلاش کنید", LoadAsync);
    }

    public string StayId { get; }

    // ── header ──────────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _petName = string.Empty;

    [ObservableProperty]
    private string _breedLine = string.Empty;

    [ObservableProperty]
    private string _speciesIconKey = "IconPaw";

    [ObservableProperty]
    private IBrush _avatarBrush = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _avatarGlyphBrush = Brushes.White;

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    [ObservableProperty]
    private IBrush _statusPillBackground = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _statusPillForeground = Brushes.Transparent;

    [ObservableProperty]
    private string _payLabel = string.Empty;

    [ObservableProperty]
    private IBrush _payPillBackground = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _payPillForeground = Brushes.Transparent;

    [ObservableProperty]
    private bool _isTransferred;

    // ── entry strip ─────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _checkInText = string.Empty;

    [ObservableProperty]
    private string _checkOutText = string.Empty;

    [ObservableProperty]
    private string _roomText = string.Empty;

    [ObservableProperty]
    private string _dailyRateText = string.Empty;

    // ── contacts ────────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _ownerName = string.Empty;

    [ObservableProperty]
    private string _ownerPhoneText = string.Empty;

    // ── stay-days timeline ──────────────────────────────────────────────────
    [ObservableProperty]
    private string _dayLabel = string.Empty;

    [ObservableProperty]
    private IBrush _progressBrush = Brushes.Transparent;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    [ObservableProperty]
    private IBrush _dayLabelBrush = Brushes.Transparent;

    public RangeObservableCollection<BoardingDayCellViewModel> DayCells { get; } = [];

    // ── note ────────────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _hasNote;

    [ObservableProperty]
    private string _noteText = string.Empty;

    // ── consumed items ──────────────────────────────────────────────────────
    public RangeObservableCollection<BoardingConsumedRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private string _itemsCountText = string.Empty;

    [ObservableProperty]
    private bool _isEditable;

    // ── totals ──────────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _stayCostLabel = string.Empty;

    [ObservableProperty]
    private string _stayCostText = string.Empty;

    [ObservableProperty]
    private string _itemsCostText = string.Empty;

    [ObservableProperty]
    private string _totalCostText = string.Empty;

    // ── footer ──────────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isDone;

    private BoardingStay _stay = null!;

    private async Task LoadAsync()
    {
        var detail = await _service.GetDetailAsync(StayId);
        if (detail is null)
        {
            // Never leave an empty modal behind: say what happened and close it (X30).
            _toast.Show("اقامت موردنظر یافت نشد", Tone.Warn);
            _dialogs.Close();
            return;
        }

        Apply(detail);
    }

    /// <summary>Renders a detail snapshot (initial load, or the detail a mutation returned — X37).</summary>
    private void Apply(StayDetail detail)
    {
        _stay = detail.Stay;
        var stay = detail.Stay;
        var items = detail.Items;

        var cls = BoardingStayClassification.Of(stay, _today);
        var nights = cls.Nights;
        var elapsed = cls.Elapsed;
        var remaining = cls.Remaining;
        var overdue = cls.IsOverdue;
        var percent = Math.Min(100, (int)Math.Round(elapsed / (double)nights * 100));

        // Header
        PetName = stay.PetName;
        BreedLine = $"{BoardingPresentation.SpeciesLabel(stay.Species)} · {stay.Breed}";
        SpeciesIconKey = BoardingPresentation.SpeciesIconKey(stay.Species);
        AvatarBrush = BoardingPresentation.AvatarBrush(stay.Tone);
        AvatarGlyphBrush = BoardingPresentation.AvatarGlyphBrush(stay.Tone);

        var status = BoardingStayCardViewModel.StatusMeta(stay, overdue, remaining);
        StatusLabel = status.Label;
        (StatusPillBackground, StatusPillForeground) = BoardingPresentation.PillBrushes(status.Tone);

        PayLabel = BoardingPresentation.PayLabel(stay.PaymentMode);
        (PayPillBackground, PayPillForeground) = BoardingPresentation.PillBrushes(
            BoardingPresentation.PayTone(stay.PaymentMode));
        IsTransferred = stay.Transferred;

        // Entry strip
        CheckInText = BoardingPresentation.Long(stay.CheckIn);
        CheckOutText = BoardingPresentation.Long(stay.CheckOut);
        RoomText = PersianNumberFormatter.ToPersianDigits(stay.Room);
        DailyRateText = BoardingPresentation.Money(stay.DailyRate);

        // Contacts
        OwnerName = stay.OwnerName;
        OwnerPhoneText = stay.OwnerPhone;

        // Stay-days timeline
        DayLabel = BuildDayLabel(stay, cls);
        ProgressBrush = BoardingPresentation.ProgressBarBrush(overdue, false, percent);
        ElapsedText = $"{BoardingPresentation.Fa(elapsed)} از {BoardingPresentation.Fa(nights)} شب گذشته";
        DayLabelBrush = BoardingPresentation.DayLabelBrush(overdue, remaining);

        // Items are counted per date once, so the day-cell build is O(nights + items)
        // with no per-day list allocation (X36).
        var itemsPerDay = new Dictionary<DateOnly, int>();
        foreach (var it in items)
        {
            itemsPerDay[it.At] = itemsPerDay.GetValueOrDefault(it.At) + 1;
        }

        var dayCells = new List<BoardingDayCellViewModel>(Math.Max(nights, 0));
        for (var i = 0; i < nights; i++)
        {
            var date = stay.CheckIn.AddDays(i);
            var isPast = i < elapsed;
            var isToday = date == _today && stay.Status == BoardingStatus.Active;
            var dayItemCount = itemsPerDay.GetValueOrDefault(date);
            var tooltip = BoardingPresentation.Long(date)
                + (dayItemCount > 0
                    ? $" — {BoardingPresentation.Fa(dayItemCount)} مورد مصرفی"
                    : string.Empty);
            dayCells.Add(new BoardingDayCellViewModel(i + 1, date, isPast, isToday, dayItemCount > 0, tooltip));
        }

        DayCells.ReplaceAll(dayCells);

        // Note
        HasNote = !string.IsNullOrWhiteSpace(stay.Note);
        NoteText = stay.Note;

        // Consumed items
        IsEditable = stay.Status == BoardingStatus.Active;
        var editable = IsEditable;
        Items.ReplaceAll(items.Select((item, i) => new BoardingConsumedRowViewModel(i, item, editable)));

        HasItems = items.Count > 0;
        ItemsCountText = $"{BoardingPresentation.Fa(items.Count)} مورد";

        // Totals
        var stayCost = (long)nights * stay.DailyRate;
        var itemsCost = items.Sum(it => (long)it.Quantity * it.Price);
        StayCostLabel = $"اقامت ({BoardingPresentation.Fa(nights)} شب × {BoardingPresentation.Money(stay.DailyRate)})";
        StayCostText = BoardingPresentation.Money(stayCost);
        ItemsCostText = BoardingPresentation.Money(itemsCost);
        TotalCostText = BoardingPresentation.Money(stayCost + itemsCost);

        IsDone = stay.Status == BoardingStatus.Done;
    }

    private static string BuildDayLabel(BoardingStay stay, BoardingStayClassification cls)
    {
        if (stay.Status == BoardingStatus.Done)
        {
            return "اقامت به پایان رسیده";
        }

        if (cls.IsOverdue)
        {
            return $"{BoardingPresentation.Fa(cls.OverdueDays)} روز عقب‌افتاده";
        }

        return $"{BoardingPresentation.Fa(cls.Elapsed)} روز گذشته · "
            + (cls.Remaining == 0 ? "امروز تخلیه" : $"{BoardingPresentation.Fa(cls.Remaining)} روز مانده");
    }

    [RelayCommand]
    private async Task RemoveItemAsync(int index)
    {
        StayDetail detail;
        try
        {
            detail = await _service.RemoveItemAsync(StayId, index);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(ex, "حذف مورد ناموفق بود؛ فهرست به‌روز شد، دوباره تلاش کنید"), Tone.Danger);
            await LoadAsync();
            return;
        }

        _toast.Show("مورد حذف شد", Tone.Warn);
        Apply(detail);
        _onChanged?.Invoke();
    }

    /// <summary>
    /// Opens the add-line picker as its own centered modal. It is the shared Sales
    /// <see cref="Animora.Desktop.Pages.Sales.CatalogModalViewModel"/> (same logic and look) fed by the boarding
    /// catalog seam (clinic goods/services priced per variant + boarding services); «بازگشت» re-opens this modal
    /// with a fresh read so the new lines show up.
    /// </summary>
    [RelayCommand]
    private void OpenCatalog()
    {
        if (!IsEditable)
        {
            return;
        }

        var stayId = StayId;
        _dialogs.ShowModal(
            new Animora.Desktop.Pages.Sales.CatalogModalViewModel(
                loadCatalog: _service.GetCatalogAsync,
                addItem: item => _service.AddItemAsync(stayId, item.Id, 1),
                toast: _toast,
                dialogs: _dialogs,
                onChanged: _onChanged,
                onBack: _onReopen,
                addedToLabel: $"اقلام «{PetName}»"),
            maxWidth: 500);
    }

    /// <summary>Opens the stay bill (fresh read, so it matches the persisted state) in the print preview (X13).</summary>
    [RelayCommand]
    private async Task Print()
    {
        try
        {
            var detail = await _service.GetDetailAsync(StayId);
            if (detail is null)
            {
                _toast.Show("اقامت پیدا نشد؛ صفحه را به‌روز کنید", Tone.Danger);
                return;
            }

            await _print.OpenWithToastAsync(
                _toast, $"boarding-{StayId}", BoardingPrintDocuments.StayBill(detail, _print.EmbeddedFontCss));
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(
                Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage(
                    ex, "چاپ صورتحساب ناموفق بود؛ دوباره تلاش کنید"),
                Tone.Danger);
        }
    }

    [RelayCommand]
    private void Checkout()
    {
        // Checkout is the single hand-off that sends the stay (room + every consumed line) to the
        // cashier's «آمادهٔ صدور» queue.
        _onCheckout?.Invoke(_stay);
    }

    [RelayCommand]
    private void GotoCashier() => _onGotoCashier?.Invoke();

    [RelayCommand]
    private void Close() => _dialogs.Close();

}
