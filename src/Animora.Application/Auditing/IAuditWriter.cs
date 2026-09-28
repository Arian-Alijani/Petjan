using Animora.Domain.Results;

namespace Animora.Application.Auditing;

public enum AuditCategory
{
    Security,
    Financial,
    Clinical,
    Configuration,
    Integrity,
    Maintenance,
    Inventory,
}

public enum AuditOutcome
{
    Success,
    Denied,
    Failed,
}

/// <summary>Machine-readable audit data. Before/after values are references, never payload snapshots.</summary>
public sealed record AuditRecord(
    string TenantId,
    string UserId,
    string DeviceId,
    DateTimeOffset OccurredAtUtc,
    AuditCategory Category,
    string Action,
    string? EntityType,
    string? EntityId,
    string OperationId,
    string CorrelationId,
    AuditOutcome Outcome,
    string? BeforeRef,
    string? AfterRef,
    long? AmountMinor,
    string? AmountCurrency,
    string? Detail);

public interface IAuditWriter
{
    Task<Result> WriteAsync(AuditRecord record, CancellationToken ct = default);

    /// <summary>Adds a record to the current unit of work without committing it independently.</summary>
    void Enlist(AuditRecord record);
}
