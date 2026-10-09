using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Animora.Persistence.PostgreSql.Context;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c>. Never opens a connection: migration
/// scaffolding only needs the model. Override the dummy string with
/// <c>ANIMORA_PG_DESIGN_CONNECTION</c> when generating idempotent scripts against a server.
/// </summary>
public sealed class AnimoraServerDbContextFactory : IDesignTimeDbContextFactory<AnimoraServerDbContext>
{
    /// <summary>Environment variable that overrides the design-time connection string.</summary>
    public const string ConnectionEnvVar = "ANIMORA_PG_DESIGN_CONNECTION";

    private const string DummyConnection = "Host=localhost;Database=animora_design;Username=design;Password=design";

    /// <inheritdoc />
    public AnimoraServerDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable(ConnectionEnvVar) is { Length: > 0 } fromEnv
            ? fromEnv
            : DummyConnection;

        DbContextOptions<AnimoraServerDbContext> options = new DbContextOptionsBuilder<AnimoraServerDbContext>()
            .UseNpgsql(connection)
            .Options;

        return new AnimoraServerDbContext(options, new NullTenantContext());
    }
}
