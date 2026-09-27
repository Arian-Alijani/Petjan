using Animora.Infrastructure.Local.Options;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Events;

namespace Animora.Infrastructure.Local.Diagnostics;

/// <summary>Creates the local-only Serilog pipeline. Sink failures never stop the application.</summary>
public static class LoggingSetup
{
    public static ILoggerFactory ConfigureLocalLogging(LocalLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            var logDirectory = Path.Combine(Path.GetFullPath(options.DataDirectory), "logs");
            Directory.CreateDirectory(logDirectory);

            var logger = new LoggerConfiguration()
                .MinimumLevel.Is(options.MinimumLevel)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore",
                    options.EnableSqlLogging ? options.MinimumLevel : LogEventLevel.Warning)
                .Enrich.With<LogRedaction>()
                .WriteTo.File(
                    Path.Combine(logDirectory, "animora-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.RetainedFiles,
                    fileSizeLimitBytes: options.FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    shared: false,
                    buffered: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(2))
                .CreateLogger();

            return new SerilogLoggerFactory(logger, dispose: true);
        }
        catch
        {
            // Logging is diagnostic-only: an unavailable or removed directory must not break startup.
            return LoggerFactory.Create(_ => { });
        }
    }
}
