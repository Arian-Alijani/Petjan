using Animora.Persistence.PostgreSql.Configurations;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Provisioning;

/// <summary>Intake-only global context. Never migrates or maps clinical/tenant rows.</summary>
public sealed class ProvisioningDbContext(DbContextOptions<ProvisioningDbContext> options) : DbContext(options)
{
    public DbSet<WebsiteClient> Clients => Set<WebsiteClient>();
    public DbSet<ProvisioningIntent> Intents => Set<ProvisioningIntent>();
    public DbSet<OperatorAudit> Audit => Set<OperatorAudit>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfiguration(new WebsiteClientConfiguration()); b.ApplyConfiguration(new ProvisioningIntentConfiguration());
        b.ApplyConfiguration(new OperatorAuditConfiguration());
    }
}
