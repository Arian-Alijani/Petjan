using Animora.Domain.Primitives;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// An append-only security-audit record (S04). <b>Not</b> a <see cref="SyncableEntity"/> — it never
/// replicates and carries no <c>Version</c>/<c>Deleted</c>. The database enforces append-only forever
/// via the <c>trg_SecurityAudit_immutable</c> trigger (raises on UPDATE/DELETE). <see cref="DetailJson"/>
/// NEVER contains a password, token, hash or other secret/PII body.
/// </summary>
public sealed class SecurityAuditEntry
{
    private SecurityAuditEntry()
    {
    }

    /// <summary>Audit row ULID.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Owning tenant ULID (tenant-filtered; first column of every index).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>When the audited action occurred, UTC.</summary>
    public DateTimeOffset OccurredUtc { get; private set; }

    /// <summary>Acting user ULID, when known; <see langword="null"/> for pre-auth failures.</summary>
    public string? ActorUserId { get; private set; }

    /// <summary>Device ULID involved, when known.</summary>
    public string? DeviceId { get; private set; }

    /// <summary>Coarse category, e.g. <c>auth</c>.</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>Action key, e.g. <c>auth.login.success</c> (see <see cref="SecurityAuditActions"/>).</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>Outcome, e.g. <c>success</c> / <c>failure</c>.</summary>
    public string Outcome { get; private set; } = string.Empty;

    /// <summary>Correlation id tying the row to request logs.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Optional non-secret structured detail (JSON); never a credential/token/hash.</summary>
    public string? DetailJson { get; private set; }

    /// <summary>Creates an audit row. <paramref name="detailJson"/> must contain no secret/PII body.</summary>
    public static SecurityAuditEntry Create(
        string id,
        string tenantId,
        DateTimeOffset occurredUtc,
        string category,
        string action,
        string outcome,
        string correlationId,
        string? actorUserId = null,
        string? deviceId = null,
        string? detailJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return new SecurityAuditEntry
        {
            Id = id,
            TenantId = tenantId,
            OccurredUtc = occurredUtc,
            Category = category,
            Action = action,
            Outcome = outcome,
            CorrelationId = correlationId,
            ActorUserId = actorUserId,
            DeviceId = deviceId,
            DetailJson = detailJson,
        };
    }
}

/// <summary>Canonical security-audit action keys written by the S04 auth flow.</summary>
public static class SecurityAuditActions
{
    /// <summary>Audit category for every auth-plane action.</summary>
    public const string Category = "auth";

    /// <summary>Outcome: the action succeeded.</summary>
    public const string OutcomeSuccess = "success";

    /// <summary>Outcome: the action failed.</summary>
    public const string OutcomeFailure = "failure";

    public const string LoginSuccess = "auth.login.success";
    public const string LoginFailure = "auth.login.failure";
    public const string LoginLocked = "auth.login.locked";
    public const string RefreshSuccess = "auth.refresh.success";
    public const string RefreshFailure = "auth.refresh.failure";
    public const string RefreshReuseDetected = "auth.refresh.reuse_detected";
    public const string Logout = "auth.logout";
    public const string DeviceRegistered = "auth.device.registered";
    public const string DeviceRevokedContact = "auth.device.revoked_contact";
}
