using System.Collections.Concurrent;
using System.Text;

namespace Animora.Desktop.Diagnostics;

/// <summary>
/// Lightweight, dependency-free diagnostic logger for the desktop client. It captures the app's
/// OWN story — startup/lifecycle milestones, navigation, button clicks, warnings and exceptions —
/// and complements <see cref="Animora.Sync.Custom.NetworkLoggingHandler"/>, which only sees HTTP
/// traffic. Together they give a complete picture when diagnosing a user-reported problem.
///
/// <para>
/// WHERE THE LOG GOES
///   * Console: when a terminal is attached (the server-connected launcher, <c>dotnet run</c>, or
///     a freshly allocated console) every line is colour-coded by level and streamed live.
///   * File: every line is ALSO appended to a dated log file under
///     <c>%LOCALAPPDATA%\Animora\logs\animora-yyyy-MM-dd.log</c> (or the OS equivalent). This means
///     a user can reproduce a crash, close the app, and send us the file even if they never saw the
///     terminal. Old files (older than <see cref="RetentionDays"/>) are pruned on startup.
/// </para>
///
/// <para>
/// It is ALWAYS on for its own events (they are cheap and invaluable for support), but the verbose
/// <see cref="Level.Debug"/> tier is gated behind <c>ANIMORA_TRACE_DEBUG=1</c> so normal runs stay
/// quiet. The whole class is best-effort: a logging failure can never crash the app.
/// </para>
/// </summary>
public static class AppTrace
{
    public enum Level
    {
        Debug,
        Info,
        Warn,
        Error,
    }

    private const int RetentionDays = 14;

    // Serialize writes so interleaved threads never corrupt a line (console or file).
    private static readonly object Gate = new();

    // Buffer messages logged before Initialize runs, then flush them in order.
    private static readonly ConcurrentQueue<string> Pending = new();

    private static StreamWriter? _file;
    private static string? _logPath;
    private static bool _initialized;
    private static bool _debugEnabled;

    /// <summary>Absolute path of the active log file, once <see cref="Initialize"/> has run.</summary>
    public static string? LogFilePath => _logPath;

    /// <summary>Allows callers to avoid constructing disabled verbose messages.</summary>
    public static bool IsDebugEnabled => _debugEnabled;

    /// <summary>
    /// Opens the dated log file and prints a session banner. Call once, as early as possible in
    /// <c>Main</c> (before any other trace). Safe to call more than once (later calls are ignored).
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _debugEnabled = IsTruthy(Environment.GetEnvironmentVariable("ANIMORA_TRACE_DEBUG"));

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Animora", "logs");
            Directory.CreateDirectory(dir);
            PruneOldLogs(dir);

            _logPath = Path.Combine(dir, $"animora-{DateTime.Now:yyyy-MM-dd}.log");
            _file = new StreamWriter(
                new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
        }
        catch
        {
            // No file log (read-only profile, permissions): console-only is still useful.
            _file = null;
            _logPath = null;
        }

        var banner =
            "══════════════════════════════════════════════════════════════════\n" +
            $"  Animora session start  {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
            $"  log file : {(_logPath ?? "(console only)")}\n" +
            $"  debug    : {(_debugEnabled ? "ON (ANIMORA_TRACE_DEBUG=1)" : "off")}\n" +
            "══════════════════════════════════════════════════════════════════";
        WriteRaw(Level.Info, banner);

        // Flush anything logged before the file was open.
        while (Pending.TryDequeue(out var line)) WriteToFile(line);
    }

    // ---- public logging surface ----------------------------------------------------------------

    public static void Debug(string message, string? category = null)
    {
        if (!_debugEnabled) return;
        Log(Level.Debug, category, message);
    }

    public static void Info(string message, string? category = null) => Log(Level.Info, category, message);

    public static void Warn(string message, string? category = null) => Log(Level.Warn, category, message);

    public static void Error(string message, Exception? ex = null, string? category = null)
    {
        var text = ex is null ? message : $"{message}\n    {ex.GetType().FullName}: {ex.Message}\n{Indent(ex.StackTrace)}";
        Log(Level.Error, category, text);
    }

    /// <summary>Logs a user interaction (click, toggle, selection) at INFO so support can see what the user did.</summary>
    public static void Interaction(string control, string? action = null, string? context = null)
    {
        var sb = new StringBuilder("⇢ ").Append(control);
        if (!string.IsNullOrWhiteSpace(action)) sb.Append(" · ").Append(action);
        if (!string.IsNullOrWhiteSpace(context)) sb.Append("  [").Append(context).Append(']');
        Log(Level.Info, "ui", sb.ToString());
    }

    /// <summary>Logs a navigation / page-switch event.</summary>
    public static void Navigate(string from, string to) => Log(Level.Info, "nav", $"➜ {from} → {to}");

    /// <summary>Marks a named startup/lifecycle milestone, optionally with the elapsed time since app start.</summary>
    public static void Milestone(string name, TimeSpan? elapsed = null)
    {
        var suffix = elapsed is { } e ? $"  (+{e.TotalMilliseconds:F0} ms)" : string.Empty;
        Log(Level.Info, "life", $"◆ {name}{suffix}");
    }

    // ---- core -----------------------------------------------------------------------------------

    private static void Log(Level level, string? category, string message)
    {
        var line = Format(level, category, message);
        WriteRaw(level, line);
    }

    private static string Format(Level level, string? category, string message)
    {
        var tag = level switch
        {
            Level.Debug => "DBG",
            Level.Info => "INF",
            Level.Warn => "WRN",
            Level.Error => "ERR",
            _ => "INF",
        };
        var cat = string.IsNullOrWhiteSpace(category) ? string.Empty : $"[{category}] ";
        return $"{DateTime.Now:HH:mm:ss.fff} {tag} {cat}{message}";
    }

    private static void WriteRaw(Level level, string line)
    {
        lock (Gate)
        {
            WriteToConsole(level, line);
            if (_file is not null) WriteToFile(line);
            else if (!_initialized) Pending.Enqueue(line);
        }
    }

    private static void WriteToConsole(Level level, string line)
    {
        ConsoleColor? previous = null;
        try
        {
            previous = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                Level.Debug => ConsoleColor.DarkGray,
                Level.Info => ConsoleColor.Gray,
                Level.Warn => ConsoleColor.Yellow,
                Level.Error => ConsoleColor.Red,
                _ => ConsoleColor.Gray,
            };
        }
        catch
        {
            // No console attached / colour unsupported: still write the text below.
        }

        try
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
        catch
        {
            // ignore — best effort
        }

        if (previous is { } prev)
        {
            try { Console.ForegroundColor = prev; } catch { /* ignore */ }
        }
    }

    private static void WriteToFile(string line)
    {
        try { _file?.WriteLine(line); }
        catch { /* disk full / closed handle — never fatal */ }
    }

    private static void PruneOldLogs(string dir)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(dir, "animora-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                }
                catch
                {
                    // A file in use / locked — skip it.
                }
            }
        }
        catch
        {
            // ignore — pruning is a nicety
        }
    }

    private static string Indent(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var raw in stackTrace.Split('\n'))
            sb.Append("    ").Append(raw.TrimEnd('\r')).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    private static bool IsTruthy(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();
        return raw is "1"
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
