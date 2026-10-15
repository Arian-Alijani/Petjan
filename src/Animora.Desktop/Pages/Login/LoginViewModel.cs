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
    private OnlineLoginOutcome? _onlineOutcome;

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

        // The tenant code configured for this build/environment (ANIMORA_TENANT_CODE) only pre-fills the field;
        // each clinic types its own code at sign-in (multi-tenant SaaS).
        _clinicCode = serverOptions?.TenantCode?.Trim() ?? string.Empty;
    }

    /// <summary>Raised once the user is signed in (sign-in or completed first-run setup).</summary>
    public event EventHandler? LoginSucceeded;

    /// <summary>The clinic (tenant) code entered by the user; required by <c>/auth/login</c> when a server is configured.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _clinicCode = string.Empty;

    /// <summary>True when a central server is configured, i.e. sign-in is online and needs a clinic code.</summary>
    public bool RequiresClinicCode => _onlineAuth is not null;

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
        (!RequiresClinicCode || !string.IsNullOrWhiteSpace(ClinicCode)) &&
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
                    // Isolated offline preview profile: its throw-away credentials do not exist on the server,
                    // so it intentionally never attempts the (now mandatory) online sign-in.
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
        // Server builds are online-only: the clinic/owner account is provisioned on the server, so a fresh install goes
        // straight to the online sign-in form (SignInLocalAsync creates the local record after the server accepts).
        // The local first-run owner setup is only meaningful for builds with no server configured.
        if (!RequiresClinicCode && await _authentication.IsFirstRunAsync(ct))
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
        var user = Username.Trim();
        // Never log the password; the username is safe and invaluable when diagnosing a failed login.
        Animora.Desktop.Diagnostics.AppTrace.Interaction("SignIn", "click", $"user={user}");
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            // 1) ONLINE FIRST (mandatory when a server is configured): clinic code + user + password are checked by
            //    the central server, which also seals the licence/subscription. No server session ⇒ no shell.
            if (!await EstablishServerSessionAsync(user, Password, ct))
            {
                Password = string.Empty;
                return;
            }

            // 2) Local session (CommandContext.UserId for the offline-first store). Only reached after the server accepted.
            var outcome = await SignInLocalAsync(user, Password, ct);
            if (outcome.Succeeded)
            {
                Animora.Desktop.Diagnostics.AppTrace.Info($"local sign-in SUCCEEDED for '{user}'", "auth");
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
                return;
            }

            Animora.Desktop.Diagnostics.AppTrace.Warn($"local sign-in FAILED for '{user}' (code={outcome.ErrorCode ?? "unknown"})", "auth");
            ErrorMessage = MapError(outcome.ErrorCode);
            Password = string.Empty;
        }
        catch (Exception ex)
        {
            Animora.Desktop.Diagnostics.AppTrace.Error($"sign-in threw for '{user}'", ex, "auth");
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the local session after the server accepted the credentials. On a fresh install there is no local user
    /// yet (the clinic is provisioned on the server, not on the desktop), so the first successful online sign-in
    /// provisions the local owner record from the same credentials instead of dead-ending at "server ok, local not".
    /// </summary>
    private async Task<AuthOutcome> SignInLocalAsync(string user, string password, CancellationToken ct)
    {
        var online = _onlineOutcome;

        // A fresh install provisions the local OWNER record — but only when the server says this user really is the
        // clinic Manager. A non-manager must never become a local Manager just because the store is empty.
        if (_onlineAuth is not null && await _authentication.IsFirstRunAsync(ct)
            && (online is null || IsManagerRole(online.Role)))
        {
            var clinic = ClinicCode.Trim();
            Animora.Desktop.Diagnostics.AppTrace.Info(
                $"fresh install: provisioning local account for '{user}' from the verified online sign-in", "auth");
            return await _authentication.CompleteFirstRunAsync(
                new FirstRunRequest(clinic.Length > 0 ? clinic : user, user, user, password,
                    online is { IsOffline: false } ? online.UserId : null), ct);
        }

        if (online is null || online.IsOffline)
        {
            return await _authentication.SignInAsync(user, password, rememberDevice: false, ct);
        }

        // The server just verified these credentials: (re)align the local mirror BEFORE the local sign-in so the local
        // user id equals the server user id (D01 — a random local id makes every pushed DoctorId fail
        // DOCTOR_NOT_AVAILABLE) and a server-side role change is reconciled even when the password is unchanged. The
        // mirror is idempotent; a failure here must not block a local sign-in that would succeed on its own.
        var mirrored = await _authentication.EnsureLocalMirrorAsync(
            new ServerIdentityMirrorRequest(online.UserId, online.TenantId, user, password, online.Role), ct);
        if (!mirrored.Succeeded)
        {
            Animora.Desktop.Diagnostics.AppTrace.Warn(
                $"local mirror alignment FAILED for '{user}' (code={mirrored.ErrorCode ?? "unknown"}, role={online.Role})", "auth");
        }

        return await _authentication.SignInAsync(user, password, rememberDevice: false, ct);
    }

    private static bool IsManagerRole(string? role) =>
        string.Equals(role?.Trim(), "Manager", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Signs the user into the central server (<see cref="IOnlineAuthClient.LoginAsync"/>) so a bearer token is persisted and
    /// the licence is sealed — the prerequisite for every server write (<c>/api/commands</c>), the sync pull, the
    /// subscription status and the online entitlement gate. Blocking: returns <c>false</c> (with <see cref="ErrorMessage"/>
    /// set) when the user must not proceed. A build with no server configured has no online model and returns <c>true</c>.
    ///
    /// <para><c>IsOffline</c> in the outcome means the server was momentarily unreachable but a previous online sign-in left a
    /// valid mirror (repeat sign-in); that is accepted by <see cref="IOnlineAuthClient"/> itself and allowed here.</para>
    /// </summary>
    private async Task<bool> EstablishServerSessionAsync(string username, string password, CancellationToken ct)
    {
        _onlineOutcome = null;
        if (_onlineAuth is null)
        {
            Animora.Desktop.Diagnostics.AppTrace.Info(
                "server login SKIPPED — no server endpoint configured; local-only build.", "auth");
            return true;
        }

        var tenantCode = ClinicCode.Trim();
        if (tenantCode.Length == 0)
        {
            Animora.Desktop.Diagnostics.AppTrace.Warn("server login BLOCKED — clinic code is empty.", "auth");
            ErrorMessage = "کد کلینیک را وارد کنید.";
            return false;
        }

        Animora.Desktop.Diagnostics.AppTrace.Info(
            $"server login → POST /auth/login  [tenant={tenantCode}, user={username}]", "auth");
        try
        {
            var result = await _onlineAuth.LoginAsync(tenantCode, username, password, ct);
            if (!result.IsSuccess)
            {
                Animora.Desktop.Diagnostics.AppTrace.Warn(
                    $"server login FAILED (code={result.Error.Code}{(string.IsNullOrWhiteSpace(result.Detail) ? "" : $", detail={result.Detail}")}).", "auth");
                ErrorMessage = MapError(result.Error.Code);
                return false;
            }

            var o = result.Value!;
            _onlineOutcome = o;
            Animora.Desktop.Diagnostics.AppTrace.Info(
                $"server login SUCCEEDED — serverTenant={o.TenantId}, serverUser={o.UserId}, device={o.DeviceId}, " +
                $"role={o.Role}, offline={o.IsOffline}, mustChangePassword={o.MustChangePassword}.", "auth");
            return true;
        }
        catch (OperationCanceledException)
        {
            Animora.Desktop.Diagnostics.AppTrace.Info("server login cancelled (window closing / shutdown).", "auth");
            return false;
        }
        catch (Exception ex)
        {
            Animora.Desktop.Diagnostics.AppTrace.Error("server login threw unexpectedly", ex, "auth");
            ErrorMessage = MapError("NETWORK_UNAVAILABLE");
            return false;
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
        "DEVICE_PENDING_APPROVAL" => "دستگاه شما ثبت شد و در انتظار تأیید مدیر کلینیک است.",

        // Local-store infrastructure (fail-closed). These never reach the server, so they
        // must be actionable here instead of collapsing into a generic message.
        "SYNC_PENDING_CHANGES" => "کلینیک قبلی تغییرات ارسال‌نشده دارد؛ ابتدا وارد همان کلینیک شوید و همگام‌سازی را کامل کنید.",
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

        // Only reachable on builds without a server (see InitializeAsync). CompleteFirstRunAsync already started the
        // local session: open the shell.
        LoginSucceeded?.Invoke(this, EventArgs.Empty);
    }
}
