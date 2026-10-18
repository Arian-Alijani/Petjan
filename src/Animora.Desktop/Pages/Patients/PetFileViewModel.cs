using System.Collections.ObjectModel;
using Animora.Contracts.Patients;
using Animora.Contracts.Visits;
using Animora.Desktop.Controls;
using Animora.Desktop.Diagnostics;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Sales.Shared;
using Animora.Desktop.Pages.Visits;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients;

/// <summary>A tab chip in the pet file's tab strip (patients-page.js PET_TABS).</summary>
public sealed partial class PetTabViewModel : ObservableObject
{
    public PetTabViewModel(string key, string label, string iconKey)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
    }

    public string Key { get; }
    public string Label { get; }
    public string IconKey { get; }

    [ObservableProperty] private bool _isActive;

    /// <summary>Optional trailing count badge (reference .pf-tab .n); null when none.</summary>
    [ObservableProperty] private string? _countText;
}

/// <summary>One key/value definition item in the info tab's .dl grid.</summary>
public sealed record PetInfoItemViewModel(string Label, string Value, string IconKey, bool IsEmpty, bool IsFullWidth);

/// <summary>One clinical/warning alert row (patients-page.js .alert-row) with pre-resolved palette.</summary>
public sealed record PetAlertRowViewModel(
    string Title, string Detail, string IconKey,
    Avalonia.Media.IBrush Background, Avalonia.Media.IBrush Border, Avalonia.Media.IBrush Foreground);

/// <summary>One item in the summary tab's last-exam vitals grid (.vcell).</summary>
public sealed record VitalCellViewModel(string Label, string IconKey, string Value, string Unit);

/// <summary>One weight-history row (.mtable tbody tr) with a computed delta + its colour.</summary>
public sealed record WeightRowViewModel(
    string DateText, string WeightText, string DeltaText, Avalonia.Media.IBrush DeltaBrush);

/// <summary>One vaccination row (.vax-item).</summary>
public sealed record VaxRowViewModel(
    string Name, string SubText, string NextText, string BadgeText, Controls.Tone BadgeTone, string IconClass);

/// <summary>One visit accordion entry (.tl-item). The optional <paramref name="OpenVisit"/> opens the exact visit.</summary>
public sealed record VisitRowViewModel(
    string Type, string Reason, string DateText, string Vet, string DotClass,
    string Exam, string Diagnosis, IReadOnlyList<string> Prescriptions, string StatsText,
    Func<Task>? OpenVisit = null);

/// <summary>One lab/imaging alert row (documents tab) with pre-resolved palette.</summary>
public sealed record LabRowViewModel(
    string Name, string Result, string DateText,
    Avalonia.Media.IBrush Background, Avalonia.Media.IBrush Border, Avalonia.Media.IBrush Foreground);

/// <summary>One radiology study row for the documents tab.</summary>
public sealed record RadiologyRowViewModel(
    string BodyPart, string Result, string DateText);

/// <summary>One stored document row (.doc-row).</summary>
public sealed record DocRowViewModel(string Name, string Meta);

/// <summary>One surgical-history alert row.</summary>
public sealed record SurgeryRowViewModel(string Title, string Surgeon);

/// <summary>
/// Backs the Pet File (Phase 09, patients-page.js petSummary/petInfoTab/
/// petVisitsTab/petWeightTab/petVaxTab/petDocsTab + renderPetFile): hero, tab
/// strip and six per-tab panes. Data comes only from
/// <see cref="IPatientsQueryService"/>.
/// </summary>
public sealed partial class PetFileViewModel : PageViewModelBase
{
    private readonly IPatientsQueryService _patients;
    private readonly IRecordSheetPresenter _presenter;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly IVisitsTriageService _triage;
    private readonly INavigationService _navigation;

    private PetDetail? _pet;
    private OwnerDetail? _owner;

    [ObservableProperty] private PetTabViewModel? _selectedTab;
    [ObservableProperty] private bool _isEditingInfo;

    // ---- weight mini-form ----
    [ObservableProperty] private string _newWeightDate = string.Empty;
    [ObservableProperty] private string _newWeightValue = string.Empty;

    // ---- info edit buffers ----
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editBreed = string.Empty;
    [ObservableProperty] private string _editColor = string.Empty;
    [ObservableProperty] private string _editBirth = string.Empty;
    [ObservableProperty] private string _editHabitat = string.Empty;
    [ObservableProperty] private string _editBehavior = string.Empty;
    [ObservableProperty] private string _editDiet = string.Empty;

    // ---- clinical-alert edit rows (Task 3) ----
    public ObservableCollection<AlertRowEditViewModel> EditAllergies { get; } = [];
    public ObservableCollection<AlertRowEditViewModel> EditChronic { get; } = [];

