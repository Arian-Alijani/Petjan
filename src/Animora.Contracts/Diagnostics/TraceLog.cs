using System.Collections.Concurrent;
using System.Text;

namespace Animora.Contracts.Diagnostics;

/// <summary>
/// The single, process-wide diagnostic trace sink for EVERY layer of the client — contracts,
/// application/dispatch, persistence (SQL reads), the sync engine (pull/apply) and the desktop UI.
///
/// <para>
/// WHY A SHARED SINK IN <c>Animora.Contracts</c>
/// The desktop UI logger (<c>Animora.Desktop.Diagnostics.AppTrace</c>) can only live in the desktop
/// assembly, so a record that is written on the server but never materialised into the LOCAL replica
/// could not previously be explained from the deeper layers (the SQL projection, the pull/apply path).
/// This type is a dependency-free leaf that every assembly can reference, so the SAME log file + console
/// window now carries the whole story end-to-end. <c>AppTrace</c> delegates to it, so existing desktop
/// call sites are unchanged and everything still lands in one place.
/// </para>
///
/// <para>
/// WHERE IT GOES
///   * Console: when a terminal is attached (the server-connected launcher, <c>dotnet run</c>, or an
///     allocated console) every line is colour-coded by level and streamed live.
///   * File: every line is ALSO appended to <c>%LOCALAPPDATA%\Animora\logs\animora-yyyy-MM-dd.log</c>
///     (or the OS equivalent). Files older than <see cref="RetentionDays"/> are pruned on startup.
/// </para>
///
/// <para>
/// It is ALWAYS on for its own events; the verbose <see cref="Level.Debug"/> tier is gated behind
/// <c>ANIMORA_TRACE_DEBUG=1</c>. The whole class is best-effort: a logging failure can never crash the app.
/// </para>
/// </summary>
public static class TraceLog
{
    public enum Level
    {
        Debug,
        Info,
        Warn,
        Error,
    }

    /// <summary>Environment variable that turns on the verbose <see cref="Level.Debug"/> tier.</summary>
    public const string DebugEnvVar = "ANIMORA_TRACE_DEBUG";

    private const int RetentionDays = 14;

    // Serialize writes so interleaved threads never corrupt a line (console or file).
    private static readonly object Gate = new();

    // Buffer messages logged before Initialize runs, then flush them in order.
    private static readonly ConcurrentQueue<string> Pending = new();

    private static StreamWriter? _file;
    private static string? _logPath;
    private static bool _initialized;
    private static bool _debugEnabled;

    // Ambient "flow" id so every line emitted while a single operation runs (a page load, a sync
    // apply, …) carries the SAME tag — even across await/thread hops and even from deep layers that
    // never received an explicit id. This is what lets one page-load be read as ONE story in the log.
    private static int _flowSeq;
    private static readonly AsyncLocal<string?> Flow = new();

    /// <summary>Absolute path of the active log file, once <see cref="Initialize"/> has run.</summary>
    public static string? LogFilePath => _logPath;

    /// <summary>The ambient flow tag attached to the current async context, if any.</summary>
    public static string? CurrentFlowId => Flow.Value;

    /// <summary>True once <see cref="Initialize"/> has run.</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>True when the verbose debug tier is enabled (<c>ANIMORA_TRACE_DEBUG=1</c>).</summary>
    public static bool DebugEnabled => _debugEnabled;

    /// <summary>
    /// Opens the dated log file and prints a session banner. Call once, as early as possible
    /// (the desktop calls it from <c>Program.Main</c>). Safe to call more than once.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _debugEnabled = IsTruthy(Environment.GetEnvironmentVariable(DebugEnvVar));

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
            $"  debug    : {(_debugEnabled ? "ON (" + DebugEnvVar + "=1)" : "off")}\n" +
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

    /// <summary>A prominent section divider, e.g. for the start of a page-load flow.</summary>
    public static void Section(string title, string? category = null) =>
        Log(Level.Info, category, $"══════════ {title} ══════════");

    /// <summary>Logs a numbered/directional step of a multi-step flow (always Info).</summary>
    public static void Step(int ordinal, string title, string? detail = null, string? category = null)
    {
        var sb = new StringBuilder("  ▶ step ").Append(ordinal).Append(": ").Append(title);
        if (!string.IsNullOrWhiteSpace(detail)) sb.Append(" — ").Append(detail);
        Log(Level.Info, category, sb.ToString());
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

    /// <summary>The console colour used for a level (used by callers that pipe raw output).</summary>
    public static ConsoleColor ColorFor(Level level) => level switch
    {
        Level.Debug => ConsoleColor.DarkGray,
        Level.Info => ConsoleColor.Gray,
        Level.Warn => ConsoleColor.Yellow,
        Level.Error => ConsoleColor.Red,
        _ => ConsoleColor.Gray,
    };

    /// <summary>
    /// Writes an already-formatted, possibly multi-line block verbatim (used by
    /// <c>NetworkLoggingHandler</c> for its request/response boxes). Level controls the console colour.
    /// Also appended to the shared log file so the network trace is captured there too.
    /// </summary>
    public static void Raw(string text, Level level = Level.Info) => WriteRaw(level, text);

    /// <summary>
    /// Runs <paramref name="body"/> inside a named diagnostic flow. Every line logged (by ANY layer,
    /// synchronously or across awaits) while the body runs is tagged with the same short flow id, so a
    /// single page-load / sync-apply can be read as one contiguous story in the interleaved log.
    /// </summary>
    public static async Task<T> FlowAsync<T>(string name, Func<Task<T>> body, string? category = null, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var id = NextFlowId(name);
        var previous = Flow.Value;
        Flow.Value = id;
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $"  ({detail})";
        Log(Level.Info, category ?? "flow", $"▶▶▶ BEGIN [{id}] {name}{suffix}");
        var started = DateTime.UtcNow;
        try
        {
            var result = await body().ConfigureAwait(false);
            var ms = (DateTime.UtcNow - started).TotalMilliseconds;
            Log(Level.Info, category ?? "flow", $"◀◀◀ END   [{id}] {name}  ({ms:F0} ms){suffix}");
            return result;
        }
        catch (Exception ex)
        {
            var ms = (DateTime.UtcNow - started).TotalMilliseconds;
            Error($"◀◀◀ END   [{id}] {name}  FAILED ({ms:F0} ms){suffix}", ex, category ?? "flow");
            throw;
        }
        finally
        {
            Flow.Value = previous;
        }
    }

    /// <summary>Non-generic <see cref="FlowAsync{T}"/> for a fire-and-forget load; returns nothing.</summary>
    public static async Task FlowAsync(string name, Func<Task> body, string? category = null, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        await FlowAsync<object?>(name, async () => { await body().ConfigureAwait(false); return null; }, category, detail)
            .ConfigureAwait(false);
    }

    private static string NextFlowId(string name)
    {
        var seq = Interlocked.Increment(ref _flowSeq);
        var shortName = new string(name.Where(char.IsLetterOrDigit).Take(10).ToArray());
        return $"{(shortName.Length == 0 ? "flow" : shortName)}#{seq}";
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
        var flow = Flow.Value is { } f ? $"{f} " : string.Empty;
        return $"{DateTime.Now:HH:mm:ss.fff} {tag} {cat}{flow}{message}";
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
