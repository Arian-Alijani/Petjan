using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// One editable clinical-alert line (an allergy or a chronic condition) in the shared row-wise
/// editor used by the pet-registration form, the pet-file info tab and the visit encounter's
/// clinical-context section (Task 3). Each row owns a single free-text value and a remove command
/// that unregisters it from the owning collection.
/// </summary>
public sealed partial class AlertRowEditViewModel : ObservableObject
{
    private readonly Action<AlertRowEditViewModel> _remove;

    public AlertRowEditViewModel(string text, Action<AlertRowEditViewModel> remove)
    {
        _text = text;
        _remove = remove;
    }

    [ObservableProperty] private string _text = string.Empty;

    [RelayCommand]
    private void Remove() => _remove(this);
}
