using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Animora.Persistence.PostgreSql.KeyRing;

/// <summary>Design-time factory for the key-ring migrations. Never opens a connection.</summary>
public sealed class AnimoraKeyRingDbContextFactory : IDesignTimeDbContextFactory<AnimoraKeyRingDbContext>
{
    private const string DummyConnection = "Host=localhost;Database=animora_design;Username=design;Password=design";

    /// <inheritdoc />
    public AnimoraKeyRingDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable("ANIMORA_PG_DESIGN_CONNECTION") is { Length: > 0 } fromEnv
            ? fromEnv
            : DummyConnection;

        DbContextOptions<AnimoraKeyRingDbContext> options = new DbContextOptionsBuilder<AnimoraKeyRingDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable(AnimoraKeyRingDbContext.MigrationsHistoryTable))
            .Options;

        return new AnimoraKeyRingDbContext(options);
    }
}
