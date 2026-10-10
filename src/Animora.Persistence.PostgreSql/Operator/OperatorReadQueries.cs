using Animora.Domain.Identity;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Licensing;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Operator;

public sealed record OperatorPage<T>(IReadOnlyList<T> Items,int Page,int Size,long Total,long Sequence=0);
public sealed record OperatorTenantDetail(Tenant Tenant,Subscription? Subscription,int Seats,int Devices,int Licenses,long GrantSequence,IReadOnlyList<SubscriptionEvent> Events);
public sealed class OperatorReadQueries(AnimoraOperatorDbContext db)
{
    public async Task<OperatorPage<Tenant>> TenantsAsync(string? status,string? search,int page,int size,CancellationToken ct)
    {
        page=Math.Clamp(page,1,int.MaxValue/200);size=Math.Clamp(size,1,200); var now=DateTimeOffset.UtcNow;
        var query=db.Tenants.AsNoTracking().Where(t=>!t.Deleted);
        if(!string.IsNullOrWhiteSpace(search)) { string term=search.Trim(); query=query.Where(t=>(t.Code!=null&&t.Code.Contains(term))||t.DisplayName.Contains(term)); }
        if(status=="expiring") query=query.Where(t=>db.Subscriptions.Any(s=>s.TenantId==t.Id&&s.CurrentPeriodEndUtc>=now&&s.CurrentPeriodEndUtc<=now.AddDays(14)));
        else if(status=="past-due") query=query.Where(t=>db.Subscriptions.Any(s=>s.TenantId==t.Id&&(s.Status==SubscriptionStatus.GracePeriod||s.Status==SubscriptionStatus.Expired)));
        else if(status=="dead-lettered") query=query.Where(t=>db.Messages.Any(m=>m.TenantId==t.Id&&m.Status==Animora.Persistence.PostgreSql.Outbox.OutboxStatus.DeadLettered));
        else if(Enum.TryParse<TenantStatus>(status,true,out var state)) query=query.Where(t=>t.Status==state);
        long total=await query.LongCountAsync(ct).ConfigureAwait(false);
        var rows=await query.OrderBy(t=>t.Code).ThenBy(t=>t.Id).Skip((page-1)*size).Take(size).ToListAsync(ct).ConfigureAwait(false);
        return new(rows,page,size,total);
    }
    public async Task<OperatorTenantDetail?> TenantAsync(string id,CancellationToken ct)
    {
        var row=await (from t in db.Tenants.AsNoTracking() where t.Id==id&&!t.Deleted
            join s in db.Subscriptions.AsNoTracking() on t.Id equals s.TenantId into subscriptions
            from s in subscriptions.DefaultIfEmpty()
            select new { Tenant=t,Subscription=s,
                Seats=db.StaffMemberships.Where(m=>m.TenantId==id&&!m.Deleted&&m.Status==MembershipStatus.Active&&m.EndedUtc==null).Select(m=>m.UserId).Distinct().Count(),
                Devices=db.Devices.Count(d=>d.TenantId==id&&!d.Deleted),Licenses=db.Licenses.Count(l=>l.TenantId==id),
                GrantSequence=db.LicenseGrants.Where(g=>g.TenantId==id).Select(g=>(long?)g.Sequence).FirstOrDefault()??0 }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if(row is null) return null;
        var events=row.Subscription is null?[]:await db.SubscriptionEvents.AsNoTracking().Where(e=>e.SubscriptionId==row.Subscription.Id)
            .OrderByDescending(e=>e.Sequence).Take(20).ToListAsync(ct).ConfigureAwait(false);
        return new(row.Tenant,row.Subscription,row.Seats,row.Devices,row.Licenses,row.GrantSequence,events);
    }
    public async Task<(long Expiring,long PastDue)> DashboardAsync(CancellationToken ct)
    {
        var now=DateTimeOffset.UtcNow;
        var row=await db.Subscriptions.AsNoTracking().GroupBy(_=>1).Select(g=>new {
            Expiring=g.LongCount(s=>s.CurrentPeriodEndUtc>=now&&s.CurrentPeriodEndUtc<=now.AddDays(14)),
            PastDue=g.LongCount(s=>s.Status==SubscriptionStatus.GracePeriod||s.Status==SubscriptionStatus.Expired) }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return (row?.Expiring??0,row?.PastDue??0);
    }
    public async Task<OperatorPage<Plan>> PlansAsync(int page,int size,CancellationToken ct)
    {
        page=Math.Clamp(page,1,int.MaxValue/200);size=Math.Clamp(size,1,200);var query=db.Plans.AsNoTracking();
        long count=await query.LongCountAsync(ct).ConfigureAwait(false);
        return new(await query.OrderBy(x=>x.Code).Skip((page-1)*size).Take(size).ToListAsync(ct).ConfigureAwait(false),page,size,count);
    }
    public async Task<OperatorPage<LicenseRecord>> LicensesAsync(string tenantId,int page,int size,CancellationToken ct)
    {
        page=Math.Clamp(page,1,int.MaxValue/200);size=Math.Clamp(size,1,200);
        var summary=await db.Tenants.AsNoTracking().Where(t=>t.Id==tenantId).Select(t=>new {
            Count=db.Licenses.LongCount(l=>l.TenantId==tenantId),Sequence=db.LicenseGrants.Where(g=>g.TenantId==tenantId).Select(g=>(long?)g.Sequence).FirstOrDefault()??0 }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var rows=await db.Licenses.AsNoTracking().Where(l=>l.TenantId==tenantId).OrderByDescending(l=>l.Id).Skip((page-1)*size).Take(size).ToListAsync(ct).ConfigureAwait(false);
        return new(rows,page,size,summary?.Count??0,summary?.Sequence??0);
    }
}
