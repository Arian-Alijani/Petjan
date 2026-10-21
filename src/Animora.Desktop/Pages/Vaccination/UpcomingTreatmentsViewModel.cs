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
/// The «واکسن‌های پیش رو» tab of «دارو و واکسن». It mirrors the «نوبت‌های پیش رو» tab of the main calendar:
/// a 14-day sliding day strip (stepped a week at a time), a header with the selected date and KPI chips, and one
/// column per plan kind (clinic / home / boarding) listing that day's vaccines and medications. One bounded
/// 14-day window is read per load (far below the service's 62-day cap); stale responses are dropped via a version guard.
/// </summary>
public sealed partial class UpcomingTreatmentsViewModel : ObservableObject
{
    private const int WindowDays = 14;

    private readonly IVaccinationService _service;
    private readonly IToastService _toast;
    private readonly Action<CalendarEventViewModel> _openDetail;
    private readonly Action _openAdd;

    private int _loadVersion;
    private bool _loadedOnce;
    private int _weekOffset;
    private DateOnly _selectedDay = Today;
    private bool _showDone;
    private List<CalendarOccurrence> _window = [];

    public UpcomingTreatmentsViewModel(
        IVaccinationService service, IToastService toast, Action<CalendarEventViewModel> openDetail, Action openAdd)
    {
        _service = service;
        _toast = toast;
        _openDetail = openDetail;
        _openAdd = openAdd;
        Render();
    }

    public RangeObservableCollection<UpcomingDayViewModel> Days { get; } = [];
    public RangeObservableCollection<UpcomingStatViewModel> Stats { get; } = [];
    public RangeObservableCollection<UpcomingKindColumnViewModel> Columns { get; } = [];

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private bool _isToday = true;

    [ObservableProperty]
    private int _columnCount = 3;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasLoadError;

    public bool IsDoneShown => _showDone;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Loads the window the first time the tab opens (later opens reuse the cache until data changes).</summary>
    public void EnsureLoaded()
    {
        if (!_loadedOnce)
        {
            _ = ReloadAsync();
        }
    }

    /// <summary>Forces a fresh read of the window (data changed elsewhere).</summary>
    public void Invalidate()
    {
        if (_loadedOnce)
        {
            _ = ReloadAsync();
        }
    }

    [RelayCommand]
    private void SelectDay(UpcomingDayViewModel? day)
    {
        if (day is null)
        {
            return;
        }

        _selectedDay = day.Day;
        Render();
    }

    [RelayCommand]
    private Task ShiftWeek(string? dir)
    {
        _weekOffset = Math.Max(0, _weekOffset + (dir == "-1" ? -1 : 1));
        return ReloadAsync();
    }

    [RelayCommand]
    private Task GoToday()
    {
        _weekOffset = 0;
        _selectedDay = Today;
        return ReloadAsync();
    }

    [RelayCommand]
    private void ToggleDone()
    {
        _showDone = !_showDone;
        OnPropertyChanged(nameof(IsDoneShown));
        Render();
    }

    [RelayCommand]
    private void OpenDetail(UpcomingCardViewModel? card)
    {
        if (card is not null)
        {
            _openDetail(card.Event);
        }
    }

    [RelayCommand]
    private void OpenAdd() => _openAdd();

    private async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        HasLoadError = false;

        var from = Today.AddDays(_weekOffset * 7);
        try
        {
            var rows = await _service.GetCalendarAsync(from, from.AddDays(WindowDays - 1));
            if (version != _loadVersion)
            {
                return;
            }

            _window = rows.ToList();
            _loadedOnce = true;
            if (_selectedDay < from || _selectedDay > from.AddDays(WindowDays - 1))
            {
                _selectedDay = from;
            }

            Render();
        }
        catch (InvalidOperationException ex)
        {
            if (version != _loadVersion)
            {
                return;
            }

            HasLoadError = true;
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "بارگذاری واکسن‌های پیش رو ناموفق بود؛ دوباره تلاش کنید."), Tone.Danger);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private void Render()
    {
        var today = Today;
        var first = today.AddDays(_weekOffset * 7);

        var pendingByDay = _window
            .Where(o => o.State == TreatmentOccurrenceState.Pending)
            .GroupBy(o => o.Day)
            .ToDictionary(g => g.Key, g => g.Count());

        Days.ReplaceAll(Enumerable.Range(0, WindowDays).Select(i =>
        {
            var d = first.AddDays(i);
            return new UpcomingDayViewModel(d, d == today, AppointmentsCalendarMath.IsFriday(d), pendingByDay.GetValueOrDefault(d))
            {
                IsActive = d == _selectedDay,
            };
        }));

        var day = _window.Where(o => o.Day == _selectedDay).ToList();
        var pending = day.Count(o => o.State == TreatmentOccurrenceState.Pending);
        Stats.ReplaceAll(
        [
            new UpcomingStatViewModel("کل", VaccinationPresentation.Fa(day.Count), Tone.Neutral),
            new UpcomingStatViewModel("در انتظار انجام", VaccinationPresentation.Fa(pending), Tone.Warn),
            new UpcomingStatViewModel("انجام‌شده", VaccinationPresentation.Fa(day.Count - pending), Tone.Ok),
        ]);

        IsToday = _selectedDay == today;
        DateText = AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(_selectedDay)] +
                   "، " + AppointmentsCalendarMath.Full(_selectedDay);

        var visible = day.Where(o => _showDone || o.State == TreatmentOccurrenceState.Pending).ToList();
        var columns = new List<UpcomingKindColumnViewModel>();

        // Next-dose reminders get their own column: they come from vaccination records, not from a plan kind, and are
        // read-only — mixing them into «کلینیک» would hide that difference from the operator.
        var boosters = visible
            .Where(o => o.IsVaccineBooster)
            .OrderBy(o => o.PetName, StringComparer.CurrentCulture)
            .Select((o, i) => new UpcomingCardViewModel(new CalendarEventViewModel(o), i + 1))
            .ToList();
        if (boosters.Count > 0)
        {
            columns.Add(UpcomingKindColumnViewModel.Boosters(boosters));
        }

        foreach (var kind in new[] { TreatmentKind.ClinicVisit, TreatmentKind.HomeByOwner, TreatmentKind.BoardingAdminister })
        {
            var cards = visible
                .Where(o => !o.IsVaccineBooster && o.Kind == kind)
                .OrderBy(o => o.State == TreatmentOccurrenceState.Done)
                .ThenBy(o => o.PetName, StringComparer.CurrentCulture)
                .Select((o, i) => new UpcomingCardViewModel(new CalendarEventViewModel(o), i + 1))
                .ToList();
            columns.Add(new UpcomingKindColumnViewModel(kind, cards));
        }

        Columns.ReplaceAll(columns);
        ColumnCount = columns.Count;
    }
}
