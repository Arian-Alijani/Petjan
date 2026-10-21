using Animora.Domain.Primitives;

namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>
/// One vendor-authored message addressed to a clinic's users: the authoritative server-side store behind the admin
/// panel's message composer. It is vendor-global (written only through <see cref="AnimoraOperatorDbContext"/>, never
/// tenant-filtered), and mirrors the field semantics of the desktop's local <c>NotificationRow</c> so the future sync
/// mapping is a straight copy. <c>AudienceKind</c>/<c>Kind</c> carry the numeric values of the Contracts enums
/// (audience: 0 all users, 1 staff role, 2 one user; kind: 0 info, 1 success, 2 warning, 3 promo, 4 system, 5 reward).
/// </summary>
/// <remarks>
/// TODO(sync, Tracks D-E): deliver these rows to the desktop through the state-sync feed once it is wired; until then
/// this table is the vendor's source of truth and nothing is pushed to clinics.
/// </remarks>
public sealed class TenantNotification
{
    public const int TitleMaxLength = 200;
    public const int TagMaxLength = 40;
    public const int BodyMaxLength = 2000;

    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public short AudienceKind { get; set; }
    public string? AudienceValue { get; set; }
    public short Kind { get; set; }
    public string? Tag { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string CreatedByOperatorId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public static TenantNotification Create(DateTimeOffset now, string tenantId, short audienceKind, string? audienceValue,
        short kind, string? tag, string title, string body, string operatorId, DateTimeOffset? scheduledForUtc,
        DateTimeOffset? expiresAtUtc) => new()
        {
            Id = IdGenerator.NewId(now), TenantId = tenantId, AudienceKind = audienceKind, AudienceValue = audienceValue,
            Kind = kind, Tag = tag, Title = title, Body = body, CreatedByOperatorId = operatorId, CreatedAtUtc = now,
            ScheduledForUtc = scheduledForUtc?.ToUniversalTime(), ExpiresAtUtc = expiresAtUtc?.ToUniversalTime(),
        };
}
