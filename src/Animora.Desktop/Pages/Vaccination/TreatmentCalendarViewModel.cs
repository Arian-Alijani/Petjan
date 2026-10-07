using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// The «تقویم» tab of «دارو و واکسن»: a Jalali month grid (Saturday-first) of the scheduled medication / vaccine
/// days, each event tinted by its plan kind (clinic / home / boarding) and filterable by kind and item type.
/// Clicking a day lists its occurrences with a «انجام شد» action. One bounded month window is read per load
/// (≤ 31 days, far below the service's 62-day cap); stale responses are dropped via a version guard.
/// </summary>
public sealed partial class TreatmentCalendarViewModel : ObservableObject
{
    private readonly IVaccinationService _service;
    private readonly IToastService _toast;
    private readonly Action _onChanged;
    private readonly Action<CalendarEventViewModel> _openPetPlans;

    private int _loadVersion;
    private bool _loadedOnce;
    private DateOnly _monthStart;
    private List<CalendarOccurrence> _occurrences = [];

    public TreatmentCalendarViewModel(
        IVaccinationService service, IToastService toast, Action onChanged, Action<CalendarEventViewModel> openPetPlans)
    {
        _service = service;
        _toast = toast;
        _onChanged = onChanged;
        _openPetPlans = openPetPlans;

        foreach (var kind in new[] { TreatmentKind.ClinicVisit, TreatmentKind.HomeByOwner, TreatmentKind.BoardingAdminister })
        {
            KindFilters.Add(new CalendarKindFilterViewModel(kind) { IsSelected = true });
        }

        ItemTypeFilters.Add(new CalendarItemTypeFilterViewModel(null, "همه") { IsSelected = true });
        ItemTypeFilters.Add(new CalendarItemTypeFilterViewModel(TreatmentItemType.Vaccine, "واکسن"));
        ItemTypeFilters.Add(new CalendarItemTypeFilterViewModel(TreatmentItemType.Medication, "دارو"));

        for (var i = 0; i < AppointmentsCalendarMath.DayNames.Length; i++)
        {
            Weekdays.Add(new CalendarWeekdayHeader(AppointmentsCalendarMath.DayNames[i], i == 6));
        }

        _monthStart = AppointmentsCalendarMath.MonthStart(Today);
        UpdateMonthTitle();
    }

    public List<CalendarKindFilterViewModel> KindFilters { get; } = [];
    public List<CalendarItemTypeFilterViewModel> ItemTypeFilters { get; } = [];
    public List<CalendarWeekdayHeader> Weekdays { get; } = [];
    public RangeObservableCollection<CalendarDayViewModel> Days { get; } = [];
    public RangeObservableCollection<CalendarEventViewModel> SelectedDayEvents { get; } = [];

    [ObservableProperty]
    private string _monthTitle = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _selectedDayTitle = string.Empty;

    [ObservableProperty]
    private bool _hasSelectedDay;

    [ObservableProperty]
    private bool _hasSelectedDayEvents;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasLoadError;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private DateOnly? _selectedDay;

    /// <summary>Loads the visible month the first time the tab opens (later opens reuse the cache until data changes).</summary>
    public void EnsureLoaded()
    {
        if (!_loadedOnce)
        {
            _ = ReloadAsync();
        }
    }

    /// <summary>Forces a fresh read of the visible month (data changed elsewhere).</summary>
    public void Invalidate()
    {
        if (_loadedOnce)
        {
            _ = ReloadAsync();
        }
    }

    [RelayCommand]
    private Task PreviousMonth() => ShiftMonthAsync(-1);

    [RelayCommand]
    private Task NextMonth() => ShiftMonthAsync(1);

    [RelayCommand]
    private Task GoToday()
    {
        _monthStart = AppointmentsCalendarMath.MonthStart(Today);
        _selectedDay = Today;
        return ReloadAsync();
    }

    [RelayCommand]
    private void ToggleKind(CalendarKindFilterViewModel? filter)
    {
        if (filter is null)
        {
            return;
        }

        filter.IsSelected = !filter.IsSelected;
        if (KindFilters.All(k => !k.IsSelected))
        {
            // «none selected» would show an empty calendar with no hint why — restore the full set instead.
            foreach (var k in KindFilters)
            {
                k.IsSelected = true;
            }
        }

        _ = ReloadAsync();
    }

    [RelayCommand]
    private void SetItemType(CalendarItemTypeFilterViewModel? filter)
    {
        if (filter is null || filter.IsSelected)
        {
            return;
        }

        foreach (var option in ItemTypeFilters)
        {
            option.IsSelected = ReferenceEquals(option, filter);
        }

        _ = ReloadAsync();
    }

    [RelayCommand]
    private void SelectDay(CalendarDayViewModel? cell)
    {
        if (cell?.Day is not { } day)
        {
            return;
        }

        _selectedDay = day;
        ApplySelection();
    }

