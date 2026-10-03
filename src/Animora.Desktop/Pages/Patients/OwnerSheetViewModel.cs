using System.Collections.ObjectModel;
using Animora.Contracts.Patients;
using Animora.Contracts.Sales;
using Animora.Desktop.Pages.Sales;
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

/// <summary>One row of the owner file's «فاکتورها» card — a real invoice registered for this owner.</summary>
public sealed class OwnerInvoiceRowViewModel
{
    public OwnerInvoiceRowViewModel(InvoiceSummary invoice)
    {
        Invoice = invoice;
        Id = invoice.Id;
        var number = string.IsNullOrWhiteSpace(invoice.Number) ? invoice.Id : invoice.Number;
        Title = $"فاکتور {PersianNumberFormatter.ToPersianDigits(number)}";
        var kind = invoice.Kind == InvoiceKind.Visit ? "از ویزیت" : "فروش";
        var when = invoice.IssuedAt is { } d ? JalaliDate.FromDateTime(d).ToString("short") : "—";
        var pet = string.IsNullOrWhiteSpace(invoice.PetName) ? string.Empty : $" · {invoice.PetName}";
        SubText = $"{PersianNumberFormatter.ToPersianDigits(when)} · {kind}{pet}";
        TotalText = SalePresentation.Money(invoice.Total);
        HasDue = invoice.Due > 0 && invoice.State is InvoiceState.Draft or InvoiceState.Partial;
        DueText = $"مانده {SalePresentation.Money(invoice.Due)}";
        StateText = SalePresentation.StateLabel(invoice.State);
        StateTone = SalePresentation.StateTone(invoice.State);
    }

    public InvoiceSummary Invoice { get; }
    public string Id { get; }
    public string Title { get; }
    public string SubText { get; }
    public string TotalText { get; }
    public bool HasDue { get; }
    public string DueText { get; }
    public string StateText { get; }
    public Tone StateTone { get; }
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
    private readonly IDataChangeNotifier _changes;
    private readonly ISalesQueryService _sales;

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
        IToastService toast,
        IDataChangeNotifier changes,
        ISalesQueryService sales)
    {
        _sales = sales;
        _patients = patients;
        _changes = changes;
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

    public ObservableCollection<OwnerInvoiceRowViewModel> Invoices { get; } = [];

    public bool HasInvoices => Invoices.Count > 0;
    public string InvoicesCountText { get; private set; } = "۰";

    public IReadOnlyList<string> Referrals => PatientsPresentation.Referrals;

    // ---- header ----
    public string Name { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Initials { get; private set; } = "؟";
    public IBrush ToneBrush { get; private set; } = BrushCache.Solid("#808080");
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
        await LoadInvoicesAsync(detail.Id);
    }

    private async Task LoadInvoicesAsync(string ownerId)
    {
        var invoices = await _sales.GetInvoicesForOwnerAsync(ownerId);
        Invoices.Clear();
        foreach (var invoice in invoices)
        {
            Invoices.Add(new OwnerInvoiceRowViewModel(invoice));
        }

        InvoicesCountText = Fa(Invoices.Count.ToString());
        OnPropertyChanged(nameof(InvoicesCountText));
        OnPropertyChanged(nameof(HasInvoices));
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
        _changes.Notify(DataDomain.Patients, DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
        Apply(saved);
        IsEditing = false;
        _toast.Show("اطلاعات مالک به‌روزرسانی شد", Tone.Ok);
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();

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
                _changes.Notify(DataDomain.Patients, DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits);
                await LoadAsync(owner.Id);
                _toast.Show("حیوان جدید به پرونده مالک اضافه شد", Tone.Ok);
            },
            _dialogs, _toast));
    }

    [RelayCommand]
    private void OpenInvoice(OwnerInvoiceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var ownerId = _owner?.Id;
        _dialogs.ShowSheet(
            new InvoiceViewerViewModel(
                _sales, _toast, _dialogs, row.Id,
                () =>
                {
                    if (ownerId is not null)
                    {
                        _ = LoadInvoicesAsync(ownerId);
                    }
                }),
            1040,
            720);
    }

    [RelayCommand]
    private Task OpenPet(OwnerSheetPetRowViewModel? row) =>
        row is not null ? _presenter.OpenPetSheetAsync(row.Pet.Id) : Task.CompletedTask;

    /// <summary>True when <paramref name="value"/> is real data (not blank / the «ثبت نشده» placeholder) and the sheet is not being edited.</summary>
    public bool CanCopy(string? value) =>
        IsViewing && !string.IsNullOrWhiteSpace(value) && value != NotRecorded && value != "—";

    /// <summary>Confirms a successful clipboard copy (the clipboard itself is written by the view).</summary>
    public void NotifyCopied(string? label) =>
        _toast.Show(string.IsNullOrWhiteSpace(label) ? "کپی شد" : $"{label} کپی شد", Tone.Ok);

    private const string NotRecorded = "ثبت نشده";

    private static string OrRecorded(string? value) =>
        string.IsNullOrWhiteSpace(value) ? NotRecorded : value;

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Fa(string? value) =>
        PersianNumberFormatter.ToPersianDigits(value ?? string.Empty);
}
