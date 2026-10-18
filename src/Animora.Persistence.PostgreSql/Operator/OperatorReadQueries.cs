using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Time;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Licensing;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Operator;

public sealed record OperatorPage<T>(IReadOnlyList<T> Items,int Page,int Size,long Total,long Sequence=0);
public sealed record OperatorTenantDetail(Tenant Tenant,Subscription? Subscription,int Seats,int Devices,int Licenses,long GrantSequence,IReadOnlyList<SubscriptionEvent> Events);
public sealed record OperatorStaffRow(string MembershipId,string UserId,string Name,string UserName,StaffRole Role,bool Active,int Devices);
public sealed record OperatorProductRow(string Id,string Sku,string Name,string CategoryId,string CategoryName,ProductKind Kind,bool Active,bool TrackStock,
    long PriceMinor,string Currency,decimal OnHand,decimal ReorderLevel,string StockState);
public sealed record OperatorCategoryRow(string Id,string Name);
public sealed record OperatorProductFilter(string? Search,string? CategoryId,string? Stock,long? MinPrice,long? MaxPrice);
public sealed class OperatorReadQueries(AnimoraOperatorDbContext db,IClock clock)
{
    public Task<bool> SessionValidAsync(string id, string stamp, CancellationToken ct = default) => db.Accounts.AsNoTracking()
        .AnyAsync(x => x.Id == id && x.SecurityStamp == stamp && x.Status == OperatorAccountStatus.Active && !x.MustChangePassword, ct);
    public async Task<OperatorPage<Tenant>> TenantsAsync(string? status,string? search,int page,int size,CancellationToken ct)
    {
        page=Math.Clamp(page,1,int.MaxValue/200);size=Math.Clamp(size,1,200); var now=DateTimeOffset.UtcNow;
        var query=db.Tenants.AsNoTracking().Where(t=>!t.Deleted);
        // Case-insensitive match on clinic name OR code (Postgres ILIKE): the operator can type the clinic name as it appears in the UI and still find it.
        if(!string.IsNullOrWhiteSpace(search)) { string term="%"+search.Trim()+"%"; query=query.Where(t=>EF.Functions.ILike(t.DisplayName,term)||(t.Code!=null&&EF.Functions.ILike(t.Code,term))); }
        if(status=="expiring") query=query.Where(t=>db.Subscriptions.Any(s=>s.TenantId==t.Id&&s.CurrentPeriodEndUtc>=now&&s.CurrentPeriodEndUtc<=now.AddDays(14)));
        else if(status=="past-due") query=query.Where(t=>db.Subscriptions.Any(s=>s.TenantId==t.Id&&(s.Status==SubscriptionStatus.GracePeriod||s.Status==SubscriptionStatus.Expired)));
        else if(status=="dead-lettered") query=query.Where(t=>db.Messages.Any(m=>m.TenantId==t.Id&&m.Status==Animora.Persistence.PostgreSql.Outbox.OutboxStatus.DeadLettered));
        else if(Enum.TryParse<TenantStatus>(status,true,out var state)) query=query.Where(t=>t.Status==state);
        long total=await query.LongCountAsync(ct).ConfigureAwait(false);
        // Order by the clinic name first (what the operator reads), then code, then id for a stable paging key.
        var rows=await query.OrderBy(t=>t.DisplayName).ThenBy(t=>t.Code).ThenBy(t=>t.Id).Skip((page-1)*size).Take(size).ToListAsync(ct).ConfigureAwait(false);
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
    /// <summary>Stock-state buckets accepted by <see cref="ProductsAsync"/>.</summary>
    public static readonly string[] StockStates=["out","low","ok"];
    /// <summary>One clinic's staff roster (memberships joined to users through a tenant-bound context), or <see langword="null"/> when the tenant does not exist.</summary>
    public async Task<OperatorPage<OperatorStaffRow>?> StaffAsync(string tenantId,string? role,string? search,int page,int size,CancellationToken ct)
    {
        page=Math.Clamp(page,1,int.MaxValue/200);size=Math.Clamp(size,1,200);
        if(!await db.Tenants.AsNoTracking().AnyAsync(t=>t.Id==tenantId&&!t.Deleted,ct).ConfigureAwait(false)) return null;
        await using var tenant=TenantBoundContext.Create(db,tenantId,clock);
        var query=from m in tenant.StaffMemberships.AsNoTracking()
            join u in tenant.Users.AsNoTracking() on m.UserId equals u.Id
            where m.TenantId==tenantId&&u.TenantId==tenantId select new { m,u };
        if(Enum.TryParse<StaffRole>(role,true,out var wanted)&&Enum.IsDefined(wanted)) query=query.Where(x=>x.m.Role==wanted);
        if(!string.IsNullOrWhiteSpace(search)) { string term=search.Trim().ToLowerInvariant(); query=query.Where(x=>x.u.DisplayName.ToLower().Contains(term)||x.u.UserName.Contains(term)); }
        long total=await query.LongCountAsync(ct).ConfigureAwait(false);
        var rows=await query.OrderBy(x=>x.m.JoinedUtc).ThenBy(x=>x.m.Id).Skip((page-1)*size).Take(size)
            .Select(x=>new OperatorStaffRow(x.m.Id,x.u.Id,x.u.DisplayName,x.u.UserName,x.m.Role,
                x.m.Status==MembershipStatus.Active&&x.m.EndedUtc==null&&x.u.Status!=UserStatus.Disabled,
                tenant.Devices.Count(d=>d.UserId==x.u.Id))).ToListAsync(ct).ConfigureAwait(false);
        return new(rows,page,size,total);
    }
    /// <summary>
    /// One clinic's product catalogue with stock derived from the movement ledger (never stored, R-B23). Filters: name/SKU text,
    /// category, stock state (<c>out</c>/<c>low</c>/<c>ok</c>) and a sale-price range in minor units. Returns <see langword="null"/> when the tenant does not exist.
    /// </summary>
    public async Task<(OperatorPage<OperatorProductRow> Page,IReadOnlyList<OperatorCategoryRow> Categories)?> ProductsAsync(
        string tenantId,OperatorProductFilter filter,int page,int size,CancellationToken ct)
    {
        page=Math.Clamp(page,1,int.MaxValue/200);size=Math.Clamp(size,1,200);
        if(!await db.Tenants.AsNoTracking().AnyAsync(t=>t.Id==tenantId&&!t.Deleted,ct).ConfigureAwait(false)) return null;
        await using var tenant=TenantBoundContext.Create(db,tenantId,clock);
        var products=tenant.ProductSet.AsNoTracking().Where(p=>p.TenantId==tenantId);
        if(!string.IsNullOrWhiteSpace(filter.Search)) { string term=filter.Search.Trim(); products=products.Where(p=>EF.Functions.ILike(p.Name,"%"+term+"%")||EF.Functions.ILike(p.Sku,"%"+term+"%")); }
        if(!string.IsNullOrWhiteSpace(filter.CategoryId)) products=products.Where(p=>p.CategoryId==filter.CategoryId);
        if(filter.MinPrice is { } min) products=products.Where(p=>p.DefaultSalePrice.Minor>=min);
        if(filter.MaxPrice is { } max) products=products.Where(p=>p.DefaultSalePrice.Minor<=max);
        var projected=products.Select(p=>new { p.Id,p.Sku,p.Name,p.CategoryId,p.Kind,p.IsActive,p.TrackStock,
            Price=p.DefaultSalePrice.Minor,Currency=p.DefaultSalePrice.Currency,Reorder=p.ReorderLevel.Value,
            OnHand=tenant.StockMovementSet.Where(m=>m.ProductId==p.Id).Sum(m=>(decimal?)m.Quantity.Value)??0m });
        projected=filter.Stock switch
        {
            "out"=>projected.Where(x=>x.TrackStock&&x.OnHand<=0m),
            "low"=>projected.Where(x=>x.TrackStock&&x.OnHand>0m&&x.OnHand<=x.Reorder),
            "ok"=>projected.Where(x=>!x.TrackStock||x.OnHand>x.Reorder),
            _=>projected,
        };
        long total=await projected.LongCountAsync(ct).ConfigureAwait(false);
        var items=await projected.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToListAsync(ct).ConfigureAwait(false);
        var categories=await tenant.ProductCategorySet.AsNoTracking().Where(c=>c.TenantId==tenantId).OrderBy(c=>c.SortOrder).ThenBy(c=>c.DisplayName)
            .Take(500).Select(c=>new OperatorCategoryRow(c.Id,c.DisplayName)).ToListAsync(ct).ConfigureAwait(false);
        var names=categories.ToDictionary(c=>c.Id,c=>c.Name);
        var rows=items.Select(x=>new OperatorProductRow(x.Id,x.Sku,x.Name,x.CategoryId,names.GetValueOrDefault(x.CategoryId,""),x.Kind,x.IsActive,x.TrackStock,x.Price,x.Currency,
            x.OnHand,x.Reorder,!x.TrackStock?"untracked":x.OnHand<=0m?"out":x.OnHand<=x.Reorder?"low":"ok")).ToArray();
        return (new OperatorPage<OperatorProductRow>(rows,page,size,total),categories);
    }
}
