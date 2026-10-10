using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class OperatorAuditConfiguration : IEntityTypeConfiguration<OperatorAudit>
{
    public void Configure(EntityTypeBuilder<OperatorAudit> b)
    {
        b.ToTable("OperatorAudit", "public"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(26); b.Property(x => x.OperatorId).HasMaxLength(26);
        b.Property(x => x.ActorKind).HasConversion<short>(); b.Property(x => x.Action).HasMaxLength(64).IsRequired();
        b.Property(x => x.TargetTenantId).HasMaxLength(26); b.Property(x => x.Outcome).HasMaxLength(16).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired(); b.Property(x => x.DetailJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.OccurredUtc).HasColumnType("timestamp(6) with time zone");
    }
}
