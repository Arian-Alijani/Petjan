namespace Animora.Contracts.Device;

/// <summary>
/// Machine-local, pre-login device preferences — the handful of settings that must be readable
/// BEFORE any user signs in (so they cannot live in the per-user <c>AppSettings</c> profile, which
/// requires an authenticated session). Persisted next to the local database in
/// <c>device-prefs.json</c>, one file per Windows user profile / installation.
/// </summary>
public sealed record DevicePreferences
{
    /// <summary>
    /// «مرا بخاطر بسپار» — when true the last successful local session is restored automatically on the
    /// next launch, so the user is taken straight to the shell instead of the sign-in form. When false,
    /// any persisted session is cleared on startup and a full sign-in is always required.
    /// </summary>
    public bool RememberMe { get; init; }

    /// <summary>
    /// «اجرای خودکار هنگام روشن شدن ویندوز» — when true the app registers itself to launch automatically
    /// when the current Windows user signs in (a per-user <c>HKCU\…\Run</c> entry, no admin rights). The
    /// auto-started instance comes up hidden in the tray and syncs in the background.
    /// </summary>
    public bool StartWithWindows { get; init; }

    /// <summary>A fresh device has neither preference enabled.</summary>
    public static DevicePreferences Default { get; } = new();
}

/// <summary>
/// Reads/writes <see cref="DevicePreferences"/>. The store is durable (a JSON file) and is safe to read
/// before the user has signed in. Implementations serialize access and never throw on a missing/corrupt
/// file — they fall back to <see cref="DevicePreferences.Default"/>.
/// </summary>
public interface IDevicePreferencesService
{
    /// <summary>The current device preferences (defaults when none were ever saved).</summary>
    Task<DevicePreferences> GetAsync(CancellationToken ct = default);

    /// <summary>Persists the full preference record.</summary>
    Task SaveAsync(DevicePreferences preferences, CancellationToken ct = default);

    /// <summary>Convenience mutator for the «مرا بخاطر بسپار» flag alone.</summary>
    Task SetRememberMeAsync(bool rememberMe, CancellationToken ct = default);

    /// <summary>Convenience mutator for the Windows-startup flag alone.</summary>
    Task SetStartWithWindowsAsync(bool startWithWindows, CancellationToken ct = default);
}

/// <summary>
/// OS integration seam for the «اجرای خودکار هنگام روشن شدن ویندوز» feature. On Windows this toggles a
/// per-user <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> entry; on other platforms it is a
/// no-op that reports <see cref="IsSupported"/> = false. Never put registry/OS calls in a ViewModel.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>True only on platforms where launch-on-sign-in can actually be registered (Windows).</summary>
    bool IsSupported { get; }

    /// <summary>True when the app is currently registered to start with the OS.</summary>
    bool IsEnabled();

    /// <summary>Registers (<paramref name="enabled"/> = true) or removes the launch-on-sign-in entry. Idempotent.</summary>
    void SetEnabled(bool enabled);
}
