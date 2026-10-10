using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
namespace Animora.Persistence.PostgreSql.Options;

/// <summary>Bridges Npgsql's actual leased-connection gauges using fixed non-secret pool names.</summary>
public sealed class DatabasePoolUsage : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentDictionary<string,int> _used=new(StringComparer.Ordinal);
    private static readonly HashSet<string> Pools=["Animora.Server/oltp-write","Animora.Server/oltp-read","Animora.Server/keyring","Animora.Server/worker"];
    public DatabasePoolUsage()
    {
        _listener=new MeterListener { InstrumentPublished=(instrument,listener)=>
        { if(instrument.Meter.Name=="Npgsql"&&instrument.Name=="db.client.connection.count")listener.EnableMeasurementEvents(instrument); } };
        _listener.SetMeasurementEventCallback<int>((i,v,t,s)=>Record(v,t));
        _listener.SetMeasurementEventCallback<long>((i,v,t,s)=>Record((int)Math.Min(int.MaxValue,v),t));
        _listener.Start();
    }
    private void Record(int count,ReadOnlySpan<KeyValuePair<string,object?>> tags)
    {
        string? pool=null,state=null;
        foreach(var tag in tags)
        { if(tag.Key=="db.client.connection.pool.name")pool=tag.Value as string;if(tag.Key=="db.client.connection.state")state=tag.Value as string; }
        if(pool is not null&&Pools.Contains(pool)&&state=="used")_used[pool]=count;
    }
    public IReadOnlyDictionary<string,int> Snapshot()
    { _listener.RecordObservableInstruments();return Pools.ToDictionary(p=>p,p=>_used.GetValueOrDefault(p)); }
    public void Dispose()=>_listener.Dispose();
}
