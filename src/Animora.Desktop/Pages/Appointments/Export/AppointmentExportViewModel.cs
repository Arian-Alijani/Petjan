using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Appointments;
using Animora.Contracts.Identity;
using Animora.Contracts.Profile;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Appointments.Export;

/// <summary>
/// The «خروجی / چاپ نوبت‌ها» dialog: choose which days (one day, a range with
/// weekday filter, or hand-picked days), which doctors / types / statuses and an
/// optional text filter, then the paper (A4/A3 × orientation), layout and
/// columns. A live summary shows how many appointments will print. Data is read
/// fresh through <see cref="IAppointmentsQueryService.GetForRangeAsync"/> for the
/// exact selection (not the calendar's ±40-day cache), and the document is
/// handed to <see cref="IPrintService"/>.
/// </summary>
public sealed partial class AppointmentExportViewModel : ObservableObject
{
    /// <summary>Hard cap on SELECTED days so a mistaken range can't produce a thousand-page print (A14: this is a
    /// count of chosen days, independent of how far apart they are).</summary>
    public const int MaxDays = 186;

    /// <summary>
    /// Widest single backend read, in INCLUSIVE days. The backend rejects <c>end - start &gt; 62</c> (= 63 inclusive
    /// days), so every fetch window is partitioned to stay inside it without relaxing the backend bound.
    /// </summary>
    public const int MaxWindowDays = 63;

    /// <summary>Nearby missing days are merged into one read when the gap between them is at most this many days.</summary>
    private const int MaxMergeGapDays = 7;

    /// <summary>Loaded-day cache bound (events + precomputed search text); the active selection is never evicted.</summary>
    private const int MaxCachedDays = 400;

    /// <summary>Count-only bound for the picker's per-day dots (LRU).</summary>
    private const int MaxPickerCountDays = 366;

    /// <summary>Explicit output caps: an oversized selection is rejected instead of freezing the UI.</summary>
    public const int MaxOutputRows = 20000;
    private const int MaxOutputChars = 40_000_000;

    private readonly IAppointmentsQueryService _service;
    private readonly IPrintService _print;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IUserProfileService _profile;
    private readonly ICurrentUserContext _user;
    private readonly IReadOnlyList<AppointmentDoctor> _doctors;
    private readonly Dictionary<string, AppointmentType> _types;
    private readonly bool _dayOnly;
    private readonly IClinicClock _clock;
    private readonly DateOnly _today;

    // Loaded appointments per day (with their precomputed normalized search text) + which days have been fetched
    // (so re-filtering never re-queries). Bounded by MaxCachedDays; an empty list means "known empty", a missing
    // key means "not loaded" (A15).
    private sealed record ExportRow(AppointmentEvent Event, string Text);

