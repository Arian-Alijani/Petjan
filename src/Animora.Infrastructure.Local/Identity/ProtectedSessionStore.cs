using System.Data.Common;
using Animora.Application.Security;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Security;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Infrastructure.Local.Identity;

/// <summary>
/// <see cref="ISessionStore"/> + <see cref="IOfflineCredentialMirrorStore"/> over <c>ClientSessions</c> (one row per tenant/user/device, local-only).
/// Bearer tokens are AES-GCM protected under an OS-keystore key (<see cref="TokenProtector"/>) before they touch SQLite.
/// <para>
/// <see cref="ClearAsync"/> (logout / session expiry) blanks the tokens but <b>keeps</b> the row's credential mirror, so a signed-out user can
/// still start cold offline; only <see cref="WipeAllAsync"/> (device revoked) removes the mirror too.
/// </para>
/// </summary>
public sealed class ProtectedSessionStore(IServiceScopeFactory scopes, ISecretStore secrets, IClock clock, ISessionTenantBinding? binding = null)
    : ISessionStore, IOfflineCredentialMirrorStore
{
    private readonly TokenProtector _protector = new(secrets);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<Result<ClientSession?>> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var row = await db.Set<ClientSessionRow>().AsNoTracking()
                .Where(r => r.AccessToken != string.Empty)
                .OrderByDescending(r => r.UpdatedUtc)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (row is null) return Result<ClientSession?>.Ok(null);
            if (binding is not null && !binding.Matches(row.TenantId))
                return Result<ClientSession?>.Fail(ErrorCodes.Unauthorized, "store_tenant_mismatch");

            var access = _protector.Unprotect(row.AccessToken);
            var refresh = _protector.Unprotect(row.RefreshToken);
            if (access.IsFailure || refresh.IsFailure) return Result<ClientSession?>.Ok(null); // unreadable ciphertext == no session
            return Result<ClientSession?>.Ok(new ClientSession(access.Value!, row.AccessExpiresUtc, refresh.Value!, row.UserId, row.TenantId,
                row.DeviceId, row.Role, row.SecurityStamp));
        }
        catch (Exception ex) when (IsStoreFault(ex))
        {
            return Result<ClientSession?>.Fail(ErrorCodes.StoreKeyUnavailable, "session_read_failed");
        }
    }

    /// <inheritdoc />
    public async Task<Result> WriteAsync(ClientSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (binding is not null)
        {
            var bound = binding.Bind(session.TenantId);
            if (bound.IsFailure) return bound;
        }
        var access = _protector.Protect(session.AccessToken);
        var refresh = _protector.Protect(session.RefreshToken);
        if (access.IsFailure) return access.ToResult();
        if (refresh.IsFailure) return refresh.ToResult();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var now = clock.UtcNow;
            var row = await db.Set<ClientSessionRow>().AsTracking().FirstOrDefaultAsync(
                r => r.TenantId == session.TenantId && r.UserId == session.UserId && r.DeviceId == session.DeviceId, ct).ConfigureAwait(false);
            if (row is null)
                db.Set<ClientSessionRow>().Add(ClientSessionRow.Create(session.TenantId, session.UserId, session.DeviceId, access.Value!,
                    session.AccessExpiresUtc, refresh.Value!, session.Role, session.SecurityStamp, offlineCredentialVerifier: null, now));
            else
                row.Replace(access.Value!, session.AccessExpiresUtc, refresh.Value!, session.Role, session.SecurityStamp, offlineCredentialVerifier: null, now);
            // A login/rotation leaves exactly one active row; never select an older user's token on a tied timestamp.
            var oldSessions = await db.Set<ClientSessionRow>().AsTracking()
                .Where(r => r.AccessToken != string.Empty && (r.TenantId != session.TenantId
                    || r.UserId != session.UserId || r.DeviceId != session.DeviceId)).ToListAsync(ct).ConfigureAwait(false);
            foreach (var old in oldSessions)
                old.Replace(string.Empty, DateTimeOffset.UnixEpoch, string.Empty, old.Role, old.SecurityStamp, null, now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (IsStoreFault(ex))
        {
            return Result.Fail(ErrorCodes.StoreKeyUnavailable, "session_write_failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result> ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var now = clock.UtcNow;
            var rows = await db.Set<ClientSessionRow>().AsTracking()
                .Where(r => r.AccessToken != string.Empty).ToListAsync(ct).ConfigureAwait(false);
            foreach (var row in rows)
                row.Replace(string.Empty, DateTimeOffset.UnixEpoch, string.Empty, row.Role, row.SecurityStamp, offlineCredentialVerifier: null, now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (IsStoreFault(ex))
        {
            return Result.Fail(ErrorCodes.StoreKeyUnavailable, "session_clear_failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    async Task<Result<OfflineCredentialMirror?>> IOfflineCredentialMirrorStore.ReadAsync(CancellationToken ct)
    {
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var row = await db.Set<ClientSessionRow>().AsNoTracking()
                .Where(r => r.OfflineCredentialVerifier != null)
                .OrderByDescending(r => r.UpdatedUtc)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (row is not null && binding is not null && !binding.Matches(row.TenantId))
                return Result<OfflineCredentialMirror?>.Fail(ErrorCodes.Unauthorized, "store_tenant_mismatch");
            return Result<OfflineCredentialMirror?>.Ok(row is null
                ? null
                : new OfflineCredentialMirror(row.TenantId, row.UserId, row.DeviceId, row.OfflineCredentialVerifier!, row.SecurityStamp, row.Role));
        }
        catch (Exception ex) when (IsStoreFault(ex))
        {
            return Result<OfflineCredentialMirror?>.Fail(ErrorCodes.StoreKeyUnavailable, "mirror_read_failed");
        }
    }

    /// <inheritdoc />
    public async Task<Result> WriteVerifierAsync(string tenantId, string userId, string deviceId, string verifier, CancellationToken ct = default)
    {
        if (binding is not null && !binding.Matches(tenantId)) return Result.Fail(ErrorCodes.Unauthorized, "store_tenant_mismatch");
        if (string.IsNullOrEmpty(verifier) || verifier.Length > ClientSessionRow.VerifierMaxLength)
            return Result.Fail(ErrorCodes.ValidationFailed, "verifier_invalid");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var row = await db.Set<ClientSessionRow>().AsTracking().FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.UserId == userId && r.DeviceId == deviceId, ct).ConfigureAwait(false);
            if (row is null) return Result.Fail(ErrorCodes.OfflineSessionUnavailable, "no_session_row");
            // Only one mirror may exist: another user's verifier must not outlive this login.
            var others = await db.Set<ClientSessionRow>().Where(r => r.Id != row.Id && r.OfflineCredentialVerifier != null).ToListAsync(ct).ConfigureAwait(false);
            db.Set<ClientSessionRow>().RemoveRange(others);
            row.Replace(row.AccessToken, row.AccessExpiresUtc, row.RefreshToken, row.Role, row.SecurityStamp, verifier, clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (IsStoreFault(ex))
        {
            return Result.Fail(ErrorCodes.StoreKeyUnavailable, "mirror_write_failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deletes every session row, including the credential mirror (device revoked).</summary>
    public async Task<Result> WipeAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var di = scopes.CreateScope();
            var db = di.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            await db.Set<ClientSessionRow>().ExecuteDeleteAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (IsStoreFault(ex))
        {
            return Result.Fail(ErrorCodes.StoreKeyUnavailable, "session_wipe_failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsStoreFault(Exception ex) => ex is DbException or DbUpdateException or InvalidOperationException;
}
