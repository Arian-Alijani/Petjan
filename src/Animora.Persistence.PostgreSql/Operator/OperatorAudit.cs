using System.Text.Json;
using Animora.Domain.Primitives;
using Animora.Domain.Subscriptions;

namespace Animora.Persistence.PostgreSql.Operator;

public sealed class OperatorAudit
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; set; }
    public string? OperatorId { get; set; }
    public ActorKind ActorKind { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TargetTenantId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string DetailJson { get; set; } = "{}";

    public static OperatorAudit Create(DateTimeOffset now, ActorRef actor, string action,
        string? tenantId, string correlationId, string outcome = "success", object? detail = null) => new()
        {
            Id = IdGenerator.NewId(now), OccurredUtc = now, OperatorId = actor.ActorId,
            ActorKind = actor.Kind, Action = action, TargetTenantId = tenantId, Outcome = outcome,
            CorrelationId = correlationId.Length > 64 ? correlationId[..64] : correlationId,
            // Callers pass allow-listed non-secret metadata, never request DTOs.
            DetailJson = JsonSerializer.Serialize(detail ?? new { }),
        };
}
