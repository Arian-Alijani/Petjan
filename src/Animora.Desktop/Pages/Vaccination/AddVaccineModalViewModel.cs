using System.Collections.ObjectModel;
using Animora.Contracts.Appointments;
using Animora.Contracts.Inventory;
using Animora.Contracts.Sales;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Pages.Sales;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// The «ثبت دارو / واکسن» modal: registers a real medication / vaccine plan for one patient.
/// The admin picks the patient (booking-style server search: no eager list, min two characters, debounced,
/// version-guarded, bounded to 20 rows), the item type (vaccine / medication), the kind (clinic / owner at home /
/// boarding), and one of four schedules (once · every N days · specific dates · weekdays). The item is named by hand
/// or picked from the clinic stock (the shared sales catalog drawer, price-free) — picking is a reminder aid only and
/// never touches stock levels. Submit calls <see cref="IVaccinationService.CreateTreatmentPlanAsync"/> — on failure
/// the modal stays open with the message.
/// </summary>
public sealed partial class AddVaccineModalViewModel : ObservableObject
{
    private readonly IVaccinationService _service;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IAppointmentBookingService _booking;
    private readonly IInventoryQueryService _inventory;
    private readonly Action _onCreated;

    private const int MinQueryLength = 2;
    private const int PatientPageSize = 20;
    private const int MaxSpecificDates = 60;
    private const int StockPageSize = 200;
    private const int MaxStockPages = 10;
    private const string HintMessage = "برای انتخاب بیمار، حداقل ۲ حرف از نام یا شماره تماس را وارد کنید.";

    // Only the latest settled input reaches the database.
    private readonly Debouncer _queryDebounce = new(TimeSpan.FromMilliseconds(300));
    private int _patientSearchVersion;
    private CancellationTokenSource? _patientSearchCancellation;
    private string _kindFilter = "all";
    private BookingPatient? _selectedPatient;

    public AddVaccineModalViewModel(
        IVaccinationService service,
        IReadOnlyList<VaccineCatalogEntry> catalog,
        IDialogService dialogs,
        IToastService toast,
        IAppointmentBookingService booking,
        IInventoryQueryService inventory,
        Action onCreated)
    {
        _inventory = inventory;
        _service = service;
        _dialogs = dialogs;
        _toast = toast;
        _booking = booking;
        _onCreated = onCreated;
        Catalog = catalog;
        BuildKindChips();

        foreach (var kind in new[] { TreatmentKind.ClinicVisit, TreatmentKind.HomeByOwner, TreatmentKind.BoardingAdminister })
        {
            TreatmentKinds.Add(new TreatmentKindOptionViewModel(kind) { IsSelected = kind == TreatmentKind.ClinicVisit });
        }

        foreach (var type in new[] { TreatmentScheduleType.Once, TreatmentScheduleType.EveryNDays, TreatmentScheduleType.SpecificDates, TreatmentScheduleType.WeeklyOnDays })
        {
            ScheduleOptions.Add(new ScheduleTypeOptionViewModel(type) { IsSelected = type == TreatmentScheduleType.Once });
        }

        for (var i = 0; i < AppointmentsCalendarMath.DayNames.Length; i++)
        {
            // Saturday-first display; the mask bit is (int)DayOfWeek.
            Weekdays.Add(new WeekdayToggleViewModel((i + 6) % 7, AppointmentsCalendarMath.DayNames[i]));
        }

        UpdateModeFlags();
    }

    public IReadOnlyList<VaccineCatalogEntry> Catalog { get; }

    public bool HasCatalog => Catalog.Count > 0;

    public List<TreatmentKindOptionViewModel> TreatmentKinds { get; } = [];

    public List<ScheduleTypeOptionViewModel> ScheduleOptions { get; } = [];

    public List<WeekdayToggleViewModel> Weekdays { get; } = [];

    public RangeObservableCollection<SpecificDayChipViewModel> SpecificDays { get; } = [];

    // ── patient picker (booking pattern) ────────────────────────────────────
    public RangeObservableCollection<BookingPatientOptionViewModel> PatientList { get; } = [];

