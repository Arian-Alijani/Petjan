using System.Collections.ObjectModel;
using Animora.Contracts.Patients;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients;

/// <summary>One selectable owner row in the wizard's existing-owner picker (.sib-row.selectable).</summary>
public sealed partial class WizardOwnerRowViewModel : ObservableObject
{
    public WizardOwnerRowViewModel(OwnerSummary owner, int petCount)
    {
        Owner = owner;
        Name = $"{owner.FirstName} {owner.LastName}";
        Initials = PatientsPresentation.Initials(owner.FirstName, owner.LastName);
        ToneBrush = PatientsPresentation.ToneBrush(owner.Tone);
        SubText = $"پرونده {owner.File} · {PersianNumberFormatter.ToPersianDigits(owner.Mobile1)} · "
                  + $"{PersianNumberFormatter.ToPersianDigits(petCount.ToString())} حیوان";
    }

    public OwnerSummary Owner { get; }
    public string Name { get; }
    public string Initials { get; }
    public Avalonia.Media.IBrush ToneBrush { get; }
    public string SubText { get; }

    [ObservableProperty] private bool _isPicked;
}

/// <summary>
/// What the wizard created, so a caller that launched it from another flow (the booking panel) can pick
/// the new patient straight away instead of searching for it again.
/// </summary>
/// <param name="OwnerId">The owner the pet belongs to (new or pre-existing).</param>
/// <param name="PetId">The created pet's id, or null if the wizard produced no pet.</param>
/// <param name="PetName">Display name of the pet.</param>
public sealed record AddPatientWizardResult(string OwnerId, string? PetId, string PetName);

/// <summary>
/// Backs the add-owner/add-pet wizard modal (Phase 09, patients-page.js
/// <c>openOwnerModal</c>/<c>setWizMode</c>/<c>renderWizOwnerList</c>/<c>paintWiz</c>):
/// a 2-step flow — owner info (new owner OR pick an existing one) then pet info.
/// </summary>
public sealed partial class AddPatientWizardViewModel : ObservableObject
{
    private readonly IPatientsQueryService _patients;
    private readonly IDialogService _dialogs;
    private readonly Action<AddPatientWizardResult> _onCompleted;
    private readonly bool _closeModalOnly;
    private IReadOnlyList<OwnerSummary> _allOwners = [];
    private IReadOnlyList<PetSummary> _allPets = [];

    [ObservableProperty] private int _step = 1;
    [ObservableProperty] private bool _isExistingMode;
    [ObservableProperty] private string _ownerSearch = string.Empty;
    [ObservableProperty] private WizardOwnerRowViewModel? _selectedOwner;

    // new-owner fields
    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;
    [ObservableProperty] private string _nationalId = string.Empty;
    [ObservableProperty] private string _mobile1 = string.Empty;
    [ObservableProperty] private string _mobile2 = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private bool _ownerIsFemale = true;
    [ObservableProperty] private string _referral = string.Empty;

    /// <summary>Standalone use (Patients page): closes every overlay when done and ignores the result.</summary>
    public AddPatientWizardViewModel(
        IPatientsQueryService patients,
        IDialogService dialogs,
        Action onCompleted)
        : this(patients, dialogs, _ => onCompleted(), closeModalOnly: false)
    {
    }

    /// <summary>
    /// Nested use: <paramref name="closeModalOnly"/> keeps a sheet/drawer that launched the wizard open
    /// (and its state intact), and <paramref name="onCompleted"/> receives what was created.
    /// </summary>
    public AddPatientWizardViewModel(
        IPatientsQueryService patients,
        IDialogService dialogs,
        Action<AddPatientWizardResult> onCompleted,
        bool closeModalOnly)
    {
        _patients = patients;
        _dialogs = dialogs;
        _onCompleted = onCompleted;
        _closeModalOnly = closeModalOnly;
        Referral = PatientsPresentation.Referrals[0];
        // Non-blocking init (PERFORMANCE.md §4): load the owner/pet lookups
        // asynchronously instead of blocking the UI thread in the constructor.
        _ = LoadLookupsAsync();
    }

    private async Task LoadLookupsAsync()
    {
        _allOwners = await _patients.GetOwnersAsync();
        _allPets = await _patients.GetPetsAsync();
        RenderOwnerList();
    }

    public PetFormViewModel Pet { get; } = new();

