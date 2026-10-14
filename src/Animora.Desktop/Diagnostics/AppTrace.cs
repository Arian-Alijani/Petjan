using Animora.Contracts.Diagnostics;

namespace Animora.Desktop.Diagnostics;

/// <summary>
/// Desktop-facing facade over the shared <see cref="TraceLog"/> sink. It keeps the historical
/// <c>AppTrace.*</c> call sites (52 of them) working unchanged while routing everything into ONE
/// process-wide log file + console, so the desktop's own story (startup, navigation, clicks, errors)
/// appears interleaved with the deeper layers (SQL reads, sync apply) that cannot reference the
/// desktop assembly.
///
/// <para>See <see cref="TraceLog"/> for where the log goes and the <c>ANIMORA_TRACE_DEBUG</c> gate.</para>
/// </summary>
public static class AppTrace
{
    /// <summary>Desktop-visible mirror of the shared log levels (same ordinals as <see cref="TraceLog.Level"/>).</summary>
    public enum Level
    {
        Debug,
        Info,
        Warn,
        Error,
    }

    /// <summary>Absolute path of the active log file, once <see cref="Initialize"/> has run.</summary>
    public static string? LogFilePath => TraceLog.LogFilePath;

    /// <summary>Opens the dated log file and prints the session banner. Safe to call more than once.</summary>
    public static void Initialize() => TraceLog.Initialize();

    public static void Debug(string message, string? category = null) => TraceLog.Debug(message, category);

    public static void Info(string message, string? category = null) => TraceLog.Info(message, category);

    public static void Warn(string message, string? category = null) => TraceLog.Warn(message, category);

    public static void Error(string message, Exception? ex = null, string? category = null) =>
        TraceLog.Error(message, ex, category);

    /// <summary>A prominent divider marking the start of a multi-step flow.</summary>
    public static void Section(string title, string? category = null) => TraceLog.Section(title, category);

    /// <summary>A numbered step inside a flow.</summary>
    public static void Step(int ordinal, string title, string? detail = null, string? category = null) =>
        TraceLog.Step(ordinal, title, detail, category);

    /// <summary>Logs a user interaction (click, toggle, selection) at INFO so support can see what the user did.</summary>
    public static void Interaction(string control, string? action = null, string? context = null) =>
        TraceLog.Interaction(control, action, context);

    /// <summary>Logs a navigation / page-switch event.</summary>
    public static void Navigate(string from, string to) => TraceLog.Navigate(from, to);

    /// <summary>Marks a named startup/lifecycle milestone, optionally with the elapsed time since app start.</summary>
    public static void Milestone(string name, TimeSpan? elapsed = null) => TraceLog.Milestone(name, elapsed);

    /// <summary>Runs a body inside a named flow so every line it logs carries one correlating id.</summary>
    public static Task<T> FlowAsync<T>(string name, Func<Task<T>> body, string? category = null, string? detail = null) =>
        TraceLog.FlowAsync(name, body, category, detail);

    /// <summary>Non-generic flow wrapper.</summary>
    public static Task FlowAsync(string name, Func<Task> body, string? category = null, string? detail = null) =>
        TraceLog.FlowAsync(name, body, category, detail);

    /// <summary>Writes an already-formatted multi-line block (colour chosen by level).</summary>
    public static void Raw(string text, Level level = Level.Info) => TraceLog.Raw(text, (TraceLog.Level)level);
}
