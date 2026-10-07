using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// The «ثبت واکسن جدید در انبار» modal: adds a clinic-specific vaccine to the writable catalog so it can be picked
/// in the registration modal and shows up in the stock grid. Failures (duplicate code, validation) keep the modal
/// open with a Persian message; success closes it, toasts and asks the page to reload the catalog.
/// </summary>
public sealed partial class AddCatalogVaccineModalViewModel : ObservableObject
{
    private readonly IVaccinationService _service;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly Action _onAdded;

    public AddCatalogVaccineModalViewModel(IVaccinationService service, IDialogService dialogs, IToastService toast, Action onAdded)
    {
        _service = service;
        _dialogs = dialogs;
        _toast = toast;
        _onAdded = onAdded;
    }

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _protects = string.Empty;

    [ObservableProperty]
    private string _totalDoses = "1";

    [ObservableProperty]
    private string _boosterDays = "365";

    [ObservableProperty]
    private string _stock = "0";

    [ObservableProperty]
    private string _minStock = "0";

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isSubmitting;

    partial void OnErrorTextChanged(string value) => HasError = value.Length > 0;

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSubmitting)
        {
            return;
        }

        ErrorText = string.Empty;
        var code = Code.Trim();
        var name = Name.Trim();
        if (code.Length == 0 || name.Length == 0)
        {
            ErrorText = "کد و نام واکسن الزامی است.";
            return;
        }

        if (!TryInt(TotalDoses, 1, 20, out var doses))
        {
            ErrorText = "تعداد نوبت‌های سری باید بین ۱ تا ۲۰ باشد.";
            return;
        }

        if (!TryInt(BoosterDays, 1, 3650, out var booster))
        {
            ErrorText = "فاصلهٔ یادآور باید بین ۱ تا ۳۶۵۰ روز باشد.";
            return;
        }

        if (!TryInt(Stock, 0, 1_000_000, out var stock) || !TryInt(MinStock, 0, 1_000_000, out var minStock))
        {
            ErrorText = "موجودی و حداقل موجودی باید عدد صحیح و نامنفی باشند.";
            return;
        }

        IsSubmitting = true;
        try
        {
            await _service.AddCatalogVaccineAsync(new VaccineCatalogInput(
                code, name, string.IsNullOrWhiteSpace(Protects) ? null : Protects.Trim(), doses, booster, stock, minStock));
        }
        catch (InvalidOperationException ex)
        {
            ErrorText = PatientsPresentation.CommandFailureMessage(ex, "ثبت واکسن ناموفق بود؛ دوباره تلاش کنید.");
            _toast.Show(ErrorText, Tone.Danger);
            return;
        }
        finally
        {
            IsSubmitting = false;
        }

        _dialogs.Close();
        _toast.Show($"واکسن «{name}» به انبار اضافه شد");
        _onAdded();
    }

    private static bool TryInt(string text, int min, int max, out int value) =>
        int.TryParse(PersianNumberFormatter.ToAsciiDigits(text).Trim(), out value) && value >= min && value <= max;
}
