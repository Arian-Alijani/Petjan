using System;
using Animora.Contracts.Patients;
using Animora.Desktop.Controls;
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
    private readonly Func<NewPetRequest, Task> _submit;
    private readonly IDialogService _dialogs;
    private readonly IToastService? _toast;

    public AddPetModalViewModel(
        OwnerDetail owner,
        Func<NewPetRequest, Task> submit,
        IDialogService dialogs,
        IToastService? toast = null)
    {
        _submit = submit;
        _dialogs = dialogs;
        _toast = toast;

        OwnerName = $"{owner.FirstName} {owner.LastName}";
        OwnerInitials = PatientsPresentation.Initials(owner.FirstName, owner.LastName);
        OwnerToneBrush = PatientsPresentation.ToneBrush(owner.Tone);
        OwnerSubText = $"پرونده {owner.File} · {Localization.PersianNumberFormatter.ToPersianDigits(owner.Mobile1)}";
        OwnerPetCountText = $"{Localization.PersianNumberFormatter.ToPersianDigits(owner.Pets.Count.ToString())} حیوان فعلی";
    }

    /// <summary>
    /// The reference's #petModal defaults the gender pills to «نر» (male) and
    /// renders the pet fields without the wizard's titled section rules, so the
    /// shared form starts male and hides its section titles.
    /// </summary>
    public PetFormViewModel Pet { get; } = new(defaultFemale: false) { ShowSectionTitles = false };

    public string OwnerName { get; }
    public string OwnerInitials { get; }
    public Avalonia.Media.IBrush OwnerToneBrush { get; }
    public string OwnerSubText { get; }
    public string OwnerPetCountText { get; }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Pet.IsValid)
        {
            return;
        }

        // The submit callback runs a backend create command, which surfaces failures as
        // InvalidOperationException (error code in the message). Catch them here and report via a toast —
        // letting them escape this async command would hit the global handler and look like a crash.
        try
        {
            await _submit(Pet.BuildRequest());
        }
        catch (InvalidOperationException ex)
        {
            _toast?.Show(
                PatientsPresentation.CommandFailureMessage(ex, "ثبت حیوان انجام نشد؛ دوباره تلاش کنید"),
                Tone.Danger);
            return;
        }
        catch (Exception)
        {
            _toast?.Show("ثبت حیوان انجام نشد؛ دوباره تلاش کنید", Tone.Danger);
            return;
        }

        _dialogs.Close();
    }
}
