using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Activity;

/// <summary>
/// C10: <c>ActivityEvent</c> feed row storage + the three feed indexes the History query relies on.
/// Column lengths are assigned by <c>ModelConventions</c> (generic short-text) which runs after this
/// configuration; JSON-arg columns stay within that bound (feed args are small key/value pairs).
/// </summary>
public sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.OccurredUtc })
            .HasDatabaseName("IX_ActivityEvents_TenantId_OccurredUtc");
        builder.HasIndex(e => new { e.TenantId, e.Kind, e.OccurredUtc })
            .HasDatabaseName("IX_ActivityEvents_TenantId_Kind_OccurredUtc");
        builder.HasIndex(e => new { e.TenantId, e.EntityType, e.EntityId })
            .HasDatabaseName("IX_ActivityEvents_TenantId_EntityType_EntityId");
        // D23: the dashboard's per-day totals and newest-first preview (LocalDay = ? ORDER BY Id DESC).
        builder.HasIndex(e => new { e.TenantId, e.LocalDay, e.Id })
            .HasDatabaseName("IX_ActivityEvents_TenantId_LocalDay_Id");
    }
}

/// <summary>C10: <c>UserPreference</c> storage + the unique per-(tenant,user,key) index.</summary>
public sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.Key }).IsUnique()
            .HasDatabaseName("IX_UserPreferences_TenantId_UserId_Key");
    }
}