    public ObservableCollection<BookingKindChipViewModel> KindChips { get; } = [];

    public string PatientSearchPlaceholder => "نام بیمار، مالک یا شماره تماس…";

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private bool _hasQuery;

    [ObservableProperty]
    private bool _isPatientSearching;

    [ObservableProperty]
    private bool _hasPatientResults;

    [ObservableProperty]
    private string _patientResultCount = string.Empty;

    [ObservableProperty]
    private bool _showPatientSearchMessage = true;

    [ObservableProperty]
    private string _patientSearchMessage = HintMessage;

    [ObservableProperty]
    private bool _isPatientSearchVisible = true;

    [ObservableProperty]
    private bool _isPatientCardVisible;

    [ObservableProperty]
    private string _selectedName = string.Empty;

    [ObservableProperty]
    private string _selectedKind = string.Empty;

    [ObservableProperty]
    private string _selectedOwner = string.Empty;

    [ObservableProperty]
    private string _selectedPhone = string.Empty;

    [ObservableProperty]
    private string _selectedIconKey = "IconPaw";

    [ObservableProperty]
    private IBrush _selectedAvatarBrush = AppointmentsPresentation.AvatarBrush(null);

    partial void OnQueryTextChanged(string value)
    {
        HasQuery = !string.IsNullOrWhiteSpace(value);
        QueuePatientSearch();
    }

    /// <summary>Cancels queued and in-flight lookups (also called when the modal closes).</summary>
    private void StopPatientSearch()
    {
        _patientSearchVersion++;
        _queryDebounce.Cancel();
        _patientSearchCancellation?.Cancel();
        _patientSearchCancellation = null;
        IsPatientSearching = false;
    }

    private void QueuePatientSearch()
    {
        StopPatientSearch();
        PatientList.ReplaceAll([]);
        HasPatientResults = false;
        PatientResultCount = string.Empty;
        ShowPatientSearchMessage = true;
        var canSearch = PersianText.Normalize(QueryText ?? string.Empty).Length >= MinQueryLength;
        PatientSearchMessage = canSearch ? "در حال جست‌وجو…" : HintMessage;
        if (canSearch)
        {
            _queryDebounce.Debounce(() => _ = SearchPatientsAsync());
        }
    }

    private async Task SearchPatientsAsync()
    {
        var version = _patientSearchVersion;
        using var cancellation = new CancellationTokenSource();
        _patientSearchCancellation = cancellation;
        IsPatientSearching = true;
        try
        {
            var rows = await _booking.SearchPatientsAsync(QueryText.Trim(), _kindFilter, cancellation.Token);
            if (version != _patientSearchVersion || cancellation.IsCancellationRequested)
            {
                return;
            }

            PatientList.ReplaceAll(rows.Take(PatientPageSize).Select(p =>
                new BookingPatientOptionViewModel(p, p.Id == _selectedPatient?.Id, false)));
            HasPatientResults = PatientList.Count > 0;
            PatientResultCount = AppointmentsPresentation.Fa(PatientList.Count) + " نتیجه";
            var hasMore = rows.Count > PatientPageSize;
            ShowPatientSearchMessage = !HasPatientResults || hasMore;
            PatientSearchMessage = hasMore
                ? "۲۰ نتیجه اول نمایش داده شد؛ برای یافتن بیمار، جست‌وجو را دقیق‌تر کنید."
                : "بیماری با این مشخصات پیدا نشد؛ نام، شماره تماس یا فیلتر گونه را بررسی کنید.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (version != _patientSearchVersion)
            {
                return;
            }

            ShowPatientSearchMessage = true;
            PatientSearchMessage = "جست‌وجو انجام نشد. دوباره جست‌وجو کنید.";
        }
        finally
        {
            if (version == _patientSearchVersion)
            {
                IsPatientSearching = false;
                _patientSearchCancellation = null;
            }
        }
    }

