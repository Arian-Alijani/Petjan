using System.Collections.Concurrent;
namespace Animora.Server.Observability;

/// <summary>Last observed probe statuses only; admin rendering never executes health/database probes.</summary>
internal sealed class ReadinessSnapshot
{
    private readonly ConcurrentDictionary<string,string> _statuses=new(StringComparer.Ordinal);
    public void Set(string name,bool healthy,bool required)=>_statuses[name]=healthy?"healthy":required?"unhealthy":"degraded";
    public string Get(string name)=>_statuses.GetValueOrDefault(name,"degraded");
}
