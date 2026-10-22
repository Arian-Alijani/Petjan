using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Animora.Contracts.Messaging;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Messaging;

/// <summary>One lead step chip («۲۴ ساعت مانده» / «۷ روز مانده»).</summary>
public sealed record SmsLeadStep(int Value, string Label);

/// <summary>
/// Editable list of reminder lead steps (multi-stage reminders, e.g. 24h and 2h before). Values are unique, positive,
/// bounded by <see cref="MaxValue"/> and kept sorted descending (earliest reminder first).
/// </summary>
public sealed partial class SmsLeadStepsViewModel : ObservableObject
{
    private const int MaxSteps = 5;
    private readonly Action _changed;

    public SmsLeadStepsViewModel(string unit, int maxValue, IEnumerable<int> initial, Action changed)
    {
        Unit = unit;
        MaxValue = maxValue;
        _changed = changed;
        Steps.ReplaceAll(Normalize(initial).Select(ToStep));
        HasSteps = Steps.Count > 0;
    }

    /// <summary>Persian unit label («ساعت» / «روز»).</summary>
    public string Unit { get; }

    public int MaxValue { get; }

    public string Placeholder => $"مثلاً {PersianNumberFormatter.ToPersianDigits(Unit == "روز" ? "7" : "24")}";

    public RangeObservableCollection<SmsLeadStep> Steps { get; } = [];

    [ObservableProperty]
    private bool _hasSteps;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newValueText = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    public IReadOnlyList<int> Values => Steps.Select(s => s.Value).ToArray();

    private bool CanAdd => !string.IsNullOrWhiteSpace(NewValueText);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var ascii = PersianNumberFormatter.ToAsciiDigits(NewValueText).Trim();
        if (!int.TryParse(ascii, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0 || value > MaxValue)
        {
            Hint = $"عددی بین ۱ تا {PersianNumberFormatter.ToPersianDigits(MaxValue.ToString(CultureInfo.InvariantCulture))} وارد کنید.";
            return;
        }

        if (Steps.Any(s => s.Value == value))
        {
            Hint = "این مرحله قبلاً اضافه شده است.";
            return;
        }

        if (Steps.Count >= MaxSteps)
        {
            Hint = $"حداکثر {PersianNumberFormatter.ToPersianDigits(MaxSteps.ToString(CultureInfo.InvariantCulture))} مرحله مجاز است.";
            return;
        }

        Steps.ReplaceAll(Normalize(Values.Append(value)).Select(ToStep));
        NewValueText = string.Empty;
        Hint = string.Empty;
        HasSteps = true;
        _changed();
    }

    [RelayCommand]
    private void Remove(SmsLeadStep? step)
    {
        if (step is null || !Steps.Remove(step)) return;
        HasSteps = Steps.Count > 0;
        Hint = string.Empty;
        _changed();
    }

    private IEnumerable<int> Normalize(IEnumerable<int> values) =>
        values.Where(v => v > 0 && v <= MaxValue).Distinct().OrderByDescending(v => v).Take(MaxSteps);

    private SmsLeadStep ToStep(int value) =>
        new(value, $"{PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture))} {Unit} مانده");
}

/// <summary>
/// The allowed send window of day (from/to hour). Persisted as <c>SendAtHours = [from, to]</c>; a reminder due outside
/// the window is deferred into it by the future backend.
/// </summary>
public sealed partial class SmsSendWindowViewModel : ObservableObject
{
    private readonly Action _changed;

    public SmsSendWindowViewModel(IReadOnlyList<int> sendAtHours, Action changed)
    {
        _changed = changed;
        var from = sendAtHours.Count > 0 ? sendAtHours[0] : 9;
        var to = sendAtHours.Count > 1 ? sendAtHours[^1] : 20;
        _fromIndex = Math.Clamp(from, 0, 23);
        _toIndex = Math.Clamp(to, 0, 23);
        Validate();
    }

    /// <summary>«۰۰:۰۰» … «۲۳:۰۰» (index = hour).</summary>
    public static IReadOnlyList<string> HourOptions { get; } = Enumerable.Range(0, 24)
        .Select(h => PersianNumberFormatter.ToPersianDigits($"{h:00}:00"))
        .ToArray();

    [ObservableProperty]
    private int _fromIndex;

    [ObservableProperty]
    private int _toIndex;

    [ObservableProperty]
    private string _hint = string.Empty;

    public bool IsValid => FromIndex < ToIndex;

