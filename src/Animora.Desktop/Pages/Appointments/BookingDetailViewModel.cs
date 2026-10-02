using System;
using System.Threading.Tasks;
using Animora.Contracts.Appointments;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// ViewModel for the appointment detail drawer (the reference's <c>.dtl</c>
/// side panel + <c>detailHtml(a)</c> in Animora-UI/assets/appointments-panel.js):
/// the type/status header, the pet lock-up, the "when" block, the KV info grid,
/// the admission note, the events timeline and the foot actions (admit / edit /
/// SMS / cancel). Reads through <see cref="IAppointmentBookingService"/> (R-18).
/// </summary>
public sealed partial class BookingDetailViewModel : ObservableObject
{
    private readonly IAppointmentBookingService _booking;
    private readonly IAppointmentsQueryService _catalog;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Action? _onChanged;
    private readonly Action<AppointmentEvent>? _onEdit;

    private AppointmentEvent _event = null!;
    private IReadOnlyList<AppointmentType> _types = [];
    private IReadOnlyList<AppointmentDoctor> _doctors = [];

    public BookingDetailViewModel(
        IAppointmentBookingService booking,
        IAppointmentsQueryService catalog,
        IToastService toast,
        IDialogService dialogs,
        Action? onChanged = null,
        Action<AppointmentEvent>? onEdit = null)
    {
        _booking = booking;
        _catalog = catalog;
        _toast = toast;
        _dialogs = dialogs;
        _onChanged = onChanged;
        _onEdit = onEdit;
    }

    public string Id => _event.Id;

    [ObservableProperty]
    private string _typeLabel = string.Empty;

    [ObservableProperty]
    private IBrush _typeAccentBrush = Brushes.Transparent;

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    [ObservableProperty]
    private Tone _statusTone = Tone.Neutral;

    [ObservableProperty]
    private string _petName = string.Empty;

    [ObservableProperty]
    private string _petSub = string.Empty;

    [ObservableProperty]
    private IBrush _avatarBrush = Brushes.Transparent;

    [ObservableProperty]
    private string _iconKey = "IconPaw";

    [ObservableProperty]
    private string _rangeText = string.Empty;

    [ObservableProperty]
    private string _whenSubText = string.Empty;

    [ObservableProperty]
    private string _durationText = string.Empty;

    [ObservableProperty]
    private string _doctorText = string.Empty;

    [ObservableProperty]
    private string _phoneText = string.Empty;

    [ObservableProperty]
    private string _codeText = string.Empty;

    [ObservableProperty]
    private string _priceText = string.Empty;

    [ObservableProperty]
    private string _noteText = string.Empty;

    [ObservableProperty]
    private string _createdText = string.Empty;

    [ObservableProperty]
    private string _statusTimelineTitle = string.Empty;

    [ObservableProperty]
    private string _statusTimelineSub = string.Empty;

    [ObservableProperty]
    private bool _statusTimelineMuted = true;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private bool _canCheckIn;

    [ObservableProperty]
    private bool _canEdit;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private bool _canOpenVisit;

