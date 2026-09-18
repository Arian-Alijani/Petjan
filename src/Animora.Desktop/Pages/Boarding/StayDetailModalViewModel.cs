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
    private readonly Action? _onChanged;
    private readonly Action<BoardingStay>? _onTransfer;
    private readonly Action<BoardingStay>? _onCheckout;

    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    public StayDetailModalViewModel(
        IBoardingQueryService service,
        IToastService toast,
        IDialogService dialogs,
        string stayId,
        Action? onChanged = null,
        Action<BoardingStay>? onTransfer = null,
        Action<BoardingStay>? onCheckout = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _onChanged = onChanged;
        _onTransfer = onTransfer;
        _onCheckout = onCheckout;
        StayId = stayId;

        _ = LoadAsync();
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

    public ObservableCollection<BoardingDayCellViewModel> DayCells { get; } = [];

    // ── note ────────────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _hasNote;

    [ObservableProperty]
    private string _noteText = string.Empty;

    // ── consumed items ──────────────────────────────────────────────────────
    public ObservableCollection<BoardingConsumedRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private string _itemsCountText = string.Empty;

    [ObservableProperty]
    private bool _isEditable;

    public ObservableCollection<BoardingAddGroupViewModel> AddGroups { get; } = [];

    [ObservableProperty]
    private BoardingAddOptionViewModel? _selectedAddOption;

    /// <summary>
    /// The add-row picker's button text — the reference's select placeholder
    /// «افزودن خدمت یا کالا…» until a line is chosen, then the option's text.
    /// </summary>
    public string AddSelectionText => SelectedAddOption?.Text ?? "افزودن خدمت یا کالا…";

    partial void OnSelectedAddOptionChanged(BoardingAddOptionViewModel? value) =>
        OnPropertyChanged(nameof(AddSelectionText));

    /// <summary>Reference optgroup dropdown visibility (data-driven, toggled by the button).</summary>
    [ObservableProperty]
    private bool _isAddDropdownOpen;

    [ObservableProperty]
    private string _addQuantityText = "1";

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
            return;
        }

        _stay = detail.Stay;
        var stay = detail.Stay;
        var items = detail.Items;

        var nights = Math.Max(1, Days(stay.CheckOut, stay.CheckIn));
        var elapsed = Math.Min(Math.Max(0, Days(_today, stay.CheckIn)), nights);
        var remaining = stay.Status == BoardingStatus.Done ? 0 : Math.Max(0, Days(stay.CheckOut, _today));
        var overdue = stay.Status == BoardingStatus.Active && stay.CheckOut < _today;
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
        DayLabel = BuildDayLabel(stay, overdue, remaining, elapsed);
        ProgressBrush = BoardingPresentation.ProgressBarBrush(overdue, false, percent);
        ElapsedText = $"{BoardingPresentation.Fa(elapsed)} از {BoardingPresentation.Fa(nights)} شب گذشته";
        DayLabelBrush = BoardingPresentation.DayLabelBrush(overdue, remaining);

        DayCells.Clear();
        for (var i = 0; i < nights; i++)
        {
            var date = stay.CheckIn.AddDays(i);
            var isPast = i < elapsed;
            var isToday = date == _today && stay.Status == BoardingStatus.Active;
            var dayItems = items.Where(it => it.At == date).ToList();
            var tooltip = BoardingPresentation.Long(date)
                + (dayItems.Count > 0
                    ? $" — {BoardingPresentation.Fa(dayItems.Count)} مورد مصرفی"
                    : string.Empty);
            DayCells.Add(new BoardingDayCellViewModel(i + 1, date, isPast, isToday, dayItems.Count > 0, tooltip));
        }

        // Note
        HasNote = !string.IsNullOrWhiteSpace(stay.Note);
        NoteText = stay.Note;

        // Consumed items
        IsEditable = stay.Status == BoardingStatus.Active;
        Items.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            Items.Add(new BoardingConsumedRowViewModel(i, items[i], IsEditable));
        }

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

    private static string BuildDayLabel(BoardingStay stay, bool overdue, int remaining, int elapsed)
    {
        if (stay.Status == BoardingStatus.Done)
        {
            return "اقامت به پایان رسیده";
        }

        if (overdue)
        {
            return $"{BoardingPresentation.Fa(Math.Abs(remaining))} روز عقب‌افتاده";
        }

        return $"{BoardingPresentation.Fa(elapsed)} روز گذشته · "
            + (remaining == 0 ? "امروز تخلیه" : $"{BoardingPresentation.Fa(remaining)} روز مانده");
    }

    /// <summary>Reference: the add-row select opens its grouped option list.</summary>
    [RelayCommand]
    private void ToggleAddDropdown() => IsAddDropdownOpen = !IsAddDropdownOpen;

    [RelayCommand]
    private void SelectAddOption(BoardingAddOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        SelectedAddOption = option;
        IsAddDropdownOpen = false;
    }

    [RelayCommand]
    private async Task RemoveItemAsync(int index)
    {
        await _service.RemoveItemAsync(StayId, index);
        _toast.Show("مورد حذف شد (نمونه طراحی)", Tone.Warn);
        await LoadAsync();
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private async Task AddItemAsync()
    {
        if (SelectedAddOption is null)
        {
            _toast.Show("ابتدا یک مورد انتخاب کنید", Tone.Danger);
            return;
        }

        var quantity = int.TryParse(PersianNumberFormatter.ToAsciiDigits(AddQuantityText), out var q) ? q : 1;
        await _service.AddItemAsync(StayId, SelectedAddOption.Item.Ref, Math.Max(1, quantity));
        _toast.Show($"«{SelectedAddOption.Item.Title}» به اقلام «{PetName}» اضافه شد (نمونه طراحی)");
        SelectedAddOption = null;
        AddQuantityText = "1";
        await LoadAsync();
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void Print() => _toast.Show("چاپ صورتحساب — نسخه بعدی (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void Transfer()
    {
        // Reference: close(); openTransferModal(st) — the dialog host replaces
        // this modal's content with the transfer modal.
        _onTransfer?.Invoke(_stay);
    }

    [RelayCommand]
    private void Checkout()
    {
        // Reference: close(); openCheckoutModal(st).
        _onCheckout?.Invoke(_stay);
    }

    [RelayCommand]
    private void GotoCashier() =>
        _toast.Show("انتقال به صندوق در فاز ۲۲ (Phase 22) ساخته می‌شود", Tone.Info);

    [RelayCommand]
    private void Close() => _dialogs.Close();

    private static int Days(DateOnly to, DateOnly from) => to.DayNumber - from.DayNumber;
}
