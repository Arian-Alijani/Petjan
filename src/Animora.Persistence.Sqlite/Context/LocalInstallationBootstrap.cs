using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Context;

/// <summary>Creates the single <see cref="LocalInstallation"/> row on first run and caches it.</summary>
public sealed class LocalInstallationBootstrap(AnimoraLocalDbContext db, LocalInstallationState state, IClock clock)
{
    public async Task<Result<LocalInstallation>> EnsureAsync(string fingerprintHash, CancellationToken cancellationToken)
    {
        if (state.Installation is { } cached) return cached;
        if (string.IsNullOrWhiteSpace(fingerprintHash) || fingerprintHash.Length > LocalInstallation.FingerprintHashLength)
            return Result<LocalInstallation>.Fail(ErrorCodes.ValidationFailed, "Invalid machine fingerprint hash.");
        try
        {
            var existing = await db.Set<LocalInstallation>().AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            if (existing is null)
            {
                var now = clock.UtcNow;
                existing = LocalInstallation.Create(IdGenerator.NewId(now), LocalInstallationState.DefaultTenantId,
                    fingerprintHash, now);
                db.Add(existing);
                await db.SaveChangesAsync(cancellationToken);
                db.Entry(existing).State = EntityState.Detached;
            }
            state.Set(existing);
            return existing;
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or DbUpdateException or InvalidOperationException)
        {
            return Result<LocalInstallation>.Fail(ErrorCodes.StoreUnavailable, ex.Message);
        }
    }
}
