using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
namespace Animora.Server.Admin;

/// <summary>
/// Vendor message authoring for the operator console. The message is stored server-side (authoritative) by
/// <see cref="OperatorNotificationAdministration"/>; the actor is always the cookie-authenticated operator.
/// TODO(sync, Tracks D–E): deliver to the desktop via the state-sync feed once it is wired.
/// </summary>
internal sealed class AdminNotificationAdapter(OperatorNotificationAdministration notifications, AdminSession session,
    IHttpContextAccessor accessor) : IAdminNotificationCommands
{
    public async Task<Result<MutationReceipt>> SendAsync(string tenantId, SendNotificationForm form, CancellationToken ct = default)
    {
        if (!form.Confirmed) return Result<MutationReceipt>.Fail(ErrorCodes.ValidationFailed);
        var correlation = accessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        static DateTimeOffset? Utc(DateTime? value) => value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
        var draft = new OperatorNotificationDraft(form.AudienceKind,
            form.AudienceKind switch { 1 => form.RoleValue, 2 => form.UserValue, _ => null }, form.Kind, form.Tag, form.Title ?? string.Empty,
            form.Body ?? string.Empty, Utc(form.ScheduledForUtc), Utc(form.ExpiresAtUtc));
        var result = await notifications.SendAsync(tenantId, draft, session.Actor, correlation, ct).ConfigureAwait(false);
        return result.Map(_ => new MutationReceipt(null, tenantId));
    }
}
