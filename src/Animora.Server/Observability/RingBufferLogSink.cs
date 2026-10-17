using Serilog.Core;
using Serilog.Events;
namespace Animora.Server.Observability;

/// <summary>One retained log line. Already redacted: the sink sits behind the secret-destructuring policy and the redacting enricher.</summary>
internal readonly record struct RingLogEntry(long Sequence, DateTimeOffset Timestamp, LogEventLevel Level, string Source, string Message);

/// <summary>
/// Bounded in-memory tail of the server log for the operator's live log viewer. Always filling is cheap (one lock, one array slot);
/// consumers PULL with a monotonically increasing sequence cursor, so nothing runs per subscriber while the viewer is stopped.
/// Only the rendered message and the logger category are kept: raw paths, queries, bodies, headers and exception messages are never
/// logged by this server, and exceptions are deliberately not captured here either.
/// </summary>
internal sealed class RingBufferLogSink(int capacity = RingBufferLogSink.DefaultCapacity) : ILogEventSink
{
    public const int DefaultCapacity = 1000;
    public const int MaxMessageLength = 2000;
    private readonly RingLogEntry[] _buffer = new RingLogEntry[Math.Clamp(capacity, 1, 10_000)];
    private readonly object _gate = new();
    private long _last;

    /// <summary>Sequence of the newest retained entry (0 when nothing was logged yet).</summary>
    public long LastSequence { get { lock (_gate) return _last; } }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        string message = logEvent.RenderMessage();
        if (message.Length > MaxMessageLength) message = message[..MaxMessageLength];
        string source = logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value) && value is ScalarValue { Value: string s } ? s : "-";
        lock (_gate)
        {
            _last++;
            _buffer[(int)((_last - 1) % _buffer.Length)] = new(_last, logEvent.Timestamp, logEvent.Level, source, message);
        }
    }

    /// <summary>
    /// Newest-last entries with <c>Sequence &gt; afterSequence</c> at or above <paramref name="minimum"/> whose message/source contains
    /// <paramref name="search"/> (case-insensitive); at most <paramref name="take"/> of the NEWEST matches are returned.
    /// </summary>
    public (IReadOnlyList<RingLogEntry> Entries, long LastSequence) Read(long afterSequence, LogEventLevel minimum, string? search, int take)
    {
        take = Math.Clamp(take, 1, _buffer.Length);
        RingLogEntry[] copy;
        long last;
        lock (_gate)
        {
            last = _last;
            long first = Math.Max(Math.Max(afterSequence, 0) + 1, last - _buffer.Length + 1);
            if (first > last) return ([], last);
            copy = new RingLogEntry[last - first + 1];
            for (long seq = first; seq <= last; seq++) copy[seq - first] = _buffer[(int)((seq - 1) % _buffer.Length)];
        }

        string? needle = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var matches = new List<RingLogEntry>(Math.Min(copy.Length, take));
        for (int i = copy.Length - 1; i >= 0 && matches.Count < take; i--)
        {
            RingLogEntry e = copy[i];
            if (e.Level < minimum) continue;
            if (needle is not null && !e.Message.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !e.Source.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;
            matches.Add(e);
        }

        matches.Reverse();
        return (matches, last);
    }
}
