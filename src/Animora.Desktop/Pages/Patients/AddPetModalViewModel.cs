using Animora.Contracts.Patients;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// Backs the "add pet to an existing owner" modal (Phase 09, patients-page.js
/// <c>openPetModal</c>): a compact owner preview plus the same pet fields the
/// wizard's step 2 uses.
/// </summary>
public sealed partial class AddPetModalViewModel : ObservableObject
{
    private readonly Action<NewPetRequest> _submit;
    private readonly IDialogService _dialogs;

    public AddPetModalViewModel(
        OwnerDetail owner,
        Action<NewPetRequest> submit,
        IDialogService dialogs)
    {
        _submit = submit;
        _dialogs = dialogs;

        OwnerName = $"{owner.FirstName} {owner.LastName}";
        OwnerInitials = PatientsPresentation.Initials(owner.FirstName, owner.LastName);
        OwnerToneBrush = PatientsPresentation.ToneBrush(owner.Tone);
        OwnerSubText = $"پرونده {owner.File} · {Localization.PersianNumberFormatter.ToPersianDigits(owner.Mobile1)}";
        OwnerPetCountText = $"{Localization.PersianNumberFormatter.ToPersianDigits(owner.Pets.Count.ToString())} حیوان فعلی";
    }

    public PetFormViewModel Pet { get; } = new();

    public string OwnerName { get; }
    public string OwnerInitials { get; }
    public Avalonia.Media.IBrush OwnerToneBrush { get; }
    public string OwnerSubText { get; }
    public string OwnerPetCountText { get; }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    [RelayCommand]
    private void Save()
    {
        if (!Pet.IsValid)
        {
            return;
        }

        _submit(Pet.BuildRequest());
        _dialogs.Close();
    }
}