    public PetFileViewModel(
        IPatientsQueryService patients,
        IRecordSheetPresenter presenter,
        IDialogService dialogs,
        IToastService toast,
        IDataChangeNotifier changes,
        IVisitsTriageService triage,
        INavigationService navigation)
    {
        _patients = patients;
        _changes = changes;
        _presenter = presenter;
        _dialogs = dialogs;
        _toast = toast;
        _triage = triage;
        _navigation = navigation;
        Title = "پرونده بیمار";
        Eyebrow = "بیماران و سرپرستان";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("بیماران و سرپرستان");
        Breadcrumbs.Add("پرونده بیمار");

        Tabs =
        [
            new("summary", "خلاصه", "IconDashboard"),
            new("info", "مشخصات", "IconPaw"),
            new("visits", "ویزیت‌ها", "IconHistory"),
            new("weight", "رشد و وزن", "IconTrend"),
            new("vax", "واکسیناسیون", "IconSyringe"),
            new("docs", "مدارک", "IconFolder"),
        ];
        SelectTab("summary");
    }

    public IReadOnlyList<PetTabViewModel> Tabs { get; }

    // ---- hero ----
    public string Name { get; private set; } = string.Empty;
    public string CodeText { get; private set; } = string.Empty;
    public string SpeciesIconKey { get; private set; } = "IconPaw";
    public IBrush ToneBrush { get; private set; } = Controls.BrushCache.Solid("#808080");
    public string SexGlyph { get; private set; } = "?";
    public string HeroStatusText { get; private set; } = string.Empty;
    public string HeroStatusClass { get; private set; } = "ok";
    public string AgeText { get; private set; } = "—";
    public string WeightChipText { get; private set; } = "—";
    public string NextVaxChipText { get; private set; } = string.Empty;
    public bool HasNextVaxChip { get; private set; }
    public bool NextVaxOverdue { get; private set; }

    // ---- owner ribbon ----
    public string OwnerInitials { get; private set; } = "؟";
    public IBrush OwnerToneBrush { get; private set; } = Controls.BrushCache.Solid("#808080");
    public string OwnerName { get; private set; } = string.Empty;
    public string OwnerFileText { get; private set; } = string.Empty;
    public string OwnerMobile { get; private set; } = string.Empty;

    // ---- summary tab ----
    public ObservableCollection<VitalCellViewModel> Vitals { get; } = [];
    public ObservableCollection<PetAlertRowViewModel> Alerts { get; } = [];
    public ObservableCollection<Controls.WeightChartPoint> WeightMiniPoints { get; } = [];
    public string CurrentWeightText { get; private set; } = "—";
    public string WeightDeltaText { get; private set; } = string.Empty;
    public string WeightDeltaClass { get; private set; } = "flat";
    public string AgeLongText { get; private set; } = "—";
    public string BirthText { get; private set; } = "—";
    public string LastVisitSinceText { get; private set; } = "—";
    public string LastVisitTypeText { get; private set; } = "بدون سابقه";
    public string NextVaxDateText { get; private set; } = "—";
    public string NextVaxLabelText { get; private set; } = "ثبت نشده";
    public bool NextVaxOverdueKd { get; private set; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public string Notes { get; private set; } = string.Empty;
    public bool HasMicrochip => !string.IsNullOrWhiteSpace(MicrochipCode);
    public string MicrochipCode { get; private set; } = string.Empty;
    public string MicrochipDigits { get; private set; } = string.Empty;
    public string MicrochipDate { get; private set; } = "—";
    public string MicrochipVendor { get; private set; } = "—";
    public string MicrochipLocation { get; private set; } = "—";
    public bool HasSurgeries => Surgeries.Count > 0;
    public RangeObservableCollection<SurgeryRowViewModel> Surgeries { get; } = [];
    public bool HasWeightData => WeightMiniPoints.Count > 0;

    // ---- info tab ----
    public ObservableCollection<PetInfoItemViewModel> InfoItems { get; } = [];

    // ---- visits tab ----
    public bool HasVisits => Visits.Count > 0;
    public RangeObservableCollection<VisitRowViewModel> Visits { get; } = [];
    public string VisitsEmptyText { get; } = "ویزیتی برای این بیمار ثبت نشده است";

    // ---- weight tab ----
    public ObservableCollection<Controls.WeightChartPoint> WeightPoints { get; } = [];
    public RangeObservableCollection<WeightRowViewModel> WeightRows { get; } = [];
    public string WeightRecordCountText => $"{Fa(WeightRows.Count.ToString())} رکورد";
    public string DefaultWeightDate => JalaliDate.Today.ToString("short");

    // ---- vax tab ----
    public bool HasVaccinations => Vaccinations.Count > 0;
    public RangeObservableCollection<VaxRowViewModel> Vaccinations { get; } = [];
    public string VaxEmptyText { get; } = "واکسنى برای این بیمار ثبت نشده است".Replace('ى', 'ی');

    // ---- docs tab ----
    public bool HasLabs => Labs.Count > 0;
    public bool HasRadiology => Radiology.Count > 0;
    public RangeObservableCollection<LabRowViewModel> Labs { get; } = [];
    public RangeObservableCollection<RadiologyRowViewModel> Radiology { get; } = [];
    public RangeObservableCollection<DocRowViewModel> Documents { get; } = [];
    public bool HasDocuments => Documents.Count > 0;
    public string DocsEmptyText { get; } = "مدرکی بارگذاری نشده است";

    // ---- visibility ----
    public bool IsSummaryTab => SelectedTab?.Key == "summary";
    public bool IsInfoTab => SelectedTab?.Key == "info";
    public bool IsVisitsTab => SelectedTab?.Key == "visits";
    public bool IsWeightTab => SelectedTab?.Key == "weight";
    public bool IsVaxTab => SelectedTab?.Key == "vax";
    public bool IsDocsTab => SelectedTab?.Key == "docs";

    [RelayCommand]
    private void SelectTab(string key)
    {
        foreach (var tab in Tabs)
        {
            tab.IsActive = tab.Key == key;
        }

        SelectedTab = Tabs.First(t => t.Key == key);
        IsEditingInfo = false;
        NotifyTabVisibility();
    }

    partial void OnSelectedTabChanged(PetTabViewModel? value) => NotifyTabVisibility();

    partial void OnIsEditingInfoChanged(bool value)
    {
        if (value)
        {
            BuildInfoItems(editable: true);
        }
        else if (_pet is not null)
        {
            BuildInfoItems(editable: false);
        }
    }

    /// <summary>
    /// Loads a pet file and its owner; awaited by the caller before the sheet is
    /// shown. Never blocks the UI thread (PERFORMANCE.md §4).
    /// </summary>
    public async Task LoadAsync(string petId)
    {
        await AppTrace.FlowAsync($"PetFile.Load[{petId}]", async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AppTrace.Info($"opening pet file for petId={petId} (GetPetAsync)…", "patients");
            var detail = await _patients.GetPetAsync(petId);
            if (detail is null)
            {
                AppTrace.Warn($"pet file: GetPetAsync returned NULL for petId={petId} — nothing to display. " +
                    "The local Pets table has no such row for the current tenant (deleted, wrong tenant, or never materialised by sync-apply).", "patients");
                return;
            }

            AppTrace.Info($"pet detail loaded in {sw.ElapsedMilliseconds} ms — name='{detail.Name}', ownerId={detail.OwnerId}, weights={detail.Weights.Count}, visits={detail.Visits?.Count ?? 0}.", "patients");
            AppTrace.Info($"resolving pet's owner (GetOwnerAsync ownerId={detail.OwnerId}) for the owner ribbon…", "patients");
            _owner = await _patients.GetOwnerAsync(detail.OwnerId);
            if (_owner is null)
                AppTrace.Warn($"pet file: owner {detail.OwnerId} not found locally — the owner ribbon will be blank.", "patients");
            else
                AppTrace.Info($"pet file owner ribbon resolved — owner='{_owner.FirstName} {_owner.LastName}'.", "patients");
            Apply(detail);
            SelectTab("summary");
            AppTrace.Info($"pet file fully loaded and rendered in {sw.ElapsedMilliseconds} ms.", "patients");
        }, "patients");
    }

