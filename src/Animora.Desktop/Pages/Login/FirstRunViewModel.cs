using System;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Identity;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Login;

/// <summary>One-time owner setup (B03c) shown from the login window on a fresh install.</summary>
public sealed partial class FirstRunViewModel(IAuthenticationService authentication, IDialogService dialogs)
    : ObservableObject
{
    /// <summary>Raised after the owner was created and the session started.</summary>
    public event EventHandler? SetupSucceeded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteSetupCommand))]
    private string _clinicName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteSetupCommand))]
    private string _ownerFullName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteSetupCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteSetupCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteSetupCommand))]
    private bool _isBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private bool CanCompleteSetup =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(ClinicName) &&
        !string.IsNullOrWhiteSpace(OwnerFullName) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanCompleteSetup))]
    private async Task CompleteSetupAsync(CancellationToken ct)
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var outcome = await authentication.CompleteFirstRunAsync(
                new FirstRunRequest(ClinicName.Trim(), OwnerFullName.Trim(), Username.Trim(), Password), ct);
            if (outcome.Succeeded)
            {
                dialogs.Close();
                SetupSucceeded?.Invoke(this, EventArgs.Empty);
                return;
            }

            ErrorMessage = LoginViewModel.MapError(outcome.ErrorCode);
            Password = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
