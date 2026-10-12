using System.Runtime.InteropServices;

namespace Animora.Desktop.Diagnostics;

/// <summary>
/// Last-resort reporter for a startup failure that happens BEFORE the Avalonia UI exists (so no
/// in-app dialog is possible). It writes the failure to the console/log via <see cref="AppTrace"/>
/// (already done by the caller) and, on Windows, pops a native MessageBox so a user who double-clicked
/// the exe — and never saw a terminal — still gets a clear, human-readable reason instead of the
/// silent "the app just closed" experience.
///
/// <para>Entirely best-effort: if the message box cannot be shown (headless, non-Windows), it falls
/// back to writing to <see cref="Console.Error"/> and never throws.</para>
/// </summary>
internal static class FatalStartup
{
    private const uint MbIconError = 0x00000010;
    private const uint MbOk = 0x00000000;
    private const uint MbSetForeground = 0x00010000;
    private const uint MbTopMost = 0x00040000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    internal static void Report(string caption, string message, Exception ex)
    {
        var full = $"{message}\n\n{ex.GetType().Name}: {ex.Message}";
        var logHint = AppTrace.LogFilePath is { } path ? $"\n\nFull log: {path}" : string.Empty;

        try { Console.Error.WriteLine($"\n{caption}\n{full}{logHint}"); } catch { /* ignore */ }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            MessageBoxW(IntPtr.Zero, full + logHint, caption, MbOk | MbIconError | MbSetForeground | MbTopMost);
        }
        catch
        {
            // No window station (service/headless): the console/log copy above is the fallback.
        }
    }
}
