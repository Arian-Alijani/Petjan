using Animora.Contracts.Visits;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// One editable row of the lab form: a test type picked from the clinic's configured catalog (Visits → تنظیمات)
/// plus the measured value and its unit (pre-filled from the type's default unit).
/// </summary>
public sealed partial class LabRowEditorViewModel : ObservableObject
{
    private readonly Action<LabRowEditorViewModel> _remove;

    public LabRowEditorViewModel(RangeObservableCollection<LabTestType> types, Action<LabRowEditorViewModel> remove)
    {
        Types = types;
        _remove = remove;
    }

    /// <summary>The selectable test types; shared with the sheet so a late catalog load reaches every row.</summary>
    public RangeObservableCollection<LabTestType> Types { get; }

    [ObservableProperty]
    private LabTestType? _selectedType;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string _unit = string.Empty;

    // Keep a unit the doctor typed by hand; only replace an empty one or the previous type's default.
    partial void OnSelectedTypeChanged(LabTestType? oldValue, LabTestType? newValue)
    {
        if (newValue is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Unit) || string.Equals(Unit, oldValue?.DefaultUnit, StringComparison.Ordinal))
        {
            Unit = newValue.DefaultUnit ?? string.Empty;
        }
    }

    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>
/// The «ثبت آزمایش» sheet opened over the encounter: Jalali date, a free-text description and a repeating
/// {test type + value} row editor fed by <see cref="ILabTestSettingsService"/>. Rows are validated against the same
/// limits the backend enforces so a bad form is explained inline instead of failing after the round trip.
/// </summary>
public sealed partial class LabSheetViewModel : ObservableObject
{
    private const int MaxRows = 100;
    private const int MaxValue = 200;
    private const int MaxUnit = 40;
    private const int MaxText = 4000;

    private readonly ILabTestSettingsService _labSettings;
    private readonly IDialogService _dialogs;
    private readonly Func<LabEntry, Task> _onSave;

    public LabSheetViewModel(
        ILabTestSettingsService labSettings,
        IDialogService dialogs,
        Func<LabEntry, Task> onSave)
    {
        _labSettings = labSettings;
        _dialogs = dialogs;
        _onSave = onSave;
        _dateValue = JalaliCalendar.Today;
        Rows.Add(NewRow());
        _ = LoadTypesAsync();
    }

    public RangeObservableCollection<LabTestType> Types { get; } = [];

    public RangeObservableCollection<LabRowEditorViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _dateValue;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTypes))]
    private bool _typesLoaded;

    public bool IsIdle => !IsBusy;

    /// <summary>True once the catalog loaded and is empty — the form then points the user at the settings tab.</summary>
    public bool HasNoTypes => TypesLoaded && Types.Count == 0;

    private LabRowEditorViewModel NewRow() => new(Types, RemoveRow);

    private async Task LoadTypesAsync()
    {
        try
        {
            Types.ReplaceAll(await _labSettings.GetTypesAsync());
        }
        catch (InvalidOperationException ex)
        {
            Error = PatientsPresentation.CommandFailureMessage(ex, "بارگذاری انواع آزمایش ناموفق بود؛ دوباره تلاش کنید.");
        }
        finally
        {
            TypesLoaded = true;
            OnPropertyChanged(nameof(HasNoTypes));
        }
    }

    [RelayCommand]
    private void AddRow()
    {
        if (Rows.Count >= MaxRows)
        {
            Error = $"حداکثر {PersianNumberFormatter.ToPersianDigits(MaxRows)} ردیف آزمایش مجاز است.";
            return;
        }

        Rows.Add(NewRow());
    }

    private void RemoveRow(LabRowEditorViewModel row)
    {
        Rows.Remove(row);

        // The form always keeps one row to fill in; removing the last one just resets it.
        if (Rows.Count == 0)
        {
            Rows.Add(NewRow());
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Error = string.Empty;
        var description = (Description ?? string.Empty).Trim();
        var items = new List<LabItem>();

        if (!ParaclinicPresentation.TryParseJalali(DateValue, out var takenAt))
        {
            Error = "تاریخ آزمایش را انتخاب کنید.";
        }
        else if (description.Length > MaxText)
        {
            Error = $"توضیحات نباید از {PersianNumberFormatter.ToPersianDigits(MaxText)} نویسه بیشتر باشد.";
        }
        else
        {
            foreach (var row in Rows)
            {
                var value = PersianNumberFormatter.ToAsciiDigits((row.Value ?? string.Empty).Trim());
                var unit = (row.Unit ?? string.Empty).Trim();

                // A completely untouched row is ignored, a half-filled one is an error.
                if (row.SelectedType is null && value.Length == 0)
                {
                    continue;
                }

                if (row.SelectedType is null)
                {
                    Error = "برای هر مقدار واردشده، نوع آزمایش را انتخاب کنید.";
                }
                else if (value.Length == 0)
                {
                    Error = $"مقدار «{row.SelectedType.Name}» را وارد کنید.";
                }
                else if (value.Length > MaxValue)
                {
                    Error = $"مقدار «{row.SelectedType.Name}» بیش از حد طولانی است.";
                }
                else if (unit.Length > MaxUnit)
                {
                    Error = $"واحد «{row.SelectedType.Name}» بیش از حد طولانی است.";
                }

                if (Error.Length > 0)
                {
                    break;
                }

                items.Add(new LabItem(row.SelectedType!.Id, row.SelectedType.Name, value, unit.Length == 0 ? null : unit));
            }

            if (Error.Length == 0 && items.Count == 0)
            {
                Error = "حداقل یک آزمایش با مقدار آن را ثبت کنید.";
            }
        }

        if (Error.Length > 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _onSave(new LabEntry(
                Id: string.Empty,
                TakenAt: takenAt,
                Description: description.Length == 0 ? null : description,
                Items: items));
            _dialogs.Close();
        }
        catch (InvalidOperationException ex)
        {
            Error = PatientsPresentation.CommandFailureMessage(ex, "ثبت آزمایش ناموفق بود؛ دوباره تلاش کنید.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
