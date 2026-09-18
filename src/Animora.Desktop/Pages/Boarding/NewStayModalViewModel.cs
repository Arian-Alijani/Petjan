using System.Collections.ObjectModel;
using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// Backs the Boarding «ثبت اقامت جدید» modal (Animora-UI/assets/boarding.js
/// <c>openNewStayModal</c>): the existing-file / manual entry mode switch, the
/// patient autocomplete (or the manual pet fields), the stay form (room / rate
/// / check-in / check-out), the payment-mode radios and the care note. Submits
/// through <see cref="IBoardingQueryService.CreateStayAsync"/> (R-18).
/// </summary>
public sealed partial class NewStayModalViewModel : ObservableObject
{
    private readonly IBoardingQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onCreated;

    private readonly List<BoardingKnownPetViewModel> _allPets = [];

    public NewStayModalViewModel(
        IBoardingQueryService service,
        IToastService toast,
        IDialogService dialogs,
        Action? onCreated = null)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _onCreated = onCreated;

        PaymentOptions.Add(new BoardingPaymentOptionViewModel(BoardingPaymentMode.Postpaid, "پس‌پرداخت", "تسویه موقع تحویل") { IsSelected = true });
        PaymentOptions.Add(new BoardingPaymentOptionViewModel(BoardingPaymentMode.Prepaid, "پیش‌پرداخت", "قبل از اقامت"));

