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
    private readonly Action<AppointmentEvent, string?>? _onEdit;
    private readonly IClinicClock _clock;
    private string? _version;
    private readonly Func<AppointmentEvent, Task>? _onOpenVisit;

    private AppointmentEvent _event = null!;
    private IReadOnlyList<AppointmentType> _types = [];
    private IReadOnlyList<AppointmentDoctor> _doctors = [];

    public BookingDetailViewModel(
        IAppointmentBookingService booking,
        IAppointmentsQueryService catalog,
        IToastService toast,
        IDialogService dialogs,
        IClinicClock clock,
        Action? onChanged = null,
        Action<AppointmentEvent, string?>? onEdit = null,
        Func<AppointmentEvent, Task>? onOpenVisit = null)
    {
        _onOpenVisit = onOpenVisit;
        _clock = clock;
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
    private bool _hasRescheduled;

    [ObservableProperty]
    private string _rescheduleText = string.Empty;

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

    private static readonly AppointmentType RemovedType =
        new("removed", "نوع حذف‌شده", "removed", "#98a2b3", "#eef1f8", "#536b68", "calendar", 30);

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
        _version = detail.Version;

        if (_types.Count == 0)
        {
            _types = await _catalog.GetTypesAsync();
            _doctors = await _catalog.GetDoctorsAsync();
        }

        // A05: never substitute another catalog entry for a missing reference.
        var type = _types.FirstOrDefault(x => x.Id == a.TypeId) ?? RemovedType;
        var doctor = _doctors.FirstOrDefault(x => x.Id == a.DoctorId) ?? AppointmentsPresentation.OrphanDoctor(a);
        var t = AppointmentsPresentation.Lifecycle(a.Lifecycle);

        TypeLabel = type.Label;
        TypeAccentBrush = AppointmentsPresentation.Accent(type.Accent);
        AvatarBrush = AppointmentsPresentation.AvatarBrush(a.AvatarTone);
        IconKey = a.SpeciesIconKey;
        PetName = a.PetName;
        PetSub = $"{a.PetKind} · سرپرست: {a.OwnerName}";
        RangeText = AppointmentsPresentation.Range(a.StartMinutes, a.EndMinutes);
        var isToday = a.Date == _clock.Today;
        WhenSubText = AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(a.Date)] + " " +
                      AppointmentsCalendarMath.Full(a.Date) + (isToday ? " · امروز" : string.Empty);
        DurationText = AppointmentsPresentation.Fa(a.DurationMinutes) + " دقیقه";
        DoctorText = $"{doctor.Name} · {doctor.Specialty}";
        PhoneText = a.OwnerPhone;
        // A28: the "code" is the raw record id and is labelled as such in the view.
        CodeText = a.Code;
        // A12: only a real linked invoice yields money; otherwise say so instead of showing a zero/estimate.
        PriceText = a.HasInvoice
            ? $"{PersianNumberFormatter.FormatGrouped(a.InvoiceTotalMinor!.Value)} تومان · مانده {PersianNumberFormatter.FormatGrouped(a.InvoiceBalanceMinor!.Value)} تومان"
            : "فاکتوری ثبت نشده است";
        HasRescheduled = a.RescheduleCount > 0;
        RescheduleText = $"{AppointmentsPresentation.Fa(a.RescheduleCount)} بار جابه‌جا شد";
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
                ("در انتظار پذیرش", "پس از حضور سرپرست", true),
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

    private bool _isActing;

    /// <summary>
    /// Runs one write. The success toast is shown only after the command committed (A10); a rejection keeps the
    /// drawer open with a localized reason. A read failure after a committed write is reported as such, never as
    /// a failed write.
    /// </summary>
    private async Task<bool> RunWriteAsync(Func<Task> write, string successMessage, Tone tone)
    {
        if (_isActing)
        {
            return false;
        }

        _isActing = true;
        try
        {
            await write();
        }
        catch (Exception ex) when (!AppointmentErrorText.IsCancellation(ex))
        {
            _toast.Show(AppointmentErrorText.For(ex), Tone.Danger);
            // A stale state is the usual reason for a rejection: re-read so the actions match reality.
            await TryReloadAsync();
            return false;
        }
        finally
        {
            _isActing = false;
        }

        _toast.Show(successMessage, tone);
        _onChanged?.Invoke();
        await TryReloadAsync();
        return true;
    }

    private async Task TryReloadAsync()
    {
        try
        {
            await LoadAsync(_event.Id);
        }
        catch (Exception ex) when (!AppointmentErrorText.IsCancellation(ex))
        {
            _toast.Show("تغییر ذخیره شد، اما بارگذاری دوبارهٔ جزئیات انجام نشد؛ پنجره را ببندید و دوباره باز کنید.", Tone.Info);
        }
    }

    [RelayCommand]
    private Task CheckIn() =>
        RunWriteAsync(() => _booking.CheckInAsync(_event.Id, _version), $"بیمار «{_event.PetName}» پذیرش شد.", Tone.Ok);

    [RelayCommand]
    private void Edit()
    {
        var ev = _event;
        var version = _version;
        _dialogs.Close();
        _onEdit?.Invoke(ev, version);
    }

    [RelayCommand]
    private Task CancelAppointment() =>
        RunWriteAsync(() => _booking.CancelAsync(_event.Id, _version), "نوبت لغو شد.", Tone.Danger);

    [RelayCommand]
    private async Task ViewVisit()
    {
        // A13: navigate to the linked visit, or say truthfully that there is none - never a placeholder toast.
        if (!_event.CanOpenVisit || _onOpenVisit is null)
        {
            _toast.Show("برای این نوبت پروندهٔ ویزیتی ثبت نشده است.", Tone.Info);
            return;
        }

        await _onOpenVisit(_event);
    }

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
