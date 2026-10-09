namespace Animora.Persistence.PostgreSql.Commands;

/// <summary>
/// Records one aggregate id a command created on the server (S06, D-S06-3). The primary id is slot 0; every further
/// created id gets the next slot. Server ids are allocated by the server — never fabricated to match the client.
/// </summary>
public sealed class CommandIdMapEntry
{
    private CommandIdMapEntry()
    {
    }

    /// <summary>Owning tenant ULID.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>The command's operation ULID.</summary>
    public string OperationId { get; private set; } = string.Empty;

    /// <summary>Zero-based slot; slot 0 is the primary entity.</summary>
    public short Slot { get; private set; }

    /// <summary>Aggregate/entity type name (≤ 32 chars).</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>The server-allocated ULID.</summary>
    public string ServerEntityId { get; private set; } = string.Empty;

    /// <summary>Creates an entry.</summary>
    public static CommandIdMapEntry Create(string tenantId, string operationId, short slot, string entityType, string serverEntityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverEntityId);

        return new CommandIdMapEntry
        {
            TenantId = tenantId,
            OperationId = operationId,
            Slot = slot,
            EntityType = entityType,
            ServerEntityId = serverEntityId,
        };
    }
}
