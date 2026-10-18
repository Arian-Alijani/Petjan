using Animora.Contracts.Vaccination;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// The pet file's «ثبت واکسن» modal (Task 9): records one administered dose for THIS pet through
/// <see cref="IVaccinationService.RegisterDoseAsync"/>, so the dose lands in the same VaccinationRecord table the
/// Vaccination module reads — the vax tab, the «واکسن بعدی» KPI and the module's history all stay consistent.
/// </summary>
public sealed partial class RegisterVaxModalViewModel : ObservableObject
{
    private readonly IVaccinationService _service;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly Func<Task> _onRegistered;
    private readonly string _petId;

    public RegisterVaxModalViewModel(
        IVaccinationService service,
        IDialogService dialogs,
        IToastService toast,
        string petId,
        string petName,
        Func<Task> onRegistered)
    {
        _service = service;
        _dialogs = dialogs;
        _toast = toast;
        _petId = petId;
        _onRegistered = onRegistered;
        PetName = petName;
        AdministeredDate = JalaliDate.Today.ToString("short");
    }

    public string PetName { get; }

    [ObservableProperty] private string _vaccineName = string.Empty;
    [ObservableProperty] private string _vaccineCode = string.Empty;
    [ObservableProperty] private string _administeredDate = string.Empty;
    [ObservableProperty] private string _doseNumber = "1";
    [ObservableProperty] private string _totalDoses = string.Empty;
    [ObservableProperty] private string _lot = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _isSubmitting;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    public string SubmitText => IsSubmitting ? "در حال ثبت…" : "ثبت واکسن";

    public bool CanSubmit => !IsSubmitting && !string.IsNullOrWhiteSpace(VaccineName);

    partial void OnVaccineNameChanged(string value) => OnPropertyChanged(nameof(CanSubmit));

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSubmitting)
        {
            return;
        }

        var name = VaccineName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText = "نام واکسن را وارد کنید";
            return;
        }

        if (!TryParseJalali(AdministeredDate, out var date))
        {
            ErrorText = "تاریخ تزریق را از تقویم انتخاب کنید";
            return;
        }

        if (date.Date > DateTime.Today)
        {
            ErrorText = "تاریخ تزریق نمی‌تواند بعد از امروز باشد";
            return;
        }

        var dose = ParseInt(DoseNumber, 1);
        int? total = string.IsNullOrWhiteSpace(TotalDoses) ? null : ParseInt(TotalDoses, 0);
        if (total is <= 0)
        {
            total = null;
        }

        if (total is { } t && dose > t)
        {
            ErrorText = "شماره نوبت نمی‌تواند از تعداد کل نوبت‌ها بیشتر باشد";
            return;
        }

        IsSubmitting = true;
        ErrorText = string.Empty;
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(CanSubmit));
        try
        {
            await _service.RegisterDoseAsync(new VaccinationDoseInput(
                _petId, name, DateOnly.FromDateTime(date), dose, total,
                string.IsNullOrWhiteSpace(VaccineCode) ? null : VaccineCode.Trim(),
                string.IsNullOrWhiteSpace(Lot) ? null : Lot.Trim(),
                string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()));
            _dialogs.CloseModal();
            _toast.Show("واکسن ثبت شد", Controls.Tone.Ok);
            await _onRegistered();
        }
        catch (InvalidOperationException ex)
        {
            ErrorText = PatientsPresentation.CommandFailureMessage(ex, "ثبت واکسن انجام نشد؛ دوباره تلاش کنید");
            IsSubmitting = false;
            OnPropertyChanged(nameof(SubmitText));
            OnPropertyChanged(nameof(CanSubmit));
        }
        catch (Exception)
        {
            ErrorText = "ثبت واکسن انجام نشد؛ دوباره تلاش کنید";
            IsSubmitting = false;
            OnPropertyChanged(nameof(SubmitText));
            OnPropertyChanged(nameof(CanSubmit));
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.CloseModal();

    private static int ParseInt(string input, int fallback)
    {
        var normalized = PersianNumberFormatter.ToAsciiDigits(input).Trim();
        return int.TryParse(normalized, out var value) ? value : fallback;
    }

    private static bool TryParseJalali(string input, out DateTime date)
    {
        date = default;
        var normalized = PersianNumberFormatter.ToAsciiDigits(input).Trim();
        var parts = normalized.Split('/', '-', '.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var y) ||
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
}
