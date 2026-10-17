using Animora.Server.Observability;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Events;
using Xunit;
namespace Animora.Server.Tests.Observability;

public sealed class RingBufferLogSinkTests
{
    private static LogEvent Event(LogEventLevel level, string message, string? source = null)
    {
        var props = source is null ? [] : new[] { new LogEventProperty("SourceContext", new ScalarValue(source)) };
        return new LogEvent(DateTimeOffset.UtcNow, level, null, new MessageTemplate(message, [new Serilog.Parsing.TextToken(message)]), props);
    }

    [Fact] public void KeepsOnlyTheNewestCapacityEntries_WithMonotonicSequence()
    {
        var sink = new RingBufferLogSink(3);
        for (int i = 1; i <= 5; i++) sink.Emit(Event(LogEventLevel.Information, "m" + i));
        var (entries, last) = sink.Read(0, LogEventLevel.Verbose, null, 10);
        Assert.Equal(5, last);
        Assert.Equal(["m3", "m4", "m5"], entries.Select(e => e.Message));
    }
    [Fact] public void Cursor_ReturnsOnlyNewEntries_AndFiltersByLevelAndSearch()
    {
        var sink = new RingBufferLogSink(10);
        sink.Emit(Event(LogEventLevel.Information, "alpha", "Svc.A"));
        var (_, cursor) = sink.Read(0, LogEventLevel.Verbose, null, 10);
        sink.Emit(Event(LogEventLevel.Warning, "Beta problem", "Svc.B"));
        sink.Emit(Event(LogEventLevel.Information, "gamma", "Svc.C"));
        Assert.Equal(["Beta problem", "gamma"], sink.Read(cursor, LogEventLevel.Verbose, null, 10).Entries.Select(e => e.Message));
        Assert.Equal(["Beta problem"], sink.Read(cursor, LogEventLevel.Warning, null, 10).Entries.Select(e => e.Message));
        Assert.Equal(["gamma"], sink.Read(0, LogEventLevel.Verbose, "svc.c", 10).Entries.Select(e => e.Message));
        Assert.Single(sink.Read(0, LogEventLevel.Verbose, null, 1).Entries);
    }
    [Fact] public void LoggerPipeline_FeedsTheRingAfterRedaction()
    {
        var ring = new RingBufferLogSink(10);
        string root = Path.Combine(Path.GetTempPath(), "animora-ring-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var logger = LoggingSetup.CreateLogger(root, false, new HttpContextAccessor(), new NullSink(), ring))
                logger.Information("Live marker {Password}", "ring-secret-never-visible");
            var entry = Assert.Single(ring.Read(0, LogEventLevel.Verbose, null, 10).Entries);
            Assert.Contains("Live marker", entry.Message); Assert.DoesNotContain("ring-secret-never-visible", entry.Message);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class NullSink : Serilog.Core.ILogEventSink { public void Emit(LogEvent logEvent) { } }
}
