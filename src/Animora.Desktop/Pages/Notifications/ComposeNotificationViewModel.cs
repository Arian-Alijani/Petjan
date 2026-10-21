using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Animora.Application.Ports;
using Animora.Contracts.Notifications;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Domain.Identity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Notifications;

/// <summary>
/// «پیام جدید» (manager only): audience (everyone / a role / one user), kind, tag, title, text and an optional
/// schedule + expiry. Dates are Jalali in the clinic's own zone and are converted to UTC for the service; validation
/// here is only friendly early feedback - the service is the authority and re-validates everything.
/// </summary>
public sealed partial class ComposeNotificationViewModel : ObservableObject
{
    private readonly INotificationService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IClinicTimeZoneProvider _zones;

    public ComposeNotificationViewModel(INotificationService service, IToastService toast, IDialogService dialogs,
        IClinicTimeZoneProvider zones)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;
        _zones = zones;

        Audiences.Add(new(NotificationAudienceKind.AllUsers, null, "همهٔ کاربران کلینیک"));
        foreach (var role in Enum.GetValues<StaffRole>())
            Audiences.Add(new(NotificationAudienceKind.Role, role.ToString(), "همهٔ " + NotificationsPresentation.RoleLabel(role)));
        SelectedAudience = Audiences[0];

        foreach (var kind in new[]
                 {
                     NotificationKind.Info, NotificationKind.Promo, NotificationKind.Warning,
                     NotificationKind.Success, NotificationKind.System,
                 })
            Kinds.Add(new(kind, NotificationsPresentation.KindLabel(kind)));
        SelectedKind = Kinds[0];
        _ = LoadUsersAsync();
    }

    public ObservableCollection<NotificationAudienceOption> Audiences { get; } = [];

    public ObservableCollection<NotificationKindOption> Kinds { get; } = [];

    [ObservableProperty] private NotificationAudienceOption? _selectedAudience;
    [ObservableProperty] private NotificationKindOption? _selectedKind;
    [ObservableProperty] private string _tag = string.Empty;
    [ObservableProperty] private string _titleText = string.Empty;
    [ObservableProperty] private string _bodyText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowScheduleFields))] private bool _isScheduled;
    [ObservableProperty] private string _scheduleDate = string.Empty;
    [ObservableProperty] private string _scheduleTime = "09:00";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowExpiryField))] private bool _hasExpiry;
    [ObservableProperty] private string _expiryDate = string.Empty;
    [ObservableProperty] private string _error = string.Empty;
    [ObservableProperty] private bool _isSaving;

    public bool ShowScheduleFields => IsScheduled;

    public bool ShowExpiryField => HasExpiry;

    public string SendLabel => IsScheduled ? "زمان‌بندی ارسال" : "ارسال پیام";

    partial void OnIsScheduledChanged(bool value) => OnPropertyChanged(nameof(SendLabel));

    private async Task LoadUsersAsync()
    {
        try
        {
            foreach (var user in await _service.GetRecipientsAsync())
                Audiences.Add(new(NotificationAudienceKind.SpecificUser, user.UserId, $"{user.Name} ({user.RoleLabel})"));
        }
        catch (InvalidOperationException)
        {
            // The "everyone" and role audiences stay usable; a specific user can simply not be picked.
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        Error = string.Empty;
        if (SelectedAudience is null || SelectedKind is null) Error = "مخاطب و نوع پیام را انتخاب کنید.";
        else if (string.IsNullOrWhiteSpace(TitleText)) Error = "عنوان پیام را وارد کنید.";
        else if (string.IsNullOrWhiteSpace(BodyText)) Error = "متن پیام را وارد کنید.";
        if (Error.Length > 0) return;

        var zone = _zones.GetClinicTimeZone();
        DateTimeOffset? scheduled = null;
        DateTimeOffset? expires = null;
        if (IsScheduled)
        {
            if (!TryParseLocal(ScheduleDate, ScheduleTime, zone, out var at))
            {
                Error = "تاریخ و ساعت ارسال را درست انتخاب کنید (مثلاً ۰۹:۳۰).";
                return;
            }

            if (at <= DateTimeOffset.UtcNow)
            {
                Error = "زمان ارسال باید در آینده باشد.";
                return;
            }

            scheduled = at;
        }

        if (HasExpiry)
        {
            // The expiry day is inclusive: the message disappears at the end of the chosen day (clinic time).
            if (!TryParseLocal(ExpiryDate, "23:59", zone, out var end))
            {
                Error = "تاریخ انقضا را انتخاب کنید.";
                return;
            }

            if (end <= (scheduled ?? DateTimeOffset.UtcNow))
            {
                Error = "تاریخ انقضا باید بعد از زمان ارسال باشد.";
                return;
            }

            expires = end;
        }

        var audience = SelectedAudience!;
        var kind = SelectedKind!;
        IsSaving = true;
        try
        {
            await _service.CreateAsync(new CreateNotificationRequest(audience.Kind, audience.Value,
                kind.Kind, string.IsNullOrWhiteSpace(Tag) ? null : Tag.Trim(), TitleText.Trim(), BodyText.Trim(),
                scheduled, expires));
        }
        catch (InvalidOperationException ex)
        {
            Error = NotificationsPresentation.Failure(ex, "ارسال پیام ناموفق بود؛ دوباره تلاش کنید.");
            IsSaving = false;
            return;
        }

        IsSaving = false;
        _toast.Show(scheduled is null ? "پیام ارسال شد" : "پیام برای ارسال در زمان انتخاب‌شده زمان‌بندی شد", Tone.Ok);
        _dialogs.Close();
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    /// <summary>Jalali <c>yyyy/MM/dd</c> + <c>HH:mm</c> in the clinic zone → UTC; false when either part is malformed.</summary>
    private static bool TryParseLocal(string jalaliDate, string time, TimeZoneInfo zone, out DateTimeOffset utc)
    {
        utc = default;
        try
        {
            if (string.IsNullOrWhiteSpace(jalaliDate)) return false;
            var parts = PersianNumberFormatter.ToAsciiDigits(time).Trim().Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)
                || h is < 0 or > 23 || m is < 0 or > 59)
                return false;
            var day = JalaliCalendar.Parse(PersianNumberFormatter.ToAsciiDigits(jalaliDate)).ToDateTime().Date;
            var local = DateTime.SpecifyKind(day.AddHours(h).AddMinutes(m), DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) local = local.AddHours(1); // skipped DST hour: the next valid instant
            utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return false;
        }
    }
}
