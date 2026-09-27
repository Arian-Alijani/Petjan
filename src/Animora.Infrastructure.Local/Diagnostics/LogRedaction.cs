using Animora.Application.Correlation;
using Serilog.Core;
using Serilog.Events;

namespace Animora.Infrastructure.Local.Diagnostics;

/// <summary>Recursively redacts sensitive structured properties before they reach a sink.</summary>
public sealed class LogRedaction : ILogEventEnricher
{
    private readonly ICorrelationContext _correlationContext = new CorrelationContext();

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "PasswordHash", "Passphrase", "Token", "Key", "Signature", "NationalId", "Phone",
    };

    private static readonly ScalarValue Redacted = new("[redacted]");

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        AddCorrelation(logEvent, propertyFactory);
        foreach (var property in logEvent.Properties.ToArray())
            logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, Rewrite(property.Key, property.Value)));
    }

    private void AddCorrelation(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlation = _correlationContext.Current;
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlation?.CorrelationId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TenantId", correlation?.TenantId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UserId", correlation?.UserId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("DeviceId", correlation?.DeviceId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("OperationId", correlation?.OperationId));
    }

    private static LogEventPropertyValue Rewrite(string? name, LogEventPropertyValue value)
    {
        if (name is not null && SensitiveNames.Contains(name)) return Redacted;

        return value switch
        {
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(item => Rewrite(null, item))),
            StructureValue structure => new StructureValue(
                structure.Properties.Select(property =>
                    new LogEventProperty(property.Name, Rewrite(property.Name, property.Value))),
                structure.TypeTag),
            DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.ToDictionary(
                pair => pair.Key,
                pair => Rewrite(pair.Key.Value?.ToString(), pair.Value))),
            _ => value,
        };
    }
}
