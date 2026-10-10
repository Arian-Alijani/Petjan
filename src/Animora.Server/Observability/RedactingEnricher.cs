using Animora.Domain.Primitives;
using Animora.Server.Hosting;
using Serilog.Core;
using Serilog.Events;
namespace Animora.Server.Observability;

internal sealed class RedactingEnricher(IHttpContextAccessor accessor) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        foreach (var property in logEvent.Properties.ToArray()) logEvent.AddOrUpdateProperty(new(property.Key,
            SensitiveLogNames.IsSensitive(property.Key) ? new ScalarValue("[REDACTED]") : Redact(property.Value)));
        var c=accessor.HttpContext;
        foreach (var pair in new Dictionary<string,string?> { ["TenantId"]=c?.User.FindFirst("tid")?.Value,
            ["DeviceId"]=c?.User.FindFirst("did")?.Value,["OperationId"]=c?.Items["OperationId"] as string,
            ["RequestId"]=c?.TraceIdentifier,["CorrelationId"]=c is null?null:CorrelationHeader.Resolve(c) })
            logEvent.AddPropertyIfAbsent(factory.CreateProperty(pair.Key,pair.Value));
    }
    internal static LogEventPropertyValue Redact(LogEventPropertyValue value) => value switch
    {
        StructureValue s => new StructureValue(s.Properties.Select(p=>new LogEventProperty(p.Name,SensitiveLogNames.IsSensitive(p.Name)?new ScalarValue("[REDACTED]"):Redact(p.Value))),s.TypeTag),
        SequenceValue s => new SequenceValue(s.Elements.Select(Redact)),
        DictionaryValue d => new DictionaryValue(d.Elements.Select(p=>new KeyValuePair<ScalarValue,LogEventPropertyValue>(p.Key,
            SensitiveLogNames.IsSensitive(p.Key.Value?.ToString()??"")?new ScalarValue("[REDACTED]"):Redact(p.Value)))),
        _ => value
    };
}
internal sealed class SecretDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory factory, out LogEventPropertyValue result)
    {
        if (value is SyncableEntity entity) { result = new StructureValue([new("Id",new ScalarValue(entity.Id))]); return true; }
        if (value is HttpRequest or HttpResponse or Stream) { result=new ScalarValue("[REDACTED]"); return true; }
        // Avoid reflecting unknown entities/DTOs: only explicitly safe scalar/string collections are retained.
        if (value is not string && !value.GetType().IsPrimitive && value is not decimal && value is not DateTimeOffset && value is not DateTime && value is not Guid && !value.GetType().IsEnum)
        { result=new ScalarValue("[OBJECT]"); return true; }
        result=null!; return false;
    }
}
