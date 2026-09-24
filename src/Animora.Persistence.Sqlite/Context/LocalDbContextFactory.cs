using Animora.Domain.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Animora.Persistence.Sqlite.Context;

public sealed class LocalDbContextFactory : IDesignTimeDbContextFactory<AnimoraLocalDbContext>
{
    public AnimoraLocalDbContext CreateDbContext(string[] args)
    {
        var path = Path.Combine(Path.GetTempPath(), "animora-design-time.db");
        var options = new DbContextOptionsBuilder<AnimoraLocalDbContext>()
            .UseSqlite($"Data Source={path}").Options;
        return new AnimoraLocalDbContext(options, new SingleTenantContext(new LocalInstallationState()), new DesignTimeClock());
    }

    private sealed class DesignTimeClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }
}
