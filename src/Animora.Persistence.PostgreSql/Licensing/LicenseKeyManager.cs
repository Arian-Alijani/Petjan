using System.Security.Cryptography;
using Animora.Contracts.Wire.Licensing;
using Animora.Domain.Subscriptions;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Security;
using Animora.Server.Licensing.Issuance;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace Animora.Persistence.PostgreSql.Licensing;

public sealed class LicenseKeyManager(LicenseStore db, IDataProtectionProvider protection, IClock clock, IOptions<LicensingOptions> options)
    : ILicenseSignerProvider
{
    public const string ProtectorPurpose = "Animora.Server.SigningKeys.v1";
    public static int RetentionDays(LicensingOptions o) => Math.Max(120, o.MaxIssueTtlDays + o.OfflineGraceDays + o.ReadOnlyWindowDays);

    public async Task<ILicenseSigner> GetAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var key = await db.Keys.AsNoTracking().Where(k => k.Purpose == SigningKey.Purposes.License && k.RetiredUtc == null
                && (k.NotAfterUtc == null || k.NotAfterUtc > now))
            .OrderByDescending(k => k.CreatedUtc).ThenByDescending(k => k.KeyId).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Licence signing key is not initialized.");
        var bytes = protection.CreateProtector(ProtectorPurpose).Unprotect(key.ProtectedPrivateKey);
        try { return new EcdsaLicenseSigner(key.KeyId, bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async Task<string> EnsureAsync(CancellationToken ct = default)
    {
        var result = await ChangeAsync(false, null, new ActorRef(ActorKind.System), ct).ConfigureAwait(false);
        return result.Value ?? throw new InvalidOperationException("Licence key initialization failed.");
    }
    public Task<Result<string>> RotateAsync(string expectedKeyId, ActorRef actor, CancellationToken ct = default) =>
        ChangeAsync(true, expectedKeyId, actor, ct);

    private async Task<Result<string>> ChangeAsync(bool rotate, string? expectedKeyId, ActorRef actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(160016)", ct).ConfigureAwait(false);
        var active = await db.Keys.Where(k => k.Purpose == SigningKey.Purposes.License && k.RetiredUtc == null)
            .OrderByDescending(k => k.CreatedUtc).ToListAsync(ct).ConfigureAwait(false);
        if (!rotate && active.FirstOrDefault(k => k.IsUsable(clock.UtcNow)) is { } existing) return Result<string>.Ok(existing.KeyId);
        if (rotate && (string.IsNullOrWhiteSpace(expectedKeyId) || active.FirstOrDefault()?.KeyId != expectedKeyId.Trim('"')))
            return Result<string>.Fail(ErrorCodes.ConcurrencyConflict);
        var horizon = clock.UtcNow.AddDays(RetentionDays(options.Value));
        foreach (var old in active) old.RetireLicense(clock, horizon);
        var (publicKey, privateKey) = EcdsaLicenseSigner.GenerateKeyPair();
        byte[] protectedKey;
        try { protectedKey = protection.CreateProtector(ProtectorPurpose).Protect(privateKey); }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
        var created = SigningKey.Create(SigningKey.Purposes.License, EcdsaLicenseSigner.Algorithm, publicKey, protectedKey, clock);
        db.Keys.Add(created);
        db.Audit.Add(OperatorAudit.Create(clock.UtcNow, actor, rotate ? "license.key_rotated" : "license.key_bootstrapped", null, created.KeyId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result<string>.Ok(created.KeyId);
    }

    public async Task<LicensePublicKeySet> PublicAsync(CancellationToken ct = default)
    {
        var rows = await db.Keys.AsNoTracking().Where(x => x.Purpose == SigningKey.Purposes.License)
            .OrderBy(x => x.CreatedUtc).Select(x => new { x.KeyId, x.Algorithm, x.PublicKeyDer, x.CreatedUtc, x.NotAfterUtc })
            .ToListAsync(ct).ConfigureAwait(false);
        return new(rows.Select(x => new LicensePublicKey(x.KeyId, x.Algorithm, Convert.ToBase64String(x.PublicKeyDer),
            x.CreatedUtc, x.NotAfterUtc)).ToArray(), clock.UtcNow);
    }
}