    private readonly Dictionary<DateOnly, List<ExportRow>> _byDay = [];
    private readonly HashSet<DateOnly> _loaded = [];
    private Queue<DateOnly> _loadOrder = new();
    private readonly OrderedDictionary<DateOnly, int> _pickerCounts = [];
    private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(160));
    private int _pickerVersion;
    private bool _loadFailed;
    private string _rangeWarning = string.Empty;
    private string _loadWarning = string.Empty;
    private string _limitWarning = string.Empty;
    private readonly HashSet<DateOnly> _picked = [];
    private readonly HashSet<int> _weekdays = [0, 1, 2, 3, 4, 5, 6];
    private int _loadVersion;
    private string _clinicName = string.Empty;
    private List<AppointmentEvent> _matches = [];
    private List<DateOnly> _days = [];

    public AppointmentExportViewModel(
        IAppointmentsQueryService service,
        IPrintService print,
        IDialogService dialogs,
        IToastService toast,
        IUserProfileService profile,
        ICurrentUserContext user,
        IClinicClock clock,
        IReadOnlyList<AppointmentDoctor> doctors,
        IReadOnlyList<AppointmentType> types,
        bool dayOnly,
        DateOnly initialStart,
        DateOnly initialEnd)
    {
        _clock = clock;
        _today = clock.Today;
        _service = service;
        _print = print;
        _dialogs = dialogs;
        _toast = toast;
        _profile = profile;
        _user = user;
        _doctors = doctors;
        _types = types.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        _dayOnly = dayOnly;

        foreach (var d in doctors)
        {
            DoctorChips.Add(new ExportChipViewModel(d.Id, d.Name, AppointmentsPresentation.AccentFromHex(d.Accent), true, d.Specialty));
        }

        foreach (var t in _types.Values)
        {
            TypeChips.Add(new ExportChipViewModel(t.Id, t.Label, AppointmentsPresentation.AccentFromHex(t.Accent), true));
        }

        foreach (var s in new[] { AppointmentLifecycle.Scheduled, AppointmentLifecycle.Confirmed, AppointmentLifecycle.CheckedIn, AppointmentLifecycle.InVisit, AppointmentLifecycle.Completed, AppointmentLifecycle.NoShow, AppointmentLifecycle.Cancelled })
        {
            StatusChips.Add(new ExportChipViewModel(
                s.ToString(), AppointmentsPresentation.Lifecycle(s).Label, Brush(StatusHex(s)), s != AppointmentLifecycle.Cancelled));
        }

        string[] wd = ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"];
        for (var i = 0; i < 7; i++)
        {
            WeekdayChips.Add(new ExportChipViewModel(i.ToString(CultureInfo.InvariantCulture), wd[i], Brush("#4a6cf7"), true));
        }

        string[] presets = ["today", "امروز", "tomorrow", "فردا", "week", "این هفته", "nextweek", "هفته بعد", "next7", "۷ روز آینده", "month", "این ماه"];
        for (var i = 0; i < presets.Length; i += 2)
        {
            PresetChips.Add(new ExportChipViewModel(presets[i], presets[i + 1], Brush("#4a6cf7"), false));
        }

        if (initialStart != initialEnd)
        {
            PresetChips.Insert(0, new ExportChipViewModel("view", "نمای فعلی تقویم", Brush("#4a6cf7"), false));
        }

        _initialStart = initialStart;
        _initialEnd = initialEnd;
        _pickerCursor = AppointmentsCalendarMath.MonthStart(initialStart);
        _dayValue = ToJalali(initialStart);
        _fromValue = ToJalali(initialStart);
        _toValue = ToJalali(initialEnd);
        _picked.Add(initialStart);
        _dateMode = initialStart == initialEnd ? "day" : "range";
        _preparedBy = user.Name;
        _layoutByDoctor = false;

        RebuildPickerGrid();
        _ = InitAsync();
    }

    private readonly DateOnly _initialStart;
    private readonly DateOnly _initialEnd;

    // ── collections ──────────────────────────────────────────────────────────
    public RangeObservableCollection<ExportChipViewModel> PresetChips { get; } = [];

    public RangeObservableCollection<ExportChipViewModel> WeekdayChips { get; } = [];

    public RangeObservableCollection<ExportChipViewModel> DoctorChips { get; } = [];

    public RangeObservableCollection<ExportChipViewModel> TypeChips { get; } = [];

    public RangeObservableCollection<ExportChipViewModel> StatusChips { get; } = [];

    public RangeObservableCollection<ExportDayCellViewModel> PickerCells { get; } = [];

    public IReadOnlyList<string> PickerWeekdayHeaders { get; } = AppointmentsCalendarMath.DayShortNames;

    // ── date selection ───────────────────────────────────────────────────────
    /// <summary><c>day</c> | <c>range</c> | <c>pick</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDayMode), nameof(IsRangeMode), nameof(IsPickMode))]
    private string _dateMode;

    public bool IsDayMode => DateMode == "day";

    public bool IsRangeMode => DateMode == "range";

    public bool IsPickMode => DateMode == "pick";

    [ObservableProperty]
    private string _dayValue;

    [ObservableProperty]
    private string _fromValue;

    [ObservableProperty]
    private string _toValue;

    private DateOnly _pickerCursor;

    public string PickerTitle => $"{AppointmentsCalendarMath.MonthName(_pickerCursor)} {AppointmentsCalendarMath.YearText(_pickerCursor)}";

    public string PickedText => _picked.Count == 0
        ? "روزی انتخاب نشده"
        : $"{Fa(_picked.Count)} روز انتخاب شده";

    partial void OnDateModeChanged(string value) => Refresh();

    partial void OnDayValueChanged(string value) => Refresh();

    partial void OnFromValueChanged(string value) => Refresh();

    partial void OnToValueChanged(string value) => Refresh();

    // ── filters ──────────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => _searchDebounce.Debounce(Recompute);

    public bool IsMultiDoctorClinic => _doctors.Count > 1;

    // ── output options ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaperSummary), nameof(IsA4), nameof(IsA3))]
    private PrintPaper _paper = PrintPaper.A4;

    public bool IsA4 => Paper == PrintPaper.A4;

    public bool IsA3 => Paper == PrintPaper.A3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaperSummary), nameof(IsPortrait))]
    private bool _landscape;

    public bool IsPortrait => !Landscape;

    public string PaperSummary =>
        $"{Paper} {(Landscape ? "افقی" : "عمودی")} · {(Paper == PrintPaper.A3 ? (Landscape ? "۴۲۰×۲۹۷" : "۲۹۷×۴۲۰") : (Landscape ? "۲۹۷×۲۱۰" : "۲۱۰×۲۹۷"))} میلی‌متر";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimelineLayout))]
    private bool _layoutByDoctor;

    public bool IsTimelineLayout => !LayoutByDoctor;

    [ObservableProperty]
    private bool _compact;

    [ObservableProperty]
    private bool _newPagePerDay;

    [ObservableProperty]
    private bool _includeEmptyDays;

    [ObservableProperty]
    private bool _includeSummary = true;

    [ObservableProperty]
    private bool _showCheckbox = true;

    [ObservableProperty]
    private bool _showPhone = true;

    [ObservableProperty]
    private bool _showType = true;

    [ObservableProperty]
    private bool _showStatus = true;

    [ObservableProperty]
    private bool _showNote = true;

    [ObservableProperty]
    private bool _showCode;

    [ObservableProperty]
    private bool _showNotesColumn;

    [ObservableProperty]
    private string _title = "فهرست نوبت‌ها";

    [ObservableProperty]
    private string _preparedBy;

    partial void OnIncludeEmptyDaysChanged(bool value) => Recompute();

    // ── live summary ─────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _summaryMain = string.Empty;

    [ObservableProperty]
    private string _summarySub = string.Empty;

    [ObservableProperty]
    private string _rangeText = string.Empty;

    [ObservableProperty]
    private string _warning = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand), nameof(SaveCommand))]
    private bool _canExport;

    public bool HasWarning => Warning.Length > 0;

    /// <summary>A read failed: output stays disabled until the user retries (A15).</summary>
    public bool HasLoadFailure => _loadFailed;

    private void SetLoadFailed(bool failed)
    {
        _loadFailed = failed;
        OnPropertyChanged(nameof(HasLoadFailure));
    }

    private void UpdateWarning() =>
        Warning = string.Join(" ", new[] { _rangeWarning, _loadWarning, _limitWarning }.Where(w => w.Length > 0));

    [RelayCommand]
    private void RetryLoad() => Refresh();

    partial void OnWarningChanged(string value) => OnPropertyChanged(nameof(HasWarning));

    // ── commands ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private void SetDateMode(string mode)
    {
        DateMode = mode;
        ClearPresetMarks();
    }

    [RelayCommand]
    private void ApplyPreset(ExportChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        (DateOnly from, DateOnly to) = chip.Id switch
        {
            "today" => (_today, _today),
            "tomorrow" => (_today.AddDays(1), _today.AddDays(1)),
            "week" => (AppointmentsCalendarMath.WeekStart(_today), AppointmentsCalendarMath.WeekStart(_today).AddDays(6)),
            "nextweek" => (AppointmentsCalendarMath.WeekStart(_today).AddDays(7), AppointmentsCalendarMath.WeekStart(_today).AddDays(13)),
            "next7" => (_today, _today.AddDays(6)),
            "month" => MonthRange(_today),
            "view" => (_initialStart, _initialEnd),
            _ => (_today, _today),
        };

        _suppress = true;
        if (from == to)
        {
            DayValue = ToJalali(from);
            DateMode = "day";
        }
        else
        {
            FromValue = ToJalali(from);
            ToValue = ToJalali(to);
            DateMode = "range";
        }

        _suppress = false;
        ClearPresetMarks();
        chip.IsOn = true;
        Refresh();
    }

    [RelayCommand]
    private void ToggleChip(ExportChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        chip.IsOn = !chip.IsOn;
        if (WeekdayChips.Contains(chip))
        {
            var i = int.Parse(chip.Id, CultureInfo.InvariantCulture);
            if (chip.IsOn)
            {
                _weekdays.Add(i);
            }
            else
            {
                _weekdays.Remove(i);
            }

            Refresh();
            return;
        }

        Recompute();
    }

    /// <summary>«همه» / «هیچ» toggles for a chip group: doctors | types | status | weekdays.</summary>
    [RelayCommand]
    private void SetAll(string spec)
    {
        var parts = spec.Split(':');
        var on = parts.Length > 1 && parts[1] == "on";
        var list = parts[0] switch
        {
            "doctors" => DoctorChips,
            "types" => TypeChips,
            "status" => StatusChips,
            _ => WeekdayChips,
        };

        foreach (var c in list)
        {
            c.IsOn = on;
        }

        if (list == WeekdayChips)
        {
            _weekdays.Clear();
            if (on)
            {
                for (var i = 0; i < 7; i++)
                {
                    _weekdays.Add(i);
                }
            }

            Refresh();
            return;
        }

        Recompute();
    }

    /// <summary>Weekday shortcut: everything except Friday (the clinic's usual closed day).</summary>
    [RelayCommand]
    private void ExcludeFriday()
    {
        foreach (var c in WeekdayChips)
        {
            c.IsOn = c.Id != "6";
        }

        _weekdays.Clear();
        for (var i = 0; i < 6; i++)
        {
            _weekdays.Add(i);
        }

        Refresh();
    }

    [RelayCommand]
    private void TogglePickDay(ExportDayCellViewModel? cell)
    {
        if (cell is null)
        {
            return;
        }

        if (!_picked.Remove(cell.Date))
        {
            if (_picked.Count >= MaxDays)
            {
                return;
            }

            _picked.Add(cell.Date);
        }

        cell.IsSelected = _picked.Contains(cell.Date);
        OnPropertyChanged(nameof(PickedText));
        Refresh();
    }

    [RelayCommand]
    private void ClearPicked()
    {
        _picked.Clear();
        foreach (var c in PickerCells)
        {
            c.IsSelected = false;
        }

        OnPropertyChanged(nameof(PickedText));
        Refresh();
    }

    [RelayCommand]
    private void ShiftPickerMonth(string delta)
    {
        var step = delta == "-1" ? -1 : 1;
        _pickerCursor = step > 0
            ? AppointmentsCalendarMath.MonthStart(_pickerCursor.AddDays(35))
            : AppointmentsCalendarMath.MonthStart(_pickerCursor.AddDays(-1));
        RebuildPickerGrid();
        _ = LoadPickerCountsAsync();
    }

    [RelayCommand]
    private void SetPaper(string paper) => Paper = paper == "A3" ? PrintPaper.A3 : PrintPaper.A4;

    [RelayCommand]
    private void SetOrientation(string o) => Landscape = o == "landscape";

    [RelayCommand]
    private void SetLayout(string l) => LayoutByDoctor = l == "doctor";

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Print()
    {
        var html = await BuildHtmlAsync(autoPrint: true);
        if (html is null)
        {
            return;
        }

        if (await _print.OpenHtmlAsync(FileStem(), html))
        {
            _toast.Show("پیش‌نمایش چاپ در مرورگر باز شد.", Tone.Ok);
            _dialogs.CloseModal();
        }
        else
        {
            _toast.Show("باز کردن پیش‌نمایش ممکن نشد؛ از «ذخیره فایل» استفاده کنید.", Tone.Danger);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Save()
    {
        var html = await BuildHtmlAsync(autoPrint: false);
        if (html is null)
        {
            return;
        }

        if (await _dialogs.SaveTextFileAsync(FileStem() + ".html", html, "html"))
        {
            _toast.Show("فایل خروجی ذخیره شد.", Tone.Ok);
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.CloseModal();

    // ── internals ────────────────────────────────────────────────────────────
    private bool _suppress;

    private async Task InitAsync()
    {
        try
        {
            _clinicName = (await _profile.GetAsync()).BranchName;
        }
        catch (Exception)
        {
            _clinicName = string.Empty;
        }

        Refresh();
        await LoadPickerCountsAsync();
    }

    /// <summary>Date selection changed: resolve the day list, fetch any missing days, recompute.</summary>
    private void Refresh()
    {
        if (_suppress)
        {
            return;
        }

        _days = ResolveDays(out var warning);
        _rangeWarning = warning;
        // The previous output no longer matches this selection: disable it immediately, before any await (A15).
        CanExport = false;
        UpdateWarning();
        RangeText = AppointmentPrintDocument.RangeText(_days);
        _ = EnsureLoadedAsync(_days);
    }

    private List<DateOnly> ResolveDays(out string warning)
    {
        warning = string.Empty;
        switch (DateMode)
        {
            case "day":
                return TryParse(DayValue) is { } d ? [d] : [];

            case "pick":
                return _picked.OrderBy(x => x).ToList();

            default:
            {
                if (TryParse(FromValue) is not { } from || TryParse(ToValue) is not { } to)
                {
                    return [];
                }

                if (to < from)
                {
                    (from, to) = (to, from);
                }

                var span = to.DayNumber - from.DayNumber + 1;
                if (span > MaxDays)
                {
                    warning = $"حداکثر {Fa(MaxDays)} روز قابل چاپ است؛ بازه کوتاه شد.";
                    to = from.AddDays(MaxDays - 1);
                }

                var list = new List<DateOnly>();
                for (var x = from; x <= to; x = x.AddDays(1))
                {
                    if (_weekdays.Contains(AppointmentsCalendarMath.WeekIndex(x)))
                    {
                        list.Add(x);
                    }
                }

                return list;
            }
        }
    }

    /// <summary>
    /// Partitions the missing days into bounded contiguous read windows (A14): each window spans at most
    /// <see cref="MaxWindowDays"/> inclusive days, and days that are far apart (a gap above
    /// <see cref="MaxMergeGapDays"/>) go to separate windows so the sparse middle is never fetched.
    /// </summary>
    internal static List<(DateOnly From, DateOnly To)> PartitionWindows(IEnumerable<DateOnly> missing)
    {
        var windows = new List<(DateOnly From, DateOnly To)>();
        DateOnly? start = null;
        var prev = default(DateOnly);
        foreach (var day in missing.Distinct().OrderBy(d => d))
        {
            if (start is { } s &&
                day.DayNumber - prev.DayNumber <= MaxMergeGapDays &&
                day.DayNumber - s.DayNumber + 1 <= MaxWindowDays)
            {
                prev = day;
                continue;
            }

            if (start is { } open)
            {
                windows.Add((open, prev));
            }

            start = day;
            prev = day;
        }

        if (start is { } last)
        {
            windows.Add((last, prev));
        }

        return windows;
    }

    private bool IsSelectionComplete() => !_loadFailed && !IsLoading && _days.All(_loaded.Contains);

    private async Task EnsureLoadedAsync(IReadOnlyList<DateOnly> days)
    {
        var version = ++_loadVersion;
        SetLoadFailed(false);
        _loadWarning = string.Empty;
        var missing = days.Where(d => !_loaded.Contains(d)).ToList();
        if (missing.Count == 0)
        {
            IsLoading = false;
            UpdateWarning();
            Recompute();
            return;
        }

        IsLoading = true;
        UpdateWarning();
        Recompute();

        // Everything is fetched into locals; the whole batch is applied only if no newer selection superseded it.
        var fetched = new Dictionary<DateOnly, List<ExportRow>>();
        try
        {
            foreach (var (from, to) in PartitionWindows(missing))
            {
                if (version != _loadVersion)
                {
                    return;
                }

                await FetchWindowAsync(from, to, fetched);
            }
        }
        catch (Exception)
        {
            if (version == _loadVersion)
            {
                SetLoadFailed(true);
                _loadWarning = "خواندن نوبت‌ها ناموفق بود؛ خروجی غیرفعال است. دوباره تلاش کنید.";
                IsLoading = false;
                UpdateWarning();
                Recompute();
            }

            return;
        }

        if (version != _loadVersion)
        {
            return;
        }

        foreach (var (day, rows) in fetched)
        {
            _byDay[day] = rows;
            if (_loaded.Add(day))
            {
                _loadOrder.Enqueue(day);
            }
        }

        TrimCache();
        IsLoading = false;
        UpdateWarning();
        Recompute();
        ApplyPickerCounts();
    }

    private async Task FetchWindowAsync(DateOnly from, DateOnly to, Dictionary<DateOnly, List<ExportRow>> into)
    {
        var rows = await _service.GetForRangeAsync(from, to);

        // Search text is normalized once per row here (off the UI thread), never per keystroke.
        var built = await Task.Run(() => rows.Select(a => new ExportRow(a, PersianText.Normalize(
            $"{a.PetName} {a.OwnerName} {a.OwnerPhone} {a.PetKind} {a.Code} {a.Note}"))).ToList());

        for (var x = from; x <= to; x = x.AddDays(1))
        {
            into[x] = [];
        }

        foreach (var row in built)
        {
            if (into.TryGetValue(row.Event.Date, out var list))
            {
                list.Add(row);
            }
        }
    }

    /// <summary>Evicts the oldest loaded days beyond <see cref="MaxCachedDays"/>; the active selection is kept.</summary>
    private void TrimCache()
    {
        if (_loaded.Count <= MaxCachedDays)
        {
            return;
        }

        var keep = _days.ToHashSet();
        var survivors = new Queue<DateOnly>();
        while (_loadOrder.Count > 0)
        {
            var day = _loadOrder.Dequeue();
            if (_loaded.Count > MaxCachedDays && !keep.Contains(day))
            {
                _loaded.Remove(day);
                _byDay.Remove(day);
            }
            else
            {
                survivors.Enqueue(day);
            }
        }

        _loadOrder = survivors;
    }

    private int CountFor(DateOnly day) =>
        _byDay.TryGetValue(day, out var l) ? l.Count : _pickerCounts.GetValueOrDefault(day);

    private void ApplyPickerCounts()
    {
        foreach (var c in PickerCells)
        {
            c.Count = CountFor(c.Date);
        }
    }

    private async Task LoadPickerCountsAsync()
    {
        if (PickerCells.Count == 0)
        {
            return;
        }

        var version = ++_pickerVersion;
        var from = PickerCells[0].Date;
        var to = PickerCells[^1].Date;
        var span = to.DayNumber - from.DayNumber + 1;
        var unknown = Enumerable.Range(0, span).Select(i => from.AddDays(i))
            .Any(d => !_loaded.Contains(d) && !_pickerCounts.ContainsKey(d));
        if (unknown)
        {
            // Count-only: the picker never retains events, so navigating months has a fixed memory bound.
            IReadOnlyList<AppointmentEvent> rows;
            try
            {
                rows = await _service.GetForRangeAsync(from, to);
            }
            catch (Exception)
            {
                return;
            }

            if (version != _pickerVersion)
            {
                return;
            }

            var counts = rows.GroupBy(a => a.Date).ToDictionary(g => g.Key, g => g.Count());
            for (var i = 0; i < span; i++)
            {
                var day = from.AddDays(i);
                if (_loaded.Contains(day))
                {
                    continue;
                }

                _pickerCounts.Remove(day);
                _pickerCounts[day] = counts.GetValueOrDefault(day);
                while (_pickerCounts.Count > MaxPickerCountDays)
                {
                    _pickerCounts.RemoveAt(0);
                }
            }
        }

        ApplyPickerCounts();
    }

    private void RebuildPickerGrid()
    {
        var start = AppointmentsCalendarMath.WeekStart(_pickerCursor);
        var month = AppointmentsCalendarMath.Month(_pickerCursor);
        var cells = new List<ExportDayCellViewModel>(42);
        for (var i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            cells.Add(new ExportDayCellViewModel(
                d,
                AppointmentsCalendarMath.DayNumber(d),
                AppointmentsCalendarMath.Month(d) == month,
                d == _today,
                AppointmentsSchedule.IsOffDay(_doctors, d))
            {
                IsSelected = _picked.Contains(d),
                Count = CountFor(d),
            });
        }

        PickerCells.ReplaceAll(cells);
        OnPropertyChanged(nameof(PickerTitle));
    }

    /// <summary>
    /// Filter change only: re-filter the already-loaded days (no I/O). One pass over the in-range rows computes the
    /// result list and all three facet counts (A16); search text is precomputed per row, never re-normalized here.
    /// Output is enabled only when the selection is complete (A15).
    /// </summary>
    private void Recompute()
    {
        var docs = DoctorChips.Where(c => c.IsOn).Select(c => c.Id).ToHashSet();
        var types = TypeChips.Where(c => c.IsOn).Select(c => c.Id).ToHashSet();
        var statuses = StatusChips.Where(c => c.IsOn).Select(c => Enum.Parse<AppointmentLifecycle>(c.Id)).ToHashSet();
        var q = PersianText.Normalize(SearchText);
        var docAll = DoctorChips.Count == 0;
        var typeAll = TypeChips.Count == 0;

        var docCounts = new Dictionary<string, int>();
        var typeCounts = new Dictionary<string, int>();
        var statusCounts = new Dictionary<AppointmentLifecycle, int>();
        var matches = new List<AppointmentEvent>();

        foreach (var day in _days)
        {
            if (!_byDay.TryGetValue(day, out var rows))
            {
                continue;
            }

            foreach (var row in rows)
            {
                var a = row.Event;
                if (q.Length > 0 && !row.Text.Contains(q, StringComparison.Ordinal))
                {
                    continue;
                }

                // Each facet counts against all *other* active filters, so the user sees what toggling a chip changes.
                var docOk = docAll || docs.Contains(a.DoctorId);
                var typeOk = typeAll || types.Contains(a.TypeId);
                var statusOk = statuses.Contains(a.Lifecycle);
                if (typeOk && statusOk)
                {
                    docCounts[a.DoctorId] = docCounts.GetValueOrDefault(a.DoctorId) + 1;
                }

                if (docOk && statusOk)
                {
                    typeCounts[a.TypeId] = typeCounts.GetValueOrDefault(a.TypeId) + 1;
                }

                if (docOk && typeOk)
                {
                    statusCounts[a.Lifecycle] = statusCounts.GetValueOrDefault(a.Lifecycle) + 1;
                }

                if (docOk && (typeOk || !_types.ContainsKey(a.TypeId)) && statusOk)
                {
                    matches.Add(a);
                }
            }
        }

        foreach (var c in DoctorChips)
        {
            c.CountText = Fa(docCounts.GetValueOrDefault(c.Id));
        }

        foreach (var c in TypeChips)
        {
            c.CountText = Fa(typeCounts.GetValueOrDefault(c.Id));
        }

        foreach (var c in StatusChips)
        {
            c.CountText = Fa(statusCounts.GetValueOrDefault(Enum.Parse<AppointmentLifecycle>(c.Id)));
        }

        _matches = matches;

        var daysWith = _matches.Select(a => a.Date).Distinct().Count();
        var printedDays = IncludeEmptyDays ? _days.Count : daysWith;
        var complete = IsSelectionComplete();
        var tooLarge = _matches.Count > MaxOutputRows;
        _limitWarning = tooLarge
            ? $"حداکثر {Fa(MaxOutputRows)} نوبت در یک خروجی ممکن است؛ بازه یا فیلترها را محدودتر کنید."
            : string.Empty;
        UpdateWarning();

        SummaryMain = complete ? $"{Fa(_matches.Count)} نوبت" : IsLoading ? "در حال بارگذاری…" : "ناقص";
        SummarySub = _days.Count == 0
            ? "روزی انتخاب نشده است"
            : !complete
                ? (_loadFailed ? "خواندن نوبت‌ها ناموفق بود؛ خروجی غیرفعال است" : "خروجی پس از بارگذاری کامل فعال می‌شود")
                : $"{Fa(printedDays)} روز در خروجی · {Fa(docs.Count)} پزشک · تخمین {Fa(EstimatePages(printedDays))} صفحه";
        CanExport = complete && !tooLarge && _days.Count > 0 && (_matches.Count > 0 || IncludeEmptyDays) &&
                    (docAll || docs.Count > 0);
    }

    /// <summary>Rough page estimate so users know before printing whether A3/compact is worth it.</summary>
    private int EstimatePages(int printedDays)
    {
        if (printedDays == 0)
        {
            return 0;
        }

        // Usable rows per page (empirical for the builder's type scale).
        var rows = (Paper, Landscape) switch
        {
            (PrintPaper.A3, false) => 44,
            (PrintPaper.A3, true) => 29,
            (PrintPaper.A4, true) => 18,
            _ => 28,
        };
        if (Compact)
        {
            rows = (int)(rows * 1.3);
        }

        if (NewPagePerDay)
        {
            return _matches.GroupBy(a => a.Date).Sum(g => Math.Max(1, (int)Math.Ceiling((g.Count() + 3) / (double)rows)))
                   + (IncludeEmptyDays ? printedDays - _matches.Select(a => a.Date).Distinct().Count() : 0);
        }

        var units = _matches.Count + (printedDays * 3) + (IncludeSummary ? 8 : 3);
        return Math.Max(1, (int)Math.Ceiling(units / (double)rows));
    }

    partial void OnPaperChanged(PrintPaper value) => Recompute();

    partial void OnLandscapeChanged(bool value) => Recompute();

    partial void OnCompactChanged(bool value) => Recompute();

    partial void OnNewPagePerDayChanged(bool value) => Recompute();

    partial void OnIncludeSummaryChanged(bool value) => Recompute();

    /// <summary>
    /// Builds the document from an immutable snapshot of the inputs taken now - only after the selection is known to
    /// be complete (A15). Returns null (after telling the user) when it is incomplete or over the output caps (A16).
    /// </summary>
    private async Task<string?> BuildHtmlAsync(bool autoPrint)
    {
        if (!IsSelectionComplete())
        {
            _toast.Show("بارگذاری نوبت‌ها کامل نیست؛ پس از اتمام یا تلاش دوباره، خروجی بگیرید.", Tone.Danger);
            return null;
        }

        if (_matches.Count > MaxOutputRows)
        {
            _toast.Show("تعداد نوبت‌ها بیش از حد مجاز خروجی است؛ بازه را محدودتر کنید.", Tone.Danger);
            return null;
        }

        var docIds = DoctorChips.Where(c => c.IsOn).Select(c => c.Id).ToHashSet();
        var doctors = _doctors.Where(d => docIds.Contains(d.Id)).ToList();

        // Appointments for a doctor not in the roster (e.g. unassigned) still print; the
        // builder labels them «بدون پزشک». Multi-doctor framing when >1 doctor actually appears.
        var options = new AppointmentPrintOptions
        {
            PreparedAt = _clock.Now,
            Days = _days.ToArray(),
            Appointments = _matches.ToArray(),
            Doctors = doctors.Count > 0 ? doctors : _doctors,
            Types = _types,
            AllDoctors = docIds.Count == _doctors.Count,
            ClinicName = _clinicName,
            PreparedBy = PreparedBy.Trim(),
            Title = string.IsNullOrWhiteSpace(Title) ? "فهرست نوبت‌ها" : Title.Trim(),
            DayOnly = _dayOnly,
            Paper = Paper,
            Landscape = Landscape,
            Layout = LayoutByDoctor ? PrintLayout.ByDoctor : PrintLayout.Timeline,
            Density = Compact ? PrintDensity.Compact : PrintDensity.Comfortable,
            NewPagePerDay = NewPagePerDay,
            IncludeEmptyDays = IncludeEmptyDays,
            IncludeSummary = IncludeSummary,
            ShowCheckbox = ShowCheckbox,
            ShowPhone = ShowPhone,
            ShowType = ShowType,
            ShowStatus = ShowStatus,
            ShowNote = ShowNote,
            ShowCode = ShowCode,
            ShowNotesColumn = ShowNotesColumn,
            AutoPrint = autoPrint,
        };

        // Base64 font embedding + string building is CPU work; keep it off the UI thread.
        var html = await Task.Run(() => AppointmentPrintDocument.Build(options, _print.EmbeddedFontCss));
        if (html.Length > MaxOutputChars)
        {
            _toast.Show("حجم خروجی بیش از حد مجاز است؛ بازه یا ستون‌ها را محدودتر کنید.", Tone.Danger);
            return null;
        }

        return html;
    }

    private string FileStem()
    {
        if (_days.Count == 0)
        {
            return "appointments";
        }

        string Iso(DateOnly d) => ToJalali(d).Replace('/', '-');
        var first = _days.Min();
        var last = _days.Max();
        return first == last ? $"appointments-{Iso(first)}" : $"appointments-{Iso(first)}_to_{Iso(last)}";
    }

    private void ClearPresetMarks()
    {
        foreach (var p in PresetChips)
        {
            p.IsOn = false;
        }
    }

    private static (DateOnly, DateOnly) MonthRange(DateOnly d)
    {
        var start = AppointmentsCalendarMath.MonthStart(d);
        var next = AppointmentsCalendarMath.MonthStart(start.AddDays(35));
        return (start, next.AddDays(-1));
    }

    private static DateOnly? TryParse(string? jalali)
    {
        if (string.IsNullOrWhiteSpace(jalali))
        {
            return null;
        }

        try
        {
            return DateOnly.FromDateTime(JalaliCalendar.Parse(PersianNumberFormatter.ToAsciiDigits(jalali)).ToDateTime());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ToJalali(DateOnly d) => JalaliCalendar.ToIso(JalaliDate.FromDateTime(d.ToDateTime(TimeOnly.MinValue)));

    private static string StatusHex(AppointmentLifecycle s) => s switch
    {
        AppointmentLifecycle.Confirmed => "#2e90fa",
        AppointmentLifecycle.Scheduled => "#f79009",
        AppointmentLifecycle.CheckedIn or AppointmentLifecycle.Completed => "#12b76a",
        AppointmentLifecycle.InVisit => "#5a67d8",
        AppointmentLifecycle.NoShow => "#f97066",
        _ => "#f04438",
    };

    private static IBrush Brush(string hex) => AppointmentsPresentation.AccentFromHex(hex);

    private static string Fa(long n) => PersianNumberFormatter.ToPersianDigits(n);
}