    private void BuildKindChips()
    {
        KindChips.Clear();
        var chips = new (string Id, string Label)[]
        {
            ("all", "همه"), ("dog", "سگ"), ("cat", "گربه"), ("bird", "پرنده"), ("other", "سایر"),
        };
        foreach (var chip in chips)
        {
            KindChips.Add(new BookingKindChipViewModel(chip.Id, chip.Label, _kindFilter == chip.Id));
        }
    }

    [RelayCommand]
    private void SelectKind(string? id)
    {
        if (string.IsNullOrEmpty(id) || _kindFilter == id)
        {
            return;
        }

        _kindFilter = id;
        BuildKindChips();
        QueuePatientSearch();
    }

    [RelayCommand]
    private void ClearQuery() => QueryText = string.Empty;

    [RelayCommand]
    private void SelectPatient(BookingPatientOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        StopPatientSearch();
        var patient = option.Patient;
        _selectedPatient = patient;
        SelectedName = patient.Name;
        SelectedKind = patient.Kind;
        SelectedOwner = patient.OwnerName;
        SelectedPhone = patient.Phone;
        SelectedIconKey = patient.SpeciesIconKey;
        SelectedAvatarBrush = AppointmentsPresentation.AvatarBrush(patient.AvatarTone);
        IsPatientCardVisible = true;
        IsPatientSearchVisible = false;
    }

    [RelayCommand]
    private void ChangePatient()
    {
        _selectedPatient = null;
        IsPatientCardVisible = false;
        IsPatientSearchVisible = true;
        if (QueryText.Length == 0)
        {
            QueuePatientSearch();
        }
        else
        {
            QueryText = string.Empty;
        }
    }

    // ── what is given ────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMedication), nameof(ItemLabel), nameof(ItemPlaceholder), nameof(ShowClinicCatalog))]
    private bool _isVaccine = true;

    public bool IsMedication => !IsVaccine;

    public string ItemLabel => IsVaccine ? "نام واکسن" : "نام دارو";

    public string ItemPlaceholder => IsVaccine ? "نام واکسن را بنویسید یا از انبار انتخاب کنید" : "مثلاً آموکسی‌سیلین";

    /// <summary>The clinic's own vaccine catalog is an optional shortcut — shown only for vaccines and only when it has entries.</summary>
    public bool ShowClinicCatalog => IsVaccine && HasCatalog;

    [ObservableProperty]
    private VaccineCatalogEntry? _selectedVaccine;

    /// <summary>The vaccine / medication name — typed by hand, or filled from the clinic catalog or the stock picker.</summary>
    [ObservableProperty]
    private string _itemName = string.Empty;

    [ObservableProperty]
    private string _dosage = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _notifyOwner = true;

    [ObservableProperty]
    private bool _canNotifyOwner;

    // Informational stock line of the item picked from the stock («موجودی انبار»). Never deducted.
    [ObservableProperty]
    private string _stockInfoText = string.Empty;

    [ObservableProperty]
    private bool _hasStockInfo;

    private string _pickedStockName = string.Empty;
    private Dictionary<string, Product> _stockById = [];

    private TreatmentKind _kind = TreatmentKind.ClinicVisit;

    // Choosing a catalog vaccine names the item (and its code is saved with the plan).
    partial void OnSelectedVaccineChanged(VaccineCatalogEntry? value)
    {
        if (value is not null)
        {
            ItemName = value.Name;
        }
    }

    // Typing a different name drops the stock hint of the previously picked item.
    partial void OnItemNameChanged(string value)
    {
        if (HasStockInfo && !string.Equals(value.Trim(), _pickedStockName, StringComparison.Ordinal))
        {
            HasStockInfo = false;
            StockInfoText = string.Empty;
        }
    }

    [RelayCommand]
    private void SetItemType(string? type) => IsVaccine = type != "medication";

