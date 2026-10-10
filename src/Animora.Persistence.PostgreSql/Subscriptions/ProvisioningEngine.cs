using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Animora.Application.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.PostgreSql.Subscriptions;

public sealed record ProvisioningRequest(string IdempotencyKey, string TenantCode, string DisplayName,
    string? LegalName, string? Timezone, string OwnerUserName, string OwnerDisplayName,
    string? OwnerInitialPassword, string PlanCode, int? SeatLimitOverride, bool StartTrial);
public sealed record ProvisioningResult(string TenantId, string TenantCode, string SubscriptionId,
    string OwnerUserId, string? OwnerTemporaryPassword, bool WasReplay);

/// <summary>The one atomic tenant/owner/feed/subscription provisioning path.</summary>
public sealed class ProvisioningEngine(AnimoraOperatorDbContext context,
    IPasswordHasher<ServerIdentityUser> passwordHasher, ILicenseIssuanceScheduler scheduler, IClock clock,
    IOptions<SubscriptionOptions>? options = null)
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
        { "admin", "operator", "api", "sync", "auth", "files", "health" };

    public static Result Validate(ProvisioningRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.IdempotencyKey) || r.IdempotencyKey.Length > 128 ||
            string.IsNullOrWhiteSpace(r.TenantCode) || !Regex.IsMatch(r.TenantCode, "^[a-z0-9-]{3,32}$",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || Reserved.Contains(r.TenantCode) ||
            string.IsNullOrWhiteSpace(r.DisplayName) || r.DisplayName.Length > 200 || r.LegalName?.Length > 200 ||
            string.IsNullOrWhiteSpace(r.OwnerUserName) || r.OwnerUserName.Length > 64 ||
            string.IsNullOrWhiteSpace(r.OwnerDisplayName) || r.OwnerDisplayName.Length > 200 ||
            string.IsNullOrWhiteSpace(r.PlanCode) || r.SeatLimitOverride is < 1)
            return Result.Fail(ErrorCodes.ValidationFailed);
        if (r.Timezone is { } zone)
        {
            if (zone.Length > 64) return Result.Fail(ErrorCodes.ValidationFailed);
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone); }
            catch (TimeZoneNotFoundException) { return Result.Fail(ErrorCodes.ValidationFailed, "unknown_timezone"); }
            catch (InvalidTimeZoneException) { return Result.Fail(ErrorCodes.ValidationFailed, "invalid_timezone"); }
        }
        return Result.Ok();
    }

    public static string Fingerprint(ProvisioningRequest r) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(CommandFingerprint.CanonicalJson(new
        {
            r.TenantCode, r.DisplayName, r.LegalName, timezone = r.Timezone ?? Tenant.DefaultTimezone,
            r.OwnerUserName, r.OwnerDisplayName, r.PlanCode, r.SeatLimitOverride, r.StartTrial,
        }))));

    public async Task<Result<ProvisioningResult>> ProvisionAsync(ProvisioningRequest r, ActorRef actor, CancellationToken ct = default)
    {
        var validation = Validate(r);
        if (validation.IsFailure) return Result<ProvisioningResult>.Fail(validation.Error, validation.Detail);
        var fingerprint = Fingerprint(r);
        await using var transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Serialize only contenders for the same key/code. The permanent ledger PK remains the arbiter.
        // Sorted lock acquisition prevents deadlock when keys and codes overlap across callers.
        foreach (var address in new[] { "provision.key:" + r.IdempotencyKey, "provision.code:" + r.TenantCode }.Order(StringComparer.Ordinal))
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({address}, 0))", ct).ConfigureAwait(false);
        var ledger = await context.ProvisioningLedger.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == r.IdempotencyKey, ct).ConfigureAwait(false);
        if (ledger is not null)
        {
            if (ledger.RequestFingerprint != fingerprint) return Result<ProvisioningResult>.Fail(ErrorCodes.ProvisioningKeyConflict);
            var tenant = await context.Tenants.AsNoTracking().Where(x => x.Id == ledger.TenantId)
                .Select(x => new { x.Code }).SingleAsync(ct).ConfigureAwait(false);
            var ownerId = await context.StaffMemberships.AsNoTracking()
                .Where(x => x.TenantId == ledger.TenantId && x.Role == StaffRole.Manager && !x.Deleted)
                .OrderBy(x => x.JoinedUtc).Select(x => x.UserId).FirstAsync(ct).ConfigureAwait(false);
            context.Audit.Add(OperatorAudit.Create(clock.UtcNow, actor, "tenant.provision_replayed", ledger.TenantId, ledger.SubscriptionId));
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return Result<ProvisioningResult>.Ok(new(ledger.TenantId, tenant.Code ?? r.TenantCode,
                ledger.SubscriptionId, ownerId, null, true));
        }
        // Seed defaults are runtime configuration, not secrets or frozen migration-time configuration.
        // Initialize only before the standard plan has ever been subscribed; existing commercial terms stay untouched.
        if (options is not null && options.Value.StandardMonthlyAmountMinor > 0)
        {
            var amount = options.Value.StandardMonthlyAmountMinor;
            var initialized = await context.Plans.Where(p => p.Code == "standard" && p.MonthlyAmountMinor == 0 &&
                !context.Subscriptions.Any(s => s.PlanId == p.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.MonthlyAmountMinor, amount), ct).ConfigureAwait(false);
            if (initialized != 0) context.Audit.Add(OperatorAudit.Create(clock.UtcNow, actor, "plan.defaults_initialized", null,
                r.IdempotencyKey.Length > 64 ? r.IdempotencyKey[..64] : r.IdempotencyKey));
        }
        var plan = await context.Plans.AsNoTracking().SingleOrDefaultAsync(x => x.Code == r.PlanCode && x.IsActive, ct).ConfigureAwait(false);
        if (plan is null) return Result<ProvisioningResult>.Fail(ErrorCodes.PlanNotFound);
        if (await context.Tenants.AsNoTracking().AnyAsync(x => x.Code == r.TenantCode, ct).ConfigureAwait(false))
            return Result<ProvisioningResult>.Fail(ErrorCodes.TenantCodeTaken);
        var password = r.OwnerInitialPassword ?? GenerateTemporaryPassword();
        var auth = new AuthTenantContext();
        var tenantOptions = new DbContextOptionsBuilder<AnimoraServerDbContext>()
            .UseNpgsql(context.Database.GetDbConnection())
            .AddInterceptors(new TenantStampingInterceptor(auth), new NoUnnamedIgnoreFiltersInterceptor(auth))
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options;
        await using var tenantContext = new AnimoraServerDbContext(tenantOptions, auth);
        await tenantContext.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct).ConfigureAwait(false);
        var provisioner = new TenantProvisioner(tenantContext, auth, passwordHasher, clock);
        try
        {
            var created = await provisioner.CreateTenantWithOwnerAsync(r.DisplayName, r.TenantCode,
                r.OwnerUserName, r.OwnerDisplayName, password, ct, r.LegalName, r.Timezone ?? Tenant.DefaultTimezone).ConfigureAwait(false);
            if (created.IsFailure || created.Value is not { } tenant)
                return Result<ProvisioningResult>.Fail(created.Error, created.Detail);
            var started = Subscription.Start(tenant.TenantId, plan, clock.UtcNow, actor, r.StartTrial, r.SeatLimitOverride);
            if (started.IsFailure || started.Value is not { } subscription)
                return Result<ProvisioningResult>.Fail(started.Error, started.Detail);
            context.Subscriptions.Add(subscription);
            var now = clock.UtcNow;
            var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public."ProvisioningLedger"
                    ("IdempotencyKey", "RequestFingerprint", "TenantId", "SubscriptionId", "ActorKind", "CreatedUtc")
                VALUES ({r.IdempotencyKey}, {fingerprint}, {tenant.TenantId}, {subscription.Id}, {(short)actor.Kind}, {now})
                ON CONFLICT ("IdempotencyKey") DO NOTHING
                """, ct).ConfigureAwait(false);
            if (inserted != 1) { context.ChangeTracker.Clear(); return Result<ProvisioningResult>.Fail(ErrorCodes.ConcurrencyConflict); }
            await scheduler.ScheduleAsync(tenant.TenantId, subscription.Id, ct).ConfigureAwait(false);
            context.Audit.Add(OperatorAudit.Create(now, actor, "tenant.provisioned", tenant.TenantId, subscription.Id));
            tenantContext.SecurityAudit.Add(SecurityAuditEntry.Create(IdGenerator.NewId(now), tenant.TenantId,
                now, "operator", "tenant.provisioned", "success", subscription.Id,
                detailJson: SecurityAuditDetail.Create(("subscriptionId", subscription.Id))));
            await tenantContext.SaveChangesAsync(ct).ConfigureAwait(false);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return Result<ProvisioningResult>.Ok(new(tenant.TenantId, tenant.TenantCode, subscription.Id,
                tenant.OwnerUserId, r.OwnerInitialPassword is null ? password : null, false));
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            context.ChangeTracker.Clear();
            return Result<ProvisioningResult>.Fail(ErrorCodes.TenantCodeTaken);
        }
    }

    public static string GenerateTemporaryPassword() => "Aa1-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
}
