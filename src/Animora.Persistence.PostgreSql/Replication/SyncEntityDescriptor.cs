namespace Animora.Persistence.PostgreSql.Replication;

/// <summary>Physical child mapping; RootIdColumn is an identifier, never executable SQL.</summary>
public sealed record SyncChildDescriptor(string Table, string RootIdColumn, bool HasTenantId);

public sealed record SyncEntityDescriptor(string Name, Type Entity, string Table,
    IReadOnlyList<SyncChildDescriptor> Children)
{
    public IReadOnlyList<string> ChildTables => Children.Select(c => c.Table).ToArray();
}