    /// <summary>
    /// Opens the clinic stock in the shared sales catalog drawer (price-free «pick one» mode). Choosing an item fills the
    /// name and shows its on-hand quantity — informational only, no stock is deducted. «بازگشت» re-opens this modal.
    /// </summary>
    [RelayCommand]
    private void OpenStockPicker()
    {
        StopPatientSearch();
        _dialogs.ShowModal(
            new CatalogModalViewModel(
                loadCatalog: LoadStockCatalogAsync,
                addItem: item =>
                {
                    ApplyStockItem(item.Id);
                    return Task.CompletedTask;
                },
                toast: _toast,
                dialogs: _dialogs,
                onBack: Reopen,
                picker: new CatalogPickerOptions(
                    IsVaccine ? "انتخاب واکسن از انبار" : "انتخاب دارو از انبار",
                    "جستجوی نام یا دستهٔ کالا…",
                    "فقط برای یادآوری انتخاب می‌شود؛ موجودی انبار کم نمی‌شود.")),
            maxWidth: 500);
    }

    private void Reopen() => _dialogs.ShowModal(this, maxWidth: 580);

    private async Task<CatalogSnapshot> LoadStockCatalogAsync()
    {
        var products = new List<Product>();
        for (var page = 1; page <= MaxStockPages; page++)
        {
            var result = await _inventory.GetProductsAsync(
                new ProductListQuery(null, null, ProductStockFilter.All, page, StockPageSize));
            products.AddRange(result.Items);
            if (result.Items.Count == 0 || products.Count >= result.TotalCount)
            {
                break;
            }
        }

        var categories = (await _inventory.GetCategoriesAsync()).ToDictionary(c => c.Id, c => c.Label);
        _stockById = products.ToDictionary(p => p.Id);

        var icon = IsVaccine ? "IconSyringe" : "IconFlask";
        var items = products
            .OrderBy(p => p.Name, StringComparer.CurrentCulture)
            .Select(p => new CatalogItem(
                p.Id, "item", p.Name, categories.GetValueOrDefault(p.CategoryId, string.Empty), p.CategoryId, 0, icon))
            .ToList();

        var filters = new List<CatalogFilterRef> { new("all", "همه", items.Count) };
        filters.AddRange(items
            .GroupBy(i => i.GroupKey)
            .Select(g => new CatalogFilterRef(g.Key, g.First().GroupLabel, g.Count()))
            .OrderByDescending(f => f.Count));
        return new CatalogSnapshot(items, filters, []);
    }

    private void ApplyStockItem(string productId)
    {
        if (!_stockById.TryGetValue(productId, out var product))
        {
            return;
        }

        _pickedStockName = product.Name.Trim();
        SelectedVaccine = null;
        ItemName = product.Name;
        var onHand = product.Variants.Sum(v => v.Stock);
        StockInfoText = $"موجودی انبار: {VaccinationPresentation.Fa(onHand)} (فقط اطلاع‌رسانی؛ از موجودی کم نمی‌شود)";
        HasStockInfo = true;
    }

    [RelayCommand]
    private void SelectKindOption(TreatmentKindOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var o in TreatmentKinds)
        {
            o.IsSelected = ReferenceEquals(o, option);
        }

