using System.Diagnostics;
using System.Diagnostics.Metrics;
using Animora.Server.Observability;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using Serilog.Events;
using Xunit;
using Animora.Persistence.PostgreSql.Options;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Sync;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
namespace Animora.Server.Tests.Observability;

public sealed class TelemetryOptionsTests
{
    [Theory] [InlineData("http://127.0.0.1:4317",false,true)] [InlineData("http://[::1]:4317",false,true)]
    [InlineData("http://localhost:4317",false,true)] [InlineData("https://8.8.8.8",true,false)]
    [InlineData("http://10.0.0.2",false,false)] [InlineData("http://10.0.0.2",true,true)]
    [InlineData("http://[fd00::1]",true,true)] [InlineData("http://example.com",true,false)]
    [InlineData("http://127.0.0.1.evil.example",false,false)] [InlineData("http://user:password@127.0.0.1",false,false)]
    public void PublicHost_Rejected_Loopback_Allowed_LanOnlyWithOptIn(string endpoint,bool lan,bool allowed)=>
        Assert.Equal(allowed,new TelemetryOptionsValidator().Validate(null,new() { OtlpEndpoint=endpoint,AllowLanCollector=lan }).Succeeded);
}
public sealed class TelemetryTests
{
    [Fact] public void Disabled_ConfiguresNoExporter()
    {
        var services=new ServiceCollection(); services.AddAnimoraTelemetry(new ConfigurationBuilder().Build());
        Assert.DoesNotContain(services,s=>s.ServiceType==typeof(OpenTelemetry.Trace.TracerProvider)||s.ServiceType==typeof(OpenTelemetry.Metrics.MeterProvider));
    }
    private sealed class FailingExporter : BaseExporter<Activity> { public override ExportResult Export(in Batch<Activity> batch)=>throw new IOException("collector unavailable"); }
    [Fact] public void UnreachableCollector_DoesNotAffectRequests()
    { using var exporter=new SafeExporter<Activity>(new FailingExporter(),new ExporterWarningGate(NullLogger<ExporterWarningGate>.Instance)); var batch=default(Batch<Activity>); Assert.Equal(ExportResult.Failure,exporter.Export(batch)); }
}
public sealed class RedactionTests
{
    [Fact] public void SecretPropertiesAndHeaders_NeverLogged()
    {
        var value=new StructureValue([new("Password",new ScalarValue("do-not-log")),new("Headers",new DictionaryValue([
            new(new ScalarValue("X-Animora-Valet"),new ScalarValue("valet-secret"))])),new("FileId",new ScalarValue("safe-id"))]);
        string redacted=RedactingEnricher.Redact(value).ToString(); Assert.DoesNotContain("do-not-log",redacted); Assert.DoesNotContain("valet-secret",redacted); Assert.Contains("safe-id",redacted);
    }
    [Fact] public void BodiesNeverLogged()
    { string source=File.ReadAllText(Path.Combine(RepoRoot.Find(),"src/Animora.Server/Observability/LoggingSetup.cs")); Assert.DoesNotContain("Request.Body",source); Assert.DoesNotContain("Request.Headers",source); Assert.DoesNotContain("Request.Query",source); }
}
public sealed class LoggingTests
{
    [Fact] public void RollingFile_Limits() { Assert.Equal(14,LoggingSetup.RetainedFiles); Assert.Equal(50L*1024*1024,LoggingSetup.FileSizeLimit); }
    [Fact] public void ConsoleSink_OnlyInDevelopment()=>Assert.Contains("if (development)",File.ReadAllText(Path.Combine(RepoRoot.Find(),"src/Animora.Server/Observability/LoggingSetup.cs")));
}
public sealed class KestrelProfileTests
{
    [Theory] [InlineData(2,256)] [InlineData(4,512)] public void Limits_By_ProfileGiB(int profile,long connections)
    { var options=new KestrelServerOptions(); KestrelProfile.Apply(options,profile); Assert.Equal(connections,options.Limits.MaxConcurrentConnections); Assert.Equal(2L*1024*1024,options.Limits.MaxRequestBodySize); Assert.Equal(64,options.Limits.MaxConcurrentUpgradedConnections); }
}
public sealed class TenantScopedCacheTests
{
    [Fact] public void MissingKeyPart_Throws()=>Assert.Throws<ArgumentException>(()=>new TenantCacheKey("","user","resource","v"));
    [Fact] public void TenantsNeverShareEntries()
    { using var cache=new TenantScopedMemoryCache<string,string>(); var a=new TenantCacheKey("a","u","r","v"); cache.Set(a,"key","secret"); Assert.False(cache.TryGet(new("b","u","r","v"),"key",out _)); Assert.True(cache.TryGet(a,"key",out _)); }
    [Fact] public void BoundedByCount()
    { using var cache=new TenantScopedMemoryCache<int,string>(2); var scope=new TenantCacheKey("t","u","r","v"); cache.Set(scope,1,"1"); cache.Set(scope,2,"2"); Assert.True(cache.TryGet(scope,1,out _)); cache.Set(scope,3,"3"); Assert.False(cache.TryGet(scope,2,out _)); Assert.True(cache.TryGet(scope,1,out _)); }
}
public sealed class MetricsTests
{
    [Fact] public void NoTenantLabelOnAnyInstrument()
    {
        var tags=new List<string>(); using var listener=new MeterListener { InstrumentPublished=(i,l)=>{ if(i.Meter.Name==ServerMetrics.MeterName) l.EnableMeasurementEvents(i); } };
        listener.SetMeasurementEventCallback<long>((i,v,t,s)=>{foreach(var tag in t)tags.Add(tag.Key);}); listener.Start();
        using var metrics=new ServerMetrics(); metrics.CommandsSettled.Add(1,new KeyValuePair<string,object?>("outcome","Accepted")); metrics.Reconcile("orphan"); listener.RecordObservableInstruments();
        Assert.DoesNotContain(tags,t=>t.Contains("tenant",StringComparison.OrdinalIgnoreCase));
    }
}
public sealed class PostgresProfileFilesTests
{
    [Theory] [InlineData(2)] [InlineData(4)] public void PoolSumPlusReserve_LE_MaxConnections(int profile)
    { var lines=File.ReadAllLines(Path.Combine(RepoRoot.Find(),$"deploy/postgresql/animora-{profile}gib.conf")); int max=int.Parse(lines.Single(l=>l.StartsWith("max_connections",StringComparison.Ordinal)).Split('=')[1]); Assert.True(max>=new Animora.Persistence.PostgreSql.Options.DatabaseOptions().PoolSum+8); }
}


