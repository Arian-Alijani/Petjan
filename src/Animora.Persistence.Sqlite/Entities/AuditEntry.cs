using Animora.Application.Auditing;
using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Durable, append-only, device-local audit record.</summary>
[LocalOnly]
public sealed class AuditEntry
{
    public const int ActionMaxLength = 200;
    public const int EntityTypeMaxLength = 128;
    public const int ReferenceMaxLength = 256;
    public const int DetailMaxLength = 1000;

    private AuditEntry()
    {
    }

    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string DeviceId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public AuditCategory Category { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string OperationId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public AuditOutcome Outcome { get; private set; }
    public string? BeforeRef { get; private set; }
    public string? AfterRef { get; private set; }
    public long? AmountMinor { get; private set; }
    public string? AmountCurrency { get; private set; }
    public string? Detail { get; private set; }

    public static AuditEntry Create(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new AuditEntry
        {
            Id = IdGenerator.NewId(record.OccurredAtUtc),
            TenantId = record.TenantId,
            UserId = record.UserId,
            DeviceId = record.DeviceId,
            OccurredAtUtc = record.OccurredAtUtc,
            Category = record.Category,
            Action = Limit(record.Action, ActionMaxLength),
            EntityType = LimitNullable(record.EntityType, EntityTypeMaxLength),
            EntityId = record.EntityId,
            OperationId = record.OperationId,
            CorrelationId = record.CorrelationId,
            Outcome = record.Outcome,
            BeforeRef = LimitNullable(record.BeforeRef, ReferenceMaxLength),
            AfterRef = LimitNullable(record.AfterRef, ReferenceMaxLength),
            AmountMinor = record.AmountMinor,
            AmountCurrency = record.AmountCurrency,
            Detail = LimitNullable(record.Detail, DetailMaxLength),
        };
    }

    private static string Limit(string value, int length) =>
        value.Length <= length ? value : value[..length];

    private static string? LimitNullable(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
