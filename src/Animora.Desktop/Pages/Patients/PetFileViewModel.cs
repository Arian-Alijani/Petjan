using System.Collections.ObjectModel;
using Animora.Contracts.Patients;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
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

/// <summary>One visit accordion entry (.tl-item).</summary>
public sealed record VisitRowViewModel(
    string Type, string Reason, string DateText, string Vet, string DotClass,
    string Exam, string Diagnosis, IReadOnlyList<string> Prescriptions, string StatsText);

/// <summary>One lab/imaging alert row (documents tab) with pre-resolved palette.</summary>
public sealed record LabRowViewModel(
    string Name, string Result, string DateText,
    Avalonia.Media.IBrush Background, Avalonia.Media.IBrush Border, Avalonia.Media.IBrush Foreground);

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

    public PetFileViewModel(
        IPatientsQueryService patients,
        IRecordSheetPresenter presenter,
        IDialogService dialogs,
        IToastService toast)
    {
        _patients = patients;
        _presenter = presenter;
        _dialogs = dialogs;
        _toast = toast;
        Title = "پرونده بیمار";
        Eyebrow = "بیماران و مالکین";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("بیماران و مالکین");
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
    public RangeObservableCollection<LabRowViewModel> Labs { get; } = [];
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
        var detail = await _patients.GetPetAsync(petId);
        if (detail is null)
        {
            return;
        }

        _owner = await _patients.GetOwnerAsync(detail.OwnerId);
        Apply(detail);
        SelectTab("summary");
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
        BirthText = pet.BirthDate == default ? "—" : $"تولد {JalaliDate.FromDateTime(pet.BirthDate).ToString("short")}";

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
        Vitals.Add(new VitalCellViewModel("دما", "IconThermo", Fa(Number(pet.Vitals.Temperature)), "°C"));
        Vitals.Add(new VitalCellViewModel("ضربان", "IconPulse", Fa(pet.Vitals.Pulse.ToString()), "bpm"));
        Vitals.Add(new VitalCellViewModel("تنفس", "IconLungs", Fa(pet.Vitals.Respiration.ToString()), "/min"));
        Vitals.Add(new VitalCellViewModel("BCS", "IconScale", pet.Vitals.BodyConditionScore, string.Empty));
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
                $"وزن {Fa(Number(v.WeightKg))} کیلو"));
        }

        OnPropertyChanged(nameof(HasVisits));
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
            var (bg, border, fg) = AlertPalette(ToneClass(PatientsPresentation.LabTone(l.Status)));
            return new LabRowViewModel(l.Name, l.Result,
                JalaliDate.FromDateTime(l.Date).ToString("short"), bg, border, fg);
        }));

        Documents.ReplaceAll(pet.Documents.Select(d =>
            new DocRowViewModel(d.Name, $"{d.Size} · {JalaliDate.FromDateTime(d.Date).ToString("short")}")));

        OnPropertyChanged(nameof(HasLabs));
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
                pet.BirthDate == default ? string.Empty : JalaliDate.FromDateTime(pet.BirthDate).ToString("short"),
                "IconCake", pet.BirthDate == default, false));
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
        EditBirth = pet.BirthDate == default ? string.Empty : JalaliDate.FromDateTime(pet.BirthDate).ToString("short");
        EditHabitat = pet.Habitat;
        EditBehavior = pet.Behavior;
        EditDiet = pet.Diet;
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
        };

        var saved = await _patients.SavePetInfoAsync(updated);
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

    [RelayCommand]
    private void AddVisit() => _toast.Show("فرم ثبت ویزیت جدید (نمونه طراحی)", Controls.Tone.Info);

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