internal sealed class CapturingSink : ILogEventSink
{
    public List<LogEvent> Events { get; } = [];
    public void Emit(LogEvent logEvent) => Events.Add(logEvent);
}
public sealed class ObservabilityBehaviorTests
{
    private sealed class UnavailableCollector : BaseExporter<Activity>
    { public override ExportResult Export(in Batch<Activity> batch) => throw new IOException("collector unavailable"); }
    [Fact] public async Task CollectorFailure_RequestsComplete_AndWarningIsRateLimited()
    {
        var sink = new CapturingSink();
        using var log = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var loggerFactory = new LoggerFactory([new Serilog.Extensions.Logging.SerilogLoggerProvider(log, dispose: false)]);
        using var exporter = new SafeExporter<Activity>(new UnavailableCollector(), new ExporterWarningGate(loggerFactory.CreateLogger<ExporterWarningGate>()));
        using var metrics = new ServerMetrics();
        var middleware = new RequestLoggingMiddleware(async c =>
        {
            var batch = default(Batch<Activity>);
            Assert.Equal(ExportResult.Failure, exporter.Export(batch));
            Assert.Equal(ExportResult.Failure, exporter.Export(batch));
            await c.Response.WriteAsync("request completed");
        });
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context, NullLogger<RequestLoggingMiddleware>.Instance, metrics);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("request completed", System.Text.Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        Assert.Single(sink.Events); Assert.Equal(LogEventLevel.Warning, sink.Events[0].Level);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ConsoleDevelopmentOnly_FileAlwaysRedacted(bool development)
    {
        string root = Path.Combine(Path.GetTempPath(), "animora-logging-" + Guid.NewGuid().ToString("N"));
        var sink = new CapturingSink();
        try
        {
            using (var logger = LoggingSetup.CreateLogger(root, development, new HttpContextAccessor(), sink))
                logger.Information("Safe marker {Password} {XAnimoraValet}", "password-never-visible", "valet-never-visible");
            Assert.Equal(development ? 1 : 0, sink.Events.Count);
            string written = string.Join("\n", Directory.GetFiles(Path.Combine(root, "logs")).Select(File.ReadAllText));
            Assert.Contains("Safe marker", written); Assert.DoesNotContain("password-never-visible", written); Assert.DoesNotContain("valet-never-visible", written);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public void EverySensitiveNameAndNestedHeader_IsRedactedAtSink()
    {
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration().Enrich.With(new RedactingEnricher(new HttpContextAccessor())).WriteTo.Sink(sink).CreateLogger();
        foreach (string name in new[] { "password", "token", "refreshToken", "authorization", "valet", "x-animora-valet",
            "connectionString", "secret", "licenseKey", "payloadJson", "detailJson" })
        {
            var entry = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
                new Serilog.Parsing.MessageTemplateParser().Parse("Sensitive event"), [new LogEventProperty(name, new ScalarValue("forbidden-marker"))]);
            logger.Write(entry);
        }
        Assert.Equal(11, sink.Events.Count);
        Assert.All(sink.Events, e => Assert.DoesNotContain("forbidden-marker", string.Join(" ", e.Properties.Values)));
    }
    [Fact] public async Task RequestBodiesAndSecrets_NeverReachLogSink_HealthSkipped()
    {
        var sink = new CapturingSink();
        using var log = new LoggerConfiguration().Destructure.With(new SecretDestructuringPolicy())
            .Enrich.With(new RedactingEnricher(new HttpContextAccessor())).WriteTo.Sink(sink).CreateLogger();
        using var provider = new Serilog.Extensions.Logging.SerilogLoggerProvider(log, dispose: false);
        var logger = new Logger<RequestLoggingMiddleware>(new LoggerFactory([provider]));
        using var metrics = new ServerMetrics();
        var middleware = new RequestLoggingMiddleware(c => { c.Response.StatusCode = 400; return Task.CompletedTask; });
        var context = new DefaultHttpContext(); context.Request.Path = "/files/secret-path";
        context.Request.QueryString = new("?token=secret-query"); context.Request.Headers.Authorization = "Bearer secret-header";
        context.Request.Body = new MemoryStream("secret-request-body"u8.ToArray());
        await middleware.InvokeAsync(context, logger, metrics);
        Assert.Single(sink.Events);
        string rendered = sink.Events[0].RenderMessage();
        Assert.DoesNotContain("secret", rendered); Assert.Contains("400", rendered);
        log.Information("Request {@Request} Response {@Response}", context.Request, context.Response);
        Assert.DoesNotContain("secret-request-body", sink.Events[^1].RenderMessage());
        context.Request.Path = "/health/ready";
        await middleware.InvokeAsync(context, logger, metrics); Assert.Equal(2, sink.Events.Count);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void AllMandatoryCacheKeyParts_RejectWhitespace(int missing)
    {
        var parts = new[] { "tenant", "user", "resource", "version" }; parts[missing] = " ";
        Assert.Throws<ArgumentException>(() => new TenantCacheKey(parts[0], parts[1], parts[2], parts[3]));
    }
    [Fact] public void Cache_ScopeIncludesUserResourceVersion_AndOverwritePreservesBound()
    {
        using var cache = new TenantScopedMemoryCache<string, string>(1);
        var scope = new TenantCacheKey("t", "u", "r", "v");
        cache.Set(scope, "k", "old"); cache.Set(scope, "k", "new");
        Assert.True(cache.TryGet(scope, "k", out var value)); Assert.Equal("new", value);
        foreach (var other in new TenantCacheKey[] { new("t", "other", "r", "v"), new("t", "u", "other", "v"), new("t", "u", "r", "other") })
            Assert.False(cache.TryGet(other, "k", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TenantScopedMemoryCache<int, string>(0));
    }
    [Theory] [InlineData(2)] [InlineData(4)]
    public void KestrelTimeoutsAndSlowBodyProtection_AreConfigured(int profile)
    {
        var options = new KestrelServerOptions(); KestrelProfile.Apply(options, profile);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Limits.RequestHeadersTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.Limits.KeepAliveTimeout);
        Assert.Equal(240d, options.Limits.MinRequestBodyDataRate!.BytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Limits.MinRequestBodyDataRate.GracePeriod);
    }
    [Fact] public async Task SyncByteProducer_CountsTransferredRepresentation_NotHeadOrFailedWrites()
    {
        using var metrics = new ServerMetrics(); long bytes = 0;
        using var listener = new MeterListener { InstrumentPublished = (i, l) => { if (i == metrics.SyncBytes) l.EnableMeasurementEvents(i); } };
        listener.SetMeasurementEventCallback<long>((i, v, t, state) => bytes += v); listener.Start();
        byte[] payload = new byte[100000];
        var context = new DefaultHttpContext(); context.Request.Method = "GET"; context.Response.Body = new MemoryStream();
        var source = new MemoryStream(payload);
        await new SyncEndpoints.SyncPageResult(new FeedPageDownload(source, "digest", payload.Length), metrics).ExecuteAsync(context);
        Assert.Equal(payload.Length, bytes); Assert.Equal(payload.Length, context.Response.Body.Length);
        Assert.False(source.CanRead); Assert.Equal("application/gzip", context.Response.ContentType);
        context.Request.Method = "HEAD"; context.Response.Body = new MemoryStream();
        await new SyncEndpoints.SyncPageResult(new(new MemoryStream(payload), "digest", payload.Length), metrics).ExecuteAsync(context);
        Assert.Equal(payload.Length, bytes); Assert.Equal(0, context.Response.Body.Length);
        context.Request.Method = "GET"; context.Response.Body = new RejectingStream();
        await Assert.ThrowsAsync<IOException>(() => new SyncEndpoints.SyncPageResult(new(new MemoryStream(payload), "digest", payload.Length), metrics).ExecuteAsync(context));
        Assert.Equal(payload.Length, bytes);
    }
    private sealed class RejectingStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => ValueTask.FromException(new IOException("client disconnected"));
    }
    [Fact] public void FullMetricCatalogue_IsProducedWithoutIdentityDimensions()
    {
        using var metrics = new ServerMetrics(); var produced = new HashSet<string>(); var keys = new HashSet<string>();
        using var listener = new MeterListener { InstrumentPublished = (i, l) => { if (i.Meter == metrics.Meter) l.EnableMeasurementEvents(i); } };
        void Record(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        { produced.Add(instrument.Name); foreach (var tag in tags) keys.Add(tag.Key); }
        listener.SetMeasurementEventCallback<long>((i, v, t, state) => Record(i, t));
        listener.SetMeasurementEventCallback<int>((i, v, t, state) => Record(i, t));
        listener.SetMeasurementEventCallback<double>((i, v, t, state) => Record(i, t)); listener.Start();
        metrics.ObservePools(() => [new Measurement<int>(1, new KeyValuePair<string, object?>("source", "worker"))]);
        metrics.ObserveWorkers(() => [new Measurement<double>(2, new KeyValuePair<string, object?>("worker", "snapshot-age"))]);
        using (metrics.CommandLease())
        {
            metrics.SetQueues(1, 2, 3); metrics.SetSnapshotAge(4);
            metrics.HttpRequests.Add(1); metrics.HttpDuration.Record(1);
            metrics.CommandsSettled.Add(1, new KeyValuePair<string, object?>("outcome", "Accepted"), new KeyValuePair<string, object?>("commandType", "registered"));
            metrics.SyncBatches.Add(1); metrics.SyncDuration.Record(1); metrics.SyncBytes.Add(12); metrics.RateLimitRejected.Add(1);
            foreach (string finding in new[] { "abandoned", "blob_missing", "zero_ref", "orphan", "bitrot" }) metrics.Reconcile(finding);
            listener.RecordObservableInstruments(); Assert.Equal(1, metrics.CommandsInProgress);
        }
        Assert.Equal(0, metrics.CommandsInProgress); Assert.Equal(3, metrics.IntakeInProgress);
        Assert.Equal(18, produced.Count);
        Assert.All(keys, key => Assert.Contains(key, new[] { "outcome", "commandType", "source", "worker" }));
    }
}
public sealed class QueryBudgetTests
{
    private sealed class InstantFactory : IReportingQuerySessionFactory
    {
        public Task<ReportingQuerySession> OpenAsync(CancellationToken ct = default) => throw new InvalidOperationException("not opened in unit test");
    }
    [Fact] public async Task ReportingMax_AllowsConcurrentPermits_AndWaitTimesOutWithTypedException()
    {
        var gate = new TaskCompletionSource();
        var sessions = new GatedFactory(gate);
        using var budget = new QueryBudget(sessions, reportingMax: 2, wait: TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, budget.ReportingMax);
        Task<QueryLease> a = budget.Acquire(QueryClass.Reporting).AsTask();
        Task<QueryLease> b = budget.Acquire(QueryClass.Reporting).AsTask();
        await sessions.WaitForCalls(2);
        Assert.Equal(2, budget.ReportingInUse);
        await Assert.ThrowsAsync<QueryBudgetExceededException>(async () => { await budget.Acquire(QueryClass.Reporting); });
        Assert.Equal(1, budget.Timeouts);
        gate.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => a); await Assert.ThrowsAsync<InvalidOperationException>(() => b);
        Assert.Equal(0, budget.ReportingInUse);
    }
    private sealed class GatedFactory(TaskCompletionSource gate) : IReportingQuerySessionFactory
    {
        private int _calls;
        public async Task WaitForCalls(int n) { while (Volatile.Read(ref _calls) < n) await Task.Delay(5); }
        public async Task<ReportingQuerySession> OpenAsync(CancellationToken ct = default)
        { Interlocked.Increment(ref _calls); await gate.Task; throw new InvalidOperationException("released"); }
    }
    private sealed class BlockingFactory : IReportingQuerySessionFactory
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public async Task<ReportingQuerySession> OpenAsync(CancellationToken ct = default)
        { Interlocked.Increment(ref Calls); Started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); }
    }
    [Fact] public async Task ReportingConcurrency1_CancellationReleasesGate_OltpNeverOpensReportingPool()
    {
        var sessions = new BlockingFactory(); using var budget = new QueryBudget(sessions);
        using var cancellation = new CancellationTokenSource();
        Task<QueryLease> first = budget.Acquire(QueryClass.Reporting, cancellation.Token).AsTask(); await sessions.Started.Task;
        using var secondCancellation = new CancellationTokenSource();
        Task<QueryLease> second = budget.Acquire(QueryClass.Reporting, secondCancellation.Token).AsTask();
        Assert.Equal(1, sessions.Calls);
        await using (var oltp = await budget.Acquire(QueryClass.Oltp)) Assert.Null(oltp.Reporting);
        secondCancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        using var thirdCancellation = new CancellationTokenSource();
        Task<QueryLease> third = budget.Acquire(QueryClass.Reporting, thirdCancellation.Token).AsTask();
        Assert.Equal(2, sessions.Calls); thirdCancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        Assert.Equal(5, ReportingQuerySession.TimeoutSeconds); Assert.Equal(10000, ReportingQuerySession.MaxRows);
    }
}