        _ = LoadAsync();
    }

    // ── mode switch ─────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isExistingMode = true;

    public bool IsManualMode => !IsExistingMode;

    [RelayCommand]
    private void SetMode(string? mode)
    {
        IsExistingMode = mode != "manual";
        if (!IsExistingMode)
        {
            // Leaving the existing-file picker clears any selection (reference
            // resets its state when toggling modes).
            SelectedPet = null;
            PatientQueryText = string.Empty;
            IsPickerVisible = true;
        }
    }

    // ── existing-file picker ────────────────────────────────────────────────
    public ObservableCollection<BoardingKnownPetViewModel> KnownPets { get; } = [];

    [ObservableProperty]
    private string _patientQueryText = string.Empty;

    [ObservableProperty]
    private bool _isPickerVisible = true;

    [ObservableProperty]
    private BoardingKnownPetViewModel? _selectedPet;

    [ObservableProperty]
    private bool _hasSelectedPet;

    [ObservableProperty]
    private string _selectedPetIconKey = "IconPaw";

    [ObservableProperty]
    private string _selectedPetName = string.Empty;

    [ObservableProperty]
    private string _selectedPetSpecies = string.Empty;

    [ObservableProperty]
    private string _selectedPetSub = string.Empty;

    partial void OnPatientQueryTextChanged(string value) => ApplyPetFilter();

    partial void OnSelectedPetChanged(BoardingKnownPetViewModel? value)
    {
        HasSelectedPet = value is not null;
        if (value is null)
        {
            return;
        }

        SelectedPetIconKey = value.SpeciesIconKey;
        SelectedPetName = value.PetName;
        SelectedPetSpecies = value.SpeciesLabel;
        SelectedPetSub = $"{value.OwnerName} · {value.Pet.OwnerPhone}";
    }

    private void ApplyPetFilter()
    {
        var query = PersianText.Normalize(PatientQueryText);
        KnownPets.Clear();
        foreach (var pet in _allPets)
        {
            if (query.Length == 0 || pet.SearchText.Contains(query, StringComparison.Ordinal))
            {
                KnownPets.Add(pet);
            }
        }
    }

    [RelayCommand]
    private void PickPet(BoardingKnownPetViewModel? pet)
    {
        if (pet is null)
        {
            return;
        }

        SelectedPet = pet;
        IsPickerVisible = false;
    }

    [RelayCommand]
    private void ClearPet()
    {
        SelectedPet = null;
        IsPickerVisible = true;
        PatientQueryText = string.Empty;
    }

    // ── manual pet fields ───────────────────────────────────────────────────
    [ObservableProperty]
    private string _manualName = string.Empty;

    [ObservableProperty]
    private string _manualBreed = string.Empty;

    [ObservableProperty]
    private string _manualOwner = string.Empty;

    [ObservableProperty]
    private string _manualPhone = string.Empty;

    public ObservableCollection<BoardingSpeciesOption> SpeciesOptions { get; } = [];

    [ObservableProperty]
    private BoardingSpeciesOption? _selectedSpeciesOption;

    // ── stay form ───────────────────────────────────────────────────────────
    public ObservableCollection<BoardingRoomOptionViewModel> RoomOptions { get; } = [];

    [ObservableProperty]
    private BoardingRoomOptionViewModel? _selectedRoomOption;

    [ObservableProperty]
    private string _dailyRateText = "800000";

    [ObservableProperty]
    private string _checkInText = string.Empty;

    [ObservableProperty]
    private string _checkOutText = string.Empty;

    public ObservableCollection<BoardingPaymentOptionViewModel> PaymentOptions { get; } = [];

    [ObservableProperty]
    private string _noteText = string.Empty;

    private async Task LoadAsync()
    {
        foreach (var pet in await _service.GetKnownPetsAsync())
        {
            _allPets.Add(new BoardingKnownPetViewModel(pet));
        }

        ApplyPetFilter();

        // Species catalog — reference filters out 'other' for the manual picker.
        foreach (var (value, label) in new (BoardingSpecies, string)[]
                 {
                     (BoardingSpecies.Dog, "سگ"),
                     (BoardingSpecies.Cat, "گربه"),
                     (BoardingSpecies.Bird, "پرنده"),
                     (BoardingSpecies.Rabbit, "خرگوش"),
                     (BoardingSpecies.Rodent, "جونده"),
                 })
        {
            SpeciesOptions.Add(new BoardingSpeciesOption(value, label, BoardingPresentation.SpeciesIconKey(value)));
        }

        SelectedSpeciesOption = SpeciesOptions[0];

        // Rooms
        foreach (var room in await _service.GetRoomsAsync())
        {
            RoomOptions.Add(new BoardingRoomOptionViewModel(room));
        }

        SelectedRoomOption = RoomOptions.FirstOrDefault();

        // Reference defaults the check-in to today's Jalali date.
        CheckInText = BoardingPresentation.Short(DateOnly.FromDateTime(DateTime.Today));
    }

    [RelayCommand]
    private void ChoosePayment(BoardingPaymentOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var payment in PaymentOptions)
        {
            payment.IsSelected = ReferenceEquals(payment, option);
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        string petName, breed, owner, phone, patientId;
        BoardingSpecies species;

        if (IsExistingMode)
        {
            if (SelectedPet is null)
            {
                _toast.Show("یک بیمار از فهرست انتخاب کنید", Tone.Danger);
                return;
            }

            petName = SelectedPet.Pet.PetName;
            species = SelectedPet.Pet.Species;
            breed = SelectedPet.Pet.Breed;
            owner = SelectedPet.Pet.OwnerName;
            phone = SelectedPet.Pet.OwnerPhone;
            patientId = SelectedPet.Pet.PatientId;
        }
        else
        {
            petName = ManualName.Trim();
            owner = ManualOwner.Trim();
            if (petName.Length == 0 || owner.Length == 0)
            {
                _toast.Show("نام حیوان و مالک الزامی است", Tone.Danger);
                return;
            }

            species = SelectedSpeciesOption?.Value ?? BoardingSpecies.Dog;
            breed = ManualBreed.Trim().Length > 0 ? ManualBreed.Trim() : "نامشخص";
            phone = ManualPhone.Trim().Length > 0 ? ManualPhone.Trim() : "—";
            patientId = string.Empty;
        }

        var checkIn = BoardingPresentation.ParseJalali(CheckInText);
        var checkOut = BoardingPresentation.ParseJalali(CheckOutText);
        if (checkIn is null || checkOut is null)
        {
            _toast.Show("تاریخ ورود و خروج را با قالب ۱۴۰۴/۰۶/۰۱ وارد کنید", Tone.Danger);
            return;
        }

        if (checkOut <= checkIn)
        {
            _toast.Show("تاریخ خروج باید پس از ورود باشد", Tone.Danger);
            return;
        }

        var rate = long.TryParse(PersianNumberFormatter.ToAsciiDigits(DailyRateText), out var parsed) ? parsed : 0;
        var paymentMode = PaymentOptions.FirstOrDefault(p => p.IsSelected)?.Mode ?? BoardingPaymentMode.Postpaid;
        var room = SelectedRoomOption?.Room.Id ?? RoomOptions.FirstOrDefault()?.Room.Id ?? "A-1";

        var request = new NewStayRequest(
            petName, species, breed, owner, phone, patientId, room,
            checkIn.Value, checkOut.Value, Math.Max(0, rate), paymentMode, NoteText);

        var stay = await _service.CreateStayAsync(request);

        var nights = Math.Max(1, stay.CheckOut.DayNumber - stay.CheckIn.DayNumber);
        _dialogs.Close();
        _toast.Show($"پذیرش «{petName}» برای {BoardingPresentation.Fa(nights)} شب ثبت شد (نمونه طراحی)");
        _onCreated?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}

/// <summary>
/// One room option of the new-stay modal's room picker, mirroring the
/// reference's <c>&lt;option value="{r.id}"&gt;{fa(r.label)}&lt;/option&gt;</c>.
/// </summary>
public sealed class BoardingRoomOptionViewModel
{
    public BoardingRoomOptionViewModel(RoomInfo room)
    {
        Room = room;
        Label = PersianNumberFormatter.ToPersianDigits(room.Label);
    }

    public RoomInfo Room { get; }
    public string Label { get; }
}