    private void Apply(PetDetail pet)
    {
        _pet = pet;

        Name = pet.Name;
        SpeciesIconKey = PatientsPresentation.SpeciesIconKey(pet.Species);
        ToneBrush = PatientsPresentation.ToneBrush(pet.Tone);
        SexGlyph = PatientsPresentation.SexDot(pet.Gender).Glyph;
        CodeText = $"کد بیمار {pet.Code} · {PatientsPresentation.SpeciesLabel(pet.Species)} · {pet.Breed}";
        HeroStatusText = PatientsPresentation.HeroStatusLabel(pet.Status);
        HeroStatusClass = PatientsPresentation.HeroStatusClass(pet.Status);

        var age = RelativeDateFormatter.Age(pet.BirthDate);
        AgeText = age.Label;
        AgeLongText = age.Label;
        BirthText = pet.BirthDate is { } birthDate ? $"تولد {JalaliDate.FromDateTime(birthDate).ToString("short")}" : "—";

        var orderedWeights = pet.Weights.OrderBy(w => w.Date).ToArray();
        CurrentWeightText = WeightValue(orderedWeights);
        WeightChipText = orderedWeights.Length > 0 ? $"{Fa(Number(orderedWeights[^1].WeightKg))} کیلو" : "—";

        var delta = orderedWeights.Length >= 2
            ? System.Math.Round(orderedWeights[^1].WeightKg - orderedWeights[^2].WeightKg, 2)
            : 0;
        WeightDeltaClass = delta > 0 ? "up" : delta < 0 ? "down" : "flat";
        WeightDeltaText = delta == 0 ? "بدون تغییر" : $"{(delta > 0 ? "+" : "−")}{Fa(Number(System.Math.Abs(delta)))} کیلو";

        // last visit
        var lastVisit = pet.Visits.OrderByDescending(v => v.Date).FirstOrDefault();
        LastVisitSinceText = lastVisit is null ? "—" : RelativeDateFormatter.Since(lastVisit.Date);
        LastVisitTypeText = lastVisit?.Type ?? "بدون سابقه";

        // next vaccine
        var nextVax = pet.Vaccinations.OrderByDescending(v => v.NextDate).FirstOrDefault();
        if (nextVax is null)
        {
            NextVaxChipText = string.Empty;
            HasNextVaxChip = false;
            NextVaxDateText = "—";
            NextVaxLabelText = "ثبت نشده";
            NextVaxOverdue = false;
            NextVaxOverdueKd = false;
        }
        else
        {
            var (days, label) = RelativeDateFormatter.Until(nextVax.NextDate);
            NextVaxChipText = $"واکسن {label}";
            HasNextVaxChip = true;
            NextVaxDateText = JalaliDate.FromDateTime(nextVax.NextDate).ToString("short");
            NextVaxLabelText = label;
            NextVaxOverdue = days < 0;
            NextVaxOverdueKd = days < 0;
        }

        // owner ribbon
        OwnerName = _owner is null ? "—" : $"{_owner.FirstName} {_owner.LastName}";
        OwnerInitials = _owner is null ? "؟" : PatientsPresentation.Initials(_owner.FirstName, _owner.LastName);
        OwnerToneBrush = _owner is null ? ToneBrush : PatientsPresentation.ToneBrush(_owner.Tone);
        OwnerFileText = _owner is null ? string.Empty : $"پرونده {_owner.File}";
        OwnerMobile = _owner is null ? string.Empty : Fa(_owner.Mobile1);

        BuildWeight(pet);
        BuildVitals(pet);
        BuildAlerts(pet);
        BuildMicrochip(pet);
        BuildSurgeries(pet);
        BuildVisits(pet);
        BuildVaccinations(pet);
        BuildDocuments(pet);
        BuildInfoItems(editable: false);
        FillInfoBuffers(pet);

        Notes = pet.Notes ?? string.Empty;

        Title = Name;
        OnPropertyChanged(string.Empty);
    }

