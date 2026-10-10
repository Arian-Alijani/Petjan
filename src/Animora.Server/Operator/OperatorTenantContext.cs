using Animora.Persistence.PostgreSql.Tenancy;

namespace Animora.Server.Operator;

internal sealed class OperatorTenantContext : ITenantContext
{
    public string? TenantId => null;
    public bool IsOperator { get; private set; }
    public void Authenticate() => IsOperator = true;
}
