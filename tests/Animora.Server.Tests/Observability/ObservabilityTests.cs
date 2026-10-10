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
    [Fact] public void ConsoleSink_OnlyInDevelopment()=>Assert.Contains("if (builder.Environment.IsDevelopment())",File.ReadAllText(Path.Combine(RepoRoot.Find(),"src/Animora.Server/Observability/LoggingSetup.cs")));
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
