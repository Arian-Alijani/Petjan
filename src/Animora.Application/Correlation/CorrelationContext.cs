using Animora.Domain.Primitives;

namespace Animora.Application.Correlation;

/// <summary><see cref="AsyncLocal{T}"/>-backed <see cref="ICorrelationContext"/>.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CurrentId => Current.Value;

    public IDisposable Begin(string? id = null)
    {
        var previous = Current.Value;
        Current.Value = string.IsNullOrWhiteSpace(id) ? previous ?? IdGenerator.NewId() : id;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Current.Value = previous;
        }
    }
}