        _kind = option.Kind;
        CanNotifyOwner = _kind == TreatmentKind.HomeByOwner;
    }

    // ── schedule ─────────────────────────────────────────────────────────────
    private TreatmentScheduleType _scheduleType = TreatmentScheduleType.Once;

    [ObservableProperty]
    private string _startDate = string.Empty;

    [ObservableProperty]
    private string _endDate = string.Empty;

    [ObservableProperty]
    private string _intervalDays = "2";

    [ObservableProperty]
    private string _doseCount = string.Empty;

    [ObservableProperty]
    private string _pendingDate = string.Empty;

    [ObservableProperty]
    private bool _showStart = true;

    [ObservableProperty]
    private bool _showEvery;

    [ObservableProperty]
    private bool _showSpecific;

    [ObservableProperty]
    private bool _showWeekly;

    [ObservableProperty]
    private bool _showRange;

    [ObservableProperty]
    private string _startLabel = "تاریخ انجام";

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isSubmitting;

    partial void OnErrorTextChanged(string value) => HasError = value.Length > 0;

    [RelayCommand]
    private void SelectSchedule(ScheduleTypeOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var o in ScheduleOptions)
        {
            o.IsSelected = ReferenceEquals(o, option);
        }

        _scheduleType = option.Type;
        UpdateModeFlags();
    }

    private void UpdateModeFlags()
    {
        ShowStart = _scheduleType != TreatmentScheduleType.SpecificDates;
        ShowEvery = _scheduleType == TreatmentScheduleType.EveryNDays;
        ShowSpecific = _scheduleType == TreatmentScheduleType.SpecificDates;
        ShowWeekly = _scheduleType == TreatmentScheduleType.WeeklyOnDays;
        ShowRange = ShowEvery || ShowWeekly;
        StartLabel = _scheduleType == TreatmentScheduleType.Once ? "تاریخ انجام" : "تاریخ شروع";
    }

    // Picking a date in the «تاریخ‌های مشخص» picker appends a chip and clears the picker for the next one.
    partial void OnPendingDateChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !TryParseDay(value, out var day))
        {
            return;
        }

        PendingDate = string.Empty;
        if (SpecificDays.Count >= MaxSpecificDates)
        {
            ErrorText = $"حداکثر {VaccinationPresentation.Fa(MaxSpecificDates)} تاریخ مجاز است.";
            return;
        }

        if (SpecificDays.Any(c => c.Day == day))
        {
            return;
        }

        var all = SpecificDays.Select(c => c.Day).Append(day).OrderBy(d => d).ToList();
        SpecificDays.ReplaceAll(all.Select(d => new SpecificDayChipViewModel(d)));
        ErrorText = string.Empty;
    }

    [RelayCommand]
    private void RemoveSpecificDay(SpecificDayChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        SpecificDays.ReplaceAll(SpecificDays.Where(c => c.Day != chip.Day).ToList());
    }

    private static bool TryParseDay(string? iso, out DateOnly day)
    {
        day = default;
        if (string.IsNullOrWhiteSpace(iso))
        {
            return false;
        }

        try
        {
            day = DateOnly.FromDateTime(JalaliCalendar.Parse(iso.Trim()).ToDateTime());
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>Builds the schedule input, or returns a Persian validation message.</summary>
    private (TreatmentScheduleInput? Schedule, string? Error) BuildSchedule()
    {
        switch (_scheduleType)
        {
            case TreatmentScheduleType.Once:
                return TryParseDay(StartDate, out var once)
                    ? (new TreatmentScheduleInput(TreatmentScheduleType.Once, Start: once), null)
                    : (null, "تاریخ انجام را انتخاب کنید.");

            case TreatmentScheduleType.EveryNDays:
            {
                if (!TryParseDay(StartDate, out var start))
                {
                    return (null, "تاریخ شروع را انتخاب کنید.");
                }

                if (!int.TryParse(PersianNumberFormatter.ToAsciiDigits(IntervalDays).Trim(), out var interval) || interval < 1 || interval > 365)
                {
                    return (null, "فاصلهٔ روزها باید عددی بین ۱ تا ۳۶۵ باشد.");
                }

                var hasCount = !string.IsNullOrWhiteSpace(DoseCount);
                var hasEnd = !string.IsNullOrWhiteSpace(EndDate);
                if (!hasCount && !hasEnd)
                {
                    return (null, "تاریخ پایان یا تعداد دفعات را وارد کنید.");
                }

                int? count = null;
                if (hasCount)
                {
                    if (!int.TryParse(PersianNumberFormatter.ToAsciiDigits(DoseCount).Trim(), out var n) || n < 1 || n > 365)
                    {
                        return (null, "تعداد دفعات باید عددی بین ۱ تا ۳۶۵ باشد.");
                    }

                    count = n;
                }

                DateOnly? end = null;
                if (hasEnd && !hasCount)
                {
                    if (!TryParseDay(EndDate, out var e))
                    {
                        return (null, "تاریخ پایان نامعتبر است.");
                    }

                    if (e < start)
                    {
                        return (null, "تاریخ پایان نباید قبل از شروع باشد.");
                    }

                    end = e;
                }

                return (new TreatmentScheduleInput(TreatmentScheduleType.EveryNDays, Start: start, End: end, IntervalDays: interval, Count: count), null);
            }

            case TreatmentScheduleType.SpecificDates:
                return SpecificDays.Count == 0
                    ? (null, "حداقل یک تاریخ را انتخاب کنید.")
                    : (new TreatmentScheduleInput(TreatmentScheduleType.SpecificDates, Days: SpecificDays.Select(c => c.Day).ToList()), null);

            default:
            {
                if (!TryParseDay(StartDate, out var start) || !TryParseDay(EndDate, out var end))
                {
                    return (null, "تاریخ شروع و پایان را انتخاب کنید.");
                }

                if (end < start)
                {
                    return (null, "تاریخ پایان نباید قبل از شروع باشد.");
                }

                var mask = Weekdays.Where(w => w.IsSelected).Aggregate(0, (m, w) => m | (1 << w.DayOfWeek));
                return mask == 0
                    ? (null, "حداقل یک روز هفته را انتخاب کنید.")
                    : (new TreatmentScheduleInput(TreatmentScheduleType.WeeklyOnDays, Start: start, End: end, WeekdayMask: mask), null);
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        StopPatientSearch();
        _dialogs.Close();
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSubmitting)
        {
            return;
        }

        ErrorText = string.Empty;
        if (_selectedPatient is null)
        {
            ErrorText = "ابتدا بیمار را انتخاب کنید.";
            return;
        }

        var title = ItemName.Trim();
        if (title.Length == 0)
        {
            ErrorText = IsVaccine
                ? "نام واکسن را وارد کنید یا از انبار انتخاب کنید."
                : "نام دارو را وارد کنید یا از انبار انتخاب کنید.";
            return;
        }

        // The catalog code is stored only while the typed name still is that catalog vaccine.
        var vaccineCode = IsVaccine && SelectedVaccine is { } picked
                          && string.Equals(picked.Name, title, StringComparison.Ordinal)
            ? picked.Id
            : null;

        var (schedule, error) = BuildSchedule();
        if (schedule is null)
        {
            ErrorText = error ?? "زمان‌بندی نامعتبر است.";
            return;
        }

        var input = new TreatmentPlanInput(
            _selectedPatient.Id,
            IsVaccine ? TreatmentItemType.Vaccine : TreatmentItemType.Medication,
            _kind,
            title,
            schedule,
            vaccineCode,
            string.IsNullOrWhiteSpace(Dosage) ? null : Dosage.Trim(),
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            _kind == TreatmentKind.HomeByOwner && NotifyOwner);

        // Thursday / Friday are the clinic weekend: ask before storing a day on them instead of deciding silently.
        var days = TreatmentHolidayPolicy.Expand(schedule);
        var holidays = TreatmentHolidayPolicy.HolidaysIn(days);
        if (holidays.Count > 0)
        {
            BeginHolidayPrompt(input, days, holidays);
            return;
        }

        await CommitAsync(input);
    }

    // ── weekend (Thursday / Friday) decision ─────────────────────────────────
    private TreatmentPlanInput? _pendingInput;
    private IReadOnlyList<DateOnly> _pendingDays = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingForm))]
    private bool _isHolidayPrompt;

    [ObservableProperty]
    private string _holidayTitle = string.Empty;

    [ObservableProperty]
    private string _holidayDetail = string.Empty;

    [ObservableProperty]
    private bool _canMoveBefore = true;

    /// <summary>The form is locked while the weekend question is open, so the answer always applies to what was reviewed.</summary>
    public bool IsEditingForm => !IsHolidayPrompt;

    private void BeginHolidayPrompt(TreatmentPlanInput input, IReadOnlyList<DateOnly> days, IReadOnlyList<DateOnly> holidays)
    {
        _pendingInput = input;
        _pendingDays = days;

        var shown = string.Join("، ", holidays.Take(4).Select(d =>
            $"{AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(d)]} {AppointmentsCalendarMath.Full(d)}"));
        var more = holidays.Count > 4 ? $" و {VaccinationPresentation.Fa(holidays.Count - 4)} مورد دیگر" : string.Empty;

        HolidayTitle = holidays.Count == 1
            ? "این نوبت در روز تعطیل کلینیک (پنجشنبه/جمعه) است"
            : $"{VaccinationPresentation.Fa(holidays.Count)} نوبت از {VaccinationPresentation.Fa(days.Count)} نوبت در روز تعطیل (پنجشنبه/جمعه) است";
        HolidayDetail = $"{shown}{more}. همین‌طور تأیید شود یا به روز کاری دیگری منتقل شود؟";

        // Moving earlier must not push a still-upcoming day into the past.
        var today = DateOnly.FromDateTime(DateTime.Today);
        CanMoveBefore = !holidays.Any(d => d >= today && TreatmentHolidayPolicy.NearestWorkingDay(d, forward: false) < today);
        IsHolidayPrompt = true;
    }

    [RelayCommand]
    private async Task ConfirmHolidayAsync()
    {
        if (_pendingInput is { } input)
        {
            await CommitAsync(input);
        }
    }

    [RelayCommand]
    private Task MoveToNextWorkingDayAsync() => MoveHolidaysAsync(forward: true);

    [RelayCommand]
    private Task MoveToPreviousWorkingDayAsync() => MoveHolidaysAsync(forward: false);

    private async Task MoveHolidaysAsync(bool forward)
    {
        if (_pendingInput is not { } input)
        {
            return;
        }

        var shifted = TreatmentHolidayPolicy.Shift(input.Schedule, _pendingDays, forward);
        await CommitAsync(input with { Schedule = shifted });
    }

    [RelayCommand]
    private void ReviewDates()
    {
        _pendingInput = null;
        _pendingDays = [];
        IsHolidayPrompt = false;
    }

    private async Task CommitAsync(TreatmentPlanInput input)
    {
        if (IsSubmitting)
        {
            return;
        }

        IsSubmitting = true;
        try
        {
            await _service.CreateTreatmentPlanAsync(input);
        }
        catch (InvalidOperationException ex)
        {
            ErrorText = PatientsPresentation.CommandFailureMessage(ex, "ثبت برنامه ناموفق بود؛ دوباره تلاش کنید.");
            _toast.Show(ErrorText, Tone.Danger);
            ReviewDates();
            return;
        }
        finally
        {
            IsSubmitting = false;
        }

        StopPatientSearch();
        _dialogs.Close();
        _toast.Show($"{(IsVaccine ? "واکسن" : "دارو")} برای «{_selectedPatient?.Name}» ثبت و در تقویم زمان‌بندی شد");
        _onCreated();
    }
}

