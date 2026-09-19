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

/// <summary>
/// One row in the Owner Sheet's «حیوانات» list (patients-page.js .od-pet):
/// species glyph + tone, name + sex dot, "species · breed · age" sub-line,
/// the status pill and a trailing chevron.
/// </summary>
public sealed class OwnerSheetPetRowViewModel
{
    public OwnerSheetPetRowViewModel(PetSummary pet)
    {
        Pet = pet;
        Name = pet.Name;
        Sex = PatientsPresentation.SexDot(pet.Gender);
        SpeciesLabel = PatientsPresentation.SpeciesLabel(pet.Species);
        SpeciesIconKey = PatientsPresentation.SpeciesIconKey(pet.Species);
        ToneBrush = PatientsPresentation.ToneBrush(pet.Tone);
        var age = RelativeDateFormatter.Age(pet.BirthDate);
        SubText = $"{SpeciesLabel} · {pet.Breed} · {age.Label}";
        StatusText = PatientsPresentation.StatusText(pet.Status);
        StatusTone = PatientsPresentation.StatusTone(pet.Status);
    }

    public PetSummary Pet { get; }
    public string Name { get; }
    public PatientsPresentation.SexDotInfo Sex { get; }
    public string SpeciesLabel { get; }
    public string SpeciesIconKey { get; }
    public IBrush ToneBrush { get; }
    public string SubText { get; }
    public string StatusText { get; }
    public Tone StatusTone { get; }
}

/// <summary>
/// Backs the Owner Sheet (Phase 09, patients-page.js <c>renderOwnerSheet</c>):
/// the owner's identity/contact/address blocks (view ↔ inline edit), the owner's
/// pet roster and the activity summary. Data comes only from
/// <see cref="IPatientsQueryService"/> — the mock is the sole swap point.
/// </summary>
public sealed partial class OwnerSheetViewModel : PageViewModelBase
{
    private readonly IPatientsQueryService _patients;
    private readonly IRecordSheetPresenter _presenter;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;

    private OwnerDetail? _owner;

    [ObservableProperty]
    private bool _isEditing;

    // ---- edit buffers ----
    [ObservableProperty] private string _editFirstName = string.Empty;
    [ObservableProperty] private string _editLastName = string.Empty;
    [ObservableProperty] private string _editNationalId = string.Empty;
    [ObservableProperty] private bool _editIsFemale;
    [ObservableProperty] private string _editMobile1 = string.Empty;
    [ObservableProperty] private string _editMobile2 = string.Empty;
    [ObservableProperty] private string _editPhone = string.Empty;
    [ObservableProperty] private string _editAddress = string.Empty;
    [ObservableProperty] private string _editReferral = string.Empty;

    public OwnerSheetViewModel(
        IPatientsQueryService patients,
        IRecordSheetPresenter presenter,
        IDialogService dialogs,
        IToastService toast)
    {
        _patients = patients;
        _presenter = presenter;
        _dialogs = dialogs;
        _toast = toast;
        Title = "پرونده مالک";
        Eyebrow = "بیماران و مالکین";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("بیماران و مالکین");
        Breadcrumbs.Add("پرونده مالک");
    }

    public ObservableCollection<OwnerSheetPetRowViewModel> Pets { get; } = [];

    public IReadOnlyList<string> Referrals => PatientsPresentation.Referrals;

