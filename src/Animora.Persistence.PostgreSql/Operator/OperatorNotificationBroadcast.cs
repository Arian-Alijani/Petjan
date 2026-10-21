using Animora.Domain.Results;
using Animora.Domain.Identity;
using Animora.Domain.Subscriptions;
namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>Who a broadcast targets: every clinic, the clinics matching a status/search category, or an explicit clinic-id list.</summary>
public sealed record OperatorBroadcastScope(int Kind, string? Status, string? Search, IReadOnlyList<string>? TenantIds);

/// <summary>Outcome of a broadcast run. <c>Errors</c> holds at most <see cref="OperatorNotificationBroadcast.MaxReportedErrors"/> <c>code:ERROR</c> entries.</summary>
public sealed record OperatorBroadcastResult(int Sent, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// Fans one validated <see cref="OperatorNotificationDraft"/> out to many clinics. Every clinic goes through the existing single-tenant
/// <see cref="OperatorNotificationAdministration.SendAsync"/> (own transaction + own audit row), so there is no giant transaction and one
/// clinic's failure never aborts the run — it is collected and reported instead.
/// </summary>
public sealed class OperatorNotificationBroadcast(OperatorReadQueries reads, OperatorNotificationAdministration notifications, AnimoraOperatorDbContext db)
{
    /// <summary>Hard cap on clinics reached by one broadcast (guards against an accidental run against a huge directory).</summary>
    public const int MaxTargets = 1000;
    public const int MaxReportedErrors = 20;
    private const int PageSize = 200;

    public async Task<Result<OperatorBroadcastResult>> SendAsync(OperatorBroadcastScope scope, OperatorNotificationDraft draft,
        ActorRef actor, string correlationId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(draft);
        // Per-user targeting has no meaning across clinics; only "all users" (0) and "a role" (1) are broadcastable.
        if (scope.Kind is < 0 or > 2 || draft.AudienceKind is < 0 or > 1) return Result<OperatorBroadcastResult>.Fail(ErrorCodes.ValidationFailed);

        var targets = await ResolveAsync(scope, ct).ConfigureAwait(false);
        if (targets.Count == 0) return Result<OperatorBroadcastResult>.Ok(new(0, 0, []));

        int sent = 0, skipped = 0, attempted = 0; var errors = new List<string>();
        foreach (var (id, label, closed) in targets)
        {
            ct.ThrowIfCancellationRequested();
            if (closed) { skipped++; continue; }
            Result<string> result; attempted++;
            try { result = await notifications.SendAsync(id, draft, actor, correlationId, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // A failed SaveChanges leaves the new row tracked; drop it so the next clinic's save is not poisoned.
                db.ChangeTracker.Clear();
                result = Result<string>.Fail(ErrorCodes.ValidationFailed, "send_failed");
            }
            if (result.IsSuccess) { sent++; continue; }
            // The draft is identical for every clinic: if it is invalid, the very first send says so — stop instead of N identical failures.
            if (attempted == 1 && result.Error == ErrorCodes.ValidationFailed && scope.Kind != 2)
                return Result<OperatorBroadcastResult>.Fail(ErrorCodes.ValidationFailed);
            skipped++;
            if (errors.Count < MaxReportedErrors) errors.Add($"{label}:{result.Error.Code}");
        }
        return Result<OperatorBroadcastResult>.Ok(new(sent, skipped, errors));
    }

    private async Task<List<(string Id, string Label, bool Closed)>> ResolveAsync(OperatorBroadcastScope scope, CancellationToken ct)
    {
        var list = new List<(string, string, bool)>();
        if (scope.Kind == 2)
        {
            // Explicit set: ids are re-validated by SendAsync (unknown / deleted ids come back as NotFound and are reported).
            foreach (var id in (scope.TenantIds ?? []).Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).Take(MaxTargets))
                list.Add((id!, id!, false));
            return list;
        }
        // "All" and "category" both page through the directory; "all" additionally skips closed clinics.
        var status = scope.Kind == 1 ? scope.Status : null;
        var search = scope.Kind == 1 ? scope.Search : null;
        for (var page = 1; list.Count < MaxTargets; page++)
        {
            var rows = await reads.TenantsAsync(status, search, page, PageSize, ct, "name").ConfigureAwait(false);
            foreach (var r in rows.Items)
            {
                if (list.Count >= MaxTargets) break;
                list.Add((r.Tenant.Id, string.IsNullOrEmpty(r.Tenant.Code) ? r.Tenant.Id : r.Tenant.Code!, scope.Kind == 0 && r.Tenant.Status == TenantStatus.Closed));
            }
            if (rows.Items.Count < PageSize || (long)page * PageSize >= rows.Total) break;
        }
        return list;
    }
}
