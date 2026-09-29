using Animora.Application.UseCases.Profile;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Profile;

/// <summary>
/// C10: stages preference rows in the caller's tracked context and commits them through the command's unit of
/// work. One row per (tenant, user, key) — an existing row is updated in place (last-writer-wins), a new key is
/// inserted, so a profile/settings save is idempotent and survives a restart.
/// </summary>
public sealed class ProfilePreferenceWriter(AnimoraLocalDbContext db) : IProfilePreferenceWriter
{
    public async Task UpsertAsync(string tenantId, string userId, string key, string valueJson, CancellationToken ct)
    {
        var existing = await db.Set<UserPreference>().AsTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.Key == key, ct);
        if (existing is null)
        {
            db.Set<UserPreference>().Add(UserPreference.Create(tenantId, userId, key, valueJson));
            return;
        }

        existing.SetValue(valueJson);
    }
}
