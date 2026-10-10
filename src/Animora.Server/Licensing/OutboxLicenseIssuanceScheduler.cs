using System.Text.Json;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Subscriptions;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Licensing;

internal sealed class OutboxLicenseIssuanceScheduler(AnimoraOperatorDbContext db, SubscriptionOutbox outbox) : ILicenseIssuanceScheduler
{
    public async Task ScheduleAsync(string tenantId, string subscriptionId, CancellationToken ct = default)
    {
        // Read the tracked aggregate BEFORE the caller saves; this also covers first provisioning.
        var source = db.Subscriptions.Local.FirstOrDefault(x => x.Id == subscriptionId && x.TenantId == tenantId)
            ?? await db.Subscriptions.AsNoTracking().SingleAsync(x => x.Id == subscriptionId && x.TenantId == tenantId, ct).ConfigureAwait(false);
        IServerOutbox port = outbox; // Operator-context adapter keeps the message in the exact caller transaction.
        var queued = await port.EnqueueAsync(OutboxMessageTypes.LicenseIssue, tenantId,
            JsonSerializer.Serialize(new { tenantId, subscriptionId, sourceEventSequence = source.NextEventSequence - 1 }),
            subscriptionId, ct).ConfigureAwait(false);
        if (queued.IsFailure) throw new InvalidOperationException("Unable to stage licence projection.");
    }
}
