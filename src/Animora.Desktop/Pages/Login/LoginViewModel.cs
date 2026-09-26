using System;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Identity;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Login;

/// <summary>
/// Sign-in gate shown at startup, before the shell (B03c: real local authentication via
/// <see cref="IAuthenticationService"/>). Sign-in stays disabled until the local store is ready
/// (<see cref="InitializeAsync"/>); on a fresh install the first-run owner setup is shown instead.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly IDialogService _dialogs;
    private readonly Func<FirstRunViewModel> _firstRunFactory;
    private FirstRunViewModel? _firstRun;

    public LoginViewModel(IAuthenticationService authentication, IDialogService dialogs,
        Func<FirstRunViewModel> firstRunFactory)
    {
        _authentication = authentication;
        _dialogs = dialogs;
        _firstRunFactory = firstRunFactory;
    }

    /// <summary>Raised once the user is signed in (sign-in or completed first-run setup).</summary>
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

    /// <summary>True while the local store starts up or a sign-in is in flight.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isBusy = true;

    /// <summary>True only after the local store initialized successfully.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isStoreReady;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private bool CanSignIn =>
        IsStoreReady &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);

    /// <summary>
    /// Called once the local-store startup finished. A failed store keeps sign-in disabled; a ready store
    /// checks for first run and, if needed, shows the owner setup form.
    /// </summary>
    public async Task InitializeAsync(bool storeReady, CancellationToken ct = default)
    {
        IsStoreReady = storeReady;
        IsBusy = false;

        if (!storeReady)
        {
            ErrorMessage = MapError(null);
            return;
        }

        if (await _authentication.IsFirstRunAsync(ct))
        {
            ShowFirstRun();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken ct)
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var outcome = await _authentication.SignInAsync(Username.Trim(), Password, rememberDevice: false, ct);
            if (outcome.Succeeded)
            {
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
                return;
            }

            ErrorMessage = MapError(outcome.ErrorCode);
            Password = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The only place identity error codes become Persian UI text (shared with first-run).</summary>
    internal static string MapError(string? errorCode) => errorCode switch
    {
        "CREDENTIALS_INVALID" => "نام کاربری یا رمز عبور نادرست است.",
        "USER_LOCKED_OUT" => "حساب موقتاً قفل شده است. بعداً تلاش کنید.",
        "USER_DISABLED" => "این حساب غیرفعال شده است.",
        "PASSWORD_POLICY_VIOLATION" => "رمز عبور باید حداقل ۸ نویسه و شامل حرف و رقم باشد.",
        _ => "خطای غیرمنتظره رخ داد.",
    };

    private void ShowFirstRun()
    {
        if (_firstRun is not null)
        {
            return; // already shown once
        }

        var firstRun = _firstRunFactory();
        _firstRun = firstRun;
        firstRun.SetupSucceeded += OnFirstRunSucceeded;
        _dialogs.ShowModal(firstRun, maxWidth: 520);
    }

    private void OnFirstRunSucceeded(object? sender, EventArgs e)
    {
        if (sender is FirstRunViewModel firstRun)
        {
            firstRun.SetupSucceeded -= OnFirstRunSucceeded;
            Username = firstRun.Username.Trim();
        }

        // CompleteFirstRunAsync already started the session: open the shell.
        LoginSucceeded?.Invoke(this, EventArgs.Empty);
    }
}
