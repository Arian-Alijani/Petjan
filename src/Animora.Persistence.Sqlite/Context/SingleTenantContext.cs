namespace Animora.Persistence.Sqlite.Context;

public sealed class SingleTenantContext : ILocalTenantContext
{
    public string TenantId => "T-LOCAL-DEFAULT";
}
