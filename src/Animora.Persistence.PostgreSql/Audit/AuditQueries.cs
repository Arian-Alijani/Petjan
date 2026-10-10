using System.Text;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Options;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Audit;
public sealed record AuditRow(string Id, DateTimeOffset OccurredUtc, string? ActorId, string Action,
    string? TenantId, string Outcome, string CorrelationId, string? Detail);
/// <summary>Vendor audit viewer. Tenant security reads always have an explicit tenant predicate.</summary>
public sealed class AuditQueries(AnimoraOperatorDbContext db, IReportingQuerySessionFactory reporting)
{
    public async Task<Result<OperatorPage<AuditRow>>> OperatorAsync(string? action, string? tenantId,
        DateTimeOffset? from, DateTimeOffset? to, int page, int size, CancellationToken ct = default)
    {
        if (action is { Length: > 64 } || tenantId is { Length: > 26 } || from > to)
            return Result<OperatorPage<AuditRow>>.Fail(ErrorCodes.ValidationFailed);
        page = Math.Clamp(page, 1, int.MaxValue / 200); size = Math.Clamp(size, 1, 200);
        // jsonb is explicitly cast to text before LEFT; never apply string substring directly to jsonb.
        var query = db.Database.SqlQueryRaw<AuditRow>("""
            SELECT "Id", "OccurredUtc", "OperatorId" AS "ActorId", "Action", "TargetTenantId" AS "TenantId",
                "Outcome", "CorrelationId", LEFT("DetailJson"::text, 2048) AS "Detail"
            FROM public."OperatorAudit"
            """).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action);
        if (!string.IsNullOrWhiteSpace(tenantId)) query = query.Where(x => x.TenantId == tenantId);
        if (from is { } start) query = query.Where(x => x.OccurredUtc >= start);
        if (to is { } end) query = query.Where(x => x.OccurredUtc < end);
        long total;
        if (string.IsNullOrWhiteSpace(action) && string.IsNullOrWhiteSpace(tenantId) && from is null && to is null)
        {
            await using var lease = await reporting.OpenAsync(ct).ConfigureAwait(false);
            var count = await lease.ReadAsync("SELECT count(*) FROM public.\"OperatorAudit\"", r => r.GetInt64(0), ct: ct).ConfigureAwait(false);
            total = count[0];
        }
        else total = await query.LongCountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(x => x.OccurredUtc).ThenByDescending(x => x.Id).Skip((page - 1) * size).Take(size)
            .ToListAsync(ct).ConfigureAwait(false);
        return Result<OperatorPage<AuditRow>>.Ok(new(rows.Select(x => x with { Detail = TruncateDetail(x.Detail) }).ToArray(), page, size, total));
    }
    public async Task<Result<OperatorPage<AuditRow>>> TenantAsync(string tenantId, DateTimeOffset from, DateTimeOffset to,
        int page, int size, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26 || to <= from || to - from > TimeSpan.FromDays(90))
            return Result<OperatorPage<AuditRow>>.Fail(ErrorCodes.ValidationFailed);
        page = Math.Clamp(page, 1, int.MaxValue / 200); size = Math.Clamp(size, 1, 200);
        var query = db.Database.SqlQueryRaw<AuditRow>("""
            SELECT "Id", "OccurredUtc", "ActorUserId" AS "ActorId", "Action", "TenantId", "Outcome", "CorrelationId",
                LEFT("DetailJson"::text, 2048) AS "Detail" FROM public."SecurityAudit"
            """).AsNoTracking().Where(x => x.TenantId == tenantId && x.OccurredUtc >= from && x.OccurredUtc < to);
        long total = await query.LongCountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(x => x.OccurredUtc).ThenByDescending(x => x.Id).Skip((page - 1) * size).Take(size)
            .ToListAsync(ct).ConfigureAwait(false);
        return Result<OperatorPage<AuditRow>>.Ok(new(rows.Select(x => x with { Detail = TruncateDetail(x.Detail) }).ToArray(), page, size, total));
    }
    public static string? TruncateDetail(string? detail)
    {
        if (detail is null || Encoding.UTF8.GetByteCount(detail) <= 2048) return detail;
        var output = new StringBuilder(); int bytes = 0;
        foreach (var rune in detail.EnumerateRunes())
        { if (bytes + rune.Utf8SequenceLength > 2045) break; output.Append(rune.ToString()); bytes += rune.Utf8SequenceLength; }
        return output.Append("…").ToString();
    }
}
