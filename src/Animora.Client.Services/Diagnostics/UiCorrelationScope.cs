using Animora.Application.Correlation;

namespace Animora.Client.Services.Diagnostics;

/// <summary>Establishes all per-gesture correlation fields for dispatch, audit and logging.</summary>
public sealed class UiCorrelationScope : IDisposable
{
    private readonly IDisposable _scope;
    private bool _disposed;

    public UiCorrelationScope(ICorrelationContext correlationContext, string tenantId, string userId,
        string deviceId, string operationId, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(correlationContext);
        _scope = correlationContext.Begin(new AmbientCorrelation(
            string.IsNullOrWhiteSpace(correlationId) ? Animora.Domain.Primitives.IdGenerator.NewId() : correlationId,
            tenantId,
            userId,
            deviceId,
            operationId));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scope.Dispose();
    }
}
