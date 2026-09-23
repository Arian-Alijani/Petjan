using Animora.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.Sqlite.Conventions;

namespace Animora.Persistence.Sqlite.Configurations.Finance;

public sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.PeriodId, e.PostedUtc }).HasDatabaseName("IX_JournalEntries_TenantId_PeriodId_PostedUtc");
        builder.HasIndex(e => new { e.TenantId, e.SourceEntityType, e.SourceEntityId }).HasDatabaseName("IX_JournalEntries_TenantId_SourceEntityType_SourceEntityId");
        builder.HasIndex(e => new { e.TenantId, e.EntryNumber }).HasDatabaseName("IX_JournalEntries_TenantId_EntryNumber");
        OwnedRows.Map(builder, e => e.Lines, "_lines", "JournalLines", "LineId");
    }
}
