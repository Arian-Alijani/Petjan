using Animora.Contracts.Vaccination;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// The «ثبت واکسن جدید» modal, mirroring the reference's <c>openVaxModal</c>
/// (Animora-UI/assets/vaccination.js): the patient search field, the vaccine
/// type / dose / dates / vet / lot fields and the send-confirmation toggle.
/// The submit is a mock/no-op that shows the reference's confirmation toast
/// (per the phase file's out-of-scope note), but every field and control the
/// reference renders is present.
/// </summary>
public sealed partial class AddVaccineModalViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;

    public AddVaccineModalViewModel(
        IReadOnlyList<VaccineCatalogEntry> catalog,
        IDialogService dialogs,
        IToastService toast)
    {
        _dialogs = dialogs;
        _toast = toast;
        Catalog = catalog;
        SelectedVaccine = catalog.Count > 0 ? catalog[0] : null;
    }

    public IReadOnlyList<VaccineCatalogEntry> Catalog { get; }

    public IReadOnlyList<string> Doses { get; } = ["نوبت اول", "یادآور اول", "یادآور دوم", "نوبت سالانه"];

    public IReadOnlyList<string> Vets { get; } = ["دکتر آریا نیک", "دکتر سمانه راد"];

    [ObservableProperty]
    private string _patientQuery = string.Empty;

    [ObservableProperty]
    private VaccineCatalogEntry? _selectedVaccine;

    [ObservableProperty]
    private string _selectedDose = "نوبت اول";

    [ObservableProperty]
    private string _injectionDate = string.Empty;

    [ObservableProperty]
    private string _nextDueDate = string.Empty;

    [ObservableProperty]
    private string _selectedVet = "دکتر آریا نیک";

    [ObservableProperty]
    private string _lot = string.Empty;

    [ObservableProperty]
    private bool _sendConfirmation = true;

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    [RelayCommand]
    private void Submit()
    {
        _dialogs.Close();
        _toast.Show("واکسن ثبت شد (نمونه طراحی)");
    }
}
