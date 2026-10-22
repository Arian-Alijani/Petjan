using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>A validated vendor message to one clinic's users (numeric enums mirror the Contracts notification enums).</summary>
public sealed record OperatorNotificationDraft(int AudienceKind, string? AudienceValue, int Kind, string? Tag, string Title,
    string Body, DateTimeOffset? ScheduledForUtc, DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// Vendor-side message authoring: validates the draft, checks the audience really exists in the clinic and stores the
/// <see cref="TenantNotification"/> with its operator-audit row in ONE transaction (no message without its audit trail).
/// </summary>
public sealed class OperatorNotificationAdministration(AnimoraOperatorDbContext db, IClock clock)
{
    public async Task<Result<string>> SendAsync(string tenantId, OperatorNotificationDraft draft, ActorRef actor,
        string correlationId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var title = (draft.Title ?? string.Empty).Trim();
        var body = (draft.Body ?? string.Empty).Trim();
        var tag = string.IsNullOrWhiteSpace(draft.Tag) ? null : draft.Tag.Trim();
        var now = clock.UtcNow;
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26
            || title.Length is 0 or > TenantNotification.TitleMaxLength
            || body.Length is 0 or > TenantNotification.BodyMaxLength
            || (tag is not null && tag.Length > TenantNotification.TagMaxLength)
            || draft.Kind is < 0 or > 5 || draft.AudienceKind is < 0 or > 2
            || (draft.ScheduledForUtc is { } at && at > now.AddYears(2))
            || (draft.ExpiresAtUtc is { } expires && expires <= (draft.ScheduledForUtc is { } s && s > now ? s : now)))
            return Result<string>.Fail(ErrorCodes.ValidationFailed);

        if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && !t.Deleted, ct).ConfigureAwait(false))
            return Result<string>.Fail(ErrorCodes.NotFound);

        string? audienceValue = null;
        switch (draft.AudienceKind)
        {
            case 1:
                if (!Enum.TryParse<StaffRole>(draft.AudienceValue, out var role) || !Enum.IsDefined(role))
                    return Result<string>.Fail(ErrorCodes.ValidationFailed);
                audienceValue = role.ToString();
                break;
            case 2:
                audienceValue = draft.AudienceValue?.Trim();
                if (string.IsNullOrEmpty(audienceValue) || audienceValue.Length > 26)
                    return Result<string>.Fail(ErrorCodes.ValidationFailed);
                if (!await db.StaffMemberships.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.UserId == audienceValue
                        && !m.Deleted && m.Status == MembershipStatus.Active && m.EndedUtc == null, ct).ConfigureAwait(false))
                    return Result<string>.Fail(ErrorCodes.NotFound);
                break;
        }

        var row = TenantNotification.Create(now, tenantId, (short)draft.AudienceKind, audienceValue, (short)draft.Kind, tag,
            title, body, actor.ActorId ?? string.Empty, draft.ScheduledForUtc, draft.ExpiresAtUtc);
        // The operator DbContext is scoped and, inside a Blazor Server circuit, reused across admin POSTs. Without a
        // post-commit reset the Notification + Audit entities added here stay Tracked; the NEXT send re-enters with
        // stale tracked state and SaveChangesAsync throws an unhandled 500 ("Request failed / Correlation"). Clearing
        // the tracker in finally (success OR failure) makes every send a clean unit of work — mirrors ServerTransactionRunner.
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            db.TenantNotifications.Add(row);
            // Audit detail is allow-listed metadata only: never the message text.
            db.Audit.Add(OperatorAudit.Create(now, actor, "notification.sent", tenantId, correlationId,
                detail: new { notificationId = row.Id, audience = draft.AudienceKind, kind = draft.Kind }));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return Result<string>.Ok(row.Id);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
