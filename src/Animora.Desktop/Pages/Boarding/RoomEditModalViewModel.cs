using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// Backs the «اتاق جدید» / «ویرایش اتاق» modal of the boarding settings tab. Creating asks for the unique code and
/// the room kind; editing keeps both fixed (they identify the room on past stays) and exposes the operational status.
/// </summary>
public sealed partial class RoomEditModalViewModel : ObservableObject
{
    private readonly IBoardingQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly RoomInfo? _existing;
    private readonly Action _onSaved;

    public RoomEditModalViewModel(IBoardingQueryService service, IToastService toast, IDialogService dialogs,
        RoomInfo? existing, Action onSaved)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _existing = existing;
        _onSaved = onSaved;

        KindOptions =
        [
            new RoomChoiceViewModel(nameof(BoardingRoomKind.Standard), "معمولی"),
            new RoomChoiceViewModel(nameof(BoardingRoomKind.Vip), "VIP"),
            new RoomChoiceViewModel(nameof(BoardingRoomKind.Suite), "سوئیت"),
            new RoomChoiceViewModel(nameof(BoardingRoomKind.Isolation), "ایزوله"),
            new RoomChoiceViewModel(nameof(BoardingRoomKind.Cage), "قفس"),
        ];
        StatusOptions =
        [
            new RoomChoiceViewModel(nameof(BoardingRoomStatus.Available), "فعال"),
            new RoomChoiceViewModel(nameof(BoardingRoomStatus.Maintenance), "در تعمیر"),
            new RoomChoiceViewModel(nameof(BoardingRoomStatus.Retired), "بازنشسته"),
        ];

        var kind = existing?.Kind ?? BoardingRoomKind.Standard;
        var status = existing?.Status ?? BoardingRoomStatus.Available;
        foreach (var option in KindOptions) option.IsSelected = option.Key == kind.ToString();
        foreach (var option in StatusOptions) option.IsSelected = option.Key == status.ToString();

        if (existing is null)
        {
            _capacityText = PersianNumberFormatter.ToPersianDigits("1");
            return;
        }

        _code = existing.Label;
        _name = string.IsNullOrWhiteSpace(existing.Name) ? existing.Label : existing.Name;
        _capacityText = PersianNumberFormatter.ToPersianDigits(existing.Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _notes = existing.Notes;
    }

    public bool IsCreate => _existing is null;

    public bool IsEdit => _existing is not null;

    public string HeadTitle => IsCreate ? "اتاق جدید" : "ویرایش اتاق";

    public string HeadSubtitle => IsCreate
        ? "کد، نام، نوع و ظرفیت اتاقی را که برای پانسیون استفاده می‌شود ثبت کنید."
        : "کد و نوع اتاق ثابت است؛ نام، ظرفیت، وضعیت و یادداشت قابل تغییر است.";

    public string SubmitText => IsCreate ? "افزودن اتاق" : "ذخیرهٔ تغییرات";

    public IReadOnlyList<RoomChoiceViewModel> KindOptions { get; }

    public IReadOnlyList<RoomChoiceViewModel> StatusOptions { get; }

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _capacityText = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    [RelayCommand]
    private void SelectKind(RoomChoiceViewModel? option)
    {
        if (option is null || IsEdit) return;
        foreach (var o in KindOptions) o.IsSelected = ReferenceEquals(o, option);
    }

    [RelayCommand]
    private void SelectStatus(RoomChoiceViewModel? option)
    {
        if (option is null) return;
        foreach (var o in StatusOptions) o.IsSelected = ReferenceEquals(o, option);
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSaving) return;

        var name = Name.Trim();
        var code = Code.Trim();
        if (IsCreate && code.Length == 0)
        {
            _toast.Show("کد اتاق را وارد کنید", Tone.Danger);
            return;
        }

        if (name.Length == 0)
        {
            _toast.Show("نام اتاق را وارد کنید", Tone.Danger);
            return;
        }

        if (!int.TryParse(PersianNumberFormatter.ToAsciiDigits(CapacityText).Trim(), out var capacity)
            || capacity is < 1 or > 10)
        {
            _toast.Show("ظرفیت اتاق باید عددی بین ۱ تا ۱۰ باشد", Tone.Danger);
            return;
        }

        var notes = Notes.Trim();
        IsSaving = true;
        try
        {
            if (_existing is null)
            {
                var kind = Enum.Parse<BoardingRoomKind>(KindOptions.First(o => o.IsSelected).Key);
                await _service.CreateRoomAsync(new NewRoomRequest(code, name, kind, capacity, notes));
            }
            else
            {
                var status = Enum.Parse<BoardingRoomStatus>(StatusOptions.First(o => o.IsSelected).Key);
                await _service.UpdateRoomAsync(_existing.Id, new UpdateRoomRequest(name, capacity, status, notes));
            }

            _dialogs.Close();
            _toast.Show(IsCreate ? $"اتاق «{name}» افزوده شد" : $"اتاق «{name}» ذخیره شد", Tone.Ok);
            _onSaved();
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "ذخیرهٔ اتاق ناموفق بود"), Tone.Danger);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}

/// <summary>One selectable chip (kind / status) of the room modal.</summary>
public sealed partial class RoomChoiceViewModel(string key, string label) : ObservableObject
{
    /// <summary>The enum member name the choice stands for.</summary>
    public string Key { get; } = key;

    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isSelected;
}