    /// <summary>Loads (or re-loads) the drawer content from the service.</summary>
    public async Task<bool> LoadAsync(string appointmentId)
    {
        var detail = await _booking.GetDetailAsync(appointmentId);
        if (detail is null)
        {
            return false;
        }

        var a = detail.Event;
        _event = a;

        if (_types.Count == 0)
        {
            _types = await _catalog.GetTypesAsync();
            _doctors = await _catalog.GetDoctorsAsync();
        }

        var type = _types.FirstOrDefault(x => x.Id == a.TypeId) ?? _types[0];
        var doctor = _doctors.FirstOrDefault(x => x.Id == a.DoctorId) ?? _doctors[0];
        var t = AppointmentsPresentation.Lifecycle(a.Lifecycle);

        TypeLabel = type.Label;
        TypeAccentBrush = AppointmentsPresentation.Accent(type.Accent);
        AvatarBrush = AppointmentsPresentation.AvatarBrush(a.AvatarTone);
        IconKey = a.SpeciesIconKey;
        PetName = a.PetName;
        PetSub = $"{a.PetKind} · مالک: {a.OwnerName}";
        RangeText = AppointmentsPresentation.Range(a.StartMinutes, a.EndMinutes);
        var isToday = a.Date == DateOnly.FromDateTime(DateTime.Today);
        WhenSubText = AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(a.Date)] + " " +
                      AppointmentsCalendarMath.Full(a.Date) + (isToday ? " · امروز" : string.Empty);
        DurationText = AppointmentsPresentation.Fa(a.DurationMinutes) + " دقیقه";
        DoctorText = $"{doctor.Name} · {doctor.Specialty}";
        PhoneText = a.OwnerPhone;
        CodeText = a.Code;
        PriceText = PersianNumberFormatter.FormatGrouped(a.Total) + " تومان";
        NoteText = string.IsNullOrWhiteSpace(a.Note) ? "یادداشتی ثبت نشده است." : a.Note;
        CreatedText = detail.CreatedText;
        StatusLabel = t.Label;
        StatusTone = t.Tone;
        IsDone = a.IsTerminal;
        CanCheckIn = a.CanCheckIn;
        CanEdit = a.CanMove;
        CanCancel = a.CanCancel;
        CanOpenVisit = a.CanOpenVisit;
        (StatusTimelineTitle, StatusTimelineSub, StatusTimelineMuted) = a.Lifecycle switch
        {
            AppointmentLifecycle.Scheduled or AppointmentLifecycle.Confirmed =>
                ("در انتظار پذیرش", "پس از حضور مالک", true),
            AppointmentLifecycle.CheckedIn =>
                ("پذیرش انجام شد", a.CheckedInUtc is null ? "ثبت در صف پذیرش" : "ثبت شده در صف پذیرش", false),
            AppointmentLifecycle.InVisit => ("ویزیت در جریان است", "ثبت در پرونده", false),
            AppointmentLifecycle.Completed => ("ویزیت انجام شد", "ثبت در پرونده", false),
            AppointmentLifecycle.NoShow => ("مراجعه نکرد", "عدم مراجعه ثبت شد", false),
            _ => ("نوبت لغو شد", string.IsNullOrWhiteSpace(a.CancelReason) || a.CancelReason == "cancelled"
                ? "بدون ذکر دلیل" : a.CancelReason, false),
        };
        return true;
    }

    [RelayCommand]
    private async Task CheckIn()
    {
        await _booking.CheckInAsync(_event.Id);
        _toast.Show($"بیمار «{_event.PetName}» پذیرش شد.", Tone.Ok);
        await LoadAsync(_event.Id);
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void Edit()
    {
        var ev = _event;
        _dialogs.Close();
        _onEdit?.Invoke(ev);
    }

    [RelayCommand]
    private void SendSms() =>
        _toast.Show("یادآور پیامکی برای " + _event.OwnerName + " ارسال شد.", Tone.Info);

    [RelayCommand]
    private async Task CancelAppointment()
    {
        await _booking.CancelAsync(_event.Id);
        _toast.Show("نوبت لغو شد و به مالک اطلاع داده شد.", Tone.Danger);
        await LoadAsync(_event.Id);
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void ViewVisit() =>
        _toast.Show("انتقال به پرونده ویزیت…", Tone.Info);

    [RelayCommand]
    private void Close() => _dialogs.Close();
}

/// <summary>
/// Read-only appointment catalog accessor (doctor + type lookup) the detail
/// drawer needs to resolve display labels while binding only to Contracts. A
/// thin wrapper over <see cref="IAppointmentsQueryService"/> so the catalog is
/// fetched once and shared — no sample data leaks into the ViewModel.
/// </summary>
public interface IAppointmentTypeCatalog
{
    AppointmentType TypeOf(string typeId);

    AppointmentDoctor DoctorOf(string doctorId);
}
