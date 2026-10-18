using System.Runtime.Versioning;
using Animora.Contracts.Device;
using Microsoft.Win32;

namespace Animora.Client.Services.Device;

/// <summary>
/// <see cref="IStartupRegistration"/> over the per-user Windows "Run" key
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>). A per-user entry needs no administrator
/// rights and launches the app when the current Windows user signs in. The command line carries the
/// <c>--startup</c> flag so the auto-started instance knows to come up hidden in the tray and sync in the
/// background. On non-Windows platforms every method is a safe no-op and <see cref="IsSupported"/> is false.
/// </summary>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    /// <summary>The value name under the Run key. Stable so toggling off reliably removes the same entry.</summary>
    internal const string RunValueName = "Animora";

    /// <summary>Flag appended to the registered command so Program.Main can detect a boot-time launch.</summary>
    public const string StartupFlag = "--startup";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        return IsEnabledWindows();
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        SetEnabledWindows(enabled);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEnabledWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetEnabledWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var command = BuildStartupCommand();
                if (command is not null) key.SetValue(RunValueName, command, RegistryValueKind.String);
            }
            else if (key.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Registry locked down by policy: honor the request silently rather than crash the settings save.
        }
    }

    /// <summary>
    /// The quoted command written to the Run key: the real executable path plus <c>--startup</c>.
    /// Prefers the process main-module path (the published <c>Animora.Desktop.exe</c>); falls back to
    /// launching the managed DLL via the host when the process is <c>dotnet</c> (dev runs). Returns null
    /// when no usable path can be determined, so we never register a broken command.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? BuildStartupCommand()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return null;

        var exeName = Path.GetFileNameWithoutExtension(exePath);
        if (string.Equals(exeName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // Running under the dotnet host (e.g. `dotnet run`): register the host + managed entry assembly.
            // Build the managed DLL path from AppContext.BaseDirectory + the entry assembly's simple name
            // instead of Assembly.Location — Location returns "" for single-file apps (IL3000), whereas
            // BaseDirectory is the app directory in every deployment model.
            var entryName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
            if (string.IsNullOrEmpty(entryName)) return null;
            var entry = Path.Combine(AppContext.BaseDirectory, entryName + ".dll");
            return $"\"{exePath}\" \"{entry}\" {StartupFlag}";
        }

        return $"\"{exePath}\" {StartupFlag}";
    }
}
