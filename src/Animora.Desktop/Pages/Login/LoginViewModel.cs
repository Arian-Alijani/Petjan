using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Login;

/// <summary>
/// Sign-in gate shown at startup, before the shell. Front-end only for now:
/// credentials are checked against a fixed design-time pair (admin / admin),
/// mirroring the app's "Mock* seam until the real backend lands" convention.
/// A future auth phase replaces <see cref="TryAuthenticate"/> with a real
/// session-backed identity service — no View changes.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    // Temporary design-time credentials (see task: enter with admin/admin).
    private const string DemoUser = "admin";
    private const string DemoPassword = "admin";

    /// <summary>Raised once the entered credentials are accepted.</summary>
    public event EventHandler? LoginSucceeded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private bool CanSignIn =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private void SignIn()
    {
        ErrorMessage = string.Empty;

        if (TryAuthenticate(Username.Trim(), Password))
        {
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
            return;
        }

        ErrorMessage = "نام کاربری یا رمز عبور نادرست است.";
        Password = string.Empty;
    }

    // Seam for the real auth backend. Today: fixed demo credentials.
    private static bool TryAuthenticate(string username, string password) =>
        string.Equals(username, DemoUser, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(password, DemoPassword, StringComparison.Ordinal);
}
