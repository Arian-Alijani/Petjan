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
    /// <summary>Hard cap so a mistaken range can't produce a thousand-page print.</summary>
    public const int MaxDays = 186;

    private readonly IAppointmentsQueryService _service;
    private readonly IPrintService _print;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IUserProfileService _profile;
    private readonly ICurrentUserContext _user;
    private readonly IReadOnlyList<AppointmentDoctor> _doctors;
    private readonly Dictionary<string, AppointmentType> _types;
    private readonly bool _dayOnly;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    // Loaded appointments per day + which days have been fetched (so re-filtering never re-queries).
    private readonly Dictionary<DateOnly, List<AppointmentEvent>> _byDay = [];
    private readonly HashSet<DateOnly> _loaded = [];
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
        IReadOnlyList<AppointmentDoctor> doctors,
        IReadOnlyList<AppointmentType> types,
        bool dayOnly,
        DateOnly initialStart,
        DateOnly initialEnd)
    {
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

        foreach (var s in new[] { AppointmentStatus.Confirmed, AppointmentStatus.Pending, AppointmentStatus.Done, AppointmentStatus.Cancelled })
        {
            StatusChips.Add(new ExportChipViewModel(
                s.ToString(), AppointmentsPresentation.Status(s).Label, Brush(StatusHex(s)), s != AppointmentStatus.Cancelled));
        }

        string[] wd = ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"];
        for (var i = 0; i < 7; i++)
        {
            WeekdayChips.Add(new ExportChipViewModel(i.ToString(CultureInfo.InvariantCulture), wd[i], Brush("#0d9488"), true));
        }

        string[] presets = ["today", "امروز", "tomorrow", "فردا", "week", "این هفته", "nextweek", "هفته بعد", "next7", "۷ روز آینده", "month", "این ماه"];
        for (var i = 0; i < presets.Length; i += 2)
        {
            PresetChips.Add(new ExportChipViewModel(presets[i], presets[i + 1], Brush("#0d9488"), false));
        }

        if (initialStart != initialEnd)
        {
            PresetChips.Insert(0, new ExportChipViewModel("view", "نمای فعلی تقویم", Brush("#0d9488"), false));
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

    partial void OnSearchTextChanged(string value) => Recompute();

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
        Warning = warning;
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

    private async Task EnsureLoadedAsync(IReadOnlyList<DateOnly> days)
    {
        var version = ++_loadVersion;
        var missing = days.Where(d => !_loaded.Contains(d)).ToList();
        if (missing.Count > 0)
        {
            IsLoading = true;
            try
            {
                await FetchAsync(missing.Min(), missing.Max());
            }
            catch (Exception)
            {
                if (version == _loadVersion)
                {
                    Warning = "خواندن نوبت‌ها ناموفق بود. دوباره تلاش کنید.";
                }
            }
            finally
            {
                if (version == _loadVersion)
                {
                    IsLoading = false;
                }
            }
        }

        if (version == _loadVersion)
        {
            Recompute();
        }
    }

    private async Task FetchAsync(DateOnly from, DateOnly to)
    {
        var rows = await _service.GetForRangeAsync(from, to);
        for (var x = from; x <= to; x = x.AddDays(1))
        {
            _byDay.Remove(x);
            _loaded.Add(x);
        }

        foreach (var a in rows)
        {
            if (!_byDay.TryGetValue(a.Date, out var l))
            {
                _byDay[a.Date] = l = [];
            }

            l.Add(a);
        }
    }

    private async Task LoadPickerCountsAsync()
    {
        if (PickerCells.Count == 0)
        {
            return;
        }

        var from = PickerCells[0].Date;
        var to = PickerCells[^1].Date;
        if (Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Any(i => !_loaded.Contains(from.AddDays(i))))
        {
            try
            {
                await FetchAsync(from, to);
            }
            catch (Exception)
            {
                return;
            }
        }

        foreach (var c in PickerCells)
        {
            c.Count = _byDay.TryGetValue(c.Date, out var l) ? l.Count : 0;
        }
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
                AppointmentsCalendarMath.IsFriday(d))
            {
                IsSelected = _picked.Contains(d),
                Count = _byDay.TryGetValue(d, out var l) ? l.Count : 0,
            });
        }

        PickerCells.ReplaceAll(cells);
        OnPropertyChanged(nameof(PickerTitle));
    }

    /// <summary>Filter change only: re-filter the already-loaded days (no I/O).</summary>
    private void Recompute()
    {
        var docs = DoctorChips.Where(c => c.IsOn).Select(c => c.Id).ToHashSet();
        var types = TypeChips.Where(c => c.IsOn).Select(c => c.Id).ToHashSet();
        var statuses = StatusChips.Where(c => c.IsOn).Select(c => Enum.Parse<AppointmentStatus>(c.Id)).ToHashSet();
        var q = PersianText.Normalize(SearchText);

        var inRange = _days.SelectMany(d => _byDay.TryGetValue(d, out var l) ? l : []).ToList();
        bool TextOk(AppointmentEvent a) => q.Length == 0 ||
            PersianText.Normalize($"{a.PetName} {a.OwnerName} {a.OwnerPhone} {a.PetKind} {a.Code} {a.Note}").Contains(q, StringComparison.Ordinal);

        // Chip counts: each group counts against all *other* active filters, so the user sees
        // what toggling that chip would add/remove.
        var docAll = DoctorChips.Count == 0;
        var typeAll = TypeChips.Count == 0;
        foreach (var c in DoctorChips)
        {
            c.CountText = Fa(inRange.Count(a => a.DoctorId == c.Id && (typeAll || types.Contains(a.TypeId)) && statuses.Contains(a.Status) && TextOk(a)));
        }

        foreach (var c in TypeChips)
        {
            c.CountText = Fa(inRange.Count(a => a.TypeId == c.Id && (docAll || docs.Contains(a.DoctorId)) && statuses.Contains(a.Status) && TextOk(a)));
        }

        foreach (var c in StatusChips)
        {
            var s = Enum.Parse<AppointmentStatus>(c.Id);
            c.CountText = Fa(inRange.Count(a => a.Status == s && (docAll || docs.Contains(a.DoctorId)) && (typeAll || types.Contains(a.TypeId)) && TextOk(a)));
        }

        _matches = inRange.Where(a =>
            (docAll || docs.Contains(a.DoctorId)) &&
            (typeAll || types.Contains(a.TypeId) || !_types.ContainsKey(a.TypeId)) &&
            statuses.Contains(a.Status) &&
            TextOk(a)).ToList();

        var daysWith = _matches.Select(a => a.Date).Distinct().Count();
        var printedDays = IncludeEmptyDays ? _days.Count : daysWith;
        SummaryMain = $"{Fa(_matches.Count)} نوبت";
        SummarySub = _days.Count == 0
            ? "روزی انتخاب نشده است"
            : $"{Fa(printedDays)} روز در خروجی · {Fa(docs.Count)} پزشک · تخمین {Fa(EstimatePages(printedDays))} صفحه";
        CanExport = _days.Count > 0 && (_matches.Count > 0 || IncludeEmptyDays) && (docAll || docs.Count > 0);
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

    private Task<string> BuildHtmlAsync(bool autoPrint)
    {
        var docIds = DoctorChips.Where(c => c.IsOn).Select(c => c.Id).ToHashSet();
        var doctors = _doctors.Where(d => docIds.Contains(d.Id)).ToList();

        // Appointments for a doctor not in the roster (e.g. unassigned) still print; the
        // builder labels them «بدون پزشک». Multi-doctor framing when >1 doctor actually appears.
        var options = new AppointmentPrintOptions
        {
            Days = _days,
            Appointments = _matches,
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
        return Task.Run(() => AppointmentPrintDocument.Build(options, _print.EmbeddedFontCss));
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

    private static string StatusHex(AppointmentStatus s) => s switch
    {
        AppointmentStatus.Confirmed => "#2e90fa",
        AppointmentStatus.Pending => "#f79009",
        AppointmentStatus.Done => "#12b76a",
        _ => "#f04438",
    };

    private static IBrush Brush(string hex) => AppointmentsPresentation.AccentFromHex(hex);

    private static string Fa(long n) => PersianNumberFormatter.ToPersianDigits(n);
}