    public Animora.Desktop.Controls.RangeObservableCollection<WizardOwnerRowViewModel> OwnerRows { get; } = [];
    public IReadOnlyList<string> Referrals => PatientsPresentation.Referrals;

    public bool IsNewMode => !IsExistingMode;
    public bool IsStepOne => Step == 1;
    public bool IsStepTwo => Step == 2;
    public string FileBadge => "پرونده ۷۲-۱۱۲۰";

    public string ModeHint => IsExistingMode
        ? "مالک موردنظر را از فهرست زیر جستجو و انتخاب کنید تا حیوان جدید به همان پرونده اضافه شود."
        : "اگر این مالک برای اولین‌بار مراجعه می‌کند، اطلاعات هویتی و تماس او را کامل کنید.";

    public string NextLabel => IsStepOne ? "ادامه — اطلاعات حیوان" : "ثبت پرونده";
    public bool IsNextWithCheck => Step == 2;

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStepOne));
        OnPropertyChanged(nameof(IsStepTwo));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(IsNextWithCheck));
    }

    partial void OnIsExistingModeChanged(bool value)
    {
        if (value)
        {
            RenderOwnerList();
        }

        OnPropertyChanged(nameof(IsNewMode));
        OnPropertyChanged(nameof(ModeHint));
    }

    partial void OnOwnerSearchChanged(string value) => RenderOwnerList();

    [RelayCommand]
    private void SetMode(string? mode) => IsExistingMode = mode == "existing";

    /// <summary>Owner-gender radio pills in step 1: "female" | "male".</summary>
    [RelayCommand]
    private void SetOwnerGender(string? gender) => OwnerIsFemale = gender != "male";

    [RelayCommand]
    private void PickOwner(WizardOwnerRowViewModel? row)
    {
        SelectedOwner = row;
        foreach (var candidate in OwnerRows)
        {
            candidate.IsPicked = ReferenceEquals(candidate, row);
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 1)
        {
            Step--;
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (Step == 1)
        {
            if (IsExistingMode && SelectedOwner is null)
            {
                return;
            }

            Step = 2;
            return;
        }

        await CommitAsync();
    }

    [RelayCommand]
    private void Cancel() => CloseSelf();

    private void CloseSelf()
    {
        if (_closeModalOnly)
        {
            _dialogs.CloseModal();
        }
        else
        {
            _dialogs.Close();
        }
    }

    private async Task CommitAsync()
    {
        AddPatientWizardResult result;
        if (IsExistingMode && SelectedOwner is not null)
        {
            var pet = await _patients.CreatePetAsync(SelectedOwner.Owner.Id, Pet.BuildRequest());
            result = new AddPatientWizardResult(SelectedOwner.Owner.Id, pet.Id, pet.Name);
        }
        else
        {
            var owner = new NewOwnerRequest(
                FirstName.Trim(), LastName.Trim(),
                OwnerIsFemale ? OwnerGender.Female : OwnerGender.Male,
                NationalId.Trim(), Mobile1.Trim(), Blank(Mobile2), Blank(Phone), Blank(Address),
                Blank(Referral), Pet.BuildRequest());
            var created = await _patients.CreateOwnerAsync(owner);
            var firstPet = created.Pets.Count > 0 ? created.Pets[0] : null;
            result = new AddPatientWizardResult(created.Id, firstPet?.Id, firstPet?.Name ?? string.Empty);
        }

        CloseSelf();
        _onCompleted(result);
    }

    private void RenderOwnerList()
    {
        var needle = PersianText.Normalize(OwnerSearch);
        var petCounts = _allPets.ToLookup(p => p.OwnerId);
        var rows = new List<WizardOwnerRowViewModel>();
        foreach (var owner in _allOwners)
        {
            var count = petCounts[owner.Id].Count();
            var haystack = PersianText.Normalize(
                $"{owner.FirstName} {owner.LastName} {owner.File} {owner.Mobile1} {owner.NationalId}");
            if (needle.Length > 0 && !PersianText.Contains(haystack, needle))
            {
                continue;
            }

            var row = new WizardOwnerRowViewModel(owner, count);
            if (SelectedOwner is not null && SelectedOwner.Owner.Id == owner.Id)
            {
                row.IsPicked = true;
            }

            rows.Add(row);
        }
        OwnerRows.ReplaceAll(rows);

        OnPropertyChanged(nameof(HasOwnerRows));
    }

    public bool HasOwnerRows => OwnerRows.Count > 0;

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
