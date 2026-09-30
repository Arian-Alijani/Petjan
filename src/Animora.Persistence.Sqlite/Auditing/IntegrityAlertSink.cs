using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Correlation;
using Animora.Contracts.Diagnostics;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Persistence.Sqlite.Auditing;

/// <summary>Persists command-integrity alerts and surfaces them as needs-attention notifications.</summary>
/// <remarks>
/// The <see cref="IUserNotificationPort"/> is optional: only presentation hosts (the desktop app) register it.
/// Headless hosts that compose the local store standalone — the manual-test data tool, background/sync services,
/// and integration tests — have no UI seam. Auditing is the security-critical guarantee (A-RULE 019) and always
/// runs; the toast is a best-effort presentation nicety that is simply skipped when no port is available.
/// </remarks>
public sealed class IntegrityAlertSink(
    IAuditWriter audit,
    ICorrelationContext correlation,
    IClock clock,
    IUserNotificationPort? notifications = null) : IIntegrityAlertSink
{
    public async Task<Result> RaiseAsync(IntegrityAlert alert, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        var record = new AuditRecord(
            alert.TenantId,
            alert.UserId,
            alert.DeviceId,
            clock.UtcNow,
            AuditCategory.Integrity,
            "integrity.command.fingerprint_mismatch",
            alert.EntityType,
            alert.EntityId,
            alert.OperationId,
            correlation.CurrentId ?? alert.OperationId,
            AuditOutcome.Failed,
            null,
            null,
            null,
            null,
            $"{alert.Code}: {alert.Detail}");

        var written = await audit.WriteAsync(record, ct).ConfigureAwait(false);
        if (written.IsFailure) return Result.Fail(ErrorCodes.AuditWriteFailed, written.Detail ?? written.Error.Code);

        notifications?.Notify(NotificationSeverity.NeedsAttention, alert.Code,
            alert.EntityId, new NotificationAction("open-diagnostics", "diagnostics"));
        return Result.Ok();
    }
}
