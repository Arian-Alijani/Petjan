using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Tests.Persistence;

internal sealed class FixedTenantContext(string? tenantId, bool isOperator = false) : ITenantContext
{
    public string? TenantId { get; } = tenantId;

    public bool IsOperator { get; } = isOperator;
}

internal static class TestSupport
{
    // Dummy: the model is inspected / change tracking is driven, but a connection is never opened.
    public const string DummyConnection = "Host=localhost;Database=never_opened;Username=x;Password=x";

    public static AnimoraServerDbContext CreateContext(ITenantContext? tenant = null, string? connection = null)
    {
        var options = new DbContextOptionsBuilder<AnimoraServerDbContext>()
            .UseNpgsql(connection ?? DummyConnection)
            .Options;
        return new AnimoraServerDbContext(options, tenant ?? new NullTenantContext());
    }
}
