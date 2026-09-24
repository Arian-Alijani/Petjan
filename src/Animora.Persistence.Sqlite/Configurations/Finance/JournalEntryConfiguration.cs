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
        builder.HasIndex(e => new { e.TenantId, e.EntryNumber }).HasDatabaseName("IX_JournalEntries_TenantId_EntryNumber").IsUnique();
        OwnedRows.Map(builder, e => e.Lines);
    }
}

/// <summary>Child rows of <see cref="JournalLine"/>'s aggregate, table <c>JournalLines</c>.</summary>
public sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        OwnedRows.Row(builder, "JournalLines", e => e.LineId);
        OwnedRows.Money(builder, e => e.Amount);
        builder.HasIndex(e => e.AccountCode).HasDatabaseName("IX_JournalLines_AccountCode");
    }
}
