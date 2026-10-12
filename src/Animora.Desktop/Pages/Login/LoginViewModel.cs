using System;
using System.Threading;
using System.Threading.Tasks;
using Animora.Client.Services.Identity;
using Animora.Contracts.Identity;
using Animora.Desktop.Services;
using Animora.Sync.Custom;
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
    private readonly IServerConnectivityService? _connectivity;
    private readonly IOnlineAuthClient? _onlineAuth;
    private readonly ServerEndpointOptions? _serverOptions;

    public LoginViewModel(IAuthenticationService authentication, IDialogService dialogs,
        Func<FirstRunViewModel> firstRunFactory, IServerConnectivityService? connectivity = null,
        LocalTestProfile? localProfile = null, IOnlineAuthClient? onlineAuth = null,
        ServerEndpointOptions? serverOptions = null)
    {
        _authentication = authentication;
        _dialogs = dialogs;
        _firstRunFactory = firstRunFactory;
        _connectivity = connectivity;
        _localProfile = localProfile;
        _onlineAuth = onlineAuth;
        _serverOptions = serverOptions;
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

    /// <summary>Live reachability of the central server, shown as an indicator on the login form.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectivityText))]
    [NotifyPropertyChangedFor(nameof(IsServerOnline))]
    [NotifyPropertyChangedFor(nameof(IsServerOffline))]
    [NotifyPropertyChangedFor(nameof(IsServerChecking))]
    private ServerConnectivityState _connectivityState = ServerConnectivityState.Unknown;

    /// <summary>True while a reachability probe is in flight (disables the re-check button).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckConnectivityCommand))]
    [NotifyPropertyChangedFor(nameof(IsServerChecking))]
    private bool _isCheckingConnectivity;

    public bool IsServerOnline => ConnectivityState == ServerConnectivityState.Online;

    public bool IsServerOffline => ConnectivityState == ServerConnectivityState.Offline;

    public bool IsServerChecking =>
        IsCheckingConnectivity || ConnectivityState == ServerConnectivityState.Unknown;

    /// <summary>The Persian label shown next to the connection indicator dot.</summary>
    public string ConnectivityText => ConnectivityState switch
    {
        ServerConnectivityState.Online => "ارتباط با سرور برقرار است",
        ServerConnectivityState.Offline => "ارتباط با سرور برقرار نیست — اینترنت یا سرور را بررسی کنید",
        ServerConnectivityState.NotConfigured => "حالت محلی (بدون سرور)",
        _ => "در حال بررسی ارتباط با سرور…",
    };

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

        // Kick off a background server reachability probe so the login screen shows the connection
        // indicator without blocking startup; failures only update the indicator, never the form.
        _ = CheckConnectivityCommand.ExecuteAsync(null);

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
                    await EstablishServerSessionAsync(username, password, ct);
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

    /// <summary>Runs one server reachability probe and updates the indicator. Safe to call repeatedly; never throws.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckConnectivity))]
    private async Task CheckConnectivityAsync(CancellationToken ct)
    {
        if (_connectivity is null)
        {
            ConnectivityState = ServerConnectivityState.NotConfigured;
            return;
        }

        Animora.Desktop.Diagnostics.AppTrace.Interaction("CheckConnectivity", "click");
        IsCheckingConnectivity = true;
        try
        {
            ConnectivityState = await _connectivity.CheckAsync(ct);
            Animora.Desktop.Diagnostics.AppTrace.Info($"server connectivity check → {ConnectivityState}", "net");
        }
        catch (OperationCanceledException)
        {
            // Window closing or app shutdown: leave the last known state untouched.
        }
        finally
        {
            IsCheckingConnectivity = false;
        }
    }

    private bool CanCheckConnectivity => !IsCheckingConnectivity;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken ct)
    {
        // Never log the password; the username is safe and invaluable when diagnosing a failed login.
        Animora.Desktop.Diagnostics.AppTrace.Interaction("SignIn", "click", $"user={Username.Trim()}");
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var outcome = await _authentication.SignInAsync(Username.Trim(), Password, rememberDevice: false, ct);
            if (outcome.Succeeded)
            {
                Animora.Desktop.Diagnostics.AppTrace.Info($"local sign-in SUCCEEDED for '{Username.Trim()}'", "auth");

                // The local session alone does NOT authorise the central server. Establish the SERVER session now
                // (bearer token + sealed licence) so /api/commands, /sync/* and the entitlement gate work. A failure
                // here is logged but never blocks sign-in — the app stays usable locally / offline.
                await EstablishServerSessionAsync(Username.Trim(), Password, ct);

                LoginSucceeded?.Invoke(this, EventArgs.Empty);
                return;
            }

            Animora.Desktop.Diagnostics.AppTrace.Warn($"sign-in FAILED for '{Username.Trim()}' (code={outcome.ErrorCode ?? "unknown"})", "auth");
            ErrorMessage = MapError(outcome.ErrorCode);
            Password = string.Empty;
        }
        catch (Exception ex)
        {
            Animora.Desktop.Diagnostics.AppTrace.Error($"sign-in threw for '{Username.Trim()}'", ex, "auth");
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Signs the just-authenticated user into the central server (<see cref="IOnlineAuthClient.LoginAsync"/>) so a bearer
    /// token is persisted and the licence is sealed — the prerequisite for every server write (<c>/api/commands</c>),
    /// the manifest/sync pull and the online entitlement gate. Without this step the desktop has only a LOCAL session and
    /// the server answers <c>401 SESSION_EXPIRED</c> / rejects writes with <c>ENTITLEMENT_REQUIRED</c>.
    ///
    /// <para>This is best effort: an unreachable server or a definitive refusal is logged in full (so the trace shows WHY
    /// server features are unavailable) but never blocks the local sign-in that already succeeded.</para>
    /// </summary>
    private async Task EstablishServerSessionAsync(string username, string password, CancellationToken ct)
    {
        if (_onlineAuth is null)
        {
            Animora.Desktop.Diagnostics.AppTrace.Info(
                "server login SKIPPED — no server endpoint configured; running on the local session only.", "auth");
            return;
        }

        var tenantCode = _serverOptions?.TenantCode;
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            Animora.Desktop.Diagnostics.AppTrace.Warn(
                "server login SKIPPED — ANIMORA_TENANT_CODE is not set, so the clinic/tenant on the server is unknown. " +
                "Server writes will fail with SESSION_EXPIRED / ENTITLEMENT_REQUIRED until a tenant code is configured.", "auth");
            return;
        }

        Animora.Desktop.Diagnostics.AppTrace.Info(
            $"server login → POST /auth/login  [tenant={tenantCode.Trim()}, user={username}]", "auth");
        try
        {
            var result = await _onlineAuth.LoginAsync(tenantCode.Trim(), username, password, ct);
            if (result.IsSuccess)
            {
                var o = result.Value!;
                Animora.Desktop.Diagnostics.AppTrace.Info(
                    $"server login SUCCEEDED — serverTenant={o.TenantId}, serverUser={o.UserId}, device={o.DeviceId}, " +
                    $"role={o.Role}, offline={o.IsOffline}, mustChangePassword={o.MustChangePassword}. " +
                    "Bearer token stored and licence sealed; server writes are now authorised.", "auth");
            }
            else
            {
                Animora.Desktop.Diagnostics.AppTrace.Warn(
                    $"server login FAILED (code={result.Error.Code}{(string.IsNullOrWhiteSpace(result.Detail) ? "" : $", detail={result.Detail}")}). " +
                    "The app continues on the local session; server writes will be rejected until this is resolved " +
                    "(check the tenant code, that this user exists on the server, and that the device is approved).", "auth");
            }
        }
        catch (OperationCanceledException)
        {
            Animora.Desktop.Diagnostics.AppTrace.Info("server login cancelled (window closing / shutdown).", "auth");
        }
        catch (Exception ex)
        {
            Animora.Desktop.Diagnostics.AppTrace.Error("server login threw unexpectedly", ex, "auth");
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

        // Licence / entitlement (the tenant's plan, not the sign-in itself). A fresh install or an
        // expired licence must give an actionable message, never a generic "unexpected error".
        "ENTITLEMENT_REQUIRED" or "LICENSE_NOT_GRANTED" =>
            "مجوز این کلینیک فعال نیست؛ با پشتیبانی تماس بگیرید.",
        "LICENSE_EXPIRED" => "مجوز استفاده منقضی شده است؛ برای تمدید با پشتیبانی تماس بگیرید.",
        "LICENSE_REVOKED" => "مجوز استفاده لغو شده است؛ با پشتیبانی تماس بگیرید.",
        "LICENSE_INVALID" or "LICENSE_KEY_UNKNOWN" =>
            "مجوز نرم‌افزار معتبر نیست؛ با پشتیبانی تماس بگیرید.",
        "LICENSE_DEVICE_MISMATCH" or "LICENSE_DEVICE_LIMIT_REACHED" =>
            "این دستگاه مجاز به استفاده نیست؛ با پشتیبانی تماس بگیرید.",
        "LICENSE_CLOCK_ROLLBACK" =>
            "ساعت سیستم عقب‌تر از زمان واقعی است؛ تاریخ و ساعت را تصحیح کنید.",
        "OFFLINE_CAPABILITY_EXPIRED" =>
            "مهلت کار آفلاین به پایان رسیده است؛ برای ادامه، یک بار به اینترنت متصل شوید.",

        // Network / server reachability (offline sign-in path).
        "NETWORK_UNAVAILABLE" => "ارتباط با سرور برقرار نشد؛ اتصال اینترنت را بررسی کنید.",
        "SERVER_BUSY" => "سرور موقتاً شلوغ است؛ کمی بعد دوباره تلاش کنید.",
        "OFFLINE_SESSION_UNAVAILABLE" =>
            "ورود آفلاین ممکن نیست؛ برای اولین ورود باید به اینترنت متصل باشید.",
        "OFFLINE_CREDENTIAL_MISMATCH" => "نام کاربری یا رمز عبور نادرست است.",
        "DEVICE_REVOKED" => "دسترسی این دستگاه لغو شده است؛ با پشتیبانی تماس بگیرید.",

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
