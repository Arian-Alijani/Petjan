using Animora.Application.Auditing;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;

namespace Animora.Server.Commands;

internal sealed class ServerCommandAuditWriter(AnimoraServerDbContext context) : IAuditWriter
{
    public Task<Result> WriteAsync(AuditRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // The command unit of work flushes effects + audit atomically. An audit sink must not
        // flush unrelated staged effects and relabel their concurrency faults AUDIT_WRITE_FAILED.
        Enlist(record);
        return Task.FromResult(Result.Ok());
    }
    public void Enlist(AuditRecord record) => context.SecurityAudit.Add(Create(record));
    private static SecurityAuditEntry Create(AuditRecord record) => SecurityAuditEntry.Create(
        IdGenerator.NewId(record.OccurredAtUtc), record.TenantId, record.OccurredAtUtc,
        record.Category.ToString().ToLowerInvariant(), record.Action, record.Outcome.ToString(),
        record.CorrelationId, record.UserId, record.DeviceId,
        SecurityAuditDetail.Create(("operationId", record.OperationId), ("entityId", record.EntityId)));
}