    // ---- header ----
    public string Name { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Initials { get; private set; } = "؟";
    public IBrush ToneBrush { get; private set; } = new SolidColorBrush(Colors.Gray);
    public string File { get; private set; } = string.Empty;
    public string FileText { get; private set; } = string.Empty;
    public string GenderLabel { get; private set; } = string.Empty;
    public string SinceText { get; private set; } = string.Empty;

    // ---- view rows ----
    public string NationalIdText { get; private set; } = string.Empty;
    public string Mobile1Text { get; private set; } = string.Empty;
    public string Mobile2Text { get; private set; } = string.Empty;
    public string PhoneText { get; private set; } = string.Empty;
    public string AddressText { get; private set; } = string.Empty;
    public string ReferralText { get; private set; } = string.Empty;

    // ---- activity ----
    public string VisitsCountText { get; private set; } = "۰";
    public string PetsCountText { get; private set; } = "۰";
    public string LastVisitText { get; private set; } = "—";

    public bool HasPets => Pets.Count > 0;
    public bool IsViewing => !IsEditing;

    /// <summary>
    /// Loads the owner for the sheet; awaited by the caller before the sheet is
    /// shown. Never blocks the UI thread (PERFORMANCE.md §4): a Datasync/SQLite-
    /// backed service keeps the shell responsive while the record is fetched.
    /// </summary>
    public async Task LoadAsync(string ownerId)
    {
        var detail = await _patients.GetOwnerAsync(ownerId);
        if (detail is null)
        {
            return;
        }

        Apply(detail);
    }

    private void Apply(OwnerDetail owner)
    {
        _owner = owner;

        Name = $"{owner.FirstName} {owner.LastName}";
        FirstName = owner.FirstName;
        LastName = owner.LastName;
        Initials = PatientsPresentation.Initials(owner.FirstName, owner.LastName);
        ToneBrush = PatientsPresentation.ToneBrush(owner.Tone);
        File = owner.File;
        FileText = $"پرونده {owner.File}";
        GenderLabel = PatientsPresentation.OwnerGenderLabel(owner.Gender);
        SinceText = $"مشتری از {JalaliDate.FromDateTime(owner.Since).ToString("short")}";

        NationalIdText = PersianNumberFormatter.ToPersianDigits(owner.NationalId);
        Mobile1Text = Fa(owner.Mobile1);
        Mobile2Text = OrRecorded(Fa(owner.Mobile2));
        PhoneText = OrRecorded(Fa(owner.Phone));
        AddressText = OrRecorded(owner.Address);
        ReferralText = OrRecorded(owner.Referral);

        Pets.Clear();
        foreach (var pet in owner.Pets)
        {
            Pets.Add(new OwnerSheetPetRowViewModel(pet));
        }

        PetsCountText = Fa(owner.Pets.Count.ToString());
        VisitsCountText = "۰"; // visits live in another context (Phase 10/11); none yet
        var last = owner.Pets
            .Where(p => p.LastVisitDate is not null)
            .OrderByDescending(p => p.LastVisitDate)
            .FirstOrDefault();
        LastVisitText = last?.LastVisitDate is { } date ? RelativeDateFormatter.Since(date) : "—";

        Title = Name;

        FillEditBuffers(owner);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(FirstName));
        OnPropertyChanged(nameof(LastName));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(ToneBrush));
        OnPropertyChanged(nameof(File));
        OnPropertyChanged(nameof(FileText));
        OnPropertyChanged(nameof(GenderLabel));
        OnPropertyChanged(nameof(SinceText));
        OnPropertyChanged(nameof(NationalIdText));
        OnPropertyChanged(nameof(Mobile1Text));
        OnPropertyChanged(nameof(Mobile2Text));
        OnPropertyChanged(nameof(PhoneText));
        OnPropertyChanged(nameof(AddressText));
        OnPropertyChanged(nameof(ReferralText));
        OnPropertyChanged(nameof(VisitsCountText));
        OnPropertyChanged(nameof(PetsCountText));
        OnPropertyChanged(nameof(LastVisitText));
        OnPropertyChanged(nameof(HasPets));
    }

    private void FillEditBuffers(OwnerDetail owner)
    {
        EditFirstName = owner.FirstName;
        EditLastName = owner.LastName;
        EditNationalId = owner.NationalId;
        EditIsFemale = owner.Gender == OwnerGender.Female;
        EditMobile1 = owner.Mobile1;
        EditMobile2 = owner.Mobile2 ?? string.Empty;
        EditPhone = owner.Phone ?? string.Empty;
        EditAddress = owner.Address ?? string.Empty;
        EditReferral = owner.Referral ?? PatientsPresentation.Referrals[0];
    }

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsViewing));

    [RelayCommand]
    private void Edit() => IsEditing = true;

    [RelayCommand]
    private void CancelEdit()
    {
        if (_owner is not null)
        {
            FillEditBuffers(_owner);
        }

        IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_owner is null)
        {
            return;
        }

        var updated = _owner with
        {
            FirstName = EditFirstName.Trim(),
            LastName = EditLastName.Trim(),
            NationalId = EditNationalId.Trim(),
            Gender = EditIsFemale ? OwnerGender.Female : OwnerGender.Male,
            Mobile1 = EditMobile1.Trim(),
            Mobile2 = NullIfBlank(EditMobile2),
            Phone = NullIfBlank(EditPhone),
            Address = NullIfBlank(EditAddress),
            Referral = NullIfBlank(EditReferral),
        };

        var saved = await _patients.SaveOwnerAsync(updated);
        Apply(saved);
        IsEditing = false;
        _toast.Show("اطلاعات مالک به‌روزرسانی شد", Tone.Ok);
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();

    [RelayCommand]
    private void Call()
    {
        if (_owner is not null)
        {
            _toast.Show($"در حال برقراری تماس با {Fa(_owner.Mobile1)}", Tone.Info);
        }
    }

    [RelayCommand]
    private void Sms() => _toast.Show("پیامک برای مالک ارسال شد", Tone.Info);

    [RelayCommand]
    private void Remind() => _toast.Show("یادآوری برای مالک ثبت شد", Tone.Info);

    [RelayCommand]
    private void AddPet()
    {
        if (_owner is null)
        {
            return;
        }

        var owner = _owner;
        _dialogs.ShowModal(new AddPetModalViewModel(
            owner,
            async req =>
            {
                await _patients.CreatePetAsync(owner.Id, req);
                await LoadAsync(owner.Id);
                _toast.Show("حیوان جدید به پرونده مالک اضافه شد", Tone.Ok);
            },
            _dialogs));
    }

    [RelayCommand]
    private Task OpenPet(OwnerSheetPetRowViewModel? row) =>
        row is not null ? _presenter.OpenPetSheetAsync(row.Pet.Id) : Task.CompletedTask;

    private static string OrRecorded(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "ثبت نشده" : value;

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Fa(string? value) =>
        PersianNumberFormatter.ToPersianDigits(value ?? string.Empty);
}
