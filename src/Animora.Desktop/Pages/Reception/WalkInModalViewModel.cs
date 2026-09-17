using Animora.Contracts.Reception;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Reception;

public sealed partial class WalkInModalViewModel : ObservableObject
{
    private readonly Func<WalkInRequest, Task> _submit;
    private readonly IDialogService _dialog;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _owner = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private ReceptionDoctor? _selectedDoctor;

    [ObservableProperty]
    private bool _isUrgent;

    [ObservableProperty]
    private bool _isSaving;

    public WalkInModalViewModel(
        IReadOnlyList<ReceptionDoctor> doctors,
        Func<WalkInRequest, Task> submit,
        IDialogService dialog)
    {
        Doctors = doctors;
        SelectedDoctor = doctors.FirstOrDefault();
        _submit = submit;
        _dialog = dialog;
    }

    public IReadOnlyList<ReceptionDoctor> Doctors { get; }

    public bool IsNormal
    {
        get => !IsUrgent;
        set => IsUrgent = !value;
    }

    partial void OnIsUrgentChanged(bool value) => OnPropertyChanged(nameof(IsNormal));

    [RelayCommand]
    private void Cancel() => _dialog.Close();

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving || SelectedDoctor is null)
        {
            return;
        }

        IsSaving = true;
        try
        {
            await _submit(new WalkInRequest(Name, Owner, Phone, SelectedDoctor.Id, IsUrgent));
            _dialog.Close();
        }
        finally
        {
            IsSaving = false;
        }
    }
}
