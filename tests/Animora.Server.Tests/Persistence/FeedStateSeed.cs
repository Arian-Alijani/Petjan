using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Tests.Persistence;

/// <summary>
/// The change-journal trigger requires a <c>TenantFeedState</c> row per tenant (production creates it in
/// <c>TenantProvisioner</c>). Intake round-trip harnesses invent tenant ids without provisioning, so they
/// migrate and then seed the row through this single shared helper.
/// </summary>
internal static class FeedStateSeed
{
    public static async Task MigrateAndSeedAsync(this AnimoraServerDbContext db, params string[] tenantIds)
    {
        await db.Database.MigrateAsync();
        foreach (var tenantId in tenantIds) await EnsureAsync(db, tenantId);
    }

    public static async Task EnsureAsync(AnimoraServerDbContext db, string tenantId) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO public.\"TenantFeedState\" (\"TenantId\") VALUES ({tenantId}) ON CONFLICT DO NOTHING");
}