    [RelayCommand]
    private void OpenPetPlans(CalendarEventViewModel? item)
    {
        if (item is not null)
        {
            _openPetPlans(item);
        }
    }

    [RelayCommand]
    private async Task MarkDoneAsync(CalendarEventViewModel? item)
    {
        if (item is null || item.IsDone)
        {
            return;
        }

        try
        {
            await _service.MarkOccurrenceDoneAsync(item.Occurrence.OccurrenceId);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "ثبت انجام ناموفق بود؛ دوباره تلاش کنید."), Tone.Danger);
            await ReloadAsync();
            return;
        }

        item.IsDone = true;
        _toast.Show("این روز به‌عنوان انجام‌شده ثبت شد");
        _onChanged();
        await ReloadAsync();
    }

    private Task ShiftMonthAsync(int delta)
    {
        var (year, month) = JalaliCalendar.ShiftMonth(
            AppointmentsCalendarMath.Year(_monthStart), AppointmentsCalendarMath.Month(_monthStart), delta);
        _monthStart = DateOnly.FromDateTime(new JalaliDate(year, month, 1).ToDateTime());
        _selectedDay = null;
        return ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        HasLoadError = false;
        UpdateMonthTitle();

        var length = JalaliCalendar.MonthLen(AppointmentsCalendarMath.Year(_monthStart), AppointmentsCalendarMath.Month(_monthStart));
        var from = _monthStart;
        var to = _monthStart.AddDays(length - 1);
        var kinds = KindFilters.Where(k => k.IsSelected).Select(k => k.Kind).ToList();
        var filter = new CalendarFilter(
            kinds.Count == KindFilters.Count ? null : kinds,
            ItemTypeFilters.FirstOrDefault(o => o.IsSelected)?.Type);

        try
        {
            var rows = await _service.GetCalendarAsync(from, to, filter);
            if (version != _loadVersion)
            {
                return;
            }

            _occurrences = rows.ToList();
            _loadedOnce = true;
            BuildGrid(length);
        }
        catch (InvalidOperationException ex)
        {
            if (version != _loadVersion)
            {
                return;
            }

            HasLoadError = true;
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "بارگذاری تقویم ناموفق بود؛ دوباره تلاش کنید."), Tone.Danger);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private void UpdateMonthTitle() =>
        MonthTitle = $"{AppointmentsCalendarMath.MonthName(_monthStart)} {AppointmentsCalendarMath.YearText(_monthStart)}";

    private void BuildGrid(int monthLength)
    {
        var byDay = _occurrences
            .GroupBy(o => o.Day)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CalendarEventViewModel>)g
                .OrderBy(o => o.State == TreatmentOccurrenceState.Done)
                .ThenBy(o => o.PetName, StringComparer.CurrentCulture)
                .Select(o => new CalendarEventViewModel(o))
                .ToList());

        var today = Today;
        var cells = new List<CalendarDayViewModel>(42);
        var lead = AppointmentsCalendarMath.WeekIndex(_monthStart);
        for (var i = 0; i < lead; i++)
        {
            cells.Add(new CalendarDayViewModel(null, false, false, []));
        }

        for (var i = 0; i < monthLength; i++)
        {
            var day = _monthStart.AddDays(i);
            cells.Add(new CalendarDayViewModel(
                day, day == today, AppointmentsCalendarMath.IsFriday(day),
                byDay.TryGetValue(day, out var events) ? events : []));
        }

        while (cells.Count % 7 != 0)
        {
            cells.Add(new CalendarDayViewModel(null, false, false, []));
        }

        Days.ReplaceAll(cells);

        var pending = _occurrences.Count(o => o.State == TreatmentOccurrenceState.Pending);
        SummaryText = _occurrences.Count == 0
            ? "در این ماه برنامه‌ای ثبت نشده است"
            : $"{PersianNumberFormatter.ToPersianDigits(_occurrences.Count)} مورد در این ماه · {PersianNumberFormatter.ToPersianDigits(pending)} مورد در انتظار انجام";

        _selectedDay ??= today >= _monthStart && today <= _monthStart.AddDays(monthLength - 1) ? today : null;
        ApplySelection();
    }

    private void ApplySelection()
    {
        foreach (var cell in Days)
        {
            cell.IsSelected = cell.Day is { } d && d == _selectedDay;
        }

        var selected = Days.FirstOrDefault(c => c.IsSelected);
        HasSelectedDay = selected is not null;
        SelectedDayEvents.ReplaceAll(selected?.Events ?? []);
        HasSelectedDayEvents = SelectedDayEvents.Count > 0;
        SelectedDayTitle = selected?.Day is { } day ? AppointmentsCalendarMath.Full(day) : string.Empty;
    }
}
