using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// Storage seam of <see cref="RefreshTokenService"/>. Operates on the tenant bound in the current scope.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Finds a row by the SHA-256 hex of its wire token.</summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Stages a new row (committed by the next unit-of-work save).</summary>
    void Add(RefreshToken row);

    /// <summary>
    /// Atomically rotates <paramref name="oldId"/> into <paramref name="successor"/>: the old row is claimed
    /// by one conditional update (so concurrent callers cannot both win) and the successor is inserted in the
    /// same transaction. Returns <see langword="false"/> — changing nothing — when the old row was already
    /// rotated or revoked.
    /// </summary>
    Task<bool> TryRotateAsync(string oldId, RefreshToken successor, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>Revokes every still-open row of a rotation family; returns the count.</summary>
    Task<int> RevokeFamilyAsync(string familyId, DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>Revokes every still-open row of a user on one device; returns the count.</summary>
    Task<int> RevokeDeviceTokensAsync(string userId, string deviceId, DateTimeOffset nowUtc, CancellationToken ct = default);
}

/// <summary>EF Core implementation of <see cref="IRefreshTokenStore"/>.</summary>
public sealed class EfRefreshTokenStore(AnimoraServerDbContext context) : IRefreshTokenStore
{
    /// <inheritdoc />
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) =>
        context.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    /// <inheritdoc />
    public void Add(RefreshToken row)
    {
        ArgumentNullException.ThrowIfNull(row);
        context.RefreshTokens.Add(row);
    }

    /// <inheritdoc />
    public async Task<bool> TryRotateAsync(string oldId, RefreshToken successor, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(successor);

        await using var tx = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        int claimed = await context.RefreshTokens
            .Where(t => t.Id == oldId && t.RotatedToId == null && t.RevokedUtc == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RotatedToId, successor.Id).SetProperty(t => t.RevokedUtc, nowUtc),
                ct)
            .ConfigureAwait(false);

        if (claimed != 1)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        context.RefreshTokens.Add(successor);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task<int> RevokeFamilyAsync(string familyId, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        context.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, nowUtc), ct);

    /// <inheritdoc />
    public Task<int> RevokeDeviceTokensAsync(string userId, string deviceId, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        context.RefreshTokens
            .Where(t => t.UserId == userId && t.DeviceId == deviceId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, nowUtc), ct);
}