    public IReadOnlyList<int> Hours => [FromIndex, ToIndex];

    partial void OnFromIndexChanged(int value) => Changed();

    partial void OnToIndexChanged(int value) => Changed();

    private void Changed()
    {
        Validate();
        _changed();
    }

    private void Validate() => Hint = IsValid ? string.Empty : "ساعت شروع باید قبل از ساعت پایان باشد.";
}

/// <summary>Template text + live character / segment counter and a token-labelled preview (no sample data).</summary>
public sealed partial class SmsTemplateEditorViewModel : ObservableObject
{
    private static readonly (string Token, string Label)[] Tokens =
    [
        ("{owner}", "«نام صاحب»"),
        ("{pet}", "«نام حیوان»"),
        ("{date}", "«تاریخ»"),
        ("{time}", "«ساعت»"),
        ("{amount}", "«مبلغ»"),
        ("{vaccine}", "«دارو/واکسن»"),
        ("{clinic}", "«نام کلینیک»"),
    ];

    private readonly Action _changed;

    public SmsTemplateEditorViewModel(string body, Action changed)
    {
        _changed = changed;
        _body = body;
        Recount();
    }

    [ObservableProperty]
    private string _body;

    [ObservableProperty]
    private string _counter = string.Empty;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private bool _isMultiSegment;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Body);

    partial void OnBodyChanged(string value)
    {
        Recount();
        _changed();
    }

    private void Recount()
    {
        var text = Body ?? string.Empty;
        var segments = SmsFormat.Segments(text.Length);
        IsMultiSegment = segments > 1;
        Counter = $"{SmsFormat.Digits(text.Length)} نویسه · {SmsFormat.Digits(segments)} پیامک";
        var preview = text;
        foreach (var (token, label) in Tokens) preview = preview.Replace(token, label, StringComparison.Ordinal);
        Preview = string.IsNullOrWhiteSpace(preview) ? "متن پیامک خالی است." : preview;
    }
}

/// <summary>One per-event settings card (enable switch + template).</summary>
public sealed partial class SmsEventSettingViewModel : ObservableObject
{
    private readonly Action _changed;

    public SmsEventSettingViewModel(SmsEventSetting setting, Action changed)
    {
        _changed = changed;
        EventType = setting.EventType;
        _enabled = setting.Enabled;
        Template = new SmsTemplateEditorViewModel(setting.TemplateBody, changed);
    }

    public SmsEventType EventType { get; }

    public string Name => SmsFormat.EventName(EventType);

    public string Hint => SmsFormat.EventHint(EventType);

    public string IconKey => SmsFormat.EventIcon(EventType);

    /// <summary>The vaccine reminder's template/schedule live in the three per-kind blocks, not on this card.</summary>
    public bool HasOwnTemplate => EventType != SmsEventType.VaccineReminder;

    /// <summary>The appointment reminder carries the extra schedule editor.</summary>
    public bool IsAppointmentReminder => EventType == SmsEventType.AppointmentReminder;

    public SmsTemplateEditorViewModel Template { get; }

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value) => _changed();

    public SmsEventSetting ToContract() => new(EventType, Enabled, Template.Body ?? string.Empty);
}

/// <summary>One of the three vaccine/medication reminder modes (clinic visit / home by owner / boarding).</summary>
public sealed partial class SmsVaccineReminderViewModel : ObservableObject
{
    private readonly Action _changed;

    public SmsVaccineReminderViewModel(VaccineReminderSetting setting, Action changed)
    {
        _changed = changed;
        Kind = setting.Kind;
        _enabled = setting.Enabled;
        Leads = new SmsLeadStepsViewModel("روز", 60, setting.LeadDaysList, changed);
        Window = new SmsSendWindowViewModel(setting.SendAtHours, changed);
        Template = new SmsTemplateEditorViewModel(setting.TemplateBody, changed);
    }

    public SmsVaccineKind Kind { get; }

    public string Name => SmsFormat.VaccineKindName(Kind);

    public string Hint => SmsFormat.VaccineKindHint(Kind);

    public string IconKey => SmsFormat.VaccineKindIcon(Kind);

    public SmsLeadStepsViewModel Leads { get; }

    public SmsSendWindowViewModel Window { get; }

    public SmsTemplateEditorViewModel Template { get; }

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value) => _changed();

    public VaccineReminderSetting ToContract() =>
        new(Kind, Enabled, Leads.Values, Window.Hours, Template.Body ?? string.Empty);
}
