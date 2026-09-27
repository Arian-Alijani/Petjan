using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Diagnostics;

public sealed class UiDraftStateConfiguration : IEntityTypeConfiguration<UiDraftState>
{
    public void Configure(EntityTypeBuilder<UiDraftState> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ScreenKey).HasMaxLength(UiDraftState.ScreenKeyMaxLength).IsRequired();
        builder.Property(e => e.PayloadJson).HasMaxLength(UiDraftState.PayloadMaxLength).IsRequired();
        builder.HasIndex(e => new { e.ScreenKey, e.UserId })
            .IsUnique()
            .HasDatabaseName("IX_UiDraftStates_ScreenKey_UserId");
        builder.HasIndex(e => e.UpdatedAtUtc).HasDatabaseName("IX_UiDraftStates_UpdatedAtUtc");
    }
}
