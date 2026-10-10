using Animora.Persistence.PostgreSql.Configurations;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Outbox;

/// <summary>Narrow cross-tenant relay context. Never used to migrate, never maps business rows.</summary>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> Messages => Set<OutboxMessage>();
    public DbSet<ConsumedMessage> Consumed => Set<ConsumedMessage>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfiguration(new OutboxMessageConfiguration());
        b.ApplyConfiguration(new OutboxConsumedMessageConfiguration());
    }
}
