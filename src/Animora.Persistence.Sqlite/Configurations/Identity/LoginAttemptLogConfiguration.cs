using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Identity;

// [LocalOnly] append-only row (AppendOnlyGuard): not tenant-filtered.
public sealed class LoginAttemptLogConfiguration : IEntityTypeConfiguration<LoginAttemptLog>
{
    public void Configure(EntityTypeBuilder<LoginAttemptLog> builder)
    {
        builder.HasKey(e => e.Id); // column types/lengths come from ModelConventions.
        builder.HasIndex(e => e.OccurredAtUtc).HasDatabaseName("IX_LoginAttemptLogs_OccurredAtUtc");
    }
}
