using Animora.Domain.Primitives;

namespace Animora.Application.Correlation;

public sealed record AmbientCorrelation(
    string CorrelationId,
    string TenantId,
    string UserId,
    string DeviceId,
    string? OperationId);

/// <summary><see cref="AsyncLocal{T}"/>-backed <see cref="ICorrelationContext"/>.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<AmbientCorrelation?> Ambient = new();

    public string? CurrentId => Ambient.Value?.CorrelationId;
    public AmbientCorrelation? Current => Ambient.Value;

    public IDisposable Begin(string? id = null)
    {
        var previous = Ambient.Value;
        var correlationId = string.IsNullOrWhiteSpace(id) ? previous?.CorrelationId ?? IdGenerator.NewId() : id;
        return Set(previous is null
            ? new AmbientCorrelation(correlationId, string.Empty, string.Empty, string.Empty, null)
            : previous with { CorrelationId = correlationId });
    }

    public IDisposable Begin(AmbientCorrelation correlation)
    {
        ArgumentNullException.ThrowIfNull(correlation);
        return Set(correlation);
    }

    private static IDisposable Set(AmbientCorrelation current)
    {
        var previous = Ambient.Value;
        Ambient.Value = current;
        return new Scope(previous);
    }

    private sealed class Scope(AmbientCorrelation? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
