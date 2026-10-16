using Animora.Contracts.Identity;
using Animora.Contracts.Visits;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Visits.Settings;

/// <summary>
/// ViewModel for the Visits <b>settings</b> tab: the clinic's laboratory test types («انواع آزمایش») that the lab
/// form of an encounter offers. Data goes only through <see cref="ILabTestSettingsService"/> (authorized use
/// cases over the local catalog store); a backend failure becomes a toast, never an unhandled exception.
/// </summary>
public sealed partial class VisitsSettingsViewModel : ObservableObject
{
    private readonly ILabTestSettingsService _service;
    private readonly IToastService _toast;
    private readonly ICapabilityGate _capabilities;

    public VisitsSettingsViewModel(ILabTestSettingsService service, IToastService toast, ICapabilityGate capabilities)
    {
        _service = service;
        _toast = toast;
        _capabilities = capabilities;
    }

    public RangeObservableCollection<LabTestTypeItemViewModel> LabTypes { get; } = [];

    /// <summary>Draft text for the «add test type» name input.</summary>
    [ObservableProperty]
    private string _newLabTypeName = string.Empty;

    /// <summary>Draft text for the «add test type» unit input.</summary>
    [ObservableProperty]
    private string _newLabTypeUnit = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Only users with <c>settings.write</c> may add or remove types; others get a read-only list.</summary>
    public bool CanEdit => _capabilities.Can("settings.write");

    public bool IsEmpty => LabTypes.Count == 0 && !IsLoading;

    public string CountText => PersianNumberFormatter.ToPersianDigits(LabTypes.Count) + " نوع";

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>Loads the catalog; called when the tab is first shown and after every change.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var types = await _service.GetTypesAsync();
            LabTypes.ReplaceAll(types.Select(t => new LabTestTypeItemViewModel(t.Id, t.Name, t.DefaultUnit, RemoveTypeAsync)));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CountText));
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "بارگذاری انواع آزمایش ناموفق بود"), Tone.Danger);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private async Task AddLabTypeAsync()
    {
        var name = (NewLabTypeName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (!CanEdit)
        {
            _toast.Show("اجازهٔ ویرایش تنظیمات را ندارید.", Tone.Danger);
            return;
        }

        try
        {
            var unit = (NewLabTypeUnit ?? string.Empty).Trim();
            await _service.AddTypeAsync(name, unit.Length == 0 ? null : unit);
            NewLabTypeName = string.Empty;
            NewLabTypeUnit = string.Empty;
            await LoadAsync();
            _toast.Show("نوع آزمایش جدید اضافه شد", Tone.Ok);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "افزودن نوع آزمایش ناموفق بود"), Tone.Danger);
        }
    }

    private async Task RemoveTypeAsync(LabTestTypeItemViewModel item)
    {
        if (!CanEdit)
        {
            _toast.Show("اجازهٔ ویرایش تنظیمات را ندارید.", Tone.Danger);
            return;
        }

        try
        {
            await _service.RemoveTypeAsync(item.Id);
            await LoadAsync();
            _toast.Show("نوع آزمایش حذف شد", Tone.Ok);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "حذف نوع آزمایش ناموفق بود"), Tone.Danger);
        }
    }
}
