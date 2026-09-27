using Animora.Domain.Results;

namespace Animora.Application.Commands;

/// <summary>
/// Sink for integrity events raised by the command pipeline (A-RULE 019). A fingerprint mismatch on a replayed
/// <c>OperationId</c> is a security event that must be audited, never silently treated as a replay. The real
/// implementation lands in B05; the default is a no-op so the pipeline is complete today.
/// </summary>
public interface IIntegrityAlertSink
{
    /// <summary>Durably records and surfaces an integrity alert; audit failure is returned to the caller.</summary>
    Task<Result> RaiseAsync(IntegrityAlert alert, CancellationToken ct = default);
}

public sealed record IntegrityAlert(
    string Code,
    string Detail,
    string TenantId,
    string UserId,
    string DeviceId,
    string OperationId,
    string? EntityType,
    string? EntityId);

/// <summary>Fallback used only when the local audit-backed sink has not been registered.</summary>
public sealed class NullIntegrityAlertSink : IIntegrityAlertSink
{
    public Task<Result> RaiseAsync(IntegrityAlert alert, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return Task.FromResult(Result.Ok());
    }
}
