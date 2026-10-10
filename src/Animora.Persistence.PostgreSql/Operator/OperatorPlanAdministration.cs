using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Domain.Time;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>Shared plan mutations with same-transaction operator audit.</summary>
public sealed class OperatorPlanAdministration(AnimoraOperatorDbContext db,IClock clock)
{
    public async Task<Result<Plan>> CreateAsync(string code,string name,int seats,long amount,int trialDays,ActorRef actor,CancellationToken ct)
    {
        var result=Plan.Create(code,name,seats,amount,trialDays,clock.UtcNow); if(result.IsFailure)return result;
        await using var tx=await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"plan:"+code},0))",ct).ConfigureAwait(false);
        if(await db.Plans.AnyAsync(p=>p.Code==code,ct).ConfigureAwait(false))return Result<Plan>.Fail(ErrorCodes.ConcurrencyConflict);
        db.Plans.Add(result.Value!);db.Audit.Add(OperatorAudit.Create(clock.UtcNow,actor,"plan.created",null,result.Value!.Id));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return result;
    }
    public async Task<Result> DeactivateAsync(string id,string stamp,ActorRef actor,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var plan=await db.Plans.FromSqlInterpolated($"SELECT * FROM public.\"Plans\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if(plan is null)return Result.Fail(ErrorCodes.PlanNotFound);
        if(plan.CreatedUtc.ToString("O",System.Globalization.CultureInfo.InvariantCulture)!=stamp.Trim('"'))return Result.Fail(ErrorCodes.ConcurrencyConflict);
        plan.Deactivate();db.Audit.Add(OperatorAudit.Create(clock.UtcNow,actor,"plan.deactivated",null,id));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);await tx.CommitAsync(ct).ConfigureAwait(false);return Result.Ok();
    }
}
