using Animora.Application.Auditing;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;

namespace Animora.Server.Commands;

internal sealed class ServerCommandAuditWriter(ISecurityAuditWriter writer, AnimoraServerDbContext context) : IAuditWriter
{
    public Task<Result> WriteAsync(AuditRecord record, CancellationToken ct = default) => writer.WriteAsync(Create(record), ct);
    public void Enlist(AuditRecord record) => context.SecurityAudit.Add(Create(record));
    private static SecurityAuditEntry Create(AuditRecord record) => SecurityAuditEntry.Create(
        IdGenerator.NewId(record.OccurredAtUtc), record.TenantId, record.OccurredAtUtc,
        record.Category.ToString().ToLowerInvariant(), record.Action, record.Outcome.ToString(),
        record.CorrelationId, record.UserId, record.DeviceId,
        SecurityAuditDetail.Create(("operationId", record.OperationId), ("entityId", record.EntityId)));
}
