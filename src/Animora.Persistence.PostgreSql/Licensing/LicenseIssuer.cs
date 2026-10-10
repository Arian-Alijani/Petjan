using System.Security.Cryptography;
using System.Text.Json;
using Animora.Contracts.Wire.Licensing;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Server.Licensing.Issuance;
using Animora.Server.Licensing.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace Animora.Persistence.PostgreSql.Licensing;

public enum IssueKind { Activate, Renew }
public sealed class LicenseIssuer(LicenseStore db, ILicenseSignerProvider signers, IClock clock, IOptions<LicensingOptions> options)
{
    public static DateTimeOffset Expiry(DateTimeOffset now, DateTimeOffset grantEnd, int ttlDays) =>
        grantEnd < now.AddDays(ttlDays) ? grantEnd : now.AddDays(ttlDays);

    public async Task<Result<LicenseResponse>> IssueAsync(IssueKind kind, string tenantId, string userId,
        string deviceId, LicenseRequest request, string correlationId, CancellationToken ct = default)
    {
        if (!LicensePayload.IsInstallationHash(request.InstallationIdHash) || request.CurrentSequence is < 0)
            return Result<LicenseResponse>.Fail(ErrorCodes.ValidationFailed);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Grant row serializes all activations for one tenant; count and supersede cannot race.
        var grant = await db.Grants.FromSqlInterpolated($"SELECT * FROM public.\"LicenseGrants\" WHERE \"TenantId\"={tenantId} FOR UPDATE")
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var now = clock.UtcNow.ToUniversalTime();
        if (grant is null) return Result<LicenseResponse>.Fail(ErrorCodes.LicenseNotGranted);
        if (grant.Status == LicenseGrantStatus.Revoked) return Result<LicenseResponse>.Fail(ErrorCodes.LicenseRevoked);
        if (grant.Status != LicenseGrantStatus.Active || now > grant.ValidUntilUtc)
            return Result<LicenseResponse>.Fail(ErrorCodes.EntitlementRequired);
        // did comes from the authenticated principal; always reread its stored ownership/status/hash.
        var device = await db.Devices.FromSqlInterpolated($"SELECT * FROM public.\"Devices\" WHERE \"TenantId\"={tenantId} AND \"Id\"={deviceId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (device is null || device.Deleted || device.UserId != userId || device.Status != DeviceStatus.Active)
            return Result<LicenseResponse>.Fail(ErrorCodes.DeviceRevoked);
        if (!string.Equals(device.InstallationIdHash, request.InstallationIdHash, StringComparison.Ordinal))
        {
            db.SecurityAudit.Add(Audit("license.device_mismatch", "denied", null));
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return Result<LicenseResponse>.Fail(ErrorCodes.LicenseDeviceMismatch);
        }
        var previous = await db.Licenses.Where(x => x.TenantId == tenantId && x.DeviceId == deviceId)
            .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (previous?.Status == LicenseRecordStatus.Revoked) return Result<LicenseResponse>.Fail(ErrorCodes.LicenseRevoked);
        if (request.CurrentSequence > (previous?.Sequence ?? 0)) return Result<LicenseResponse>.Fail(ErrorCodes.LicenseInvalid);
        if (previous?.Status != LicenseRecordStatus.Active && await db.Licenses.CountAsync(
            x => x.TenantId == tenantId && x.Status == LicenseRecordStatus.Active, ct).ConfigureAwait(false) >= grant.MaxDevices)
            return Result<LicenseResponse>.Fail(ErrorCodes.LicenseDeviceLimitReached);
        var tenantCode = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantId && !x.Deleted)
            .Select(x => x.Code).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (tenantCode is null) return Result<LicenseResponse>.Fail(ErrorCodes.LicenseNotGranted);
        var id = IdGenerator.NewId(now); var sequence = checked((previous?.Sequence ?? 0) + 1);
        var expires = Expiry(now, grant.ValidUntilUtc, options.Value.MaxIssueTtlDays);
        var grace = expires.AddDays(options.Value.OfflineGraceDays);
        var payload = new LicensePayload(1, id, tenantId, tenantCode, grant.SubscriptionId, grant.PlanCode,
            grant.SeatLimit, grant.MaxDevices, device.InstallationIdHash, deviceId, sequence,
            now, now.AddMinutes(-5), expires, grace, []);
        var bytes = LicenseCanonicalPayload.Serialize(payload);
        using var signer = await signers.GetAsync(ct).ConfigureAwait(false);
        var signed = signer.Sign(bytes);
        if (signed.IsFailure || signed.Value is not { } signature) return Result<LicenseResponse>.Fail(signed.Error);
        // Flush supersede first: the partial unique index is immediate, not deferred.
        if (previous?.Status == LicenseRecordStatus.Active)
        {
            previous.Status = LicenseRecordStatus.Superseded;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        db.Licenses.Add(new LicenseRecord { TenantId = tenantId, Id = id, DeviceId = deviceId,
            InstallationIdHash = device.InstallationIdHash, Sequence = sequence, IssuedUtc = now,
            ExpiresUtc = expires, OfflineGraceUntilUtc = grace, KeyId = signer.KeyId,
            PayloadSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), Status = LicenseRecordStatus.Active });
        db.SecurityAudit.Add(Audit("license.issued", "success", JsonSerializer.Serialize(new { licenseId = id, sequence, keyId = signer.KeyId, kind = kind.ToString() })));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result<LicenseResponse>.Ok(new(Base64Url.Encode(bytes) + "." + Base64Url.Encode(signature) + "." + signer.KeyId,
            expires, grace, sequence, now));

        SecurityAuditEntry Audit(string action, string outcome, string? detail) => SecurityAuditEntry.Create(
            IdGenerator.NewId(now), tenantId, now, "licensing", action, outcome, correlationId, userId, deviceId, detail);
    }
}