    private void BuildWeight(PetDetail pet)
    {
        WeightPoints.Clear();
        WeightMiniPoints.Clear();
        WeightRows.Clear();
        var ordered = pet.Weights.OrderBy(w => w.Date).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var date = JalaliDate.FromDateTime(ordered[i].Date);
            var full = date.ToString("short");
            var shortLabel = full[2..]; // reference: w.d.slice(2) → "04/05/11"
            var point = new Controls.WeightChartPoint(full, shortLabel, ordered[i].WeightKg);
            WeightMiniPoints.Add(point);
            WeightPoints.Add(point);

            var prev = i > 0 ? ordered[i - 1].WeightKg : (double?)null;
            var d = prev is null ? (double?)null : System.Math.Round(ordered[i].WeightKg - prev.Value, 2);
            var brush = DeltaFlat;
            var txt = "—";
            if (d is { } dv)
            {
                if (dv > 0) { brush = DeltaUp; txt = $"▲ {Fa(Number(System.Math.Abs(dv)))}"; }
                else if (dv < 0) { brush = DeltaDown; txt = $"▼ {Fa(Number(System.Math.Abs(dv)))}"; }
                else { brush = DeltaFlat; txt = "۰"; }
            }

            WeightRows.Add(new WeightRowViewModel(full, Fa(Number(ordered[i].WeightKg)), txt, brush));
        }

        // newest first (reference reverses the rows)
        var reversed = WeightRows.Reverse().ToArray();
        WeightRows.ReplaceAll(reversed);

