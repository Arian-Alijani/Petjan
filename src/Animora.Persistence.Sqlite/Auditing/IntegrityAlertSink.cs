using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Correlation;
using Animora.Application.Ports;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Persistence.Sqlite.Auditing;

/// <summary>Persists command-integrity alerts and surfaces them as needs-attention notifications.</summary>
public sealed class IntegrityAlertSink(
    IAuditWriter audit,
    IUserNotificationPort notifications,
    ICorrelationContext correlation,
    IClock clock) : IIntegrityAlertSink
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

        return await notifications.RaiseAsync(
            new UserNotification(UserNotificationClass.NeedsAttention, alert.Code, alert.Detail), ct)
            .ConfigureAwait(false);
    }
}
