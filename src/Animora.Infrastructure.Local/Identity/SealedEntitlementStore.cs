using System.Data.Common;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Server.Licensing.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Infrastructure.Local.Identity;

/// <summary>
/// <see cref="ISealedEntitlementStore"/> over the encrypted local store (<c>SealedEntitlements</c>, one row per tenant). Every write is
/// merged <b>forward-only</b> with the existing row via <see cref="SealedEntitlementMerge.Merge"/> inside a single serialised
/// transaction: the high-water clock and licence sequence can never decrease, and a rolled-back or foreign-device token is rejected.
/// <para>
/// The licence token is server-signed and re-verified on every read by the entitlement gate, so tampering with the row cannot forge
/// entitlement; the file itself is the SQLCipher-encrypted store.
/// </para>
/// </summary>
public sealed class SealedEntitlementStore(IServiceScopeFactory scopes) : ISealedEntitlementStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<Result<SealedEntitlement?>> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var row = await db.Set<SealedEntitlementRow>().AsNoTracking()
                .OrderByDescending(r => r.WrittenUnixMs)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            return Result<SealedEntitlement?>.Ok(row is null ? null : ToRecord(row));
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or InvalidOperationException)
        {
            return Result<SealedEntitlement?>.Fail(ErrorCodes.StoreKeyUnavailable, "sealed_entitlement_read_failed");
        }
    }

    /// <inheritdoc />
    public async Task<Result> WriteAsync(SealedEntitlement entitlement, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entitlement);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            // The shared read-oriented context defaults to NoTracking; this row must be
            // tracked so an accepted forward merge is actually persisted by SaveChanges.
            var row = await db.Set<SealedEntitlementRow>().AsTracking()
                .FirstOrDefaultAsync(r => r.TenantId == entitlement.TenantId, ct).ConfigureAwait(false);

            var merged = SealedEntitlementMerge.Merge(row is null ? null : ToRecord(row), entitlement);
            if (merged.IsFailure) return merged.ToResult();

            var m = merged.Value!;
            if (row is null)
                db.Set<SealedEntitlementRow>().Add(SealedEntitlementRow.Create(m.TenantId, m.LicenseToken, m.HighWaterUnixMs,
                    m.HighestSequenceSeen, m.InstallationIdHash, m.WrittenUnixMs));
            else
                row.Replace(m.LicenseToken, m.HighWaterUnixMs, m.HighestSequenceSeen, m.InstallationIdHash, m.WrittenUnixMs);

            await db.SaveChangesAsync(ct).ConfigureAwait(false); // one SaveChanges = one atomic transaction
            return Result.Ok();
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or InvalidOperationException)
        {
            return Result.Fail(ErrorCodes.StoreKeyUnavailable, "sealed_entitlement_write_failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes every sealed entitlement (device revoked / sign-out wipe): offline authority is gone.</summary>
    public async Task<Result> ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            await db.Set<SealedEntitlementRow>().ExecuteDeleteAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or InvalidOperationException)
        {
            return Result.Fail(ErrorCodes.StoreKeyUnavailable, "sealed_entitlement_clear_failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SealedEntitlement ToRecord(SealedEntitlementRow r) =>
        new(r.LicenseToken, r.HighWaterUnixMs, r.HighestSequenceSeen, r.TenantId, r.InstallationIdHash, r.WrittenUnixMs);
}
