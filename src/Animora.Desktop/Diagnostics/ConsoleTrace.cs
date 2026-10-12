using System.Runtime.InteropServices;
using System.Text;

namespace Animora.Desktop.Diagnostics;

/// <summary>
/// Makes the console trace visible for the <c>WinExe</c> desktop build. A GUI (<c>WinExe</c>)
/// process has no console by default, so anything written to <see cref="Console.Out"/> goes
/// nowhere when the app is double-clicked. When the server-connected launcher sets
/// <c>ANIMORA_NET_LOG=1</c> it runs the exe from an already-open terminal; this helper then
/// ATTACHES to that parent console so the <see cref="Animora.Sync.Custom.NetworkLoggingHandler"/>
/// trace streams into the same window the user is watching. If there is no parent console
/// (true double-click) it ALLOCATES a fresh console window so the trace is still visible.
///
/// <para>Windows-only and entirely best-effort: on any other OS, or if attach/alloc fails, it is a
/// silent no-op and the app runs exactly as before.</para>
/// </summary>
internal static class ConsoleTrace
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    /// <summary>
    /// Ensures a console is available and wired to <see cref="Console.Out"/>/<see cref="Console.Error"/>
    /// when network tracing is requested. Call once, very early in <c>Main</c>.
    /// </summary>
    internal static void EnsureAttachedIfRequested()
    {
        // Attach/allocate a console when ANY live trace is requested: the server-traffic trace
        // (ANIMORA_NET_LOG) OR the verbose app trace (ANIMORA_TRACE_DEBUG). Either way the user
        // wants to watch the diagnostic stream in a terminal.
        if (!Animora.Sync.Custom.NetworkLoggingHandler.IsEnabled && !AppTraceConsoleRequested()) return;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            // Already have a console (e.g. launched from `dotnet run` or a dev terminal)? Nothing to do.
            if (GetConsoleWindow() != IntPtr.Zero)
            {
                TryEnableUtf8();
                return;
            }

            // Prefer the parent terminal (the launcher's window) so logs appear where the user is looking.
            if (!AttachConsole(AttachParentProcess))
            {
                // No parent console (double-click): make our own window so the trace is still visible.
                AllocConsole();
            }

            RebindStandardStreams();
            TryEnableUtf8();
            Console.Out.WriteLine("── Animora network trace enabled (ANIMORA_NET_LOG=1). All server requests/responses will be logged below. ──");
            Console.Out.Flush();
        }
        catch
        {
            // Never let a diagnostics convenience break app startup.
        }
    }

    private static bool AppTraceConsoleRequested()
    {
        var raw = Environment.GetEnvironmentVariable("ANIMORA_TRACE_DEBUG");
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();
        return raw is "1"
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    // After AttachConsole/AllocConsole the managed Console.Out still points at the old (null) handles;
    // rebind it to the real console output so writes actually appear.
    private static void RebindStandardStreams()
    {
        try
        {
            var stdOut = Console.OpenStandardOutput();
            var writer = new StreamWriter(stdOut, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(writer);

            var stdErr = Console.OpenStandardError();
            var errWriter = new StreamWriter(stdErr, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetError(errWriter);
        }
        catch
        {
            // ignore — best effort
        }
    }

    private static void TryEnableUtf8()
    {
        // The box-drawing characters and Persian text in logs need a UTF-8 console code page.
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* some hosts forbid changing it */ }
    }
}
