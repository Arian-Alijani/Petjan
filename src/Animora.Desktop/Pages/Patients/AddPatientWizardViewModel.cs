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
/// Backs the add-owner/add-pet wizard modal (Phase 09, patients-page.js
/// <c>openOwnerModal</c>/<c>setWizMode</c>/<c>renderWizOwnerList</c>/<c>paintWiz</c>):
/// a 2-step flow — owner info (new owner OR pick an existing one) then pet info.
/// </summary>
public sealed partial class AddPatientWizardViewModel : ObservableObject
{
    private readonly IPatientsQueryService _patients;
    private readonly IDialogService _dialogs;
    private readonly Action _onCompleted;
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

    public AddPatientWizardViewModel(
        IPatientsQueryService patients,
        IDialogService dialogs,
        Action onCompleted)
    {
        _patients = patients;
        _dialogs = dialogs;
        _onCompleted = onCompleted;
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

    public ObservableCollection<WizardOwnerRowViewModel> OwnerRows { get; } = [];
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
    private void Cancel() => _dialogs.Close();

    private async Task CommitAsync()
    {
        if (IsExistingMode && SelectedOwner is not null)
        {
            await _patients.CreatePetAsync(SelectedOwner.Owner.Id, Pet.BuildRequest());
        }
        else
        {
            var owner = new NewOwnerRequest(
                FirstName.Trim(), LastName.Trim(),
                OwnerIsFemale ? OwnerGender.Female : OwnerGender.Male,
                NationalId.Trim(), Mobile1.Trim(), Blank(Mobile2), Blank(Phone), Blank(Address),
                Blank(Referral), Pet.BuildRequest());
            await _patients.CreateOwnerAsync(owner);
        }

        _dialogs.Close();
        _onCompleted();
    }

    private void RenderOwnerList()
    {
        var needle = PersianText.Normalize(OwnerSearch);
        OwnerRows.Clear();
        foreach (var owner in _allOwners)
        {
            var count = _allPets.Count(p => p.OwnerId == owner.Id);
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

            OwnerRows.Add(row);
        }

        OnPropertyChanged(nameof(HasOwnerRows));
    }

    public bool HasOwnerRows => OwnerRows.Count > 0;

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
