using Animora.Application.Security;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.Sqlite.Security;

/// <summary>
/// SQLite implementation of <see cref="ICapabilityStore"/> over the scoped <see cref="AnimoraLocalDbContext"/> (B04b).
/// Inside an active unit-of-work transaction it only flushes, so the caller's commit/rollback decides; standalone it
/// opens its own short transaction so replace/revoke stay atomic. Every query binds tenant + user + device.
/// </summary>
public sealed class CapabilityStore(AnimoraLocalDbContext db, ICapabilitySnapshotSigner signer) : ICapabilityStore
{
    public async Task<Result<StoredCapabilitySnapshot?>> GetAsync(string tenantId, string userId, string deviceId,
        CancellationToken ct = default)
    {
        if (!Valid(tenantId, userId, deviceId)) return Result<StoredCapabilitySnapshot?>.Ok(null);
        try
        {
            var row = await db.Set<DeviceCapabilitySnapshot>().AsNoTracking()
                .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId && e.DeviceId == deviceId, ct)
                .ConfigureAwait(false);
            return Result<StoredCapabilitySnapshot?>.Ok(row?.ToStored());
        }
        catch (SqliteException)
        {
            return Result<StoredCapabilitySnapshot?>.Fail(ErrorCodes.StoreUnavailable);
        }
    }

    public Task<Result> ReplaceAsync(StoredCapabilitySnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Valid(snapshot.TenantId, snapshot.UserId, snapshot.DeviceId) || string.IsNullOrWhiteSpace(snapshot.Id)
            || string.IsNullOrWhiteSpace(snapshot.SnapshotSignature))
            return Task.FromResult(Result.Fail(ErrorCodes.CapabilityNotGranted, "incomplete capability snapshot"));

        return InTransactionAsync(async () =>
        {
            var set = db.Set<DeviceCapabilitySnapshot>();
            var existing = await set.AsTracking()
                .Where(e => e.TenantId == snapshot.TenantId && e.UserId == snapshot.UserId && e.DeviceId == snapshot.DeviceId)
                .ToListAsync(ct).ConfigureAwait(false);
            if (existing.Count != 0)
            {
                set.RemoveRange(existing);
                // Delete first so the unique (tenant, user, device) index never sees two current rows.
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            set.Add(DeviceCapabilitySnapshot.From(snapshot));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, ct);
    }

    public Task<Result> RevokeAsync(string tenantId, string userId, string deviceId, DateTimeOffset revokedAtUtc,
        CancellationToken ct = default)
    {
        if (!Valid(tenantId, userId, deviceId)) return Task.FromResult(Result.Ok());

        return InTransactionAsync(async () =>
        {
            var set = db.Set<DeviceCapabilitySnapshot>();
            var row = await set.AsTracking()
                .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId && e.DeviceId == deviceId, ct)
                .ConfigureAwait(false);
            if (row is null) return Result.Ok();

            // Never launder a tampered row by re-signing it: verify first, discard on mismatch.
            var verified = signer.Verify(row.ToStored());
            if (verified.IsFailure)
            {
                if (verified.Error != ErrorCodes.CapabilitySnapshotTampered) return verified;
                set.Remove(row);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return Result.Fail(ErrorCodes.CapabilitySnapshotTampered, "tampered snapshot discarded");
            }

            if (row.RevokedAtUtc is not null) return Result.Ok(); // earliest revocation wins
            var revoked = revokedAtUtc.ToUniversalTime();
            var signature = signer.Sign(row.ToStored() with { RevokedAtUtc = revoked, SnapshotSignature = string.Empty });
            if (signature.IsFailure) return signature.ToResult();
            row.ApplyRevocation(revoked, signature.Value!);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }, ct, commitOnFailure: true);
    }

    /// <summary>
    /// Joins the caller's ambient transaction (flush only) or runs in its own short transaction. On failure the
    /// standalone transaction rolls back, except where <paramref name="commitOnFailure"/> allows a deliberate
    /// discard (tampered row deletion) to persist alongside the failure result.
    /// </summary>
    private async Task<Result> InTransactionAsync(Func<Task<Result>> work, CancellationToken ct, bool commitOnFailure = false)
    {
        IDbContextTransaction? own = null;
        var committed = false;
        try
        {
            if (db.Database.CurrentTransaction is null)
                own = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var outcome = await work().ConfigureAwait(false);
            if (own is not null && (outcome.IsSuccess || (commitOnFailure && outcome.Error == ErrorCodes.CapabilitySnapshotTampered)))
            {
                await own.CommitAsync(ct).ConfigureAwait(false);
                committed = true;
            }

            return outcome;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(ErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException)
        {
            return Result.Fail(ErrorCodes.StoreUnavailable);
        }
        catch (SqliteException)
        {
            return Result.Fail(ErrorCodes.StoreUnavailable);
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync().ConfigureAwait(false); // uncommitted ⇒ rolled back
                if (!committed) db.ChangeTracker.Clear(); // rolled back: drop state that no longer matches the store
            }
        }
    }

    private static bool Valid(string? tenantId, string? userId, string? deviceId) =>
        !string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(deviceId);
}
