using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Server.Licensing.Issuance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
namespace Animora.Persistence.PostgreSql.Licensing;

public sealed class LicenseGrantProjector(AnimoraOperatorDbContext db, OutboxDbContext relay,
    IClock clock, IOptions<LicensingOptions> options)
{
    /// <summary>Rereads source state and atomically fences stale/out-of-order redeliveries.</summary>
    public async Task<Result> ProjectAsync(string tenantId, CancellationToken ct = default)
    {
        var transaction = relay.Database.CurrentTransaction ?? throw new InvalidOperationException("Fenced relay transaction required.");
        db.Database.SetDbConnection(relay.Database.GetDbConnection());
        await db.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct).ConfigureAwait(false);
        var source = await (from s in db.Subscriptions.AsNoTracking()
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            join t in db.Tenants.AsNoTracking() on s.TenantId equals t.Id
            where s.TenantId == tenantId && !t.Deleted
            select new { s.Id, p.Code, s.SeatLimit, s.Status, s.CurrentPeriodEndUtc, s.GraceEndsUtc, s.NextEventSequence })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (source is null) return Result.Ok(); // Deleted tenant is acknowledged, not retried forever.
        var status = (short)LicenseGrant.MapStatus(source.Status);
        var until = source.GraceEndsUtc ?? source.CurrentPeriodEndUtc;
        var sequence = source.NextEventSequence - 1;
        var devices = checked(source.SeatLimit * options.Value.DevicesPerSeat);
        var now = clock.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."LicenseGrants" ("TenantId","SubscriptionId","PlanCode","SeatLimit","MaxDevices",
                "ValidUntilUtc","Status","Sequence","SourceEventSequence","UpdatedUtc")
            VALUES ({tenantId},{source.Id},{source.Code},{source.SeatLimit},{devices},{until},{status},1,{sequence},{now})
            ON CONFLICT ("TenantId") DO UPDATE SET
                "SubscriptionId"=EXCLUDED."SubscriptionId", "PlanCode"=EXCLUDED."PlanCode",
                "SeatLimit"=EXCLUDED."SeatLimit", "MaxDevices"=EXCLUDED."MaxDevices", "ValidUntilUtc"=EXCLUDED."ValidUntilUtc",
                "Status"=EXCLUDED."Status", "Sequence"="LicenseGrants"."Sequence"+1,
                "SourceEventSequence"=EXCLUDED."SourceEventSequence", "UpdatedUtc"=EXCLUDED."UpdatedUtc"
            WHERE "LicenseGrants"."SourceEventSequence" < EXCLUDED."SourceEventSequence"
            """, ct).ConfigureAwait(false);
        return Result.Ok();
    }
}
