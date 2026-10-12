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
    private readonly LocalTestProfile? _localProfile;

    public LoginViewModel(IAuthenticationService authentication, IDialogService dialogs,
        Func<FirstRunViewModel> firstRunFactory, LocalTestProfile? localProfile = null)
    {
        _authentication = authentication;
        _dialogs = dialogs;
        _firstRunFactory = firstRunFactory;
        _localProfile = localProfile;
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
            ErrorMessage = "پایگاه دادهٔ محلی آماده نشد؛ ورود برای حفاظت از داده‌ها غیرفعال است. برای تست، برنامه را با --local-test اجرا کنید.";
            return;
        }

        if (_localProfile?.ClinicBakPreviewPath is not null)
        {
            // Only a content-addressed isolated preview can automate credential entry.
            // This creates/signs in a real Manager; it never bypasses authorization.
            IsBusy = true;
            try
            {
                const string username = "clinic-preview";
                const string password = "ClinicPreview2026!";
                var outcome = await _authentication.IsFirstRunAsync(ct)
                    ? await _authentication.CompleteFirstRunAsync(new FirstRunRequest(
                        "بازبینی بکاپ کلینیک قدیمی", "مدیر بازبینی", username, password), ct)
                    : await _authentication.SignInAsync(username, password, rememberDevice: false, ct);
                if (outcome.Succeeded)
                {
                    Username = username;
                    LoginSucceeded?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ErrorMessage = "ورود خودکار پروفایل بازبینی انجام نشد؛ " + MapError(outcome.ErrorCode);
                }
            }
            finally { IsBusy = false; }
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
        // Credential / account gates (sign-in).
        "CREDENTIALS_INVALID" => "نام کاربری یا رمز عبور نادرست است.",
        "USER_LOCKED_OUT" => "حساب موقتاً قفل شده است. بعداً تلاش کنید.",
        "USER_DISABLED" => "این حساب غیرفعال شده است.",
        "SESSION_EXPIRED" => "نشست شما منقضی شده است؛ دوباره وارد شوید.",
        "PERMISSION_DENIED" => "نشست قفل شده است؛ دوباره وارد شوید.",
        "MEMBERSHIP_NOT_ACTIVE" => "این حساب دیگر عضو فعال کلینیک نیست.",
        "TENANT_SUSPENDED" => "دسترسی این کلینیک معلق شده است؛ با پشتیبانی تماس بگیرید.",

        // Input / policy (sign-in and first-run).
        "VALIDATION_FAILED" => "اطلاعات واردشده معتبر نیست؛ فیلدها را بررسی کنید.",
        "PASSWORD_POLICY_VIOLATION" => "رمز عبور باید حداقل ۸ نویسه و شامل حرف و رقم باشد.",

        // First-run owner setup.
        "FIRST_RUN_ALREADY_COMPLETED" => "راه‌اندازی اولیه قبلاً انجام شده است؛ با نام کاربری خود وارد شوید.",
        "USERNAME_EXISTS" => "این نام کاربری قبلاً ثبت شده است؛ نام دیگری انتخاب کنید.",

        // Local-store infrastructure (fail-closed). These never reach the server, so they
        // must be actionable here instead of collapsing into a generic message.
        "STORE_UNAVAILABLE" => "پایگاه دادهٔ محلی در دسترس نیست؛ برنامه را دوباره اجرا کنید.",
        "STORE_KEY_UNAVAILABLE" => "کلید پایگاه دادهٔ محلی خوانده نشد؛ برنامه را دوباره اجرا کنید.",
        "STORE_CORRUPT" => "پایگاه دادهٔ محلی آسیب دیده است؛ از پشتیبان بازیابی کنید.",
        "NATIVE_SQLITE_TOO_OLD" => "نسخهٔ کتابخانهٔ داخلی قدیمی است؛ برنامه را به‌روزرسانی کنید.",
        "AUDIT_WRITE_FAILED" => "ثبت رویداد ممیزی ناموفق بود؛ عملیات لغو شد. دوباره تلاش کنید.",
        "HANDLER_NOT_REGISTERED" or "DISPATCH_FAILED" =>
            "یک نقص داخلی در برنامه رخ داد؛ با پشتیبانی تماس بگیرید.",

        // Unknown code: surface it so the issue is diagnosable instead of opaque.
        null or "" => "خطای غیرمنتظره رخ داد.",
        _ => $"خطای غیرمنتظره رخ داد (کد: {errorCode}).",
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
