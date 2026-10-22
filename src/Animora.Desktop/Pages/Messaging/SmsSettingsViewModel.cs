using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Messaging;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Messaging;

/// <summary>
/// «تنظیمات» section: one card per <see cref="SmsEventType"/> (enable + template with a 70-char segment counter), the
/// appointment reminder schedule (multi-stage lead hours + send window), and the vaccine/medication reminder configured
/// separately for each of its three modes (<see cref="SmsVaccineKind"/>: lead days + send window + template).
/// Everything defaults to off; saving goes through <see cref="ISmsService.SaveSettingsAsync"/> (a no-op stub today),
/// and the toast says so — the UI never claims an SMS will actually be sent.
/// </summary>
public sealed partial class SmsSettingsViewModel(ISmsService service, IToastService toast) : SmsSectionViewModel
{
    private const int MaxAppointmentLeadHours = 168;

    private bool _suppressDirty;

    public override SmsSection Section => SmsSection.Settings;

    public override string Label => "تنظیمات";

    public override string IconKey => "IconTools";

    public override bool UsesRange => false;

    public string TokenHint => SmsFormat.TokenHint;

    public RangeObservableCollection<SmsEventSettingViewModel> Events { get; } = [];

    public RangeObservableCollection<SmsVaccineReminderViewModel> VaccineReminders { get; } = [];

    /// <summary>Appointment reminder: multi-stage lead hours (e.g. 24h and 2h before).</summary>
    [ObservableProperty]
    private SmsLeadStepsViewModel? _appointmentLeads;

    /// <summary>Appointment reminder: allowed send window of day.</summary>
    [ObservableProperty]
    private SmsSendWindowViewModel? _appointmentWindow;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DiscardCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    /// <summary>Master switch of the vaccine reminder card (mirrors the per-event entry).</summary>
    public SmsEventSettingViewModel? VaccineEvent => Events.FirstOrDefault(e => e.EventType == SmsEventType.VaccineReminder);

    protected override async Task LoadCoreAsync(SmsReportRange range, CancellationToken ct)
    {
        var settings = await service.GetSettingsAsync(ct);
        await PublishOnUiAsync(() => Apply(settings));
    }

    protected override void ReleaseHeavyData()
    {
        Events.Clear();
        VaccineReminders.Clear();
        AppointmentLeads = null;
        AppointmentWindow = null;
        IsDirty = false;
        ValidationMessage = string.Empty;
    }

    private bool CanSave => IsDirty && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var error = Validate();
        ValidationMessage = error ?? string.Empty;
        if (error is not null)
        {
            toast.Show(error, Tone.Warn);
            return;
        }

        IsSaving = true;
        try
        {
            await service.SaveSettingsAsync(BuildSettings());
            IsDirty = false;
            toast.Show("تنظیمات پیامک ذخیره شد (نمونهٔ طراحی؛ درگاه پیامک هنوز متصل نیست).", Tone.Info);
        }
        catch (SmsServiceException ex)
        {
            toast.Show(ex.UserMessage, Tone.Danger);
        }
        catch (Exception)
        {
            toast.Show("ذخیرهٔ تنظیمات پیامک انجام نشد؛ دوباره تلاش کنید.", Tone.Danger);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task DiscardAsync()
    {
        Invalidate();
        try
        {
            await LoadAsync(SmsReportRange.Default, force: true, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Apply(SmsSettings settings)
    {
        _suppressDirty = true;
        try
        {
            var byType = settings.Events.ToDictionary(e => e.EventType);
            Events.ReplaceAll(Enum.GetValues<SmsEventType>().Select(t =>
                new SmsEventSettingViewModel(byType.GetValueOrDefault(t) ?? DefaultEvent(t), MarkDirty)));
            OnPropertyChanged(nameof(VaccineEvent));

            AppointmentLeads = new SmsLeadStepsViewModel("ساعت", MaxAppointmentLeadHours,
                settings.AppointmentReminder.LeadHoursList, MarkDirty);
            AppointmentWindow = new SmsSendWindowViewModel(settings.AppointmentReminder.SendAtHours, MarkDirty);

            var byKind = settings.VaccineReminders.ToDictionary(v => v.Kind);
            VaccineReminders.ReplaceAll(Enum.GetValues<SmsVaccineKind>().Select(k =>
                new SmsVaccineReminderViewModel(byKind.GetValueOrDefault(k) ?? DefaultVaccine(k), MarkDirty)));

            IsDirty = false;
            ValidationMessage = string.Empty;
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void MarkDirty()
    {
        if (_suppressDirty) return;
        IsDirty = true;
        ValidationMessage = string.Empty;
    }

    /// <summary>Returns a Persian validation error, or null when the settings can be saved.</summary>
    private string? Validate()
    {
        foreach (var e in Events)
            if (e.Enabled && e.HasOwnTemplate && e.Template.IsEmpty)
                return $"متن پیامک «{e.Name}» خالی است.";

        var appointment = Events.FirstOrDefault(e => e.IsAppointmentReminder);
        if (appointment?.Enabled == true)
        {
            if (AppointmentLeads is null || !AppointmentLeads.HasSteps)
                return "برای یادآوری نوبت دست‌کم یک مرحلهٔ زمانی تعیین کنید.";
            if (AppointmentWindow is { IsValid: false })
                return "بازهٔ ساعت ارسال یادآوری نوبت معتبر نیست.";
        }

        if (VaccineEvent?.Enabled == true)
        {
            foreach (var v in VaccineReminders.Where(v => v.Enabled))
            {
                if (!v.Leads.HasSteps) return $"برای یادآوری «{v.Name}» دست‌کم یک مرحلهٔ زمانی تعیین کنید.";
                if (!v.Window.IsValid) return $"بازهٔ ساعت ارسال «{v.Name}» معتبر نیست.";
                if (v.Template.IsEmpty) return $"متن پیامک «{v.Name}» خالی است.";
            }
        }

        return null;
    }

    private SmsSettings BuildSettings() => new(
        Events.Select(e => e.ToContract()).ToArray(),
        new ReminderSchedule(AppointmentLeads?.Values ?? [], AppointmentWindow?.Hours ?? ReminderSchedule.Default.SendAtHours),
        VaccineReminders.Select(v => v.ToContract()).ToArray());

    private static SmsEventSetting DefaultEvent(SmsEventType type) =>
        SmsSettings.Default.Events.FirstOrDefault(e => e.EventType == type) ?? new SmsEventSetting(type, false, string.Empty);

    private static VaccineReminderSetting DefaultVaccine(SmsVaccineKind kind) =>
        SmsSettings.Default.VaccineReminders.FirstOrDefault(v => v.Kind == kind)
        ?? new VaccineReminderSetting(kind, false, [], ReminderSchedule.Default.SendAtHours, string.Empty);
}
