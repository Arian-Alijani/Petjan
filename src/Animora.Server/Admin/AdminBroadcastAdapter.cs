using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

/// <summary>
/// Bridges the operator console's broadcast composer to <see cref="OperatorNotificationBroadcast"/>. The actor is always the cookie-authenticated operator;
/// delivery to the desktop is pull-based (GET /notifications) exactly like the single-clinic message.
/// </summary>
internal sealed class AdminBroadcastAdapter(OperatorNotificationBroadcast broadcast, AdminSession session, IHttpContextAccessor accessor) : IAdminBroadcastCommands
{
    public async Task<Result<BroadcastReceipt>> BroadcastAsync(BroadcastNotificationForm form, CancellationToken ct = default)
    {
        _ = session.OperatorId;
        if (!form.Confirmed || form.Search?.Length > 200 || form.Status?.Length > 40
            || (form.ScopeKind == 2 && (form.TenantIds is null || form.TenantIds.Length == 0 || form.TenantIds.Length > OperatorNotificationBroadcast.MaxTargets)))
            return Result<BroadcastReceipt>.Fail(ErrorCodes.ValidationFailed);
        var correlation = accessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        static DateTimeOffset? Utc(DateTime? value) => value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
        var draft = new OperatorNotificationDraft(form.AudienceKind, form.AudienceKind == 1 ? form.RoleValue : null, form.Kind, form.Tag,
            form.Title ?? string.Empty, form.Body ?? string.Empty, Utc(form.ScheduledForUtc), Utc(form.ExpiresAtUtc));
        var scope = new OperatorBroadcastScope(form.ScopeKind, string.IsNullOrWhiteSpace(form.Status) ? null : form.Status,
            string.IsNullOrWhiteSpace(form.Search) ? null : form.Search, form.TenantIds);
        var result = await broadcast.SendAsync(scope, draft, session.Actor, correlation, ct).ConfigureAwait(false);
        return result.Map(r => new BroadcastReceipt(r.Sent, r.Skipped, r.Errors));
    }
}