/// <summary>A kind option (clinic / home / boarding) of the registration modal, tinted with the kind colour.</summary>
public sealed partial class TreatmentKindOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public TreatmentKindOptionViewModel(TreatmentKind kind)
    {
        Kind = kind;
        Label = VaccinationPresentation.KindLabel(kind);
        IconKey = VaccinationPresentation.KindIconKey(kind);
        Brush = VaccinationPresentation.KindBrush(kind);
        Wash = VaccinationPresentation.KindWash(kind);
    }

    public TreatmentKind Kind { get; }
    public string Label { get; }
    public string IconKey { get; }
    public IBrush Brush { get; }
    public IBrush Wash { get; }
}

/// <summary>A schedule-type option of the registration modal.</summary>
public sealed partial class ScheduleTypeOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ScheduleTypeOptionViewModel(TreatmentScheduleType type)
    {
        Type = type;
        Label = VaccinationPresentation.ScheduleLabel(type);
    }

    public TreatmentScheduleType Type { get; }
    public string Label { get; }
}

/// <summary>A weekday toggle (Saturday-first); <see cref="DayOfWeek"/> is the <see cref="System.DayOfWeek"/> value (mask bit).</summary>
public sealed partial class WeekdayToggleViewModel(int dayOfWeek, string label) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public int DayOfWeek { get; } = dayOfWeek;
    public string Label { get; } = label;
}

/// <summary>A picked date of the «تاریخ‌های مشخص» schedule, shown as a removable chip.</summary>
public sealed class SpecificDayChipViewModel(DateOnly day)
{
    public DateOnly Day { get; } = day;
    public string Text { get; } = AppointmentsCalendarMath.Full(day);
}
