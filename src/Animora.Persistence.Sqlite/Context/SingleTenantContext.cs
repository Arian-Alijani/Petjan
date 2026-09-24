namespace Animora.Persistence.Sqlite.Context;

/// <summary>Tenant from the cached <c>LocalInstallation</c> row; the getter never touches the database.</summary>
public sealed class SingleTenantContext(LocalInstallationState state) : ILocalTenantContext
{
    public string TenantId => state.TenantId;
}