        OnPropertyChanged(nameof(HasWeightData));
        OnPropertyChanged(nameof(WeightRecordCountText));
    }

    private void BuildVitals(PetDetail pet)
    {
        Vitals.Clear();
        // Vitals belong to an exam: a pet with no visit (or a visit that recorded none) shows "—", never "۰".
        var hasExam = pet.Visits.Count > 0
            && (pet.Vitals.Temperature > 0 || pet.Vitals.Pulse > 0 || pet.Vitals.Respiration > 0);
        Vitals.Add(new VitalCellViewModel("دما", "IconThermo",
            hasExam ? Fa(Number(pet.Vitals.Temperature)) : "—", "°C"));
        Vitals.Add(new VitalCellViewModel("ضربان", "IconPulse",
            hasExam ? Fa(pet.Vitals.Pulse.ToString()) : "—", "bpm"));
        Vitals.Add(new VitalCellViewModel("تنفس", "IconLungs",
            hasExam ? Fa(pet.Vitals.Respiration.ToString()) : "—", "/min"));
        Vitals.Add(new VitalCellViewModel("BCS", "IconScale",
            hasExam && !string.IsNullOrWhiteSpace(pet.Vitals.BodyConditionScore) ? pet.Vitals.BodyConditionScore : "—",
            string.Empty));
    }

    private void BuildAlerts(PetDetail pet)
    {
        Alerts.Clear();
        foreach (var c in pet.ChronicConditions)
        {
            AddAlert("بیماری مزمن", c, "IconPulse", "danger");
        }

        foreach (var a in pet.Allergies)
        {
            AddAlert("حساسیت", a, "IconAlert", "warn");
        }

        var nextVax = pet.Vaccinations.OrderByDescending(v => v.NextDate).FirstOrDefault();
        if (nextVax is not null)
        {
            var (days, _) = RelativeDateFormatter.Until(nextVax.NextDate);
            if (days < 0)
            {
                AddAlert("واکسن عقب‌افتاده",
                    $"{nextVax.Name} — سررسید {JalaliDate.FromDateTime(nextVax.NextDate).ToString("short")}",
                    "IconSyringe", "warn");
            }
        }

        if (Alerts.Count == 0)
        {
            AddAlert("بدون هشدار فعال",
                "سابقه‌ای از بیماری مزمن یا حساسیت ثبت نشده است.", "IconCheck", "ok");
        }
    }

    private void AddAlert(string title, string detail, string iconKey, string cls)
    {
        var (bg, border, fg) = AlertPalette(cls);
        Alerts.Add(new PetAlertRowViewModel(title, detail, iconKey, bg, border, fg));
    }

    private void BuildMicrochip(PetDetail pet)
    {
        MicrochipCode = pet.Microchip.Code ?? string.Empty;
        MicrochipDigits = Fa(MicrochipCode);
        MicrochipDate = pet.Microchip.ImplantDate is { } d ? JalaliDate.FromDateTime(d).ToString("short") : "—";
        MicrochipVendor = string.IsNullOrWhiteSpace(pet.Microchip.Vendor) ? "—" : pet.Microchip.Vendor!;
        MicrochipLocation = string.IsNullOrWhiteSpace(pet.Microchip.Location) ? "—" : pet.Microchip.Location!;
        OnPropertyChanged(nameof(HasMicrochip));
    }

    private void BuildSurgeries(PetDetail pet)
    {
        Surgeries.ReplaceAll(pet.Surgeries.Select(s =>
        {
            var note = string.IsNullOrWhiteSpace(s.Note) || s.Note == "—" ? string.Empty : $" — {s.Note}";
            return new SurgeryRowViewModel(
                $"{s.Title} · {JalaliDate.FromDateTime(s.Date).ToString("short")}", $"{s.Surgeon}{note}");
        }));

        OnPropertyChanged(nameof(HasSurgeries));
    }

    private void BuildVisits(PetDetail pet)
    {
        Visits.Clear();
        foreach (var v in pet.Visits)
        {
            Visits.Add(new VisitRowViewModel(
                v.Type, v.Reason, JalaliDate.FromDateTime(v.Date).ToString("short"), v.Vet,
                PatientsPresentation.VisitDotVax(v.Type), v.Exam, v.Diagnosis, v.Prescriptions,
                VisitStatsText(v),
                () => _presenter.OpenVisitSheetAsync(v.VisitId)));
        }

        OnPropertyChanged(nameof(HasVisits));
    }

    private static string VisitStatsText(VisitSummaryRef v)
    {
        var parts = new List<string>();
        if (v.WeightKg > 0) parts.Add($"وزن {Fa(Number(v.WeightKg))} کیلو");
        if (v.Cost > 0) parts.Add($"مبلغ {Animora.Desktop.Pages.Visits.VisitsPresentation.Money(v.Cost)}");
        if (!string.IsNullOrEmpty(v.InvoiceId)) parts.Add($"فاکتور {v.InvoiceId}");
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    private void BuildVaccinations(PetDetail pet)
    {
        Vaccinations.ReplaceAll(pet.Vaccinations.Select(v =>
        {
            var (days, _) = RelativeDateFormatter.Until(v.NextDate);
            var cls = days < 0 ? "over" : days < 30 ? "due" : string.Empty;
            var badge = days < 0 ? "عقب‌افتاده" : days < 30 ? "نزدیک" : "به‌روز";
            var tone = days < 0 ? Controls.Tone.Danger : days < 30 ? Controls.Tone.Warn : Controls.Tone.Ok;
            var sub = $"تزریق {JalaliDate.FromDateTime(v.Date).ToString("short")} · {v.Vet} · سری {v.Lot} · {v.Site}";
            return new VaxRowViewModel(
                v.Name, sub, $"یادآور بعدی: {JalaliDate.FromDateTime(v.NextDate).ToString("short")}",
                badge, tone, cls);
        }));

        OnPropertyChanged(nameof(HasVaccinations));
    }

    private void BuildDocuments(PetDetail pet)
    {
        Labs.ReplaceAll(pet.Labs.Select(l =>
        {
            var (bg, border, fg) = AlertPalette(ToneClass(Tone.Info));
            return new LabRowViewModel(l.Title, l.Detail,
                l.DateJalali, bg, border, fg);
        }));

        Radiology.ReplaceAll(pet.Radiology.Select(r =>
            new RadiologyRowViewModel(r.BodyPart, r.Result, r.DateJalali)));

        Documents.ReplaceAll(pet.Documents.Select(d =>
            new DocRowViewModel(d.Name, $"{d.Size} · {JalaliDate.FromDateTime(d.Date).ToString("short")}")));

        OnPropertyChanged(nameof(HasLabs));
        OnPropertyChanged(nameof(HasRadiology));
        OnPropertyChanged(nameof(HasDocuments));
    }

    private void BuildInfoItems(bool editable)
    {
        if (_pet is not { } pet)
        {
            return;
        }

        InfoItems.Clear();
        var age = RelativeDateFormatter.Age(pet.BirthDate);
        if (!editable)
        {
            InfoItems.Add(new("نام حیوان", pet.Name, "IconPaw", Empty(pet.Name), false));
            InfoItems.Add(new("نژاد", pet.Breed, "IconTag", Empty(pet.Breed), false));
            InfoItems.Add(new("رنگ", pet.Color, "IconPalette", Empty(pet.Color), false));
            InfoItems.Add(new("گونه", PatientsPresentation.SpeciesLabel(pet.Species), SpeciesIconKey, false, false));
            InfoItems.Add(new("جنسیت", PatientsPresentation.SexDot(pet.Gender).Title, "IconInfo", false, false));
            InfoItems.Add(new("تاریخ تولد",
                pet.BirthDate is { } birthDate ? JalaliDate.FromDateTime(birthDate).ToString("short") : string.Empty,
                "IconCake", pet.BirthDate is null, false));
            InfoItems.Add(new("سن", age.Label, "IconClock", false, false));
            InfoItems.Add(new("محل زندگی", pet.Habitat, "IconHome2", Empty(pet.Habitat), false));
            InfoItems.Add(new("رفتار", pet.Behavior, "IconSmile", Empty(pet.Behavior), false));
            InfoItems.Add(new("جیره غذایی", pet.Diet, "IconBowl", Empty(pet.Diet), false));
            InfoItems.Add(new("گروه خونی", pet.BloodType ?? string.Empty, "IconFlask", Empty(pet.BloodType), false));
            InfoItems.Add(new("وضعیت عقیم‌سازی",
                pet.Neutered
                    ? $"عقیم شده{(pet.NeuteredDate is { } nd ? " · " + JalaliDate.FromDateTime(nd).ToString("short") : string.Empty)}"
                    : "عقیم نشده",
                "IconScalpel", false, false));
            InfoItems.Add(new("بیمه", pet.Insurance ?? string.Empty, "IconShield", Empty(pet.Insurance), true));
        }
        else
        {
            // The edit pane shows the same labels; the View swaps value text for inputs.
            InfoItems.Add(new("نام حیوان", EditName, "IconPaw", false, false));
            InfoItems.Add(new("نژاد", EditBreed, "IconTag", false, false));
            InfoItems.Add(new("رنگ", EditColor, "IconPalette", false, false));
            InfoItems.Add(new("تاریخ تولد", EditBirth, "IconCake", false, false));
            InfoItems.Add(new("محل زندگی", EditHabitat, "IconHome2", false, false));
            InfoItems.Add(new("رفتار", EditBehavior, "IconSmile", false, false));
            InfoItems.Add(new("جیره غذایی", EditDiet, "IconBowl", false, false));
        }
    }

    private void FillInfoBuffers(PetDetail pet)
    {
        EditName = pet.Name;
        EditBreed = pet.Breed;
        EditColor = pet.Color;
        EditBirth = pet.BirthDate is { } birthDate ? JalaliDate.FromDateTime(birthDate).ToString("short") : string.Empty;
        EditHabitat = pet.Habitat;
        EditBehavior = pet.Behavior;
        EditDiet = pet.Diet;
        FillAlertRows(EditAllergies, pet.Allergies);
        FillAlertRows(EditChronic, pet.ChronicConditions);
    }

    /// <summary>Rebuilds a row-edit collection from a persisted alert list (Task 3).</summary>
    private static void FillAlertRows(
        ObservableCollection<AlertRowEditViewModel> rows, IReadOnlyList<string> values)
    {
        rows.Clear();
        foreach (var value in values)
        {
            rows.Add(new AlertRowEditViewModel(value, rows.Remove));
        }
    }

    private void NotifyTabVisibility()
    {
        OnPropertyChanged(nameof(IsSummaryTab));
        OnPropertyChanged(nameof(IsInfoTab));
        OnPropertyChanged(nameof(IsVisitsTab));
        OnPropertyChanged(nameof(IsWeightTab));
        OnPropertyChanged(nameof(IsVaxTab));
        OnPropertyChanged(nameof(IsDocsTab));
    }

    [RelayCommand]
    private void EditInfo() => IsEditingInfo = true;

    /// <summary>Opens the info tab in edit mode so the alerts editor is visible (summary «+ هشدار»).</summary>
    [RelayCommand]
    private void EditAlerts()
    {
        SelectTab("info");
        IsEditingInfo = true;
    }

    /// <summary>Adds an empty allergy row to the info-tab alerts editor (Task 3).</summary>
    [RelayCommand]
    private void AddEditAllergy() => EditAllergies.Add(new AlertRowEditViewModel(string.Empty, EditAllergies.Remove));

    /// <summary>Adds an empty chronic-condition row to the info-tab alerts editor (Task 3).</summary>
    [RelayCommand]
    private void AddEditChronic() => EditChronic.Add(new AlertRowEditViewModel(string.Empty, EditChronic.Remove));

    [RelayCommand]
    private void CancelInfoEdit()
    {
        if (_pet is not null)
        {
            FillInfoBuffers(_pet);
        }

        IsEditingInfo = false;
    }

    [RelayCommand]
    private async Task SaveInfoAsync()
    {
        if (_pet is null)
        {
            return;
        }

        var birth = _pet.BirthDate;
        if (TryParseJalali(EditBirth, out var parsed))
        {
            birth = parsed;
        }

        var updated = _pet with
        {
            Name = Trim(EditName),
            Breed = Trim(EditBreed),
            Color = Trim(EditColor),
            BirthDate = birth,
            Habitat = Trim(EditHabitat),
            Behavior = Trim(EditBehavior),
            Diet = Trim(EditDiet),
            Allergies = PetFormViewModel.AlertTexts(EditAllergies),
            ChronicConditions = PetFormViewModel.AlertTexts(EditChronic),
        };

        var saved = await _patients.SavePetInfoAsync(updated);
        _changes.Notify(DataDomain.Patients, DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
        Apply(saved);
        IsEditingInfo = false;
        _toast.Show("مشخصات بیمار به‌روزرسانی شد", Controls.Tone.Ok);
    }

    [RelayCommand]
    private async Task AddWeightAsync()
    {
        if (_pet is null)
        {
            return;
        }

        if (!TryParseWeight(NewWeightValue, out var weight) || weight <= 0)
        {
            _toast.Show("لطفاً وزن معتبر وارد کنید", Controls.Tone.Info);
            return;
        }

        var date = string.IsNullOrWhiteSpace(NewWeightDate) ? DateTime.Today : DateTime.Today;
        if (!string.IsNullOrWhiteSpace(NewWeightDate))
        {
            if (!TryParseJalali(NewWeightDate, out date))
            {
                _toast.Show("تاریخ را به شکل ۱۴۰۴/۰۵/۱۱ وارد کنید", Controls.Tone.Info);
                return;
            }
        }

        var saved = await _patients.AddWeightEntryAsync(_pet.Id, new WeightEntry(date, weight));
        Apply(saved);
        NewWeightValue = string.Empty;
        NewWeightDate = string.Empty;
        _toast.Show("وزن جدید ثبت شد", Controls.Tone.Ok);
    }

    /// <summary>
    /// KPI «وزن فعلی» entry affordance (Task 4): jumps to the weight tab and focuses the
    /// add-weight form so the operator records the value through the real write path.
    /// </summary>
    [RelayCommand]
    private void AddWeightFromKpi()
    {
        SelectTab("weight");
        NewWeightValue = string.Empty;
    }

    [RelayCommand]
    private async Task AddVisitAsync()
    {
        if (_pet is null)
        {
            return;
        }

        // The exact pet is bound by id, so the encounter is tied to THIS record (no name lookup).
        var pet = _pet;
        var ownerName = _owner is null ? "—" : $"{_owner.FirstName} {_owner.LastName}".Trim();
        var ownerPhone = _owner?.Mobile1 ?? string.Empty;
        var kind = string.IsNullOrWhiteSpace(pet.Breed) ? pet.Species.ToString() : pet.Breed.Trim();
        var request = new NewVisitRequest(
            DoctorId: string.Empty, // filled in by the picker on confirm
            PatientName: pet.Name,
            PatientKind: kind,
            OwnerName: ownerName,
            OwnerPhone: ownerPhone,
            PatientIconKey: VisitsPresentation.IconKeyForKind(kind),
            IsNewPatient: false,
            TypeId: "visit",
            IsUrgent: false,
            Note: string.Empty,
            PatientId: pet.Id);

        var picker = new PickDoctorModalViewModel(
            _triage,
            _dialogs,
            pet.Name,
            request,
            async draft =>
            {
                _toast.Show($"ویزیت « {draft.PatientName} » آغاز شد.", Controls.Tone.Ok);
                await _navigation.NavigateToAsync<VisitEncounterViewModel>(vm => vm.LoadAsync(draft));
            });

        _dialogs.ShowModal(picker, 520);
        await picker.LoadAsync();
    }

    [RelayCommand]
    private void AddVax() => _toast.Show("فرم ثبت واکسن جدید (نمونه طراحی)", Controls.Tone.Info);

    [RelayCommand]
    private void CopyMicrochip()
    {
        if (HasMicrochip)
        {
            _toast.Show("شناسه میکروچیپ کپی شد", Controls.Tone.Ok);
        }
    }

    [RelayCommand]
    private void DownloadDoc(DocRowViewModel? doc) =>
        _toast.Show("دانلود فایل (نمونه طراحی)", Controls.Tone.Info);

    [RelayCommand]
    private void UploadDoc() => _toast.Show("بارگذاری فایل (نمونه طراحی)", Controls.Tone.Info);

    [RelayCommand]
    private void CallOwner()
    {
        if (_owner is not null)
        {
            _toast.Show($"در حال برقراری تماس با {Fa(_owner.Mobile1)}", Controls.Tone.Info);
        }
    }

    [RelayCommand]
    private void Print() => _toast.Show("چاپ پرونده (نمونه طراحی)", Controls.Tone.Info);

    [RelayCommand]
    private Task BackToOwner()
    {
        var ownerId = _pet?.OwnerId;
        if (ownerId is null)
        {
            return Task.CompletedTask;
        }

        // The reference closes the pet sheet then reopens the owner sheet
        // (closePetFile(); openOwnerDrawer(...)) so the record surface stays a
        // centered overlay rather than a page navigation.
        return _presenter.OpenOwnerSheetAsync(ownerId);
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();

    // ---- delete (soft) ----

    /// <summary>
    /// Asks for confirmation, then tombstones the pet via <see cref="IPatientsQueryService.DeletePetAsync"/>.
    /// The confirm modal sits on top of this sheet; on confirm it closes itself first, then the sheet
    /// is closed here once the command has succeeded so a failure leaves the file open.
    /// </summary>
    [RelayCommand]
    private void Delete()
    {
        if (_pet is null)
        {
            return;
        }

        var petId = _pet.Id;
        var petName = _pet.Name;
        _dialogs.ShowModal(new ConfirmDialogViewModel(
            _dialogs,
            "danger",
            "IconTrash",
            "حذف پرونده بیمار",
            "پرونده ",
            $"«{petName}»",
            " از فهرست بیماران حذف شود؟",
            "این پرونده از فهرست و پرونده سرپرست حذف می‌شود؛ سوابق ویزیت و واکسیناسیون در تاریخچه باقی می‌ماند.",
            "بله، حذف کن",
            "انصراف",
            () => _ = DeleteConfirmedAsync(petId)), 480);
    }

    private async Task DeleteConfirmedAsync(string petId)
    {
        try
        {
            await _patients.DeletePetAsync(petId);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(
                PatientsPresentation.CommandFailureMessage(ex, "حذف پرونده بیمار انجام نشد؛ دوباره تلاش کنید"),
                Controls.Tone.Danger);
            return;
        }
        catch (Exception)
        {
            _toast.Show("حذف پرونده بیمار انجام نشد؛ دوباره تلاش کنید", Controls.Tone.Danger);
            return;
        }

        _changes.Notify(DataDomain.Patients, DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
        _dialogs.Close();
        _toast.Show("پرونده بیمار حذف شد", Controls.Tone.Ok);
    }

    // ---- helpers ----

    // Alert/status palette, transcribed from patients.css .alert-row.{danger,warn,
    // info,ok} (background, border, foreground). Pre-resolved here so the View
    // binds a brush, never a converter/string.
    // Shared via BrushCache (PERFORMANCE.md §10) — three fixed semantic colours.
    private static readonly Avalonia.Media.IBrush DeltaUp =
        Controls.BrushCache.Solid("#027a48");
    private static readonly Avalonia.Media.IBrush DeltaDown =
        Controls.BrushCache.Solid("#d92d20");
    private static readonly Avalonia.Media.IBrush DeltaFlat =
        Controls.BrushCache.Solid("#667085");

    private static (Avalonia.Media.IBrush Bg, Avalonia.Media.IBrush Border, Avalonia.Media.IBrush Fg) AlertPalette(string cls) => cls switch
    {
        "danger" => (Brush("#fef3f2"), Brush("#f6cfc9"), Brush("#8f2b21")),
        "warn" => (Brush("#fffaeb"), Brush("#f0dcb4"), Brush("#7a5314")),
        "ok" => (Brush("#ecfdf3"), Brush("#c3dfd1"), Brush("#1f6a4d")),
        _ => (Brush("#eff8ff"), Brush("#cfdcf5"), Brush("#2f4f8c")),
    };

    private static Avalonia.Media.IBrush Brush(string hex) =>
        Controls.BrushCache.Solid(hex);

    private static string ToneClass(Controls.Tone tone) => tone switch
    {
        Controls.Tone.Danger => "danger",
        Controls.Tone.Warn => "warn",
        Controls.Tone.Ok => "ok",
        _ => "info",
    };

    private static bool Empty(string? value) => string.IsNullOrWhiteSpace(value);

    private static string Trim(string value) => value.Trim();

    private static string WeightValue(WeightEntry[] ordered) =>
        ordered.Length == 0 ? "—" : Fa(Number(ordered[^1].WeightKg));

    private static string Number(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Fa(string value) => PersianNumberFormatter.ToPersianDigits(value);

    private static bool TryParseJalali(string input, out DateTime date)
    {
        date = default;
        var normalized = PersianNumberFormatter.ToAsciiDigits(input).Trim();
        var parts = normalized.Split('/', '-', '.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var y) ||
            !int.TryParse(parts[1], out var m) ||
            !int.TryParse(parts[2], out var d))
        {
            return false;
        }

        try
        {
            date = new JalaliDate(y, m, d).ToDateTime();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseWeight(string input, out double weight)
    {
        weight = 0;
        var normalized = PersianNumberFormatter.ToAsciiDigits(input)
            .Replace('٫', '.').Replace('،', '.').Trim();
        return double.TryParse(normalized, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out weight);
    }
}
