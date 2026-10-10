using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
namespace Animora.Server.Observability;

internal static class TelemetrySetup
{
    public static IServiceCollection AddAnimoraTelemetry(this IServiceCollection services,IConfiguration config)
    {
        services.AddOptions<TelemetryOptions>().Bind(config.GetSection(TelemetryOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TelemetryOptions>,TelemetryOptionsValidator>();
        var options=config.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()??new();
        if (!options.Enabled) return services;
        services.AddSingleton<ExporterWarningGate>();
        OtlpExporterOptions ExporterOptions()=>new() { Endpoint=new Uri(options.OtlpEndpoint),TimeoutMilliseconds=2000 };
        services.AddOpenTelemetry()
            .WithTracing(t=>t.AddAspNetCoreInstrumentation(o=>o.Filter=c=>!c.Request.Path.StartsWithSegments("/health"))
                .AddSource(ServerMetrics.MeterName)
                .AddProcessor(sp=>new BatchActivityExportProcessor(new SafeOtlpTraceExporter(ExporterOptions(),sp.GetRequiredService<ExporterWarningGate>()),maxQueueSize:512,maxExportBatchSize:128)))
            .WithMetrics(m=>m.AddAspNetCoreInstrumentation().AddRuntimeInstrumentation().AddMeter(ServerMetrics.MeterName)
                .AddReader(sp=>new PeriodicExportingMetricReader(new SafeOtlpMetricExporter(ExporterOptions(),sp.GetRequiredService<ExporterWarningGate>()),exportIntervalMilliseconds:15000,exportTimeoutMilliseconds:2500)));
        // No database instrumentation is registered: statement text cannot enter traces.
        return services;
    }
}
internal sealed class ExporterWarningGate(ILogger<ExporterWarningGate> logger)
{
    private long _next;
    public void Warn()
    {
        long now=Environment.TickCount64,previous=Interlocked.Read(ref _next);
        if (now<previous || Interlocked.CompareExchange(ref _next,now+60000,previous)!=previous) return;
        logger.LogWarning("Optional local telemetry collector unavailable; export dropped.");
    }
}
internal sealed class SafeExporter<T>(BaseExporter<T> inner,ExporterWarningGate warnings) : BaseExporter<T> where T:class
{
    public override ExportResult Export(in Batch<T> batch)
    {
        try { var result=inner.Export(batch); if (result!=ExportResult.Success) warnings.Warn(); return result; }
        catch (Exception) { warnings.Warn(); return ExportResult.Failure; }
    }
    protected override bool OnShutdown(int timeoutMilliseconds) { try { return inner.Shutdown(timeoutMilliseconds); } catch (Exception) { return true; } }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}

// Subclass the SDK exporters so ParentProvider/resource/temporality wiring is retained.
internal sealed class SafeOtlpTraceExporter(OtlpExporterOptions options,ExporterWarningGate warnings) : OtlpTraceExporter(options)
{
    public override ExportResult Export(in Batch<Activity> batch)
    { try { var result=base.Export(batch);if(result!=ExportResult.Success)warnings.Warn();return result; } catch(Exception) {warnings.Warn();return ExportResult.Failure;} }
}
internal sealed class SafeOtlpMetricExporter(OtlpExporterOptions options,ExporterWarningGate warnings) : OtlpMetricExporter(options)
{
    public override ExportResult Export(in Batch<Metric> batch)
    { try { var result=base.Export(batch);if(result!=ExportResult.Success)warnings.Warn();return result; } catch(Exception) {warnings.Warn();return ExportResult.Failure;} }
}
