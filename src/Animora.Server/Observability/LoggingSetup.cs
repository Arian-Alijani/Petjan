using Animora.Server.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
namespace Animora.Server.Observability;

internal static class LoggingSetup
{
    public const int RetainedFiles=14;
    public const long FileSizeLimit=50L*1024*1024;
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<Serilog.ILogger>(sp=>
        {
            var root=sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServerOptions>>().Value.DataRootPath;
            return CreateLogger(root, builder.Environment.IsDevelopment(), sp.GetRequiredService<IHttpContextAccessor>());
        });
        builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(sp=>new Serilog.Extensions.Logging.SerilogLoggerProvider(sp.GetRequiredService<Serilog.ILogger>(),dispose:true));
    }
    internal static Logger CreateLogger(string root, bool development, IHttpContextAccessor accessor, ILogEventSink? consoleSink = null)
    {
        var config = new LoggerConfiguration().MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            // OBS-STARTUP-01: the host lifetime category carries the "Now listening on …",
            // "Application started" and "Hosting environment" banner. Keep it at Information
            // (above the blanket Microsoft→Warning override) so an operator watching the
            // console / journalctl sees a clear "the server is up and NOT stuck" signal — the
            // absence of this line is exactly why a healthy Production boot looked frozen.
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Destructure.With(new SecretDestructuringPolicy())
            .Enrich.With(new RedactingEnricher(accessor))
            .WriteTo.File(Path.Combine(root, "logs", "server-.log"), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFiles, fileSizeLimitBytes: FileSizeLimit, rollOnFileSizeLimit: true,
                shared: false, buffered: true, flushToDiskInterval: TimeSpan.FromSeconds(1));
        // OBS-STARTUP-01: write to the console in every environment (not just Development). The
        // sink runs AFTER the secret-destructuring policy and redacting enricher, so nothing
        // sensitive reaches stdout. systemd captures stdout into journalctl, and a manual
        // `dotnet run` now prints lifecycle output instead of looking hung.
        config.WriteTo.Sink(consoleSink ?? new ConsoleSink());
        return config.CreateLogger();
    }
    private sealed class ConsoleSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)=>Console.WriteLine("{0:O} [{1}] {2}",logEvent.Timestamp,logEvent.Level,logEvent.RenderMessage());
    }
}
internal sealed class RequestLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext c,ILogger<RequestLoggingMiddleware> logger,ServerMetrics metrics)
    {
        if (c.Request.Path.StartsWithSegments("/health")) { await next(c).ConfigureAwait(false); return; }
        var start=System.Diagnostics.Stopwatch.GetTimestamp();
        try { await next(c).ConfigureAwait(false); }
        catch { if(!c.Response.HasStarted)c.Response.StatusCode=500; throw; }
        finally
        {
            double ms=System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            metrics.HttpRequests.Add(1,new KeyValuePair<string,object?>("status",c.Response.StatusCode)); metrics.HttpDuration.Record(ms);
            if (c.Response.StatusCode>=400||Random.Shared.Next(20)==0)
                // Neither raw path/query, bodies, headers nor exception messages are logged.
                logger.LogInformation("HTTP {Method} {StatusCode} in {DurationMs} ms",c.Request.Method,c.Response.StatusCode,ms);
        }
    }
}
